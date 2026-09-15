using System;
using System.Text;

namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    /// <summary>Explicit bounds for the read-only Component contract projection.</summary>
    public static class ComponentPublisherContractCatalogLimits
    {
        public const int MaxResults = ComponentPublisherSessionLimits.MaxEntries;
        public const int MaxTopicBytes = 512;
        public const int MaxDiagnosticBytes = 1024;
        public const int MaxShapeIdentityBytes = 256;
        public const int MaxResponseBytes = 64 * 1024;

        internal static string TruncateUtf8(string value, int maxBytes, out bool truncated)
        {
            value ??= string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            {
                truncated = false;
                return value;
            }

            var builder = new StringBuilder();
            var bytes = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                var text = rune.ToString();
                var size = Encoding.UTF8.GetByteCount(text);
                if (bytes + size > Math.Max(0, maxBytes - 3)) break;
                builder.Append(text);
                bytes += size;
            }
            truncated = true;
            return builder + "...";
        }
    }
}
