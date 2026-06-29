using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_37Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-37 Tests ---");
            _passed = 0;

            VerifyUnitySampleAssetTestsCacheRepoRoot();
            VerifyPhase17ScansOnlyTextCandidates();
            VerifyPhase37ReusesFixedPayloads();
            VerifyPhase16AvoidsPassingPathSortsAndUnneededReplace();
            VerifyRegistry();

            Console.WriteLine("Phase 164-37: " + _passed + " checks passed.\n");
        }

        private static void VerifyUnitySampleAssetTestsCacheRepoRoot()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoSamplesAssetsTests.cs");
            Check(source.Contains("private static readonly Lazy<string> RepoRoot", StringComparison.Ordinal),
                "164-37A-1: Unity demo sample asset tests cache repo-root discovery");
            Check(source.Contains("RepoRoot.Value", StringComparison.Ordinal),
                "164-37A-2: Unity demo sample asset path helper uses cached repo root");
        }

        private static void VerifyPhase17ScansOnlyTextCandidates()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");
            var method = MethodBody(source, "static bool ShouldScanForAbsolutePaths(string file)");

            Check(source.Contains("AbsolutePathTextExtensions", StringComparison.Ordinal)
                  && method.Contains("AbsolutePathTextExtensions.Contains(ext)", StringComparison.Ordinal),
                "164-37B-1: Phase17 absolute-path scan uses a text-extension allowlist");
            Check(source.Contains("Directory.EnumerateFiles(dir, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal)
                  && source.Contains("Directory.EnumerateFiles(path, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal),
                "164-37B-2: Phase17 absolute-path scan streams file enumeration");
            Check(!source.Contains("var allFiles = Directory.GetFiles(dir, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal),
                "164-37B-3: Phase17 no longer materializes every sample file before filtering");
        }

        private static void VerifyPhase37ReusesFixedPayloads()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase37Validation.cs");
            var chunkMethod = MethodBody(source, "private static byte[][] BuildChunkPayloads()");
            var smallChunks = MethodBody(source, "private static void VerifySmallChunksRemainReadable()");

            Check(source.Contains("private static readonly byte[] EmptyJsonPayload", StringComparison.Ordinal)
                  && source.Contains("private static readonly byte[] SeriesPayload0", StringComparison.Ordinal),
                "164-37C-1: Phase37 reuses fixed JSON payload byte arrays");
            Check(source.Contains("private static readonly byte[][] ChunkPayloads = BuildChunkPayloads();", StringComparison.Ordinal)
                  && smallChunks.Contains("ChunkPayloads[i]", StringComparison.Ordinal),
                "164-37C-2: Phase37 prebuilds small chunk payloads instead of formatting inside the write loop");
            Check(!smallChunks.Contains("Encoding.UTF8.GetBytes($", StringComparison.Ordinal)
                  && chunkMethod.Contains("\"{\\\"i\\\":\" + i + \"}\"", StringComparison.Ordinal),
                "164-37C-3: Phase37 avoids per-iteration interpolated payload strings in the hot loop");
        }

        private static void VerifyPhase16AvoidsPassingPathSortsAndUnneededReplace()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");
            var packageOutputs = MethodBody(source, "static void ValidatePackageBuildOutputsAbsent(string repoRoot)");
            var pythonDocs = MethodBody(source, "static void ValidatePythonDocstrings(string repoRoot)");
            var workflowRefs = MethodBody(source, "static void ValidateWorkflowScriptReferences(string repoRoot)");

            Check(packageOutputs.Contains("if (leakedDirectories.Length > 0)", StringComparison.Ordinal)
                  && packageOutputs.Contains("Array.Sort(leakedDirectories, StringComparer.Ordinal);", StringComparison.Ordinal),
                "164-37D-1: Phase16 sorts package build-output diagnostics only on failure");
            Check(pythonDocs.Contains("Directory.EnumerateFiles(scriptsDir, \"*.py\", SearchOption.AllDirectories)", StringComparison.Ordinal)
                  && !pythonDocs.Contains(".OrderBy(path => path", StringComparison.Ordinal),
                "164-37D-2: Phase16 streams Python docstring validation without sorting all files");
            Check(workflowRefs.Contains("if (text.Contains('\\\\'))", StringComparison.Ordinal)
                  && workflowRefs.Contains("text = text.Replace('\\\\', '/');", StringComparison.Ordinal),
                "164-37D-3: Phase16 normalizes workflow slashes only when needed");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-37\"", StringComparison.Ordinal), "164-37E-1: validation registry exposes Phase164-37");
            Check(project.Contains("Phase164_37Validation.cs", StringComparison.Ordinal), "164-37E-2: runtime validation project compiles Phase164-37");
        }

        private static string MethodBody(string source, string signature)
        {
            var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
            if (signatureStart < 0)
                throw new Exception("[FAIL] missing method signature: " + signature);

            var bodyStart = source.IndexOf('{', signatureStart);
            if (bodyStart < 0)
                throw new Exception("[FAIL] missing method body: " + signature);

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            throw new Exception("[FAIL] unterminated method body: " + signature);
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
