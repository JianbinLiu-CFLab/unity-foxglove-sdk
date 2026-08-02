// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 96 validation for ROS2 Bridge topic profiles and QoS metadata.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase96Validation.
    /// </summary>
    public static class Phase96Validation
    {
        private const ulong SampleTimeNs = 1_700_096_000_000_000_000UL;
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 96: ROS2 Bridge Topic Profiles And QoS ===");
            _passed = 0;

            VerifyTopicHelpers();
            VerifyQosHelpers();
            VerifyFrameHeaderCompatibility();
            VerifyRuntimeSourceIntegration();
            VerifySidecarSourceExpectations();
            VerifyInspectorSourceExpectations();

            Console.WriteLine($"Phase 96: {_passed} checks passed.");
        }

        private static void VerifyTopicHelpers()
        {
            Check(Resolve("", "/tf", "") == "/tf", "96A-1: empty namespace preserves topic");
            Check(Resolve("/robot1", "/tf", "") == "/robot1/tf", "96A-2: namespace prefixes publisher topic");
            Check(Resolve("/robot1/", "/unity//point_cloud", "") == "/robot1/unity/point_cloud",
                "96A-3: namespace and topic collapse duplicate slashes");
            Check(Resolve("/", "/tf", "") == "/tf", "96A-4: root namespace behaves like no prefix");
            Check(Resolve("/robot1", "/unity/point_cloud", "/lidar/front") == "/lidar/front",
                "96A-5: absolute override wins over manager namespace");

            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeNamespace("robot1", out _, out _),
                "96A-6: namespace without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeTopic("lidar/front", out _, out _),
                "96A-7: override without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic("/robot1", "tf", "", out _, out _),
                "96A-8: publisher topic without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic("", "/", "", out _, out _),
                "96A-9: root publisher topic is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeTopic("/", out _, out _),
                "96A-10: root override topic is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeNamespace("/robot one", out _, out _),
                "96A-11: namespace with invalid ROS 2 characters is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeTopic("/lidar/front$", out _, out _),
                "96A-12: override with invalid ROS 2 characters is rejected");
            Check(!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic("", "/unity/bad topic", "", out _, out _),
                "96A-13: publisher topic with invalid ROS 2 characters is rejected");
        }

        private static void VerifyQosHelpers()
        {
            Check((int)FoxRunQosProfile.Default == 1, "96B-1: Default profile enum value is stable");
            Check((int)FoxRunQosProfile.SensorData == 2, "96B-2: Sensor Data profile enum value is stable");
            Check((int)FoxRunQosProfile.SystemDefault == 3, "96B-3: System Default profile enum value is stable");
            Check((int)FoxRunQosHistory.KeepAll == 3, "96B-4: Keep All history enum value is stable");

            var reliable = FoxRunRos2QosProfileResolver.FromProfile(FoxRunQosProfile.Default);
            Check(reliable.Reliability == FoxRunQosReliability.Reliable
                  && reliable.Durability == FoxRunQosDurability.Volatile
                  && reliable.History == FoxRunQosHistory.KeepLast
                  && reliable.Depth == 10,
                "96B-5: Default maps to reliable volatile Keep Last depth 10");

            var sensor = FoxRunRos2QosProfileResolver.FromProfile(FoxRunQosProfile.SensorData);
            Check(sensor.Reliability == FoxRunQosReliability.BestEffort
                  && sensor.Durability == FoxRunQosDurability.Volatile
                  && sensor.History == FoxRunQosHistory.KeepLast
                  && sensor.Depth == 5,
                "96B-6: Sensor Data maps to best-effort volatile Keep Last depth 5");

            var systemDefault = FoxRunRos2QosProfileResolver.FromProfile(FoxRunQosProfile.SystemDefault);
            Check(systemDefault.Reliability == FoxRunQosReliability.SystemDefault
                  && systemDefault.Durability == FoxRunQosDurability.SystemDefault
                  && systemDefault.History == FoxRunQosHistory.SystemDefault
                  && systemDefault.Depth == 0,
                "96B-7: System Default preserves every transport-default policy");

            var custom = FoxRunRos2QosProfileResolver.Resolve(
                FoxRunQosProfile.Default,
                hasProfile: true,
                FoxRunQosReliability.BestEffort,
                hasReliability: true,
                FoxRunQosDurability.TransientLocal,
                hasDurability: true,
                FoxRunQosHistory.KeepLast,
                hasHistory: true,
                depth: 1,
                hasDepth: true,
                FoxRunResolvedQos.Default);
            Check(custom.Success
                  && custom.Qos.Reliability == FoxRunQosReliability.BestEffort
                  && custom.Qos.Durability == FoxRunQosDurability.TransientLocal
                  && custom.Qos.History == FoxRunQosHistory.KeepLast
                  && custom.Qos.Depth == 1,
                "96B-8: explicit portable policy overrides remain exact");

            var invalid = FoxRunRos2QosProfileResolver.Resolve(
                FoxRunQosProfile.Default,
                hasProfile: false,
                default,
                hasReliability: false,
                default,
                hasDurability: false,
                FoxRunQosHistory.KeepAll,
                hasHistory: true,
                depth: 1,
                hasDepth: true,
                FoxRunResolvedQos.Default);
            Check(!invalid.Success
                  && invalid.DiagnosticCode == FoxRunQosDiagnosticCode.DepthRequiresKeepLast,
                "96B-9: Keep All with depth fails closed");
        }

        private static void VerifyFrameHeaderCompatibility()
        {
            var legacy = new Ros2BridgeFrame(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                1,
                new byte[] { 0, 1, 0, 0, 1 });
            var legacyHeader = ReadHeader(Ros2BridgeFrameWriter.Write(legacy));
            Check(legacyHeader["profileName"] == null && legacyHeader["qos"] == null,
                "96C-1: legacy frame constructor omits QoS metadata");
            Check(!legacy.Qos.HasValue,
                "96C-2: legacy frame keeps QoS optional");

            var qos = FoxRunResolvedQos.SensorData;
            var profiled = new Ros2BridgeFrame(
                "/lidar/front",
                "foxglove_msgs/msg/PointCloud",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                2,
                new byte[] { 0, 1, 0, 0, 2 },
                qos);
            var profiledHeader = ReadHeader(Ros2BridgeFrameWriter.Write(profiled));
            Check(profiledHeader["topic"]?.ToString() == "/lidar/front", "96C-3: profiled frame uses effective bridge topic");
            Check(profiledHeader["profileName"]?.ToString() == "sensor_data"
                  && profiledHeader["qos"]?["profile"]?.ToString() == "sensor_data",
                "96C-4: header contains the portable profile");
            Check(profiledHeader["qos"]?["reliability"]?.ToString() == "best_effort", "96C-5: header contains QoS reliability");
            Check(profiledHeader["qos"]?["durability"]?.ToString() == "volatile", "96C-6: header contains QoS durability");
            Check(profiledHeader["qos"]?["history"]?.ToString() == "keep_last"
                  && profiledHeader["qos"]?["depth"]?.Value<int>() == 5,
                "96C-7: header contains QoS history and depth");
        }

        private static void VerifyRuntimeSourceIntegration()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var managerProviders = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var bridgeProvider = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");
            var bridgeDrawer = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2BridgeProviderDrawer.cs");
            var bridgeQos = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/FoxRunRos2Qos.cs");
            var wrapper = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgePublisher.cs");

            Check(bridgeProvider.Contains("_host")
                  && bridgeProvider.Contains("_port")
                  && bridgeProvider.Contains("Ros2BridgeRuntime")
                  && bridgeQos.Contains("public readonly struct FoxRunResolvedQos")
                  && bridgeQos.Contains("public static class FoxRunRos2QosProfileResolver"),
                "96D-1: Bridge package owns runtime configuration and portable QoS authority");
            Check(bridgeProvider.Contains("route.Topic")
                  && bridgeProvider.Contains("ResolveQos(route.DeliveryPolicy)")
                  && bridgeProvider.Contains("FoxRunDeliveryPolicy policy"),
                "96D-2: Bridge Provider resolves neutral topic and delivery policy");
            Check(bridgeProvider.Contains("IFoxRunOrdinaryPayloadMapper")
                  && bridgeProvider.Contains("TryMapOrdinary")
                  && bridgeProvider.Contains("FoxRunOrdinaryPayloadContribution")
                  && bridgeProvider.Contains("Ros2BridgeMcapCodecs.MessageEncoding"),
                "96D-3: Bridge Provider owns ordinary-value CDR mapping");
            Check(bridgeProvider.Contains("runtime.PreparePublisher")
                  && bridgeProvider.Contains("Ros2BridgeFrame.CreateOwned")
                  && bridgeProvider.Contains("runtime.TryEnqueue(frame, out reason)"),
                "96D-4: Bridge Provider prepares and enqueues QoS-profiled owned frames");
            Check(publisherBase.Contains("FoxRunOrdinaryPayloadRequest")
                  && publisherBase.Contains("_topic")
                  && publisherBase.Contains("FoxRunDeliveryPolicy.ProviderDefault")
                  && !publisherBase.Contains("_ros2BridgeTopicOverride")
                  && !publisherBase.Contains("EffectiveRos2BridgeTopic"),
                "96D-5: Publisher base passes topic and delivery policy through a neutral request");
            Check(managerProviders.Contains("PublishOrdinaryTransports")
                  && bridgeDrawer.Contains("FoxRunTransportProviderDrawerRegistry.Register")
                  && bridgeDrawer.Contains("Ros2BridgeTransportProvider")
                  && !manager.Contains("Ros2Bridge")
                  && !managerProviders.Contains("Ros2Bridge"),
                "96D-6: generic Manager fanout and Bridge Provider drawer preserve package ownership");
            Check(wrapper.Contains("Ros2BridgeFrame.CreateOwned(", StringComparison.Ordinal)
                  && wrapper.Contains("Ros2BridgeFrame.CdrEncoding", StringComparison.Ordinal)
                  && !wrapper.Contains("new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding", StringComparison.Ordinal),
                "96D-7: Phase94/95 wrapper uses owned-payload frame construction");
        }

        private static void VerifySidecarSourceExpectations()
        {
            var sidecar = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");

            Check(sidecar.Contains("profileName") && sidecar.Contains("qos"),
                "96E-1: sidecar parses optional QoS header fields");
            Check(sidecar.Contains("qos.reliability must be system_default, reliable, or best_effort"),
                "96E-2: sidecar rejects invalid reliability strings");
            Check(sidecar.Contains("qos.durability must be system_default, volatile, or transient_local"),
                "96E-3: sidecar rejects invalid durability strings");
            Check(sidecar.Contains("qos.history must be system_default, keep_last, or keep_all")
                  && sidecar.Contains("qos.depth must be 0 unless qos.history is keep_last"),
                "96E-4: sidecar rejects invalid history/depth combinations");
            Check(sidecar.Contains("auto qos = make_qos(frame);")
                  && sidecar.Contains("publisher_factory_(frame.topic, frame.schema_name, qos)"),
                "96E-5: sidecar applies requested QoS through its publisher factory");
            Check(sidecar.Contains("reused with different schemaName or QoS: was [")
                  && sidecar.Contains("] got ["),
                "96E-6: sidecar rejects same-topic schema/QoS conflicts");
            Check(sidecar.Contains("profile=%s reliability=%s durability=%s history=%s depth=%d"),
                "96E-7: sidecar logs publisher QoS details");
        }

        private static void VerifyInspectorSourceExpectations()
        {
            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var dataTransportEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.DataTransport.cs");
            var publishDataEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var bridgeProvider = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");
            var bridgeDrawer = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2BridgeProviderDrawer.cs");
            var cameraEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var pointCloudEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");

            Check(dataTransportEditor.Contains("DrawFoxRunTransportProviderExtensions")
                  && publishDataEditor.Contains("FoxRunTransportProviderDrawerRegistry.Capture")
                  && !managerEditor.Contains("DrawRos2BridgeSection")
                  && !dataTransportEditor.Contains("DrawRos2BridgeSection")
                  && !publishDataEditor.Contains("DrawRos2BridgeSection"),
                "96F-1: Manager Inspector hosts generic Provider extensions under Data Transport");
            Check(bridgeProvider.Contains("IFoxRunOrdinaryPayloadMapper")
                  && bridgeProvider.Contains("ResolveQos(route.DeliveryPolicy)")
                  && bridgeProvider.Contains("route.Topic"),
                "96F-2: Bridge Provider owns topic and portable QoS mapping");
            Check(bridgeDrawer.Contains("\"ROS 2 Bridge\"")
                  && bridgeDrawer.Contains("\"Available\"")
                  && bridgeDrawer.Contains("\"Auto Connect\"")
                  && bridgeDrawer.Contains("\"Host\""),
                "96F-3: extracted Bridge Provider labels are compact product labels");
            Check(cameraEditor.Contains("Provider Payload")
                  && pointCloudEditor.Contains("Packed Provider Frame")
                  && !cameraEditor.Contains("Bridge Topic Override")
                  && !pointCloudEditor.Contains("Bridge Topic Override"),
                "96F-4: custom publisher Inspectors expose Provider-neutral payload controls");
            Check(publisherBase.Contains("_topic")
                  && publisherBase.Contains("FoxRunOrdinaryPayloadRequest")
                  && bridgeProvider.Contains("route.Topic"),
                "96F-5: effective topic flows through the neutral Provider request");
            Check(publisherBase.Contains("FoxRunDeliveryPolicy.ProviderDefault")
                  && bridgeProvider.Contains("route.DeliveryPolicy")
                  && bridgeProvider.Contains("FoxRunResolvedQos"),
                "96F-6: effective QoS is resolved inside the Bridge Provider");
        }

        private static MethodDeclarationSyntax FindMethod(string source, string methodName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == methodName && method.Body != null)
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static InvocationExpressionSyntax[] DirectInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : DirectInvocations(method.Body.Statements).ToArray();
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectInvocations(IEnumerable<StatementSyntax> statements)
        {
            foreach (var statement in statements.OfType<ExpressionStatementSyntax>())
            {
                if (statement.Expression is InvocationExpressionSyntax invocation)
                    yield return invocation;
            }
        }

        private static InvocationExpressionSyntax[] ExecutableInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.Body.Statements.SelectMany(ExecutableInvocations).ToArray();
        }

        private static IEnumerable<InvocationExpressionSyntax> ExecutableInvocations(StatementSyntax statement)
        {
            return statement is LocalFunctionStatementSyntax
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : statement.DescendantNodes(ShouldDescendIntoExecutableNode).OfType<InvocationExpressionSyntax>();
        }

        private static bool ShouldDescendIntoExecutableNode(SyntaxNode node)
        {
            return !(node is AnonymousFunctionExpressionSyntax)
                   && !(node is LocalFunctionStatementSyntax);
        }

        private static IfStatementSyntax[] DirectIfStatements(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Array.Empty<IfStatementSyntax>()
                : method.Body.Statements.OfType<IfStatementSyntax>().ToArray();
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectThenInvocations(IfStatementSyntax statement)
        {
            return DirectBranchStatements(statement?.Statement)
                .OfType<ExpressionStatementSyntax>()
                .Select(branchStatement => branchStatement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null);
        }

        private static IEnumerable<StatementSyntax> DirectBranchStatements(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements;

            return statement == null
                ? Enumerable.Empty<StatementSyntax>()
                : new[] { statement };
        }

        private static InvocationExpressionSyntax[] AllInvocations(MethodDeclarationSyntax method)
        {
            return method == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        }

        private static bool HasGetBoolCondition(IfStatementSyntax statement, string propertyName)
        {
            return statement?.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "GetBool")
                   && HasStringArgument(invocation, 0, propertyName);
        }

        private static bool IsRos2BridgeOutputSubsection(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                   && HasStringArgument(invocation, 0, "ROS 2 Bridge Output")
                   && HasStringArgument(invocation, 1, "DataTransportRos2Bridge")
                   && HasRefIdentifierArgument(invocation, 2, "_dataTransportRos2BridgeExpanded")
                   && HasMethodGroupArgument(invocation, 3, "DrawRos2BridgeSection");
        }

        private static bool IsPublishDataTransportSubsection(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                   && HasStringArgument(invocation, 0, "Publish Data")
                   && HasStringArgument(invocation, 1, "DataTransportPublish")
                   && HasRefIdentifierArgument(invocation, 2, "_dataTransportPublishExpanded")
                   && HasMethodGroupArgument(invocation, 3, "DrawPublishDataSection");
        }

        private static bool HasStringArgument(InvocationExpressionSyntax invocation, int argumentIndex, string value)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is LiteralExpressionSyntax literal
                   && literal.IsKind(SyntaxKind.StringLiteralExpression)
                   && literal.Token.ValueText == value;
        }

        private static bool HasRefIdentifierArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static bool HasMethodGroupArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string methodName)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && IsMethodGroupNamed(invocation.ArgumentList.Arguments[argumentIndex].Expression, methodName);
        }

        private static bool HasMethodGroupArgument(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Any(argument =>
                       IsMethodGroupNamed(argument.Expression, methodName));
        }

        private static bool IsMethodGroupNamed(ExpressionSyntax expression, string methodName)
        {
            if (expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == methodName;

            return expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == methodName;
        }

        private static bool IsInvocationNamed(InvocationExpressionSyntax invocation, string name)
        {
            if (invocation?.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == name;

            return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == name;
        }

        private static string Resolve(string bridgeNamespace, string publisherTopic, string overrideTopic)
        {
            if (!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(
                bridgeNamespace,
                publisherTopic,
                overrideTopic,
                out var effective,
                out var error))
            {
                throw new Exception(error);
            }

            return effective;
        }

        private static JObject ReadHeader(byte[] bytes)
        {
            var headerLength = ReadUInt32(bytes, 8);
            var headerJson = Encoding.UTF8.GetString(bytes, 16, checked((int)headerLength));
            return JObject.Parse(headerJson);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }
    }
}
