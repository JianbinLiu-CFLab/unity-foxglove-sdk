// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates UPM samples declaration, BasicVisualization and FullDemoVisualization sample integrity, forbidden items, and layout consistency.

using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase17Validation
    {
        /// <summary>
        /// Phase 17 — UPM samples: package.json.samples declaration,
        /// BasicVisualization lightweight sample, FullDemoVisualization complete demo.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 17 Tests ---");

            var repoRoot = Phase16Validation.FindRepoRoot();
            if (repoRoot == null)
            {
                Console.WriteLine("[WARN] Could not find repo root — skipping path-based checks.");
                return;
            }

            var pkgDir = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk");
            var samplesDir = Path.Combine(pkgDir, "Samples~");
            var basicDir = Path.Combine(samplesDir, "BasicVisualization");
            var fullDir = Path.Combine(samplesDir, "FullDemoVisualization");
            var ros2Dir = Path.Combine(samplesDir, "Ros2BridgeSample");
            var configsDir = Path.Combine(repoRoot, "Unity2Foxglove", "Configs");

            // ── 17A: package.json samples declaration ──
            var pkgJson = File.ReadAllText(Path.Combine(pkgDir, "package.json"));
            var pkg = JObject.Parse(pkgJson);
            Assert(pkg["samples"] != null, "package.json.samples exists");
            var samples = pkg["samples"] as JArray;
            Assert(samples != null && samples.Count == 3, "package.json.samples has 3 entries");

            bool hasBasic = false;
            bool hasFull = false;
            bool hasRos2Bridge = false;
            foreach (var s in samples)
            {
                var name = (string)s["displayName"];
                var path = (string)s["path"];
                if (name == "Basic Visualization") hasBasic = true;
                if (name == "Full Demo Visualization") hasFull = true;
                if (name == "ROS2 Bridge Sample") hasRos2Bridge = true;
                Assert(Directory.Exists(Path.Combine(pkgDir, path)), $"Sample path exists: {path}");
            }
            Assert(hasBasic, "Basic Visualization sample declared");
            Assert(hasFull, "Full Demo Visualization sample declared");
            Assert(hasRos2Bridge, "ROS2 Bridge Sample declared");

            // ── package core dependencies ──
            var deps = pkg["dependencies"] as JObject;
            Assert(deps != null, "package.json has dependencies");
            Assert(deps["com.unity.inputsystem"] == null, "No Input System in core dependencies");
            Assert(deps["com.unity.render-pipelines.universal"] == null, "No URP in core dependencies");

            // ── 17B: BasicVisualization ──
            Assert(Directory.Exists(basicDir), "BasicVisualization/ exists");
            Assert(File.Exists(Path.Combine(basicDir, "README.md")), "BasicVisualization README exists");
            Assert(File.Exists(Path.Combine(basicDir, "Scenes", "BasicVisualization.unity")), "BasicVisualization scene exists");
            Assert(File.Exists(Path.Combine(basicDir, "Scenes", "BasicVisualization.unity.meta")), "BasicVisualization scene meta exists");
            Assert(File.Exists(Path.Combine(basicDir, "FoxgloveSimpleLayout.json")), "BasicVisualization FoxgloveSimpleLayout.json exists");
            Assert(File.Exists(Path.Combine(basicDir, "FoxgloveSimpleLayout.json.meta")), "BasicVisualization FoxgloveSimpleLayout.json.meta exists");

            // Basic sample should NOT have the old full layout
            Assert(!File.Exists(Path.Combine(basicDir, "FoxgloveLayout.json")), "BasicVisualization does not contain old FoxgloveLayout.json");

            // ── 17C: FullDemoVisualization ──
            Assert(Directory.Exists(fullDir), "FullDemoVisualization/ exists");
            Assert(File.Exists(Path.Combine(fullDir, "README.md")), "FullDemoVisualization README exists");
            Assert(File.Exists(Path.Combine(fullDir, "Scenes", "FullDemoVisualization.unity")), "FullDemoVisualization scene exists");
            Assert(File.Exists(Path.Combine(fullDir, "Scenes", "FullDemoVisualization.unity.meta")), "FullDemoVisualization scene meta exists");
            Assert(File.Exists(Path.Combine(fullDir, "FoxgloveFullLayout.json")), "FullDemoVisualization FoxgloveFullLayout.json exists");

            // Script pairs
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "FoxgloveDemoSetup.cs")), "FullDemo scripts: FoxgloveDemoSetup.cs");
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "FoxgloveDemoSetup.cs.meta")), "FullDemo scripts: FoxgloveDemoSetup.cs.meta");
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "MouseDragCube.cs")), "FullDemo scripts: MouseDragCube.cs");
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "MouseDragCube.cs.meta")), "FullDemo scripts: MouseDragCube.cs.meta");
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "TestLog.cs")), "FullDemo scripts: TestLog.cs");
            Assert(File.Exists(Path.Combine(fullDir, "Scripts", "TestLog.cs.meta")), "FullDemo scripts: TestLog.cs.meta");

            // InputSystem
            Assert(File.Exists(Path.Combine(fullDir, "InputSystem_Actions.inputactions")), "FullDemo InputSystem_Actions.inputactions exists");
            Assert(File.Exists(Path.Combine(fullDir, "InputSystem_Actions.inputactions.meta")), "FullDemo InputSystem_Actions.inputactions.meta exists");

            // URP settings
            Assert(Directory.Exists(Path.Combine(fullDir, "Settings")), "FullDemo Settings/ exists");
            Assert(File.Exists(Path.Combine(fullDir, "Settings", "UniversalRenderPipelineGlobalSettings.asset")), "FullDemo URP global settings exists");
            Assert(File.Exists(Path.Combine(fullDir, "Settings", "UniversalRenderPipelineGlobalSettings.asset.meta")), "FullDemo URP global settings meta exists");

            var fullScene = File.ReadAllText(Path.Combine(fullDir, "Scenes", "FullDemoVisualization.unity"));
            Assert(!fullScene.Contains("Assembly-CSharp::Phase53FoxRunTriggerSmoke"), "FullDemo scene has current FoxRun trigger class identifier");

            var defaultVolumeProfile = File.ReadAllText(Path.Combine(fullDir, "Settings", "DefaultVolumeProfile.asset"));
            Assert(!defaultVolumeProfile.Contains("Unity.RenderPipelines.Core.Editor.Tests"), "FullDemo default volume profile has no URP Editor.Tests components");

            var demoSetupSource = File.ReadAllText(Path.Combine(fullDir, "Scripts", "FoxgloveDemoSetup.cs"));
            Assert(demoSetupSource.Contains("OnClientMessage -= OnClientMessageReceived"), "FullDemo demo setup unsubscribes client message callback");
            Assert(demoSetupSource.Contains("UnregisterService(_resetSvcId)"), "FullDemo demo setup unregisters reset service on destroy");
            Assert(demoSetupSource.Contains("Mathf.Clamp") && demoSetupSource.Contains("ScaleMinimum") && demoSetupSource.Contains("ScaleMaximum"), "FullDemo demo setup clamps remote scale parameter");

            var sampleSyncSource = File.ReadAllText(Path.Combine(repoRoot, "Scripts", "samples", "sync_full_demo.py"));
            Assert(sampleSyncSource.Contains("\"  _recordingDirectory:\""), "FullDemo sample sync scrubs recording directory");

            // ── 17D: ROS2 Bridge Sample ──
            Assert(Directory.Exists(ros2Dir), "Ros2BridgeSample/ exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "README.md")), "Ros2BridgeSample README exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scenes", "Ros2BridgeSample.unity")), "Ros2BridgeSample scene exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scenes", "Ros2BridgeSample.unity.meta")), "Ros2BridgeSample scene meta exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "FoxgloveRos2BridgeLayout.json")), "Ros2BridgeSample layout exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "FoxgloveRos2BridgeLayout.json.meta")), "Ros2BridgeSample layout meta exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scripts", "Ros2BridgeSampleController.cs")), "Ros2BridgeSample controller exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scripts", "Ros2BridgeSampleController.cs.meta")), "Ros2BridgeSample controller meta exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scripts", "Ros2BridgeSampleLaserScan.cs")), "Ros2BridgeSample laser script exists");
            Assert(File.Exists(Path.Combine(ros2Dir, "Scripts", "Ros2BridgeSamplePointCloud.cs")), "Ros2BridgeSample point cloud script exists");

            // ── Forbidden items in samples ──
            var forbidden = new[] { "Generated", "TutorialInfo", "Editor", "Plugins", "Library", "Logs", "Recordings" };
            foreach (var sampleDir in new[] { basicDir, fullDir, ros2Dir })
            {
                foreach (var f in forbidden)
                {
                    Assert(!Directory.Exists(Path.Combine(sampleDir, f)), $"{Path.GetFileName(sampleDir)}: no {f}/");
                }

                // No *_FoxRun.g.cs
                var allFiles = Directory.GetFiles(sampleDir, "*.cs", SearchOption.AllDirectories);
                foreach (var file in allFiles)
                {
                    var name = Path.GetFileName(file);
                    Assert(!name.Contains("_FoxRun"), $"{Path.GetFileName(sampleDir)}: no generated {name}");
                }
            }

            // ── No absolute paths in samples ──
            // .unity, .asset, and .inputactions are text YAML/JSON — these are
            // exactly where serialized local paths appear, so they MUST be scanned.
            var sampleDirs = new[] { basicDir, fullDir, ros2Dir };
            var windowsAbsPath = repoRoot.Replace('/', '\\');
            var unixAbsPath = repoRoot.Replace('\\', '/');
            foreach (var dir in sampleDirs)
            {
                var allFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                foreach (var file in allFiles)
                {
                    // Skip only binary asset bundles / DLLs; text-serialized Unity files are fair game
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".dll" || ext == ".exe" || ext == ".so" || ext == ".dylib")
                        continue;
                    try
                    {
                        var content = File.ReadAllText(file);
                        Assert(!content.Contains(windowsAbsPath) && !content.Contains(unixAbsPath),
                            $"Sample {Path.GetFileName(dir)}/{Path.GetRelativePath(dir, file)}: no absolute path");
                    }
                    catch { /* skip truly binary files */ }
                }
            }

            // ── Demo project path hygiene ──
            // The demo project is part of the repository experience, so it also
            // must not serialize one developer's local clone path.
            var demoDir = Path.Combine(repoRoot, "Unity2Foxglove");
            Assert(File.Exists(Path.Combine(demoDir, "README.md")), "Unity2Foxglove README exists");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "README.md"), repoRoot, "Unity2Foxglove/README.md");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Assets"), repoRoot, "Unity2Foxglove/Assets");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Packages"), repoRoot, "Unity2Foxglove/Packages");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Configs"), repoRoot, "Unity2Foxglove/Configs");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Docs"), repoRoot, "Unity2Foxglove/Docs");

            // ── 17D: Layout consistency ──
            Assert(Directory.Exists(configsDir), "Unity2Foxglove/Configs/ exists");
            var configFullLayout = Path.Combine(configsDir, "FoxgloveFullLayout.json");
            Assert(File.Exists(configFullLayout), "Unity2Foxglove/Configs/FoxgloveFullLayout.json exists");
            var sampleFullLayout = Path.Combine(fullDir, "FoxgloveFullLayout.json");
            var configContent = File.ReadAllText(configFullLayout);
            var sampleContent = File.ReadAllText(sampleFullLayout);
            Assert(configContent == sampleContent, "FullLayout.json consistent between Configs and sample");

            Console.WriteLine("Phase 17: All checks passed.");
        }

        static void Assert(bool condition, string description)
        {
            if (condition)
                Console.WriteLine($"[PASS] {description}");
            else
                throw new Exception($"[FAIL] {description}");
        }

        static void ScanNoAbsolutePaths(string path, string repoRoot, string label)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;

            var windowsAbsPath = repoRoot.Replace('/', '\\');
            var unixAbsPath = repoRoot.Replace('\\', '/');
            var files = File.Exists(path)
                ? new[] { path }
                : Directory.GetFiles(path, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".dll" || ext == ".exe" || ext == ".so" || ext == ".dylib")
                    continue;

                try
                {
                    var content = File.ReadAllText(file);
                    var rel = File.Exists(path) ? Path.GetFileName(file) : Path.GetRelativePath(path, file);
                    Assert(!content.Contains(windowsAbsPath) && !content.Contains(unixAbsPath),
                        $"{label}/{rel}: no absolute path");
                }
                catch
                {
                    // Skip files that are not valid text in the current runtime.
                }
            }
        }
    }
}
