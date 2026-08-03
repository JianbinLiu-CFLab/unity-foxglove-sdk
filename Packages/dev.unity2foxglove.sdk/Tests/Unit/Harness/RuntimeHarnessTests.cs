// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Runtime validation harness behavior and source-structure checks.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "140-35")]
    [Trait("Domain", "Harness")]
    public class RuntimeHarnessTests
    {
        // A full local CI run intentionally overlaps MCAP conformance with this
        // xUnit lane. Keep the one-time harness build bounded, but allow three
        // times the normal two-minute budget for that legitimate contention.
        private const int HarnessBuildTimeoutMilliseconds = 360_000;
        private static readonly SemaphoreSlim HarnessBuildLock = new SemaphoreSlim(1, 1);
        private static bool _harnessBuilt;

        [Fact]
        public async Task UnknownFlagFailsInsteadOfRunningDefaultSuite()
        {
            var result = await RunHarnessAsync(new[] { "--phase-typo" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("unknown", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ServeRejectsNonIntegerPortWithoutStartingServer()
        {
            var result = await RunHarnessAsync(new[] { "--serve", "--port", "not-a-number" }, timeoutMilliseconds: 10_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--port", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("integer", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ServeIsDisabledInCiEnvironment()
        {
            var result = await RunHarnessAsync(
                new[] { "--serve" },
                timeoutMilliseconds: 10_000,
                environment: new Dictionary<string, string> { ["CI"] = "true" });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--serve", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("CI", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MultipleValidationFlagsFailInsteadOfRunningFirstMatch()
        {
            var result = await RunHarnessAsync(new[] { "--phase1", "--phase2" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Multiple validation flags", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("--phase1", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("--phase2", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Phase91GenerationFailuresWriteFailLineToStderr()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "phase140_35_" + Guid.NewGuid().ToString("N")));
            try
            {
                var result = await RunHarnessAsync(new[] { "--phase91-ros2-cdr-mcap", tempDir.FullName }, timeoutMilliseconds: 20_000);

                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains("[FAIL]", result.StandardError, StringComparison.Ordinal);
                Assert.DoesNotContain("[FAIL]", result.StandardOutput, StringComparison.Ordinal);
            }
            finally
            {
                tempDir.Delete(recursive: true);
            }
        }

        [Fact]
        public void RunTestsDoesNotOwnTempMcapHelperCleanup()
        {
            var method = LoadProgramTree()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "RunTests");

            Assert.DoesNotContain(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => invocation.ToString().Contains("TempMcapHelper.Cleanup()", StringComparison.Ordinal));
        }

        [Fact]
        public void MainCleansTempMcapHelperInFinallyForAllEntrypoints()
        {
            var method = LoadProgramTree()
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "Main");

            var tryStatement = method.DescendantNodes().OfType<TryStatementSyntax>().SingleOrDefault();
            Assert.NotNull(tryStatement);
            Assert.Contains(
                tryStatement!.Finally?.Block.DescendantNodes().OfType<InvocationExpressionSyntax>() ?? Enumerable.Empty<InvocationExpressionSyntax>(),
                invocation => invocation.ToString().Contains("TempMcapHelper.Cleanup()", StringComparison.Ordinal));
        }

        [Fact]
        public void RuntimeServerDisposesTimersWithCallbackDrain()
        {
            var text = LoadRuntimeSource("Program.cs");

            Assert.Contains("Interlocked.Exchange(ref stopping, 1)", text, StringComparison.Ordinal);
            Assert.Contains("DisposeTimerAndWait(heartbeat)", text, StringComparison.Ordinal);
            Assert.Contains("timer.Dispose(disposed)", text, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref stopping)", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DemoFlagsRequireServe()
        {
            var result = await RunHarnessAsync(new[] { "--demo" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--serve", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DemoAndDemo3dCannotRunTogether()
        {
            var result = await RunHarnessAsync(new[] { "--serve", "--demo", "--demo3d" }, timeoutMilliseconds: 10_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("cannot be used together", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ListValidationsIncludesLegacyManualToolFlags()
        {
            var result = await RunHarnessAsync(new[] { "--list-validations" }, timeoutMilliseconds: 20_000);

            Assert.Equal(0, result.ExitCode);
            foreach (var flag in new[]
            {
                "--phase97-health",
                "--phase98-sample-send-all",
                "--phase98-live",
                "--phase99-live",
                "--phase94-bridge-send",
                "--phase91-ros2-cdr-mcap",
                "--phase92-ros2-product-mcap",
                "--phase93-ros2-full-mcap",
                "--phase93-inspect-mcap",
                "--phase68-indexed-reader-smoke",
                "--phase44-all-schemas-mcap",
                "--phase139b-remote-data-loader-server",
            })
            {
                Assert.Contains(flag, result.StandardOutput, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RuntimeGitLsFilesDrainsPipesBeforeTimedWait()
        {
            var text = LoadRuntimeSource("PhaseRos2ForUnityValidationHelpers.cs");

            Assert.Contains("ReadToEndAsync()", text, StringComparison.Ordinal);
            Assert.Contains("GitLsFilesTimeoutMilliseconds", text, StringComparison.Ordinal);
            Assert.Contains("process.Kill(entireProcessTree: true)", text, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeSourceMethodScannerUsesUnambiguousDeclarations()
        {
            var text = LoadRuntimeSource("PhaseValidationSourceHelpers.cs");

            Assert.Contains("SourceDeclaration(source, methodName, IsSourceMethodDeclaration)", text, StringComparison.Ordinal);
            Assert.Contains("declaration.ContainsDiagnostics", text, StringComparison.Ordinal);
            Assert.Contains("matches.Length != 1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("source.IndexOf(methodName", text, StringComparison.Ordinal);
        }

        [Fact]
        public void RegistryChecksDuplicateValidationNames()
        {
            var constructor = LoadRuntimeSyntax("PhaseValidationRegistry.cs")
                .GetRoot()
                .DescendantNodes()
                .OfType<ConstructorDeclarationSyntax>()
                .Single(node => node.Modifiers.Any(SyntaxKind.StaticKeyword));

            Assert.Contains(
                constructor.DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
                access => access.Name.Identifier.ValueText == "Name");
        }

        [Fact]
        public void Phase32UsesNamespaceAndValidateEntryPoint()
        {
            var root = LoadRuntimeSyntax("Phase32Validation.cs").GetRoot();
            var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Phase32Validation");

            Assert.Contains(root.DescendantNodes().OfType<NamespaceDeclarationSyntax>(), ns => ns.Name.ToString() == "Unity.FoxgloveSDK.Tests");
            Assert.Contains(declaration.Modifiers, token => token.IsKind(SyntaxKind.InternalKeyword));
            Assert.Contains(declaration.Modifiers, token => token.IsKind(SyntaxKind.StaticKeyword));
            Assert.Contains(declaration.Members.OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "Validate");
        }

        [Fact]
        public void ServerCancelKeyPressHandlersAreUnregistered()
        {
            var text = LoadRuntimeSource("Program.cs");
            Assert.True(
                CountOccurrences(text, "Console.CancelKeyPress -=") >= 2,
                "Server paths should unregister their CancelKeyPress handlers before returning.");
        }

        [Fact]
        public void RuntimeHelpersAreInternal()
        {
            AssertInternalClass("McapRecordReader.cs", "McapRecordReader");
            AssertInternalClass("FoxgloveProtoSampleFactory.cs", "FoxgloveProtoSample");
            AssertInternalClass("FoxgloveProtoSampleFactory.cs", "FoxgloveProtoSampleFactory");
        }

        [Fact]
        public void RuntimeCompileSurfaceDocumentsUnityDependentGlobs()
        {
            var project = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Runtime",
                "FoxgloveSdk.Tests.csproj"));

            Assert.Contains("Runtime/Sensors wildcard is intentionally broad", project, StringComparison.Ordinal);
            Assert.Contains("Runtime/Schemas/Proto wildcard is intentionally broad", project, StringComparison.Ordinal);
            Assert.Contains("Exclude=\"../../Runtime/Sensors/**/VirtualLidar.cs;", project, StringComparison.Ordinal);
            Assert.Contains("Exclude=\"../../Runtime/Schemas/Proto/**/*Publisher.cs;", project, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeHarnessBuildRestoresBeforeNoRestoreBuild()
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Unit",
                "Harness",
                "RuntimeHarnessTests.cs"));
            var method = source.LastIndexOf(
                "private static async Task EnsureHarnessBuiltAsync",
                StringComparison.Ordinal);
            var restore = source.IndexOf("\"restore\"", method, StringComparison.Ordinal);
            var build = source.IndexOf("\"build\"", method, StringComparison.Ordinal);

            Assert.True(method >= 0);
            Assert.True(restore > method);
            Assert.True(build > restore);
        }

        [Fact]
        public void Phase14013RepoLocatorSupportsGitWorktreeFiles()
        {
            var method = LoadRuntimeSyntax("Phase140_13Validation.cs")
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "FindRepoRoot");
            var source = method.ToString();

            Assert.Contains("Directory.Exists(Path.Combine(dir, \".git\"))", source, StringComparison.Ordinal);
            Assert.Contains("File.Exists(Path.Combine(dir, \".git\"))", source, StringComparison.Ordinal);
        }

        [Fact]
        public void TestSourcesRepoLocatorSupportsGitWorktreeFiles()
        {
            var path = Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Unit",
                "Harness",
                "RuntimeValidationOptimizationTests.cs");
            var method = CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "FindRepoRoot");
            var source = method.ToString();

            Assert.Contains("Directory.Exists(Path.Combine(dir.FullName, \".git\"))", source, StringComparison.Ordinal);
            Assert.Contains("File.Exists(Path.Combine(dir.FullName, \".git\"))", source, StringComparison.Ordinal);
        }

        [Fact]
        public void DescriptorReaderRejectsUnknownPolicy()
        {
            var method = LoadRuntimeSyntax("FoxRunGenerationDescriptorJsonReader.cs")
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "PolicyValue");

            Assert.Contains(
                method.DescendantNodes().OfType<ThrowStatementSyntax>(),
                statement => statement.ToString().Contains("Unknown FoxRun policy", StringComparison.Ordinal));
        }

        private static async Task<ProcessResult> RunHarnessAsync(params string[] args)
            => await RunHarnessAsync(args, timeoutMilliseconds: 20_000);

        private static async Task<ProcessResult> RunHarnessAsync(
            string[] args,
            int timeoutMilliseconds,
            IReadOnlyDictionary<string, string> environment = null)
        {
            var repoRoot = FindRepoRoot();
            var project = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", "FoxgloveSdk.Tests.csproj");
            await EnsureHarnessBuiltAsync(repoRoot, project);
            // The Phase179 lane split deliberately isolates all test outputs. The child
            // harness build has no optional-lane property, so it always uses `default`.
            var harnessDll = Path.Combine(repoRoot, "build", "Tests", "default", "Debug", "net10.0", "FoxgloveSdk.Tests.dll");
            if (!File.Exists(harnessDll))
                throw new FileNotFoundException("Runtime harness build did not produce the expected DLL.", harnessDll);

            return await RunProcessAsync(
                "dotnet",
                repoRoot,
                timeoutMilliseconds,
                new[] { harnessDll }.Concat(args).ToArray(),
                environment);
        }

        private static async Task EnsureHarnessBuiltAsync(string repoRoot, string project)
        {
            await HarnessBuildLock.WaitAsync();
            try
            {
                if (_harnessBuilt)
                    return;

                var restore = await RunProcessAsync(
                    "dotnet",
                    repoRoot,
                    HarnessBuildTimeoutMilliseconds,
                    new[]
                    {
                        "restore",
                        project,
                        "--nologo",
                        "--ignore-failed-sources"
                    });
                if (restore.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Failed to restore runtime harness before CLI tests." + Environment.NewLine +
                        restore.StandardOutput + Environment.NewLine + restore.StandardError);

                var result = await RunProcessAsync(
                    "dotnet",
                    repoRoot,
                    HarnessBuildTimeoutMilliseconds,
                    new[]
                    {
                        "build",
                        project,
                        "--nologo",
                        "--no-restore"
                    });
                if (result.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Failed to build runtime harness before CLI tests." + Environment.NewLine +
                        result.StandardOutput + Environment.NewLine + result.StandardError);

                _harnessBuilt = true;
            }
            finally
            {
                HarnessBuildLock.Release();
            }
        }

        private static async Task<ProcessResult> RunProcessAsync(
            string fileName,
            string workingDirectory,
            int timeoutMilliseconds,
            string[] args,
            IReadOnlyDictionary<string, string> environment = null)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);
            if (environment != null)
            {
                foreach (var pair in environment)
                    startInfo.Environment[pair.Key] = pair.Value;
            }

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start runtime harness process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Runtime harness process did not exit before timeout.");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        private static SyntaxTree LoadProgramTree()
            => LoadRuntimeSyntax("Program.cs");

        private static SyntaxTree LoadRuntimeSyntax(string fileName)
            => CSharpSyntaxTree.ParseText(LoadRuntimeSource(fileName));

        private static string LoadRuntimeSource(string fileName)
        {
            var path = Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Runtime",
                fileName);
            Assert.True(File.Exists(path), $"Runtime source file not found: {path}");
            return File.ReadAllText(path);
        }

        private static void AssertInternalClass(string fileName, string className)
        {
            var declarations = LoadRuntimeSyntax(fileName)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(node => node.Identifier.ValueText == className)
                .ToList();
            Assert.Single(declarations);

            var declaration = declarations[0];
            Assert.Contains(declaration.Modifiers, token => token.IsKind(SyntaxKind.InternalKeyword));
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
        }
    }
}
