// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits bounded topic-level transactional MessagePack input.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class MessagePackInputDispatchEmitter
    {
        private sealed class TransactionTopic
        {
            internal TransactionTopic(
                string topic,
                IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
            {
                Topic = topic;
                Members = members;
            }

            internal string Topic { get; }
            internal IReadOnlyList<FoxgloveSourceEmitter.TopicMember> Members { get; }
            internal bool IsStream => Members.Count == 1 && Members[0].IsStream;
        }

        private sealed class ShapeEntry
        {
            internal ShapeEntry(FoxRunTypeShape shape, string identity)
            {
                Shape = shape;
                Identity = identity;
            }

            internal FoxRunTypeShape Shape { get; }
            internal string Identity { get; }
        }

        internal static bool HasTransactionalInput(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
            => BuildTopics(members).Count > 0;

        internal static bool HasTransactionalOwnedInput(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
            => BuildTopics(members).Any(topic => topic.IsStream);

        internal static bool TryGetTransactionIndex(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            FoxgloveSourceEmitter.TopicMember member,
            out int transactionIndex)
        {
            var topics = BuildTopics(members);
            for (var index = 0; index < topics.Count; index++)
            {
                if (!topics[index].IsStream
                    && topics[index].Members.Contains(member))
                {
                    transactionIndex = index;
                    return true;
                }
            }
            transactionIndex = -1;
            return false;
        }

        internal static void EmitInput(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> publishTopics,
            string pad)
        {
            var topics = BuildTopics(members);
            if (topics.Count == 0)
                return;

            var shapes = CollectShapes(topics);
            sb.AppendLine();
            for (var index = 0; index < topics.Count; index++)
                EmitFields(sb, topics[index], index, pad);
            EmitTransactionSurface(sb, topics, pad);
            EmitFlush(sb, topics, publishTopics, pad);
            EmitOwnedSurface(sb, topics, pad);
            for (var index = 0; index < topics.Count; index++)
                EmitTopicDecoder(sb, topics[index], index, pad, shapes);
            for (var index = 0; index < shapes.Count; index++)
                EmitShapeReader(sb, shapes[index].Shape, index, pad, shapes);
        }

        private static List<TransactionTopic> BuildTopics(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
        {
            if (members == null)
                return new List<TransactionTopic>();

            return members
                .Where(member => member != null)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new TransactionTopic(
                    group.Key,
                    group.OrderBy(member => member.MemberName, StringComparer.Ordinal)
                        .ToList()))
                .Where(topic => topic.Members.All(MayUseMessagePack))
                .ToList();
        }

        private static bool MayUseMessagePack(
            FoxgloveSourceEmitter.TopicMember member)
        {
            if (string.Equals(
                    member.Encoding,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    StringComparison.Ordinal))
            {
                return true;
            }
            if (!string.Equals(
                    member.Encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var shape = member.TypeShape;
            if (shape == null)
            {
                var canonical = member.CanonicalType ?? string.Empty;
                return IsSupportedCanonical(canonical);
            }
            return IsSupportedShape(shape);
        }

        private static bool IsSupportedShape(FoxRunTypeShape shape)
        {
            if (shape == null)
                return false;
            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    return IsSupportedCanonical(shape.CanonicalType);
                case FoxRunTypeShapeKind.Enum:
                    return !string.IsNullOrWhiteSpace(shape.TypeName);
                case FoxRunTypeShapeKind.Collection:
                    return shape.ElementShape != null
                           && IsSupportedShape(shape.ElementShape);
                case FoxRunTypeShapeKind.Object:
                    return !string.IsNullOrWhiteSpace(shape.TypeName)
                           && shape.Fields != null
                           && shape.Fields.All(field =>
                               field != null
                               && IsSupportedShape(field.TypeShape));
                default:
                    return false;
            }
        }

        private static bool IsSupportedCanonical(string canonical)
        {
            switch (canonical)
            {
                case "bool":
                case "int8":
                case "uint8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "float32":
                case "float64":
                case "string":
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitFields(
            StringBuilder sb,
            TransactionTopic topic,
            int transactionIndex,
            string pad)
        {
            if (topic.IsStream)
            {
                sb.AppendLine(
                    $"{pad}    private global::Unity.FoxgloveSDK.Components.FoxRunStream<{GlobalTypeName(topic.Members[0].TypeName)}> __foxRunMessagePackStream_{transactionIndex};");
                return;
            }

            sb.AppendLine(
                $"{pad}    private sealed class __FoxRunMessagePackTransaction_{transactionIndex}");
            sb.AppendLine($"{pad}    {{");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                sb.AppendLine(
                    $"{pad}        internal readonly {GlobalTypeName(topic.Members[memberIndex].TypeName)} Value_{memberIndex};");
            }
            sb.Append($"{pad}        internal __FoxRunMessagePackTransaction_{transactionIndex}(");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                if (memberIndex != 0)
                    sb.Append(", ");
                sb.Append(
                    GlobalTypeName(topic.Members[memberIndex].TypeName)
                    + " value_"
                    + memberIndex);
            }
            sb.AppendLine(")");
            sb.AppendLine($"{pad}        {{");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
                sb.AppendLine($"{pad}            Value_{memberIndex} = value_{memberIndex};");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine(
                $"{pad}    private __FoxRunMessagePackTransaction_{transactionIndex} __foxRunMessagePackPending_{transactionIndex};");
            sb.AppendLine(
                $"{pad}    private __FoxRunMessagePackTransaction_{transactionIndex} __foxRunMessagePackApplied_{transactionIndex};");
            sb.AppendLine($"{pad}    private double __foxRunMessagePackLastApplySec_{transactionIndex};");
            sb.AppendLine($"{pad}    private double __foxRunMessagePackNextApplySec_{transactionIndex};");
        }

        private static void EmitTransactionSurface(
            StringBuilder sb,
            IReadOnlyList<TransactionTopic> topics,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    int IFoxgloveTransactionalInputSource.FoxgloveInput_TransactionCount => {topics.Count};");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    FoxgloveInputTopicInfo IFoxgloveTransactionalInputSource.FoxgloveInput_GetTransaction(int transactionIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (transactionIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < topics.Count; index++)
            {
                var member = topics[index].Members[0];
                var mode = member.Mode == 3
                    ? "FoxRunFlow.PublishAndSubscribe"
                    : "FoxRunFlow.Subscribe";
                sb.AppendLine(
                    $"{pad}            case {index}: return new FoxgloveInputTopicInfo(" +
                    $"\"{StringLiteralEmitter.CSharpStringLiteral(topics[index].Topic)}\", " +
                    $"{WireEncodingLiteral(member.Encoding)}, {mode}, " +
                    $"{InputDispatchEmitter.SourceLiteral(member.Source)}, " +
                    $"hasExplicitSource: {BoolLiteral(HasExplicit(member, FoxRunNamedArgumentPresence.Source))}, " +
                    $"hasExplicitEncoding: {BoolLiteral(HasExplicit(member, FoxRunNamedArgumentPresence.Encoding))}, " +
                    $"supportsWebSocket: true, supportsRos2Native: false, " +
                    $"policy: {TopicMetadataEmitter.PolicyLiteral(member.Policy)}, " +
                    $"hz: {TypeExprEmitter.FloatLiteral(member.Hz)}, " +
                    $"hasExplicitHz: {BoolLiteral(member.HasExplicitHz)}, " +
                    $"declaredTargets: {InputDispatchEmitter.TargetsLiteral(member.Targets)}, " +
                    $"hasExplicitTargets: {BoolLiteral(HasExplicit(member, FoxRunNamedArgumentPresence.Targets))}, " +
                    $"hasExplicitQos: {BoolLiteral(InputDispatchEmitter.HasExplicitQos(member))}, " +
                    $"isStream: {BoolLiteral(topics[index].IsStream)});");
            }
            sb.AppendLine(
                $"{pad}            default: throw new ArgumentOutOfRangeException(nameof(transactionIndex));");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    bool IFoxgloveTransactionalInputSource.FoxgloveInput_TryStageTransaction(");
            sb.AppendLine($"{pad}        int transactionIndex,");
            sb.AppendLine($"{pad}        byte[] payload,");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReadLimits limits,");
            sb.AppendLine($"{pad}        out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (transactionIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < topics.Count; index++)
                EmitStageCase(sb, topics[index], index, pad);
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine($"{pad}                error = \"Unknown FoxRun MessagePack transaction index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    void IFoxgloveTransactionalInputSource.FoxgloveInput_ClearTransaction(int transactionIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (transactionIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < topics.Count; index++)
            {
                sb.AppendLine($"{pad}            case {index}:");
                if (!topics[index].IsStream)
                {
                    sb.AppendLine(
                        $"{pad}                global::System.Threading.Interlocked.Exchange(ref __foxRunMessagePackPending_{index}, null);");
                }
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitStageCase(
            StringBuilder sb,
            TransactionTopic topic,
            int transactionIndex,
            string pad)
        {
            sb.AppendLine($"{pad}            case {transactionIndex}:");
            sb.AppendLine($"{pad}            {{");
            if (topic.IsStream)
            {
                var typeName = GlobalTypeName(topic.Members[0].TypeName);
                sb.AppendLine($"{pad}                var __stream = __foxRunMessagePackStream_{transactionIndex};");
                sb.AppendLine($"{pad}                if (__stream == null)");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    error = \"FoxRunStream field is not owned by the active MessagePack registration.\";");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine(
                    $"{pad}                var __ingress = (global::Unity.FoxgloveSDK.Components.IFoxRunStreamInputIngress<{typeName}>)__stream;");
                sb.AppendLine(
                    $"{pad}                if (!__ingress.TryReserveInput(out var __reservation))");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    error = string.Empty;");
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine($"{pad}                var __finalized = false;");
                sb.AppendLine($"{pad}                try");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine(
                    $"{pad}                    if (!__TryDecodeFoxRunMessagePackTopic_{transactionIndex}(payload, limits, out {typeName} __value, out error))");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine(
                    $"{pad}                    __ingress.CommitOwnedInput(__reservation, __value, static _ => {{ }});");
                sb.AppendLine($"{pad}                    __finalized = true;");
                sb.AppendLine($"{pad}                    error = string.Empty;");
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine($"{pad}                finally");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    if (!__finalized)");
                sb.AppendLine($"{pad}                        __ingress.CancelInput(__reservation);");
                sb.AppendLine($"{pad}                }}");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}                if (!__TryDecodeFoxRunMessagePackTopic_{transactionIndex}(payload, limits, out var __transaction, out error))");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine(
                    $"{pad}                global::System.Threading.Interlocked.Exchange(ref __foxRunMessagePackPending_{transactionIndex}, __transaction);");
                sb.AppendLine($"{pad}                error = string.Empty;");
                sb.AppendLine($"{pad}                return true;");
            }
            sb.AppendLine($"{pad}            }}");
        }

        private static void EmitFlush(
            StringBuilder sb,
            IReadOnlyList<TransactionTopic> topics,
            IReadOnlyList<string> publishTopics,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private int __FoxRunFlushMessagePackTransactions(double nowSeconds, int inheritedSubscribeRateHz)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var applied = 0;");
            for (var index = 0; index < topics.Count; index++)
            {
                var topic = topics[index];
                if (topic.IsStream)
                    continue;
                var member = topic.Members[0];
                var rate = member.HasExplicitHz && member.Hz > 0f
                    ? TypeExprEmitter.FloatLiteral(member.Hz)
                    : "(float)global::System.Math.Max(1, inheritedSubscribeRateHz)";
                var hasHeartbeat =
                    member.Policy == 2 && member.HasExplicitHz && member.Hz > 0f;
                var interval = TypeExprEmitter.FloatLiteral(
                    hasHeartbeat ? 1f / member.Hz : 0f);
                var policy = TopicMetadataEmitter.PolicyLiteral(member.Policy);
                sb.AppendLine(
                    $"{pad}        var __pending_{index} = global::System.Threading.Volatile.Read(ref __foxRunMessagePackPending_{index});");
                if (!string.IsNullOrWhiteSpace(member.OnlyIf))
                {
                    sb.AppendLine(
                        $"{pad}        if (__pending_{index} != null && !{ConditionEmitter.ConditionAccess(member.OnlyIf, member.ConditionMemberKind)})");
                    sb.AppendLine($"{pad}        {{");
                    sb.AppendLine(
                        $"{pad}            global::System.Threading.Interlocked.CompareExchange(ref __foxRunMessagePackPending_{index}, null, __pending_{index});");
                    sb.AppendLine($"{pad}            __foxRunMessagePackApplied_{index} = null;");
                    sb.AppendLine($"{pad}        }}");
                    sb.AppendLine($"{pad}        else if (__pending_{index} != null)");
                }
                else
                {
                    sb.AppendLine($"{pad}        if (__pending_{index} != null)");
                }
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            var __rate_{index} = {rate};");
                sb.AppendLine(
                    $"{pad}            if ({policy} != FoxRunPolicy.Trigger && nowSeconds >= __foxRunMessagePackNextApplySec_{index})");
                sb.AppendLine($"{pad}            {{");
                sb.AppendLine(
                    $"{pad}                var __changed_{index} = __foxRunMessagePackApplied_{index} == null;");
                for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
                {
                    var field = topic.Members[memberIndex];
                    var changed = LocalChangeExpr(
                        "__pending_" + index + ".Value_" + memberIndex,
                        field.TypeName,
                        "__foxRunMessagePackApplied_" + index + ".Value_" + memberIndex,
                        field.Tolerance);
                    sb.AppendLine(
                        $"{pad}                if (!__changed_{index}) __changed_{index} = {changed};");
                }
                sb.AppendLine(
                    $"{pad}                if (Unity.FoxgloveSDK.Util.FoxRunUpdatePolicy.ShouldApply(");
                sb.AppendLine($"{pad}                        {policy}, true,");
                sb.AppendLine(
                    $"{pad}                        __foxRunMessagePackApplied_{index} != null,");
                sb.AppendLine($"{pad}                        __changed_{index}, nowSeconds,");
                sb.AppendLine(
                    $"{pad}                        __foxRunMessagePackLastApplySec_{index}, {interval}))");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine(
                    $"{pad}                    if (__FoxRunApplyMessagePackTransaction_{index}(nowSeconds, __rate_{index})) applied++;");
                sb.AppendLine($"{pad}                }}");
                if (member.Policy == 2 && !hasHeartbeat)
                {
                    sb.AppendLine($"{pad}                else if (!__changed_{index})");
                    sb.AppendLine(
                        $"{pad}                    global::System.Threading.Interlocked.CompareExchange(ref __foxRunMessagePackPending_{index}, null, __pending_{index});");
                }
                sb.AppendLine($"{pad}            }}");
                sb.AppendLine($"{pad}        }}");
            }
            sb.AppendLine($"{pad}        return applied;");
            sb.AppendLine($"{pad}    }}");

            for (var index = 0; index < topics.Count; index++)
            {
                var topic = topics[index];
                if (topic.IsStream)
                    continue;
                sb.AppendLine();
                sb.AppendLine(
                    $"{pad}    private bool __FoxRunApplyMessagePackTransaction_{index}(double nowSeconds, float applyRateHz)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine(
                    $"{pad}        var __transaction = global::System.Threading.Interlocked.Exchange(ref __foxRunMessagePackPending_{index}, null);");
                sb.AppendLine($"{pad}        if (__transaction == null) return false;");
                for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
                {
                    sb.AppendLine(
                        $"{pad}        {TypeExprEmitter.MemberAccess(topic.Members[memberIndex].MemberName)} = __transaction.Value_{memberIndex};");
                }
                sb.AppendLine($"{pad}        __foxRunMessagePackApplied_{index} = __transaction;");
                sb.AppendLine($"{pad}        __foxRunMessagePackLastApplySec_{index} = nowSeconds;");
                sb.AppendLine(
                    $"{pad}        __foxRunMessagePackNextApplySec_{index} = applyRateHz > 0f ? nowSeconds + 1d / applyRateHz : nowSeconds;");
                if (topic.Members.Any(candidate => candidate.Mode == 3))
                {
                    var publishIndex = IndexOf(publishTopics, topic.Topic);
                    if (publishIndex >= 0)
                        sb.AppendLine($"{pad}        __FoxRunMarkRemoteApplied_{publishIndex}();");
                }
                sb.AppendLine($"{pad}        return true;");
                sb.AppendLine($"{pad}    }}");
            }
        }

        private static void EmitOwnedSurface(
            StringBuilder sb,
            IReadOnlyList<TransactionTopic> topics,
            string pad)
        {
            if (!topics.Any(topic => topic.IsStream))
                return;

            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    bool IFoxgloveTransactionalOwnedInputSource.FoxgloveInput_TryAcquireTransactionalOwned(int transactionIndex, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (transactionIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < topics.Count; index++)
            {
                if (!topics[index].IsStream)
                    continue;
                var member = topics[index].Members[0];
                sb.AppendLine($"{pad}            case {index}:");
                sb.AppendLine($"{pad}            {{");
                sb.AppendLine(
                    $"{pad}                var __stream = {TypeExprEmitter.MemberAccess(member.MemberName)};");
                sb.AppendLine($"{pad}                if (__stream == null)");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine(
                    $"{pad}                    error = \"FoxRunStream field '{StringLiteralEmitter.CSharpStringLiteral(member.MemberName)}' must be initialized before registration.\";");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine(
                    $"{pad}                if (global::System.Threading.Interlocked.CompareExchange(ref __foxRunMessagePackStream_{index}, __stream, null) != null)");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine(
                    $"{pad}                    error = \"FoxRunStream field is already owned by a MessagePack input provider.\";");
                sb.AppendLine($"{pad}                    return false;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine($"{pad}                error = string.Empty;");
                sb.AppendLine($"{pad}                return true;");
                sb.AppendLine($"{pad}            }}");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine(
                $"{pad}                error = \"Transaction index does not identify a MessagePack FoxRunStream field.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    void IFoxgloveTransactionalOwnedInputSource.FoxgloveInput_ClearTransactionalOwned(int transactionIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (transactionIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < topics.Count; index++)
            {
                if (!topics[index].IsStream)
                    continue;
                sb.AppendLine($"{pad}            case {index}:");
                sb.AppendLine(
                    $"{pad}                global::System.Threading.Interlocked.Exchange(ref __foxRunMessagePackStream_{index}, null)?.Clear();");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitTopicDecoder(
            StringBuilder sb,
            TransactionTopic topic,
            int transactionIndex,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var resultType = topic.IsStream
                ? GlobalTypeName(topic.Members[0].TypeName)
                : "__FoxRunMessagePackTransaction_" + transactionIndex;
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static bool __TryDecodeFoxRunMessagePackTopic_{transactionIndex}(");
            sb.AppendLine($"{pad}        byte[] payload,");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReadLimits limits,");
            sb.AppendLine($"{pad}        out {resultType} value,");
            sb.AppendLine($"{pad}        out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        value = default;");
            sb.AppendLine($"{pad}        error = string.Empty;");
            sb.AppendLine(
                $"{pad}        var __reader = new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReader(payload, limits);");

            sb.AppendLine(
                $"{pad}        if (!__reader.TryReadMapHeader(out var __count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = __reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                sb.AppendLine(
                    $"{pad}        var __seen_{memberIndex} = false;");
                sb.AppendLine(
                    $"{pad}        {GlobalTypeName(topic.Members[memberIndex].TypeName)} __value_{memberIndex} = default;");
            }
            sb.AppendLine($"{pad}        for (var __index = 0; __index < __count; __index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (!__reader.TryReadString(out var __key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                error = __reader.Error;");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            switch (__key)");
            sb.AppendLine($"{pad}            {{");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                var member = topic.Members[memberIndex];
                var shape = member.TypeShape
                            ?? FoxRunTypeShape.Canonical(member.CanonicalType);
                var shapeIndex = FindShape(shape, shapes);
                sb.AppendLine(
                    $"{pad}                case \"{StringLiteralEmitter.CSharpStringLiteral(member.JsonFieldName)}\":");
                sb.AppendLine($"{pad}                    if (__seen_{memberIndex})");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine(
                    $"{pad}                        error = \"MessagePack object contains a duplicate known key.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
                sb.AppendLine($"{pad}                    __seen_{memberIndex} = true;");
                sb.AppendLine(
                    $"{pad}                    if (!__TryReadFoxRunMessagePackValue_{shapeIndex}(__reader, out var __decoded_{memberIndex}, out error)) return false;");
                sb.AppendLine(
                    $"{pad}                    __value_{memberIndex} = __decoded_{memberIndex};");
                sb.AppendLine($"{pad}                    break;");
            }
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (!__reader.TrySkipValue())");
            sb.AppendLine($"{pad}                    {{");
            sb.AppendLine($"{pad}                        error = __reader.Error;");
            sb.AppendLine($"{pad}                        return false;");
            sb.AppendLine($"{pad}                    }}");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                sb.AppendLine($"{pad}        if (!__seen_{memberIndex})");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine(
                    $"{pad}            error = \"MessagePack object is missing a required known field.\";");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
            }

            sb.AppendLine($"{pad}        if (!__reader.TryComplete())");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = __reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            if (topic.IsStream)
            {
                sb.AppendLine($"{pad}        value = __value_0;");
            }
            else
            {
                sb.Append($"{pad}        value = new {resultType}(");
                for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
                {
                    if (memberIndex != 0)
                        sb.Append(", ");
                    sb.Append("__value_" + memberIndex);
                }
                sb.AppendLine(");");
            }
            sb.AppendLine($"{pad}        error = string.Empty;");
            sb.AppendLine($"{pad}        return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitShapeReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            int shapeIndex,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var typeName = ClrType(shape);
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static bool __TryReadFoxRunMessagePackValue_{shapeIndex}(");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReader reader,");
            sb.AppendLine($"{pad}        out {typeName} value,");
            sb.AppendLine($"{pad}        out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        value = default;");
            sb.AppendLine($"{pad}        error = string.Empty;");
            if (CanBeNil(shape))
            {
                sb.AppendLine($"{pad}        if (!reader.TryReadNil(out var isNil))");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            error = reader.Error;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        if (isNil) return true;");
            }

            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    EmitCanonicalReader(sb, shape, pad);
                    break;
                case FoxRunTypeShapeKind.Enum:
                    sb.AppendLine($"{pad}        if (!reader.TryReadInt32(out var decoded))");
                    sb.AppendLine($"{pad}        {{");
                    sb.AppendLine($"{pad}            error = reader.Error;");
                    sb.AppendLine($"{pad}            return false;");
                    sb.AppendLine($"{pad}        }}");
                    sb.AppendLine(
                        $"{pad}        value = ({GlobalTypeName(shape.TypeName)})decoded;");
                    sb.AppendLine($"{pad}        return true;");
                    break;
                case FoxRunTypeShapeKind.Collection:
                    EmitCollectionReader(sb, shape, pad, shapes);
                    break;
                case FoxRunTypeShapeKind.Object:
                    EmitObjectReader(sb, shape, pad, shapes);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported MessagePack input shape.");
            }
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitCanonicalReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad)
        {
            string method;
            switch (shape.CanonicalType)
            {
                case "bool": method = "TryReadBoolean"; break;
                case "int8": method = "TryReadSByte"; break;
                case "uint8": method = "TryReadByte"; break;
                case "int16": method = "TryReadInt16"; break;
                case "uint16": method = "TryReadUInt16"; break;
                case "int32": method = "TryReadInt32"; break;
                case "uint32": method = "TryReadUInt32"; break;
                case "int64": method = "TryReadInt64"; break;
                case "uint64": method = "TryReadUInt64"; break;
                case "float32": method = "TryReadSingle"; break;
                case "float64": method = "TryReadDouble"; break;
                case "string": method = "TryReadString"; break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported canonical MessagePack input type '"
                        + shape.CanonicalType
                        + "'.");
            }
            sb.AppendLine($"{pad}        if (!reader.{method}(out var decoded))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        value = decoded;");
            sb.AppendLine($"{pad}        return true;");
        }

        private static void EmitCollectionReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            if (shape.CollectionKind == FoxRunCollectionKind.Binary)
            {
                sb.AppendLine($"{pad}        if (!reader.TryReadBinary(out value))");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            error = reader.Error;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        return true;");
                return;
            }

            var elementType = ClrType(shape.ElementShape);
            var elementReader = FindShape(shape.ElementShape, shapes);
            sb.AppendLine($"{pad}        if (!reader.TryReadArrayHeader(out var count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            if (shape.CollectionKind == FoxRunCollectionKind.Array)
                sb.AppendLine($"{pad}        var collection = new {elementType}[count];");
            else
                sb.AppendLine(
                    $"{pad}        var collection = new global::System.Collections.Generic.List<{elementType}>(count);");
            sb.AppendLine($"{pad}        for (var index = 0; index < count; index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine(
                $"{pad}            if (!__TryReadFoxRunMessagePackValue_{elementReader}(reader, out {elementType} item, out error)) return false;");
            if (shape.CollectionKind == FoxRunCollectionKind.Array)
                sb.AppendLine($"{pad}            collection[index] = item;");
            else
                sb.AppendLine($"{pad}            collection.Add(item);");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        value = collection;");
            sb.AppendLine($"{pad}        return true;");
        }

        private static void EmitObjectReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            sb.AppendLine($"{pad}        if (!reader.TryReadMapHeader(out var count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                var field = shape.Fields[index];
                sb.AppendLine($"{pad}        var seen_{index} = false;");
                sb.AppendLine(
                    $"{pad}        {ClrType(field.TypeShape)} field_{index} = default;");
            }
            sb.AppendLine($"{pad}        for (var index = 0; index < count; index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (!reader.TryReadString(out var key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                error = reader.Error;");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            switch (key)");
            sb.AppendLine($"{pad}            {{");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                var field = shape.Fields[index];
                var readerIndex = FindShape(field.TypeShape, shapes);
                sb.AppendLine(
                    $"{pad}                case \"{StringLiteralEmitter.CSharpStringLiteral(field.JsonName)}\":");
                sb.AppendLine($"{pad}                    if (seen_{index})");
                sb.AppendLine($"{pad}                    {{");
                sb.AppendLine(
                    $"{pad}                        error = \"MessagePack object contains a duplicate known key.\";");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    }}");
                sb.AppendLine($"{pad}                    seen_{index} = true;");
                sb.AppendLine(
                    $"{pad}                    if (!__TryReadFoxRunMessagePackValue_{readerIndex}(reader, out field_{index}, out error)) return false;");
                sb.AppendLine($"{pad}                    break;");
            }
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (!reader.TrySkipValue())");
            sb.AppendLine($"{pad}                    {{");
            sb.AppendLine($"{pad}                        error = reader.Error;");
            sb.AppendLine($"{pad}                        return false;");
            sb.AppendLine($"{pad}                    }}");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                sb.AppendLine($"{pad}        if (!seen_{index})");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine(
                    $"{pad}            error = \"MessagePack object is missing a required known field.\";");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
            }
            sb.AppendLine($"{pad}        var decoded = new {GlobalTypeName(shape.TypeName)}();");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                sb.AppendLine(
                    $"{pad}        decoded.{IdentifierUtils.EscapeIdentifier(shape.Fields[index].MemberName)} = field_{index};");
            }
            sb.AppendLine($"{pad}        value = decoded;");
            sb.AppendLine($"{pad}        return true;");
        }

        private static List<ShapeEntry> CollectShapes(
            IReadOnlyList<TransactionTopic> topics)
        {
            var result = new List<ShapeEntry>();
            foreach (var topic in topics)
            foreach (var member in topic.Members)
                CollectShape(
                    member.TypeShape
                    ?? FoxRunTypeShape.Canonical(member.CanonicalType),
                    result);
            return result;
        }

        private static void CollectShape(
            FoxRunTypeShape shape,
            ICollection<ShapeEntry> result)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            if (result.Any(entry =>
                    string.Equals(entry.Identity, identity, StringComparison.Ordinal)))
            {
                return;
            }
            result.Add(new ShapeEntry(shape, identity));
            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                foreach (var field in shape.Fields)
                    CollectShape(field.TypeShape, result);
            }
            else if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                CollectShape(shape.ElementShape, result);
            }
        }

        private static int FindShape(
            FoxRunTypeShape shape,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            for (var index = 0; index < shapes.Count; index++)
            {
                if (string.Equals(shapes[index].Identity, identity, StringComparison.Ordinal))
                    return index;
            }
            throw new InvalidOperationException(
                "MessagePack input shape was not collected.");
        }

        private static string ClrType(FoxRunTypeShape shape)
        {
            string type;
            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    type = CanonicalClrType(shape.CanonicalType);
                    break;
                case FoxRunTypeShapeKind.Enum:
                case FoxRunTypeShapeKind.Object:
                    type = GlobalTypeName(shape.TypeName);
                    break;
                case FoxRunTypeShapeKind.Collection:
                    if (shape.CollectionKind == FoxRunCollectionKind.Binary)
                        return "byte[]";
                    type = shape.CollectionKind == FoxRunCollectionKind.Array
                        ? ClrType(shape.ElementShape) + "[]"
                        : "global::System.Collections.Generic.List<"
                          + ClrType(shape.ElementShape)
                          + ">";
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported MessagePack CLR type shape.");
            }
            if (shape.Nullable
                && shape.Kind != FoxRunTypeShapeKind.Collection
                && shape.Kind != FoxRunTypeShapeKind.Object
                && !string.Equals(type, "string", StringComparison.Ordinal))
            {
                return "global::System.Nullable<" + type + ">";
            }
            return type;
        }

        private static string CanonicalClrType(string canonicalType)
        {
            switch (canonicalType)
            {
                case "bool": return "bool";
                case "int8": return "sbyte";
                case "uint8": return "byte";
                case "int16": return "short";
                case "uint16": return "ushort";
                case "int32": return "int";
                case "uint32": return "uint";
                case "int64": return "long";
                case "uint64": return "ulong";
                case "float32": return "float";
                case "float64": return "double";
                case "string": return "string";
                default:
                    throw new InvalidOperationException(
                        "Unsupported canonical MessagePack CLR type '"
                        + canonicalType
                        + "'.");
            }
        }

        private static bool CanBeNil(FoxRunTypeShape shape)
            => shape.Nullable
               || shape.Kind == FoxRunTypeShapeKind.Collection
               || shape.Kind == FoxRunTypeShapeKind.Canonical
               && string.Equals(shape.CanonicalType, "string", StringComparison.Ordinal);

        private static string GlobalTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)
                || typeName.StartsWith("global::", StringComparison.Ordinal))
            {
                return typeName;
            }
            if (typeName.EndsWith("?", StringComparison.Ordinal))
            {
                return GlobalTypeName(
                           typeName.Substring(0, typeName.Length - 1))
                       + "?";
            }
            if (typeName.EndsWith("[]", StringComparison.Ordinal))
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
                case "string":
                    return typeName;
            }
            return "global::" + typeName;
        }

        private static string WireEncodingLiteral(string encoding)
            => string.Equals(
                encoding,
                FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                StringComparison.Ordinal)
                ? "FoxRunEncoding.MessagePack"
                : "(FoxRunEncoding)0";

        private static string LocalChangeExpr(
            string current,
            string type,
            string previous,
            float tolerance)
        {
            var normalized = type.StartsWith("UnityEngine.", StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
            var epsilon = TypeExprEmitter.FloatLiteral(Math.Max(0f, tolerance));
            switch (normalized)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}, {previous}, {epsilon})";
                case "double":
                case "Double":
                case "System.Double":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.DoubleChanged({current}, {previous}, {epsilon})";
                case "Vector2":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon})";
                case "Vector3":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.z, {previous}.z, {epsilon})";
                case "Quaternion":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.z, {previous}.z, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.w, {previous}.w, {epsilon})";
                case "Color":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.r, {previous}.r, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.g, {previous}.g, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.b, {previous}.b, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.a, {previous}.a, {epsilon})";
                default:
                    return
                        $"!global::System.Collections.Generic.EqualityComparer<{GlobalTypeName(type)}>.Default.Equals({current}, {previous})";
            }
        }

        private static bool HasExplicit(
            FoxgloveSourceEmitter.TopicMember member,
            FoxRunNamedArgumentPresence argument)
            => (member.NamedArgumentPresence & argument) == argument;

        private static string BoolLiteral(bool value) => value ? "true" : "false";

        private static int IndexOf(
            IReadOnlyList<string> values,
            string value)
        {
            if (values == null)
                return -1;
            for (var index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                    return index;
            }
            return -1;
        }
    }
}
