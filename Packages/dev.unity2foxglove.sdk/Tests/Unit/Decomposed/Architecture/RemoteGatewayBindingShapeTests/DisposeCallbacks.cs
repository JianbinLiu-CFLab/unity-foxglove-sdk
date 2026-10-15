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


public sealed partial class RemoteGatewayBindingShapeTests
    {
        private const string BehaviorProbeSource = @"
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.FoxgloveSDK.RemoteGateway.Native;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    public sealed class StartupProbeResult
    {
        public int ContextNewCalls { get; set; }
        public int GatewayStartCalls { get; set; }
        public int ContextFreeCalls { get; set; }
        public int GatewayStopCalls { get; set; }
        public int DiagnosticCount { get; set; }
        public bool StartupFaulted { get; set; }
        public bool HasOwnedResources { get; set; }
        public string ConnectionStatus { get; set; }
        public WeakReference Callback { get; set; }
    }

    public sealed class PostHandleProbeResult
    {
        public bool FailureObserved { get; set; }
        public int SinkIdCalls { get; set; }
        public int GatewayStopCalls { get; set; }
    }

    public sealed class DiscoveryProbeResult
    {
        public bool FirstLookupSucceeded { get; set; }
        public bool SecondLookupSucceeded { get; set; }
        public int CallsBeforeManagerAppears { get; set; }
        public int FindObjectOfTypeCalls { get; set; }
        public int SuppressedLookupAttempts { get; set; }
    }

    public sealed class QueueProbeResult
    {
        public bool RetainedStatus { get; set; }
        public string Status { get; set; }
        public bool InitialStatusAccepted { get; set; }
        public bool ReplacementStatusAccepted { get; set; }
        public bool WorkReplacementAccepted { get; set; }
        public bool StatusAccepted { get; set; }
        public bool WorkAccepted { get; set; }
        public long DroppedCount { get; set; }
        public int PeakCount { get; set; }
        public int RemainingCount { get; set; }
    }

    public static class RemoteGatewayBehaviorProbe
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static WeakReference DisposeCallbacks()
        {
            var callbacks = new RemoteGatewayCallbacks(new RemoteGatewayEventQueue(1));
            var weakReference = new WeakReference(callbacks);
            callbacks.Dispose();
            callbacks.Dispose();
            callbacks = null;
            return weakReference;
        }

        public static StartupProbeResult ContextNewThrows()
            => Run(throwFromContextNew: true, returnGatewayError: false);

        public static StartupProbeResult GatewayStartThrows()
            => Run(throwFromContextNew: false, returnGatewayError: false);

        public static StartupProbeResult GatewayStartReturnsError()
            => Run(throwFromContextNew: false, returnGatewayError: true);

        public static StartupProbeResult GatewayStartReturnsErrorWithHandle()
            => Run(throwFromContextNew: false, returnGatewayError: true, returnGatewayHandle: true);

        public static StartupProbeResult ConnectionStatusThrowsAfterHandle()
            => Run(throwFromContextNew: false, returnGatewayError: false, returnGatewayHandle: true, throwConnectionStatus: true);

        public static StartupProbeResult MirrorAttachThrowsAfterHandle()
            => Run(throwFromContextNew: false, returnGatewayError: false, returnGatewayHandle: true, throwMirrorAttach: true);

        public static PostHandleProbeResult SinkIdThrowsAfterHandle()
        {
            var nativeApi = new ThrowingStartupNativeApi(false, false, true, false, true);
            var handle = new RemoteGatewayHandle(
                new IntPtr(5678),
                nativeApi.GatewayConnectionStatus,
                nativeApi.GatewaySinkId,
                nativeApi.GatewayStop);
            var failureObserved = false;
            try
            {
                var unused = handle.SinkId;
            }
            catch (EntryPointNotFoundException)
            {
                failureObserved = true;
            }
            handle.Dispose();
            return new PostHandleProbeResult
            {
                FailureObserved = failureObserved,
                SinkIdCalls = nativeApi.SinkIdCalls,
                GatewayStopCalls = nativeApi.GatewayStopCalls
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static DiscoveryProbeResult LateManagerDiscovery()
        {
            UnityEngine.Object.FindObjectOfTypeResult = null;
            UnityEngine.Object.FindObjectOfTypeCalls = 0;
            UnityEngine.Component.GetComponentResult = null;
            var controller = new FoxgloveRemoteGatewayController();
            var ensureManager = typeof(FoxgloveRemoteGatewayController).GetMethod(
                ""EnsureManager"",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var first = (bool)ensureManager.Invoke(controller, null);
            var callsBeforeManagerAppears = UnityEngine.Object.FindObjectOfTypeCalls;
            var suppressedLookupAttempts = 0;
            for (var i = 0; i < 60; i++)
            {
                if ((bool)ensureManager.Invoke(controller, null))
                    throw new InvalidOperationException(""Manager lookup unexpectedly succeeded during the backoff window."");
                suppressedLookupAttempts++;
            }
            UnityEngine.Object.FindObjectOfTypeResult = new Unity.FoxgloveSDK.Components.FoxgloveManager();
            var second = (bool)ensureManager.Invoke(controller, null);
            var calls = UnityEngine.Object.FindObjectOfTypeCalls;
            UnityEngine.Object.FindObjectOfTypeResult = null;
            return new DiscoveryProbeResult
            {
                FirstLookupSucceeded = first,
                SecondLookupSucceeded = second,
                CallsBeforeManagerAppears = callsBeforeManagerAppears,
                FindObjectOfTypeCalls = calls,
                SuppressedLookupAttempts = suppressedLookupAttempts
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static QueueProbeResult StatusSurvivesQueueSaturation()
        {
            var queue = new RemoteGatewayEventQueue(2);
            queue.TryEnqueue(RemoteGatewayEvent.ClientEvent(RemoteGatewayEventKind.ClientSubscribed, 1U));
            queue.TryEnqueue(RemoteGatewayEvent.ClientEvent(RemoteGatewayEventKind.ClientUnsubscribed, 2U));
            var initialStatusAccepted = queue.TryEnqueue(RemoteGatewayEvent.ConnectionStatusChanged(
                RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Connected));
            var replacementStatusAccepted = queue.TryEnqueue(RemoteGatewayEvent.ConnectionStatusChanged(
                RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Connected));
            var workReplacementAccepted = queue.TryEnqueue(RemoteGatewayEvent.ClientEvent(
                RemoteGatewayEventKind.ClientSubscribed, 3U));
            var peakCount = queue.Count;

            var controller = new FoxgloveRemoteGatewayController();
            typeof(FoxgloveRemoteGatewayController).GetField(
                ""_events"",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, queue);
            typeof(FoxgloveRemoteGatewayController).GetMethod(
                ""DrainGatewayEvents"",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            var status = controller.ConnectionStatus;

            return new QueueProbeResult
            {
                RetainedStatus = string.Equals(status, ""Connected"", StringComparison.Ordinal),
                Status = status,
                InitialStatusAccepted = initialStatusAccepted,
                ReplacementStatusAccepted = replacementStatusAccepted,
                WorkReplacementAccepted = workReplacementAccepted,
                DroppedCount = queue.DroppedCount,
                PeakCount = peakCount,
                RemainingCount = queue.Count
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static QueueProbeResult CapacityOneStatusReservation()
        {
            var queue = new RemoteGatewayEventQueue(1);
            var statusAccepted = queue.TryEnqueue(RemoteGatewayEvent.ConnectionStatusChanged(
                RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Connected));
            var workAccepted = queue.TryEnqueue(RemoteGatewayEvent.ClientEvent(
                RemoteGatewayEventKind.ClientSubscribed, 1U));
            var peakCount = queue.Count;

            var controller = new FoxgloveRemoteGatewayController();
            typeof(FoxgloveRemoteGatewayController).GetField(
                ""_events"",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, queue);
            typeof(FoxgloveRemoteGatewayController).GetMethod(
                ""DrainGatewayEvents"",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

            return new QueueProbeResult
            {
                StatusAccepted = statusAccepted,
                WorkAccepted = workAccepted,
                Status = controller.ConnectionStatus,
                PeakCount = peakCount,
                DroppedCount = queue.DroppedCount,
                RemainingCount = queue.Count
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StartupProbeResult Run(bool throwFromContextNew, bool returnGatewayError, bool returnGatewayHandle = false, bool throwConnectionStatus = false, bool throwMirrorAttach = false)
        {
            UnityEngine.Debug.Reset();
            Unity.FoxgloveSDK.Components.FoxgloveManager.SetMirrorSinkAction = null;
            var nativeApi = new ThrowingStartupNativeApi(throwFromContextNew, returnGatewayError, returnGatewayHandle, throwConnectionStatus);
            if (throwMirrorAttach)
                Unity.FoxgloveSDK.Components.FoxgloveManager.SetMirrorSinkAction = sink => throw new EntryPointNotFoundException(""injected mirror attach failure"");
            var controller = new FoxgloveRemoteGatewayController
            {
                StartupNativeApiForTests = nativeApi
            };
            typeof(FoxgloveRemoteGatewayController).GetField(""_manager"", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, new Unity.FoxgloveSDK.Components.FoxgloveManager());

            try
            {
                Attempt(controller);
                if (!throwMirrorAttach)
                    Attempt(controller);

                return new StartupProbeResult
            {
                ContextNewCalls = nativeApi.ContextNewCalls,
                GatewayStartCalls = nativeApi.GatewayStartCalls,
                ContextFreeCalls = nativeApi.ContextFreeCalls,
                DiagnosticCount = UnityEngine.Debug.WarningCount + UnityEngine.Debug.ErrorCount,
                StartupFaulted = controller.StartupFaultedForTests,
                HasOwnedResources = controller.HasOwnedResourcesForTests,
                ConnectionStatus = controller.ConnectionStatus,
                Callback = nativeApi.Callback,
                GatewayStopCalls = nativeApi.GatewayStopCalls
                };
            }
            finally
            {
                Unity.FoxgloveSDK.Components.FoxgloveManager.SetMirrorSinkAction = null;
            }
        }

        private static void Attempt(FoxgloveRemoteGatewayController controller)
        {
            try
            {
                controller.TryStartGatewayWithToken(""test-token"");
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private sealed class ThrowingStartupNativeApi : IRemoteGatewayStartupNativeApi
        {
            private readonly bool _throwFromContextNew;
            private readonly bool _returnGatewayError;
            private readonly bool _returnGatewayHandle;
            private readonly bool _throwConnectionStatus;
            private readonly bool _throwSinkId;

            internal ThrowingStartupNativeApi(bool throwFromContextNew, bool returnGatewayError, bool returnGatewayHandle = false, bool throwConnectionStatus = false, bool throwSinkId = false)
            {
                _throwFromContextNew = throwFromContextNew;
                _returnGatewayError = returnGatewayError;
                _returnGatewayHandle = returnGatewayHandle;
                _throwConnectionStatus = throwConnectionStatus;
                _throwSinkId = throwSinkId;
            }

            internal int ContextNewCalls { get; private set; }
            internal int GatewayStartCalls { get; private set; }
            internal int ContextFreeCalls { get; private set; }
            internal int GatewayStopCalls { get; private set; }
            internal int SinkIdCalls { get; private set; }
            internal WeakReference Callback { get; private set; }

            public IntPtr ContextNew()
            {
                ContextNewCalls++;
                if (_throwFromContextNew)
                    throw new DllNotFoundException(""injected context allocation failure"");

                return new IntPtr(1234);
            }

            public void ContextFree(IntPtr context)
            {
                if (context == IntPtr.Zero)
                    throw new InvalidOperationException(""zero context cannot be freed"");

                ContextFreeCalls++;
            }

            public RemoteGatewayNativeMethods.FoxgloveError GatewayStart(
                ref RemoteGatewayNativeMethods.FoxgloveGatewayOptions options,
                out IntPtr gateway)
            {
                GatewayStartCalls++;
                gateway = _returnGatewayHandle ? new IntPtr(5678) : IntPtr.Zero;
                var nativeCallbacks = Marshal.PtrToStructure<RemoteGatewayNativeMethods.FoxgloveGatewayCallbacks>(
                    options.Callbacks);
                Callback = new WeakReference(GCHandle.FromIntPtr(nativeCallbacks.Context).Target);
                if (_returnGatewayError)
                    return RemoteGatewayNativeMethods.FoxgloveError.ConfigurationError;

                if (_returnGatewayHandle)
                    return RemoteGatewayNativeMethods.FoxgloveError.Ok;

                throw new EntryPointNotFoundException(""injected gateway start failure"");
            }

            public RemoteGatewayNativeMethods.FoxgloveConnectionStatus GatewayConnectionStatus(RemoteGatewayHandle gateway)
            {
                if (_throwConnectionStatus)
                    throw new EntryPointNotFoundException(""injected connection status failure"");
                return RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Connected;
            }

            public ulong GatewaySinkId(RemoteGatewayHandle gateway)
            {
                SinkIdCalls++;
                if (_throwSinkId)
                    throw new EntryPointNotFoundException(""injected sink id failure"");
                return 1UL;
            }

            public RemoteGatewayNativeMethods.FoxgloveError GatewayStop(IntPtr gateway)
            {
                GatewayStopCalls++;
                return RemoteGatewayNativeMethods.FoxgloveError.Ok;
            }
        }
    }
}
";

        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRoot);

        private static string FindRepoRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("REPO_ROOT");
            if (IsRepoRoot(overrideRoot))
                return Path.GetFullPath(overrideRoot);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (IsRepoRoot(dir.FullName))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate repository root from "
                + AppContext.BaseDirectory
                + ". Set REPO_ROOT to a checkout containing README.md, Unity2Foxglove/, and Packages/.");
        }

        private static bool IsRepoRoot(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && File.Exists(Path.Combine(path, "README.md"))
                   && Directory.Exists(Path.Combine(path, "Unity2Foxglove"))
                   && Directory.Exists(Path.Combine(path, "Packages"));
        }
    }
}
