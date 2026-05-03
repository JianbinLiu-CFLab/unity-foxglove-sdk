using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// IFoxgloveClock with playback control support (play/pause/seek/speed).
    /// Live mode by default. EnableRange() activates playback control.
    /// </summary>
    public class PlaybackClock : IFoxgloveClock
    {
        private readonly IFoxgloveClock _inner;
        private bool _enabled;
        private ulong _startNs, _endNs;
        private byte _status; // 0=Playing, 1=Paused, 2=Buffering, 3=Ended
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

                if (_status == 1 /* Paused */ || _status == 3 /* Ended */)
                    return _currentTimeNs;

                var now = DateTime.UtcNow;
                if (_lastTickWallTime.HasValue)
                {
                    var elapsed = (now - _lastTickWallTime.Value).Ticks;
                    _currentTimeNs += (ulong)(elapsed * _speed * 100); // 1 tick = 100ns
                    if (_currentTimeNs >= _endNs)
                    {
                        _currentTimeNs = _endNs;
                        _status = 3; // Ended
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
            _status = 1; // Paused
            _speed = 1f;
        }

        public void Apply(byte command, float speed, bool hasSeek, ulong seekTimeNs)
        {
            if (!_enabled) return;

            _speed = speed > 0 ? speed : 1f;

            switch (command)
            {
                case 0: // Play
                    _status = 0;
                    _lastTickWallTime = DateTime.UtcNow;
                    break;
                case 1: // Pause
                    _status = 1;
                    break;
            }

            if (hasSeek)
            {
                _currentTimeNs = Math.Clamp(seekTimeNs, _startNs, _endNs);
                _status = _status == 0 ? (byte)0 : (byte)1;
            }
        }

        public PlaybackStateSnapshot ToState(bool didSeek, string requestId)
        {
            return new PlaybackStateSnapshot
            {
                Status = _status,
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
