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

namespace Unity.FoxgloveSDK.UnitTests
{


public partial class RuntimeHarnessTests
    {
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
                try { process.WaitForExit(5_000); } catch { }

                var drain = Task.WhenAll(stdoutTask, stderrTask);
                var drained = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5)));
                if (!ReferenceEquals(drained, drain))
                {
                    ObserveFault(stdoutTask);
                    ObserveFault(stderrTask);
                }
                else
                {
                    try { await drain; } catch { }
                }

                var standardOutput = stdoutTask.Status == TaskStatus.RanToCompletion
                    ? stdoutTask.Result
                    : string.Empty;
                var standardError = stderrTask.Status == TaskStatus.RanToCompletion
                    ? stderrTask.Result
                    : string.Empty;
                throw new TimeoutException(
                    "Runtime harness process did not exit before timeout." + Environment.NewLine +
                    "Standard output:" + Environment.NewLine + standardOutput + Environment.NewLine +
                    "Standard error:" + Environment.NewLine + standardError);
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
