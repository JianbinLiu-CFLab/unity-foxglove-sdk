// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins the focused Phase179 optional compilation lanes and source-only R2FU stubs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-B")]
    [Trait("Domain", "OptionalCompilation")]
    public sealed partial class FoxRunRos2OptionalCompilationTests
    {
        private const string StubPath =
            "Packages/dev.unity2foxglove.sdk/Tests/NativeCompileStubs/Ros2ForUnityNativeCompileStubs.cs";

        [Fact]
        public void NativeLaneIsFocusedDefinedAndNonVacuous()
        {
            var props = Text("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");
            var unitProject = Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj");
            var runtimeProject = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            foreach (var project in new[] { unitProject, runtimeProject })
            {
                Assert.Contains("IncludeRos2ForUnityNative", project, StringComparison.Ordinal);
                Assert.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", project, StringComparison.Ordinal);
                Assert.Contains("Ros2ForUnityNativeCompileStubs.cs", project, StringComparison.Ordinal);
                Assert.Contains("ValidatePhase179NativeCompileSurface", project, StringComparison.Ordinal);
                Assert.Contains("Phase179OptionalCompilationLane", project, StringComparison.Ordinal);
                Assert.Contains("<OutputPath>", project, StringComparison.Ordinal);
                Assert.Contains("<IntermediateOutputPath>", project, StringComparison.Ordinal);
                Assert.Contains("Unity2Foxglove.Ros2ForUnity.Native</AssemblyName>", project, StringComparison.Ordinal);
            }

            Assert.Contains("Compile Remove=", props, StringComparison.Ordinal);
            Assert.Contains("/Runtime/Native/**/*.cs", props.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("/Runtime/Native/FoxRun/**/*.cs", props.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityNativeBridgeLifecycleGate.cs", props, StringComparison.Ordinal);
            Assert.Contains("IFoxRun*.cs", props, StringComparison.Ordinal);
            Assert.Contains("IFoxRun*.cs", runtimeProject, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "<Compile Include=\"../NativeCompileStubs/**/*.cs\"",
                unitProject,
                StringComparison.Ordinal);
        }

        [Fact]
        public void AdapterLaneIncludesR2fuGenerationModelsWithoutNativeRuntime()
        {
            var props = Text("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props")
                .Replace('\\', '/');
            const string editorModels =
                "../../dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/**/*.cs";
            var include = props.IndexOf(editorModels, StringComparison.Ordinal);

            Assert.True(include >= 0);
            var elementEnd = props.IndexOf("/>", include, StringComparison.Ordinal);
            Assert.True(elementEnd > include);
            var element = props.Substring(include, elementEnd - include);
            Assert.Contains("IncludeRos2ForUnityAdapter", element, StringComparison.Ordinal);
            Assert.Contains("IncludeRos2ForUnityNative", element, StringComparison.Ordinal);
        }

        [Fact]
        public void CompileOnlyR2fuStubsMatchAllPackagedSourceSignatures()
        {
            var expected = RelevantSignatures(Text(StubPath));
            Assert.NotEmpty(expected);
            Assert.Contains(
                "ROS2Node.ctor[internal](string:unityROS2NodeName=DefaultNodeName)",
                expected);
            Assert.DoesNotContain(
                expected,
                signature => string.Equals(
                    signature,
                    "ROS2Node.ctor[public]()",
                    StringComparison.Ordinal));

            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var scripts = "Packages/dev.unity2foxglove.ros2forunity.runtime."
                              + distro + ".win64/Runtime/Ros2ForUnity/Scripts/";
                var actual = RelevantSignatures(
                    Text(scripts + "ROS2UnityComponent.cs")
                    + Environment.NewLine
                    + Text(scripts + "ROS2Node.cs"));
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void CoreProjectsDoNotReferenceConcreteRos2Assemblies()
        {
            foreach (var path in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef",
                         "Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef",
                         "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
                     })
            {
                var source = Text(path);
                Assert.DoesNotContain("ros2cs_common", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ros2cs_core", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("_msgs_assembly", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Unity2Foxglove.Ros2ForUnity", source, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void CustomInterfaceDefineRequiresTheVerified181CSelectionAndSettledReload()
        {
            var defineInstaller = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var preflight = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportPreflight.cs");

            Assert.Contains("GetActiveCustomTypesupportSelection", defineInstaller, StringComparison.Ordinal);
            Assert.Contains("if (customTypesupport?.IsReady == true)", defineInstaller, StringComparison.Ordinal);
            Assert.Contains(
                "EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol)",
                defineInstaller,
                StringComparison.Ordinal);
            Assert.Contains(
                "RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol)",
                defineInstaller,
                StringComparison.Ordinal);
            Assert.Contains(
                "input.Selection == null || !input.Selection.IsReady",
                preflight,
                StringComparison.Ordinal);
            Assert.Contains(
                "!input.EditorReloadSettled || !input.CustomCompileSymbolDefined",
                preflight,
                StringComparison.Ordinal);
        }

        [Fact]
        public void PlayModeGuardDoesNotLockAfterAnEarlierEntryHandlerCancelsPlay()
        {
            var source = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/"
                + "Ros2ForUnityRuntimePlayModeGuard.cs");
            const string methodMarker = "private static void OnExitingEditMode()";
            var methodStart = source.IndexOf(methodMarker, StringComparison.Ordinal);
            var methodEnd = source.IndexOf(
                "private static bool TryGetMissingZenohRouterDiagnostic(",
                methodStart,
                StringComparison.Ordinal);

            Assert.True(methodStart >= 0 && methodEnd > methodStart);
            var method = source.Substring(methodStart, methodEnd - methodStart);
            var canceledGuard = method.IndexOf(
                "if (!EditorApplication.isPlayingOrWillChangePlaymode)",
                StringComparison.Ordinal);
            var nativeDemandScan = method.IndexOf(
                "InvalidateNativeDemandCache();",
                StringComparison.Ordinal);

            Assert.True(canceledGuard >= 0 && canceledGuard < nativeDemandScan);
            Assert.Contains("ScheduleReloadAssembliesUnlock();", method, StringComparison.Ordinal);
        }

        [Fact]
        public void PlayModeGuardStopsFoxRunEndpointsBeforeSharedRosShutdown()
        {
            var source = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/"
                + "Ros2ForUnityRuntimePlayModeGuard.cs");
            const string methodMarker =
                "private static void RequestNativeRuntimeShutdownBeforeReload(string reason)";
            var methodStart = source.IndexOf(methodMarker, StringComparison.Ordinal);
            var methodEnd = source.IndexOf(
                "private static bool TryInvokeStatic(",
                methodStart,
                StringComparison.Ordinal);

            Assert.True(methodStart >= 0 && methodEnd > methodStart);
            var method = source.Substring(methodStart, methodEnd - methodStart);
            var subscriptionStop = method.IndexOf(
                "FoxRunRos2SubscriptionHubTypeName",
                StringComparison.Ordinal);
            var publisherStop = method.IndexOf(
                "FoxRunRos2CustomPublisherHubTypeName",
                StringComparison.Ordinal);
            var executorStop = method.IndexOf(
                "\"StopAllExecutorsForRosShutdown\"",
                StringComparison.Ordinal);
            var sharedShutdown = method.IndexOf(
                "\"ShutdownShared\"",
                StringComparison.Ordinal);

            Assert.True(subscriptionStop >= 0);
            Assert.True(publisherStop > subscriptionStop);
            Assert.True(executorStop > publisherStop);
            Assert.True(sharedShutdown > executorStop);
            Assert.Contains(
                "\"StopForNativeRuntimeShutdown\"",
                method,
                StringComparison.Ordinal);
        }

        [Fact]
        public void CustomPublisherHubStopsOnSynchronousPublishSessionEnd()
        {
            var hub = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/"
                + "FoxRunRos2CustomPublisherHub.cs");
            var updateStart = hub.IndexOf(
                "private void Update()",
                StringComparison.Ordinal);
            var lifecycleRead = updateStart < 0
                ? -1
                : hub.IndexOf(
                    "IsShuttingDownForBridge(",
                    updateStart,
                    StringComparison.Ordinal);
            var lifecycleRefresh = lifecycleRead < 0
                ? -1
                : hub.IndexOf(
                    "CanInitializeNativeRuntimeForBridge(",
                    lifecycleRead,
                    StringComparison.Ordinal);
            var publishStop = lifecycleRead < 0
                ? -1
                : hub.IndexOf(
                    "ShouldStopFoxRunPublishing(",
                    lifecycleRead,
                    StringComparison.Ordinal);

            Assert.Contains(
                "_manager.FoxRunPublishSessionChanged += OnPublishSessionChanged;",
                hub,
                StringComparison.Ordinal);
            Assert.Contains(
                "_manager.FoxRunPublishSessionChanged -= OnPublishSessionChanged;",
                hub,
                StringComparison.Ordinal);
            Assert.Contains(
                "=> ApplyPublishSessionPolicy(policy);",
                hub,
                StringComparison.Ordinal);
            Assert.Contains(
                "ShouldStopFoxRunPublishing(",
                hub,
                StringComparison.Ordinal);
            Assert.Contains(
                "_publishSessionTracker.AllowsPublishing,",
                hub,
                StringComparison.Ordinal);
            Assert.True(
                lifecycleRead >= 0
                && lifecycleRefresh > lifecycleRead
                && publishStop > lifecycleRefresh,
                "The custom publisher Hub must refresh a dirty lifecycle cache before treating its cached shutdown state as permanent.");
        }

        [Fact]
        public void FoxRunAddOnDiscoveryDelegatesNativePathRegistrationToR2fu()
        {
            var bootstrap = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/"
                + "FoxRunRos2CustomTypesupportNativePluginBootstrap.cs");

            Assert.Contains("PackageInfo.FindForAssembly", bootstrap, StringComparison.Ordinal);
            Assert.Contains(
                "Ros2ForUnityNativePluginBootstrap.RegisterEditorPackagePluginDirectory(package.resolvedPath)",
                bootstrap,
                StringComparison.Ordinal);
            Assert.DoesNotContain("GlobalVariables.RegisterNativeLibraryDirectory", bootstrap, StringComparison.Ordinal);
            Assert.DoesNotContain("NativePluginRelativeDirectory", bootstrap, StringComparison.Ordinal);

            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var runtimeBootstrap = Text(
                    "Packages/dev.unity2foxglove.ros2forunity.runtime."
                    + distro
                    + ".win64/Runtime/Ros2ForUnity/Scripts/Ros2ForUnityNativePluginBootstrap.cs");
                Assert.Contains("RegisterEditorPackagePluginDirectory", runtimeBootstrap, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void FoxRunAddOnDiscoveryAlsoMakesSiblingNativeDependenciesDiscoverable()
        {
            var bootstrap = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/"
                + "FoxRunRos2CustomTypesupportNativePluginBootstrap.cs");

            Assert.Contains(
                "AppendSelectedAddOnPluginDirectoryToProcessPath",
                bootstrap,
                StringComparison.Ordinal);
            Assert.Contains("Environment.SetEnvironmentVariable", bootstrap, StringComparison.Ordinal);
            Assert.Contains("RestoreEditorProcessPath", bootstrap, StringComparison.Ordinal);
            Assert.Contains("\"PATH\"", bootstrap, StringComparison.Ordinal);
            Assert.Contains("Path.PathSeparator", bootstrap, StringComparison.Ordinal);
            Assert.Contains("TryRemoveOwnedPathEntries", bootstrap, StringComparison.Ordinal);
            Assert.Contains("ownedProcessPathEntries", bootstrap, StringComparison.Ordinal);
            Assert.Contains("var wasOwned = processPathOwned", bootstrap, StringComparison.Ordinal);
            Assert.Contains(
                "Ros2ForUnityNativePluginBootstrap.RegisterEditorPackagePluginDirectory(package.resolvedPath)",
                bootstrap,
                StringComparison.Ordinal);
            Assert.DoesNotContain("GlobalVariables.RegisterNativeLibraryDirectory", bootstrap, StringComparison.Ordinal);
        }

        [Fact]
        public void ZenohRouterConfigurationIsAnOptionalR2fuSessionSetting()
        {
            var runtimeSelection = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var zenohRouterSettings = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityZenohRouterSettings.cs");
            var customInspector = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/FoxRunRos2CustomTypesupportInspector.cs");
            var playModeGuard = Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");

            Assert.Contains("SessionZenohRouterEndpointKey", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("GetZenohRouterEndpointRequiringEditorRestart", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityZenohRouterSettings", runtimeSelection, StringComparison.Ordinal);

            Assert.Contains("DefaultZenohRouterPort = 8778", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("ZenohRouterAddressEditorUserSettingsKey", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("ZenohRouterPortEditorUserSettingsKey", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("R2fuZenohRouterSettings.json", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("ZENOH_SESSION_CONFIG_URI", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("ReadAllTextForVerification(templatePath)", zenohRouterSettings, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllText(templatePath)", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("SetProcessEnvironmentVariable(ZenohSessionConfigEnvironmentVariable", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityEditorEnvironmentLease.Set", zenohRouterSettings, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityEditorEnvironmentLease", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("_wputenv_s", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("if (!pair.Value.HasApplied", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("string.Equals(current, pair.Value.Applied", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("string.IsNullOrWhiteSpace(rmwImplementation) ? null", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("RestoreEditorProcessEnvironment", playModeGuard, StringComparison.Ordinal);
            Assert.Contains("GetZenohRouterEndpointRequiringEditorRestart", playModeGuard, StringComparison.Ordinal);

            var customInterfaceIndex = customInspector.IndexOf(
                "Custom FoxRun ROS 2 Interface",
                StringComparison.Ordinal);
            var routerIndex = customInspector.IndexOf("DrawZenohRouterSettings", StringComparison.Ordinal);
            var contractsIndex = customInspector.IndexOf("DrawContracts(result.Contracts)", StringComparison.Ordinal);
            Assert.True(
                customInterfaceIndex >= 0 && routerIndex > customInterfaceIndex && contractsIndex > routerIndex,
                "The conditional Zenoh Router controls must be between Custom FoxRun ROS 2 Interface and Generated Contracts.");
            Assert.Contains("Router Address", customInspector, StringComparison.Ordinal);
            Assert.Contains("Router Port", customInspector, StringComparison.Ordinal);
        }

    }
}
