// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun/Decomposed/Ros2CustomCdrEmitter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    internal static partial class FoxRunBridgeSourceEmitter
    {
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
    }
}
