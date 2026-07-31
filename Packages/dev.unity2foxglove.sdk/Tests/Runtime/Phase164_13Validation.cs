using System;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_13Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-13 Tests ---");
            _passed = 0;

            VerifyProtocolHotPathOptimizationsRemainInPlace();
            VerifyRos1ServiceEncodingIsCached();
            VerifyPointCloudBuildersRemainDeferredToPointCloudPhase();
            VerifyTimeUtilityAvoidsPerCallUtcNow();
            VerifyRegistry();

            Console.WriteLine("Phase 164-13: " + _passed + " checks passed.\n");
        }

        private static void VerifyProtocolHotPathOptimizationsRemainInPlace()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            var message = PhaseValidationSourceHelpers.SourceMethod(source, "public static void EncodeServerMessageData");
            var time = PhaseValidationSourceHelpers.SourceMethod(source, "public static void EncodeTime(byte[] destination");

            Check(source.Contains("public const int ServerMessageDataHeaderLength = 13", StringComparison.Ordinal)
                  && source.Contains("public const int TimeFrameLength = 9", StringComparison.Ordinal),
                "164-13A-1: fixed binary frame lengths are shared constants");
            Check(message.Contains("ValidateBufferRange(destination", StringComparison.Ordinal)
                  && message.Contains("WriteU32LEUnchecked", StringComparison.Ordinal)
                  && message.Contains("WriteU64LEUnchecked", StringComparison.Ordinal),
                "164-13A-2: MessageData reusable encoder validates once then uses unchecked writes");
            Check(time.Contains("ValidateBufferRange(destination", StringComparison.Ordinal)
                  && time.Contains("WriteU64LEUnchecked", StringComparison.Ordinal),
                "164-13A-3: Time reusable encoder validates once then uses unchecked writes");
        }

        private static void VerifyRos1ServiceEncodingIsCached()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            var cache = PhaseValidationSourceHelpers.SourceMethod(source, "private static byte[] GetCachedServiceEncodingBytes");

            Check(source.Contains("Ros1EncodingBytes", StringComparison.Ordinal)
                  && cache.Contains("string.Equals(encoding, \"ros1\", StringComparison.Ordinal)", StringComparison.Ordinal),
                "164-13B-1: common ros1 service response encoding avoids per-call UTF-8 allocation");

            var ros1Response = BinaryEncoding.EncodeServerServiceCallResponse(1, 2, "ros1", new byte[] { 3 });
            Check(BinaryEncoding.ReadU32LE(ros1Response, 9) == 4u,
                "164-13B-2: cached ros1 encoding writes the correct encoding length");
            Check(ros1Response[13] == (byte)'r' && ros1Response[14] == (byte)'o'
                  && ros1Response[15] == (byte)'s' && ros1Response[16] == (byte)'1'
                  && ros1Response[17] == 3,
                "164-13B-3: cached ros1 encoding preserves the service response wire bytes");
        }

        private static void VerifyPointCloudBuildersRemainDeferredToPointCloudPhase()
        {
            var packed = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var nativePacked = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");

            Check(packed.Contains("public static PointCloudPackedData Build(PointCloudFrame frame)", StringComparison.Ordinal)
                  && nativePacked.Contains("internal static PointCloudPackedData BuildVirtualLidarFullStride", StringComparison.Ordinal),
                "164-13C-1: point-cloud builder optimization candidates are present for the dedicated point-cloud phase");
        }

        private static void VerifyTimeUtilityAvoidsPerCallUtcNow()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MessageDefinitions/FoxgloveTimeUtil.cs");
            var now = PhaseValidationSourceHelpers.SourceMethod(source, "public static ulong NowUnixTimeNs");

            Check(now.Contains("Stopwatch.GetTimestamp()", StringComparison.Ordinal)
                  && !now.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal)
                  && !now.Contains("DateTime.UtcNow", StringComparison.Ordinal),
                "164-13D-1: timestamp hot path uses the Stopwatch anchor instead of per-call UTC lookup");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-13\"", StringComparison.Ordinal), "164-13E-1: validation registry exposes Phase164-13");
            Check(project.Contains("Phase164_13Validation.cs", StringComparison.Ordinal), "164-13E-2: runtime validation project compiles Phase164-13");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
