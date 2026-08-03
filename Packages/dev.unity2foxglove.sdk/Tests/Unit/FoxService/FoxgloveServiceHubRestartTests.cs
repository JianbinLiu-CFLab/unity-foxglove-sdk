// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Execute FoxServiceHub manager restart ownership without Unity Editor dependencies.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxService
{
    [Trait("Phase", "187")]
    [Trait("Domain", "FoxService")]
    public sealed class FoxgloveServiceHubRestartTests
    {
        [Fact]
        public void ManagerRestartReadvertisesExplicitSourceWithoutFallbackScan()
        {
            var probeType = ProbeAssembly.Value.GetType(
                "Unity.FoxgloveSDK.Components.FoxgloveServiceHubRestartProbe",
                throwOnError: true);
            var result = (int[])probeType
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);

            Assert.Equal(2, result[0]);
            Assert.Equal(1, result[1]);
            Assert.Equal(1, result[2]);
        }

        private static Assembly CompileProbeAssembly()
        {
            var productionSources = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveServiceHub.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveServiceHub.Registration.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveGeneratedServiceDescriptor.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/IFoxgloveServiceSource.cs"
            }.Select(path => CSharpSyntaxTree.ParseText(
                TestSources.Text(path),
                ParseOptions,
                path: path));
            var probe = CSharpSyntaxTree.ParseText(ProbeSource, ParseOptions, path: "FoxgloveServiceHubRestartProbe.cs");
            var compilation = CSharpCompilation.Create(
                "FoxgloveServiceHubRestartProbe_" + Guid.NewGuid().ToString("N"),
                productionSources.Concat(new[] { probe }),
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static MetadataReference[] References()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            Assert.False(string.IsNullOrEmpty(trustedAssemblies));
            var testAssembly = typeof(FoxgloveServiceHubRestartTests).Assembly.Location;

            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.Equals(
                    path,
                    testAssembly,
                    StringComparison.OrdinalIgnoreCase))
                .Append(typeof(JToken).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static readonly CSharpParseOptions ParseOptions =
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);

        private static readonly Lazy<Assembly> ProbeAssembly = new Lazy<Assembly>(CompileProbeAssembly);

        private const string ProbeSource = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityEngine
{
    public class Object
    {
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode)
            where T : Object => Array.Empty<T>();
        public static void DontDestroyOnLoad(Object target) {}
    }

    public class Component : Object
    {
        public GameObject gameObject { get; set; } = new GameObject();
    }

    public class Behaviour : Component
    {
        public bool isActiveAndEnabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour {}

    public sealed class GameObject : Object
    {
        public GameObject(string value = ""probe"") { name = value; }
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public T AddComponent<T>() where T : new() => new T();
    }

    public enum HideFlags { HideAndDontSave }
    public enum FindObjectsInactive { Exclude }
    public enum FindObjectsSortMode { None }
    public enum RuntimeInitializeLoadType { SubsystemRegistration, AfterSceneLoad }

    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string value) {}
    }

    public sealed class SerializeField : Attribute {}

    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) {}
    }

    public static class Time
    {
        public static float deltaTime => 0.1f;
    }

    public static class Debug
    {
        public static void LogWarning(string message) {}
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene {}
    public enum LoadSceneMode { Single }

    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded { add {} remove {} }
        public static event Action<Scene> sceneUnloaded { add {} remove {} }
    }
}

namespace Unity.FoxgloveSDK.Protocol
{
    public sealed class ServiceSchemaDescriptor
    {
        public string Encoding { get; set; }
        public string SchemaName { get; set; }
        public string Schema { get; set; }
    }

    public sealed class ServiceDescriptor
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public ServiceSchemaDescriptor Request { get; set; }
        public ServiceSchemaDescriptor Response { get; set; }
    }
}

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager : UnityEngine.MonoBehaviour
    {
        private readonly HashSet<uint> _activeIds = new HashSet<uint>();
        private uint _nextId = 1;

        public bool IsRunning { get; set; } = true;
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }
        public int ActiveCount => _activeIds.Count;

        public uint RegisterService(
            Protocol.ServiceDescriptor descriptor,
            Func<JToken, JToken> handler)
        {
            RegisterCalls++;
            var id = _nextId++;
            _activeIds.Add(id);
            return id;
        }

        public void UnregisterService(uint id)
        {
            if (_activeIds.Remove(id))
                UnregisterCalls++;
        }
    }

    public sealed partial class FoxgloveServiceHub
    {
        private readonly HashSet<string> _warnedFailures = new HashSet<string>();

        private void WarnOnce(IFoxgloveServiceSource source, string serviceName, string message) {}

        internal static void ResetForProbe() => ResetStaticState();

        internal void ConfigureForProbe(FoxgloveManager manager)
        {
            _manager = manager;
            _enableFallbackSceneScan = false;
            Awake();
        }

        internal void TickForProbe() => Update();
        internal void DestroyForProbe() => OnDestroy();
    }

    public static class FoxgloveServiceHubRestartProbe
    {
        public static int[] Run()
        {
            FoxgloveServiceHub.ResetForProbe();
            var manager = new FoxgloveManager();
            var hub = new FoxgloveServiceHub();
            var source = new ProbeSource();
            hub.ConfigureForProbe(manager);

            FoxgloveServiceHub.RegisterSource(source);
            hub.TickForProbe();
            manager.IsRunning = false;
            hub.TickForProbe();
            manager.IsRunning = true;
            hub.TickForProbe();

            var result = new[] { manager.RegisterCalls, manager.UnregisterCalls, manager.ActiveCount };
            FoxgloveServiceHub.UnregisterSource(source);
            hub.DestroyForProbe();
            return result;
        }

        private sealed class ProbeSource : UnityEngine.MonoBehaviour, IFoxgloveServiceSource
        {
            private readonly IReadOnlyList<FoxgloveGeneratedServiceDescriptor> _services =
                new[]
                {
                    new FoxgloveGeneratedServiceDescriptor(
                        ""/phase187/restart"",
                        ""Phase187.Restart"",
                        ""restart probe"",
                        ""Phase187.Restart.Request"",
                        ""Phase187.Restart.Response"",
                        request => JValue.CreateNull())
                };

            public IReadOnlyList<FoxgloveGeneratedServiceDescriptor> FoxgloveServices => _services;
        }
    }
}
";
    }
}
