using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class WorkstreamFClosureTests
    {
        [Fact]
        public void NativeCleanupRetainsOwnerAfterInjectedFailure()
        {
            var handle = new object();
            var attempts = 0;

            Assert.False(RetryableNativeCleanup.TryRemove(
                ref handle,
                _ =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("injected");
                },
                _ => { }));
            Assert.NotNull(handle);

            Assert.True(RetryableNativeCleanup.TryRemove(ref handle, _ => attempts++, _ => { }));
            Assert.Null(handle);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public void RunInBackgroundLeaseDoesNotClobberAnotherOwner()
        {
            var value = false;
            var lease = RunInBackgroundLease.Acquire(() => value, next => value = next);
            Assert.True(value);

            value = false;
            Assert.False(lease.Release());
            Assert.False(value);
        }

        [Fact]
        public void RunInBackgroundLeaseKeepsGlobalSettingForOverlappingOwners()
        {
            var value = false;
            var first = RunInBackgroundLease.Acquire(() => value, next => value = next);
            var second = RunInBackgroundLease.Acquire(() => value, next => value = next);

            Assert.True(first.Release());
            Assert.True(value);
            Assert.True(second.Release());
            Assert.False(value);
        }

        [Fact]
        public void WiringGenerationRejectsQueuedCallbacksAfterReenable()
        {
            var gate = new WiringGenerationGate();
            var first = gate.Activate();
            gate.Invalidate();
            var second = gate.Activate();

            Assert.NotEqual(first, second);
            Assert.False(gate.IsCurrent(first));
            Assert.True(gate.IsCurrent(second));
        }

        [Fact]
        public void DemoWiringOwnershipRunsEveryCleanupAfterAFailure()
        {
            var order = new List<string>();
            var ownership = new DemoWiringOwnership();
            ownership.Add(() =>
            {
                order.Add("first");
                throw new InvalidOperationException("first cleanup failure");
            });
            ownership.Add(() => order.Add("second"));
            ownership.Add(() =>
            {
                order.Add("third");
                throw new InvalidOperationException("third cleanup failure");
            });

            var error = Assert.Throws<InvalidOperationException>(() => ownership.Dispose());

            Assert.Equal("third cleanup failure", error.Message);
            Assert.Equal(new[] { "third", "second", "first" }, order);
            ownership.Dispose();
            Assert.Equal(new[] { "third", "second", "first" }, order);
        }

        [Fact]
        public void OptionalStartExecutorGateReportsMissingHookOnceAndPreservesTruthfulState()
        {
            var gate = new OptionalStartExecutorGate();
            var warnings = 0;
            var starts = 0;

            Assert.False(gate.TryStart(null, () => warnings++));
            Assert.False(gate.TryStart(() => starts++, () => warnings++));
            Assert.True(gate.Attempted);
            Assert.False(gate.Started);
            Assert.Equal(1, warnings);
            Assert.Equal(0, starts);

            gate.Reset();
            Assert.True(gate.TryStart(() => starts++, () => warnings++));
            Assert.True(gate.TryStart(() => starts++, () => warnings++));
            Assert.True(gate.Started);
            Assert.Equal(1, starts);
            Assert.Equal(1, warnings);
        }

        [Fact]
        public void DemoInputScriptsCompileWithOnlyTheLegacyInputBackend()
        {
            var stub = @"
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute { }
    public class MonoBehaviour
    {
        public Transform transform { get; } = new Transform();
        protected T FindFirstObjectByType<T>() where T : class => null;
        protected Coroutine StartCoroutine(System.Collections.IEnumerator routine) => null;
        protected void StopCoroutine(Coroutine routine) { }
    }
    public sealed class Coroutine { }
    public sealed class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public sealed class Camera { public static Camera main; public Transform transform { get; } = new Transform(); }
    public sealed class Transform
    {
        public Vector3 position;
        public Vector3 localScale;
        public Vector3 up;
        public Vector3 right;
        public void Rotate(Vector3 axis, float angle, Space relativeTo) { }
    }
    public sealed class GameObject { }
    public struct Vector2
    {
        public static Vector2 zero => new Vector2();
        public float x;
        public float y;
        public static implicit operator Vector2(Vector3 value) => new Vector2 { x = value.x, y = value.y };
        public static Vector2 operator -(Vector2 left, Vector2 right) => new Vector2 { x = left.x - right.x, y = left.y - right.y };
    }
    public struct Vector3
    {
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float x;
        public float y;
        public float z;
        public static Vector3 operator +(Vector3 left, Vector3 right) => new Vector3();
        public static Vector3 operator *(Vector3 left, float right) => new Vector3();
    }
    public enum Space { World }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => value;
    }
    public enum KeyCode { F7, F8 }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static Vector2 mouseScrollDelta;
        public static bool GetMouseButton(int button) => false;
        public static bool GetKeyDown(KeyCode key) => false;
    }
    public static class Debug
    {
        public static void Log(object message) { }
    }
}

public sealed class FoxgloveDemoSetup : UnityEngine.MonoBehaviour
{
    public const float ScaleMinimum = 0.2f;
    public const float ScaleMaximum = 5f;
    public void SyncScaleToParameter(float value) { }
}

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager
    {
        public bool IsRunning;
        public void PublishWarningStatus(string message, string id) { }
        public void RemoveStatus(string id) { }
    }
}
";
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "ENABLE_LEGACY_INPUT_MANAGER" });
            var trees = new[]
            {
                CSharpSyntaxTree.ParseText(
                    TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/MouseDragCube.cs"),
                    parseOptions),
                CSharpSyntaxTree.ParseText(
                    TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveStatusSmoke.cs"),
                    parseOptions),
                CSharpSyntaxTree.ParseText(stub, parseOptions)
            };
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "UnityDemoLegacyInputFixture",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.Empty(errors);
        }

        [Fact]
        public void MazeCoordinateAuthorityAppliesNonDefaultModeToBothDirections()
        {
            var fields = new Dictionary<string, CoordinateMode>();
            CoordinateModeAuthority.Apply((name, value) => fields[name] = value, CoordinateMode.LeftHand);

            Assert.Equal(CoordinateMode.LeftHand, fields[CoordinateModeAuthority.OutputFieldName]);
            Assert.Equal(CoordinateMode.LeftHand, fields[CoordinateModeAuthority.InputFieldName]);
        }

        [Fact]
        public void DynamicTfAnchorPreservesNonIdentityPose()
        {
            var pose = PackedPointCloudTfAnchorResolver.Resolve(
                positionX: 1f,
                positionY: 2f,
                positionZ: 3f,
                rotationX: 0f,
                rotationY: 0f,
                rotationZ: 0.70710677f,
                rotationW: 0.70710677f,
                offsetX: 0.5f,
                offsetY: -0.25f,
                offsetZ: 1f,
                offsetRotationX: 0f,
                offsetRotationY: 0f,
                offsetRotationZ: 0f,
                offsetRotationW: 1f);

            Assert.Equal(3.5f, pose.TranslationX, 4);
            Assert.Equal(-1.25f, pose.TranslationY, 4);
            Assert.Equal(3f, pose.TranslationZ, 4);
            Assert.NotEqual(1f, pose.RotationW);
            Assert.NotEqual(0f, pose.RotationX);
        }

        [Fact]
        public void ProductionSourcesUseClosureGuards()
        {
            var fullDemo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            var importedFullDemo = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var bridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            var phase138 = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/Phase138VirtualLidarPointCloud2Smoke.cs");
            var batch = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");

            foreach (var source in new[] { fullDemo, importedFullDemo })
            {
                Assert.Contains("WiringGenerationGate", source, StringComparison.Ordinal);
                Assert.Contains("var wiringGeneration = _wiringGeneration.Activate()", source, StringComparison.Ordinal);
                Assert.Contains("OnParameterChangedForRuntime(rt, wiringGeneration", source, StringComparison.Ordinal);
                Assert.Contains("_wiringGeneration.IsCurrent(wiringGeneration)", source, StringComparison.Ordinal);
                Assert.DoesNotContain("_wiringGeneration.Capture()", source, StringComparison.Ordinal);
                Assert.Contains("DemoWiringOwnership", source, StringComparison.Ordinal);
                Assert.Contains("var committed = false;", source, StringComparison.Ordinal);
                Assert.Contains("if (!committed)", source, StringComparison.Ordinal);
                Assert.Contains("ownership?.Dispose()", source, StringComparison.Ordinal);
            }
            Assert.Contains("PackedPointCloudTfAnchorResolver.Resolve", bridge, StringComparison.Ordinal);
            Assert.DoesNotContain("TransformStamped", phase138, StringComparison.Ordinal);
            Assert.Contains("RunInBackgroundLease", batch, StringComparison.Ordinal);

            var phase110 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");
            var phase127 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            Assert.Contains("OptionalStartExecutorGate", phase110, StringComparison.Ordinal);
            Assert.Contains("OptionalStartExecutorGate", phase127, StringComparison.Ordinal);
        }

        [Fact]
        public void MazeSamplesUseCoordinateModeAuthorityForBothDirections()
        {
            var sources = new[]
            {
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs"),
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs"),
                TestSources.Text("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.6/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs"),
                TestSources.Text("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.6/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs")
            };

            foreach (var source in sources)
            {
                var entryPoint = source.Contains("public static void BuildScene()", StringComparison.Ordinal)
                    ? "public static void BuildScene()"
                    : "private void Start()";
                var entryPointBody = TestSources.ExtractMethod(source, entryPoint);
                Assert.Contains("CoordinateModeAuthority.Apply<CoordinateMode>", entryPointBody, StringComparison.Ordinal);
                Assert.DoesNotContain("\"_coordinateMode\"", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void DemoWiringOwnershipSupportsDisableAndReenableWithoutStaleResources()
        {
            var store = new FoxgloveParameterStore();
            var activeCallbacks = new HashSet<string>(StringComparer.Ordinal);

            DemoWiringOwnership Enable(string generation)
            {
                var ownership = new DemoWiringOwnership();
                ownership.Add(store.RegisterOwned("/" + generation + "/color", new JValue(1), "number", true));
                ownership.Add(store.RegisterOwned("/" + generation + "/scale", new JValue(1), "number", true));
                activeCallbacks.Add(generation + ":parameter");
                ownership.Add(() => activeCallbacks.Remove(generation + ":parameter"));
                activeCallbacks.Add(generation + ":client");
                ownership.Add(() => activeCallbacks.Remove(generation + ":client"));
                return ownership;
            }

            var first = Enable("first");
            Assert.Equal(2, store.GetAllWireParameters().Count);
            Assert.Equal(2, activeCallbacks.Count);

            first.Dispose();
            Assert.Empty(store.GetAllWireParameters());
            Assert.Empty(activeCallbacks);

            var second = Enable("second");
            Assert.Equal(new[] { "/second/color", "/second/scale" }, store.GetAllWireParameters().Select(x => x.Name).OrderBy(x => x));
            Assert.Equal(new[] { "second:client", "second:parameter" }, activeCallbacks.OrderBy(x => x));

            second.Dispose();
            second.Dispose();
            Assert.Empty(store.GetAllWireParameters());
            Assert.Empty(activeCallbacks);
        }

        [Fact]
        public void DemoWiringBootstrapRollsBackPartiallyAcquiredResourcesBeforeRetry()
        {
            var store = new FoxgloveParameterStore();
            var activeCallbacks = new HashSet<string>(StringComparer.Ordinal);
            var committed = false;
            var ownership = new DemoWiringOwnership();

            try
            {
                ownership.Add(store.RegisterOwned("/cube/color", new JValue(1), "number", true));
                ownership.Add(store.RegisterOwned("/cube/scale", new JValue(1), "number", true));
                activeCallbacks.Add("parameter");
                ownership.Add(() => activeCallbacks.Remove("parameter"));
                throw new InvalidOperationException("injected initial-state failure");
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                if (!committed)
                    ownership.Dispose();
            }

            Assert.Empty(store.GetAllWireParameters());
            Assert.Empty(activeCallbacks);

            var retry = new DemoWiringOwnership();
            retry.Add(store.RegisterOwned("/cube/color", new JValue(2), "number", true));
            activeCallbacks.Add("retry");
            retry.Add(() => activeCallbacks.Remove("retry"));
            Assert.Single(store.GetAllWireParameters());
            Assert.Equal(new[] { "retry" }, activeCallbacks);
            retry.Dispose();
            Assert.Empty(store.GetAllWireParameters());
            Assert.Empty(activeCallbacks);
        }
    }
}
