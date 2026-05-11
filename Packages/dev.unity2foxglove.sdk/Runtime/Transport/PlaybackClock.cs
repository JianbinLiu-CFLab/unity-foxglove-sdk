// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: PlaybackClock with play/pause/seek/speed control for MCAP replay, implementing IFoxgloveClock.

using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// IFoxgloveClock with playback control support (play/pause/seek/speed).
    /// Live mode by default. EnableRange() activates playback control.
    /// </summary>
    public class PlaybackClock : IFoxgloveClock
    {
        /// <summary>Playback lifecycle states.</summary>
        private enum PlaybackStatus : byte
        {
            /// <summary>Clock is advancing with wall time.</summary>
            Playing = 0,
            /// <summary>Clock is frozen at the current position.</summary>
            Paused = 1,
            /// <summary>Reserved for future buffering support.</summary>
            Buffering = 2,
            /// <summary>Playback has reached the end of the range.</summary>
            Ended = 3
        }

        /// <summary>Fallback system clock used when playback is disabled.</summary>
        private readonly IFoxgloveClock _inner;
        /// <summary>Whether playback range mode is enabled (false = live clock).</summary>
        private bool _enabled;
        /// <summary>Playback range start (nanoseconds).</summary>
        private ulong _startNs;
        /// <summary>Playback range end (nanoseconds).</summary>
        private ulong _endNs;
        /// <summary>Current playback state (Playing / Paused / Ended).</summary>
        private PlaybackStatus _playbackStatus = PlaybackStatus.Paused;
        /// <summary>Current simulated time in nanoseconds.</summary>
        private ulong _currentTimeNs;
        /// <summary>Playback speed multiplier (1.0 = real-time).</summary>
        private float _speed = 1f;
        /// <summary>Wall-clock timestamp of the last Tick call, used for elapsed-time calculation.</summary>
        private DateTime? _lastTickWallTime;

        /// <summary>Create a playback clock backed by the given inner clock (defaults to SystemClock).</summary>
        public PlaybackClock(IFoxgloveClock inner = null)
        {
            _inner = inner ?? new SystemClock();
        }

        /// <summary>Whether playback range mode is active (false = live wall clock).</summary>
        public bool PlaybackEnabled => _enabled;
        /// <summary>The beginning of the playback range in nanoseconds.</summary>
        public ulong StartNs => _startNs;
        /// <summary>The end of the playback range in nanoseconds.</summary>
        public ulong EndNs => _endNs;

        /// <summary>
        /// Current time in nanoseconds. This property is a pure read.
        /// When playback is disabled, delegates to the inner system clock.
        /// When playback is enabled, returns the current frozen or ticked position.
        /// </summary>
        public ulong NowNs
        {
            get
            {
                if (!_enabled) return _inner.NowNs;
                return _currentTimeNs;
            }
        }

        /// <summary>Advance playback time using the current UTC wall clock.</summary>
        public void Tick() => Tick(DateTime.UtcNow);

        /// <summary>Advance playback time using a deterministic UTC wall-clock value.</summary>
        internal void Tick(DateTime nowUtc)
        {
            if (!_enabled)
                return;

            if (_playbackStatus != PlaybackStatus.Playing)
            {
                _lastTickWallTime = nowUtc;
                return;
            }

            if (_lastTickWallTime.HasValue)
            {
                var elapsedTicks = (nowUtc - _lastTickWallTime.Value).Ticks;
                if (elapsedTicks > 0)
                {
                    var advanceNs = elapsedTicks * 100d * (double)_speed;
                    if (advanceNs > 0)
                    {
                        var remainingNs = _endNs > _currentTimeNs ? _endNs - _currentTimeNs : 0UL;
                        if (advanceNs >= remainingNs)
                        {
                            _currentTimeNs = _endNs;
                            _playbackStatus = PlaybackStatus.Ended;
                        }
                        else
                        {
                            _currentTimeNs += (ulong)advanceNs;
                        }
                    }
                }
            }

            _lastTickWallTime = nowUtc;
        }

        /// <summary>Activate playback mode and set the time range. Clock starts paused at <c>startNs</c>.</summary>
        public void EnableRange(ulong startNs, ulong endNs)
        {
            _enabled = true;
            _startNs = startNs;
            _endNs = endNs;
            _currentTimeNs = startNs;
            _playbackStatus = PlaybackStatus.Paused;
            _speed = 1f;
            _lastTickWallTime = null;
        }

        /// <summary>
        /// Apply a playback control command (play/pause), optional speed, and optional seek.
        /// No-op when playback is disabled.
        /// </summary>
        public void Apply(byte command, float speed, bool hasSeek, ulong seekTimeNs)
        {
            if (!_enabled) return;

            _speed = speed > 0 ? speed : 1f;

            switch (command)
            {
                case 0: // Play
                    Play();
                    break;
                case 1: // Pause
                    Pause();
                    break;
            }

            if (hasSeek)
            {
                _currentTimeNs = Math.Clamp(seekTimeNs, _startNs, _endNs);
                _lastTickWallTime = null;
                // Keep current status or switch to Paused if not Playing
                if (_playbackStatus != PlaybackStatus.Playing)
                    _playbackStatus = PlaybackStatus.Paused;
            }
        }

        /// <summary>Start or resume playback without changing the current speed.</summary>
        public void Play()
        {
            if (!_enabled) return;
            _playbackStatus = PlaybackStatus.Playing;
            _lastTickWallTime ??= DateTime.UtcNow;
        }

        /// <summary>Pause playback without changing the current speed or time.</summary>
        public void Pause()
        {
            if (!_enabled) return;
            _playbackStatus = PlaybackStatus.Paused;
            _lastTickWallTime = null;
        }

        /// <summary>Capture the current playback state as a serializable snapshot for protocol response.</summary>
        public PlaybackStateSnapshot ToState(bool didSeek, string requestId)
        {
            return new PlaybackStateSnapshot
            {
                Status = (byte)_playbackStatus,
                CurrentTimeNs = NowNs,
                Speed = _speed,
                DidSeek = didSeek,
                RequestId = requestId
            };
        }

        /// <summary>Serializable snapshot of the playback clock state for protocol messages.</summary>
        public struct PlaybackStateSnapshot
        {
            /// <summary>Raw PlaybackStatus byte (0=Playing, 1=Paused).</summary>
            public byte Status;
            /// <summary>Current playback time in nanoseconds.</summary>
            public ulong CurrentTimeNs;
            /// <summary>Current playback speed multiplier.</summary>
            public float Speed;
            /// <summary>Whether this snapshot was triggered by a seek command.</summary>
            public bool DidSeek;
            /// <summary>Request ID from the originating playback control message.</summary>
            public string RequestId;
        }
    }
}
