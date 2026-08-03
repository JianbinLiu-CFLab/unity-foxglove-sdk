// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits generated, statically typed FoxRun inbound staging and main-thread application.

using System.Collections.Generic;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class InputDispatchEmitter
    {
        internal static void EmitInput(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> publishTopics,
            string pad,
            bool hasTransactionalInput = false)
        {
            if ((members == null || members.Count == 0)
                && !hasTransactionalInput)
                return;

            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            EmitStagingFields(sb, members, pad);
            sb.AppendLine();
            sb.AppendLine($"{pad}    int IFoxgloveInputSource.FoxgloveInput_TopicCount => {members.Count};");
            sb.AppendLine();
            sb.AppendLine($"{pad}    FoxgloveInputTopicInfo IFoxgloveInputSource.FoxgloveInput_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var topic = StringLiteralEmitter.CSharpStringLiteral(member.Topic);
                var mode = member.Mode == 3 ? "FoxRunFlow.PublishAndSubscribe" : "FoxRunFlow.Subscribe";
                sb.AppendLine(
                    $"{pad}            case {i}: return new FoxgloveInputTopicInfo(" +
                    $"\"{topic}\", {WireEncodingLiteral(member.Encoding)}, {mode}, " +
                    $"publishTransportIds: {TopicMetadataEmitter.TransportIdsLiteral(member.PublishTransportIds)}, " +
                    $"subscribeTransportId: {TopicMetadataEmitter.NullableStringLiteral(member.SubscribeTransportId)}, " +
                    $"hasExplicitEncoding: {BoolLiteral(HasExplicit(member, FoxRunNamedArgumentPresence.Encoding))}, " +
                    $"supportsWebSocket: {BoolLiteral(member.GeneratesWebSocketCodec)}, " +
                    $"deliveryPolicy: new FoxRunDeliveryPolicy(" +
                    $"{TopicMetadataEmitter.ReliabilityLiteral(member.Reliability)}, " +
                    $"{TopicMetadataEmitter.DurabilityLiteral(member.Durability)}, " +
                    $"{TopicMetadataEmitter.HistoryLiteral(member.History)}, " +
                    $"{member.Depth}), " +
                    $"hasExplicitDeliveryPolicy: {BoolLiteral(HasExplicitDeliveryPolicy(member))}, " +
                    $"policy: {TopicMetadataEmitter.PolicyLiteral(member.Policy)}, " +
                    $"hz: {TypeExprEmitter.FloatLiteral(member.Hz)}, " +
                    $"hasExplicitHz: {BoolLiteral(member.HasExplicitHz)}, " +
                    $"isStream: {BoolLiteral(member.IsStream)});");
            }
            sb.AppendLine($"{pad}            default: throw new ArgumentOutOfRangeException(nameof(index));");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxgloveInputSource.FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var i = 0; i < members.Count; i++)
                EmitStagingCase(sb, declaringType, members[i], i, pad);
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine($"{pad}                error = \"Unknown FoxRun inbound topic index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            EmitInputFlush(
                sb,
                members,
                publishTopics,
                pad,
                hasTransactionalInput);
            EmitOwnedInputClear(sb, members, pad);
            ProtobufInputDispatchEmitter.EmitReaders(sb, declaringType, members, pad);
        }

        private static bool HasExplicit(
            FoxgloveSourceEmitter.TopicMember member,
            FoxRunNamedArgumentPresence argument)
            => (member.NamedArgumentPresence & argument) == argument;

        private static void EmitStagingFields(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            string pad)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].IsStream)
                {
                    sb.AppendLine(
                        $"{pad}    private global::Unity.FoxgloveSDK.Components.FoxRunStream<{GlobalTypeName(members[i].TypeName)}> __foxRunInputStream_{i};");
                    continue;
                }
                var typeName = GlobalTypeName(members[i].TypeName);
                sb.AppendLine($"{pad}    private bool __foxRunInputHasPending_{i};");
                sb.AppendLine($"{pad}    private {typeName} __foxRunInputPending_{i};");
                sb.AppendLine($"{pad}    private bool __foxRunInputHasApplied_{i};");
                sb.AppendLine($"{pad}    private {typeName} __foxRunInputApplied_{i};");
                sb.AppendLine($"{pad}    private double __foxRunInputLastApplySec_{i};");
                sb.AppendLine($"{pad}    private double __foxRunInputNextApplySec_{i};");
            }
        }

        private static void EmitStagingCase(
            StringBuilder sb,
            string declaringType,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            string pad)
        {
            var fieldName = StringLiteralEmitter.CSharpStringLiteral(member.JsonFieldName);
            var typeName = GlobalTypeName(member.TypeName);
            var protobuf = UsesProtobuf(member.Encoding);
            var inherited = IsInherited(member.Encoding);
            var protobufFieldNumber = FoxRunProtobufFieldNumber.Resolve(
                FoxRunProtobufContractBuilder.BuildFieldIdentity(
                    declaringType,
                    member.Topic,
                    member.SchemaName,
                    member.MemberName),
                member.ProtobufFieldNumber);
            var protobufReader = protobuf
                ? ProtobufInputDispatchEmitter.ReaderCall(
                    protobufFieldNumber,
                    typeName,
                    member.TypeShape,
                    index)
                : string.Empty;
            var jsonReader = SupportsGeneratedJsonObject(member)
                ? $"FoxRunInboundJson.TryReadObject(payload, \"{fieldName}\", out {typeName} __value, out error)"
                : $"FoxRunInboundJson.TryRead(payload, \"{fieldName}\", out {typeName} __value, out error)";

            sb.AppendLine($"{pad}            case {index}:");
            sb.AppendLine($"{pad}                {{");
            if (member.IsStream)
            {
                sb.AppendLine($"{pad}                    var __stream = __foxRunInputStream_{index};");
                sb.AppendLine($"{pad}                    if (__stream == null)");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        error = \"FoxRunStream field is null.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
                sb.AppendLine($"{pad}                    if (!__stream.TryAdmitInput())");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        error = string.Empty;");
                sb.AppendLine($"{pad}                        return true;");
                sb.AppendLine($"{pad}                    }}");
            }
            if (inherited)
            {
                sb.AppendLine($"{pad}                    if (string.Equals(encoding, \"protobuf\", global::System.StringComparison.OrdinalIgnoreCase))");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        if (!{protobufReader}) return false;");
                EmitStageAssignment(sb, member, index, pad + "                        ");
                sb.AppendLine($"{pad}                    }}");
                if (SupportsJsonInbound(member))
                {
                    sb.AppendLine($"{pad}                    else if (string.Equals(encoding, \"json\", global::System.StringComparison.OrdinalIgnoreCase))");
                    sb.AppendLine($"{pad}                    {{");
                    sb.AppendLine($"{pad}                        if (!{jsonReader}) return false;");
                    EmitStageAssignment(sb, member, index, pad + "                        ");
                    sb.AppendLine($"{pad}                    }}");
                }
                else
                {
                    sb.AppendLine($"{pad}                    else if (string.Equals(encoding, \"json\", global::System.StringComparison.OrdinalIgnoreCase))");
                    sb.AppendLine($"{pad}                    {{");
                    sb.AppendLine($"{pad}                        error = \"This inherited FoxRun input requires Protobuf for its declared type.\";");
                    sb.AppendLine($"{pad}                        return false;");
                    sb.AppendLine($"{pad}                    }}");
                }
                sb.AppendLine($"{pad}                    else");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        error = \"Unsupported FoxRun inbound wire encoding.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
            }
            else
            {
                var reader = protobuf ? protobufReader : jsonReader;
                sb.AppendLine($"{pad}                    if (!{reader})");
                sb.AppendLine($"{pad}                        return false;");
                EmitStageAssignment(sb, member, index, pad + "                    ");
            }
            sb.AppendLine($"{pad}                    error = string.Empty;");
            sb.AppendLine($"{pad}                    return true;");
            sb.AppendLine($"{pad}                }}");
        }

        private static void EmitStageAssignment(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            string pad)
        {
            if (member.IsStream)
            {
                sb.AppendLine($"{pad}__stream.TryEnqueueOwned(__value, static _ => {{ }});");
                return;
            }
            sb.AppendLine($"{pad}__foxRunInputPending_{index} = __value;");
            sb.AppendLine($"{pad}__foxRunInputHasPending_{index} = true;");
        }

        private static void EmitInputFlush(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> publishTopics,
            string pad,
            bool hasTransactionalInput)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    int IFoxgloveInputSource.FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var applied = 0;");
            for (var i = 0; i < members.Count; i++)
            {
                if (!members[i].IsStream)
                    EmitInputFlushMember(sb, members[i], i, pad);
            }
            if (hasTransactionalInput)
            {
                sb.AppendLine(
                    $"{pad}        applied += __FoxRunFlushMessagePackTransactions(nowSeconds, inheritedSubscribeRateHz);");
            }
            sb.AppendLine($"{pad}        return applied;");
            sb.AppendLine($"{pad}    }}");

            for (var i = 0; i < members.Count; i++)
            {
                if (!members[i].IsStream)
                    EmitInputApplyHelper(sb, members[i], publishTopics, i, pad);
            }
        }

        private static void EmitOwnedInputClear(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            string pad)
        {
            var hasStream = false;
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index].IsStream)
                {
                    hasStream = true;
                    break;
                }
            }
            if (!hasStream)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxgloveOwnedInputSource.FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < members.Count; index++)
            {
                if (!members[index].IsStream)
                    continue;
                var access = TypeExprEmitter.MemberAccess(members[index].MemberName);
                sb.AppendLine($"{pad}            case {index}:");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    var __stream = {access};");
                sb.AppendLine($"{pad}                    if (__stream == null)");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        error = \"FoxRunStream field '{StringLiteralEmitter.CSharpStringLiteral(members[index].MemberName)}' must be initialized before registration.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
                sb.AppendLine($"{pad}                    if (global::System.Threading.Interlocked.CompareExchange(ref __foxRunInputStream_{index}, __stream, null) != null)");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine($"{pad}                        error = \"FoxRunStream field is already owned by an input provider.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
                sb.AppendLine($"{pad}                    error = string.Empty;");
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine($"{pad}                error = \"Topic index does not identify a FoxRunStream field.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxgloveOwnedInputSource.FoxgloveInput_ClearOwned(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < members.Count; index++)
            {
                if (!members[index].IsStream)
                    continue;
                sb.AppendLine($"{pad}            case {index}:");
                sb.AppendLine(
                    $"{pad}                global::System.Threading.Interlocked.Exchange(ref __foxRunInputStream_{index}, null)?.Clear();");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitInputFlushMember(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            string pad)
        {
            var rate = member.HasExplicitHz && member.Hz > 0f
                ? TypeExprEmitter.FloatLiteral(member.Hz)
                : "(float)global::System.Math.Max(1, inheritedSubscribeRateHz)";
            var hasHeartbeat = member.Policy == 2 && member.HasExplicitHz && member.Hz > 0f;
            var interval = TypeExprEmitter.FloatLiteral(hasHeartbeat ? 1f / member.Hz : 0f);
            var policy = TopicMetadataEmitter.PolicyLiteral(member.Policy);
            var changed = TypeExprEmitter.ChangeExpr(
                "__foxRunInputPending_" + index,
                member.TypeName,
                "__foxRunInputApplied_" + index,
                member.Tolerance);

            if (!string.IsNullOrWhiteSpace(member.OnlyIf))
            {
                sb.AppendLine($"{pad}        if (__foxRunInputHasPending_{index} && !{ConditionEmitter.ConditionAccess(member.OnlyIf, member.ConditionMemberKind)})");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            __foxRunInputHasPending_{index} = false;");
                sb.AppendLine($"{pad}            __foxRunInputHasApplied_{index} = false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        else if (__foxRunInputHasPending_{index})");
            }
            else
            {
                sb.AppendLine($"{pad}        if (__foxRunInputHasPending_{index})");
            }
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            var __foxRunInputRateHz_{index} = {rate};");
            sb.AppendLine($"{pad}            if ({policy} != FoxRunPolicy.Trigger && nowSeconds >= __foxRunInputNextApplySec_{index})");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                var __foxRunInputChanged_{index} = !__foxRunInputHasApplied_{index};");
            sb.AppendLine($"{pad}                if (!__foxRunInputChanged_{index})");
            sb.AppendLine($"{pad}                    __foxRunInputChanged_{index} = {changed};");
            sb.AppendLine($"{pad}                if (Unity.FoxgloveSDK.Util.FoxRunUpdatePolicy.ShouldApply(");
            sb.AppendLine($"{pad}                        {policy},");
            sb.AppendLine($"{pad}                        __foxRunInputHasPending_{index},");
            sb.AppendLine($"{pad}                        __foxRunInputHasApplied_{index},");
            sb.AppendLine($"{pad}                        __foxRunInputChanged_{index},");
            sb.AppendLine($"{pad}                        nowSeconds,");
            sb.AppendLine($"{pad}                        __foxRunInputLastApplySec_{index},");
            sb.AppendLine($"{pad}                        {interval}))");
            sb.AppendLine($"{pad}                {{");
            sb.AppendLine($"{pad}                    if (__FoxRunApplyInput_{index}(nowSeconds, __foxRunInputRateHz_{index}))");
            sb.AppendLine($"{pad}                        applied++;");
            sb.AppendLine($"{pad}                }}");
            if (member.Policy == 2 && !hasHeartbeat)
            {
                sb.AppendLine($"{pad}                else if (!__foxRunInputChanged_{index})");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    __foxRunInputHasPending_{index} = false;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
        }

        private static void EmitInputApplyHelper(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember member,
            IReadOnlyList<string> publishTopics,
            int index,
            string pad)
        {
            var access = TypeExprEmitter.MemberAccess(member.MemberName);
            sb.AppendLine();
            sb.AppendLine($"{pad}    private bool __FoxRunApplyInput_{index}(double nowSeconds, float applyRateHz)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (!__foxRunInputHasPending_{index}) return false;");
            sb.AppendLine($"{pad}        {access} = __foxRunInputPending_{index};");
            sb.AppendLine($"{pad}        __foxRunInputApplied_{index} = __foxRunInputPending_{index};");
            sb.AppendLine($"{pad}        __foxRunInputHasApplied_{index} = true;");
            sb.AppendLine($"{pad}        __foxRunInputHasPending_{index} = false;");
            sb.AppendLine($"{pad}        __foxRunInputLastApplySec_{index} = nowSeconds;");
            sb.AppendLine($"{pad}        __foxRunInputNextApplySec_{index} = applyRateHz > 0f");
            sb.AppendLine($"{pad}            ? nowSeconds + 1d / applyRateHz");
            sb.AppendLine($"{pad}            : nowSeconds;");
            if (member.Mode == 3)
            {
                var publishIndex = IndexOf(publishTopics, member.Topic);
                if (publishIndex >= 0)
                    sb.AppendLine($"{pad}        __FoxRunMarkRemoteApplied_{publishIndex}();");
            }
            sb.AppendLine($"{pad}        return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static string GlobalTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", System.StringComparison.Ordinal))
                return typeName;
            if (typeName.EndsWith("[]", System.StringComparison.Ordinal))
                return GlobalTypeName(typeName.Substring(0, typeName.Length - 2)) + "[]";
            switch (typeName)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "char":
                case "object":
                    return typeName;
            }
            return "global::" + typeName;
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            if (values == null)
                return -1;
            for (var i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, System.StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static bool UsesProtobuf(string encoding)
            => string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal)
               || IsInherited(encoding);

        private static bool IsInherited(string encoding)
            => string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, System.StringComparison.Ordinal);

        private static string WireEncodingLiteral(string encoding)
        {
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal))
                return "FoxRunEncoding.Protobuf";
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.JsonEncoding, System.StringComparison.Ordinal))
                return "FoxRunEncoding.JSON";
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.MessagePackEncoding, System.StringComparison.Ordinal))
                return "FoxRunEncoding.MessagePack";
            return "(FoxRunEncoding)0";
        }

        internal static bool HasExplicitDeliveryPolicy(
            FoxgloveSourceEmitter.TopicMember member)
        {
            const FoxRunNamedArgumentPresence arguments =
                FoxRunNamedArgumentPresence.Reliability
                | FoxRunNamedArgumentPresence.Durability
                | FoxRunNamedArgumentPresence.History
                | FoxRunNamedArgumentPresence.Depth;
            return (member.NamedArgumentPresence & arguments) != 0;
        }

        internal static string BoolLiteral(bool value) => value ? "true" : "false";

        private static bool SupportsJsonInbound(FoxgloveSourceEmitter.TopicMember member)
        {
            if (SupportsGeneratedJsonObject(member))
                return true;

            var type = member.TypeName ?? string.Empty;
            return !type.EndsWith("[]", System.StringComparison.Ordinal)
                   && type.IndexOf("List<", System.StringComparison.Ordinal) < 0
                   && type.IndexOf("IList<", System.StringComparison.Ordinal) < 0
                   && type.IndexOf("IReadOnlyList<", System.StringComparison.Ordinal) < 0;
        }

        private static bool SupportsGeneratedJsonObject(
            FoxgloveSourceEmitter.TopicMember member)
        {
            var shape = member?.TypeShape;
            if (shape?.Kind == FoxRunTypeShapeKind.Collection)
                shape = shape.ElementShape;
            return shape != null
                   && (shape.Kind == FoxRunTypeShapeKind.Object
                       || shape.Kind == FoxRunTypeShapeKind.Enum)
                   && !UsesBuiltInJsonCodec(shape);
        }

        private static bool UsesBuiltInJsonCodec(FoxRunTypeShape shape)
        {
            var typeName = shape?.TypeName ?? string.Empty;
            return string.Equals(typeName, "UnityEngine.Vector2", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Vector3", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Quaternion", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Color", System.StringComparison.Ordinal);
        }
    }
}
