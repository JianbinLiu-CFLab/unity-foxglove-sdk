using System;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    public interface IRuntimeContext
    {
        bool PlaybackEnabled { get; }
        ulong GetPlaybackStartNs();
        ulong GetPlaybackEndNs();
        void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs);
        PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId);
        void ReplaySeek(ulong timeNs);
        void ReplayPlay();
        void ReplayPause();
        FoxgloveAssetRegistry Assets { get; }
    }
}
