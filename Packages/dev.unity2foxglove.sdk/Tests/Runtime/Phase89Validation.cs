// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 89 validation for Draco CompressedPointCloud productization.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 89 Raw/Draco point-cloud output-mode productization.
    /// </summary>
    public static class Phase89Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 89: CompressedPointCloud Draco Productization ===");
            _passed = 0;

            VerifyPointCloudOutputMode();
            VerifyUnifiedPointCloudPublisher();
            VerifyLegacySpikePublisher();
            VerifyDracoNativeEncoder();
            VerifyPointCloudInspector();
            VerifyDracoNativeCheck();
            VerifyDocumentation();
            VerifyBundledWindowsDracoNativePlugin();

            Console.WriteLine($"Phase 89: {_passed} checks passed.");
        }

        private static void VerifyPointCloudOutputMode()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudOutputMode.cs");
            Check(!string.IsNullOrEmpty(source),
                "89A-1: PointCloudOutputMode source exists");
            Check(source.Contains("public enum PointCloudOutputMode")
                  && source.Contains("Raw = 0")
                  && source.Contains("Draco = 1"),
                "89A-2: PointCloudOutputMode exposes Raw=0 and Draco=1");
            Check(source.Contains("PointCloudOutputModeDefaults")
                  && source.Contains("RawTopic = \"/unity/point_cloud\"")
                  && source.Contains("DracoTopic = \"/unity/point_cloud_draco\"")
                  && source.Contains("RawSchema = \"foxglove.PointCloud\"")
                  && source.Contains("DracoSchema = \"foxglove.CompressedPointCloud\""),
                "89A-3: output mode defaults define raw/draco topics and schemas");
            Check(source.Contains("PointCloudOutputProfile")
                  && source.Contains("SupportsJson")
                  && source.Contains("SupportsProtobuf")
                  && source.Contains("ForMode(PointCloudOutputMode mode)"),
                "89A-4: output profile resolves encoding support per mode");

            var qos = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs");
            Check(!qos.Contains("enum PointCloudOutputMode"),
                "89A-5: output mode stays separate from PointCloudQoS sampling");

            var modeType = Type.GetType("Unity.FoxgloveSDK.Components.PointCloudOutputMode, FoxgloveSdk.Tests");
            Check(modeType != null && modeType.IsEnum,
                "89A-6: PointCloudOutputMode type is loadable");
            Check((int)Enum.Parse(modeType, "Raw") == 0 && (int)Enum.Parse(modeType, "Draco") == 1,
                "89A-7: PointCloudOutputMode enum values are stable");
        }

        private static void VerifyUnifiedPointCloudPublisher()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Check(source.Contains("[Header(\"Point Cloud Output\")]")
                  && source.Contains("_outputMode = PointCloudOutputMode.Raw")
                  && source.Contains("_warnedDracoFailure"),
                "89B-1: point-cloud publisher exposes Raw/Draco output fields without a helper path");
            Check(source.Contains("ActiveProfile => PointCloudOutputProfile.ForMode(_outputMode)")
                  && source.Contains("SchemaNameOverride => ActiveProfile.SchemaName")
                  && source.Contains("DefaultTopic => ActiveProfile.DefaultTopic"),
                "89B-2: schema and default topic resolve from output mode profile");
            Check(source.Contains("public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson")
                  && source.Contains("public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf"),
                "89B-3: supported encodings are mode-gated by the output profile");
            Check(source.Contains("if (_outputMode == PointCloudOutputMode.Draco)")
                  && source.Contains("PublishDracoFrame(frame, unixNs)")
                  && source.Contains("PublishRawFrame(frame, unixNs)"),
                "89B-4: PublishPreparedFrame branches raw versus Draco inside the unified publisher");
            Check(source.Contains("DracoPointCloudNativeEncoder")
                  && source.Contains("CompressedPointCloudMessageBuilder.SerializeProtobuf")
                  && source.Contains("TryEncode(frame, out var dracoPayload, out var encodeError)")
                  && source.Contains("PublishProto(payload, unixNs)"),
                "89B-5: Draco mode encodes through bundled native DLL and publishes CompressedPointCloud protobuf");
            Check(source.Contains("PointCloudMessageBuilder.SerializeProtobuf(frame)")
                  && source.Contains("PointCloudMessageBuilder.CreateJson(frame)"),
                "89B-6: raw mode preserves existing PointCloud protobuf and JSON builders");
            Check(source.Contains("publishes nothing")
                  && source.Contains("LogDracoFailure"),
                "89B-7: Draco failures log and do not fall back to raw");
            Check(!source.Contains("Task.Run")
                  && !source.Contains("BlockingCollection")
                  && !source.Contains("ConcurrentQueue"),
                "89B-8: Phase89 keeps synchronous native encode and adds no async worker queue");
        }

        private static void VerifyDracoNativeEncoder()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");
            Check(source.Contains("public static class DracoPointCloudNativeEncoder")
                  && source.Contains("Unity2FoxgloveDracoNative")
                  && source.Contains("DllImport"),
                "89C-1: native Draco encoder wraps the bundled Windows DLL through P/Invoke");
            Check(source.Contains("TryEncode(PointCloudFrame frame, out byte[] dracoPayload, out string error)")
                  && source.Contains("BuildXyzArray")
                  && source.Contains("GCHandle.Alloc"),
                "89C-2: native Draco encoder accepts PointCloudFrame and pins XYZ/output buffers");
            Check(source.Contains("TryGetAvailability")
                  && source.Contains("DllNotFoundException")
                  && source.Contains("EntryPointNotFoundException")
                  && source.Contains("BadImageFormatException"),
                "89C-3: native Draco encoder reports missing or incompatible DLLs explicitly");
        }

        private static void VerifyLegacySpikePublisher()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedPointCloudPublisher.cs");
            Check(source.Contains("[AddComponentMenu(\"\")]")
                  && source.Contains("Legacy standalone Phase 87 Draco spike publisher")
                  && source.Contains("Point Cloud Output Mode")
                  && source.Contains("Draco"),
                "89D-1: legacy compressed point-cloud spike publisher is hidden and points users to unified Draco mode");
            Check(source.Contains("class FoxgloveCompressedPointCloudPublisher : FoxglovePointCloudPublisher")
                  && source.Contains("DracoPointCloudNativeEncoder.TryEncode")
                  && source.Contains("_logDracoFailures")
                  && !source.Contains("_helperExecutablePath")
                  && !source.Contains("_encodeTimeoutMs"),
                "89D-2: legacy publisher uses the bundled native DLL and exposes no helper executable setup");
            Check(!source.Contains("PointCloudMessageBuilder.CreateJson")
                  && !source.Contains(" PointCloudMessageBuilder.SerializeProtobuf")
                  && !source.Contains("= PointCloudMessageBuilder.SerializeProtobuf"),
                "89D-3: retained legacy publisher does not route through raw PointCloud builders");
        }

        private static void VerifyPointCloudInspector()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            Check(source.Contains("PointCloudOutputModeLabels")
                  && source.Contains("Point Cloud Output Mode")
                  && source.Contains("ApplyTopicForModeChange"),
                "89E-1: point-cloud Inspector exposes output mode and preserves default-topic switching");
            Check(source.Contains("Draco")
                  && source.Contains("Check Draco")
                  && source.Contains("Draco Help..."),
                "89E-2: Draco Inspector section exposes native DLL check and help actions without helper path setup");
            Check(source.Contains("bundled Windows native plugin")
                  && source.Contains("synchronous native encode"),
                "89E-3: Inspector documents bundled DLL dependency and synchronous native encode limitation");
            Check(source.Contains("General")
                  && source.Contains("Point Sources")
                  && source.Contains("Point Cloud QoS")
                  && source.Contains("Publish Rate")
                  && source.Contains("Encoding Policy")
                  && source.Contains("DrawResolvedSummaries"),
                "89E-4: Inspector preserves existing point-cloud workflow sections");
        }

        private static void VerifyDracoNativeCheck()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/DracoPointCloudNativeCheck.cs");
            Check(source.Contains("enum DracoPointCloudNativeStatus")
                  && source.Contains("NotChecked")
                  && source.Contains("Available")
                  && source.Contains("Missing")
                  && source.Contains("Invalid"),
                "89F-1: Draco native check defines expected status states");
            Check(source.Contains("DracoPointCloudNativeEncoder.TryGetAvailability")
                  && source.Contains("DracoPointCloudNativeEncoder")
                  && source.Contains("TryEncode"),
                "89F-2: Draco native check validates the bundled DLL with the runtime encoder");
            Check(source.Contains("new PointCloudPoint(0f, 0f, 0f)")
                  && source.Contains("new PointCloudPoint(1f, 0f, 0f)")
                  && source.Contains("new PointCloudPoint(0f, 1f, 0f)"),
                "89F-3: Draco native check performs a tiny three-point XYZ encode smoke");
            Check(!source.Contains("download")
                  && !source.Contains("install")
                  && !source.Contains("PATH")
                  && !source.Contains("OpenFilePanel"),
                "89F-4: Draco native check does not download, install, mutate PATH, or ask for a helper executable");
        }

        private static void VerifyDocumentation()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md");
            var troubleshooting = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/11_Troubleshooting.md");
            var sensors = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/14_Typed_Sensor_Publishers.md");
            var combined = inspector + "\n" + troubleshooting + "\n" + sensors;

            Check(combined.Contains("Point Cloud Output Mode")
                  && combined.Contains("Raw")
                  && combined.Contains("Draco")
                  && combined.Contains("foxglove.CompressedPointCloud")
                  && combined.Contains("format = `draco`"),
                "89G-1: docs describe Raw/Draco point-cloud output modes and CompressedPointCloud format");
            Check(combined.Contains("bundled Windows native plugin")
                  && combined.Contains("Unity2FoxgloveDracoNative.dll")
                  && combined.Contains("publishes nothing")
                  && combined.Contains("raw mode"),
                "89G-2: docs state bundled Draco DLL behavior and no raw fallback");
            Check(combined.Contains("synchronous native")
                  && combined.Contains("Windows"),
                "89G-3: docs mention Phase89 synchronous native encode limitation");
        }

        private static void VerifyBundledWindowsDracoNativePlugin()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var plugin = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Plugins",
                "Windows",
                "x86_64",
                "Unity2FoxgloveDracoNative.dll");
            Check(File.Exists(plugin) && new FileInfo(plugin).Length > 0,
                "89H-1: Windows Draco native plugin DLL is bundled in the SDK package");
            Check(File.Exists(plugin + ".meta"),
                "89H-2: Windows Draco native plugin has Unity metadata");

            var forbiddenBinaryExtensions = new[] { ".exe", ".lib", ".a", ".so", ".dylib" };
            var forbiddenNativeSourceExtensions = new[] { ".h", ".hpp", ".cc", ".cpp", ".c" };
            var checkedRoots = new[]
            {
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk"),
                Path.Combine(root, "Unity2Foxglove", "Assets")
            };

            var forbidden = checkedRoots
                .Where(Directory.Exists)
                .SelectMany(checkedRoot => Directory.EnumerateFiles(checkedRoot, "*", SearchOption.AllDirectories))
                .Where(path => Path.GetFileName(path).IndexOf("draco", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(path => forbiddenBinaryExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                               || forbiddenNativeSourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToArray();

            Check(forbidden.Length == 0,
                "89H-3: no Draco helper executables, import libraries, or vendored native source are bundled under package/assets");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
    }
}
