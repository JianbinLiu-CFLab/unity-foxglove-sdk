// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins native ROS2 binding validation, message metadata identity, and host parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "179-B")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunRos2BindingGenerationTests
    {
        [Fact]
        public void WebSocketOnlyGeneratedSourceDoesNotEmitNativeBusDemandProbe()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "WebSocketOnlySource",
                new[]
                {
                    new FoxgloveSourceEmitter.TopicMember(
                        "Value",
                        "int",
                        "/phase181/websocket-only",
                        10f,
                        "phase181.WebSocketOnly"),
                });

            Assert.DoesNotContain("IFoxgloveTopicBusDemandSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxgloveLog_HasBusSubscribers", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedInputTriggerNamesAreUniqueAcrossSanitizedNativeMembers()
        {
            var first = BuildNativeTriggerMember("_command", "/phase184/first", 0);
            var second = BuildNativeTriggerMember("command", "/phase184/second", 1);

            var source = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[] { first, second }).Types.Single());

            Assert.Equal(
                1,
                CountOccurrences(source, "public bool FoxRun_Apply_command()"));
            Assert.Equal(
                1,
                CountOccurrences(source, "public bool FoxRun_Apply_command_2()"));
        }

        [Fact]
        public void NativeTriggerSourceExposesDirectionalApplyAndBulkMethods()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase184",
                "NativeTriggerSource",
                new[]
                {
                    new FoxgloveSourceEmitter.TopicMember(
                        "_incoming",
                        "vendor_msgs.msg.Command",
                        "/phase184/native-trigger",
                        0f,
                        "vendor_msgs/msg/Command",
                        policy: (int)FoxRunPolicy.Trigger,
                        tolerance: 0f,
                        mode: (int)FoxRunFlow.Subscribe,
                        canonicalType: "vendor_msgs/msg/Command",
                        encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                        source:
                            FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                        ros2Qos: FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                        generatesWebSocketCodec: false,
                        generatesRos2NativeRegistration: true,
                        ros2MessageShape: ValidShape(),
                        hasExplicitHz: false,
                        onlyIf: "CanApply",
                        conditionMemberKind: FoxRunConditionMemberKind.Method)
                });

            Assert.Contains("public bool FoxRun_Apply_incoming()", source, StringComparison.Ordinal);
            Assert.Contains("public bool FoxRun_ApplyAll()", source, StringComparison.Ordinal);
            Assert.Contains("() => CanApply()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!CanApply()) return;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRun_Trigger_", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRun_TriggerAll", source, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("invalid", "inherit", (int)FoxRunFlow.Subscribe, "FOXRUN204")]
        [InlineData("ros2-native", "inherit", (int)FoxRunFlow.Publish, "FOXRUN612")]
        [InlineData("ros2-native", "json", (int)FoxRunFlow.Subscribe, "FOXRUN612")]
        [InlineData("ros2-native", "protobuf", (int)FoxRunFlow.Subscribe, "FOXRUN612")]
        public void DirectionalEndpointPolicyErrorsUseTargetedDiagnostics(
            string provider,
            string encoding,
            int mode,
            string expectedId)
        {
            var presence = FoxRunNamedArgumentPresence.Source;
            if (!string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                presence |= FoxRunNamedArgumentPresence.Encoding;
            }

            var member = BuildMember(
                provider,
                encoding,
                mode,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                ValidShape(),
                namedArgumentPresence: presence);

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { member }));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == expectedId);
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
        }

        [Fact]
        public void NativeSourceSupportsPublishAndSubscribeWithInheritedEncoding()
        {
            var member = BuildMember(
                FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                (int)FoxRunFlow.PublishAndSubscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                ValidShape());

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { member }));

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN612");
        }

        [Fact]
        public void ExplicitWebSocketQosIsIgnoredWithWarningInsteadOfNativeFallback()
        {
            var member = BuildMember(
                FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                FoxRunGenerationDescriptorConstants.JsonEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                ros2MessageShape: null,
                generatesWebSocketCodec: true,
                generatesNativeRegistration: false);

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { member }));

            var warning = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FOXRUN213");
            Assert.Equal("Warning", warning.Severity);
            Assert.Contains("ignored", warning.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NativeShapeDiagnosticsOnlyApplyWhenTheProviderCanRequireNativeCapability()
        {
            var invalidShape = InvalidNativeShape();
            var explicitWebSocket = BuildMember(
                FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                FoxRunGenerationDescriptorConstants.JsonEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                invalidShape,
                generatesWebSocketCodec: true,
                generatesNativeRegistration: false);
            var inheritedWithWebSocket = BuildMember(
                FoxRunGenerationDescriptorConstants.InheritSource,
                FoxRunGenerationDescriptorConstants.JsonEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                invalidShape,
                generatesWebSocketCodec: true,
                generatesNativeRegistration: false);
            var inheritedNativeOnly = BuildMember(
                FoxRunGenerationDescriptorConstants.InheritSource,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                invalidShape,
                generatesWebSocketCodec: false,
                generatesNativeRegistration: false);
            var explicitNative = BuildMember(
                FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                invalidShape,
                generatesWebSocketCodec: false,
                generatesNativeRegistration: false);

            var webSocketDiagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { explicitWebSocket }));
            var inheritedWebSocketDiagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { inheritedWithWebSocket }));
            var inheritedNativeDiagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { inheritedNativeOnly }));
            var explicitNativeDiagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { explicitNative }));

            Assert.DoesNotContain(webSocketDiagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
            Assert.DoesNotContain(inheritedWebSocketDiagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
            Assert.Contains(inheritedNativeDiagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
            Assert.Contains(explicitNativeDiagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
        }

        [Fact]
        public void InheritedDualCapabilityStillValidatesAdvertisedWebSocketShape()
        {
            var protobufShape = FoxRunProtobufTypeShape.Object(
                "vendor_msgs.msg.Command",
                new[]
                {
                    new FoxRunProtobufTypeField(
                        "value",
                        "Value",
                        FoxRunProtobufTypeShape.Canonical("int32"),
                        canAssign: false)
                });
            var member = BuildMember(
                FoxRunGenerationDescriptorConstants.InheritSource,
                FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                ValidShape(),
                generatesWebSocketCodec: true,
                generatesNativeRegistration: true,
                protobufFieldNumber: 19000,
                protobufTypeShape: protobufShape);

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { member }));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN603");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200");
        }

        [Fact]
        public void RoslynAndReflectionBuildersRequireRos2csCommonMessageMetadataIdentity()
        {
            var valid = CompileFixture(@"
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; set; }
    }
}");
            var unrelated = CompileFixture(@"
namespace User { public interface Message { } }
namespace vendor_msgs.msg
{
    public sealed class Command : User.Message
    {
        public Command() { }
        public int Value { get; set; }
    }
}");
            var wrongAssembly = CompileFixture(@"
namespace ROS2 { public interface Message { } }
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; set; }
    }
}");

            AssertHostParity(valid, implementsRos2Message: true, expectedCanonicalType: "vendor_msgs/msg/Command");
            AssertHostParity(unrelated, implementsRos2Message: false, expectedCanonicalType: string.Empty);
            AssertHostParity(wrongAssembly, implementsRos2Message: false, expectedCanonicalType: string.Empty);
        }

        [Fact]
        public void MessageWithoutPublicParameterlessConstructorFailsIdenticallyAcrossHosts()
        {
            var fixture = CompileFixture(@"
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        private Command() { }
        public int Value { get; set; }
    }
}");

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
                Assert.Contains(shape.Diagnostics, diagnostic => diagnostic.StartsWith("FOXRUN208|vendor_msgs.msg.Command|", StringComparison.Ordinal)));
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Fact]
        public void AbstractMessageCannotSatisfyNativeConstructorContractAcrossHostsOrGenerator()
        {
            const string source = @"
namespace vendor_msgs.msg
{
    public abstract class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; set; }
    }
}";
            var fixture = CompileFixture(source);

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
            {
                Assert.False(shape.HasPublicParameterlessConstructor);
                Assert.Contains(shape.Diagnostics, diagnostic =>
                    diagnostic.StartsWith("FOXRUN208|vendor_msgs.msg.Command|", StringComparison.Ordinal));
            });
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);

            var generated = RunGenerator(source, "vendor_msgs/msg/Command");
            Assert.Contains(generated.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN208");
        }

        [Theory]
        [InlineData(false, "vendor_msgs.msg.Command[]")]
        [InlineData(true, "System.Collections.Generic.List<vendor_msgs.msg.Command>")]
        public void TopLevelMessageCollectionsCannotBecomeNativeElementSubscriptions(
            bool list,
            string sourceTypeName)
        {
            var fixture = CompileFixture(ValidMessageSource(
                "vendor_msgs.msg",
                "public int Value { get; set; }",
                publicConstructor: true,
                includeReceiver: false));
            var roslynType = list
                ? fixture.Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1").Construct(fixture.Symbol)
                : (ITypeSymbol)fixture.Compilation.CreateArrayTypeSymbol(fixture.Symbol);
            var reflectionType = list
                ? typeof(List<>).MakeGenericType(fixture.RuntimeType)
                : fixture.RuntimeType.MakeArrayType();
            var shapes = new[]
            {
                FoxRunRoslynRos2MessageShapeBuilder.Build(roslynType, fixture.Compilation),
                FoxRunReflectionRos2MessageShapeBuilder.Build(reflectionType)
            };

            Assert.All(shapes, shape =>
            {
                Assert.False(shape.ImplementsRos2Message);
                Assert.NotEqual("global::vendor_msgs.msg.Command", shape.FullyQualifiedTypeName);
                Assert.Contains(shape.Diagnostics, diagnostic =>
                    diagnostic.StartsWith("FOXRUN207|", StringComparison.Ordinal));
            });

            var reflectionMember = new FoxrunCodeGenerator.MemberData(
                "_incoming",
                reflectionType,
                "field",
                "Demo",
                "Receiver",
                "/command",
                10f,
                "vendor_msgs/msg/Command",
                mode: (int)FoxRunFlow.Subscribe,
                source: 2).ToReflectionMember();
            Assert.False(reflectionMember.GeneratesRos2NativeRegistration);
            Assert.False(reflectionMember.Ros2MessageShape.ImplementsRos2Message);

            var generated = RunGenerator(
                ValidMessageSource(
                    "vendor_msgs.msg",
                    "public int Value { get; set; }",
                    publicConstructor: true),
                "vendor_msgs/msg/Command",
                messageTypeName: sourceTypeName);
            Assert.Contains(generated.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN207");
            Assert.DoesNotContain(
                generated.Results.SelectMany(result => result.GeneratedSources),
                source => source.SourceText.ToString().Contains(
                    "\"generatesRos2NativeRegistration\":true",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                generated.Results.SelectMany(result => result.GeneratedSources),
                source => source.SourceText.ToString().Contains(
                    "Register<global::vendor_msgs.msg.Command>",
                    StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("vendor_msgs.srv.Command")]
        [InlineData("vendor_msgs.action.Command")]
        [InlineData("vendor_msgs.deep.msg.Command")]
        public void ServiceActionAndNestedNamespacesAreNotInferredAsMessages(string metadataName)
        {
            var lastDot = metadataName.LastIndexOf('.');
            var ns = metadataName.Substring(0, lastDot);
            var fixture = CompileFixture(@"
namespace " + ns + @"
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; set; }
    }
}", metadataName);

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
            {
                Assert.Equal(string.Empty, shape.CanonicalRosType);
                Assert.Contains(shape.Diagnostics, diagnostic => diagnostic.StartsWith("FOXRUN209|" + metadataName + "|", StringComparison.Ordinal));
            });
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Fact]
        public void ExplicitNativeSchemaMustMatchValidatedCanonicalRosType()
        {
            var member = BuildMember(
                FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                mode: (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                ValidShape(),
                schemaName: "other_msgs/msg/Command");

            var diagnostic = Assert.Single(
                FoxRunGenerationModelValidator.Validate(FoxRunGenerationModel.FromMembers(new[] { member })),
                item => item.Id == "FOXRUN210");
            Assert.Contains("vendor_msgs/msg/Command", diagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SupportedNestedSequenceAndGetterOnlyFixedArrayShapesMatchAcrossHosts()
        {
            var fixture = CompileFixture(@"
namespace vendor_msgs.msg
{
    public enum State { Unknown = 0, Active = 1 }
    public sealed class Child : ROS2.Message
    {
        public Child() { }
        public double X { get; set; }
    }
    public sealed class Command : ROS2.Message
    {
        public Command() { Covariance = new double[9]; }
        public Child Child { get; set; }
        public float[] Values { get; set; }
        public System.Collections.Generic.List<int> Buttons { get; set; }
        public double[] Covariance { get; }
        public State State { get; set; }
    }
}");

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape => Assert.Empty(shape.Diagnostics));
            Assert.Equal(shapes[0].CopyShapeIdentity, shapes[1].CopyShapeIdentity);
            Assert.Equal(
                new[] { "Buttons", "Child", "Covariance", "State", "Values" },
                shapes[0].Members.Select(member => member.Name));
            var fixedArray = Assert.Single(shapes[0].Members, member => member.Name == "Covariance");
            Assert.Equal(FoxRunRos2MessageMemberKind.Sequence, fixedArray.Kind);
            Assert.Equal(FoxRunRos2SequenceRepresentation.FixedArray, fixedArray.SequenceRepresentation);
            Assert.True(fixedArray.CanRead);
            Assert.False(fixedArray.CanWrite);
            Assert.Equal(0, fixedArray.FixedSize);
        }

        [Fact]
        public void GetterOnlyScalarFailsWithInboundWritableDiagnosticAndCanonicalPath()
        {
            var fixture = CompileFixture(@"
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; }
    }
}");

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
                Assert.Contains(shape.Diagnostics, diagnostic => diagnostic.StartsWith("FOXRUN203|vendor_msgs.msg.Command.Value|", StringComparison.Ordinal)));
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Fact]
        public void InitOnlyScalarFailsIdenticallyAcrossHosts()
        {
            var fixture = CompileFixture(@"
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value { get; init; }
    }
}");

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
                Assert.Contains(shape.Diagnostics, diagnostic =>
                    diagnostic.StartsWith("FOXRUN203|vendor_msgs.msg.Command.Value|", StringComparison.Ordinal)));
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Fact]
        public void GeneratorNativeDiagnosticPreservesTheReflectionCanonicalNestedPath()
        {
            const string source = @"
namespace vendor_msgs.msg
{
    public sealed class Child : ROS2.Message
    {
        public Child() { }
        public int Value { get; }
    }
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public Child Child { get; set; }
    }
}";
            var fixture = CompileFixture(source);
            var reflectionShape = FoxRunReflectionRos2MessageShapeBuilder.Build(fixture.RuntimeType);
            var encoded = Assert.Single(
                reflectionShape.Diagnostics,
                diagnostic => diagnostic.StartsWith("FOXRUN203|", StringComparison.Ordinal));
            Assert.True(FoxRunRos2ShapeDiagnostic.TryDecode(
                encoded,
                out _,
                out var reflectionPath,
                out _));
            Assert.Equal("vendor_msgs.msg.Command.Child.Value", reflectionPath);

            var generated = RunGenerator(source, "vendor_msgs/msg/Command");
            var diagnostic = Assert.Single(generated.Diagnostics, item => item.Id == "FOXRUN203");
            Assert.Contains(reflectionPath, diagnostic.GetMessage(), StringComparison.Ordinal);
        }

        [Fact]
        public void LifecycleLikePropertyNameWithoutRealRos2csInterfaceMappingIsNotExcluded()
        {
            var fixture = CompileFixture(ValidMessageSource(
                "vendor_msgs.msg",
                "public System.IntPtr Handle { get; }",
                publicConstructor: true,
                includeReceiver: false));

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
                Assert.Contains(shape.Diagnostics, diagnostic =>
                    diagnostic.StartsWith("FOXRUN203|vendor_msgs.msg.Command.Handle|", StringComparison.Ordinal)));
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Theory]
        [InlineData("public Command Child { get; set; }", "vendor_msgs.msg.Command.Child")]
        [InlineData("public int[,] Grid { get; set; }", "vendor_msgs.msg.Command.Grid")]
        public void UnsafeRecursiveOrSequenceShapeFailsAtDeterministicPath(
            string memberDeclaration,
            string expectedPath)
        {
            var fixture = CompileFixture(@"
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        " + memberDeclaration + @"
    }
}");

            var shapes = BuildHostShapes(fixture);
            Assert.All(shapes, shape =>
                Assert.Contains(shape.Diagnostics, diagnostic => diagnostic.StartsWith("FOXRUN211|" + expectedPath + "|", StringComparison.Ordinal)));
            Assert.Equal(shapes[0].Diagnostics, shapes[1].Diagnostics);
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void PackagedMinimumMessagesBuildWithoutDiagnosticsAcrossHosts(string distro)
        {
            var plugins = Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64",
                "Runtime",
                "Ros2ForUnity",
                "Plugins");
            var managedPaths = new[]
            {
                "ros2cs_common.dll",
                "ros2cs_core.dll",
                "builtin_interfaces_assembly.dll",
                "std_msgs_assembly.dll",
                "geometry_msgs_assembly.dll",
                "sensor_msgs_assembly.dll"
            }.Select(file => Path.Combine(plugins, file)).ToArray();
            Assert.All(managedPaths, path => Assert.True(File.Exists(path), path));

            var compilation = CSharpCompilation.Create(
                "phase179_packaged_" + distro,
                references: PlatformReferences().Concat(managedPaths.Select(path => MetadataReference.CreateFromFile(path))),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var loadContext = new AssemblyLoadContext("phase179-packaged-" + distro, isCollectible: true);
            loadContext.Resolving += (_, name) =>
            {
                var path = Path.Combine(plugins, name.Name + ".dll");
                return File.Exists(path) ? loadContext.LoadFromAssemblyPath(path) : null;
            };
            // Load the distro's real contract before ros2cs_core. Earlier
            // synthetic fixtures deliberately load a same-identity test
            // assembly in the default ALC; this keeps package evidence isolated.
            loadContext.LoadFromAssemblyPath(managedPaths[0]);
            var runtimeAssemblies = managedPaths
                .Where(path => !path.EndsWith("ros2cs_common.dll", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path),
                    path => loadContext.LoadFromAssemblyPath(path),
                    StringComparer.OrdinalIgnoreCase);
            var cases = new[]
            {
                ("std_msgs.msg.String", "std_msgs_assembly"),
                ("geometry_msgs.msg.Twist", "geometry_msgs_assembly"),
                ("sensor_msgs.msg.Joy", "sensor_msgs_assembly"),
                ("sensor_msgs.msg.Imu", "sensor_msgs_assembly")
            };

            foreach (var item in cases)
            {
                var symbol = compilation.GetTypeByMetadataName(item.Item1);
                var runtimeType = runtimeAssemblies[item.Item2].GetType(item.Item1, throwOnError: true);
                Assert.NotNull(symbol);
                var runtimeContract = Assert.Single(
                    runtimeType.GetInterfaces(),
                    contract => string.Equals(contract.FullName, "ROS2.Message", StringComparison.Ordinal));
                Assert.Equal("ros2cs_common", runtimeContract.Assembly.GetName().Name);
                var roslyn = FoxRunRoslynRos2MessageShapeBuilder.Build(symbol, compilation);
                var reflection = FoxRunReflectionRos2MessageShapeBuilder.Build(runtimeType);
                Assert.Empty(roslyn.Diagnostics);
                Assert.True(
                    reflection.Diagnostics.Count == 0,
                    string.Join(Environment.NewLine, reflection.Diagnostics)
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        runtimeType.GetInterfaces().Select(contract =>
                            contract.FullName + "|" + contract.Assembly.GetName().Name)));
                Assert.Equal(roslyn.CopyShapeIdentity, reflection.CopyShapeIdentity);
                Assert.All(
                    roslyn.Members.Where(member => !string.IsNullOrEmpty(member.NestedShapeIdentity)),
                    member => Assert.NotNull(member.NestedShape));
                Assert.All(
                    reflection.Members.Where(member => !string.IsNullOrEmpty(member.NestedShapeIdentity)),
                    member => Assert.NotNull(member.NestedShape));

                if (item.Item1 == "geometry_msgs.msg.Twist")
                    Assert.All(roslyn.Members, member => Assert.Equal(FoxRunRos2MessageMemberKind.NestedMessage, member.Kind));
                if (item.Item1 == "sensor_msgs.msg.Joy")
                    Assert.All(
                        roslyn.Members.Where(member => member.Name == "Axes" || member.Name == "Buttons"),
                        member => Assert.Equal(FoxRunRos2SequenceRepresentation.Array, member.SequenceRepresentation));
                if (item.Item1 == "sensor_msgs.msg.Imu")
                {
                    Assert.All(
                        roslyn.Members.Where(member => member.Name.EndsWith("_covariance", StringComparison.Ordinal)),
                        member =>
                        {
                            Assert.Equal(FoxRunRos2SequenceRepresentation.FixedArray, member.SequenceRepresentation);
                            Assert.False(member.CanWrite);
                            Assert.Equal(0, member.FixedSize);
                        });
                    Assert.Contains(
                        "builtin_interfaces/msg/Time",
                        roslyn.Members.Single(member => member.Name == "Header").NestedShapeIdentity,
                        StringComparison.Ordinal);
                }
            }
            loadContext.Unload();
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void GeneratedImuBindingCompilesAgainstEachPackagedManagedContract(string distro)
        {
            var plugins = Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64",
                "Runtime",
                "Ros2ForUnity",
                "Plugins");
            var managedPaths = new[]
            {
                "ros2cs_common.dll",
                "ros2cs_core.dll",
                "builtin_interfaces_assembly.dll",
                "std_msgs_assembly.dll",
                "geometry_msgs_assembly.dll",
                "sensor_msgs_assembly.dll"
            }.Select(file => Path.Combine(plugins, file)).ToArray();
            var references = PlatformReferences()
                .Concat(managedPaths.Select(path => MetadataReference.CreateFromFile(path)))
                .ToArray();
            var metadataCompilation = CSharpCompilation.Create(
                "phase179_imu_shape_" + distro,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = metadataCompilation.GetTypeByMetadataName("sensor_msgs.msg.Imu");
            Assert.NotNull(symbol);
            var shape = FoxRunRoslynRos2MessageShapeBuilder.Build(symbol, metadataCompilation);
            Assert.Empty(shape.Diagnostics);
            var generated = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[]
                {
                    BuildNativeGoldenMember("sensor_msgs.msg.Imu", shape)
                }).Types.Single());
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var nativeReference = BuildNativeAssemblyReference(@"
using System;
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(string id, string topic, string declaringType,
            string memberName, string canonicalRosType,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode,
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy, float hz,
            bool hasExplicitHz, float heartbeatIntervalSeconds) { }
    }
    public sealed class FoxRunRos2CopyContext { public void RequireBytes(long value) { } }
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }
        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
            Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new();
    }
}");
            var host = CSharpSyntaxTree.ParseText(@"
namespace UnityEngine { }
namespace UnityEngine.Scripting { public sealed class PreserveAttribute : System.Attribute { } }
namespace Unity.FoxgloveSDK.Components { }
namespace Demo { public partial class Receiver { private sensor_msgs.msg.Imu _incoming; } }
", parseOptions);
            var output = CSharpCompilation.Create(
                "phase179_generated_imu_" + distro,
                new[] { CSharpSyntaxTree.ParseText(generated, parseOptions), host },
                references.Concat(new[] { CoreAttributeAssembly.Value, nativeReference }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void SourceGeneratorBuildsNativeShapeWithoutJsonOrProtobufFallback()
        {
            var result = RunGenerator(
                ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", publicConstructor: true),
                schemaName: "vendor_msgs/msg/Command");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = result.Results.Single().GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText.ToString();
            Assert.Contains("\\\"generatesRos2NativeRegistration\\\":true", descriptor, StringComparison.Ordinal);
            Assert.Contains("vendor_msgs/msg/Command", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("FOXRUN006", result.Diagnostics.Select(diagnostic => diagnostic.Id));
        }

        [Fact]
        public void SourceGeneratorMarksCustomDtoNativePublishAndSubscribeAsNativeCapable()
        {
            var result = RunGenerator(
                @"namespace vendor_msgs.msg
{
    public sealed class Command
    {
        public Command() { }
        public int Value { get; set; }
    }
}",
                "vendor_msgs/msg/Command",
                sourceEndpoint: "Ros2Native",
                mode: "PublishAndSubscribe",
                encoding: "JSON");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = result.Results.Single().GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText.ToString();
            Assert.Contains("\\\"generatesRos2NativeRegistration\\\":true", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"ros2ContractKind\\\":\\\"CustomDto\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"ros2MessageShape\\\":null", descriptor, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomNativeOnlyIfUsesRegistrarConditionDelegateInsteadOfApplySideEffect()
        {
            var result = RunGenerator(
                @"namespace vendor_msgs.msg
{
    public sealed class Command
    {
        public Command() { }
        public int Value { get; set; }
    }
}",
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: true,
                sourceEndpoint: "Ros2Native",
                mode: "PublishAndSubscribe",
                encoding: "JSON",
                onlyIf: "CanApply");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var generated = string.Join(
                Environment.NewLine,
                result.Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()));
            Assert.Contains("() => CanApply()", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!CanApply()) return;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void SourceGeneratorKeepsCustomAndPackagedShapeFailuresInTheirOwnDiagnosticFamilies()
        {
            var customInvalid = RunGenerator(
                @"namespace custom_msgs.msg
{
    public sealed class Command
    {
        public Command() { }
        public char Value { get; set; }
    }
}",
                "custom_msgs/msg/Command",
                messageTypeName: "custom_msgs.msg.Command",
                sourceEndpoint: "Ros2Native",
                mode: "PublishAndSubscribe",
                encoding: "JSON");
            var packagedInvalid = RunGenerator(
                ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", publicConstructor: false),
                "vendor_msgs/msg/Command",
                sourceEndpoint: "Ros2Native",
                mode: "PublishAndSubscribe",
                encoding: "JSON");

            Assert.Contains(customInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN606");
            Assert.Contains(customInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.DoesNotContain(customInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN207");
            Assert.DoesNotContain(customInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.Contains(packagedInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN208");
            Assert.DoesNotContain(packagedInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(packagedInvalid.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
        }

        [Fact]
        public void SourceGeneratorReportsTargetedPackagedNativeShapeDiagnostics()
        {
            var cases = new[]
            {
                (ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", false), "vendor_msgs/msg/Command", "FOXRUN208", "vendor_msgs.msg.Command"),
                (ValidMessageSource("vendor_msgs.srv", "public int Value { get; set; }", true), "vendor_msgs/srv/Command", "FOXRUN209", "vendor_msgs.srv.Command"),
                (ValidMessageSource("vendor_msgs.msg", "public int Value { get; }", true), "vendor_msgs/msg/Command", "FOXRUN203", "vendor_msgs.msg.Command"),
                (ValidMessageSource("vendor_msgs.msg", "public Command Child { get; set; }", true), "vendor_msgs/msg/Command", "FOXRUN211", "vendor_msgs.msg.Command"),
                (ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", true), "other_msgs/msg/Command", "FOXRUN210", "vendor_msgs.msg.Command")
            };

            foreach (var item in cases)
            {
                var result = RunGenerator(item.Item1, item.Item2, item.Item4);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == item.Item3);
                Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
            }
        }

        [Fact]
        public void NativeDefineRequiresRealNativeAssemblyReferenceFromCompilation()
        {
            var source = ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", true);
            var missing = RunGenerator(source, "vendor_msgs/msg/Command", nativeDefine: true, nativeReference: false);
            var present = RunGenerator(source, "vendor_msgs/msg/Command", nativeDefine: true, nativeReference: true);
            var defineOff = RunGenerator(source, "vendor_msgs/msg/Command", nativeDefine: false, nativeReference: false);
            var webSocketOnly = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false,
                sourceEndpoint: "Foxglove");
            var publish = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false,
                sourceEndpoint: null,
                mode: "Publish");
            var ordinaryDto = RunGenerator(
                @"namespace vendor_msgs.msg
{
    public sealed class Command
    {
        public Command() { }
        public int Value { get; set; }
    }
}",
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false,
                sourceEndpoint: null,
                encoding: "Protobuf");
            var customNativeDto = RunGenerator(
                ValidMessageSource(
                        "vendor_msgs.msg",
                        "public int Value { get; set; }",
                        true,
                        interfaceName: "User.Message")
                    .Replace(
                        "namespace vendor_msgs.msg",
                        "namespace User { public interface Message { } } namespace vendor_msgs.msg"),
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false);
            var sameNameEmptyShell = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: true,
                nativeReferenceSource: @"
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class Marker { }
}");
            var missingRegistrar = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: true,
                nativeReferenceSource: @"
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public interface IFoxRunRos2SubscriptionSource { }
}");
            var wrongVisibility = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: true,
                nativeReferenceSource: @"
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public interface IFoxRunRos2SubscriptionSource { }
    internal interface IFoxRunRos2SubscriptionRegistrar { }
}");
            var unknownProvider = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false,
                sourceExpression:
                    "(Unity.FoxgloveSDK.Components.FoxRunEndpoint)99");

            Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(present.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(defineOff.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(webSocketOnly.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(publish.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(ordinaryDto.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(customNativeDto.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(customNativeDto.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN207");
            Assert.DoesNotContain(customNativeDto.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Contains(sameNameEmptyShell.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.Contains(missingRegistrar.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.Contains(wrongVisibility.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.Contains(unknownProvider.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN204");
            Assert.DoesNotContain(unknownProvider.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(unknownProvider.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
        }

        [Theory]
        [MemberData(nameof(InvalidNativeCompilationSeams))]
        public void NativeDefineRejectsShapeDriftedPublicSeamsAndSuppressesDependentPartial(
            string caseName,
            string nativeReferenceSource)
        {
            var result = RunGenerator(
                ValidMessageSource("vendor_msgs.msg", "public int Value { get; set; }", true),
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: true,
                nativeReferenceSource: nativeReferenceSource);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            var generated = string.Join(
                Environment.NewLine,
                result.GeneratedTrees.Select(tree => tree.GetText().ToString()));
            Assert.DoesNotContain(
                "IFoxRunRos2SubscriptionSource",
                generated,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(caseName));
        }

        public static IEnumerable<object[]> InvalidNativeCompilationSeams()
        {
            yield return new object[]
            {
                "source count is not get-only",
                CompleteNativeSeamSource.Replace(
                    "int FoxRunRos2SubscriptionCount { get; }",
                    "int FoxRunRos2SubscriptionCount { get; set; }")
            };
            yield return new object[]
            {
                "source register uses wrong parameter",
                CompleteNativeSeamSource.Replace(
                    "void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);",
                    "void FoxRunRos2RegisterSubscriptions(object registrar);")
            };
            yield return new object[]
            {
                "registrar omits scheduling delegates and constructor constraint",
                CompleteNativeSeamSource.Replace(
                    "Action<T> apply, Func<T, bool> clearIfOwned,\n            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,\n            Func<bool> canApply) where T : ROS2.Message, new();",
                    "Action<T> apply) where T : ROS2.Message;")
            };
            yield return new object[]
            {
                "registrar omits main-thread condition delegate",
                CompleteNativeSeamSource.Replace(
                    ", Func<bool> consumeTrigger,\n            Func<bool> canApply) where T : ROS2.Message, new();",
                    ", Func<bool> consumeTrigger) where T : ROS2.Message, new();")
            };
            yield return new object[]
            {
                "contract constructor omits qos",
                CompleteNativeSeamSource.Replace(
                    "Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,",
                    "bool supportsNative,")
            };
            yield return new object[]
            {
                "copy context budget signature drifts",
                CompleteNativeSeamSource.Replace(
                    "public void RequireBytes(long byteCount) { }",
                    "public int RequireBytes(int byteCount) { return 0; }")
            };
        }

        [Fact]
        public void ReflectionMemberDataBuildsNativeShapeWithoutReplacingWebSocketCapability()
        {
            var fixture = CompileFixture(ValidMessageSource(
                "vendor_msgs.msg",
                "public int Value { get; set; }",
                publicConstructor: true,
                includeReceiver: false));
            var data = new FoxrunCodeGenerator.MemberData(
                "_incoming",
                fixture.RuntimeType,
                "field",
                "Demo",
                "Receiver",
                "/command",
                10f,
                "vendor_msgs/msg/Command",
                mode: (int)FoxRunFlow.Subscribe,
                source: 2,
                ros2Qos: 3);
            var member = Assert.Single(
                FoxRunReflectionGenerationModelLowerer.Lower(new[] { data.ToReflectionMember() }).Types.Single().Members);

            Assert.True(member.GeneratesRos2NativeRegistration);
            Assert.NotNull(member.Ros2MessageShape);
            Assert.True(member.GeneratesWebSocketCodec);
        }

        [Fact]
        public void PublishCustomDtoBuildsNativeContractAcrossReflectionAndRoslyn()
        {
            const string source = @"
namespace Demo
{
    public sealed class CustomPayload
    {
        public int Count { get; set; }
    }
}";
            var fixture = CompileFixture(source, "Demo.CustomPayload");
            var roslynShape = FoxRunRoslynRos2CustomDtoShapeBuilder.Build(
                fixture.Symbol,
                fixture.Compilation);
            Assert.True(roslynShape.IsSupported, string.Join(Environment.NewLine, roslynShape.Diagnostics));
            Assert.True(FoxRunRos2ContractCapability.IsNativeRegistrationCapable(null, roslynShape));

            var data = new FoxrunCodeGenerator.MemberData(
                "Payload",
                fixture.RuntimeType,
                "field",
                "Demo",
                "Publisher",
                "/custom",
                10f,
                "Demo.CustomPayload",
                mode: (int)FoxRunFlow.Publish,
                source: 0);
            var reflected = Assert.Single(
                FoxRunReflectionGenerationModelLowerer.Lower(new[] { data.ToReflectionMember() }).Types.Single().Members);

            Assert.NotNull(reflected.Ros2CustomDtoShape);
            Assert.Equal(FoxRunRos2ContractKind.CustomDto, reflected.Ros2ContractKind);
            Assert.True(reflected.GeneratesRos2NativeRegistration);

            var generated = RunGenerator(
                source,
                "Demo.CustomPayload",
                messageTypeName: "CustomPayload",
                sourceEndpoint: null,
                mode: "Publish",
                encoding: "JSON");
            var descriptor = generated.Results.Single().GeneratedSources
                .Single(item => item.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.True(
                !generated.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                string.Join(Environment.NewLine, generated.Diagnostics) + Environment.NewLine + descriptor);
            Assert.Contains("\\\"ros2ContractKind\\\":\\\"CustomDto\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"generatesRos2NativeRegistration\\\":true", descriptor, StringComparison.Ordinal);
        }

        private static void AssertHostParity(
            Fixture fixture,
            bool implementsRos2Message,
            string expectedCanonicalType)
        {
            var roslynShape = FoxRunRoslynRos2MessageShapeBuilder.Build(
                fixture.Symbol,
                fixture.Compilation);
            var reflectionShape = FoxRunReflectionRos2MessageShapeBuilder.Build(fixture.RuntimeType);

            Assert.Equal(implementsRos2Message, roslynShape.ImplementsRos2Message);
            Assert.Equal(implementsRos2Message, reflectionShape.ImplementsRos2Message);
            Assert.Equal(expectedCanonicalType, roslynShape.CanonicalRosType);
            Assert.Equal(expectedCanonicalType, reflectionShape.CanonicalRosType);
            Assert.Equal(roslynShape.CopyShapeIdentity, reflectionShape.CopyShapeIdentity);
            Assert.Equal(roslynShape.Diagnostics, reflectionShape.Diagnostics);
        }

        private static FoxRunGenerationMember BuildMember(
            string provider,
            string encoding,
            int mode,
            string qos,
            FoxRunRos2MessageShape ros2MessageShape,
            bool generatesWebSocketCodec = false,
            bool generatesNativeRegistration = true,
            int protobufFieldNumber = 0,
            FoxRunProtobufTypeShape protobufTypeShape = null,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null)
            => BuildMember(
                provider,
                encoding,
                mode,
                qos,
                ros2MessageShape,
                "vendor_msgs/msg/Command",
                generatesWebSocketCodec,
                generatesNativeRegistration,
                protobufFieldNumber,
                protobufTypeShape,
                namedArgumentPresence);

        private static FoxRunGenerationMember BuildMember(
            string provider,
            string encoding,
            int mode,
            string qos,
            FoxRunRos2MessageShape ros2MessageShape,
            string schemaName,
            bool generatesWebSocketCodec = false,
            bool generatesNativeRegistration = true,
            int protobufFieldNumber = 0,
            FoxRunProtobufTypeShape protobufTypeShape = null,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null)
            => new FoxRunGenerationMember(
                "Demo", "Receiver", "_incoming", "field",
                "vendor_msgs.msg.Command", "global::vendor_msgs.msg.Command", "vendor_msgs.msg.Command",
                false, false, "", "/command", 10f, schemaName,
                (int)FoxRunPolicy.FixedRate, 0f,
                "Roslyn", 1, "", mode: mode, encoding: encoding,
                protobufFieldNumber: protobufFieldNumber,
                protobufTypeShape: protobufTypeShape,
                source: provider, ros2Qos: qos,
                generatesWebSocketCodec: generatesWebSocketCodec,
                generatesRos2NativeRegistration: generatesNativeRegistration,
                ros2MessageShape: ros2MessageShape,
                namedArgumentPresence: namedArgumentPresence);

        private static FoxRunRos2MessageShape ValidShape()
            => new FoxRunRos2MessageShape(
                "global::vendor_msgs.msg.Command",
                "vendor_msgs/msg/Command",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "vendor_msgs/msg/Command|Value:scalar:System.Int32",
                members: new[]
                {
                    new FoxRunRos2MessageMemberShape(
                        "Value",
                        FoxRunRos2MessageMemberKind.Scalar,
                        "System.Int32",
                        "",
                        "")
                },
                diagnostics: Array.Empty<string>());

        private static FoxRunRos2MessageShape InvalidNativeShape()
            => new FoxRunRos2MessageShape(
                "global::vendor_msgs.msg.Command",
                "vendor_msgs/msg/Command",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: string.Empty,
                members: Array.Empty<FoxRunRos2MessageMemberShape>(),
                diagnostics: new[]
                {
                    FoxRunRos2ShapeDiagnostic.Encode(
                        "FOXRUN203",
                        "vendor_msgs.msg.Command.Value",
                        "Native ROS2 message members must be writable.")
                });

        [Theory]
        [MemberData(nameof(NativeBindingGoldenCases))]
        public void NativeBindingGoldenSourcesUseClosedRegistrationAndOwnedGraphOperations(
            string typeName,
            FoxRunRos2MessageShape shape,
            string[] requiredFragments)
        {
            var member = new FoxRunGenerationMember(
                "Demo", "Receiver", "_incoming", "field",
                typeName, "global::" + typeName,
                canonicalType: shape.CanonicalRosType,
                isValueType: false,
                isArray: false,
                elementTypeName: "",
                topic: "/phase179/native",
                hz: 10f,
                schemaName: shape.CanonicalRosType,
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: shape);

            var source = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[] { member }).Types.Single());

            Assert.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", source, StringComparison.Ordinal);
            Assert.Contains(
                "IFoxRunRos2SubscriptionSource",
                source,
                StringComparison.Ordinal);
            Assert.Contains("registrar.Register<global::" + typeName + ">", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CopyContext", source, StringComparison.Ordinal);
            Assert.Contains("ReferenceEquals", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MakeGenericMethod", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Activator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("dynamic", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UnityEngine.", NativeConditionalSection(source), StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.Log", NativeConditionalSection(source), StringComparison.Ordinal);
            foreach (var fragment in requiredFragments)
                Assert.Contains(fragment, source, StringComparison.Ordinal);
        }

        [Fact]
        public void NestedListAddFailureOwnsTheCurrentCopyAndLeavesPriorItemsToOuterCleanup()
        {
            var generated = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[]
                {
                    BuildNativeGoldenMember("test_msgs.msg.Complex", BuildComplexBehaviorShape())
                }).Types.Single()).Replace("\r\n", "\n");

            var addIndex = generated.IndexOf(".Add(__copiedItem);", StringComparison.Ordinal);
            Assert.True(addIndex >= 0, "Nested List<T> emission must add a separately owned copied item.");
            var start = Math.Max(0, addIndex - 320);
            var rethrowIndex = generated.IndexOf("throw;", addIndex, StringComparison.Ordinal);
            Assert.True(rethrowIndex > addIndex, "Nested List<T> Add failure must rethrow the original exception.");
            var fragment = generated.Substring(start, rethrowIndex + "throw;".Length - start);
            Assert.Contains("var __copiedItem = __FoxRunRos2CopyNested_", fragment, StringComparison.Ordinal);
            Assert.Contains("try\n", fragment, StringComparison.Ordinal);
            Assert.Contains("catch\n", fragment, StringComparison.Ordinal);
            Assert.Contains("__FoxRunRos2DisposeNested_", fragment, StringComparison.Ordinal);
            Assert.Contains("throw;", fragment, StringComparison.Ordinal);
            Assert.Contains("catch\n", fragment.Substring(fragment.LastIndexOf("try\n", StringComparison.Ordinal)), StringComparison.Ordinal);
        }

        [Fact]
        public void ExplicitNativeInputDoesNotEmitWebSocketFallbackAndNoDefineKeepsSourceParseable()
        {
            var member = BuildNativeGoldenMember("std_msgs.msg.String", BuildStringGoldenShape());
            var source = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[] { member }).Types.Single());

            Assert.DoesNotContain("IFoxgloveInputSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunInboundJson", source, StringComparison.Ordinal);
            Assert.DoesNotContain("TryReadFoxRunProtobuf", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunFlow)2", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunEndpoint.Ros2Native", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2QosPreset.SensorData", source, StringComparison.Ordinal);
            Assert.Contains(
                "(global::Unity.FoxgloveSDK.Components.FoxRunPolicy)1",
                source,
                StringComparison.Ordinal);

            var tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp9));
            Assert.DoesNotContain(
                tree.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void CoexistingJsonProtobufAndNativeMembersStayInTheirOwningGeneratedPaths()
        {
            const string jsonTopic = "/phase179/coexist/json";
            const string protobufTopic = "/phase179/coexist/protobuf";
            const string nativeTopic = "/phase179/coexist/native";
            var json = new FoxRunGenerationMember(
                "Demo", "Receiver", "_jsonInput", "field",
                "System.String", "string", "string",
                isValueType: false,
                isArray: false,
                elementTypeName: "",
                topic: jsonTopic,
                hz: 10f,
                schemaName: "",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false);
            var protobuf = new FoxRunGenerationMember(
                "Demo", "Receiver", "_protobufInput", "field",
                "System.Int32", "int", "int32",
                isValueType: true,
                isArray: false,
                elementTypeName: "",
                topic: protobufTopic,
                hz: 10f,
                schemaName: "Demo.CountInput",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 1,
                conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                protobufFieldNumber: 17,
                protobufTypeShape: FoxRunProtobufTypeShape.Canonical("int32"),
                source: FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false);
            var native = new FoxRunGenerationMember(
                "Demo", "Receiver", "_nativeInput", "field",
                "std_msgs.msg.String", "global::std_msgs.msg.String", "std_msgs/msg/String",
                isValueType: false,
                isArray: false,
                elementTypeName: "",
                topic: nativeTopic,
                hz: 10f,
                schemaName: "std_msgs/msg/String",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 2,
                conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: BuildStringGoldenShape());
            var members = new[] { json, protobuf, native };

            var source = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(members).Types.Single());
            var nativeStart = source.IndexOf(
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                StringComparison.Ordinal);
            Assert.True(nativeStart > 0, "Expected a distinct conditional native partial.");
            var webSocketPartial = source.Substring(0, nativeStart);
            var nativePartial = source.Substring(nativeStart);

            Assert.Contains("IFoxgloveInputSource", webSocketPartial, StringComparison.Ordinal);
            Assert.Contains(jsonTopic, webSocketPartial, StringComparison.Ordinal);
            Assert.Contains(protobufTopic, webSocketPartial, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson", webSocketPartial, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundProtobuf.TryRead", webSocketPartial, StringComparison.Ordinal);
            Assert.DoesNotContain(nativeTopic, webSocketPartial, StringComparison.Ordinal);
            Assert.DoesNotContain("Register<global::std_msgs.msg.String>", webSocketPartial, StringComparison.Ordinal);

            Assert.Contains("IFoxRunRos2SubscriptionSource", nativePartial, StringComparison.Ordinal);
            Assert.Contains("Register<global::std_msgs.msg.String>", nativePartial, StringComparison.Ordinal);
            Assert.Contains(nativeTopic, nativePartial, StringComparison.Ordinal);
            Assert.Contains("FoxRunFlow)2", nativePartial, StringComparison.Ordinal);
            Assert.Contains("FoxRunEndpoint.Ros2Native", nativePartial, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2QosPreset.SensorData", nativePartial, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveInputSource", nativePartial, StringComparison.Ordinal);
            Assert.DoesNotContain(jsonTopic, nativePartial, StringComparison.Ordinal);
            Assert.DoesNotContain(protobufTopic, nativePartial, StringComparison.Ordinal);

            var manifest = FoxRunManifestBuilder.Build(
                members.Select(FoxRunManifestMember.FromGenerationMember).ToArray(),
                manifestVersion: 2);
            var wireContracts = manifest.Sections.FoxRun.Types
                .SelectMany(type => type.Contracts)
                .ToArray();
            var bindings = manifest.Sections.Subscriptions.Bindings;

            Assert.Equal(2, wireContracts.Length);
            Assert.Contains(wireContracts, contract => contract.Topic == jsonTopic && contract.Encoding == "json");
            Assert.Contains(wireContracts, contract => contract.Topic == protobufTopic && contract.Encoding == "protobuf");
            Assert.DoesNotContain(wireContracts, contract => contract.Topic == nativeTopic);
            Assert.Equal(3, bindings.Count);
            Assert.Contains(bindings, binding => binding.Topic == jsonTopic
                                                 && binding.SupportsWebSocket
                                                 && !binding.SupportsRos2Native);
            Assert.Contains(bindings, binding => binding.Topic == protobufTopic
                                                 && binding.SupportsWebSocket
                                                 && !binding.SupportsRos2Native);
            Assert.Contains(bindings, binding => binding.Topic == nativeTopic
                                                 && !binding.SupportsWebSocket
                                                 && binding.SupportsRos2Native
                                                 && binding.DeclaredSource == FoxRunGenerationDescriptorConstants.Ros2NativeSource);
            Assert.DoesNotContain(
                "\"encoding\":\"cdr\"",
                FoxRunManifestJsonWriter.WriteCanonical(manifest),
                StringComparison.Ordinal);
        }

        [Fact]
        public void MissingNativeReferenceSuppressesOnlyTheDependentNativePartial()
        {
            var source = ValidMessageSource(
                "vendor_msgs.msg",
                "public int Value { get; set; }",
                publicConstructor: true)
                + @"
namespace Demo
{
    public partial class Receiver
    {
        [Unity.FoxgloveSDK.Components.FoxRun(""/web"",
            Mode = Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
            Source = Unity.FoxgloveSDK.Components.FoxRunEndpoint.Foxglove)]
        private int _web;
    }
}";
            var result = RunGenerator(
                source,
                "vendor_msgs/msg/Command",
                nativeDefine: true,
                nativeReference: false,
                sourceEndpoint: "Ros2Native");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            var generatedSources = result.Results.Single().GeneratedSources
                .Select(source => source.SourceText.ToString())
                .ToArray();
            var generated = generatedSources
                .SingleOrDefault(source => source.Contains("partial class Receiver", StringComparison.Ordinal));
            Assert.True(
                generated != null,
                "Expected Receiver output. Diagnostics: "
                + string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()))
                + "; generated: "
                + string.Join(", ", generatedSources.Select(source => source.Substring(0, Math.Min(source.Length, 80)))));
            Assert.DoesNotContain("IFoxRunRos2SubscriptionSource", generated, StringComparison.Ordinal);
            Assert.Contains("IFoxgloveInputSource", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedNativeStringBindingCompilesAgainstTheExactPublicSeam()
        {
            var generated = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[]
                {
                    BuildNativeGoldenMember("std_msgs.msg.String", BuildStringGoldenShape())
                }).Types.Single());
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var nativeReference = BuildNativeAssemblyReference(@"
using System;
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(string id, string topic, string declaringType,
            string memberName, string canonicalRosType,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode,
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy, float hz,
            bool hasExplicitHz, float heartbeatIntervalSeconds) { }
    }
    public sealed class FoxRunRos2CopyContext
    {
        public void RequireBytes(long value) { }
    }
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }
        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
            Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new();
    }
}");
            var sources = new[]
            {
                CSharpSyntaxTree.ParseText(generated, parseOptions),
                CSharpSyntaxTree.ParseText(@"
namespace UnityEngine { }
namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : System.Attribute { }
}
namespace Unity.FoxgloveSDK.Components { }
namespace std_msgs.msg
{
    public sealed class String : ROS2.Message, System.IDisposable
    {
        public string Data { get; set; } = string.Empty;
        public void Dispose() { }
    }
}
namespace Demo
{
    public partial class Receiver
    {
        private std_msgs.msg.String _incoming;
    }
}", parseOptions)
            };
            var compilation = CSharpCompilation.Create(
                "phase179_generated_native_string",
                sources,
                PlatformReferences().Concat(new[]
                {
                    CoreAttributeAssembly.Value,
                    Ros2Contract.Value.Reference,
                    nativeReference
                }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.DoesNotContain(
                compilation.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void GeneratedNativeContractMetadataEscapesAndRoundTripsAllCSharpStringBoundaries()
        {
            const string boundary = "tab\t nul\0 control\u0001 lines\u2028\u2029 surrogate\uD800 quote\" slash\\";
            var topic = "/phase179/" + boundary;
            var canonical = "std_msgs/msg/String|" + boundary;
            const string provider = FoxRunGenerationDescriptorConstants.Ros2NativeSource;
            const string qos = FoxRunGenerationDescriptorConstants.SensorDataRos2Qos;
            var shape = MessageShape(
                "std_msgs.msg.String",
                canonical,
                new FoxRunRos2MessageMemberShape(
                    "Data", FoxRunRos2MessageMemberKind.String, "System.String", "", ""));
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_incoming",
                "std_msgs.msg.String",
                topic,
                10f,
                canonical,
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                mode: (int)FoxRunFlow.Subscribe,
                canonicalType: canonical,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: provider,
                ros2Qos: qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: shape);
            var generatedBuilder = new System.Text.StringBuilder();
            Ros2InputDispatchEmitter.EmitConditionalPartial(
                generatedBuilder,
                "Demo",
                "Receiver",
                new[] { member });
            var generated = generatedBuilder.ToString();

            Assert.Contains("\\t", generated, StringComparison.Ordinal);
            Assert.Contains("\\0", generated, StringComparison.Ordinal);
            Assert.Contains("\\u0001", generated, StringComparison.Ordinal);
            Assert.Contains("\\u2028", generated, StringComparison.Ordinal);
            Assert.Contains("\\u2029", generated, StringComparison.Ordinal);
            Assert.Contains("\\uD800", generated, StringComparison.Ordinal);

            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var support = CSharpSyntaxTree.ParseText(@"
using System;
namespace ROS2 { public interface Message { } }
namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunFlow { Publish = 1, Subscribe = 2, PublishAndSubscribe = 3 }
    public enum FoxRunPolicy { FixedRate = 1, Change = 2, Trigger = 4 }
    public enum FoxRunEndpoint { Inherit, FoxgloveWebSocket, Ros2Native }
    public enum FoxRunRos2QosPreset { Inherit, Default, Reliable, SensorData, TransientLocal }
}
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(string id, string topic, string declaringType,
            string memberName, string canonicalRosType,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode,
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy, float hz,
            bool hasExplicitHz, float heartbeatIntervalSeconds)
        {
            Id = id; Topic = topic; DeclaringType = declaringType; MemberName = memberName;
            CanonicalRosType = canonicalRosType; Mode = mode; Source = provider;
            QosPreset = qos; SupportsRos2Native = supportsNative;
        }
        public string Id { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public Unity.FoxgloveSDK.Components.FoxRunFlow Mode { get; }
        public Unity.FoxgloveSDK.Components.FoxRunEndpoint Source { get; }
        public Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset QosPreset { get; }
        public bool SupportsRos2Native { get; }
    }
    public sealed class FoxRunRos2CopyContext { public void RequireBytes(long value) { } }
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }
        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
            Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new();
    }
}
namespace std_msgs.msg
{
    public sealed class String : ROS2.Message, IDisposable
    {
        public string Data { get; set; } = string.Empty;
        public void Dispose() { }
    }
}
namespace Demo { public partial class Receiver { private std_msgs.msg.String _incoming; } }
namespace TestSupport
{
    public sealed class CaptureRegistrar : Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar
    {
        public Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract Contract { get; private set; }
        public void Register<T>(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract contract,
            Func<T, Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext, T> copy,
            Action<T> dispose, Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new()
            => Contract = contract;
    }
}
", parseOptions);
            var compilation = CSharpCompilation.Create(
                "phase179_contract_literal_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(generated, parseOptions), support },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiver = Activator.CreateInstance(assembly.GetType("Demo.Receiver", throwOnError: true));
            var registrar = Activator.CreateInstance(assembly.GetType("TestSupport.CaptureRegistrar", throwOnError: true));
            var sourceInterface = assembly.GetType(
                "Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionSource",
                throwOnError: true);
            sourceInterface.GetMethod("FoxRunRos2RegisterSubscriptions").Invoke(receiver, new[] { registrar });
            var contract = Get(registrar, "Contract");
            var id = Ros2InputDispatchEmitter.BuildContractId(
                "Demo.Receiver", "_incoming", topic, provider, canonical, qos);
            Assert.Equal(id, Get(contract, "Id"));
            Assert.Equal(topic, Get(contract, "Topic"));
            Assert.Equal("Demo.Receiver", Get(contract, "DeclaringType"));
            Assert.Equal("_incoming", Get(contract, "MemberName"));
            Assert.Equal(canonical, Get(contract, "CanonicalRosType"));
            Assert.Equal((int)FoxRunFlow.Subscribe, Convert.ToInt32(Get(contract, "Mode")));
            Assert.Equal(2, Convert.ToInt32(Get(contract, "Source")));
            Assert.Equal(3, Convert.ToInt32(Get(contract, "QosPreset")));
            Assert.Equal(true, Get(contract, "SupportsRos2Native"));
        }

        [Fact]
        public void NativeContractIdUsesVersionedLengthPrefixedTupleWithoutDelimiterCollisions()
        {
            var left = Ros2InputDispatchEmitter.BuildContractId(
                "Demo.Receiver", "_incoming", "a|b", "c", "std_msgs/msg/String", "sensor-data");
            var right = Ros2InputDispatchEmitter.BuildContractId(
                "Demo.Receiver", "_incoming", "a", "b|c", "std_msgs/msg/String", "sensor-data");

            Assert.NotEqual(left, right);
            Assert.Equal(
                "foxrun-ros2-subscription:v1|13:Demo.Receiver9:_incoming3:a|b1:c19:std_msgs/msg/String11:sensor-data",
                left);
            Assert.Equal(
                "foxrun-ros2-subscription:v1|13:Demo.Receiver9:_incoming1:a3:b|c19:std_msgs/msg/String11:sensor-data",
                right);
        }

        [Fact]
        public void GeneratedComplexBindingExecutesIndependentCopyExactCleanupAndOwnedClearSemantics()
        {
            var generated = FoxgloveSourceEmitter.EmitClass(
                FoxRunGenerationModel.FromMembers(new[]
                {
                    BuildNativeGoldenMember("test_msgs.msg.Complex", BuildComplexBehaviorShape())
                }).Types.Single());
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var support = CSharpSyntaxTree.ParseText(@"
using System;
using System.Collections.Generic;
namespace UnityEngine { }
namespace UnityEngine.Scripting { public sealed class PreserveAttribute : Attribute { } }
namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunFlow { Publish = 1, Subscribe = 2, PublishAndSubscribe = 3 }
    public enum FoxRunPolicy { FixedRate = 1, Change = 2, Trigger = 4 }
    public enum FoxRunEndpoint { Inherit, FoxgloveWebSocket, Ros2Native }
    public enum FoxRunRos2QosPreset { Inherit, Default, Reliable, SensorData, TransientLocal }
}
namespace ROS2 { public interface Message { } }
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(string id, string topic, string declaringType,
            string memberName, string canonicalRosType,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode,
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy, float hz,
            bool hasExplicitHz, float heartbeatIntervalSeconds) { }
    }
    public sealed class FoxRunRos2CopyContext
    {
        public FoxRunRos2CopyContext(long maximumBytes) { RemainingBytes = maximumBytes; }
        public long RemainingBytes { get; private set; }
        public void RequireBytes(long value)
        {
            if (value > RemainingBytes) throw new InvalidOperationException(""budget"");
            RemainingBytes -= value;
        }
    }
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }
        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
            Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new();
    }
}
namespace test_msgs.msg
{
    public sealed class Child : ROS2.Message, IDisposable
    {
        public Child() { TotalConstructed++; Instances.Add(this); }
        public static int TotalConstructed;
        public static int TotalDisposeCalls;
        public static int ThrowOnDisposeCall;
        public static readonly List<Child> Instances = new List<Child>();
        public int DisposeCalls;
        public bool ThrowOnDispose { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public void Dispose()
        {
            DisposeCalls++;
            TotalDisposeCalls++;
            if (ThrowOnDispose || (ThrowOnDisposeCall > 0 && TotalDisposeCalls == ThrowOnDisposeCall))
                throw new InvalidOperationException(""child dispose"");
        }
        public static void Reset()
        {
            TotalConstructed = 0;
            TotalDisposeCalls = 0;
            ThrowOnDisposeCall = 0;
            Instances.Clear();
        }
    }
    public sealed class Complex : ROS2.Message, IDisposable
    {
        private readonly double[] covariance;
        private readonly Child[] fixedChildren;
        private Child child;
        private List<Child> children;
        public Complex()
        {
            TotalConstructed++;
            child = new Child();
            children = new List<Child> { new Child() };
            fixedChildren = new[] { new Child(), new Child() };
            covariance = new double[NextFixedLength];
        }
        public static int NextFixedLength = 3;
        public static bool ThrowOnChildrenSet;
        public static bool ThrowAfterChildSet;
        public static bool ThrowAfterChildrenNullSet;
        public static bool ThrowAfterChildrenSet;
        public static bool ThrowOnDispose;
        public static int TotalConstructed;
        public static int TotalDisposeCalls;
        public int DisposeCalls;
        public Child Child
        {
            get => child;
            set
            {
                child = value;
                if (value != null && ThrowAfterChildSet)
                {
                    ThrowAfterChildSet = false;
                    throw new InvalidOperationException(""child setter after write"");
                }
            }
        }
        public List<Child> Children
        {
            get => children;
            set
            {
                if (ThrowOnChildrenSet && value != null)
                {
                    ThrowOnChildrenSet = false;
                    throw new InvalidOperationException(""children setter"");
                }
                children = value;
                if (value == null && ThrowAfterChildrenNullSet)
                {
                    ThrowAfterChildrenNullSet = false;
                    throw new InvalidOperationException(""children null setter after write"");
                }
                if (value != null && ThrowAfterChildrenSet)
                {
                    ThrowAfterChildrenSet = false;
                    throw new InvalidOperationException(""children setter after write"");
                }
            }
        }
        public Child[] FixedChildren => fixedChildren;
        public double[] Covariance => covariance;
        public string Label { get; set; } = string.Empty;
        public int[] Values { get; set; } = Array.Empty<int>();
        public void Dispose()
        {
            DisposeCalls++;
            TotalDisposeCalls++;
            if (ThrowOnDispose)
                throw new InvalidOperationException(""complex dispose"");
            child?.Dispose();
            if (children != null) foreach (var child in children) child?.Dispose();
            foreach (var child in fixedChildren) child?.Dispose();
        }
        public static void Reset()
        {
            NextFixedLength = 3;
            ThrowOnChildrenSet = false;
            ThrowAfterChildSet = false;
            ThrowAfterChildrenNullSet = false;
            ThrowAfterChildrenSet = false;
            ThrowOnDispose = false;
            TotalConstructed = 0;
            TotalDisposeCalls = 0;
            Child.Reset();
        }
    }
}
namespace Demo
{
    public partial class Receiver
    {
        private test_msgs.msg.Complex incoming;
        public static int IncomingThrowsAfterWrite;
        public static bool ThrowBeforeIncomingNullSet;
        private test_msgs.msg.Complex _incoming
        {
            get => incoming;
            set
            {
                if (value == null && ThrowBeforeIncomingNullSet)
                {
                    ThrowBeforeIncomingNullSet = false;
                    throw new InvalidOperationException(""component rollback before write"");
                }
                incoming = value;
                if (IncomingThrowsAfterWrite > 0)
                {
                    IncomingThrowsAfterWrite--;
                    throw new InvalidOperationException(""component setter after write"");
                }
            }
        }
    }
}
", parseOptions);
            var compilation = CSharpCompilation.Create(
                "phase179_generated_behavior_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(generated, parseOptions), support },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType("Demo.Receiver", throwOnError: true);
            var complexType = assembly.GetType("test_msgs.msg.Complex", throwOnError: true);
            var childType = assembly.GetType("test_msgs.msg.Child", throwOnError: true);
            var contextType = assembly.GetType(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext",
                throwOnError: true);
            var copy = receiverType.GetMethod("__FoxRunRos2Copy_0", BindingFlags.NonPublic | BindingFlags.Static);
            var dispose = receiverType.GetMethod("__FoxRunRos2Dispose_0", BindingFlags.NonPublic | BindingFlags.Static);
            var apply = receiverType.GetMethod("__FoxRunRos2Apply_0", BindingFlags.NonPublic | BindingFlags.Instance);
            var clear = receiverType.GetMethod("__FoxRunRos2ClearIfOwned_0", BindingFlags.NonPublic | BindingFlags.Instance);
            var field = receiverType.GetProperty("_incoming", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(copy);
            Assert.NotNull(dispose);
            Assert.NotNull(apply);
            Assert.NotNull(clear);
            Assert.NotNull(field);

            InvokeStatic(complexType, "Reset");
            var source = Activator.CreateInstance(complexType);
            Set(source, "Label", "source");
            Set(source, "Values", new[] { 10, 20 });
            var sourceChild = Get(source, "Child");
            Set(sourceChild, "Name", "root-child");
            Set(sourceChild, "Value", 7);
            var children = Get(source, "Children");
            var listAdd = children.GetType().GetMethod("Add");
            var sequenceChild = children.GetType().GetProperty("Item").GetValue(children, new object[] { 0 });
            Set(sequenceChild, "Name", "sequence-child");
            Set(sequenceChild, "Value", 9);
            ((double[])Get(source, "Covariance"))[0] = 1.5d;

            var childDisposedBeforeCopy = StaticInt(childType, "TotalDisposeCalls");
            var childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var owned = copy.Invoke(null, new[] { source, Activator.CreateInstance(contextType, 4096L) });
            var createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.NotSame(source, owned);
            Assert.Equal(8, createdByCopy.Length);
            Assert.All(createdByCopy.Take(4), item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.All(createdByCopy.Skip(4), item => Assert.Equal(0, Get(item, "DisposeCalls")));
            Assert.Equal(childDisposedBeforeCopy + 4, StaticInt(childType, "TotalDisposeCalls"));
            Assert.NotSame(Get(source, "Child"), Get(owned, "Child"));
            Assert.NotSame(Get(source, "Values"), Get(owned, "Values"));
            Assert.NotSame(Get(source, "Children"), Get(owned, "Children"));
            Assert.NotSame(Get(source, "Covariance"), Get(owned, "Covariance"));

            Set(source, "Label", "changed");
            Set(sourceChild, "Value", 70);
            ((int[])Get(source, "Values"))[0] = 100;
            Set(sequenceChild, "Value", 90);
            ((double[])Get(source, "Covariance"))[0] = 15d;
            Assert.Equal("source", Get(owned, "Label"));
            Assert.Equal(7, Get(Get(owned, "Child"), "Value"));
            Assert.Equal(10, ((int[])Get(owned, "Values"))[0]);
            var ownedChildren = Get(owned, "Children");
            Assert.Equal(9, Get(ownedChildren.GetType().GetProperty("Item").GetValue(ownedChildren, new object[] { 0 }), "Value"));
            Assert.Equal(1.5d, ((double[])Get(owned, "Covariance"))[0]);

            var receiver = Activator.CreateInstance(receiverType);
            apply.Invoke(receiver, new[] { owned });
            Assert.Same(owned, field.GetValue(receiver));
            var secondOwned = copy.Invoke(null, new[] { source, Activator.CreateInstance(contextType, 4096L) });
            apply.Invoke(receiver, new[] { secondOwned });
            Assert.False((bool)clear.Invoke(receiver, new[] { owned }));
            var ownedFixedChildren = ((Array)Get(owned, "FixedChildren")).Cast<object>().ToArray();
            dispose.Invoke(null, new[] { owned });
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.All(((Array)Get(owned, "FixedChildren")).Cast<object>(), Assert.Null);
            Assert.All(ownedFixedChildren, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Same(secondOwned, field.GetValue(receiver));
            var userValue = Activator.CreateInstance(complexType);
            field.SetValue(receiver, userValue);
            Assert.False((bool)clear.Invoke(receiver, new[] { secondOwned }));
            dispose.Invoke(null, new[] { secondOwned });
            Assert.Same(userValue, field.GetValue(receiver));
            Assert.Equal(0, Get(userValue, "DisposeCalls"));
            Assert.Equal(0, Get(Get(userValue, "Child"), "DisposeCalls"));
            Assert.All(((Array)Get(userValue, "FixedChildren")).Cast<object>(),
                item => Assert.Equal(0, Get(item, "DisposeCalls")));

            InvokeStatic(complexType, "Reset");
            var nullSource = Activator.CreateInstance(complexType);
            Set(nullSource, "Children", null);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var nullOwned = copy.Invoke(null, new[] { nullSource, Activator.CreateInstance(contextType, 4096L) });
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(7, createdByCopy.Length);
            Assert.All(createdByCopy.Take(4), item => Assert.Equal(1, Get(item, "DisposeCalls")));
            dispose.Invoke(null, new[] { nullOwned });
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));

            InvokeStatic(complexType, "Reset");
            var setterSource = Activator.CreateInstance(complexType);
            complexType.GetField("ThrowOnChildrenSet", BindingFlags.Public | BindingFlags.Static).SetValue(null, true);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var setterFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { setterSource, Activator.CreateInstance(contextType, 4096L) }));
            Assert.IsType<InvalidOperationException>(setterFailure.InnerException);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(6, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Equal(1, StaticInt(complexType, "TotalDisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var nestedAfterWriteSource = Activator.CreateInstance(complexType);
            complexType.GetField("ThrowAfterChildSet", BindingFlags.Public | BindingFlags.Static).SetValue(null, true);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var nestedAfterWriteFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { nestedAfterWriteSource, Activator.CreateInstance(contextType, 4096L) }));
            Assert.Equal("child setter after write", nestedAfterWriteFailure.InnerException?.Message);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(5, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));

            InvokeStatic(complexType, "Reset");
            var detachAfterWriteSource = Activator.CreateInstance(complexType);
            complexType.GetField("ThrowAfterChildrenNullSet", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, true);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var detachAfterWriteFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { detachAfterWriteSource, Activator.CreateInstance(contextType, 4096L) }));
            Assert.Equal("children null setter after write", detachAfterWriteFailure.InnerException?.Message);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(5, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));

            InvokeStatic(complexType, "Reset");
            var sequenceAfterWriteSource = Activator.CreateInstance(complexType);
            complexType.GetField("ThrowAfterChildrenSet", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, true);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var sequenceAfterWriteFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { sequenceAfterWriteSource, Activator.CreateInstance(contextType, 4096L) }));
            Assert.Equal("children setter after write", sequenceAfterWriteFailure.InnerException?.Message);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(6, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));

            InvokeStatic(complexType, "Reset");
            var applySource = Activator.CreateInstance(complexType);
            var applyOwned = copy.Invoke(null, new[] { applySource, Activator.CreateInstance(contextType, 4096L) });
            receiver = Activator.CreateInstance(receiverType);
            receiverType.GetField("IncomingThrowsAfterWrite", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, 2);
            var applyAfterWriteFailure = Assert.Throws<TargetInvocationException>(() =>
                apply.Invoke(receiver, new[] { applyOwned }));
            Assert.Equal("component setter after write", applyAfterWriteFailure.InnerException?.Message);
            Assert.Null(field.GetValue(receiver));
            Assert.Equal(0, Get(applyOwned, "DisposeCalls"));
            dispose.Invoke(null, new[] { applyOwned });
            Assert.Equal(1, Get(applyOwned, "DisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var committedApplySource = Activator.CreateInstance(complexType);
            var committedApplyOwned = copy.Invoke(
                null,
                new[] { committedApplySource, Activator.CreateInstance(contextType, 4096L) });
            receiver = Activator.CreateInstance(receiverType);
            receiverType.GetField("IncomingThrowsAfterWrite", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, 1);
            receiverType.GetField("ThrowBeforeIncomingNullSet", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, true);
            apply.Invoke(receiver, new[] { committedApplyOwned });
            Assert.Same(committedApplyOwned, field.GetValue(receiver));
            Assert.True((bool)clear.Invoke(receiver, new[] { committedApplyOwned }));
            Assert.Null(field.GetValue(receiver));
            dispose.Invoke(null, new[] { committedApplyOwned });
            Assert.Equal(1, Get(committedApplyOwned, "DisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var budgetSource = Activator.CreateInstance(complexType);
            listAdd = Get(budgetSource, "Children").GetType().GetMethod("Add");
            listAdd.Invoke(Get(budgetSource, "Children"), new[] { Activator.CreateInstance(childType) });
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var budgetFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { budgetSource, Activator.CreateInstance(contextType, 0L) }));
            Assert.IsType<InvalidOperationException>(budgetFailure.InnerException);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(5, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Equal(5, StaticInt(childType, "TotalDisposeCalls"));
            Assert.Equal(1, StaticInt(complexType, "TotalDisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var fixedSource = Activator.CreateInstance(complexType);
            complexType.GetField("NextFixedLength", BindingFlags.Public | BindingFlags.Static).SetValue(null, 2);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var fixedFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { fixedSource, Activator.CreateInstance(contextType, 4096L) }));
            Assert.IsType<InvalidOperationException>(fixedFailure.InnerException);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(8, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Equal(8, StaticInt(childType, "TotalDisposeCalls"));
            Assert.Equal(1, StaticInt(complexType, "TotalDisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var disposalSource = Activator.CreateInstance(complexType);
            var disposalOwned = copy.Invoke(
                null,
                new[] { disposalSource, Activator.CreateInstance(contextType, 4096L) });
            var disposalGraph = new[] { Get(disposalOwned, "Child") }
                .Concat(((System.Collections.IEnumerable)Get(disposalOwned, "Children")).Cast<object>())
                .Concat(((Array)Get(disposalOwned, "FixedChildren")).Cast<object>())
                .ToArray();
            Set(disposalGraph[0], "ThrowOnDispose", true);
            complexType.GetField("ThrowOnDispose", BindingFlags.Public | BindingFlags.Static).SetValue(null, true);
            var disposalFailure = Assert.Throws<TargetInvocationException>(() =>
                dispose.Invoke(null, new[] { disposalOwned }));
            Assert.Equal("child dispose", disposalFailure.InnerException?.Message);
            Assert.All(disposalGraph, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Equal(1, Get(disposalOwned, "DisposeCalls"));

            InvokeStatic(complexType, "Reset");
            var cleanupFailureSource = Activator.CreateInstance(complexType);
            childType.GetField("ThrowOnDisposeCall", BindingFlags.Public | BindingFlags.Static).SetValue(null, 3);
            complexType.GetField("ThrowOnDispose", BindingFlags.Public | BindingFlags.Static).SetValue(null, true);
            childInstances = (System.Collections.IEnumerable)childType
                .GetField("Instances", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            instanceCountBeforeCopy = childInstances.Cast<object>().Count();
            var originalCopyFailure = Assert.Throws<TargetInvocationException>(() =>
                copy.Invoke(null, new[] { cleanupFailureSource, Activator.CreateInstance(contextType, 0L) }));
            Assert.Equal("budget", originalCopyFailure.InnerException?.Message);
            createdByCopy = childInstances.Cast<object>().Skip(instanceCountBeforeCopy).ToArray();
            Assert.Equal(5, createdByCopy.Length);
            Assert.All(createdByCopy, item => Assert.Equal(1, Get(item, "DisposeCalls")));
            Assert.Equal(1, StaticInt(complexType, "TotalDisposeCalls"));
        }

        [Fact]
        public void RoslynAndReflectionLoweredNativeFixturesEmitIdenticalGeneratedSource()
        {
            var shape = BuildComplexBehaviorShape();
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "test_msgs.msg.Complex", "global::test_msgs.msg.Complex",
                    false, false, "", "/phase179/native", "test_msgs/msg/Complex",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: 0,
                    source: 2,
                    ros2Qos: 3,
                    generatesWebSocketCodec: false,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "test_msgs.msg.Complex", "global::test_msgs.msg.Complex",
                    false, false, "", "/phase179/native", "test_msgs/msg/Complex",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: 0,
                    source: 2,
                    ros2Qos: 3,
                    generatesWebSocketCodec: false,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });

            Assert.Equal(
                FoxgloveSourceEmitter.EmitClass(roslyn.Types.Single()),
                FoxgloveSourceEmitter.EmitClass(reflection.Types.Single()));
        }

        public static IEnumerable<object[]> NativeBindingGoldenCases()
        {
            yield return new object[]
            {
                "std_msgs.msg.String",
                BuildStringGoldenShape(),
                new[] { ".Data", "RequireBytes", ".Dispose()" }
            };
            yield return new object[]
            {
                "geometry_msgs.msg.Twist",
                BuildTwistGoldenShape(),
                new[] { ".Angular", ".Linear", "FoxRunRos2CopyNested", "FoxRunRos2DisposeNested" }
            };
            yield return new object[]
            {
                "sensor_msgs.msg.Joy",
                BuildJoyGoldenShape(),
                new[] { ".Axes", ".Buttons", ".Header", "new global::System.Single[", "new global::System.Int32[" }
            };
            yield return new object[]
            {
                "sensor_msgs.msg.Imu",
                BuildImuGoldenShape(),
                new[] { ".Orientation", ".Angular_velocity_covariance", "targetLength", "sourceLength" }
            };
        }

        private static FoxRunGenerationMember BuildNativeGoldenMember(
            string typeName,
            FoxRunRos2MessageShape shape)
            => new FoxRunGenerationMember(
                "Demo", "Receiver", "_incoming", "field",
                typeName, "global::" + typeName,
                canonicalType: shape.CanonicalRosType,
                isValueType: false, isArray: false, elementTypeName: "",
                topic: "/phase179/native", hz: 10f, schemaName: shape.CanonicalRosType,
                policy: (int)FoxRunPolicy.FixedRate, tolerance: 0f,
                hostKind: "UnitTest", rawMemberOrder: 0, conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: shape);

        private static FoxRunGenerationMember BuildNativeTriggerMember(
            string memberName,
            string topic,
            int rawMemberOrder)
            => new FoxRunGenerationMember(
                "Demo", "Receiver", memberName, "field",
                "std_msgs.msg.String", "global::std_msgs.msg.String",
                canonicalType: "std_msgs/msg/String",
                isValueType: false, isArray: false, elementTypeName: "",
                topic: topic, hz: 0f, schemaName: "std_msgs/msg/String",
                policy: (int)FoxRunPolicy.Trigger, tolerance: 0f,
                hostKind: "UnitTest", rawMemberOrder: rawMemberOrder, conditionalSymbols: "",
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: BuildStringGoldenShape());

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static FoxRunRos2MessageShape BuildStringGoldenShape()
            => MessageShape(
                "std_msgs.msg.String",
                "std_msgs/msg/String",
                new FoxRunRos2MessageMemberShape(
                    "Data", FoxRunRos2MessageMemberKind.String, "System.String", "", ""));

        private static FoxRunRos2MessageShape BuildVector3GoldenShape()
            => MessageShape(
                "geometry_msgs.msg.Vector3",
                "geometry_msgs/msg/Vector3",
                Scalar("X", "System.Double"),
                Scalar("Y", "System.Double"),
                Scalar("Z", "System.Double"));

        private static FoxRunRos2MessageShape BuildQuaternionGoldenShape()
            => MessageShape(
                "geometry_msgs.msg.Quaternion",
                "geometry_msgs/msg/Quaternion",
                Scalar("W", "System.Double"),
                Scalar("X", "System.Double"),
                Scalar("Y", "System.Double"),
                Scalar("Z", "System.Double"));

        private static FoxRunRos2MessageShape BuildTimeGoldenShape()
            => MessageShape(
                "builtin_interfaces.msg.Time",
                "builtin_interfaces/msg/Time",
                Scalar("Nanosec", "System.UInt32"),
                Scalar("Sec", "System.Int32"));

        private static FoxRunRos2MessageShape BuildHeaderGoldenShape()
        {
            var time = BuildTimeGoldenShape();
            return MessageShape(
                "std_msgs.msg.Header",
                "std_msgs/msg/Header",
                new FoxRunRos2MessageMemberShape(
                    "Frame_id", FoxRunRos2MessageMemberKind.String, "System.String", "", ""),
                Nested("Stamp", time));
        }

        private static FoxRunRos2MessageShape BuildTwistGoldenShape()
        {
            var vector = BuildVector3GoldenShape();
            return MessageShape(
                "geometry_msgs.msg.Twist",
                "geometry_msgs/msg/Twist",
                Nested("Angular", vector),
                Nested("Linear", vector));
        }

        private static FoxRunRos2MessageShape BuildJoyGoldenShape()
            => MessageShape(
                "sensor_msgs.msg.Joy",
                "sensor_msgs/msg/Joy",
                Sequence("Axes", "System.Single[]", "System.Single"),
                Sequence("Buttons", "System.Int32[]", "System.Int32"),
                Nested("Header", BuildHeaderGoldenShape()));

        private static FoxRunRos2MessageShape BuildImuGoldenShape()
        {
            var vector = BuildVector3GoldenShape();
            return MessageShape(
                "sensor_msgs.msg.Imu",
                "sensor_msgs/msg/Imu",
                Nested("Angular_velocity", vector),
                FixedSequence("Angular_velocity_covariance", "System.Double[]", "System.Double"),
                Nested("Header", BuildHeaderGoldenShape()),
                Nested("Linear_acceleration", vector),
                FixedSequence("Linear_acceleration_covariance", "System.Double[]", "System.Double"),
                Nested("Orientation", BuildQuaternionGoldenShape()),
                FixedSequence("Orientation_covariance", "System.Double[]", "System.Double"));
        }

        private static FoxRunRos2MessageShape BuildComplexBehaviorShape()
        {
            var child = MessageShape(
                "test_msgs.msg.Child",
                "test_msgs/msg/Child",
                new FoxRunRos2MessageMemberShape(
                    "Name", FoxRunRos2MessageMemberKind.String, "System.String", "", ""),
                Scalar("Value", "System.Int32"));
            return MessageShape(
                "test_msgs.msg.Complex",
                "test_msgs/msg/Complex",
                Nested("Child", child),
                new FoxRunRos2MessageMemberShape(
                    "Children",
                    FoxRunRos2MessageMemberKind.Sequence,
                    "System.Collections.Generic.List<test_msgs.msg.Child>",
                    "test_msgs.msg.Child",
                    child.CopyShapeIdentity,
                    sequenceRepresentation: FoxRunRos2SequenceRepresentation.List,
                    nestedShape: child),
                new FoxRunRos2MessageMemberShape(
                    "FixedChildren",
                    FoxRunRos2MessageMemberKind.Sequence,
                    "test_msgs.msg.Child[]",
                    "test_msgs.msg.Child",
                    child.CopyShapeIdentity,
                    canRead: true,
                    canWrite: false,
                    sequenceRepresentation: FoxRunRos2SequenceRepresentation.FixedArray,
                    fixedSize: 2,
                    nestedShape: child),
                FixedSequence("Covariance", "System.Double[]", "System.Double"),
                new FoxRunRos2MessageMemberShape(
                    "Label", FoxRunRos2MessageMemberKind.String, "System.String", "", ""),
                Sequence("Values", "System.Int32[]", "System.Int32"));
        }

        private static FoxRunRos2MessageShape MessageShape(
            string typeName,
            string canonical,
            params FoxRunRos2MessageMemberShape[] members)
            => new FoxRunRos2MessageShape(
                "global::" + typeName,
                canonical,
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: canonical + "|golden",
                members: members,
                diagnostics: Array.Empty<string>());

        private static FoxRunRos2MessageMemberShape Scalar(string name, string typeName)
            => new FoxRunRos2MessageMemberShape(
                name, FoxRunRos2MessageMemberKind.Scalar, typeName, "", "");

        private static FoxRunRos2MessageMemberShape Nested(
            string name,
            FoxRunRos2MessageShape nested)
            => new FoxRunRos2MessageMemberShape(
                name,
                FoxRunRos2MessageMemberKind.NestedMessage,
                nested.FullyQualifiedTypeName.Replace("global::", string.Empty),
                "",
                nested.CopyShapeIdentity,
                nestedShape: nested);

        private static FoxRunRos2MessageMemberShape Sequence(
            string name,
            string typeName,
            string elementTypeName)
            => new FoxRunRos2MessageMemberShape(
                name,
                FoxRunRos2MessageMemberKind.Sequence,
                typeName,
                elementTypeName,
                "",
                sequenceRepresentation: FoxRunRos2SequenceRepresentation.Array);

        private static FoxRunRos2MessageMemberShape FixedSequence(
            string name,
            string typeName,
            string elementTypeName)
            => new FoxRunRos2MessageMemberShape(
                name,
                FoxRunRos2MessageMemberKind.Sequence,
                typeName,
                elementTypeName,
                "",
                canRead: true,
                canWrite: false,
                sequenceRepresentation: FoxRunRos2SequenceRepresentation.FixedArray,
                fixedSize: 0);

        private static string NativeConditionalSection(string source)
        {
            var start = source.IndexOf("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal);
            return start < 0 ? string.Empty : source.Substring(start);
        }

        private static object Get(object target, string propertyName)
        {
            var property = target.GetType().GetProperty(propertyName);
            if (property != null)
                return property.GetValue(target);
            var field = target.GetType().GetField(propertyName);
            if (field != null)
                return field.GetValue(target);
            throw new MissingMemberException(target.GetType().FullName, propertyName);
        }

        private static void Set(object target, string propertyName, object value)
            => target.GetType().GetProperty(propertyName).SetValue(target, value);

        private static void InvokeStatic(Type type, string methodName)
            => type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

        private static int StaticInt(Type type, string fieldName)
            => (int)type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static).GetValue(null);

        private static FoxRunRos2MessageShape[] BuildHostShapes(Fixture fixture)
            => new[]
            {
                FoxRunRoslynRos2MessageShapeBuilder.Build(fixture.Symbol, fixture.Compilation),
                FoxRunReflectionRos2MessageShapeBuilder.Build(fixture.RuntimeType)
            };

        private static Fixture CompileFixture(string source, string metadataName = "vendor_msgs.msg.Command")
        {
            var contract = Ros2Contract.Value;
            var compilation = CSharpCompilation.Create(
                "phase179_shape_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                PlatformReferences().Concat(new[] { contract.Reference }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(metadataName);
            Assert.NotNull(symbol);

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            image.Position = 0;
            var loadContext = new FixtureLoadContext(contract.Image);
            var runtimeAssembly = loadContext.LoadFromStream(image);
            var runtimeType = runtimeAssembly.GetType(metadataName, throwOnError: true);
            return new Fixture(compilation, symbol, runtimeType, loadContext);
        }

        private static readonly Lazy<Contract> Ros2Contract = new Lazy<Contract>(() =>
        {
            var compilation = CSharpCompilation.Create(
                "ros2cs_common",
                new[] { CSharpSyntaxTree.ParseText("namespace ROS2 { public interface Message { } }") },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            var bytes = image.ToArray();
            return new Contract(MetadataReference.CreateFromImage(bytes), bytes);
        });

        private const string CompleteNativeSeamSource = @"
using System;
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(string id, string topic, string declaringType,
            string memberName, string canonicalRosType,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode,
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset qos, bool supportsNative,
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy, float hz,
            bool hasExplicitHz, float heartbeatIntervalSeconds) { }
    }
    public sealed class FoxRunRos2CopyContext
    {
        public void RequireBytes(long byteCount) { }
    }
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }
        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
            Action<T> apply, Func<T, bool> clearIfOwned,
            Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
            Func<bool> canApply) where T : ROS2.Message, new();
    }
}";

        private static readonly Lazy<MetadataReference> NativeAssembly = new Lazy<MetadataReference>(() =>
            BuildNativeAssemblyReference(CompleteNativeSeamSource));

        private static readonly Lazy<MetadataReference> CoreAttributeAssembly =
            new Lazy<MetadataReference>(BuildCoreAttributeAssemblyReference);

        private static MetadataReference BuildCoreAttributeAssemblyReference()
        {
            var attributeRoot = Path.Combine(
                FindRepoRoot(), "Packages", "dev.unity2foxglove.sdk", "Runtime", "Components", "Attributes");
            var trees = new[]
                {
                    "FoxRunAttribute.cs",
                    "FoxRunFlow.cs",
                    "FoxRunPolicy.cs",
                    Path.Combine("..", "..", "Utilities", "FoxRunUpdatePolicy.cs"),
                    "FoxRunEncoding.cs",
                    "FoxRunEndpoint.cs",
                    "FoxRunRos2QosPreset.cs"
                }
                .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(attributeRoot, file))));
            var compilation = CSharpCompilation.Create(
                "Unity.FoxgloveSDK.FoxRunContractFixture",
                trees,
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return MetadataReference.CreateFromImage(image.ToArray());
        }

        private static MetadataReference BuildNativeAssemblyReference(string source)
        {
            var compilation = CSharpCompilation.Create(
                "Unity2Foxglove.Ros2ForUnity.Native",
                new[] { CSharpSyntaxTree.ParseText(source) },
                PlatformReferences().Concat(new[]
                {
                    CoreAttributeAssembly.Value,
                    Ros2Contract.Value.Reference
                }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return MetadataReference.CreateFromImage(image.ToArray());
        }

        private static GeneratorDriverRunResult RunGenerator(
            string source,
            string schemaName,
            string messageTypeName = "vendor_msgs.msg.Command",
            bool nativeDefine = false,
            bool nativeReference = false,
            string sourceEndpoint = "Ros2Native",
            string mode = "Subscribe",
            string sourceExpression = null,
            string nativeReferenceSource = null,
            string encoding = null,
            string onlyIf = null)
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: nativeDefine ? new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" } : Array.Empty<string>());
            var resolvedSourceExpression = !string.IsNullOrEmpty(sourceExpression)
                ? sourceExpression
                : string.IsNullOrEmpty(sourceEndpoint)
                    ? null
                    : "Unity.FoxgloveSDK.Components.FoxRunEndpoint." + sourceEndpoint;
            var sourceArgument = string.IsNullOrEmpty(resolvedSourceExpression)
                ? string.Empty
                : "Source = " + resolvedSourceExpression + ",";
            source += @"
namespace Demo
{
    public partial class Receiver
    {
        [Unity.FoxgloveSDK.Components.FoxRun(""/command"",
            Mode = Unity.FoxgloveSDK.Components.FoxRunFlow." + mode + @",
            " + sourceArgument + @"
            Ros2Qos = Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.SensorData,
            " + (string.IsNullOrEmpty(encoding)
                ? string.Empty
                : "Encoding = Unity.FoxgloveSDK.Components.FoxRunEncoding." + encoding + ",") + @"
            " + (string.IsNullOrEmpty(onlyIf)
                ? string.Empty
                : "OnlyIf = nameof(" + onlyIf + "),") + @"
            SchemaName = """ + schemaName + @""")]
        private " + messageTypeName + @" _incoming;
        " + (string.IsNullOrEmpty(onlyIf)
            ? string.Empty
            : "private bool " + onlyIf + "() => true;") + @"
    }
}";
            var references = PlatformReferences()
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Concat(new[]
                {
                    CoreAttributeAssembly.Value,
                    Ros2Contract.Value.Reference
                })
                .Concat(nativeReference
                    ? new[]
                    {
                        nativeReferenceSource == null
                            ? NativeAssembly.Value
                            : BuildNativeAssemblyReference(nativeReferenceSource)
                    }
                    : Array.Empty<MetadataReference>());
            var compilation = CSharpCompilation.Create(
                "phase179_generator_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: parseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None));
            return driver.RunGenerators(compilation).GetRunResult();
        }

        private static string ValidMessageSource(
            string ns,
            string members,
            bool publicConstructor,
            string interfaceName = "ROS2.Message",
            bool includeReceiver = false)
            => "namespace " + ns + @"
{
    public sealed class Command : " + interfaceName + @"
    {
        " + (publicConstructor ? "public" : "private") + @" Command() { }
        " + members + @"
    }
}";

        private static MetadataReference[] PlatformReferences()
            => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(typeof(FoxRunRos2BindingGenerationTests).Assembly.Location),
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => MetadataReference.CreateFromFile(path))
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory + ".");
        }

        private sealed class Fixture
        {
            public Fixture(
                CSharpCompilation compilation,
                INamedTypeSymbol symbol,
                Type runtimeType,
                AssemblyLoadContext loadContext)
            {
                Compilation = compilation;
                Symbol = symbol;
                RuntimeType = runtimeType;
                LoadContext = loadContext;
            }

            public CSharpCompilation Compilation { get; }
            public INamedTypeSymbol Symbol { get; }
            public Type RuntimeType { get; }
            private AssemblyLoadContext LoadContext { get; }
        }

        private sealed class Contract
        {
            public Contract(MetadataReference reference, byte[] image)
            {
                Reference = reference;
                Image = image;
            }

            public MetadataReference Reference { get; }
            public byte[] Image { get; }
        }

        private sealed class FixtureLoadContext : AssemblyLoadContext
        {
            private readonly byte[] _ros2ContractImage;

            public FixtureLoadContext(byte[] ros2ContractImage)
                : base(isCollectible: true)
            {
                _ros2ContractImage = ros2ContractImage;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (!string.Equals(assemblyName.Name, "ros2cs_common", StringComparison.Ordinal))
                    return null;

                using var image = new MemoryStream(_ros2ContractImage, writable: false);
                return LoadFromStream(image);
            }
        }
    }
}
