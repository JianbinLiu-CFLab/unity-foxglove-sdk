// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits ROS-free XCDR1 writers for Phase181 custom FoxRun DTO envelopes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    internal static class FoxRunBridgeSourceEmitter
    {
        private const string RosPackageName =
            "unity2foxglove_foxrun_interfaces_v1";
        private const string BridgeProviderId =
            "unity2foxglove.ros2bridge";

#if FOXRUN_BRIDGE_ANALYZER
        internal static string GeneratedSourceName(
            string ns,
            string className)
        {
            var identity = string.IsNullOrEmpty(ns)
                ? className
                : ns + "." + className;
            return IdentifierUtils.SanitizeFileStem(identity)
                   + "_unity2foxglove_ros2bridge_typed_cdr_FoxRun.g.cs";
        }
#endif

        internal static string EmitBridgeContribution(
            FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var topicMap = type.Members
                .Where(member => member.Mode != 2)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var topics = topicMap.Keys
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var subscribeTopicMap = type.Members
                .Where(member => member.Mode != 1)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var subscribeTopics = subscribeTopicMap.Keys
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var hasPublish = topics.Any(topic =>
                    topicMap[topic].Count == 1
                    && IsSupportedPublish(topicMap[topic][0]));
            var subscribeBindings = BuildSubscribeBindings(
                type,
                topics,
                subscribeTopics,
                subscribeTopicMap);
            var hasSubscribe = subscribeBindings.Count != 0;
            if (!hasPublish && !hasSubscribe)
            {
                return string.Empty;
            }

            var body = new StringBuilder();
            var pad = string.IsNullOrEmpty(type.Namespace) ? string.Empty : "    ";

            if (!string.IsNullOrEmpty(type.Namespace))
            {
                body.Append("namespace ")
                    .Append(type.Namespace)
                    .AppendLine();
                body.AppendLine("{");
            }

            body.Append(pad)
                .Append("public partial class ")
                .Append(EscapeIdentifier(type.ClassName))
                .Append(" : ");
            var interfaces = new List<string>();
            if (hasPublish)
            {
                interfaces.Add(
                    "global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedPublishSource");
            }
            if (hasSubscribe)
            {
                interfaces.Add(
                    "global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource");
            }
            body.AppendLine(string.Join(", ", interfaces));
            body.Append(pad).AppendLine("{");
            if (hasPublish)
            {
                EmitPublishDispatch(
                    body,
                    type,
                    topics,
                    topicMap,
                    pad);
            }
            if (hasSubscribe)
            {
                EmitSubscribeDispatch(
                    body,
                    subscribeBindings,
                    pad);
            }
            if (hasPublish)
                EmitBuilders(body, topics, topicMap, pad);
            if (hasSubscribe)
            {
                EmitReaders(
                    body,
                    subscribeBindings,
                    pad);
            }
            body.Append(pad).AppendLine("}");

            if (!string.IsNullOrEmpty(type.Namespace))
                body.AppendLine("}");

            return body.ToString();
        }

        private static void EmitPublishDispatch(
            StringBuilder sb,
            FoxRunGenerationType type,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxRunGenerationMember>> topicMap,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    bool global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedPublishSource.FoxRunBridge_TryBuildPublish(int topicIndex, ulong nowNs, out global::Unity.FoxgloveSDK.Components.FoxRunTransportPublishRoute route, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        route = default;");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0;
                 topicIndex < topics.Count;
                 topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count != 1 || !IsSupportedPublish(fields[0]))
                    continue;

                var member = fields[0];
                var isStandard = IsSupportedStandardPublish(member);
                var stableId = BuildStableMemberId(
                    type.DeclaringType,
                    member.MemberKind,
                    member.MemberName,
                    member.Topic,
                    member.Mode,
                    member.JsonFieldName);
                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine($"{pad}            {{");
                if (isStandard)
                {
                    var sourceType = GlobalTypeName(
                        member.TypeShape.TypeName);
                    sb.AppendLine(
                        $"{pad}                if (!global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrSerializerRegistry.TryGetByClrType(typeof({sourceType}), out var __serializer))");
                    sb.AppendLine($"{pad}                {{");
                    sb.AppendLine(
                        $"{pad}                    reason = \"The generated Bridge CDR serializer registry has no entry for the declared Foxglove type.\";");
                    sb.AppendLine($"{pad}                    return false;");
                    sb.AppendLine($"{pad}                }}");
                    sb.AppendLine(
                        $"{pad}                var __source = __foxRunCapture_{topicIndex}_0;");
                    sb.AppendLine(
                        $"{pad}                if ((object)__source == null) {{ reason = \"Official Foxglove ROS 2 message is null.\"; return false; }}");
                    sb.AppendLine(
                        $"{pad}                if (__foxRunCaptureSequence_{topicIndex} == 0) {{ reason = \"ROS 2 message sequence was not captured.\"; return false; }}");
                    sb.AppendLine($"{pad}                byte[] payload;");
                    sb.AppendLine($"{pad}                try");
                    sb.AppendLine($"{pad}                {{");
                    sb.AppendLine(
                        $"{pad}                    payload = __serializer.Serialize(__source);");
                    sb.AppendLine($"{pad}                }}");
                    sb.AppendLine(
                        $"{pad}                catch (global::System.Exception exception)");
                    sb.AppendLine($"{pad}                {{");
                    sb.AppendLine(
                        $"{pad}                    reason = exception.Message ?? \"Official Foxglove ROS 2 serialization failed.\";");
                    sb.AppendLine(
                        $"{pad}                    if (reason.Length > 512) reason = reason.Substring(0, 512);");
                    sb.AppendLine($"{pad}                    return false;");
                    sb.AppendLine($"{pad}                }}");
                    sb.AppendLine(
                        $"{pad}                route = new global::Unity.FoxgloveSDK.Components.FoxRunTransportPublishRoute(");
                    sb.AppendLine(
                        $"{pad}                    \"{CSharpStringLiteral(stableId)}\",");
                    sb.AppendLine(
                        $"{pad}                    \"{CSharpStringLiteral(member.Topic)}\",");
                    sb.AppendLine($"{pad}                    __serializer.SchemaName,");
                    sb.AppendLine($"{pad}                    payload,");
                    sb.AppendLine($"{pad}                    nowNs,");
                    sb.AppendLine(
                        $"{pad}                    __foxRunCaptureSequence_{topicIndex},");
                    sb.AppendLine(
                        $"{pad}                    new global::Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy(");
                    sb.AppendLine(
                        $"{pad}                        global::Unity.FoxgloveSDK.Components.{ReliabilityLiteral(member.Reliability)},");
                    sb.AppendLine(
                        $"{pad}                        global::Unity.FoxgloveSDK.Components.{DurabilityLiteral(member.Durability)},");
                    sb.AppendLine(
                        $"{pad}                        global::Unity.FoxgloveSDK.Components.{HistoryLiteral(member.History)},");
                    sb.AppendLine(
                        $"{pad}                        {member.Depth}),");
                    sb.AppendLine($"{pad}                    \"cdr\",");
                    sb.AppendLine($"{pad}                    \"ros2msg\");");
                    sb.AppendLine($"{pad}                return true;");
                    sb.AppendLine($"{pad}            }}");
                    continue;
                }

                var shape = ProjectShape(member.TypeShape);
                var schemaName = RosPackageName
                                 + "/msg/"
                                 + shape.PayloadIdentity
                                 + "Envelope";
                sb.AppendLine(
                    $"{pad}                if (!__TryBuildFoxRunRos2Cdr_{topicIndex}(nowNs, out var payload, out reason))");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine(
                    $"{pad}                route = new global::Unity.FoxgloveSDK.Components.FoxRunTransportPublishRoute(");
                sb.AppendLine(
                    $"{pad}                    \"{CSharpStringLiteral(stableId)}\",");
                sb.AppendLine(
                    $"{pad}                    \"{CSharpStringLiteral(member.Topic)}\",");
                sb.AppendLine(
                    $"{pad}                    \"{CSharpStringLiteral(schemaName)}\",");
                sb.AppendLine($"{pad}                    payload,");
                sb.AppendLine($"{pad}                    nowNs,");
                sb.AppendLine(
                    $"{pad}                    __foxRunCaptureSequence_{topicIndex},");
                sb.AppendLine(
                    $"{pad}                    new global::Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy(");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{ReliabilityLiteral(member.Reliability)},");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{DurabilityLiteral(member.Durability)},");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{HistoryLiteral(member.History)},");
                sb.AppendLine(
                    $"{pad}                        {member.Depth}),");
                sb.AppendLine($"{pad}                    \"cdr\",");
                sb.AppendLine($"{pad}                    \"ros2msg\");");
                sb.AppendLine($"{pad}                return true;");
                sb.AppendLine($"{pad}            }}");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine(
                $"{pad}                reason = \"The Bridge physical emitter has no publish binding for this topic index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static IReadOnlyList<SubscribeBindingShape>
            BuildSubscribeBindings(
                FoxRunGenerationType type,
                IReadOnlyList<string> publishTopics,
                IReadOnlyList<string> subscribeTopics,
                IReadOnlyDictionary<string, List<FoxRunGenerationMember>>
                    subscribeTopicMap)
        {
            var bindings = new List<SubscribeBindingShape>();
            for (var topicIndex = 0;
                 topicIndex < subscribeTopics.Count;
                 topicIndex++)
            {
                var topic = subscribeTopics[topicIndex];
                var members = subscribeTopicMap[topic];
                if (members.Count != 1)
                {
                    continue;
                }

                var member = members[0];
                var isStandard = IsSupportedStandardSubscribe(member);
                if (!isStandard
                    && !IsSupportedCustomSubscribe(member))
                {
                    continue;
                }

                var shape = isStandard
                    ? null
                    : ProjectShape(member.TypeShape);
                var schemaContent = isStandard
                    ? string.Empty
                    : BuildSchemaContent(
                        shape,
                        RosPackageName);
                var publishTopicIndex = -1;
                if (member.Mode == 3)
                {
                    for (var candidate = 0;
                         candidate < publishTopics.Count;
                         candidate++)
                    {
                        if (!string.Equals(
                                publishTopics[candidate],
                                member.Topic,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        publishTopicIndex = candidate;
                        break;
                    }
                }

                bindings.Add(
                    new SubscribeBindingShape(
                        bindings.Count,
                        topicIndex,
                        publishTopicIndex,
                        member,
                        shape,
                        isStandard
                            ? string.Empty
                            : RosPackageName
                              + "/msg/"
                              + shape.PayloadIdentity
                              + "Envelope",
                        isStandard
                            ? string.Empty
                            : Sha256Hex(schemaContent),
                        isStandard));
            }

            return bindings;
        }

        private static void EmitSubscribeDispatch(
            StringBuilder sb,
            IReadOnlyList<SubscribeBindingShape> bindings,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    int global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource.FoxRunBridge_SubscribeBindingCount => {bindings.Count};");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    bool global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource.FoxRunBridge_TryGetSubscribeBinding(int bindingIndex, out global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeGeneratedSubscribeBinding binding, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        binding = default;");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        switch (bindingIndex)");
            sb.AppendLine($"{pad}        {{");
            foreach (var binding in bindings)
            {
                var member = binding.Member;
                var decoderEntry = "__foxRunCdrDecoder_" + binding.BindingIndex;
                var schemaEntry = "__foxRunCdrSchema_" + binding.BindingIndex;
                var stableId = BuildStableMemberId(
                    member.DeclaringType,
                    member.MemberKind,
                    member.MemberName,
                    member.Topic,
                    member.Mode,
                    member.JsonFieldName);
                sb.AppendLine($"{pad}            case {binding.BindingIndex}:");
                if (binding.IsStandard)
                {
                    sb.AppendLine(
                        $"{pad}                if (!global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrDeserializerRegistry.TryGetByClrType(typeof({GlobalTypeName(member.TypeShape.TypeName)}), out var {decoderEntry}))");
                    sb.AppendLine($"{pad}                {{");
                    sb.AppendLine(
                        $"{pad}                    reason = \"The generated Bridge CDR decoder registry has no entry for the declared Foxglove type.\";");
                    sb.AppendLine($"{pad}                    return false;");
                    sb.AppendLine($"{pad}                }}");
                    sb.AppendLine(
                        $"{pad}                if (!global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.FoxgloveRos2MsgSchemaCatalog.TryGet({decoderEntry}.SchemaName, out var {schemaEntry}))");
                    sb.AppendLine($"{pad}                {{");
                    sb.AppendLine(
                        $"{pad}                    reason = \"The generated Bridge schema catalog has no entry for the declared Foxglove type.\";");
                    sb.AppendLine($"{pad}                    return false;");
                    sb.AppendLine($"{pad}                }}");
                }
                sb.AppendLine(
                    $"{pad}                binding = new global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeGeneratedSubscribeBinding(");
                sb.AppendLine(
                    $"{pad}                    {binding.BindingIndex},");
                sb.AppendLine(
                    $"{pad}                    {binding.TopicIndex},");
                sb.AppendLine(
                    $"{pad}                    {binding.PublishTopicIndex},");
                sb.AppendLine(
                    $"{pad}                    \"{CSharpStringLiteral(stableId)}\",");
                sb.AppendLine(
                    $"{pad}                    \"{CSharpStringLiteral(member.Topic)}\",");
                sb.AppendLine(binding.IsStandard
                    ? $"{pad}                    {decoderEntry}.SchemaName,"
                    : $"{pad}                    \"{CSharpStringLiteral(binding.CanonicalRosType)}\",");
                sb.AppendLine(binding.IsStandard
                    ? $"{pad}                    {schemaEntry}.SourceSha256,"
                    : $"{pad}                    \"{binding.SchemaSha256}\",");
                sb.AppendLine(
                    $"{pad}                    new global::Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy(");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{ReliabilityLiteral(member.Reliability)},");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{DurabilityLiteral(member.Durability)},");
                sb.AppendLine(
                    $"{pad}                        global::Unity.FoxgloveSDK.Components.{HistoryLiteral(member.History)},");
                sb.AppendLine(
                    $"{pad}                        {member.Depth}),");
                sb.AppendLine(binding.IsStandard
                    ? $"{pad}                    global::Unity2Foxglove.Ros2Bridge.Ros2BridgeFrameWriter.MaxPayloadBytes);"
                    : $"{pad}                    checked((int)global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumBytes));");
                sb.AppendLine($"{pad}                return true;");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine(
                $"{pad}                reason = \"The Bridge physical emitter has no subscribe binding for this binding index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    bool global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource.FoxRunBridge_TryDecodeAndApply(int bindingIndex, global::System.ReadOnlyMemory<byte> payload, string ownershipTransportId, ulong ownershipGeneration, bool markRemoteOwned, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        switch (bindingIndex)");
            sb.AppendLine($"{pad}        {{");
            foreach (var binding in bindings)
            {
                var root = binding.IsStandard
                    ? null
                    : new ShapeRegistry(binding.BindingIndex)
                        .Get(binding.Shape);
                var access = "this."
                             + EscapeIdentifier(
                                 binding.Member.MemberName);
                sb.AppendLine($"{pad}            case {binding.BindingIndex}:");
                sb.AppendLine($"{pad}                try");
                sb.AppendLine($"{pad}                {{");
                if (binding.IsStandard)
                {
                    var decodedType = GlobalTypeName(
                        binding.Member.TypeShape.TypeName);
                    sb.AppendLine(
                        $"{pad}                    if (payload.Length > global::Unity2Foxglove.Ros2Bridge.Ros2BridgeFrameWriter.MaxPayloadBytes)");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.IO.InvalidDataException(\"Bridge CDR payload exceeds the standard ROS message byte budget.\");");
                    sb.AppendLine(
                        $"{pad}                    if (!global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrDeserializerRegistry.TryGetByClrType(typeof({decodedType}), out var __decoder))");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.IO.InvalidDataException(\"The generated Bridge CDR decoder registry has no entry for the declared Foxglove type.\");");
                    sb.AppendLine(
                        $"{pad}                    var __decoded = ({decodedType})__decoder.Deserialize(payload.ToArray());");
                }
                else
                {
                    sb.AppendLine(
                        $"{pad}                    if (payload.Length > global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumBytes)");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.IO.InvalidDataException(\"Bridge CDR payload exceeds the custom DTO byte budget.\");");
                    sb.AppendLine(
                        $"{pad}                    var __reader = new global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrReader(payload.ToArray());");
                    sb.AppendLine(
                        $"{pad}                    var __wireOrigin = __reader.ReadString();");
                    sb.AppendLine(
                        $"{pad}                    var __wireSequence = __reader.ReadUInt64();");
                    sb.AppendLine(
                        $"{pad}                    var __wireSeconds = __reader.ReadInt32();");
                    sb.AppendLine(
                        $"{pad}                    var __wireNanoseconds = __reader.ReadUInt32();");
                    sb.AppendLine(
                        $"{pad}                    if (__wireSequence == 0)");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.IO.InvalidDataException(\"Bridge CDR envelope sequence must be non-zero.\");");
                    sb.AppendLine(
                        $"{pad}                    if (__wireNanoseconds >= 1000000000U)");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.IO.InvalidDataException(\"Bridge CDR envelope nanoseconds must be below one second.\");");
                    sb.AppendLine(
                        $"{pad}                    var __decoded = {root.ReadMethod}(__reader);");
                    sb.AppendLine(
                        $"{pad}                    __reader.EnsureFullyConsumed();");
                }
                sb.AppendLine(
                    $"{pad}                    {access} = __decoded;");
                if (binding.PublishTopicIndex >= 0)
                {
                    sb.AppendLine(
                        $"{pad}                    if (markRemoteOwned)");
                    sb.AppendLine(
                        $"{pad}                        ((global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource)this).FoxRunOrigin_MarkRemoteApplied({binding.PublishTopicIndex}, ownershipTransportId, ownershipGeneration);");
                }
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine(
                    $"{pad}                catch (global::System.Exception exception)");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine(
                    $"{pad}                    reason = exception.Message ?? \"Bridge CDR input failed.\";");
                sb.AppendLine(
                    $"{pad}                    if (reason.Length > 512) reason = reason.Substring(0, 512);");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine(
                $"{pad}                reason = \"The Bridge physical emitter has no subscribe binding for this binding index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    void global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource.FoxRunBridge_ReleaseRemoteOwnership(int topicIndex, string ownershipTransportId, ulong ownershipGeneration)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine(
                $"{pad}        var ownershipSource = ((object)this) as global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource;");
            sb.AppendLine(
                $"{pad}        if (ownershipSource != null)");
            sb.AppendLine(
                $"{pad}            ownershipSource.FoxRunOrigin_ClearRemoteApplied(topicIndex, ownershipTransportId, ownershipGeneration);");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitReaders(
            StringBuilder sb,
            IReadOnlyList<SubscribeBindingShape> bindings,
            string pad)
        {
            foreach (var binding in bindings)
            {
                if (binding.IsStandard)
                    continue;

                var registry = new ShapeRegistry(binding.BindingIndex);
                registry.Get(binding.Shape);
                for (var shapeIndex = 0;
                     shapeIndex < registry.Count;
                     shapeIndex++)
                {
                    EmitShapeReader(
                        sb,
                        pad,
                        registry[shapeIndex],
                        registry);
                }
            }
        }

        private static void EmitShapeReader(
            StringBuilder sb,
            string pad,
            ShapeEntry entry,
            ShapeRegistry registry)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)} {entry.ReadMethod}(");
            sb.AppendLine(
                $"{pad}        global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrReader reader)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine(
                $"{pad}        var source = new {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)}();");
            var ordinal = 0;
            foreach (var member in entry.Shape.Members
                         .OrderBy(
                             value => value.RosFieldName,
                             StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                EmitMemberReader(
                    sb,
                    pad + "        ",
                    member,
                    registry,
                    ordinal++);
            }
            sb.AppendLine($"{pad}        return source;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitMemberReader(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal)
        {
            var value = "__value_" + ordinal;
            var access = "source." + EscapeIdentifier(member.Name);
            switch (member.Kind)
            {
                case BridgeDtoMemberKind.NestedDto:
                    sb.AppendLine(
                        $"{pad}var {value} = {registry.Get(member.NestedShape).ReadMethod}(reader);");
                    break;
                case BridgeDtoMemberKind.Sequence:
                    EmitSequenceReader(
                        sb,
                        pad,
                        member,
                        registry,
                        ordinal,
                        value);
                    break;
                case BridgeDtoMemberKind.String:
                    sb.AppendLine(
                        $"{pad}var {value} = reader.ReadString();");
                    break;
                case BridgeDtoMemberKind.Enum:
                    var enumType = member.FullyQualifiedTypeName;
                    if (TryUnwrapNullable(enumType, out var unwrappedEnum))
                        enumType = unwrappedEnum;
                    sb.AppendLine(
                        $"{pad}var {value} = ({GlobalTypeName(enumType)}){PrimitiveReadExpression(member.RosType)};");
                    break;
                case BridgeDtoMemberKind.Scalar:
                    sb.AppendLine(
                        $"{pad}var {value} = {PrimitiveReadExpression(member.RosType)};");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported custom ROS 2 DTO member kind: "
                        + member.Kind
                        + ".");
            }

            if (!member.HasPresence)
            {
                sb.AppendLine($"{pad}{access} = {value};");
                return;
            }

            var hasValue = "__hasValue_" + ordinal;
            sb.AppendLine($"{pad}var {hasValue} = reader.ReadBool();");
            if (IsNullable(member.FullyQualifiedTypeName))
            {
                sb.AppendLine(
                    $"{pad}{access} = {hasValue} ? ({GlobalTypeName(member.FullyQualifiedTypeName)}){value} : default({GlobalTypeName(member.FullyQualifiedTypeName)});");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}{access} = {hasValue} ? {value} : null;");
            }
        }

        private static void EmitSequenceReader(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal,
            string value)
        {
            var count = "__count_" + ordinal;
            sb.AppendLine($"{pad}var {count} = reader.ReadSequenceLength();");
            sb.AppendLine(
                $"{pad}if ({count} > global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumSequenceItems)");
            sb.AppendLine(
                $"{pad}    throw new global::System.IO.InvalidDataException(\"Bridge CDR sequence exceeds the custom DTO item budget.\");");
            var elementType = GlobalTypeName(
                member.SequenceElementTypeName);
            if (member.SequenceRepresentation
                == BridgeSequenceRepresentation.List)
            {
                sb.AppendLine(
                    $"{pad}var {value} = new global::System.Collections.Generic.List<{elementType}>({count});");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}var {value} = new {elementType}[{count}];");
            }
            sb.AppendLine(
                $"{pad}for (var __index_{ordinal} = 0; __index_{ordinal} < {count}; __index_{ordinal}++)");
            sb.AppendLine($"{pad}{{");
            string item;
            if (member.NestedShape != null)
            {
                item = registry.Get(member.NestedShape).ReadMethod
                       + "(reader)";
            }
            else if (member.SequenceElementIsEnum)
            {
                item = "(" + elementType + ")"
                       + PrimitiveReadExpression(
                           StripArray(member.RosType));
            }
            else if (string.Equals(
                         StripArray(member.RosType),
                         "string",
                         StringComparison.Ordinal))
            {
                item = "reader.ReadString()";
            }
            else
            {
                item = PrimitiveReadExpression(
                    StripArray(member.RosType));
            }
            if (member.SequenceRepresentation
                == BridgeSequenceRepresentation.List)
            {
                sb.AppendLine($"{pad}    {value}.Add({item});");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}    {value}[__index_{ordinal}] = {item};");
            }
            sb.AppendLine($"{pad}}}");
        }

        private static string PrimitiveReadExpression(string rosType)
        {
            switch (StripArray(rosType))
            {
                case "bool": return "reader.ReadBool()";
                case "int8": return "unchecked((global::System.SByte)reader.ReadUInt8())";
                case "uint8": return "reader.ReadUInt8()";
                case "int16": return "reader.ReadInt16()";
                case "uint16": return "reader.ReadUInt16()";
                case "int32": return "reader.ReadInt32()";
                case "uint32": return "reader.ReadUInt32()";
                case "int64": return "reader.ReadInt64()";
                case "uint64": return "reader.ReadUInt64()";
                case "float32": return "reader.ReadFloat32()";
                case "float64": return "reader.ReadFloat64()";
                default:
                    throw new InvalidOperationException(
                        "Unsupported custom ROS 2 CDR primitive: "
                        + rosType
                        + ".");
            }
        }

        private static string BuildStableMemberId(
            string declaringType,
            string memberKind,
            string memberName,
            string topic,
            int flow,
            string jsonFieldName)
            => (declaringType ?? string.Empty)
               + "\n"
               + (memberKind ?? string.Empty)
               + "\n"
               + (memberName ?? string.Empty)
               + "\n"
               + (topic ?? string.Empty)
               + "\n"
               + flow.ToString(CultureInfo.InvariantCulture)
               + "\n"
               + (jsonFieldName ?? string.Empty);

        private static string ReliabilityLiteral(string value)
        {
            if (string.Equals(value, "reliable", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.Reliable";
            if (string.Equals(value, "best-effort", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.BestEffort";
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryReliability.SystemDefault";
            }
            return "FoxRunDeliveryReliability.ProviderDefault";
        }

        private static string DurabilityLiteral(string value)
        {
            if (string.Equals(value, "volatile", StringComparison.Ordinal))
                return "FoxRunDeliveryDurability.Volatile";
            if (string.Equals(
                    value,
                    "transient-local",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryDurability.TransientLocal";
            }
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryDurability.SystemDefault";
            }
            return "FoxRunDeliveryDurability.ProviderDefault";
        }

        private static string HistoryLiteral(string value)
        {
            if (string.Equals(value, "keep-last", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepLast";
            if (string.Equals(value, "keep-all", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepAll";
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryHistory.SystemDefault";
            }
            return "FoxRunDeliveryHistory.ProviderDefault";
        }

        private static void EmitBuilders(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxRunGenerationMember>> topicMap,
            string pad)
        {
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count != 1 || !IsSupportedCustomPublish(fields[0]))
                    continue;

                var member = fields[0];
                var projectedShape = ProjectShape(member.TypeShape);
                var registry = new ShapeRegistry(topicIndex);
                var root = registry.Get(projectedShape);
                var schemaContent = BuildSchemaContent(
                    projectedShape,
                    RosPackageName);
                sb.AppendLine();
                sb.AppendLine($"{pad}    private const string __foxRunRos2Schema_{topicIndex} = \"{CSharpStringLiteral(schemaContent)}\";");
                sb.AppendLine($"{pad}    private bool __TryBuildFoxRunRos2Cdr_{topicIndex}(ulong nowNs, out byte[] payload, out string reason)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        payload = null;");
                sb.AppendLine($"{pad}        reason = string.Empty;");
                sb.AppendLine($"{pad}        var __source = __foxRunCapture_{topicIndex}_0;");
                sb.AppendLine($"{pad}        if ((object)__source == null) {{ reason = \"Custom ROS 2 DTO is null.\"; return false; }}");
                sb.AppendLine($"{pad}        var __seconds = nowNs / 1000000000UL;");
                sb.AppendLine($"{pad}        if (__seconds > int.MaxValue) {{ reason = \"ROS 2 envelope timestamp exceeds builtin_interfaces/Time.\"; return false; }}");
                sb.AppendLine($"{pad}        if (__foxRunCaptureSequence_{topicIndex} == 0) {{ reason = \"ROS 2 envelope sequence was not captured.\"; return false; }}");
                sb.AppendLine($"{pad}        try");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            var __writer = new global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriter(");
                sb.AppendLine($"{pad}                4,");
                sb.AppendLine($"{pad}                checked((int)global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumBytes));");
                sb.AppendLine($"{pad}            __writer.WriteString(__foxRunOrigin);");
                sb.AppendLine($"{pad}            __writer.WriteUInt64(__foxRunCaptureSequence_{topicIndex});");
                sb.AppendLine($"{pad}            __writer.WriteInt32((int)__seconds);");
                sb.AppendLine($"{pad}            __writer.WriteUInt32((uint)(nowNs % 1000000000UL));");
                sb.AppendLine($"{pad}            {root.Method}(__writer, __source);");
                sb.AppendLine($"{pad}            payload = __writer.ToArray();");
                sb.AppendLine($"{pad}            return true;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        catch (global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException exception)");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            reason = exception.Message;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");

                for (var shapeIndex = 0; shapeIndex < registry.Count; shapeIndex++)
                    EmitShapeWriter(sb, pad, registry[shapeIndex], registry);
            }
        }

        private static void EmitShapeWriter(
            StringBuilder sb,
            string pad,
            ShapeEntry entry,
            ShapeRegistry registry)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void {entry.Method}(");
            sb.AppendLine($"{pad}        global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriter writer,");
            sb.AppendLine($"{pad}        {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)} source)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var __hasSource = (object)source != null;");
            var ordinal = 0;
            foreach (var member in entry.Shape.Members
                         .OrderBy(value => value.RosFieldName, StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                EmitMember(sb, pad + "        ", member, registry, ordinal++);
            }
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitMember(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal)
        {
            var access = "source." + EscapeIdentifier(member.Name);
            if (member.HasPresence)
            {
                var captured = "__member_" + ordinal;
                sb.AppendLine(
                    $"{pad}var {captured} = __hasSource ? {access} : default({GlobalTypeName(member.FullyQualifiedTypeName)});");
                access = captured;
            }
            switch (member.Kind)
            {
                case BridgeDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    sb.AppendLine($"{pad}{nested.Method}(writer, __hasSource ? {access} : null);");
                    break;
                case BridgeDtoMemberKind.Sequence:
                    EmitSequence(sb, pad, member, access, registry, ordinal);
                    break;
                case BridgeDtoMemberKind.String:
                    sb.AppendLine($"{pad}writer.WriteString(__hasSource ? {access} : null);");
                    break;
                case BridgeDtoMemberKind.Enum:
                    var enumExpression = TryUnwrapNullable(
                        member.FullyQualifiedTypeName,
                        out var nullableEnumType)
                        ? "__hasSource ? "
                          + access
                          + ".GetValueOrDefault() : default("
                          + GlobalTypeName(nullableEnumType)
                          + ")"
                        : "__hasSource ? "
                          + access
                          + " : default("
                          + GlobalTypeName(member.FullyQualifiedTypeName)
                          + ")";
                    EmitPrimitive(
                        sb,
                        pad,
                        member.RosType,
                        enumExpression);
                    break;
                case BridgeDtoMemberKind.Scalar:
                    var scalar = IsNullable(member.FullyQualifiedTypeName)
                        ? "__hasSource ? " + access + ".GetValueOrDefault() : default"
                        : "__hasSource ? " + access + " : default";
                    EmitPrimitive(sb, pad, member.RosType, scalar);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported custom ROS 2 DTO member kind: " + member.Kind + ".");
            }

            if (member.HasPresence)
            {
                var presence = IsNullable(member.FullyQualifiedTypeName)
                    ? "__hasSource && " + access + ".HasValue"
                    : "__hasSource && (object)" + access + " != null";
                sb.AppendLine($"{pad}writer.WriteBool({presence});");
            }
        }

        private static void EmitSequence(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            string access,
            ShapeRegistry registry,
            int ordinal)
        {
            var variable = "__sequence_" + ordinal;
            sb.AppendLine($"{pad}var {variable} = __hasSource ? {access} : null;");
            var countExpression = member.SequenceRepresentation == BridgeSequenceRepresentation.List
                ? variable + " == null ? 0 : " + variable + ".Count"
                : variable + " == null ? 0 : " + variable + ".Length";
            var count = "__sequenceCount_" + ordinal;
            sb.AppendLine($"{pad}var {count} = {countExpression};");
            sb.AppendLine($"{pad}writer.WriteSequenceLength({count});");
            sb.AppendLine($"{pad}if ({variable} != null)");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    for (var __index = 0; __index < {count}; __index++)");
            sb.AppendLine($"{pad}    {{");
            var item = variable + "[__index]";
            if (member.NestedShape != null)
            {
                sb.AppendLine($"{pad}        {registry.Get(member.NestedShape).Method}(writer, {item});");
            }
            else if (string.Equals(StripArray(member.RosType), "string", StringComparison.Ordinal))
            {
                sb.AppendLine($"{pad}        writer.WriteString({item} ?? string.Empty);");
            }
            else
            {
                EmitPrimitive(sb, pad + "        ", StripArray(member.RosType), item);
            }
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitPrimitive(StringBuilder sb, string pad, string rosType, string expression)
        {
            switch (StripArray(rosType))
            {
                case "bool": sb.AppendLine($"{pad}writer.WriteBool((bool)({expression}));"); return;
                case "int8": sb.AppendLine($"{pad}writer.WriteUInt8(unchecked((byte)(sbyte)({expression})));"); return;
                case "uint8": sb.AppendLine($"{pad}writer.WriteUInt8((byte)({expression}));"); return;
                case "int16": sb.AppendLine($"{pad}writer.WriteInt16((short)({expression}));"); return;
                case "uint16": sb.AppendLine($"{pad}writer.WriteUInt16((ushort)({expression}));"); return;
                case "int32": sb.AppendLine($"{pad}writer.WriteInt32((int)({expression}));"); return;
                case "uint32": sb.AppendLine($"{pad}writer.WriteUInt32((uint)({expression}));"); return;
                case "int64": sb.AppendLine($"{pad}writer.WriteInt64((long)({expression}));"); return;
                case "uint64": sb.AppendLine($"{pad}writer.WriteUInt64((ulong)({expression}));"); return;
                case "float32": sb.AppendLine($"{pad}writer.WriteFloat32((float)({expression}));"); return;
                case "float64": sb.AppendLine($"{pad}writer.WriteFloat64((double)({expression}));"); return;
                default:
                    throw new InvalidOperationException("Unsupported custom ROS 2 CDR primitive: " + rosType + ".");
            }
        }

        private static bool IsSupportedCustomPublish(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 2
               && !IsOfficialFoxgloveMessage(member)
               && (member.PublishTransportIds == null
                   || member.PublishTransportIds.Contains(
                        BridgeProviderId,
                        StringComparer.Ordinal))
               && ProjectShape(member.TypeShape) != null;

        private static bool IsSupportedStandardPublish(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 2
               && IsOfficialFoxgloveMessage(member)
               && (member.PublishTransportIds == null
                   || member.PublishTransportIds.Contains(
                       BridgeProviderId,
                       StringComparer.Ordinal));

        private static bool IsSupportedPublish(
            FoxRunGenerationMember member)
            => IsSupportedCustomPublish(member)
               || IsSupportedStandardPublish(member);

        private static bool IsSupportedCustomSubscribe(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 1
               && !IsOfficialFoxgloveMessage(member)
               && (string.IsNullOrEmpty(member.SubscribeTransportId)
                   || string.Equals(
                       member.SubscribeTransportId,
                       BridgeProviderId,
                       StringComparison.Ordinal))
               && ProjectShape(member.TypeShape) != null;

        private static bool IsSupportedStandardSubscribe(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 1
               && (string.IsNullOrEmpty(member.SubscribeTransportId)
                   || string.Equals(
                       member.SubscribeTransportId,
                       BridgeProviderId,
                       StringComparison.Ordinal))
               && IsOfficialFoxgloveMessage(member);

        private static bool IsOfficialFoxgloveMessage(
            FoxRunGenerationMember member)
        {
            var shape = member?.TypeShape;
            if (shape == null || shape.Kind != FoxRunTypeShapeKind.Object)
                return false;

            var typeName = (shape.TypeName ?? string.Empty).Trim();
            if (typeName.StartsWith("global::", StringComparison.Ordinal))
                typeName = typeName.Substring("global::".Length);
            return typeName.StartsWith("Foxglove.", StringComparison.Ordinal);
        }

        private static string Sha256Hex(string value)
        {
            byte[] digest;
            using (var sha =
                   global::System.Security.Cryptography.SHA256.Create())
            {
                digest = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            var builder = new StringBuilder(digest.Length * 2);
            foreach (var octet in digest)
            {
                builder.Append(
                    octet.ToString(
                        "x2",
                        CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string BuildSchemaContent(
            BridgeDtoShape root,
            string packageName)
        {
            var registry = new ShapeRegistry(-1);
            registry.Get(root);
            var builder = new StringBuilder();
            builder.Append("string foxrun_origin_id\n");
            builder.Append("uint64 foxrun_sequence\n");
            builder.Append("builtin_interfaces/Time foxrun_stamp\n");
            builder.Append(root.PayloadIdentity).Append(" payload\n");
            for (var index = 0; index < registry.Count; index++)
            {
                var shape = registry[index].Shape;
                builder.Append("================================================================================\n");
                builder.Append("MSG: ").Append(packageName).Append('/').Append(shape.PayloadIdentity).Append('\n');
                foreach (var member in shape.Members
                             .OrderBy(value => value.RosFieldName, StringComparer.Ordinal)
                             .ThenBy(value => value.Name, StringComparer.Ordinal))
                {
                    var rosType = member.Kind == BridgeDtoMemberKind.NestedDto
                        ? member.NestedShape.PayloadIdentity
                        : member.RosType;
                    builder.Append(rosType).Append(' ').Append(member.RosFieldName).Append('\n');
                    if (member.HasPresence)
                    {
                        builder.Append("bool ")
                            .Append(member.PresenceFieldName)
                            .Append('\n');
                    }
                }
            }
            builder.Append("================================================================================\n");
            builder.Append("MSG: builtin_interfaces/Time\n");
            builder.Append("int32 sec\nuint32 nanosec\n");
            return builder.ToString();
        }

        private static BridgeDtoShape ProjectShape(FoxRunTypeShape shape)
        {
            if (shape == null
                || shape.Kind != FoxRunTypeShapeKind.Object
                || !shape.CanConstruct)
            {
                return null;
            }

            var members = new List<BridgeDtoMemberShape>();
            var rosNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in shape.Fields
                         .OrderBy(value => value.MemberName, StringComparer.Ordinal))
            {
                if (field == null || !field.CanAssign)
                    return null;

                var rosName = ToRosFieldName(field.MemberName);
                if (string.IsNullOrEmpty(rosName)
                    || rosName.StartsWith("foxrun_", StringComparison.Ordinal)
                    || !rosNames.Add(rosName))
                {
                    return null;
                }

                var member = ProjectMember(field, rosName);
                if (member == null)
                    return null;
                members.Add(member);
            }

            var canonicalIdentity = BuildCanonicalIdentity(
                shape.TypeName,
                members);
            return new BridgeDtoShape(
                shape.TypeName,
                canonicalIdentity,
                BuildPayloadIdentity(
                    shape.TypeName,
                    canonicalIdentity),
                members);
        }

        private static BridgeDtoMemberShape ProjectMember(
            FoxRunTypeField field,
            string rosName)
        {
            var shape = field.TypeShape;
            if (shape == null)
                return null;

            if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                var element = shape.ElementShape;
                if (element == null
                    || element.Kind == FoxRunTypeShapeKind.Collection
                    || element.Nullable)
                {
                    return null;
                }

                var nested = element.Kind == FoxRunTypeShapeKind.Object
                    ? ProjectShape(element)
                    : null;
                var elementRosType = RosType(element, nested);
                var elementTypeName = ClrTypeName(element);
                if (string.IsNullOrEmpty(elementRosType)
                    || string.IsNullOrEmpty(elementTypeName))
                {
                    return null;
                }

                var representation =
                    shape.CollectionKind == FoxRunCollectionKind.List
                        ? BridgeSequenceRepresentation.List
                        : BridgeSequenceRepresentation.Array;
                if (shape.CollectionKind != FoxRunCollectionKind.Array
                    && shape.CollectionKind != FoxRunCollectionKind.List
                    && shape.CollectionKind != FoxRunCollectionKind.Binary)
                {
                    return null;
                }

                var collectionTypeName =
                    representation == BridgeSequenceRepresentation.List
                        ? "System.Collections.Generic.List<"
                          + elementTypeName
                          + ">"
                        : elementTypeName + "[]";
                return new BridgeDtoMemberShape(
                    field.MemberName,
                    rosName,
                    BridgeDtoMemberKind.Sequence,
                    collectionTypeName,
                    elementRosType + "[]",
                    elementTypeName,
                    nested,
                    hasPresence: true,
                    sequenceElementIsEnum:
                        element.Kind == FoxRunTypeShapeKind.Enum,
                    representation);
            }

            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var nested = ProjectShape(shape);
                return nested == null
                    ? null
                    : new BridgeDtoMemberShape(
                        field.MemberName,
                        rosName,
                        BridgeDtoMemberKind.NestedDto,
                        shape.TypeName,
                        nested.PayloadIdentity,
                        string.Empty,
                        nested,
                        hasPresence: true,
                        sequenceElementIsEnum: false,
                        BridgeSequenceRepresentation.None);
            }

            if (shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                var enumRosType = string.IsNullOrEmpty(shape.CanonicalType)
                    ? "int32"
                    : shape.CanonicalType;
                return new BridgeDtoMemberShape(
                    field.MemberName,
                    rosName,
                    BridgeDtoMemberKind.Enum,
                    NullableTypeName(shape.TypeName, shape.Nullable),
                    enumRosType,
                    string.Empty,
                    null,
                    shape.Nullable || field.IsNullable,
                    sequenceElementIsEnum: false,
                    BridgeSequenceRepresentation.None);
            }

            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return null;

            var rosType = RosType(shape, null);
            var clrType = ClrTypeName(shape.WithNullable(false));
            if (string.IsNullOrEmpty(rosType)
                || string.IsNullOrEmpty(clrType))
            {
                return null;
            }

            var isString = string.Equals(
                shape.CanonicalType,
                "string",
                StringComparison.Ordinal);
            return new BridgeDtoMemberShape(
                field.MemberName,
                rosName,
                isString
                    ? BridgeDtoMemberKind.String
                    : BridgeDtoMemberKind.Scalar,
                NullableTypeName(
                    clrType,
                    !isString && (shape.Nullable || field.IsNullable)),
                rosType,
                string.Empty,
                null,
                isString || shape.Nullable || field.IsNullable,
                sequenceElementIsEnum: false,
                BridgeSequenceRepresentation.None);
        }

        private static string RosType(
            FoxRunTypeShape shape,
            BridgeDtoShape nested)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object)
                return nested?.PayloadIdentity ?? string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Enum)
                return string.IsNullOrEmpty(shape.CanonicalType)
                    ? "int32"
                    : shape.CanonicalType;
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            switch (shape.CanonicalType)
            {
                case "bool":
                case "uint8":
                case "int8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "float32":
                case "float64":
                case "string":
                    return shape.CanonicalType;
                default:
                    return string.Empty;
            }
        }

        private static string ClrTypeName(FoxRunTypeShape shape)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object
                || shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                return NullableTypeName(
                    shape.TypeName,
                    shape.Nullable
                    && shape.Kind == FoxRunTypeShapeKind.Enum);
            }
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            string typeName;
            switch (shape.CanonicalType)
            {
                case "bool": typeName = "System.Boolean"; break;
                case "uint8": typeName = "System.Byte"; break;
                case "int8": typeName = "System.SByte"; break;
                case "int16": typeName = "System.Int16"; break;
                case "uint16": typeName = "System.UInt16"; break;
                case "int32": typeName = "System.Int32"; break;
                case "uint32": typeName = "System.UInt32"; break;
                case "int64": typeName = "System.Int64"; break;
                case "uint64": typeName = "System.UInt64"; break;
                case "float32": typeName = "System.Single"; break;
                case "float64": typeName = "System.Double"; break;
                case "string": return "System.String";
                default: return string.Empty;
            }
            return NullableTypeName(typeName, shape.Nullable);
        }

        private static string NullableTypeName(
            string typeName,
            bool nullable)
            => nullable
                ? "System.Nullable<" + typeName + ">"
                : typeName ?? string.Empty;

        private static string BuildCanonicalIdentity(
            string typeName,
            IEnumerable<BridgeDtoMemberShape> members)
        {
            var builder = new StringBuilder();
            AppendLengthFramed(builder, typeName);
            foreach (var member in members)
            {
                AppendLengthFramed(builder, member.Name);
                AppendLengthFramed(builder, member.RosFieldName);
                AppendLengthFramed(builder, member.Kind.ToString());
                AppendLengthFramed(builder, member.FullyQualifiedTypeName);
                AppendLengthFramed(builder, member.RosType);
                AppendLengthFramed(builder, member.SequenceElementTypeName);
                AppendLengthFramed(builder, member.NestedShapeIdentity);
                AppendLengthFramed(
                    builder,
                    member.HasPresence ? "1" : "0");
                AppendLengthFramed(
                    builder,
                    member.SequenceRepresentation.ToString());
            }
            return builder.ToString();
        }

        private static string BuildPayloadIdentity(
            string typeName,
            string canonicalIdentity)
        {
            var simpleName = typeName ?? string.Empty;
            var dot = simpleName.LastIndexOf('.');
            if (dot >= 0)
                simpleName = simpleName.Substring(dot + 1);
            var pascal = ToPascalIdentifier(simpleName);
            if (string.IsNullOrEmpty(pascal))
                pascal = "FoxRunPayload";
            return pascal + Fnv1a64Hex(
                (typeName ?? string.Empty).ToUpperInvariant()
                + "|"
                + (canonicalIdentity ?? string.Empty));
        }

        private static string Fnv1a64Hex(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }
            return hash
                .ToString("X16", CultureInfo.InvariantCulture)
                .Substring(0, 12);
        }

        private static void AppendLengthFramed(
            StringBuilder builder,
            string value)
        {
            value ??= string.Empty;
            builder.Append(
                value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':').Append(value).Append('|');
        }

        private static string ToRosFieldName(string value)
        {
            value ??= string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (char.IsLetterOrDigit(current))
                {
                    var previous = index > 0 ? value[index - 1] : '\0';
                    var next = index + 1 < value.Length
                        ? value[index + 1]
                        : '\0';
                    var needsSeparator = index > 0
                        && char.IsUpper(current)
                        && (char.IsLower(previous)
                            || char.IsDigit(previous)
                            || (char.IsUpper(previous)
                                && char.IsLower(next)));
                    if (needsSeparator
                        && builder.Length > 0
                        && builder[builder.Length - 1] != '_')
                    {
                        builder.Append('_');
                    }
                    builder.Append(char.ToLowerInvariant(current));
                }
                else if (builder.Length > 0
                         && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }
            return builder.ToString().Trim('_');
        }

        private static string ToPascalIdentifier(string value)
        {
            value ??= string.Empty;
            var builder = new StringBuilder(value.Length);
            var capitalize = true;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    capitalize = true;
                    continue;
                }
                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append('N');
                builder.Append(
                    capitalize
                        ? char.ToUpperInvariant(character)
                        : character);
                capitalize = false;
            }
            return builder.ToString();
        }

        private static bool IsNullable(string typeName)
        {
            var value = (typeName ?? string.Empty).Trim();
            return value.EndsWith("?", StringComparison.Ordinal)
                   || value.StartsWith("System.Nullable<", StringComparison.Ordinal)
                   || value.StartsWith("Nullable<", StringComparison.Ordinal);
        }

        private static bool TryUnwrapNullable(string typeName, out string elementType)
        {
            var value = (typeName ?? string.Empty).Trim();
            if (value.EndsWith("?", StringComparison.Ordinal))
            {
                elementType = value.Substring(0, value.Length - 1);
                return elementType.Length > 0;
            }

            const string systemPrefix = "System.Nullable<";
            const string prefix = "Nullable<";
            var matchedPrefix = value.StartsWith(systemPrefix, StringComparison.Ordinal)
                ? systemPrefix
                : value.StartsWith(prefix, StringComparison.Ordinal)
                    ? prefix
                    : null;
            if (matchedPrefix != null && value.EndsWith(">", StringComparison.Ordinal))
            {
                elementType = value.Substring(
                    matchedPrefix.Length,
                    value.Length - matchedPrefix.Length - 1);
                return elementType.Length > 0;
            }

            elementType = string.Empty;
            return false;
        }

        private static string StripArray(string rosType)
        {
            var value = rosType ?? string.Empty;
            var bracket = value.IndexOf('[');
            return bracket < 0 ? value : value.Substring(0, bracket);
        }

        private static string GlobalTypeName(string typeName)
            => string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", StringComparison.Ordinal)
                ? typeName
                : "global::" + typeName;

        private static string EscapeIdentifier(string value)
        {
            switch (value)
            {
                case "abstract":
                case "as":
                case "base":
                case "bool":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "checked":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "goto":
                case "if":
                case "implicit":
                case "in":
                case "int":
                case "interface":
                case "internal":
                case "is":
                case "lock":
                case "long":
                case "namespace":
                case "new":
                case "null":
                case "object":
                case "operator":
                case "out":
                case "override":
                case "params":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "ref":
                case "return":
                case "sbyte":
                case "sealed":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unsafe":
                case "ushort":
                case "using":
                case "virtual":
                case "void":
                case "volatile":
                case "while":
                    return "@" + value;
                default:
                    return value;
            }
        }

        private static string CSharpStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }

        private enum BridgeDtoMemberKind
        {
            Scalar = 0,
            Enum = 1,
            String = 2,
            NestedDto = 3,
            Sequence = 4
        }

        private enum BridgeSequenceRepresentation
        {
            None = 0,
            Array = 1,
            List = 2
        }

        private sealed class BridgeDtoMemberShape
        {
            internal BridgeDtoMemberShape(
                string name,
                string rosFieldName,
                BridgeDtoMemberKind kind,
                string fullyQualifiedTypeName,
                string rosType,
                string sequenceElementTypeName,
                BridgeDtoShape nestedShape,
                bool hasPresence,
                bool sequenceElementIsEnum,
                BridgeSequenceRepresentation sequenceRepresentation)
            {
                Name = name ?? string.Empty;
                RosFieldName = rosFieldName ?? string.Empty;
                PresenceFieldName = hasPresence
                    ? "foxrun_has_" + RosFieldName
                    : string.Empty;
                Kind = kind;
                FullyQualifiedTypeName =
                    fullyQualifiedTypeName ?? string.Empty;
                RosType = rosType ?? string.Empty;
                SequenceElementTypeName =
                    sequenceElementTypeName ?? string.Empty;
                NestedShape = nestedShape;
                NestedShapeIdentity =
                    nestedShape?.CanonicalIdentity ?? string.Empty;
                HasPresence = hasPresence;
                SequenceElementIsEnum = sequenceElementIsEnum;
                SequenceRepresentation = sequenceRepresentation;
            }

            internal string Name { get; }
            internal string RosFieldName { get; }
            internal string PresenceFieldName { get; }
            internal BridgeDtoMemberKind Kind { get; }
            internal string FullyQualifiedTypeName { get; }
            internal string RosType { get; }
            internal string SequenceElementTypeName { get; }
            internal string NestedShapeIdentity { get; }
            internal BridgeDtoShape NestedShape { get; }
            internal bool HasPresence { get; }
            internal bool SequenceElementIsEnum { get; }
            internal BridgeSequenceRepresentation
                SequenceRepresentation { get; }
        }

        private sealed class BridgeDtoShape
        {
            internal BridgeDtoShape(
                string fullyQualifiedTypeName,
                string canonicalIdentity,
                string payloadIdentity,
                IReadOnlyList<BridgeDtoMemberShape> members)
            {
                FullyQualifiedTypeName =
                    fullyQualifiedTypeName ?? string.Empty;
                CanonicalIdentity = canonicalIdentity ?? string.Empty;
                PayloadIdentity = payloadIdentity ?? string.Empty;
                Members = members
                          ?? Array.Empty<BridgeDtoMemberShape>();
            }

            internal string FullyQualifiedTypeName { get; }
            internal string CanonicalIdentity { get; }
            internal string PayloadIdentity { get; }
            internal IReadOnlyList<BridgeDtoMemberShape> Members { get; }
        }

        private sealed class SubscribeBindingShape
        {
            internal SubscribeBindingShape(
                int bindingIndex,
                int topicIndex,
                int publishTopicIndex,
                FoxRunGenerationMember member,
                BridgeDtoShape shape,
                string canonicalRosType,
                string schemaSha256,
                bool isStandard)
            {
                BindingIndex = bindingIndex;
                TopicIndex = topicIndex;
                PublishTopicIndex = publishTopicIndex;
                Member = member
                         ?? throw new ArgumentNullException(nameof(member));
                Shape = shape;
                CanonicalRosType = canonicalRosType ?? string.Empty;
                SchemaSha256 = schemaSha256 ?? string.Empty;
                IsStandard = isStandard;
                if (!IsStandard && Shape == null)
                    throw new ArgumentNullException(nameof(shape));
            }

            internal int BindingIndex { get; }
            internal int TopicIndex { get; }
            internal int PublishTopicIndex { get; }
            internal FoxRunGenerationMember Member { get; }
            internal BridgeDtoShape Shape { get; }
            internal string CanonicalRosType { get; }
            internal string SchemaSha256 { get; }
            internal bool IsStandard { get; }
        }

        private sealed class ShapeRegistry
        {
            private readonly List<ShapeEntry> _entries = new List<ShapeEntry>();
            private readonly int _topicIndex;

            internal ShapeRegistry(int topicIndex)
            {
                _topicIndex = topicIndex;
            }

            internal int Count => _entries.Count;
            internal ShapeEntry this[int index] => _entries[index];

            internal ShapeEntry Get(BridgeDtoShape shape)
            {
                if (shape == null)
                    throw new InvalidOperationException("Custom ROS 2 CDR shape is missing.");
                for (var index = 0; index < _entries.Count; index++)
                {
                    if (string.Equals(
                            _entries[index].Shape.CanonicalIdentity,
                            shape.CanonicalIdentity,
                            StringComparison.Ordinal))
                    {
                        return _entries[index];
                    }
                }

                var entry = new ShapeEntry(
                    shape,
                    "__WriteFoxRunRos2CustomCdr_"
                    + _topicIndex
                    + "_"
                    + _entries.Count,
                    "__ReadFoxRunRos2CustomCdr_"
                    + _topicIndex
                    + "_"
                    + _entries.Count);
                _entries.Add(entry);
                foreach (var member in shape.Members)
                {
                    if (member.NestedShape != null)
                        Get(member.NestedShape);
                }
                return entry;
            }
        }

        private sealed class ShapeEntry
        {
            internal ShapeEntry(
                BridgeDtoShape shape,
                string method,
                string readMethod)
            {
                Shape = shape;
                Method = method;
                ReadMethod = readMethod;
            }

            internal BridgeDtoShape Shape { get; }
            internal string Method { get; }
            internal string ReadMethod { get; }
        }
    }
}
