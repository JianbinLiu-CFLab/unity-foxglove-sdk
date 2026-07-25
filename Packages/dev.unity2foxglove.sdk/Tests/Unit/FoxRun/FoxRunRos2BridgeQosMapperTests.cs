// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks portable QoS preservation across U2R2 and directional sessions.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunRos2BridgeQosMapperTests
    {
        [Fact]
        public void U2R2CarriesEveryPortableAxisIndependently()
        {
            var qos = new FoxRunResolvedQos(
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                37);

            var header = WriteHeader(qos);
            var wire = header.GetProperty("qos");

            Assert.Equal("sensor_data", wire.GetProperty("profile").GetString());
            Assert.Equal("reliable", wire.GetProperty("reliability").GetString());
            Assert.Equal("transient_local", wire.GetProperty("durability").GetString());
            Assert.Equal("keep_last", wire.GetProperty("history").GetString());
            Assert.Equal(37, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2PreservesSystemDefaultWithoutProfileDowngrade()
        {
            var header = WriteHeader(FoxRunResolvedQos.SystemDefault);
            var wire = header.GetProperty("qos");

            Assert.Equal("system_default", header.GetProperty("profileName").GetString());
            Assert.Equal("system_default", wire.GetProperty("profile").GetString());
            Assert.Equal("system_default", wire.GetProperty("reliability").GetString());
            Assert.Equal("system_default", wire.GetProperty("durability").GetString());
            Assert.Equal("system_default", wire.GetProperty("history").GetString());
            Assert.Equal(0, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2PreservesKeepAllWithoutSynthesizingDepth()
        {
            var qos = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepAll,
                0);

            var wire = WriteHeader(qos).GetProperty("qos");

            Assert.Equal("keep_all", wire.GetProperty("history").GetString());
            Assert.Equal(0, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2RejectsDefaultResolvedQosInsteadOfSerializingFallbackPolicies()
        {
            Assert.Throws<ArgumentException>(() => WriteHeader(default));
        }

        [Fact]
        public void U2R2WireMapperRejectsUnknownProfileInsteadOfDowngradingToDefault()
        {
            var mapper = typeof(Ros2BridgeFrameWriter).GetMethod(
                "ProfileWireValue",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(mapper);

            var invocation = Assert.Throws<TargetInvocationException>(
                () => mapper.Invoke(null, new object[] { (FoxRunQosProfile)99 }));
            Assert.IsType<ArgumentOutOfRangeException>(invocation.InnerException);
        }

        [Fact]
        public void FoxRunBridgeDemandIsIndependentOfLegacyComponentOutputSwitch()
        {
            var bridgeSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.Ros2Bridge.cs");
            var bridgeRoot = CSharpSyntaxTree.ParseText(bridgeSource)
                .GetCompilationUnitRoot();
            var foxRunPrepare = bridgeRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method =>
                    method.Identifier.ValueText
                    == "TryPrepareFoxRunRos2BridgePublish");
            var prepareBody = foxRunPrepare.Body?.ToFullString() ?? string.Empty;

            Assert.DoesNotContain("_ros2BridgeEnabled", prepareBody, StringComparison.Ordinal);
            Assert.Contains(
                "EnsureFoxRunRos2BridgeRuntimeDemand",
                prepareBody,
                StringComparison.Ordinal);
            Assert.Contains(
                "_foxRunRos2BridgeRuntimeDemand = true",
                bridgeSource,
                StringComparison.Ordinal);

            var managerSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            Assert.Contains(
                "if (!_foxRunRos2BridgeRuntimeDemand)",
                managerSource,
                StringComparison.Ordinal);
        }

        [Fact]
        public void FoxRunBridgeDemandStartsOnlyAfterSessionAndContractValidation()
        {
            var bridgeSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.Ros2Bridge.cs");
            var bridgeRoot = CSharpSyntaxTree.ParseText(bridgeSource)
                .GetCompilationUnitRoot();
            var methods = bridgeRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .ToArray();
            var prepare = methods.Single(method =>
                method.Identifier.ValueText == "TryPrepareFoxRunRos2BridgePublish");
            var ensure = methods.Single(method =>
                method.Identifier.ValueText == "EnsureFoxRunRos2BridgeRuntimeDemand");
            var prepareBody = prepare.Body?.ToFullString() ?? string.Empty;
            var ensureBody = ensure.Body?.ToFullString() ?? string.Empty;

            var topicValidation = prepareBody.IndexOf(
                "TryResolveRos2BridgeTopic",
                StringComparison.Ordinal);
            var schemaValidation = prepareBody.IndexOf(
                "IsValidCanonicalRosMessageType",
                StringComparison.Ordinal);
            var qosValidation = prepareBody.IndexOf(
                "IsValidResolvedQos",
                StringComparison.Ordinal);
            var demand = prepareBody.IndexOf(
                "EnsureFoxRunRos2BridgeRuntimeDemand",
                StringComparison.Ordinal);

            Assert.True(topicValidation >= 0 && topicValidation < demand);
            Assert.True(schemaValidation >= 0 && schemaValidation < demand);
            Assert.True(qosValidation >= 0 && qosValidation < demand);
            Assert.Contains(
                "ActiveFoxRunPublishSessionPolicy.SessionActive",
                ensureBody,
                StringComparison.Ordinal);
            Assert.Contains("isActiveAndEnabled", ensureBody, StringComparison.Ordinal);
            Assert.True(
                ensureBody.IndexOf("_ros2BridgeRuntime.Start", StringComparison.Ordinal)
                < ensureBody.IndexOf(
                    "_foxRunRos2BridgeRuntimeDemand = true",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void EndingPublishAlwaysReleasesBridgeDemandEvenWithoutAnActiveSession()
        {
            var publishingSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs");
            var method = CSharpSyntaxTree.ParseText(publishingSource)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(candidate =>
                    candidate.Identifier.ValueText == "EndFoxRunPublishSession");

            Assert.Empty(method.Body?.Statements.OfType<IfStatementSyntax>()
                         ?? Enumerable.Empty<IfStatementSyntax>());
            var terminalTry = Assert.Single(
                method.Body?.Statements.OfType<TryStatementSyntax>()
                ?? Enumerable.Empty<TryStatementSyntax>());
            Assert.Contains(
                "ReleaseFoxRunRos2BridgeRuntimeDemand",
                terminalTry.Finally?.ToFullString() ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void SourceFailureDiagnosticsUseReasonAwareCooldownAndResetAtSessionBoundary()
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .ToArray();
            var logFailure = methods.Single(method =>
                method.Identifier.ValueText == "LogSourceFailure");
            var publishSessionChanged = methods.Single(method =>
                method.Identifier.ValueText == "OnFoxRunPublishSessionChanged");
            var logBody = logFailure.Body?.ToFullString() ?? string.Empty;
            var sessionBody = publishSessionChanged.Body?.ToFullString() ?? string.Empty;

            Assert.Contains(
                "WarningDebouncer.ShouldEmitKeyedCooldown",
                logBody,
                StringComparison.Ordinal);
            Assert.Contains("ex.GetType().FullName", logBody, StringComparison.Ordinal);
            Assert.Contains(
                "_warnedSourceFailures.Clear()",
                sessionBody,
                StringComparison.Ordinal);
        }

        [Fact]
        public void DirectionalSessionsCaptureThreeDifferentQosDefaults()
        {
            var publishState = new FoxRunPublishSessionState();
            var subscribeState = new FoxRunSubscriptionSessionState();
            var nativePublish = FoxRunResolvedQos.SensorData;
            var bridgePublish = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepAll,
                0);
            var nativeSubscribe = FoxRunResolvedQos.SystemDefault;

            var firstPublish = publishState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.Protobuf,
                10f,
                nativePublish,
                bridgePublish);
            var firstSubscribe = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                nativeSubscribe,
                4 * 1024 * 1024,
                120,
                60);

            Assert.Equal(nativePublish, firstPublish.NativeRos2Qos);
            Assert.Equal(bridgePublish, firstPublish.BridgeRos2Qos);
            Assert.Equal(nativeSubscribe, firstSubscribe.DefaultRos2Qos);
            Assert.NotEqual(firstPublish.NativeRos2Qos, firstPublish.BridgeRos2Qos);
            Assert.NotEqual(firstPublish.NativeRos2Qos, firstSubscribe.DefaultRos2Qos);
            Assert.NotEqual(firstPublish.BridgeRos2Qos, firstSubscribe.DefaultRos2Qos);
        }

        [Fact]
        public void ActivePublishSessionIgnoresQosEditsAndRecapturesAfterEnd()
        {
            var publishState = new FoxRunPublishSessionState();
            var initialNative = FoxRunResolvedQos.SensorData;
            var initialBridge = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepAll,
                0);
            var editedNative = FoxRunResolvedQos.Default;
            var editedBridge = FoxRunResolvedQos.SystemDefault;

            var first = publishState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.Protobuf,
                10f,
                initialNative,
                initialBridge);
            var repeated = publishState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.Protobuf,
                10f,
                editedNative,
                editedBridge);

            Assert.Same(first, repeated);
            Assert.Equal(initialNative, repeated.NativeRos2Qos);
            Assert.Equal(initialBridge, repeated.BridgeRos2Qos);

            publishState.End();
            var recaptured = publishState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.Protobuf,
                10f,
                editedNative,
                editedBridge);

            Assert.NotSame(first, recaptured);
            Assert.Equal(editedNative, recaptured.NativeRos2Qos);
            Assert.Equal(editedBridge, recaptured.BridgeRos2Qos);
            Assert.Equal(first.SessionGeneration + 1UL, recaptured.SessionGeneration);
        }

        [Fact]
        public void ManagerBridgeQosUsesOneSnapshotForActiveAccessorAndTryPrepare()
        {
            var assembly = CompileManagerBridgeQosHarness();
            var managerType = assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxgloveManager",
                throwOnError: true);
            var manager = Activator.CreateInstance(managerType);

            Invoke(manager, "ConfigureBridgeProfileForTest", (int)FoxRunQosProfile.SensorData);
            AssertHarnessQos(
                Invoke(manager, "ResolveConfiguredRos2BridgeQosForTest"),
                FoxRunResolvedQos.SensorData);
            AssertHarnessQos(
                Invoke(manager, "ResolveRos2BridgeQos"),
                FoxRunResolvedQos.SensorData);
            AssertHarnessQos(
                GetProperty(manager, "ActiveFoxRunBridgePublishQos"),
                FoxRunResolvedQos.SensorData);
            AssertHarnessQos(TryPrepareBridgeQos(manager), FoxRunResolvedQos.SensorData);

            Invoke(manager, "BeginPublishSessionForTest");
            AssertHarnessQos(
                GetProperty(GetProperty(manager, "ActiveFoxRunPublishSessionPolicy"), "BridgeRos2Qos"),
                FoxRunResolvedQos.SensorData);

            Invoke(manager, "ConfigureBridgeProfileForTest", (int)FoxRunQosProfile.SystemDefault);
            AssertHarnessQos(
                Invoke(manager, "ResolveConfiguredRos2BridgeQosForTest"),
                FoxRunResolvedQos.SystemDefault);
            AssertHarnessQos(
                Invoke(manager, "ResolveRos2BridgeQos"),
                FoxRunResolvedQos.SensorData);
            AssertHarnessQos(
                GetProperty(manager, "ActiveFoxRunBridgePublishQos"),
                FoxRunResolvedQos.SensorData);
            AssertHarnessQos(TryPrepareBridgeQos(manager), FoxRunResolvedQos.SensorData);

            Invoke(manager, "EndPublishSessionForTest");
            AssertHarnessQos(
                Invoke(manager, "ResolveRos2BridgeQos"),
                FoxRunResolvedQos.SystemDefault);
            AssertHarnessQos(
                GetProperty(manager, "ActiveFoxRunBridgePublishQos"),
                FoxRunResolvedQos.SystemDefault);
            AssertHarnessQos(TryPrepareBridgeQos(manager), FoxRunResolvedQos.SystemDefault);

            Invoke(manager, "BeginPublishSessionForTest");
            AssertHarnessQos(
                GetProperty(GetProperty(manager, "ActiveFoxRunPublishSessionPolicy"), "BridgeRos2Qos"),
                FoxRunResolvedQos.SystemDefault);
            AssertHarnessQos(
                Invoke(manager, "ResolveRos2BridgeQos"),
                FoxRunResolvedQos.SystemDefault);
            AssertHarnessQos(
                GetProperty(manager, "ActiveFoxRunBridgePublishQos"),
                FoxRunResolvedQos.SystemDefault);
        }

        [Fact]
        public void BridgeQosConsumersUseActivePublishSessionAccessor()
        {
            var managerPublishing = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.Ros2Bridge.cs");
            var publisherBase = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var managerEditor = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Ros2Bridge.cs");
            var publisherEditors = new[]
            {
                ReadRepoText(
                    "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs"),
                ReadRepoText(
                    "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs"),
                ReadRepoText(
                    "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs"),
            };

            Assert.Contains("qos = ActiveFoxRunBridgePublishQos;", managerPublishing, StringComparison.Ordinal);
            Assert.Contains("_manager.ActiveFoxRunBridgePublishQos", publisherBase, StringComparison.Ordinal);
            Assert.Contains("manager.ActiveFoxRunBridgePublishQos", managerEditor, StringComparison.Ordinal);
            Assert.Contains(
                "disabling and re-enabling the Manager",
                managerEditor,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "restarting the sidecar",
                managerEditor,
                StringComparison.Ordinal);
            Assert.DoesNotContain("qos = ResolveRos2BridgeQos();", managerPublishing, StringComparison.Ordinal);
            Assert.DoesNotContain("_manager.ResolveRos2BridgeQos()", publisherBase, StringComparison.Ordinal);
            Assert.DoesNotContain("manager.ResolveRos2BridgeQos()", managerEditor, StringComparison.Ordinal);
            foreach (var publisherEditor in publisherEditors)
            {
                Assert.Contains(
                    "FoxRunRos2SubscriptionInspectorPresentation.Summary(publisher.EffectiveRos2BridgeQos)",
                    publisherEditor,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "publisher.EffectiveRos2BridgeQos.DisplaySummary",
                    publisherEditor,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ActiveSubscriptionSessionIgnoresQosEditsAndRecapturesAfterReenable()
        {
            var subscribeState = new FoxRunSubscriptionSessionState();
            var initial = FoxRunResolvedQos.SystemDefault;
            var edited = FoxRunResolvedQos.Default;

            var first = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                initial,
                4 * 1024 * 1024,
                120,
                60);
            var repeated = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                edited,
                4 * 1024 * 1024,
                120,
                60);

            Assert.Same(first, repeated);
            Assert.Equal(initial, repeated.DefaultRos2Qos);

            subscribeState.End();
            var recaptured = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                edited,
                4 * 1024 * 1024,
                120,
                60);

            Assert.NotSame(first, recaptured);
            Assert.Equal(edited, recaptured.DefaultRos2Qos);
            Assert.Equal(first.SessionGeneration + 1UL, recaptured.SessionGeneration);
        }

        private static Assembly CompileManagerBridgeQosHarness()
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var sourcePaths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunEndpoint.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunEncoding.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunQosProfile.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunQosReliability.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunQosDurability.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunQosHistory.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunResolvedQos.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunRos2QosProfileResolver.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunQosProfileSettings.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunPublishSessionPolicy.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs",
            };
            var syntaxTrees = sourcePaths
                .Select(path => CSharpSyntaxTree.ParseText(ReadRepoText(path), parseOptions))
                .Concat(new[]
                {
                    ReducedManagerQosResolverTree(parseOptions),
                    ReducedRos2BridgePublishingTree(parseOptions),
                    CSharpSyntaxTree.ParseText(ManagerHarnessSupportSource, parseOptions),
                });

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "Phase184ManagerBridgeQosHarness_" + Guid.NewGuid().ToString("N"),
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            if (!emit.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emit.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException(errors);
            }

            return Assembly.Load(image.ToArray());
        }

        private static SyntaxTree ReducedManagerQosResolverTree(CSharpParseOptions parseOptions)
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var root = CSharpSyntaxTree.ParseText(source, parseOptions)
                .GetCompilationUnitRoot();
            var manager = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "FoxgloveManager");
            var resolverMethods = manager.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "ResolveRos2BridgeQos"
                                 || method.Identifier.ValueText == "ResolveConfiguredRos2BridgeQos")
                .Select(method => method.WithoutTrivia())
                .Cast<MemberDeclarationSyntax>()
                .ToArray();
            Assert.Equal(2, resolverMethods.Length);

            var reducedManager = SyntaxFactory.ClassDeclaration("FoxgloveManager")
                .AddModifiers(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.PartialKeyword))
                .WithMembers(SyntaxFactory.List(resolverMethods));
            var reducedNamespace = SyntaxFactory.NamespaceDeclaration(
                    SyntaxFactory.ParseName("Unity.FoxgloveSDK.Components"))
                .AddMembers(reducedManager);
            var reducedRoot = SyntaxFactory.CompilationUnit()
                .AddMembers(reducedNamespace)
                .NormalizeWhitespace();
            return CSharpSyntaxTree.Create(reducedRoot, parseOptions);
        }

        private static SyntaxTree ReducedRos2BridgePublishingTree(CSharpParseOptions parseOptions)
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.Ros2Bridge.cs");
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
            var root = tree.GetCompilationUnitRoot();
            var manager = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "FoxgloveManager");
            var tryPrepareMethods = manager.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "TryPrepareRos2BridgePublish")
                .Cast<MemberDeclarationSyntax>()
                .ToArray();
            Assert.Equal(2, tryPrepareMethods.Length);

            var reducedManager = manager.WithMembers(
                SyntaxFactory.List(tryPrepareMethods));
            return CSharpSyntaxTree.Create(
                root.ReplaceNode(manager, reducedManager),
                parseOptions);
        }

        private static object TryPrepareBridgeQos(object manager)
        {
            var method = manager.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == "TryPrepareRos2BridgePublish"
                                     && candidate.GetParameters().Length == 6);
            var arguments = new object[]
            {
                "/phase184/qos",
                string.Empty,
                "foxglove_msgs/msg/FrameTransform",
                null,
                null,
                null,
            };

            Assert.False((bool)method.Invoke(manager, arguments));
            Assert.Equal("ROS2 Bridge runtime is unavailable.", arguments[5]);
            Assert.NotNull(arguments[4]);
            return arguments[4];
        }

        private static void AssertHarnessQos(object actual, FoxRunResolvedQos expected)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Profile.ToString(), GetProperty(actual, "Profile").ToString());
            Assert.Equal(expected.Reliability.ToString(), GetProperty(actual, "Reliability").ToString());
            Assert.Equal(expected.Durability.ToString(), GetProperty(actual, "Durability").ToString());
            Assert.Equal(expected.History.ToString(), GetProperty(actual, "History").ToString());
            Assert.Equal(expected.Depth, (int)GetProperty(actual, "Depth"));
        }

        private static object GetProperty(object instance, string propertyName)
        {
            Assert.NotNull(instance);
            var property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            return property.GetValue(instance);
        }

        private static object Invoke(object instance, string methodName, params object[] arguments)
        {
            Assert.NotNull(instance);
            var method = instance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == methodName
                                     && candidate.GetParameters().Length == arguments.Length);
            return method.Invoke(instance, arguments);
        }

        private static string ReadRepoText(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not find repository file " + relativePath + ".");
        }

        private static JsonElement WriteHeader(FoxRunResolvedQos qos)
        {
            var frame = new Ros2BridgeFrame(
                "/phase184/qos",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                1234UL,
                7UL,
                new byte[] { 0, 1, 0, 0 },
                qos);
            var bytes = Ros2BridgeFrameWriter.Write(frame);
            var headerLength = ReadUInt32LittleEndian(bytes, 8);
            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(bytes, 16, checked((int)headerLength)));
            return document.RootElement.Clone();
        }

        private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private const string ManagerHarnessSupportSource = @"
using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HideInInspector : Attribute { }

    public static class Debug
    {
        public static void LogException(Exception exception) { }
    }
}

namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : Attribute
    {
        public FormerlySerializedAsAttribute(string oldName) { }
    }
}

namespace Unity.FoxgloveSDK.Ros2Bridge { }
namespace Unity.FoxgloveSDK.Schemas.Ros2Msg { }

namespace Unity.FoxgloveSDK.Components
{
    public static class FoxRunEndpointResolver
    {
        public static FoxRunEndpoint ValidateProfileTargets(FoxRunEndpoint targets)
            => targets;
    }

    public static class FoxRunEncodingResolver
    {
        public static FoxRunEncoding ValidateProfileDefault(FoxRunEncoding encoding)
            => encoding;
    }

    public partial class FoxgloveManager
    {
        private readonly HarnessConnectionState _connectionState = new HarnessConnectionState();
        private FoxRunQosProfileSettings _ros2BridgeQos = new FoxRunQosProfileSettings();
        private bool _ros2BridgeEnabled = true;
        private object _ros2BridgeRuntime;

        public float DefaultPublishRateHz => 10f;
        public bool SuppressLivePublishersForReplay => false;

        public FoxRunResolvedQos ResolveConfiguredRos2BridgeQosForTest()
            => ResolveConfiguredRos2BridgeQos();

        public void ConfigureBridgeProfileForTest(int profile)
            => _ros2BridgeQos.Profile = (FoxRunQosProfile)profile;

        public void BeginPublishSessionForTest()
            => BeginFoxRunPublishSessionIfNeeded();

        public void EndPublishSessionForTest()
            => EndFoxRunPublishSession();

        private void ReleaseFoxRunRos2BridgeRuntimeDemand() { }

        private static bool TryResolveRos2BridgeTopic(
            string topic,
            string topicOverride,
            out string effectiveTopic,
            out string reason)
        {
            effectiveTopic = string.IsNullOrWhiteSpace(topicOverride)
                ? topic
                : topicOverride;
            reason = string.Empty;
            return true;
        }

        private sealed class HarnessConnectionState
        {
            public string Ros2BridgeSetupError { get; set; } = string.Empty;
        }
    }
}
";
    }
}
