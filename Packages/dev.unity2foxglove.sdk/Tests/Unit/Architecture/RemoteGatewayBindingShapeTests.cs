// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 171 remote gateway native binding boundary checks.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "171")]
    [Trait("Domain", "Architecture")]
    public sealed class RemoteGatewayBindingShapeTests
    {
        private const string RuntimeRoot = "Packages/dev.unity2foxglove.remotegateway.win64/Runtime";

        [Fact]
        public void NativeMethodsDeclareOfficialGatewayAndChannelAbi()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayNativeMethods.cs");

            Assert.Contains("private const string LibraryName = \"foxglove\";", source, StringComparison.Ordinal);
            foreach (var entryPoint in new[]
            {
                "foxglove_gateway_start",
                "foxglove_gateway_stop",
                "foxglove_gateway_connection_status",
                "foxglove_gateway_sink_id",
                "foxglove_context_new",
                "foxglove_context_free",
                "foxglove_raw_channel_create",
                "foxglove_channel_log",
                "foxglove_channel_close",
                "foxglove_channel_free"
            })
            {
                Assert.Contains("EntryPoint = \"" + entryPoint + "\"", source, StringComparison.Ordinal);
            }

            Assert.Contains("[StructLayout(LayoutKind.Sequential)]", source, StringComparison.Ordinal);
            Assert.Contains("internal struct FoxgloveGatewayOptions", source, StringComparison.Ordinal);
            Assert.Contains("internal struct FoxgloveGatewayCallbacks", source, StringComparison.Ordinal);
            Assert.Contains("internal enum FoxgloveConnectionStatus", source, StringComparison.Ordinal);
            Assert.Contains("internal enum FoxgloveError", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GatewayHandleOwnsNativeStopExactlyOnce()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayHandle.cs");

            Assert.Contains("SafeHandleZeroOrMinusOneIsInvalid", source, StringComparison.Ordinal);
            Assert.Contains("protected override bool ReleaseHandle()", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.GatewayStop(handle)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange", source, StringComparison.Ordinal);
            Assert.DoesNotContain("~RemoteGatewayHandle", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CallbacksAreRootedAndMarshaledThroughBoundedQueue()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayCallbacks.cs");

            Assert.Contains("GCHandle.Alloc", source, StringComparison.Ordinal);
            Assert.Contains("GCHandle.FromIntPtr", source, StringComparison.Ordinal);
            Assert.Contains("MonoPInvokeCallback", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayEventQueue", source, StringComparison.Ordinal);
            Assert.Contains("TryEnqueue", source, StringComparison.Ordinal);
            Assert.Contains("OnConnectionStatusChanged", source, StringComparison.Ordinal);
            Assert.Contains("OnMessageData", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UnityEngine.", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EventQueueIsBoundedAndNeverBlocksNativeCallbacks()
        {
            var source = Text(RuntimeRoot + "/RemoteGatewayEventQueue.cs");

            Assert.Contains("readonly int _capacity", source, StringComparison.Ordinal);
            Assert.Contains("TryEnqueue", source, StringComparison.Ordinal);
            Assert.Contains("DropOldest", source, StringComparison.Ordinal);
            Assert.Contains("DroppedCount", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Wait(", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Result", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LifecycleGateBlocksNativeStartDuringEditorReloadQuit()
        {
            var source = Text(RuntimeRoot + "/RemoteGatewayLifecycleGate.cs");

            Assert.Contains("Application.quitting", source, StringComparison.Ordinal);
            Assert.Contains("AssemblyReloadEvents.beforeAssemblyReload", source, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.isCompiling", source, StringComparison.Ordinal);
            Assert.Contains("CanStartNativeGateway", source, StringComparison.Ordinal);
            Assert.Contains("CanStopNativeGateway", source, StringComparison.Ordinal);
        }

        [Fact]
        public void MirrorSinkUsesCoreMirrorContractNotTransport()
        {
            var source = Text(RuntimeRoot + "/RemoteGatewayMirrorSink.cs");

            Assert.Contains("IFoxgloveMirrorSink", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayChannelRegistry", source, StringComparison.Ordinal);
            Assert.Contains("HasChannelDemand", source, StringComparison.Ordinal);
            Assert.Contains("RegisterChannel", source, StringComparison.Ordinal);
            Assert.Contains("Publish(", source, StringComparison.Ordinal);
            Assert.Contains("MirroredMessageCount", source, StringComparison.Ordinal);
            Assert.Contains("DroppedMessageCount", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveTransport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerPublishingImportsCoreMirrorContractNamespace()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");

            Assert.Contains("using Unity.FoxgloveSDK.Core;", source, StringComparison.Ordinal);
            Assert.Contains("public void SetMirrorSink(IFoxgloveMirrorSink sink)", source, StringComparison.Ordinal);
            Assert.Contains("public IFoxgloveMirrorSink GetMirrorSink()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ChannelRegistryMirrorsThroughOfficialChannelLogAbi()
        {
            var source = Text(RuntimeRoot + "/RemoteGatewayChannelRegistry.cs");

            Assert.Contains("RemoteGatewayNativeMethods.RawChannelCreate", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.ChannelLog", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.ChannelClose", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.ChannelFree", source, StringComparison.Ordinal);
            Assert.Contains("GatewaySinkId", source, StringComparison.Ordinal);
            Assert.Contains("Convert.FromBase64String", source, StringComparison.Ordinal);
            Assert.Contains("GCHandle.Alloc", source, StringComparison.Ordinal);
            Assert.Contains("FoxgloveSchema", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveTransport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WaitAllRequests", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ControllerSurfaceIsDefaultOffAndTokenSafe()
        {
            var source = Text(RuntimeRoot + "/FoxgloveRemoteGatewayController.cs");

            Assert.Contains("[SerializeField] private bool _enableRemoteGateway;", source, StringComparison.Ordinal);
            Assert.Contains("FOXGLOVE_DEVICE_TOKEN", source, StringComparison.Ordinal);
            Assert.Contains("EditorUserSettings", source, StringComparison.Ordinal);
            Assert.Contains("token in a scene", source, StringComparison.Ordinal);
            Assert.Contains("Foxglove Cloud", source, StringComparison.Ordinal);
            Assert.Contains("SetMirrorSink", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayLifecycleGate.CanStartNativeGateway", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayLifecycleGate.CanStopNativeGateway", source, StringComparison.Ordinal);
            Assert.Contains("StopGateway", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.Log(_deviceToken", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.LogWarning(_deviceToken", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ControllerStopsGatewayInlineBeforeReleasingCallbackRoot()
        {
            var source = Text(RuntimeRoot + "/FoxgloveRemoteGatewayController.cs");
            var handleDispose = source.IndexOf("handle?.Dispose();", StringComparison.Ordinal);
            var callbacksDispose = source.IndexOf("callbacks?.Dispose();", StringComparison.Ordinal);
            var eventsClear = source.IndexOf("_events = null;", StringComparison.Ordinal);

            Assert.DoesNotContain("using System.Threading;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ThreadPool.QueueUserWorkItem", source, StringComparison.Ordinal);
            Assert.True(handleDispose >= 0, "StopGateway must dispose the native gateway handle.");
            Assert.True(callbacksDispose > handleDispose, "Callback GCHandle roots must outlive blocking native stop.");
            Assert.True(eventsClear > callbacksDispose, "Pending native callback events must be cleared after callback roots are released.");
            Assert.Contains("_connectionStatus = \"ShuttingDown\";", source, StringComparison.Ordinal);
            Assert.Contains("_connectionStatus = \"Shutdown\";", source, StringComparison.Ordinal);
        }

        [Fact]
        public void V1CapabilityPolicyKeepsInboundCloudControlDisabled()
        {
            var source = Text(RuntimeRoot + "/RemoteGatewayCapabilityPolicy.cs");

            Assert.Contains("V1CapabilityFlags = 0", source, StringComparison.Ordinal);
            Assert.Contains("CreateOutboundOnlyCapabilities", source, StringComparison.Ordinal);
            Assert.Contains("ClientPublish", source, StringComparison.Ordinal);
            Assert.Contains("Services", source, StringComparison.Ordinal);
            Assert.Contains("Parameters", source, StringComparison.Ordinal);
            Assert.Contains("Assets", source, StringComparison.Ordinal);
            Assert.Contains("ConnectionGraph", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FOXGLOVE_GATEWAY_CAPABILITY_CLIENT_PUBLISH", source, StringComparison.Ordinal);
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRoot);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Packages")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
