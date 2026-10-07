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
using System.Reflection;
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
    public partial class RuntimeHarnessTests
    {
        // A full local CI run intentionally overlaps MCAP conformance with this
        // xUnit lane. Keep the one-time harness build bounded, but allow three
        // times the normal two-minute budget for that legitimate contention.
        private const int HarnessBuildTimeoutMilliseconds = 360_000;
        private static readonly SemaphoreSlim HarnessBuildLock = new SemaphoreSlim(1, 1);
        private static bool _harnessBuilt;

#if !UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public async Task FoxgloveSessionCompositionHasValidSyntaxAndContainsAllMethods()
        {
            var repoRoot = FindRepoRoot();
            var project = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", "FoxgloveSdk.Tests.csproj");
            await EnsureHarnessBuiltAsync(repoRoot, project);
            var assembly = Assembly.LoadFrom(Path.Combine(
                repoRoot, "build", "Tests", "default", "Debug", "net10.0", "FoxgloveSdk.Tests.dll"));
            var helper = assembly.GetType("Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers", throwOnError: true);
            var source = (string)helper.GetMethod("ReadFoxgloveSessionSources").Invoke(null, null);
            var syntax = CSharpSyntaxTree.ParseText(source);

            Assert.DoesNotContain(
                syntax.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var session = Assert.Single(
                syntax.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>(),
                declaration => declaration.Identifier.ValueText == "FoxgloveSession");
            var actual = session.Members.OfType<MethodDeclarationSyntax>()
                .Select(method => method.WithoutTrivia().NormalizeWhitespace().ToFullString()).ToArray();
            var sessionRoot = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Runtime", "Core", "Session");
            var expected = new[] { Path.Combine(sessionRoot, "FoxgloveSession.cs") }
                .Concat(Directory.GetFiles(Path.Combine(sessionRoot, "Decomposed", "FoxgloveSession"), "*.cs"))
                .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
                    .DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Where(declaration => declaration.Identifier.ValueText == "FoxgloveSession")
                    .SelectMany(declaration => declaration.Members.OfType<MethodDeclarationSyntax>()))
                .Select(method => method.WithoutTrivia().NormalizeWhitespace().ToFullString()).ToArray();
            Assert.Equal(expected.OrderBy(method => method, StringComparer.Ordinal),
                actual.OrderBy(method => method, StringComparer.Ordinal));
            Assert.Contains(session.Members.OfType<MethodDeclarationSyntax>(),
                method => method.Identifier.ValueText == "OnClientBinary");
        }
#endif

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
            Assert.True(
                result.StandardError.Contains("integer", StringComparison.OrdinalIgnoreCase)
                || result.StandardError.Contains("cannot be combined", StringComparison.OrdinalIgnoreCase),
                result.StandardError);
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
        public async Task RegisteredValidationRejectsUnknownTokenBeforeDelegate()
        {
            var result = await RunHarnessAsync(new[] { "--phase163-57", "--phase-typo" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Unexpected validation argument", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("checks passed", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RegisteredValidationRejectsDuplicateSelector()
        {
            var result = await RunHarnessAsync(new[] { "--phase163-57", "--phase163-57" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("exactly once", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ListValidationsRejectsConflictingArguments()
        {
            var result = await RunHarnessAsync(new[] { "--list-validations", "--phase163-57" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("cannot be combined", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RunValidationRejectsEmptyDelegateBodies()
        {
            var source = LoadProgramTree().GetText().ToString();

            Assert.Contains("validation delegate has no executable body", source, StringComparison.Ordinal);
            Assert.Contains("GetILAsByteArray", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RunValidationRestoresConsoleStateWithIndependentCleanup()
        {
            var source = LoadProgramTree().GetText().ToString();

            Assert.Contains("TryCleanup(() => classifiedOut.Flush()", source, StringComparison.Ordinal);
            Assert.Contains("TryCleanup(() => classifiedError.Flush()", source, StringComparison.Ordinal);
            Assert.Contains("originalOut.NewLine = originalOutNewLine", source, StringComparison.Ordinal);
            Assert.Contains("originalError.NewLine = originalErrorNewLine", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ValidationRegistryPublishesReadOnlyBackingStorage()
        {
            var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", "PhaseValidationRegistry.cs"));

            Assert.Contains("Array.AsReadOnly(new[]", source, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ServeRejectsConflictingValidationSelector()
        {
            var result = await RunHarnessAsync(new[] { "--serve", "--phase163-57" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("cannot be combined", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ServeRejectsMissingPortValue()
        {
            var result = await RunHarnessAsync(new[] { "--serve", "--port" }, timeoutMilliseconds: 20_000);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--port", result.StandardError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TimedOutHarnessReportsOutputAfterDrainingKilledProcess()
        {
            var environment = new Dictionary<string, string>
            {
                ["CI"] = "false",
                ["GITHUB_ACTIONS"] = "false",
                ["TF_BUILD"] = "false",
                ["BUILD_BUILDID"] = ""
            };

            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                RunHarnessAsync(
                    new[] { "--serve", "--port", "0" },
                    timeoutMilliseconds: 1_000,
                    environment: environment));

            Assert.Contains("Standard output:", error.Message, StringComparison.Ordinal);
            Assert.Contains("Standard error:", error.Message, StringComparison.Ordinal);
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
        public void RuntimeSourceMethodScannerFailsClosedOnAmbiguousDeclarations()
        {
            var text = LoadRuntimeSource("PhaseValidationSourceHelpers.cs");
            var methods = LoadRuntimeSyntax("PhaseValidationSourceHelpers.cs")
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>();
            var tryMethod = methods.Single(
                node => node.Identifier.ValueText == "TrySourceMethod");
            var invocation = Assert.IsType<InvocationExpressionSyntax>(
                tryMethod.ExpressionBody?.Expression);
            Assert.Equal("SourceDeclaration", invocation.Expression.ToString());
            Assert.Collection(
                invocation.ArgumentList.Arguments,
                argument => Assert.Equal("source", argument.Expression.ToString()),
                argument => Assert.Equal("methodName", argument.Expression.ToString()),
                argument => Assert.Equal("IsSourceMethodDeclaration", argument.Expression.ToString()),
                argument => Assert.Equal("CSharpParseOptions.Default", argument.Expression.ToString()));

            var requiredMethod = methods.Single(
                node => node.Identifier.ValueText == "SourceMethod");
            Assert.Contains(
                requiredMethod.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                node => node.Expression.ToString() == "TrySourceMethod");
            Assert.Contains(
                requiredMethod.DescendantNodes().OfType<ThrowStatementSyntax>(),
                node => node.Expression?.ToString().StartsWith(
                    "new InvalidOperationException(",
                    StringComparison.Ordinal) == true);

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


    }
}
