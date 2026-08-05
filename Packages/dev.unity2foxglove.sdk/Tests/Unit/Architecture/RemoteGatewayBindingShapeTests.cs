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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                expectCallback: true);
        }

        [Fact]
        public void GatewayHandleOwnsNativeStopExactlyOnce()
        {
            var source = Text(RuntimeRoot + "/Native/RemoteGatewayHandle.cs");

            Assert.Contains("SafeHandleZeroOrMinusOneIsInvalid", source, StringComparison.Ordinal);
            Assert.Contains("protected override bool ReleaseHandle()", source, StringComparison.Ordinal);
            Assert.Contains("RemoteGatewayNativeMethods.GatewayStop(handle)", source, StringComparison.Ordinal);
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
            Assert.Contains("GatewayConnectionStatus(this)", handle, StringComparison.Ordinal);
            Assert.Contains("GatewaySinkId(this)", handle, StringComparison.Ordinal);
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
            Assert.DoesNotContain("[SerializeField] private string _deviceToken", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_deviceToken", source, StringComparison.Ordinal);
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
            var stopGateway = ParseMethod(source, "StopGateway");
            var statements = stopGateway
                .DescendantNodes()
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.ToString())
                .ToArray();
            var handleDispose = Array.FindIndex(
                statements,
                statement => statement.Contains("handle?.Dispose()", StringComparison.Ordinal));
            var callbacksDispose = Array.FindIndex(
                statements,
                statement => statement.Contains("callbacks?.Dispose()", StringComparison.Ordinal));
            var eventsClear = Array.FindIndex(
                statements,
                statement => statement.Contains("_events = null", StringComparison.Ordinal));

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

        private static MethodDeclarationSyntax ParseMethod(string source, string methodName)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var method = root
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, methodName, StringComparison.Ordinal));

            Assert.NotNull(method);
            Assert.NotNull(method.Body);
            return method;
        }

        private static void AssertStartupFailure(
            object result,
            int expectedContextNewCalls,
            int expectedGatewayStartCalls,
            int expectedContextFreeCalls,
            bool expectCallback)
        {
            Assert.Equal(expectedContextNewCalls, ReadProperty<int>(result, "ContextNewCalls"));
            Assert.Equal(expectedGatewayStartCalls, ReadProperty<int>(result, "GatewayStartCalls"));
            Assert.Equal(expectedContextFreeCalls, ReadProperty<int>(result, "ContextFreeCalls"));
            Assert.Equal(1, ReadProperty<int>(result, "DiagnosticCount"));
            Assert.True(ReadProperty<bool>(result, "StartupFaulted"));
            Assert.False(ReadProperty<bool>(result, "HasOwnedResources"));
            Assert.Equal("Faulted", ReadProperty<string>(result, "ConnectionStatus"));

            var callback = ReadProperty<WeakReference>(result, "Callback");
            if (!expectCallback)
            {
                Assert.Null(callback);
                return;
            }

            Assert.NotNull(callback);
            ForceCollection();
            Assert.False(callback.IsAlive);
        }

        private static T ReadProperty<T>(object target, string propertyName)
            => (T)target.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                .GetValue(target);

        private static object InvokeBehaviorProbe(string methodName)
        {
            var probeType = BehaviorProbeAssembly.Value.GetType(
                "Unity.FoxgloveSDK.RemoteGateway.RemoteGatewayBehaviorProbe",
                throwOnError: true);
            return probeType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, null);
        }

        private static void ForceCollection()
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private static Assembly CompileBehaviorProbeAssembly()
        {
            var behaviorProbe = CSharpSyntaxTree.ParseText(
                BehaviorProbeSource,
                RemoteGatewayParseOptions,
                path: "RemoteGatewayBehaviorProbe.cs");
            var compilation = CreateRuntimeCompilation(
                "RemoteGatewayBehaviorProbe_" + Guid.NewGuid().ToString("N"),
                behaviorProbe);
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            if (!emit.Success)
            {
                var errors = emit.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString());
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static CSharpCompilation CreateRuntimeCompilation(
            string assemblyName,
            params SyntaxTree[] additionalSources)
        {
            var runtimeSources = Directory.GetFiles(PathOf(RuntimeRoot), "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    RemoteGatewayParseOptions,
                    path: path));
            var sources = runtimeSources
                .Concat(new[] { CSharpSyntaxTree.ParseText(UnityCompileStub, RemoteGatewayParseOptions) })
                .Concat(additionalSources);

            return CSharpCompilation.Create(
                assemblyName,
                sources,
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static MetadataReference[] BasicReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            Assert.False(
                string.IsNullOrEmpty(trustedAssemblies),
                "TRUSTED_PLATFORM_ASSEMBLIES must be available to compile the remote gateway shape test surface.");

            var trusted = trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            return trusted
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static readonly CSharpParseOptions RemoteGatewayParseOptions =
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp9)
                .WithPreprocessorSymbols("UNITY_EDITOR");

        private static readonly Lazy<Assembly> BehaviorProbeAssembly =
            new Lazy<Assembly>(CompileBehaviorProbeAssembly);

        private const string UnityCompileStub = @"
using System;

namespace AOT
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type callbackType) {}
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static T FindObjectOfType<T>() where T : Object => null;
    }

    public class Component : Object
    {
        public T GetComponent<T>() where T : class => null;
    }

    public class MonoBehaviour : Component {}
    public sealed class DisallowMultipleComponentAttribute : Attribute {}
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) {} }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) {} }
    public sealed class SerializeField : Attribute {}
    public sealed class MinAttribute : Attribute { public MinAttribute(float min) {} }

    public static class Debug
    {
        public static int WarningCount { get; private set; }
        public static int ErrorCount { get; private set; }
        public static void Log(string message) {}
        public static void LogError(string message) { ErrorCount++; }
        public static void LogException(Exception exception) { ErrorCount++; }
        public static void LogWarning(string message) { WarningCount++; }
        public static void Reset()
        {
            WarningCount = 0;
            ErrorCount = 0;
        }
    }

    public static class Application
    {
        public static bool isPlaying => true;
        public static event Action quitting { add {} remove {} }
    }

    public enum RuntimeInitializeLoadType
    {
        SubsystemRegistration
    }

    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) {}
    }
}

namespace UnityEditor
{
    public static class EditorUserSettings
    {
        public static string GetConfigValue(string key) => null;
    }

    public static class EditorApplication
    {
        public static bool isCompiling => false;
        public static bool isUpdating => false;
    }

    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload { add {} remove {} }
    }
}

namespace Unity.FoxgloveSDK.Protocol
{
    public sealed class AdvertiseChannel
    {
        public uint Id { get; set; }
        public string Topic { get; set; }
        public string Encoding { get; set; }
        public string SchemaName { get; set; }
        public string SchemaEncoding { get; set; }
        public string Schema { get; set; }
    }
}

namespace Unity.FoxgloveSDK.Core
{
    public interface IFoxgloveMirrorSink
    {
        bool HasChannelDemand(Unity.FoxgloveSDK.Protocol.AdvertiseChannel channel);
        void RegisterChannel(Unity.FoxgloveSDK.Protocol.AdvertiseChannel channel);
        void UnregisterChannel(uint channelId);
        void Publish(Unity.FoxgloveSDK.Protocol.AdvertiseChannel channel, ulong logTimeNs, byte[] payload);
    }
}

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager : UnityEngine.MonoBehaviour
    {
        public bool IsRunning => true;
        public void SetMirrorSink(Unity.FoxgloveSDK.Core.IFoxgloveMirrorSink sink) {}
    }
}
";

        private const string BehaviorProbeSource = @"
using System;
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
        public int DiagnosticCount { get; set; }
        public bool StartupFaulted { get; set; }
        public bool HasOwnedResources { get; set; }
        public string ConnectionStatus { get; set; }
        public WeakReference Callback { get; set; }
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StartupProbeResult Run(bool throwFromContextNew, bool returnGatewayError)
        {
            UnityEngine.Debug.Reset();
            var nativeApi = new ThrowingStartupNativeApi(throwFromContextNew, returnGatewayError);
            var controller = new FoxgloveRemoteGatewayController
            {
                StartupNativeApiForTests = nativeApi
            };

            Attempt(controller);
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
                Callback = nativeApi.Callback
            };
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
        }

        private sealed class ThrowingStartupNativeApi : IRemoteGatewayStartupNativeApi
        {
            private readonly bool _throwFromContextNew;
            private readonly bool _returnGatewayError;

            internal ThrowingStartupNativeApi(bool throwFromContextNew, bool returnGatewayError)
            {
                _throwFromContextNew = throwFromContextNew;
                _returnGatewayError = returnGatewayError;
            }

            internal int ContextNewCalls { get; private set; }
            internal int GatewayStartCalls { get; private set; }
            internal int ContextFreeCalls { get; private set; }
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
                gateway = IntPtr.Zero;
                var nativeCallbacks = Marshal.PtrToStructure<RemoteGatewayNativeMethods.FoxgloveGatewayCallbacks>(
                    options.Callbacks);
                Callback = new WeakReference(GCHandle.FromIntPtr(nativeCallbacks.Context).Target);
                if (_returnGatewayError)
                    return RemoteGatewayNativeMethods.FoxgloveError.ConfigurationError;

                throw new EntryPointNotFoundException(""injected gateway start failure"");
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
