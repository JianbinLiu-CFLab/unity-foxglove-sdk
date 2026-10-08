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
            int expectedGatewayStopCalls,
            bool expectCallback)
        {
            Assert.Equal(expectedContextNewCalls, ReadProperty<int>(result, "ContextNewCalls"));
            Assert.Equal(expectedGatewayStartCalls, ReadProperty<int>(result, "GatewayStartCalls"));
            Assert.Equal(expectedContextFreeCalls, ReadProperty<int>(result, "ContextFreeCalls"));
            Assert.Equal(expectedGatewayStopCalls, ReadProperty<int>(result, "GatewayStopCalls"));
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
        public static object FindObjectOfTypeResult;
        public static int FindObjectOfTypeCalls;
        public static T FindObjectOfType<T>() where T : Object
        {
            FindObjectOfTypeCalls++;
            return FindObjectOfTypeResult as T;
        }
    }

    public class Component : Object
    {
        public static object GetComponentResult;
        public T GetComponent<T>() where T : class => GetComponentResult as T;
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
        public static Action<Unity.FoxgloveSDK.Core.IFoxgloveMirrorSink> SetMirrorSinkAction;
        public void SetMirrorSink(Unity.FoxgloveSDK.Core.IFoxgloveMirrorSink sink)
        {
            SetMirrorSinkAction?.Invoke(sink);
        }
    }
}
";

    }
}
