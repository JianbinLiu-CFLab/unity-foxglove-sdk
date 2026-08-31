// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Unity-free value object for external replay cursor requests.

using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Revocable authority held by one loopback endpoint worker generation.
    /// Queue consumers check it while committing a request so a callback that
    /// started before endpoint replacement cannot publish after revocation.
    /// </summary>
    internal sealed class ReplayCursorGenerationLease
    {
        private readonly object _gate = new object();
        private bool _active = true;

        public bool IsActive
        {
            get
            {
                lock (_gate)
                {
                    return _active;
                }
            }
        }

        public void Revoke()
        {
            lock (_gate)
            {
                _active = false;
            }
        }
    }

    /// <summary>
    /// Read-only replay cursor state exposed to the optional Foxglove extension.
    /// It mirrors Unity's replay clock without granting the HTTP endpoint direct
    /// access to Unity objects or replay mutation.
    /// </summary>
    public readonly struct ReplayCursorState
    {
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;
        private const byte PlaybackStatusPlaying = 0;
        private const byte PlaybackStatusEnded = 3;

        /// <summary>State returned when replay state is not available.</summary>
        public static ReplayCursorState Unavailable(string message)
            => new ReplayCursorState(false, false, false, false, false, 0UL, 0UL, 0UL, 1f, message);

        /// <summary>Whether Unity can provide a replay cursor right now.</summary>
        public bool Available { get; }

        /// <summary>Whether MCAP replay is enabled.</summary>
        public bool ReplayEnabled { get; }

        /// <summary>Whether playback-control range mode is enabled.</summary>
        public bool PlaybackEnabled { get; }

        /// <summary>Whether Unity's replay clock is currently advancing.</summary>
        public bool Playing { get; }

        /// <summary>Whether Unity's replay clock reached the end of the range.</summary>
        public bool Ended { get; }

        /// <summary>Current replay cursor in Unix nanoseconds.</summary>
        public ulong TimeNs { get; }

        /// <summary>Start of the replay range in Unix nanoseconds.</summary>
        public ulong StartNs { get; }

        /// <summary>End of the replay range in Unix nanoseconds.</summary>
        public ulong EndNs { get; }

        /// <summary>Current replay speed multiplier.</summary>
        public float Speed { get; }

        /// <summary>Human-readable status for diagnostics.</summary>
        public string Message { get; }

        private ReplayCursorState(
            bool available,
            bool replayEnabled,
            bool playbackEnabled,
            bool playing,
            bool ended,
            ulong timeNs,
            ulong startNs,
            ulong endNs,
            float speed,
            string message)
        {
            Available = available;
            ReplayEnabled = replayEnabled;
            PlaybackEnabled = playbackEnabled;
            Playing = playing;
            Ended = ended;
            TimeNs = timeNs;
            StartNs = startNs;
            EndNs = endNs;
            Speed = speed;
            Message = message ?? string.Empty;
        }

        /// <summary>Create a cursor state from the runtime playback clock snapshot.</summary>
        public static ReplayCursorState FromPlayback(
            bool replayEnabled,
            bool playbackEnabled,
            PlaybackClock.PlaybackStateSnapshot snapshot,
            ulong startNs,
            ulong endNs)
        {
            var validRange = endNs >= startNs;
            var available = replayEnabled && playbackEnabled && validRange;
            return new ReplayCursorState(
                available,
                replayEnabled,
                playbackEnabled,
                snapshot.Status == PlaybackStatusPlaying,
                snapshot.Status == PlaybackStatusEnded,
                snapshot.CurrentTimeNs,
                startNs,
                endNs,
                snapshot.Speed,
                CreateAvailabilityMessage(available, replayEnabled, playbackEnabled, validRange));
        }

        private static string CreateAvailabilityMessage(
            bool available,
            bool replayEnabled,
            bool playbackEnabled,
            bool validRange)
        {
            if (available)
            {
                return "Replay cursor state available.";
            }

            if (!replayEnabled)
            {
                return "Replay is not loaded; external cursor control is unavailable.";
            }

            if (!playbackEnabled)
            {
                return "Playback control is not enabled; external cursor control is unavailable.";
            }

            if (!validRange)
            {
                return "Replay playback range is invalid; external cursor control is unavailable.";
            }

            return "Replay cursor state is unavailable.";
        }

        /// <summary>Serialize this state as the loopback endpoint JSON contract.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            AppendBool(builder, "available", Available).Append(',');
            AppendBool(builder, "replayEnabled", ReplayEnabled).Append(',');
            AppendBool(builder, "playbackEnabled", PlaybackEnabled).Append(',');
            AppendBool(builder, "playing", Playing).Append(',');
            AppendBool(builder, "ended", Ended).Append(',');
            builder.Append("\"speed\":").Append(Speed.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            AppendTime(builder, "time", TimeNs).Append(',');
            AppendTime(builder, "startTime", StartNs).Append(',');
            AppendTime(builder, "endTime", EndNs).Append(',');
            builder.Append("\"message\":").Append(JsonEscape(Message));
            builder.Append('}');
            return builder.ToString();
        }

        private static StringBuilder AppendBool(StringBuilder builder, string name, bool value)
            => builder.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");

        private static StringBuilder AppendTime(StringBuilder builder, string name, ulong timeNs)
        {
            var sec = timeNs / NanosecondsPerSecond;
            var nsec = timeNs % NanosecondsPerSecond;
            return builder
                .Append('"').Append(name).Append("\":{\"sec\":")
                .Append(sec)
                .Append(",\"nsec\":")
                .Append(nsec)
                .Append('}');
        }

        private static string JsonEscape(string value)
            => JsonConvert.ToString(value ?? string.Empty);
    }

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

        /// <summary>Whether the cursor represents an explicit Foxglove seek/scrub operation.</summary>
        public bool DidSeek { get; }

        internal ReplayCursorGenerationLease GenerationLease { get; }

        private ReplayCursorRequest(
            string source,
            long sequence,
            long sec,
            int nsec,
            ulong timeNs,
            string mode,
            bool didSeek,
            ReplayCursorGenerationLease generationLease = null)
        {
            Source = source ?? string.Empty;
            Sequence = sequence;
            Sec = sec;
            Nsec = nsec;
            TimeNs = timeNs;
            Mode = string.IsNullOrWhiteSpace(mode) ? "seek" : mode;
            DidSeek = didSeek;
            GenerationLease = generationLease;
        }

        /// <summary>Create a request for runtime tests without JSON parsing.</summary>
        public static ReplayCursorRequest CreateForTests(ulong timeNs, string source, long sequence, bool didSeek = true)
        {
            var sec = (long)(timeNs / NanosecondsPerSecond);
            var nsec = (int)(timeNs % NanosecondsPerSecond);
            return new ReplayCursorRequest(source, sequence, sec, nsec, timeNs, didSeek ? "seek" : "advance", didSeek);
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
                var didSeek = TryReadBool(root["didSeek"], out var parsedDidSeek)
                    ? parsedDidSeek
                    : !string.Equals(mode, "advance", StringComparison.OrdinalIgnoreCase);
                TryReadInt64(root["sequence"], out var sequence);
                request = new ReplayCursorRequest(
                    source,
                    sequence,
                    sec,
                    nsec,
                    secAsUlong * NanosecondsPerSecond + (ulong)nsec,
                    mode,
                    didSeek);
                return true;
            }
            catch (Exception ex)
            {
                error = "Cursor request JSON is invalid: " + ex.Message;
                return false;
            }
        }

        /// <summary>Read a JSON integer token without throwing parser exceptions to callers.</summary>
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

        /// <summary>Read a JSON boolean token without throwing parser exceptions to callers.</summary>
        private static bool TryReadBool(JToken token, out bool value)
        {
            value = false;
            if (token == null)
            {
                return false;
            }

            try
            {
                value = token.Value<bool>();
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
            return new ReplayCursorRequest(Source, Sequence, sec, nsec, timeNs, Mode, DidSeek, GenerationLease);
        }

        /// <summary>Attach endpoint-generation authority without changing the wire payload.</summary>
        internal ReplayCursorRequest WithGenerationLease(ReplayCursorGenerationLease generationLease)
            => new ReplayCursorRequest(Source, Sequence, Sec, Nsec, TimeNs, Mode, DidSeek, generationLease);
    }
}
