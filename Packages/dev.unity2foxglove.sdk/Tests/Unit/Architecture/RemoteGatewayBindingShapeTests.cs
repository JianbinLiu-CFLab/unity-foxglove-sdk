// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 171 remote gateway native binding boundary checks.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "171")]
    [Trait("Domain", "Architecture")]
    public sealed partial class RemoteGatewayBindingShapeTests
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
            Assert.Contains("internal enum FoxgloveReliability : byte", source, StringComparison.Ordinal);
            Assert.Contains("internal struct FoxgloveQosProfile", source, StringComparison.Ordinal);
            Assert.DoesNotContain("public FoxgloveString Durability", source, StringComparison.Ordinal);
            Assert.DoesNotContain("public FoxgloveString Profile", source, StringComparison.Ordinal);
        }

        [Fact]
        public void QosProfileDeclaresExactlyOneByteReliabilityField()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayNativeMethods.cs");
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var qos = root.DescendantNodes()
                .OfType<StructDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "FoxgloveQosProfile");

            var fields = qos.Members.OfType<FieldDeclarationSyntax>().ToArray();
            Assert.Single(fields);
            Assert.Equal("FoxgloveReliability", fields[0].Declaration.Type.ToString());
            Assert.Equal("Reliability", fields[0].Declaration.Variables.Single().Identifier.ValueText);

            var reliability = root.DescendantNodes()
                .OfType<EnumDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "FoxgloveReliability");
            Assert.Equal("byte", reliability.BaseList.Types.Single().Type.ToString());
        }

        [Fact]
        public void ManagedGatewayLayoutsMatchExpectedX64Snapshot()
        {
            Assert.Equal(8, IntPtr.Size);

            var compilation = CreateRuntimeCompilation("RemoteGatewayLayoutProbe");
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var native = assembly.GetType(
                "Unity.FoxgloveSDK.RemoteGateway.Native.RemoteGatewayNativeMethods",
                throwOnError: true)!;
            var qos = native.GetNestedType("FoxgloveQosProfile", BindingFlags.NonPublic)!;
            var stringType = native.GetNestedType("FoxgloveString", BindingFlags.NonPublic)!;
            var callbacks = native.GetNestedType("FoxgloveGatewayCallbacks", BindingFlags.NonPublic)!;
            var options = native.GetNestedType("FoxgloveGatewayOptions", BindingFlags.NonPublic)!;

            Assert.Equal(1, Marshal.SizeOf(qos));
            Assert.Equal(0, Marshal.OffsetOf(qos, "Reliability").ToInt32());
            Assert.Equal(16, Marshal.SizeOf(stringType));
            Assert.Equal(104, Marshal.SizeOf(callbacks));
            Assert.Equal(168, Marshal.SizeOf(options));
            Assert.Equal(48, Marshal.OffsetOf(options, "Capabilities").ToInt32());
            Assert.Equal(56, Marshal.OffsetOf(options, "SupportedEncodings").ToInt32());
            Assert.Equal(136, Marshal.OffsetOf(options, "FoxgloveApiUrl").ToInt32());
            Assert.Equal(152, Marshal.OffsetOf(options, "FoxgloveApiTimeoutSecs").ToInt32());
            Assert.Equal(160, Marshal.OffsetOf(options, "MessageBacklogSize").ToInt32());
        }

        [Fact]
        public void RuntimeSourcesCompileWithMinimalUnitySurface()
        {
            var compilation = CreateRuntimeCompilation("RemoteGatewayRuntimeProbe");

            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToArray();

            Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        }

        [Fact]
        public void CallbackDisposeReleasesSelfHandleRoot()
        {
            var weakReference = (WeakReference)InvokeBehaviorProbe("DisposeCallbacks");

            ForceCollection();

            Assert.False(weakReference.IsAlive);
        }

        [Fact]
        public void ContextNewExceptionCleansStagedOwnershipAndBlocksRetryStorm()
        {
            var result = InvokeBehaviorProbe("ContextNewThrows");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 0,
                expectedContextFreeCalls: 0,
                expectedGatewayStopCalls: 0,
                expectCallback: false);
        }

        [Fact]
        public void GatewayStartExceptionCleansStagedOwnershipAndBlocksRetryStorm()
        {
            var result = InvokeBehaviorProbe("GatewayStartThrows");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 1,
                expectedContextFreeCalls: 1,
                expectedGatewayStopCalls: 0,
                expectCallback: true);
        }

        [Fact]
        public void GatewayStartErrorResultCleansStagedOwnershipAndBlocksRetryStorm()
        {
            var result = InvokeBehaviorProbe("GatewayStartReturnsError");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 1,
                expectedContextFreeCalls: 1,
                expectedGatewayStopCalls: 0,
                expectCallback: true);
        }

        [Fact]
        public void GatewayStartErrorWithHandleStopsBeforeFreeingContext()
        {
            var result = InvokeBehaviorProbe("GatewayStartReturnsErrorWithHandle");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 1,
                expectedContextFreeCalls: 1,
                expectedGatewayStopCalls: 1,
                expectCallback: true);
        }

        [Fact]
        public void ConnectionStatusFailureAfterHandleStopsBeforeFreeingContext()
        {
            var result = InvokeBehaviorProbe("ConnectionStatusThrowsAfterHandle");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 1,
                expectedContextFreeCalls: 1,
                expectedGatewayStopCalls: 1,
                expectCallback: true);
        }

        [Fact]
        public void MirrorAttachFailureAfterHandleStopsBeforeFreeingContext()
        {
            var result = InvokeBehaviorProbe("MirrorAttachThrowsAfterHandle");

            AssertStartupFailure(
                result,
                expectedContextNewCalls: 1,
                expectedGatewayStartCalls: 1,
                expectedContextFreeCalls: 1,
                expectedGatewayStopCalls: 1,
                expectCallback: true);
        }
        [Fact]
        public void SinkIdFailureAfterHandleIsInjectableAndStopRemainsSingle()
        {
            var result = InvokeBehaviorProbe("SinkIdThrowsAfterHandle");

            Assert.True(ReadProperty<bool>(result, "FailureObserved"));
            Assert.Equal(1, ReadProperty<int>(result, "SinkIdCalls"));
            Assert.Equal(1, ReadProperty<int>(result, "GatewayStopCalls"));
        }
        [Fact]
        public void ManagerLookupRetriesAfterBoundedDiscoveryBackoff()
        {
            var result = InvokeBehaviorProbe("LateManagerDiscovery");

            Assert.False(ReadProperty<bool>(result, "FirstLookupSucceeded"));
            Assert.True(ReadProperty<bool>(result, "SecondLookupSucceeded"));
            Assert.Equal(1, ReadProperty<int>(result, "CallsBeforeManagerAppears"));
            Assert.Equal(2, ReadProperty<int>(result, "FindObjectOfTypeCalls"));
            Assert.Equal(60, ReadProperty<int>(result, "SuppressedLookupAttempts"));
        }

        [Fact]
        public void ConnectionStatusSurvivesUnsupportedEventQueueSaturation()
        {
            var result = InvokeBehaviorProbe("StatusSurvivesQueueSaturation");

            Assert.True(ReadProperty<bool>(result, "RetainedStatus"));
            Assert.Equal("Connected", ReadProperty<string>(result, "Status"));
            Assert.True(ReadProperty<bool>(result, "InitialStatusAccepted"));
            Assert.True(ReadProperty<bool>(result, "ReplacementStatusAccepted"));
            Assert.True(ReadProperty<long>(result, "DroppedCount") >= 1);
            Assert.True(ReadProperty<int>(result, "PeakCount") <= 2);
            Assert.Equal(0, ReadProperty<int>(result, "RemainingCount"));
        }

        [Fact]
        public void CapacityOneReservesTheOnlySlotForConnectionStatus()
        {
            var result = InvokeBehaviorProbe("CapacityOneStatusReservation");

            Assert.True(ReadProperty<bool>(result, "StatusAccepted"));
            Assert.False(ReadProperty<bool>(result, "WorkAccepted"));
            Assert.Equal("Connected", ReadProperty<string>(result, "Status"));
            Assert.Equal(1, ReadProperty<int>(result, "PeakCount"));
            Assert.Equal(1L, ReadProperty<long>(result, "DroppedCount"));
            Assert.Equal(0, ReadProperty<int>(result, "RemainingCount"));
        }

        [Fact]
        public void GatewayHandleOwnsNativeStopExactlyOnce()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayHandle.cs");

            Assert.Contains("SafeHandleZeroOrMinusOneIsInvalid", source, StringComparison.Ordinal);
            Assert.Contains("protected override bool ReleaseHandle()", source, StringComparison.Ordinal);
            Assert.Contains("_stop(handle)", source, StringComparison.Ordinal);
            Assert.Contains("handle = IntPtr.Zero", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.FoxgloveConnectionStatus", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.FoxgloveError", source, StringComparison.Ordinal);
            Assert.DoesNotContain("internal FoxgloveConnectionStatus ConnectionStatus", source, StringComparison.Ordinal);
            Assert.DoesNotContain("== FoxgloveError.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("~RemoteGatewayHandle", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GatewayStatusQueriesKeepSafeHandleAliveAcrossNativeCall()
        {
            var nativeMethods = Text(RuntimeRoot + "/Native/RemoteGatewayNativeMethods.cs");
            var handle = Text(RuntimeRoot + "/Native/RemoteGatewayHandle.cs");

            Assert.Contains("GatewayConnectionStatus(RemoteGatewayHandle gateway)", nativeMethods, StringComparison.Ordinal);
            Assert.Contains("GatewaySinkId(RemoteGatewayHandle gateway)", nativeMethods, StringComparison.Ordinal);
            Assert.DoesNotContain("GatewayConnectionStatus(IntPtr gateway)", nativeMethods, StringComparison.Ordinal);
            Assert.DoesNotContain("GatewaySinkId(IntPtr gateway)", nativeMethods, StringComparison.Ordinal);
            Assert.Contains("_connectionStatus(this)", handle, StringComparison.Ordinal);
            Assert.Contains("_sinkId(this)", handle, StringComparison.Ordinal);
            Assert.DoesNotContain("GatewayConnectionStatus(handle)", handle, StringComparison.Ordinal);
            Assert.DoesNotContain("GatewaySinkId(handle)", handle, StringComparison.Ordinal);
            Assert.Contains("IsClosed || IsInvalid", handle, StringComparison.Ordinal);
            Assert.Contains("catch (ObjectDisposedException)", handle, StringComparison.Ordinal);
        }

        [Fact]
        public void CallbacksAreRootedAndMarshaledThroughBoundedQueue()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayCallbacks.cs");

            Assert.Contains("GCHandle.Alloc", source, StringComparison.Ordinal);
            Assert.Contains("GCHandle.FromIntPtr", source, StringComparison.Ordinal);
            Assert.Contains("_selfHandle.Free()", source, StringComparison.Ordinal);
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
            Assert.Contains("return !droppedOldest;", source, StringComparison.Ordinal);
            Assert.Contains("internal static RemoteGatewayEvent ConnectionStatusChanged", source, StringComparison.Ordinal);
            Assert.DoesNotContain("internal static RemoteGatewayEvent ConnectionStatus(", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Wait(", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Result", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EventQueueReportsOverflowWhenItDropsOldestWork()
        {
            var result = InvokeBehaviorProbe("StatusSurvivesQueueSaturation");
            Assert.True(ReadProperty<bool>(result, "InitialStatusAccepted")
                || ReadProperty<bool>(result, "ReplacementStatusAccepted"));
            Assert.True(ReadProperty<long>(result, "DroppedCount") > 0);
            Assert.False(ReadProperty<bool>(result, "WorkReplacementAccepted"));

            var capacityOne = InvokeBehaviorProbe("CapacityOneStatusReservation");
            Assert.True(ReadProperty<bool>(capacityOne, "StatusAccepted"));
            Assert.False(ReadProperty<bool>(capacityOne, "WorkAccepted"));
            Assert.Equal("Connected", ReadProperty<string>(capacityOne, "Status"));
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

    }
}
