// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-21/22/23/24/27 legacy phase optimization checks.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-21")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunGenerationParityTests
    {
        [Fact]
        public void SharedValidatorReportsHostIndependentDiagnostics()
        {
            var conflicts = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Conflicts", new[]
                {
                    Member("_value", "/demo/conflict", "schema.A", publishMode: 0),
                    Member("__value", "/demo/conflict", "schema.B", publishMode: 1)
                })
            });

            var conflictDiagnostics = FoxRunGenerationModelValidator.Validate(conflicts);
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN002");
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN003");
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN005");

            var policy = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Policy", new[]
                {
                    Member("_nanRate", "/demo/nan", rateHz: float.NaN),
                    Member("_infEpsilon", "/demo/inf_eps", changeEpsilon: float.PositiveInfinity),
                    Member("_infInterval", "/demo/inf_interval", forceIntervalSeconds: float.NegativeInfinity)
                })
            });

            var policyDiagnostics = FoxRunGenerationModelValidator.Validate(policy);
            Assert.Contains(policyDiagnostics, d => d.Id == "FOXRUN009" && d.MemberName == "_nanRate");
            Assert.Contains(policyDiagnostics, d => d.Id == "FOXRUN009" && d.MemberName == "_infEpsilon");
            Assert.Contains(policyDiagnostics, d => d.Id == "FOXRUN009" && d.MemberName == "_infInterval");
        }

        [Fact]
        public void FoxRunGenerationSourcesKeepParityGuards()
        {
            var codegen = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var generator = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var build = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var hook = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");
            var methodComment = TestSources.Slice(codegen, "/// Refresh canonical FoxRun manifest artifacts", "public static FoxRunCanonicalManifest GenerateManifestFilesOnly()");

            Assert.Contains("typeof(IList<>)", codegen, StringComparison.Ordinal);
            Assert.Contains("case '\\t':", generator, StringComparison.Ordinal);
            Assert.Contains("case '\\b':", generator, StringComparison.Ordinal);
            Assert.Contains("ToString(\"x4\", CultureInfo.InvariantCulture)", generator, StringComparison.Ordinal);
            Assert.Contains("emittedTypes", generator, StringComparison.Ordinal);
            Assert.Contains("FoxRunGenerationModel(emittedTypes", generator, StringComparison.Ordinal);
            Assert.Contains("WriteSourceFileIfChanged", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllBytes(absolutePath, sourceBytes)", codegen, StringComparison.Ordinal);
            Assert.Contains("WriteTextIfChanged", build, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllText(linkPath, linkXml)", build, StringComparison.Ordinal);
            Assert.Contains("schema info", methodComment, StringComparison.Ordinal);
            Assert.Contains("generation descriptor", methodComment, StringComparison.Ordinal);
            Assert.Contains("artifact set", hook, StringComparison.Ordinal);
            Assert.Contains("next Play attempt", hook, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException ex", codegen, StringComparison.Ordinal);
            Assert.Contains("Debug.LogWarning", codegen, StringComparison.Ordinal);
            Assert.Contains("LoaderExceptions", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("const string OutputDir", codegen, StringComparison.Ordinal);
            Assert.Contains("diagnostic.MemberName", generator, StringComparison.Ordinal);
            Assert.Contains("TryGetValue", generator, StringComparison.Ordinal);
            Assert.DoesNotContain("foreach (var pair in memberLocations)", generator, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14021MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_21Validation.cs", "--phase140-21", "Phase140_21Validation.Validate");

        private static FoxRunGenerationMember Member(
            string name,
            string topic,
            string schemaName = "",
            float rateHz = 10f,
            int publishMode = 0,
            float changeEpsilon = 0f,
            float forceIntervalSeconds = 0f)
        {
            return new FoxRunGenerationMember(
                "Demo",
                "Probe",
                name,
                "field",
                "System.Single",
                "float",
                true,
                false,
                string.Empty,
                topic,
                rateHz,
                schemaName,
                publishMode,
                changeEpsilon,
                forceIntervalSeconds,
                "Test",
                0,
                string.Empty);
        }
    }

    [Trait("Phase", "140-22")]
    [Trait("Domain", "Harness")]
    public sealed class InspectorPublisherHardeningTests
    {
        [Fact]
        public void InspectorAsyncChecksAndSmallFixesRemainPresent()
        {
            var check = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            var cameraEditor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var preflight = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");
            var mcap = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var cameraInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Assert.DoesNotContain("process.WaitForExit();", check, StringComparison.Ordinal);
            Assert.DoesNotContain("WaitForStreamDrain(stdoutTask, stderrTask, -1)", check, StringComparison.Ordinal);
            Assert.Contains("StartOpenH264Check(", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => OpenH264ExecutableCheck.Check", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteOpenH264CheckIfReady", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("serializedObject.targetObject == null", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("StartOpenH264Check(installedHelperPath, installedDllPath)", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => AnalyzeReplayMcapWorker", preflight, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteAnalyzeReplayMcapIfReady", preflight, StringComparison.Ordinal);
            Assert.Contains("Analyzing replay file", preflight, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => FindLatestReadableRecordingWorker", preflight, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteFindLatestRecordingIfReady", preflight, StringComparison.Ordinal);
            Assert.Contains("Searching latest readable recording", preflight, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", preflight, StringComparison.Ordinal);
            Assert.Contains("Skipping unreadable MCAP", preflight, StringComparison.Ordinal);
            Assert.Contains("Undo.RecordObject(target, \"Disable Replay Auto Play\")", mcap, StringComparison.Ordinal);
            Assert.Contains("OpenCurrentEvidenceRoot()", mcap, StringComparison.Ordinal);
            Assert.Contains("catch (System.Exception ex)", mcap, StringComparison.Ordinal);
            Assert.Contains("Failed to open current schema evidence", mcap, StringComparison.Ordinal);
            Assert.Contains("BuildCameraOutputModeLabels()", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("CameraVideoOutputProfile.ForMode", cameraEditor, StringComparison.Ordinal);
            Assert.DoesNotContain("currentIndex = 0;", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("_cachedRootCaFingerprintPath", manager, StringComparison.Ordinal);
            Assert.Contains("GetCachedRootCaFingerprint", manager, StringComparison.Ordinal);
            Assert.Contains("_lastRootCaDistributorPath", manager, StringComparison.Ordinal);
            Assert.Contains("RestartEditorRootCaDistributorIfPossible", manager, StringComparison.Ordinal);
            Assert.Contains("PlayModeStateChange.EnteredEditMode", manager, StringComparison.Ordinal);
            Assert.Contains("ObjectFieldTypeCache", cameraInfo, StringComparison.Ordinal);
            Assert.Contains("TryGetValue(typeName", cameraInfo, StringComparison.Ordinal);
            Assert.DoesNotContain("private static bool _connectionSecurityExpanded", manager, StringComparison.Ordinal);
            Assert.Contains("private bool _connectionSecurityExpanded", manager, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14022MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_22Validation.cs", "--phase140-22", "Phase140_22Validation.Validate");
    }

    [Trait("Phase", "140-23")]
    [Trait("Domain", "Harness")]
    public sealed class NativeEditorSchemaEvidenceHardeningTests
    {
        [Fact]
        public void SchemaEvidenceAndManifestWritersKeepFailureBoundaries()
        {
            var paths = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var build = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var schemaInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var manifest = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");

            Assert.Contains("Path.GetFullPath(Path.Combine(ProjectRoot, candidate))", paths, StringComparison.Ordinal);
            Assert.Contains("Schema evidence root must stay inside Assets.", paths, StringComparison.Ordinal);
            Assert.Contains("ProjectAssetsRoot", paths, StringComparison.Ordinal);
            Assert.Contains("IsSameOrChildPath", paths, StringComparison.Ordinal);
            Assert.Contains("RemoveStaleLinkXml(linkPath)", build, StringComparison.Ordinal);
            Assert.Contains("Failed at: delete-stale-link", build, StringComparison.Ordinal);
            Assert.Contains("throw new BuildFailedException", build, StringComparison.Ordinal);
            Assert.Contains("TryDeleteTempFile(tempPath)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("catch (IOException)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("catch (UnauthorizedAccessException)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("TryDeleteTempFile(tempPath)", manifest, StringComparison.Ordinal);
            Assert.Contains("catch (IOException)", manifest, StringComparison.Ordinal);
            Assert.Contains("catch (UnauthorizedAccessException)", manifest, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14023MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_23Validation.cs", "--phase140-23", "Phase140_23Validation.Validate");
    }

    [Trait("Phase", "140-24")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2ForUnityPackageContractTests
    {
        private const string BaseSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string NativePackageSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE";

        [Fact]
        public void NativeBridgeDefinesAndDocsStayPackageScoped()
        {
            var asmdef = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            var installer = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var readme = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/README.md");
            var contextInterface = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Context.cs");
            var unavailable = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2UnavailableContext.cs");

            Assert.Contains("\"" + BaseSymbol + "\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"" + NativePackageSymbol + "\"", asmdef, StringComparison.Ordinal);
            Assert.True(asmdef.IndexOf("\"defineConstraints\"", StringComparison.Ordinal) < asmdef.IndexOf("\"" + NativePackageSymbol + "\"", StringComparison.Ordinal));
            Assert.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("BaseCompileSymbol", installer, StringComparison.Ordinal);
            Assert.Contains("NativeRuntimePackageCompileSymbol", installer, StringComparison.Ordinal);
            Assert.Contains(NativePackageSymbol, installer, StringComparison.Ordinal);
            Assert.Contains("EnsureSymbol(parts, BaseCompileSymbol)", installer, StringComparison.Ordinal);
            Assert.Contains("EnsureSymbol(parts, NativeRuntimePackageCompileSymbol)", installer, StringComparison.Ordinal);
            Assert.Contains("RemoveSymbol(parts, NativeRuntimePackageCompileSymbol)", installer, StringComparison.Ordinal);
            Assert.DoesNotContain("set the symbol manually for external imports", readme, StringComparison.Ordinal);
            Assert.DoesNotContain("For an external, non-package ROS2 For Unity import, add that symbol manually.", readme, StringComparison.Ordinal);
            Assert.Contains(NativePackageSymbol, readme, StringComparison.Ordinal);
            Assert.Contains("managed by the runtime-package detector", readme, StringComparison.Ordinal);
            Assert.Contains("external source-only adapter samples", readme, StringComparison.Ordinal);
            Assert.Contains("Null or whitespace node names must be normalized", contextInterface, StringComparison.Ordinal);
            Assert.Contains("Unavailable subscriptions preserve the topic but intentionally do not invoke callbacks", unavailable, StringComparison.Ordinal);
        }

        [Fact]
        public void ComplianceHashesAreLowercase()
        {
            var manifest = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Compliance/ros2-for-unity-adoption-manifest.json");
            var matches = Regex.Matches(manifest, "\"(?:artifactSha256|releaseAssetSha256)\"\\s*:\\s*\"([0-9A-Fa-f]{64})\"");
            Assert.True(matches.Count >= 2);

            foreach (Match match in matches)
            {
                var value = match.Groups[1].Value;
                Assert.Equal(value.ToLowerInvariant(), value);
            }
        }

        [Fact]
        public void Phase14024MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_24Validation.cs", "--phase140-24", "Phase140_24Validation.Validate");
    }

    [Trait("Phase", "140-27")]
    [Trait("Domain", "Harness")]
    public sealed class UnityDemoRuntimeScriptTests
    {
        [Fact]
        public void DemoRuntimeAndManualAcceptanceContractsStayExplicit()
        {
            var fullDemo = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var mouseDrag = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs");
            var status = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveStatusSmoke.cs");
            var testLog = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");
            var scene = TestSources.Text("Unity2Foxglove/Assets/Scenes/SampleScene.unity");

            Assert.Contains("var runtime = _manager?.Runtime;", fullDemo, StringComparison.Ordinal);
            Assert.Contains("if (runtime?.Session == null)", fullDemo, StringComparison.Ordinal);
            Assert.Contains("_initialized = false;", fullDemo, StringComparison.Ordinal);
            Assert.DoesNotContain("FindGameObjectWithTag(\"Player\")", fullDemo, StringComparison.Ordinal);
            Assert.DoesNotContain("using Player-tagged fallback object", fullDemo, StringComparison.Ordinal);
            Assert.Contains("return JToken.Parse(\"{\\\"status\\\":\\\"error\\\",\\\"reason\\\":\\\"cube not found\\\"}\");", fullDemo, StringComparison.Ordinal);
            Assert.Contains("private static readonly UTF8Encoding StrictUtf8", fullDemo, StringComparison.Ordinal);
            Assert.Contains("new UTF8Encoding(false, true)", fullDemo, StringComparison.Ordinal);
            Assert.Contains("#if ENABLE_INPUT_SYSTEM", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("TryReadMouse", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("private Camera _camera;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("_camera = Camera.main;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("var cam = _camera;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("#if ENABLE_INPUT_SYSTEM", status, StringComparison.Ordinal);
            Assert.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", status, StringComparison.Ordinal);
            Assert.Contains("WasKeyPressed", status, StringComparison.Ordinal);
            Assert.Contains("private Vector3 _position2;", testLog, StringComparison.Ordinal);
            Assert.DoesNotContain("public Vector3 position;", testLog, StringComparison.Ordinal);
            Assert.DoesNotContain("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\r\n  _manager: {fileID: 0}\r\n  _cube: {fileID: 0}", scene, StringComparison.Ordinal);
            Assert.DoesNotContain("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\n  _manager: {fileID: 0}\n  _cube: {fileID: 0}", scene, StringComparison.Ordinal);
        }

        [Fact]
        public void ManualSmokeStateAndCountersRemainLongRunningSafe()
        {
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveDebugOverlaySmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs");

            var phase110 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");
            var phase127 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            var probe = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxRun115FManualProbe.cs");
            var trigger = TestSources.Text("Unity2Foxglove/Assets/Scripts/FoxRun/FoxRunTriggerTelemetrySmoke.cs");
            var context = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs");

            Assert.Contains("_warnedMissingStartExecutor", phase110, StringComparison.Ordinal);
            Assert.Contains("StartExecutor reflection hook was not found", phase110, StringComparison.Ordinal);
            Assert.Contains("continuing without explicit executor start", phase110, StringComparison.Ordinal);
            Assert.Contains("_warnedMissingStartExecutor", phase127, StringComparison.Ordinal);
            Assert.Contains("StartExecutor reflection hook was not found", phase127, StringComparison.Ordinal);
            Assert.Contains("continuing without explicit executor start", phase127, StringComparison.Ordinal);
            Assert.Contains("UNITY2FOXGLOVE_R2FU_EXECUTOR_STARTED=False", phase127, StringComparison.Ordinal);
            Assert.DoesNotContain("method?.Invoke(_ros2Unity, null);", phase127, StringComparison.Ordinal);
            Assert.Contains("private long _frameCount;", probe, StringComparison.Ordinal);
            Assert.Contains("sampleList[2] = (float)(_frameCount % 16777216L);", probe, StringComparison.Ordinal);
            Assert.Contains("public long fixedCounter;", trigger, StringComparison.Ordinal);
            Assert.DoesNotContain("public int fixedCounter;", trigger, StringComparison.Ordinal);

            var isAvailableIndex = context.IndexOf("public bool IsAvailable", StringComparison.Ordinal);
            var tryEnsureIndex = context.IndexOf("public bool TryEnsureReady()", StringComparison.Ordinal);
            Assert.True(isAvailableIndex >= 0 && tryEnsureIndex > isAvailableIndex);
            var isAvailableBlock = context.Substring(isAvailableIndex, tryEnsureIndex - isAvailableIndex);
            Assert.DoesNotContain("TryEnsureReady()", isAvailableBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("AddComponent<ROS2UnityComponent>()", isAvailableBlock, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14027MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_27Validation.cs", "--phase140-27", "Phase140_27Validation.Validate");

        private static void VerifyRunInBackgroundRestore(string path)
        {
            var source = TestSources.Text(path);
            Assert.Contains("private bool _previousRunInBackground;", source, StringComparison.Ordinal);
            Assert.Contains("_previousRunInBackground = Application.runInBackground;", source, StringComparison.Ordinal);
            Assert.Contains("Application.runInBackground = true;", source, StringComparison.Ordinal);
            Assert.Contains("Application.runInBackground = _previousRunInBackground;", source, StringComparison.Ordinal);
        }
    }
}
