using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_16Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-16 Tests ---");
            _passed = 0;

            VerifyAnnexBPacketizersUseBulkAppend();
            VerifySidecarDiagnosticsAvoidTemporaryArrays();
            VerifyMediaFoundationOutputQueueCapacityIsCached();
            VerifyRegistry();

            Console.WriteLine("Phase 164-16: " + _passed + " checks passed.\n");
        }

        private static void VerifyAnnexBPacketizersUseBulkAppend()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AnnexBAccessUnitPacketizer.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H265AnnexBAccessUnitPacketizer.cs",
            })
            {
                var source = Read(relativePath);
                var append = PhaseValidationSourceHelpers.SourceMethod(source, "public void Append(byte[] data, int offset, int count)");

                Check(append.Contains("_buffer.AddRange(new ArraySegment<byte>(data, offset, count));", StringComparison.Ordinal),
                    "164-16A-1: " + relativePath + " appends read chunks with a bulk copy");
                Check(!append.Contains("for (var i = 0; i < count; i++)", StringComparison.Ordinal)
                      && !append.Contains("_buffer.Add(data[offset + i])", StringComparison.Ordinal),
                    "164-16A-2: " + relativePath + " avoids per-byte List.Add in Append");
            }
        }

        private static void VerifySidecarDiagnosticsAvoidTemporaryArrays()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
            })
            {
                var source = Read(relativePath);
                var publish = PhaseValidationSourceHelpers.SourceMethod(source, "private static void PublishDiagnosticLine");

                Check(source.Contains("var retained = new byte[lineLimit];", StringComparison.Ordinal)
                      && source.Contains("retained[retainedCount++] = value;", StringComparison.Ordinal),
                    "164-16B-1: " + relativePath + " reuses a bounded byte buffer for stderr lines");
                Check(publish.Contains("Encoding.UTF8.GetString(retained, 0, retainedCount)", StringComparison.Ordinal)
                      && !publish.Contains("retained.ToArray()", StringComparison.Ordinal),
                    "164-16B-2: " + relativePath + " decodes stderr lines without a temporary ToArray allocation");
            }
        }

        private static void VerifyMediaFoundationOutputQueueCapacityIsCached()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var start = PhaseValidationSourceHelpers.SourceMethod(source, "public bool Start");
            var enqueue = PhaseValidationSourceHelpers.SourceMethod(source, "private void EnqueueAccessUnit");
            var stop = PhaseValidationSourceHelpers.SourceMethod(source, "private void Stop");

            Check(source.Contains("private int _maxOutputQueue = 4;", StringComparison.Ordinal)
                  && start.Contains("_maxOutputQueue = Math.Max(1, _options.MaxOutputQueue);", StringComparison.Ordinal)
                  && stop.Contains("_maxOutputQueue = 4;", StringComparison.Ordinal),
                "164-16C-1: Media Foundation sidecar resolves output queue capacity once per session");
            Check(enqueue.Contains("while (_outputCount >= _maxOutputQueue", StringComparison.Ordinal)
                  && !enqueue.Contains("_options?.MaxOutputQueue", StringComparison.Ordinal),
                "164-16C-2: Media Foundation output enqueue avoids repeated option lookup inside the lock");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-16\"", StringComparison.Ordinal), "164-16D-1: validation registry exposes Phase164-16");
            Check(project.Contains("Phase164_16Validation.cs", StringComparison.Ordinal), "164-16D-2: runtime validation project compiles Phase164-16");
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
