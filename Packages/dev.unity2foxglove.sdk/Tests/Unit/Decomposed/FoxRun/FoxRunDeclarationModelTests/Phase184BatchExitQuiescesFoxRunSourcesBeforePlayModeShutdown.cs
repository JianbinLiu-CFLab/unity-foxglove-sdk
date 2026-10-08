// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchExitQuiescesFoxRunSourcesBeforePlayModeShutdown()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var scheduleExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void SchedulePlayModeExit()");

            Assert.Contains(
                "PHASE184G_BATCH_SOURCES_QUIESCED",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "route.isActiveAndEnabled",
                source,
                StringComparison.Ordinal);
            var exitScheduleIndex = scheduleExit.IndexOf(
                "EditorApplication.delayCall += ExitPlayModeNow;",
                StringComparison.Ordinal);
            var quiesceIndex = scheduleExit.IndexOf(
                "QuiesceAcceptanceSources();",
                StringComparison.Ordinal);
            Assert.True(exitScheduleIndex >= 0);
            Assert.True(quiesceIndex >= 0);
            Assert.True(exitScheduleIndex < quiesceIndex);
            Assert.Contains(
                "catch (Exception exception)",
                scheduleExit,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchProbeRestoresDeadlinesAndKeepsExitIdempotentAcrossReload()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var attach =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void AttachHandlers()");
            var open =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void OpenSceneAndEnterPlayMode()");
            var retry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RetryCanceledPlayEntry()");
            var requestEditorExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RequestEditorExit(int exitCode, string outcome)");
            var queueRetry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void QueuePlayEntryRetry(string reason)");
            var workerResults =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static bool AllRequiredWorkerResultsReady()");

            Assert.Contains("RestoreRunState();", attach, StringComparison.Ordinal);
            Assert.Contains(
                "PersistTime(\"started-at\", value);",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.SetBool(SessionKey(\"terminal-pass-observed\")",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.GetBool(SessionKey(\"exit-requested\"), false)",
                open,
                StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", open, StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", retry, StringComparison.Ordinal);
            Assert.Contains("_editorExitQueued", requestEditorExit, StringComparison.Ordinal);
            Assert.Contains(
                "SessionKey(\"exit-code\")",
                requestEditorExit,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SessionState.SetBool(SessionKey(\"play-entry-retry-queued\"), true);",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "SchedulePlayEntryAttempt();",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "_requiredWorkerResultPaths.Length == 0",
                workerResults,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchProbeRequiresTokenBoundariesOnBothSides()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var matcher =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static bool HasExactRunToken(string condition)");

            Assert.Contains(
                "index > 0 && !char.IsWhiteSpace(condition[index - 1])",
                matcher,
                StringComparison.Ordinal);
            Assert.Contains(
                "end == condition.Length || char.IsWhiteSpace(condition[end])",
                matcher,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void BatchNativeAcceptanceRetriesPlayCanceledBeforeEditModeTransition()
        {
            foreach (var path in new[]
                     {
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase181BatchModeCustomRos2InteropProbe.cs",
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase184BatchModeProfileProbe.cs",
                     })
            {
                var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(path);
                Assert.Contains(
                    "SessionState.SetBool(SessionKey(\"play-entry-pending\"), true);",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "RetryCanceledPlayEntry",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "SessionState.GetBool(SessionKey(\"play-entry-pending\"), false)",
                    source,
                    StringComparison.Ordinal);
            }
        }



        [Theory]
        [Trait("Phase", "184-E")]
        [InlineData(1, "field", false, 0L)]
        [InlineData(3, "field", false, 0L)]
        [InlineData(2, "property", false, 0L)]
        [InlineData(2, "field", true, 0L)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.PublishTransportIds)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Policy)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Hz)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Tolerance)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.OnlyIf)]
        public void ReflectionLowererRejectsIllegalStreamDeclarationShapes(
            int mode,
            string memberKind,
            bool isAggregateMember,
            long namedArgumentPresence)
        {
            var model = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Streams",
                    "_stream",
                    memberKind,
                    "System.Int32",
                    "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/stream",
                    schemaName: "",
                    hz: -1f,
                    policy: 0,
                    tolerance: 0f,
                    rawMemberOrder: 0,
                    conditionalSymbols: "",
                    isAggregateMember: isAggregateMember,
                    mode: mode,
                    namedArgumentPresence:
                        (FoxRunNamedArgumentPresence)namedArgumentPresence,
                    isStream: true)
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN215");
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void ReflectionLowererAcceptsValidSubscribeStreamFieldShape()
        {
            var model = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Streams",
                    "_stream",
                    "field",
                    "System.Int32",
                    "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/stream",
                    schemaName: "",
                    hz: -1f,
                    policy: 0,
                    tolerance: 0f,
                    rawMemberOrder: 0,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.Subscribe,
                    isStream: true)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN215");
        }

        [Fact]
        [Trait("Phase", "184-F")]
        public void ControlledTestLogPhysicalFallbackMatchesRoslynEmitter()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");
            var messagePackSource = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.MessagePack.cs");
            var result = RunGenerator(source, messagePackSource);
            var core = result.Results
                .Single()
                .GeneratedSources
                .Single(generated => generated.HintName == "TestLog_FoxRun.g.cs")
                .SourceText
                .ToString();
            var expected = string.Join(
                    "\n",
                    "// <auto-generated/>",
                    "// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.",
                    "// SPDX-License-Identifier: Apache-2.0",
                    "// " + FoxRunGeneratedSourceReconciler.GeneratedSourceSentinel,
                    "// In the Unity Editor, the Roslyn analyzer already generates this partial type in memory.",
                    "#if !UNITY_EDITOR")
                + "\n"
                + NormalizeGeneratedSource(core).TrimEnd()
                + "\n#endif";
            var actual = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");

            Assert.Equal(expected, NormalizeGeneratedSource(actual).TrimEnd());
        }

        [Theory]
        [Trait("Phase", "184-E")]
        [InlineData("private FoxRunStream<int> _stream;")]
        [InlineData("private FoxRunStream<int> _stream = null;")]
        [InlineData("private FoxRunStream<int> _stream = default;")]
        public void RoslynRejectsStreamWithoutNonNullFieldInitializer(string declaration)
        {
            var result = RunGenerator($@"
using Unity.FoxgloveSDK.Components;
namespace Demo
{{
    public partial class Streams
    {{
        [FoxRun(""/stream"", Mode = FoxRunFlow.Subscribe)]
        {declaration}
    }}
}}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN216");
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void RoslynRejectsMultipleFoxRunAttributesOnOneStream()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public partial class Streams
    {
        [FoxRun(""/one"", Mode = FoxRunFlow.Subscribe)]
        [FoxRun(""/two"", Mode = FoxRunFlow.Subscribe)]
        private FoxRunStream<int> _stream = new FoxRunStream<int>();
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN215");
        }

        private sealed class Phase184TransportClientMarkerStateProbe
        {
            private const BindingFlags InstanceMembers =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            private readonly object _state;
            private readonly MethodInfo _observe;
            private readonly MethodInfo _resetPending;
            private readonly MethodInfo _reset;
            private readonly PropertyInfo _isOverflowed;
            private readonly PropertyInfo _kind;
            private readonly PropertyInfo _active;
            private readonly PropertyInfo _accepted;

            internal Phase184TransportClientMarkerStateProbe(
                string acceptanceSource,
                int maximumMarkerCount)
            {
                var syntaxRoot = CSharpSyntaxTree.ParseText(acceptanceSource).GetRoot();
                var declarations = new[]
                    {
                        "Phase184TransportClientMarkerKind",
                        "Phase184TransportClientMarkerDecision",
                        "Phase184TransportClientMarkerState",
                    }
                    .Select(name => syntaxRoot.DescendantNodes()
                        .OfType<BaseTypeDeclarationSyntax>()
                        .Single(type => type.Identifier.ValueText == name)
                        .NormalizeWhitespace()
                        .ToFullString());
                var isolatedSource =
                    "using System;\n"
                    + "namespace Unity2Foxglove.ManualAcceptance\n{\n"
                    + string.Join(Environment.NewLine, declarations)
                    + "\n}";
                var trustedAssemblies =
                    AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                    ?? string.Empty;
                var references = trustedAssemblies
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => MetadataReference.CreateFromFile(path));
                var compilation = CSharpCompilation.Create(
                    "Phase184TransportMarkerProbe_" + Guid.NewGuid().ToString("N"),
                    new[] { CSharpSyntaxTree.ParseText(isolatedSource) },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var image = new MemoryStream();
                var emit = compilation.Emit(image);
                Assert.True(
                    emit.Success,
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
                var assembly = Assembly.Load(image.ToArray());
                const string typePrefix =
                    "Unity2Foxglove.ManualAcceptance.";
                KindType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerKind",
                    throwOnError: true);
                DecisionType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerDecision",
                    throwOnError: true);
                var stateType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerState",
                    throwOnError: true);
                var constructor = stateType.GetConstructor(
                    InstanceMembers,
                    binder: null,
                    new[] { typeof(int) },
                    modifiers: null);
                Assert.NotNull(constructor);
                _state = constructor.Invoke(new object[] { maximumMarkerCount });
                _observe = RequiredMethod(stateType, "Observe");
                _resetPending = RequiredMethod(stateType, "ResetPending");
                _reset = RequiredMethod(stateType, "Reset");
                _isOverflowed = RequiredProperty(stateType, "IsOverflowed");
                _kind = RequiredProperty(DecisionType, "Kind");
                _active = RequiredProperty(DecisionType, "ActiveClientCount");
                _accepted = RequiredProperty(DecisionType, "TotalAcceptedClients");
            }

            internal Type KindType { get; }
            internal Type DecisionType { get; }

            internal bool IsOverflowed =>
                Assert.IsType<bool>(_isOverflowed.GetValue(_state));

            internal (string Kind, int Active, long Accepted) Observe(
                int active,
                long accepted)
            {
                var decision = _observe.Invoke(_state, new object[] { active, accepted });
                Assert.NotNull(decision);
                var kind = _kind.GetValue(decision);
                Assert.NotNull(kind);
                return (
                    kind.ToString(),
                    Assert.IsType<int>(_active.GetValue(decision)),
                    Assert.IsType<long>(_accepted.GetValue(decision)));
            }

            internal void ResetPending()
                => _resetPending.Invoke(_state, Array.Empty<object>());

            internal void Reset()
                => _reset.Invoke(_state, Array.Empty<object>());

            private static MethodInfo RequiredMethod(Type type, string name)
            {
                var method = type.GetMethod(name, InstanceMembers);
                Assert.NotNull(method);
                return method;
            }

            private static PropertyInfo RequiredProperty(Type type, string name)
            {
                var property = type.GetProperty(name, InstanceMembers);
                Assert.NotNull(property);
                return property;
            }
        }

    }
}
