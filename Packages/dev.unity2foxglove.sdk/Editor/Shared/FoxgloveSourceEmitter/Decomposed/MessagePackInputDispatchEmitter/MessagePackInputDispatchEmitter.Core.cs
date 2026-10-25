// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Phase192 partial source section for MessagePackInputDispatchEmitter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class MessagePackInputDispatchEmitter
    {
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
                    $"publishTransportIds: {TopicMetadataEmitter.TransportIdsLiteral(member.PublishTransportIds)}, " +
                    $"subscribeTransportId: {TopicMetadataEmitter.NullableStringLiteral(member.SubscribeTransportId)}, " +
                    $"hasExplicitEncoding: {BoolLiteral(HasExplicit(member, FoxRunNamedArgumentPresence.Encoding))}, " +
                    $"supportsWebSocket: true, " +
                    $"deliveryPolicy: new FoxRunDeliveryPolicy(" +
                    $"{TopicMetadataEmitter.ReliabilityLiteral(member.Reliability)}, " +
                    $"{TopicMetadataEmitter.DurabilityLiteral(member.Durability)}, " +
                    $"{TopicMetadataEmitter.HistoryLiteral(member.History)}, " +
                    $"{member.Depth}), " +
                    $"hasExplicitDeliveryPolicy: {BoolLiteral(InputDispatchEmitter.HasExplicitDeliveryPolicy(member))}, " +
                    $"policy: {TopicMetadataEmitter.PolicyLiteral(member.Policy)}, " +
                    $"hz: {TypeExprEmitter.FloatLiteral(member.Hz)}, " +
                    $"hasExplicitHz: {BoolLiteral(member.HasExplicitHz)}, " +
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

    }
}
