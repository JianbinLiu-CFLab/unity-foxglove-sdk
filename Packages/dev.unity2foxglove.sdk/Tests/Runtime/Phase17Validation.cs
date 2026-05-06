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
            var configsDir = Path.Combine(repoRoot, "Untiy2Foxglove", "Configs");

            // ── 17A: package.json samples declaration ──
            var pkgJson = File.ReadAllText(Path.Combine(pkgDir, "package.json"));
            var pkg = JObject.Parse(pkgJson);
            Assert(pkg["samples"] != null, "package.json.samples exists");
            var samples = pkg["samples"] as JArray;
            Assert(samples != null && samples.Count == 2, "package.json.samples has 2 entries");

            bool hasBasic = false;
            bool hasFull = false;
            foreach (var s in samples)
            {
                var name = (string)s["displayName"];
                var path = (string)s["path"];
                if (name == "Basic Visualization") hasBasic = true;
                if (name == "Full Demo Visualization") hasFull = true;
                Assert(Directory.Exists(Path.Combine(pkgDir, path)), $"Sample path exists: {path}");
            }
            Assert(hasBasic, "Basic Visualization sample declared");
            Assert(hasFull, "Full Demo Visualization sample declared");

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

            // ── Forbidden items in samples ──
            var forbidden = new[] { "Generated", "TutorialInfo", "Editor", "Plugins", "Library", "Logs", "Recordings" };
            foreach (var sampleDir in new[] { basicDir, fullDir })
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
            var sampleDirs = new[] { basicDir, fullDir };
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
            var demoDir = Path.Combine(repoRoot, "Untiy2Foxglove");
            Assert(File.Exists(Path.Combine(demoDir, "README.md")), "Untiy2Foxglove README exists");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "README.md"), repoRoot, "Untiy2Foxglove/README.md");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Assets"), repoRoot, "Untiy2Foxglove/Assets");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Packages"), repoRoot, "Untiy2Foxglove/Packages");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Configs"), repoRoot, "Untiy2Foxglove/Configs");
            ScanNoAbsolutePaths(Path.Combine(demoDir, "Docs"), repoRoot, "Untiy2Foxglove/Docs");

            // ── 17D: Layout consistency ──
            Assert(Directory.Exists(configsDir), "Untiy2Foxglove/Configs/ exists");
            var configFullLayout = Path.Combine(configsDir, "FoxgloveFullLayout.json");
            Assert(File.Exists(configFullLayout), "Untiy2Foxglove/Configs/FoxgloveFullLayout.json exists");
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
