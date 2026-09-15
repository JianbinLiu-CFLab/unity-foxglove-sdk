using System;
using System.Text;

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    public static class ComponentMessagePackMetadataLimits
    {
        public const int MaxContributions = 64;
        public const int MaxEntriesPerContribution = 256;
        public const int MaxAggregateEntries = 1024;
        public const int MaxConflictRows = 64;
        public const int MaxDiagnosticUtf8Bytes = 2048;
        public const int MaxMetadataBytesPerEntry = 8192;
        public const int MaxMetadataBytesTotal = 1048576;
        public const int MaxStringUtf8Bytes = 512;
        public static bool Fits(string value, int maxBytes = MaxStringUtf8Bytes)
            => value == null || Encoding.UTF8.GetByteCount(value) <= maxBytes;
    }
}
