// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep native R2FU bridge and sensor ownership fail-closed.

using System;
using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "R2FU ownership")]
    public sealed class R2fuOwnershipReviewTests
    {
        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorParticipantCreationRejectsAnExistingPublisher(string distro)
        {
            var method = Method(
                $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs",
                "CreateROSParticipants");
            var publisherCreation = method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString().Contains("CreateSensorPublisher", StringComparison.Ordinal));
            var ownershipGuard = method.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .FirstOrDefault(statement =>
                    statement.Condition.ToString().Contains("publisher", StringComparison.Ordinal)
                    && statement.Statement.DescendantNodesAndSelf().OfType<ThrowStatementSyntax>().Any());

            Assert.NotNull(ownershipGuard);
            Assert.True(ownershipGuard.SpanStart < publisherCreation.SpanStart);
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorPublishNeverExecutesInsideReadingsLock(string distro)
        {
            var method = Method(
                $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs",
                "ExecutorThreadSensorPublishAction");
            var publish = method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression.ToString().EndsWith(
                        ".Publish",
                        StringComparison.Ordinal));

            Assert.Empty(publish.Ancestors().OfType<LockStatementSyntax>());
        }

        [Fact]
        public void JazzySensorDefersPublisherRemovalUntilPublishReturns()
        {
            const string path =
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs";
            var executor = Method(path, "ExecutorThreadSensorPublishAction");
            var dispose = Method(path, "DisposeRosParticipants");
            var completion = Method(path, "CompletePublisherCall");

            Assert.Contains(
                "CompletePublisherCall(ownershipToUse);",
                executor.ToFullString(),
                StringComparison.Ordinal);
            Assert.NotEmpty(
                executor.DescendantNodes().OfType<FinallyClauseSyntax>());
            Assert.Contains(
                "ownershipToRetire.Retired = true;",
                dispose.ToFullString(),
                StringComparison.Ordinal);
            var remove = completion.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString()
                    .Contains("RemovePublisherSafely", StringComparison.Ordinal));
            Assert.Empty(remove.Ancestors().OfType<LockStatementSyntax>());
        }

        [Fact]
        public async Task JazzySensorCanRebindWhileTheRetiredPublisherFinishes()
        {
            var assembly = CompileJazzySensorProbe();
            var sensorType = assembly.GetType("ROS2.SensorProbe", throwOnError: true);
            var componentType = assembly.GetType("ROS2.ROS2UnityComponent", throwOnError: true);
            var nodeType = assembly.GetType("ROS2.ROS2Node", throwOnError: true);
            var messageType = assembly.GetType("ROS2.TestMessage", throwOnError: true);
            var hooksType = assembly.GetType("ROS2.SensorTestHooks", throwOnError: true);
            var sensor = Activator.CreateInstance(sensorType);
            var component = Activator.CreateInstance(componentType);
            var node = Activator.CreateInstance(nodeType);
            var releasePublish = Assert.IsType<ManualResetEventSlim>(
                hooksType.GetField("ReleasePublish")?.GetValue(null));
            var publishEntered = Assert.IsType<ManualResetEventSlim>(
                hooksType.GetField("PublishEntered")?.GetValue(null));
            var create = sensorType.GetMethod("CreateROSParticipants");
            var sensorBase = sensorType.BaseType;
            var dispose = sensorBase?.GetMethod(
                "DisposeRosParticipants",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                create?.Invoke(sensor, new[] { component, node, "robot" });
                sensorBase?.GetField("readings", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(sensor, Activator.CreateInstance(messageType));
                sensorBase?.GetField("newReadings", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(sensor, true);
                var executable = Assert.IsType<Action>(
                    componentType.GetProperty("Executable")?.GetValue(component));
                var publishTask = Task.Run(executable);
                Assert.True(publishEntered.Wait(TimeSpan.FromSeconds(2)));

                dispose?.Invoke(sensor, null);
                var rebindError = Record.Exception(
                    () => create?.Invoke(sensor, new[] { component, node, "robot" }));

                Assert.Null(rebindError);
                Assert.Equal(2, nodeType.GetProperty("CreateCount")?.GetValue(node));

                releasePublish.Set();
                var completed = await Task.WhenAny(
                    publishTask,
                    Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.Same(publishTask, completed);
                await publishTask;

                var created = Assert.IsAssignableFrom<IList>(
                    nodeType.GetProperty("CreatedPublishers")?.GetValue(node));
                var removed = Assert.IsAssignableFrom<IList>(
                    nodeType.GetProperty("RemovedPublishers")?.GetValue(node));
                Assert.Equal(2, created.Count);
                Assert.Single(removed.Cast<object>());
                Assert.Same(created[0], removed[0]);
                Assert.NotSame(created[1], removed[0]);
            }
            finally
            {
                releasePublish.Set();
                dispose?.Invoke(sensor, null);
                releasePublish.Dispose();
                publishEntered.Dispose();
            }
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorBufferedReadingCanBeClearedDuringTeardown(string distro)
        {
            var root = CSharpSyntaxTree.ParseText(TestSources.Text(
                    $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs"))
                .GetCompilationUnitRoot();
            var sensor = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "Sensor");
            var constraints = sensor.ConstraintClauses.Single().ToFullString();
            var probe = $@"
namespace ROS2
{{
    public interface MessageWithHeader {{ }}

    public sealed class SensorProbe<T> {constraints}
    {{
        private T readings;

        public void Clear()
        {{
            readings = null;
        }}
    }}
}}";
            var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.False(string.IsNullOrEmpty(trusted));
            var compilation = CSharpCompilation.Create(
                $"{distro}-sensor-teardown-probe",
                new[] { CSharpSyntaxTree.ParseText(probe) },
                trusted.Split(Path.PathSeparator)
                    .Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(
                errors.Length == 0,
                string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }

        [Theory]
        [InlineData("Ros2ForUnityCameraNativeBridge.cs")]
        [InlineData("Ros2ForUnityImuNativeBridge.cs")]
        [InlineData("Ros2ForUnityPackedPointCloudBridge.cs")]
        [InlineData("Ros2ForUnityTransformNativeBridge.cs")]
        public void NativeBridgeNeverRebindsToAnUnrelatedRos2Component(string fileName)
        {
            var method = Method(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + fileName,
                "TryGetExistingRos2Unity");
            var source = method.ToFullString();

            Assert.DoesNotContain("FindFirstObjectByType", source, StringComparison.Ordinal);
            Assert.Contains("GetComponent<ROS2UnityComponent>()", source, StringComparison.Ordinal);
            Assert.Contains("BeginShutdown()", source, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax Method(string path, string name)
        {
            var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY");
            var root = CSharpSyntaxTree.ParseText(TestSources.Text(path), parseOptions)
                .GetCompilationUnitRoot();
            return root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == name && method.Body != null);
        }

        private static Assembly CompileJazzySensorProbe()
        {
            const string path =
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs";
            const string probe = @"
using System;
using System.Collections.Generic;
using System.Threading;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Debug { public static void LogWarning(object value) { } }
    public static class Time { public static float fixedDeltaTime = 0.02f; }
}

namespace ROS2
{
    public interface MessageWithHeader { void SetHeaderFrame(string value); }
    public sealed class TestMessage : MessageWithHeader
    {
        public void SetHeaderFrame(string value) { }
    }

    public static class SensorTestHooks
    {
        public static readonly ManualResetEventSlim PublishEntered = new ManualResetEventSlim(false);
        public static readonly ManualResetEventSlim ReleasePublish = new ManualResetEventSlim(false);
    }

    public sealed class Publisher<T>
    {
        public void Publish(T value)
        {
            SensorTestHooks.PublishEntered.Set();
            if (!SensorTestHooks.ReleasePublish.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException(""Timed out waiting to release the publisher probe."");
        }
    }

    public sealed class ROS2UnityComponent
    {
        public Action Executable { get; private set; }
        public bool Ok() => true;
        public void RegisterExecutable(Action executable) => Executable = executable;
        public void UnregisterExecutable(Action executable)
        {
            if (Executable == executable)
                Executable = null;
        }
    }

    public sealed class ROS2Node
    {
        public bool IsDisposed { get; set; }
        public int CreateCount { get; private set; }
        public List<object> CreatedPublishers { get; } = new List<object>();
        public List<object> RemovedPublishers { get; } = new List<object>();

        public Publisher<T> CreateSensorPublisher<T>(string topic)
        {
            var publisher = new Publisher<T>();
            CreateCount++;
            CreatedPublishers.Add(publisher);
            return publisher;
        }

        public bool TryUpdateROSTimestamp(ref MessageWithHeader value) => true;
        public void RemovePublisher<T>(Publisher<T> publisher) => RemovedPublishers.Add(publisher);
    }

    public sealed class SensorProbe : Sensor<TestMessage>
    {
        public SensorProbe()
        {
            topicName = ""topic"";
            publishing = true;
        }

        protected override TestMessage AcquireValue() => new TestMessage();
        protected override bool HasNewData() => true;
    }
}
";
            var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.False(string.IsNullOrEmpty(trusted));
            var compilation = CSharpCompilation.Create(
                "jazzy-sensor-ownership-" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(TestSources.Text(path)),
                    CSharpSyntaxTree.ParseText(probe)
                },
                trusted.Split(Path.PathSeparator)
                    .Select(reference => MetadataReference.CreateFromFile(reference)),
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
            return Assembly.Load(image.ToArray());
        }
    }
}
