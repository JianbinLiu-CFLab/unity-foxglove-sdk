namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Foxglove WebSocket protocol v1 binary opcodes (server → client).</summary>
    public static class ServerOpcode
    {
        public const byte MessageData = 1;
        public const byte Time = 2;
        public const byte ServiceCallResponse = 3;
        public const byte FetchAssetResponse = 4;
        public const byte PlaybackState = 5;
    }

    /// <summary>Foxglove WebSocket protocol v1 binary opcodes (client → server).</summary>
    public static class ClientOpcode
    {
        public const byte MessageData = 1;
        public const byte ServiceCallRequest = 2;
        public const byte PlaybackControlRequest = 3;
    }
}
