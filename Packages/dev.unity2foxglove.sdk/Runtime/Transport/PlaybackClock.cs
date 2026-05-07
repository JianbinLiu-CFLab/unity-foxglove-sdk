using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// IFoxgloveClock with playback control support (play/pause/seek/speed).
    /// Live mode by default. EnableRange() activates playback control.
    /// </summary>
    public class PlaybackClock : IFoxgloveClock
    {
        private enum PlaybackStatus : byte
        {
            Playing = 0,
            Paused = 1,
            Buffering = 2,
            Ended = 3
        }

        private readonly IFoxgloveClock _inner;
        private bool _enabled;
        private ulong _startNs, _endNs;
        private PlaybackStatus _playbackStatus = PlaybackStatus.Paused;
        private ulong _currentTimeNs;
        private float _speed = 1f;
        private DateTime? _lastTickWallTime;

        public PlaybackClock(IFoxgloveClock inner = null)
        {
            _inner = inner ?? new SystemClock();
        }

        public bool PlaybackEnabled => _enabled;
        public ulong StartNs => _startNs;
        public ulong EndNs => _endNs;

        public ulong NowNs
        {
            get
            {
                if (!_enabled) return _inner.NowNs;

                if (_playbackStatus == PlaybackStatus.Paused || _playbackStatus == PlaybackStatus.Ended)
                    return _currentTimeNs;

                var now = DateTime.UtcNow;
                if (_lastTickWallTime.HasValue)
                {
                    var elapsed = (now - _lastTickWallTime.Value).Ticks;
                    _currentTimeNs += (ulong)(elapsed * _speed * 100); // 1 tick = 100ns
                    if (_currentTimeNs >= _endNs)
                    {
                        _currentTimeNs = _endNs;
                        _playbackStatus = PlaybackStatus.Ended;
                    }
                }
                _lastTickWallTime = now;
                return _currentTimeNs;
            }
        }

        public void EnableRange(ulong startNs, ulong endNs)
        {
            _enabled = true;
            _startNs = startNs;
            _endNs = endNs;
            _currentTimeNs = startNs;
            _playbackStatus = PlaybackStatus.Paused;
            _speed = 1f;
        }

        public void Apply(byte command, float speed, bool hasSeek, ulong seekTimeNs)
        {
            if (!_enabled) return;

            _speed = speed > 0 ? speed : 1f;

            switch (command)
            {
                case 0: // Play
                    _playbackStatus = PlaybackStatus.Playing;
                    _lastTickWallTime = DateTime.UtcNow;
                    break;
                case 1: // Pause
                    _playbackStatus = PlaybackStatus.Paused;
                    break;
            }

            if (hasSeek)
            {
                _currentTimeNs = Math.Clamp(seekTimeNs, _startNs, _endNs);
                // Keep current status or switch to Paused if not Playing
                if (_playbackStatus != PlaybackStatus.Playing)
                    _playbackStatus = PlaybackStatus.Paused;
            }
        }

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

        public struct PlaybackStateSnapshot
        {
            public byte Status;
            public ulong CurrentTimeNs;
            public float Speed;
            public bool DidSeek;
            public string RequestId;
        }
    }
}
