// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 171 public validation for the optional Remote Gateway package boundary.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class RemoteGatewayBoundaryValidation
    {
        private const string PackageRoot = "Packages/dev.unity2foxglove.remotegateway.win64";
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 171 Tests ---");
            _passCount = 0;

            VerifyPackageShape();
            VerifyNativeArtifactManifestAndBuildScript();
            VerifyCoreDependencyBoundary();
            VerifyMirrorSinkBoundary();
            VerifyControllerTokenAndLifecyclePolicy();
            VerifyInboundCapabilityPolicy();
            VerifyCompileAndRegistrySurface();

            Console.WriteLine("Phase 171: " + _passCount + " checks passed.\n");
        }

        private static void VerifyPackageShape()
        {
            var packageJson = ReadRepoText(PackageRoot + "/package.json");
            var asmdef = ReadRepoText(PackageRoot + "/Runtime/Unity.FoxgloveSDK.RemoteGateway.asmdef");
            var readme = ReadRepoText(PackageRoot + "/README.md");
            var notices = ReadRepoText(PackageRoot + "/THIRD_PARTY_NOTICES.md");

            Check(packageJson.Contains("\"name\": \"dev.unity2foxglove.remotegateway.win64\"", StringComparison.Ordinal)
                  && packageJson.Contains("\"dev.unity2foxglove.sdk\"", StringComparison.Ordinal)
                  && packageJson.Contains("Windows x64", StringComparison.Ordinal),
                "171-1: optional remote gateway package declares Win64 package metadata and core SDK dependency");

            Check(asmdef.Contains("\"name\": \"Unity.FoxgloveSDK.RemoteGateway\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"Unity.FoxgloveSDK\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"WindowsStandalone64\"", StringComparison.Ordinal),
                "171-2: optional asmdef is scoped to Editor/WindowsStandalone64 and references core SDK only");

            Check(readme.Contains("disabled by default", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("Foxglove Cloud", StringComparison.Ordinal)
                  && readme.Contains("Windows x64", StringComparison.Ordinal),
                "171-3: README states default-off Foxglove Cloud Win64 scope");

            Check(notices.Contains("foxglove-sdk", StringComparison.OrdinalIgnoreCase)
                  && notices.Contains("LiveKit", StringComparison.OrdinalIgnoreCase),
                "171-4: third-party notices mention foxglove-sdk and LiveKit dependency closure");

            Check(HasValidGuid(PackageRoot + "/Runtime/FoxgloveRemoteGatewayController.cs.meta")
                  && HasValidGuid(PackageRoot + "/Runtime/RemoteGatewayChannelRegistry.cs.meta")
                  && HasValidGuid(PackageRoot + "/Runtime/Native/RemoteGatewayNativeMethods.cs.meta"),
                "171-5: new Unity script metas have valid 32-hex GUIDs");
        }

        private static void VerifyNativeArtifactManifestAndBuildScript()
        {
            var manifest = ReadRepoText(PackageRoot + "/Runtime/Plugins/Windows/x86_64/foxglove-gateway-native-artifact.json");
            var buildScript = ReadRepoText("Scripts/remotegateway/build_foxglove_c_win64.py");

            Check(manifest.Contains("\"features\": \"remote-access\"", StringComparison.Ordinal)
                  && manifest.Contains("\"sha256\"", StringComparison.Ordinal)
                  && manifest.Contains("\"sideDependencies\"", StringComparison.Ordinal)
                  && manifest.Contains("intentionally not committed", StringComparison.Ordinal),
                "171-6: native artifact manifest records remote-access build metadata without requiring the DLL");

            Check(buildScript.Contains("cargo\", \"build\", \"--release\", \"--features\", \"remote-access\"", StringComparison.Ordinal)
                   && buildScript.Contains("STAGING_RELATIVE = \"build/remotegateway/foxglove-c-win64\"", StringComparison.Ordinal)
                   && buildScript.Contains("--copy-to-package", StringComparison.Ordinal)
                   && buildScript.Contains("--update-package-manifest", StringComparison.Ordinal)
                   && buildScript.Contains("APPROVED_ARTIFACTS", StringComparison.Ordinal),
                "171-7: native build script stages outside Packages and copies only reviewed artifacts on request");

            Check(!File.Exists(RepoPath(PackageRoot + "/Runtime/Plugins/Windows/x86_64/foxglove.dll")),
                "171-8: public repository does not commit the generated native DLL");
        }

        private static void VerifyCoreDependencyBoundary()
        {
            var coreAsmdef = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var managerPublishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var mirrorContract = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Routing/FoxgloveMirrorSink.cs");

            Check(!coreAsmdef.Contains("RemoteGateway", StringComparison.Ordinal)
                  && !runtime.Contains("RemoteGateway", StringComparison.Ordinal)
                  && !session.Contains("RemoteGateway", StringComparison.Ordinal)
                  && !managerPublishing.Contains("RemoteGateway", StringComparison.Ordinal),
                "171-9: core SDK has no compile-time dependency on the remote gateway package");

            Check(mirrorContract.Contains("public interface IFoxgloveMirrorSink", StringComparison.Ordinal)
                  && runtime.Contains("SetMirrorSink", StringComparison.Ordinal)
                  && session.Contains("HasMirrorDemand", StringComparison.Ordinal)
                  && session.Contains("TryMirrorPublish", StringComparison.Ordinal)
                  && managerPublishing.Contains("using Unity.FoxgloveSDK.Core;", StringComparison.Ordinal),
                "171-10: core exposes a generic mirror sink hook without naming the optional gateway");
        }

        private static void VerifyMirrorSinkBoundary()
        {
            var mirror = ReadRepoText(PackageRoot + "/Runtime/RemoteGatewayMirrorSink.cs");
            var registry = ReadRepoText(PackageRoot + "/Runtime/RemoteGatewayChannelRegistry.cs");
            var native = ReadRepoText(PackageRoot + "/Runtime/Native/RemoteGatewayNativeMethods.cs");
            var handle = ReadRepoText(PackageRoot + "/Runtime/Native/RemoteGatewayHandle.cs");
            var eventQueue = ReadRepoText(PackageRoot + "/Runtime/RemoteGatewayEventQueue.cs");
            var callbacks = ReadRepoText(PackageRoot + "/Runtime/Native/RemoteGatewayCallbacks.cs");

            Check(mirror.Contains("IFoxgloveMirrorSink", StringComparison.Ordinal)
                  && mirror.Contains("RemoteGatewayChannelRegistry", StringComparison.Ordinal)
                  && mirror.Contains("MirroredMessageCount", StringComparison.Ordinal)
                  && mirror.Contains("DroppedMessageCount", StringComparison.Ordinal)
                  && !mirror.Contains("IFoxgloveTransport", StringComparison.Ordinal),
                "171-11: optional mirror sink implements mirror contract instead of transport");

            Check(registry.Contains("RemoteGatewayNativeMethods.RawChannelCreate", StringComparison.Ordinal)
                  && registry.Contains("RemoteGatewayNativeMethods.ChannelLog", StringComparison.Ordinal)
                  && registry.Contains("GatewaySinkId", StringComparison.Ordinal)
                  && registry.Contains("GCHandle.Alloc", StringComparison.Ordinal),
                "171-12: channel registry mirrors through official foxglove_c channel ABI with pinned call-scoped buffers");

            Check(native.Contains("EntryPoint = \"foxglove_gateway_start\"", StringComparison.Ordinal)
                  && native.Contains("EntryPoint = \"foxglove_gateway_stop\"", StringComparison.Ordinal)
                  && native.Contains("EntryPoint = \"foxglove_channel_log\"", StringComparison.Ordinal)
                  && handle.Contains("SafeHandleZeroOrMinusOneIsInvalid", StringComparison.Ordinal)
                  && handle.Contains("RemoteGatewayNativeMethods.GatewayStop(handle)", StringComparison.Ordinal)
                  && handle.Contains("RemoteGatewayNativeMethods.FoxgloveConnectionStatus", StringComparison.Ordinal)
                  && handle.Contains("RemoteGatewayNativeMethods.FoxgloveError", StringComparison.Ordinal)
                  && !handle.Contains("internal FoxgloveConnectionStatus ConnectionStatus", StringComparison.Ordinal)
                  && eventQueue.Contains("internal static RemoteGatewayEvent ConnectionStatusChanged", StringComparison.Ordinal)
                  && !eventQueue.Contains("internal static RemoteGatewayEvent ConnectionStatus(", StringComparison.Ordinal)
                  && callbacks.Contains("RemoteGatewayEvent.ConnectionStatusChanged", StringComparison.Ordinal),
                "171-13: native binding covers gateway lifecycle and channel log ABI");
        }

        private static void VerifyControllerTokenAndLifecyclePolicy()
        {
            var controller = ReadRepoText(PackageRoot + "/Runtime/FoxgloveRemoteGatewayController.cs");
            var lifecycle = ReadRepoText(PackageRoot + "/Runtime/RemoteGatewayLifecycleGate.cs");

            Check(controller.Contains("[SerializeField] private bool _enableRemoteGateway;", StringComparison.Ordinal)
                  && controller.Contains("FOXGLOVE_DEVICE_TOKEN", StringComparison.Ordinal)
                  && controller.Contains("EditorUserSettings", StringComparison.Ordinal)
                  && !controller.Contains("[SerializeField] private string _deviceToken", StringComparison.Ordinal)
                  && controller.Contains("Foxglove Cloud", StringComparison.Ordinal),
                "171-14: controller is default-off and keeps device tokens out of serialized scene fields");

            var logLines = controller.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("Debug.Log", StringComparison.Ordinal));
            Check(logLines.All(line => !line.Contains("_deviceToken", StringComparison.Ordinal)
                                       && !line.Contains("deviceToken", StringComparison.Ordinal)),
                "171-15: controller never logs the device token variable");

            var handleDispose = controller.IndexOf("handle?.Dispose();", StringComparison.Ordinal);
            var callbacksDispose = controller.IndexOf("callbacks?.Dispose();", StringComparison.Ordinal);
            var eventsClear = controller.IndexOf("_events = null;", StringComparison.Ordinal);
            Check(controller.Contains("RemoteGatewayLifecycleGate.CanStartNativeGateway", StringComparison.Ordinal)
                  && controller.Contains("RemoteGatewayLifecycleGate.CanStopNativeGateway", StringComparison.Ordinal)
                  && !controller.Contains("ThreadPool.QueueUserWorkItem", StringComparison.Ordinal)
                  && handleDispose >= 0
                  && callbacksDispose > handleDispose
                  && eventsClear > callbacksDispose
                  && lifecycle.Contains("Application.quitting", StringComparison.Ordinal)
                  && lifecycle.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal),
                "171-16: lifecycle gate blocks unsafe starts and stop releases callback roots after native shutdown");
        }

        private static void VerifyInboundCapabilityPolicy()
        {
            var policy = ReadRepoText(PackageRoot + "/Runtime/RemoteGatewayCapabilityPolicy.cs");
            var callbacks = ReadRepoText(PackageRoot + "/Runtime/Native/RemoteGatewayCallbacks.cs");

            Check(policy.Contains("V1CapabilityFlags = 0", StringComparison.Ordinal)
                  && policy.Contains("CreateOutboundOnlyCapabilities", StringComparison.Ordinal)
                  && !policy.Contains("FOXGLOVE_GATEWAY_CAPABILITY_CLIENT_PUBLISH", StringComparison.Ordinal),
                "171-17: v1 remote gateway capabilities are outbound-only");

            Check(callbacks.Contains("OnMessageData", StringComparison.Ordinal)
                  && callbacks.Contains("RemoteGatewayEventQueue", StringComparison.Ordinal)
                  && callbacks.Contains("Interlocked.Exchange(ref _disposed, 1)", StringComparison.Ordinal)
                  && callbacks.Contains("Volatile.Read(ref _disposed)", StringComparison.Ordinal)
                  && callbacks.Contains("_selfHandle.IsAllocated", StringComparison.Ordinal)
                  && callbacks.Contains("_selfHandle.Free()", StringComparison.Ordinal)
                  && callbacks.Contains("blocking GatewayStop", StringComparison.Ordinal)
                  && !callbacks.Contains("UnityEngine.", StringComparison.Ordinal),
                "171-18: native callbacks are fail-closed marshaled events and do not touch Unity APIs");

            Check(callbacks.Contains("V1 advertises outbound-only capabilities", StringComparison.Ordinal)
                  && callbacks.Contains("Parameter requests are", StringComparison.Ordinal)
                  && callbacks.Contains("Parameter mutations are", StringComparison.Ordinal),
                "171-18B: unsupported parameter callbacks are documented as outbound-only diagnostics");
        }

        private static void VerifyCompileAndRegistrySurface()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("RemoteGatewayBoundaryValidation.cs", StringComparison.Ordinal),
                "171-19: runtime validation project compiles Phase171 validation");

            Check(registry.Contains("Ci(\"--phase171\", \"Phase 171: optional Remote Access Gateway package boundary\", RemoteGatewayBoundaryValidation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && PhaseValidationRegistry.All.Any(item => item.Flag == "--phase171"),
                "171-20: validation registry exposes --phase171 outside default CI");
        }

        private static bool HasValidGuid(string relativePath)
        {
            var source = ReadRepoText(relativePath);
            var match = Regex.Match(source, @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
            return match.Success;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new InvalidOperationException("[FAIL] 171-file: required repository file not found: " + relativePath);

            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
