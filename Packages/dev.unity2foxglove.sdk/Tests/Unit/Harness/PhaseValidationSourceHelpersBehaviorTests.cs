// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Behavior coverage for shared Phase validation source helpers.

using System;
using System.IO;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class PhaseValidationSourceHelpersBehaviorTests
    {
        private const string PackageJsonPath = "Packages/dev.unity2foxglove.sdk/package.json";

        [Fact]
        public void RequiredReaderDelegateReturnsExactBytesAndRejectsMissingPaths()
        {
            Func<string, string> reader = PhaseValidationSourceHelpers.ReadRequiredRepoText;
            var root = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var expected = File.ReadAllText(Path.Combine(root, PackageJsonPath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(expected, reader(PackageJsonPath));
            Assert.Throws<FileNotFoundException>(() => reader("Packages/fixture/missing.txt"));
        }

        [Fact]
        public void SharedReaderAndSourceMethodDelegatesReturnBehavioralResults()
        {
            var source = "public class Fixture { public void Target() { } }";
            Func<string, string, string> sourceMethod = PhaseValidationSourceHelpers.SourceMethod;
            Func<string, string, string, bool> sourceMethodContains = PhaseValidationSourceHelpers.SourceMethodContains;
            Func<string, string, string[], string> sourceMethodWithSymbols =
                (text, method, symbols) => PhaseValidationSourceHelpers.SourceMethodWithPreprocessorSymbols(text, method, symbols);
            Assert.Contains("Target", sourceMethod(source, "Target"), StringComparison.Ordinal);
            Assert.True(sourceMethodContains(source, "Target", "Target"));
            Assert.Contains("Target", sourceMethodWithSymbols(source, "Target", new[] { "UNITY_EDITOR" }), StringComparison.Ordinal);

            Func<string> replayReader = PhaseValidationSourceHelpers.ReadReplayControllerSources;
            Func<string> logReader = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources;
            Func<string> editorReader = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources;
            Func<string> bridgeReader = PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources;
            Func<string> serviceReader = PhaseValidationSourceHelpers.ReadFoxgloveServiceHubSources;
            Assert.NotEmpty(replayReader());
            Assert.NotEmpty(logReader());
            Assert.NotEmpty(editorReader());
            Assert.NotEmpty(bridgeReader());
            Assert.NotEmpty(serviceReader());

            Func<string, string> pythonReader = PhaseValidationSourceHelpers.ReadSplitPythonSource;
            Func<string, string> pythonPathReader = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath;
            var pythonPath = Path.Combine(
                PhaseValidationSourceHelpers.FindRequiredRepoRoot(),
                "Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py".Replace('/', Path.DirectorySeparatorChar));
            Assert.NotEmpty(pythonReader("Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py"));
            Assert.NotEmpty(pythonPathReader(pythonPath));

            Func<string, string[], string> registeredReader =
                (facade, fragments) => PhaseValidationSourceHelpers.ReadRegisteredCSharpSource(facade, fragments);
            Assert.NotEmpty(registeredReader(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs",
                new[]
                {
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Dispatch.cs",
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Lifetime.cs",
                }));
        }

        [Fact]
        public void ValidatorReaderIncludesTheFacadeAndRegisteredFragments()
        {
            var source = PhaseValidationSourceHelpers.ReadRegisteredCSharpSource(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModelValidator/ValidationRules.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModelValidator/ValidationTopology.cs");

            Assert.Contains("AddError(diagnostics, \"FOXRUN008\"", source, StringComparison.Ordinal);
            Assert.Contains("private static void ValidateTopicGroups", source, StringComparison.Ordinal);
        }
    }
}
