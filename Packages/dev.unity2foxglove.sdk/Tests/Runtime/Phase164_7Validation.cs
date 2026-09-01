using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_7Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-7 Tests ---");
            _passed = 0;

            VerifyRecorderChunkFlushReusesScratchState();
            VerifyDirectionalChannelMetadataRoundtrip();
            VerifyWriterStringPathsAvoidPerFieldArraysAndLinq();
            VerifyCompressionAndAmendmentBuffersAreReusable();
            VerifyParameterChangeUsesStableDto();
            VerifyRegistry();

            Console.WriteLine("Phase 164-7: " + _passed + " checks passed.\n");
        }

        private static void VerifyRecorderChunkFlushReusesScratchState()
        {
            var recorder = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var flush = SourceMethod(recorder, "void FlushChunk()");
            var reset = SourceMethod(recorder, "private void ResetActiveChunkState");
            var summary = SourceMethod(recorder, "private McapFileSummary BuildFinalSummary");

            Check(recorder.Contains("_messageIndexOffsetsScratch", StringComparison.Ordinal)
                  && flush.Contains("var mio = _messageIndexOffsetsScratch", StringComparison.Ordinal)
                  && flush.Contains("mio.Clear()", StringComparison.Ordinal)
                  && flush.Contains("new Dictionary<ushort, ulong>(mio)", StringComparison.Ordinal),
                "164-7A-1: chunk flush reuses a message-index-offset scratch dictionary and snapshots it for chunk indexes");
            Check(flush.Contains("var channelStates = FillAndGetScratchChannelWriteStates()", StringComparison.Ordinal)
                  && flush.Contains("ResetActiveChunkState(channelStates)", StringComparison.Ordinal)
                  && reset.Contains("channelStates ?? FillAndGetScratchChannelWriteStates()", StringComparison.Ordinal),
                "164-7A-2: chunk flush avoids a second channel-state scan on the success path");
            Check(!summary.Contains(".ToDictionary(", StringComparison.Ordinal)
                  && recorder.Contains("BuildChannelMessageCounts()", StringComparison.Ordinal),
                "164-7A-3: final statistics build channel message counts without LINQ");
            Check(recorder.Contains("CreateChannelMetadata(McapChannelDirection.Output)", StringComparison.Ordinal)
                  && recorder.Contains("CreateChannelMetadata(McapChannelDirection.Input)", StringComparison.Ordinal)
                  && recorder.Contains("CreateEmptyChannelMetadata()", StringComparison.Ordinal)
                  && recorder.Contains("SnapshotChannelMetadata(meta)", StringComparison.Ordinal),
                "164-7A-4: directional channel metadata is snapshotted for durable summary records");
        }

        private static void VerifyDirectionalChannelMetadataRoundtrip()
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(stream, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase164-7/output", "json", "", "", "");
                recorder.WriteClientMessage(
                    7,
                    1,
                    10,
                    new byte[] { 1 },
                    "/phase164-7/input");
                recorder.Close();
            }

            stream.Position = 0;
            using var reader = new McapReader(stream);
            var channels = reader.ReadSummary().Channels;
            var output = channels.Single(channel => channel.Topic == "/phase164-7/output");
            var input = channels.Single(channel => channel.Topic == "/phase164-7/input");
            Check(output.Metadata[McapRecorder.DataDirectionMetadataKey] == "output"
                  && input.Metadata[McapRecorder.DataDirectionMetadataKey] == "input",
                "164-7A-5: directional channel metadata roundtrips through the maintained reader");
        }

        private static void VerifyWriterStringPathsAvoidPerFieldArraysAndLinq()
        {
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");
            var writeString = SourceMethod(writer, "public static void WriteString");
            var writeStringMap = SourceMethod(writer, "public static void WriteStringMap");

            Check(writer.Contains("using System.Buffers;", StringComparison.Ordinal)
                  && writeString.Contains("ArrayPool<byte>.Shared.Rent", StringComparison.Ordinal)
                  && !writeString.Contains("Encoding.UTF8.GetBytes(value ??", StringComparison.Ordinal),
                "164-7B-1: MCAP string writer rents UTF-8 buffers instead of allocating per string");
            Check(writer.Contains("_stringMapScratch", StringComparison.Ordinal)
                  && writeStringMap.Contains("map.Count == 1", StringComparison.Ordinal)
                  && writeStringMap.Contains("ordered.Sort", StringComparison.Ordinal)
                  && !writeStringMap.Contains(".OrderBy(", StringComparison.Ordinal)
                  && !writeStringMap.Contains(".ToList()", StringComparison.Ordinal),
                "164-7B-2: MCAP string maps fast-path empty/single entries and avoid LINQ sorting allocations");
            Check(writer.Contains("WriteStringMap(m, meta);", StringComparison.Ordinal),
                "164-7B-3: MCAP writer accepts null metadata without allocating an empty dictionary at call sites");
        }

        private static void VerifyCompressionAndAmendmentBuffersAreReusable()
        {
            var compression = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common/McapCompression.cs");
            var recorder = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var amendment = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs");
            var copyExact = SourceMethod(amendment, "private static void CopyExact");

            Check(recorder.Contains("_compressionBuf", StringComparison.Ordinal)
                  && recorder.Contains("McapCompression.Compress(", StringComparison.Ordinal)
                  && recorder.Contains("_options.Lz4CompressionLevel", StringComparison.Ordinal)
                  && recorder.Contains("_compressionBuf,", StringComparison.Ordinal)
                  && compression.Contains("lz4OutputBuffer.SetLength(0)", StringComparison.Ordinal)
                  && compression.Contains("lz4OutputBuffer.TryGetBuffer", StringComparison.Ordinal),
                "164-7C-1: LZ4 chunk compression can reuse a recorder-owned MemoryStream");
            Check(amendment.Contains("using System.Buffers;", StringComparison.Ordinal)
                  && copyExact.Contains("ArrayPool<byte>.Shared.Rent(64 * 1024)", StringComparison.Ordinal)
                  && copyExact.Contains("ArrayPool<byte>.Shared.Return(buffer)", StringComparison.Ordinal)
                  && !copyExact.Contains("new byte[64 * 1024]", StringComparison.Ordinal),
                "164-7C-2: MCAP amendment copy uses ArrayPool for the 64 KiB transfer buffer");
        }

        private static void VerifyParameterChangeUsesStableDto()
        {
            var controller = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            var onParameterChanged = SourceMethod(controller, "private void OnParameterChanged");

            Check(controller.Contains("private sealed class ParameterMetadataEntry", StringComparison.Ordinal)
                  && controller.Contains("[JsonProperty(\"name\")]", StringComparison.Ordinal)
                  && onParameterChanged.Contains("new ParameterMetadataEntry", StringComparison.Ordinal)
                  && !onParameterChanged.Contains("new { name, type, value, timestamp }", StringComparison.Ordinal),
                "164-7D: parameter-change metadata uses a stable DTO contract instead of an anonymous object");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-7\"", StringComparison.Ordinal), "164-7E-1: validation registry exposes Phase164-7");
            Check(project.Contains("Phase164_7Validation.cs", StringComparison.Ordinal), "164-7E-2: runtime validation project compiles Phase164-7");
            var validation = PhaseValidationRegistry.Find(new[] { "--phase164-7" });
            Check(validation != null
                  && !validation.IncludeInDefault
                  && PhaseValidationRegistry.DefaultValidations(includeLocalEvidence: false)
                      .All(item => item != validation),
                "164-7E-3: Phase164-7 remains explicit-only and directly selectable");
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                throw new InvalidOperationException("Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(
                        dir.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "Runtime",
                        "FoxgloveSdk.Tests.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
