// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Parses Remote MCAP HTTP time and byte-range request parameters.

using System;
using System.Globalization;
using System.Net;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed partial class RemoteMcapHttpRouter
    {
        private const int MaxIsoFractionDigits = 9;
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;

        private static bool TryApplyTimeRange(HttpListenerContext context, RemoteMcapRequest request, out string problem)
        {
            problem = string.Empty;
            var startTime = context.Request.QueryString["startTime"];
            var endTime = context.Request.QueryString["endTime"];

            if (!string.IsNullOrEmpty(startTime) && !TryParseIsoUtcNs(startTime, out request.StartTimeNs))
            {
                problem = "Invalid startTime. Use an ISO 8601 UTC timestamp such as 2026-06-05T12:00:00Z.";
                return false;
            }

            if (!string.IsNullOrEmpty(endTime) && !TryParseIsoUtcNs(endTime, out request.EndTimeNs))
            {
                problem = "Invalid endTime. Use an ISO 8601 UTC timestamp such as 2026-06-05T12:00:00Z.";
                return false;
            }

            if (request.StartTimeNs > request.EndTimeNs)
            {
                problem = "startTime must be less than or equal to endTime.";
                return false;
            }

            return true;
        }

        private static bool TryParseIsoUtcNs(string value, out ulong nanoseconds)
        {
            nanoseconds = 0;
            if (string.IsNullOrEmpty(value) || !value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return false;

            var withoutZone = value.AsSpan(0, value.Length - 1);
            var dot = withoutZone.IndexOf('.');
            var secondsPart = dot >= 0 ? withoutZone.Slice(0, dot) : withoutZone;
            var fractionPart = dot >= 0 ? withoutZone.Slice(dot + 1) : ReadOnlySpan<char>.Empty;
            if (fractionPart.Length > MaxIsoFractionDigits)
                return false;
            for (var i = 0; i < fractionPart.Length; i++)
                if (!char.IsDigit(fractionPart[i]))
                    return false;

            if (!DateTimeOffset.TryParseExact(
                    secondsPart,
                    "yyyy-MM-dd'T'HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return false;
            }

            var unixSeconds = parsed.ToUnixTimeSeconds();
            if (unixSeconds < 0)
                return false;

            try
            {
                var fractionalNanoseconds = 0UL;
                for (var i = 0; i < fractionPart.Length; i++)
                {
                    fractionalNanoseconds = fractionalNanoseconds * 10UL + (ulong)(fractionPart[i] - '0');
                }
                for (var i = fractionPart.Length; i < MaxIsoFractionDigits; i++)
                    fractionalNanoseconds *= 10UL;

                nanoseconds = checked((ulong)unixSeconds * NanosecondsPerSecond + fractionalNanoseconds);
                return true;
            }
            catch (OverflowException)
            {
                nanoseconds = 0;
                return false;
            }
        }

        private static bool TryParseByteRange(
            string header,
            long totalLength,
            out long start,
            out long end,
            out string problem)
        {
            start = -1;
            end = -1;
            problem = string.Empty;

            if (string.IsNullOrEmpty(header))
                return true;
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                problem = "Only bytes ranges are supported.";
                return false;
            }

            var spec = header.Substring("bytes=".Length).Trim();
            if (spec.IndexOf(',') >= 0)
            {
                problem = "Only single byte ranges are supported.";
                return false;
            }

            var dash = spec.IndexOf('-');
            if (dash < 0)
            {
                problem = "Invalid Range header.";
                return false;
            }

            var startPart = spec.Substring(0, dash).Trim();
            var endPart = spec.Substring(dash + 1).Trim();
            if (startPart.Length == 0)
            {
                if (!long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out var suffixLength)
                    || suffixLength <= 0)
                {
                    problem = "Invalid suffix byte range.";
                    return false;
                }

                if (totalLength <= 0)
                {
                    problem = "Requested range is outside the file.";
                    return false;
                }

                start = Math.Max(0, totalLength - suffixLength);
                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(startPart, NumberStyles.None, CultureInfo.InvariantCulture, out start)
                || start < 0)
            {
                problem = "Invalid range start.";
                return false;
            }

            if (endPart.Length == 0)
            {
                end = totalLength - 1;
            }
            else if (!long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
            {
                problem = "Invalid range end.";
                return false;
            }

            if (start >= totalLength || end < start)
            {
                problem = "Requested range is outside the file.";
                return false;
            }

            end = Math.Min(end, totalLength - 1);
            return true;
        }
    }
}
