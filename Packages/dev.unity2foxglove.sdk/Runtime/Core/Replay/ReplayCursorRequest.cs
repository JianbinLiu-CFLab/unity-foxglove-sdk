// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Unity-free value object for external replay cursor requests.

using System;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// External replay cursor metadata received from an optional Foxglove
    /// extension. The timestamp is intentionally transported as split
    /// seconds/nanoseconds so JavaScript never has to represent Unix
    /// nanoseconds in a lossy number.
    /// </summary>
    public readonly struct ReplayCursorRequest
    {
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;

        /// <summary>Human-readable source of the cursor signal.</summary>
        public string Source { get; }

        /// <summary>Monotonic extension-side sequence number, if supplied.</summary>
        public long Sequence { get; }

        /// <summary>Requested replay cursor timestamp in Unix nanoseconds.</summary>
        public ulong TimeNs { get; }

        /// <summary>Cursor seconds field exactly as received.</summary>
        public long Sec { get; }

        /// <summary>Cursor nanoseconds field exactly as received.</summary>
        public int Nsec { get; }

        /// <summary>Cursor operation mode. Phase139D accepts seek metadata.</summary>
        public string Mode { get; }

        private ReplayCursorRequest(string source, long sequence, long sec, int nsec, ulong timeNs, string mode)
        {
            Source = source ?? string.Empty;
            Sequence = sequence;
            Sec = sec;
            Nsec = nsec;
            TimeNs = timeNs;
            Mode = string.IsNullOrWhiteSpace(mode) ? "seek" : mode;
        }

        /// <summary>Create a request for runtime tests without JSON parsing.</summary>
        public static ReplayCursorRequest CreateForTests(ulong timeNs, string source, long sequence)
        {
            var sec = (long)(timeNs / NanosecondsPerSecond);
            var nsec = (int)(timeNs % NanosecondsPerSecond);
            return new ReplayCursorRequest(source, sequence, sec, nsec, timeNs, "seek");
        }

        /// <summary>
        /// Parse the explicit Phase139D JSON payload.
        /// </summary>
        /// <param name="json">JSON body containing source, sequence, mode, and time.sec/time.nsec.</param>
        /// <param name="request">Parsed cursor request when successful.</param>
        /// <param name="error">Human-readable failure reason when parsing fails.</param>
        /// <returns>True when the payload is valid.</returns>
        public static bool TryParseJson(string json, out ReplayCursorRequest request, out string error)
        {
            request = default;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Cursor request body is empty.";
                return false;
            }

            try
            {
                var root = JObject.Parse(json);
                if (root["timeNs"] != null)
                {
                    error = "Use split time.sec/time.nsec fields; timeNs number payloads are rejected.";
                    return false;
                }

                var time = root["time"] as JObject;
                if (time == null)
                {
                    error = "Cursor request is missing time object.";
                    return false;
                }

                if (!TryReadInt64(time["sec"], out var sec) || sec < 0)
                {
                    error = "Cursor time.sec must be a non-negative integer.";
                    return false;
                }

                if (!TryReadInt64(time["nsec"], out var nsecLong) || nsecLong < 0 || nsecLong >= (long)NanosecondsPerSecond)
                {
                    error = "Cursor time.nsec must be an integer in [0, 1000000000).";
                    return false;
                }

                var nsec = (int)nsecLong;
                var secAsUlong = (ulong)sec;
                if (secAsUlong > (ulong.MaxValue - (ulong)nsec) / NanosecondsPerSecond)
                {
                    error = "Cursor timestamp overflows UInt64 nanoseconds.";
                    return false;
                }

                var source = (string)root["source"] ?? string.Empty;
                var mode = (string)root["mode"] ?? "seek";
                TryReadInt64(root["sequence"], out var sequence);
                request = new ReplayCursorRequest(
                    source,
                    sequence,
                    sec,
                    nsec,
                    secAsUlong * NanosecondsPerSecond + (ulong)nsec,
                    mode);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cursor request JSON is invalid: " + ex.Message;
                return false;
            }
        }

        private static bool TryReadInt64(JToken token, out long value)
        {
            value = 0;
            if (token == null)
            {
                return false;
            }

            try
            {
                value = token.Value<long>();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Return a copy with a clamped nanosecond timestamp.</summary>
        internal ReplayCursorRequest WithTimeNs(ulong timeNs)
        {
            var sec = (long)(timeNs / NanosecondsPerSecond);
            var nsec = (int)(timeNs % NanosecondsPerSecond);
            return new ReplayCursorRequest(Source, Sequence, sec, nsec, timeNs, Mode);
        }
    }
}
