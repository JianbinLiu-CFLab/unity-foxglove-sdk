// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Emits the <c>FoxgloveLog_Publish</c> dispatch method that builds a
    /// JSON payload dictionary from member values and calls
    /// <c>FoxgloveManager.PublishJson</c> for each topic index.
    /// </summary>
    /// <remarks>Phase192 source sections: PublishDispatchEmitter.Dispatch.cs, PublishDispatchEmitter.Json.cs, PublishDispatchEmitter.JsonValues.cs</remarks>
    internal static partial class PublishDispatchEmitter
    {
        private const int ChangePolicy = 2;

        internal static void EmitCaptureAndTargets(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            sb.AppendLine($"{pad}    private readonly string __foxRunOrigin = \"unity2foxglove-\" + global::System.Guid.NewGuid().ToString(\"N\");");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                sb.AppendLine($"{pad}    private bool __foxRunCaptureActive_{topicIndex};");
                if (MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                {
                    sb.AppendLine(
                        $"{pad}    private bool __foxRunRecordMessagePack_{topicIndex};");
                }
                if (TopicMetadataEmitter.IsInherited(fields))
                    sb.AppendLine($"{pad}    private FoxRunEncoding __foxRunCaptureEncoding_{topicIndex};");
                if (fields.Count == 1 && NeedsCaptureSequence(fields[0]))
                {
                    sb.AppendLine($"{pad}    private ulong __foxRunSequence_{topicIndex};");
                    sb.AppendLine($"{pad}    private ulong __foxRunCaptureSequence_{topicIndex};");
                }
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}    private {CaptureTypeName(fields[fieldIndex].TypeName)} __foxRunCapture_{topicIndex}_{fieldIndex};");

                if (!fields.Any(field => field.Mode == 3))
                    continue;

                sb.AppendLine($"{pad}    private bool __foxRunRemoteOwned_{topicIndex};");
                sb.AppendLine($"{pad}    private string __foxRunRemoteTransport_{topicIndex};");
                sb.AppendLine($"{pad}    private ulong __foxRunRemoteGeneration_{topicIndex};");
                if (NeedsStructuralOriginSnapshot(fields))
                {
                    sb.AppendLine($"{pad}    private byte[] __foxRunRemoteValue_{topicIndex}_0;");
                }
                else
                {
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                        sb.AppendLine($"{pad}    private {CaptureTypeName(fields[fieldIndex].TypeName)} __foxRunRemoteValue_{topicIndex}_{fieldIndex};");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine($"{pad}                if (__foxRunCaptureActive_{topicIndex}) return false;");
                if (fields.Count == 1 && NeedsCaptureSequence(fields[0]))
                    sb.AppendLine($"{pad}                if (__foxRunSequence_{topicIndex} == ulong.MaxValue) return false;");
                sb.AppendLine($"{pad}                try");
                sb.AppendLine($"{pad}                {{");
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}                    __foxRunCapture_{topicIndex}_{fieldIndex} = {TypeExprEmitter.MemberAccess(fields[fieldIndex].MemberName)};");
                if (fields.Count == 1 && NeedsCaptureSequence(fields[0]))
                    sb.AppendLine($"{pad}                    __foxRunCaptureSequence_{topicIndex} = ++__foxRunSequence_{topicIndex};");
                if (MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                {
                    var buildCondition =
                        MessagePackPublishDispatchEmitter
                            .UsesMessagePack(fields)
                            ? "true"
                            : TopicMetadataEmitter
                                .IsInherited(fields)
                                ? $"__foxRunCaptureEncoding_{topicIndex} == FoxRunEncoding.MessagePack || __foxRunRecordMessagePack_{topicIndex}"
                                : $"__foxRunRecordMessagePack_{topicIndex}";
                    sb.AppendLine(
                        $"{pad}                    if ({buildCondition})");
                    sb.AppendLine($"{pad}                    {{");
                    sb.AppendLine(
                        $"{pad}                        var __payload_{topicIndex} = __BuildFoxRunMessagePack_{topicIndex}();");
                    sb.AppendLine(
                        $"{pad}                        __foxRunLastMessagePack_{topicIndex} = __payload_{topicIndex};");
                    sb.AppendLine($"{pad}                    }}");
                }
                else if (TopicMetadataEmitter.IsInherited(fields))
                {
                    sb.AppendLine(
                        $"{pad}                    if (__foxRunCaptureEncoding_{topicIndex} == FoxRunEncoding.MessagePack)");
                    sb.AppendLine(
                        $"{pad}                        throw new global::System.InvalidOperationException(\"Typed MessagePack capture is unavailable for this declaration.\");");
                }
                sb.AppendLine($"{pad}                    __foxRunCaptureActive_{topicIndex} = true;");
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine($"{pad}                catch");
                sb.AppendLine($"{pad}                {{");
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}                    __foxRunCapture_{topicIndex}_{fieldIndex} = default;");
                if (fields.Count == 1 && NeedsCaptureSequence(fields[0]))
                    sb.AppendLine($"{pad}                    __foxRunCaptureSequence_{topicIndex} = 0;");
                if (MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                {
                    sb.AppendLine($"{pad}                    __foxRunLastMessagePack_{topicIndex} = null;");
                    sb.AppendLine(
                        $"{pad}                    __foxRunRecordMessagePack_{topicIndex} = false;");
                }
                if (TopicMetadataEmitter.IsInherited(fields))
                    sb.AppendLine($"{pad}                    __foxRunCaptureEncoding_{topicIndex} = (FoxRunEncoding)0;");
                sb.AppendLine($"{pad}                    __foxRunCaptureActive_{topicIndex} = false;");
                sb.AppendLine($"{pad}                    throw;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                sb.AppendLine($"{pad}            case {topicIndex}:");
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}                __foxRunCapture_{topicIndex}_{fieldIndex} = default;");
                if (fields.Count == 1 && NeedsCaptureSequence(fields[0]))
                    sb.AppendLine($"{pad}                __foxRunCaptureSequence_{topicIndex} = 0;");
                sb.AppendLine($"{pad}                __foxRunCaptureActive_{topicIndex} = false;");
                if (IsAggregateTopic(fields)
                    && !MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                    sb.AppendLine($"{pad}                __foxRunLastJson_{topicIndex} = null;");
                if (MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                {
                    sb.AppendLine($"{pad}                __foxRunLastMessagePack_{topicIndex} = null;");
                    sb.AppendLine(
                        $"{pad}                __foxRunRecordMessagePack_{topicIndex} = false;");
                }
                if (TopicMetadataEmitter.IsInherited(fields))
                    sb.AppendLine($"{pad}                __foxRunCaptureEncoding_{topicIndex} = (FoxRunEncoding)0;");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            EmitOriginMethods(sb, topics, topicMap, pad);
            EmitWebSocketEncodingSetter(
                sb,
                topics,
                topicMap,
                pad);
            EmitRecordingMethods(
                sb,
                topics,
                topicMap,
                pad);
        }

        private static void EmitOriginMethods(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishOriginSource.FoxgloveLog_CanPublishOrigin(int topicIndex, bool explicitTrigger)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (explicitTrigger) return true;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!fields.Any(field => field.Mode == 3))
                {
                    sb.AppendLine($"{pad}            case {topicIndex}: return true;");
                    continue;
                }
                sb.AppendLine($"{pad}            case {topicIndex}: return __FoxRunCanPublishOrigin_{topicIndex}();");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxRunRemoteOwnershipSource.FoxRunOrigin_MarkRemoteApplied(int topicIndex, string transportId, ulong generation)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Any(field => field.Mode == 3))
                    sb.AppendLine($"{pad}            case {topicIndex}: __FoxRunMarkRemoteApplied_{topicIndex}(transportId, generation); return;");
            }
            sb.AppendLine($"{pad}            default: return;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxRunRemoteOwnershipSource.FoxRunOrigin_TryGetRemoteApplied(int topicIndex, out string transportId, out ulong generation)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        transportId = string.Empty;");
            sb.AppendLine($"{pad}        generation = 0;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!fields.Any(field => field.Mode == 3))
                    continue;
                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine($"{pad}                if (!__foxRunRemoteOwned_{topicIndex}) return false;");
                sb.AppendLine($"{pad}                transportId = __foxRunRemoteTransport_{topicIndex} ?? string.Empty;");
                sb.AppendLine($"{pad}                generation = __foxRunRemoteGeneration_{topicIndex};");
                sb.AppendLine($"{pad}                return true;");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxRunRemoteOwnershipSource.FoxRunOrigin_ClearRemoteApplied(int topicIndex, string transportId, ulong generation)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!fields.Any(field => field.Mode == 3))
                    continue;
                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine($"{pad}                if (__foxRunRemoteOwned_{topicIndex}");
                sb.AppendLine($"{pad}                    && global::System.String.Equals(__foxRunRemoteTransport_{topicIndex}, transportId ?? string.Empty, global::System.StringComparison.Ordinal)");
                sb.AppendLine($"{pad}                    && __foxRunRemoteGeneration_{topicIndex} == generation)");
                sb.AppendLine($"{pad}                    __FoxRunClearRemoteApplied_{topicIndex}();");
                sb.AppendLine($"{pad}                return;");
            }
            sb.AppendLine($"{pad}            default: return;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!fields.Any(field => field.Mode == 3))
                    continue;

                sb.AppendLine();
                sb.AppendLine($"{pad}    private bool __FoxRunCanPublishOrigin_{topicIndex}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        if (!__foxRunRemoteOwned_{topicIndex}) return true;");
                if (NeedsStructuralOriginSnapshot(fields))
                {
                    sb.AppendLine($"{pad}        var __remoteUnchanged = global::Unity.FoxgloveSDK.Components.FoxRunOriginSnapshot.BytesEqual(");
                    sb.AppendLine($"{pad}            __foxRunRemoteValue_{topicIndex}_0,");
                    sb.AppendLine($"{pad}            __BuildFoxRunOriginFingerprint_{topicIndex}());");
                }
                else
                {
                    sb.AppendLine($"{pad}        var __remoteUnchanged = true;");
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    {
                        var field = fields[fieldIndex];
                        var access = TypeExprEmitter.MemberAccess(field.MemberName);
                        sb.AppendLine($"{pad}        if (__remoteUnchanged) __remoteUnchanged = global::System.Collections.Generic.EqualityComparer<{CaptureTypeName(field.TypeName)}>.Default.Equals({access}, __foxRunRemoteValue_{topicIndex}_{fieldIndex});");
                    }
                }
                sb.AppendLine($"{pad}        if (__remoteUnchanged) return false;");
                if (fields[0].Policy == ChangePolicy)
                {
                    // The local value may return to the exact value stored by
                    // the previous successful local publish. Releasing remote
                    // ownership must therefore invalidate the Change-policy
                    // snapshot before the hub evaluates ShouldPublish.
                    sb.AppendLine($"{pad}        __hasLast_{topicIndex} = false;");
                    sb.AppendLine($"{pad}        __hasRecorded_{topicIndex} = false;");
                }
                sb.AppendLine($"{pad}        __FoxRunClearRemoteApplied_{topicIndex}();");
                sb.AppendLine($"{pad}        return true;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __FoxRunClearRemoteApplied_{topicIndex}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        __foxRunRemoteOwned_{topicIndex} = false;");
                sb.AppendLine($"{pad}        __foxRunRemoteTransport_{topicIndex} = null;");
                sb.AppendLine($"{pad}        __foxRunRemoteGeneration_{topicIndex} = 0;");
                if (NeedsStructuralOriginSnapshot(fields))
                {
                    sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_0 = null;");
                }
                else
                {
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                        sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_{fieldIndex} = default;");
                }
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __FoxRunMarkRemoteApplied_{topicIndex}()");
                sb.AppendLine($"{pad}        => __FoxRunMarkRemoteApplied_{topicIndex}(string.Empty, 0);");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __FoxRunMarkRemoteApplied_{topicIndex}(string transportId, ulong generation)");
                sb.AppendLine($"{pad}    {{");
                if (NeedsStructuralOriginSnapshot(fields))
                {
                    sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_0 = __BuildFoxRunOriginFingerprint_{topicIndex}();");
                }
                else
                {
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    {
                        var access = TypeExprEmitter.MemberAccess(fields[fieldIndex].MemberName);
                        sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_{fieldIndex} = {access};");
                    }
                }
                sb.AppendLine($"{pad}        __foxRunRemoteTransport_{topicIndex} = transportId ?? string.Empty;");
                sb.AppendLine($"{pad}        __foxRunRemoteGeneration_{topicIndex} = generation;");
                sb.AppendLine($"{pad}        __foxRunRemoteOwned_{topicIndex} = true;");
                sb.AppendLine($"{pad}    }}");
            }
        }

        private static void EmitWebSocketEncodingSetter(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxRunWebSocketCaptureSource.FoxgloveLog_SetWebSocketEncoding(int topicIndex, FoxRunEncoding encoding)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                if (!TopicMetadataEmitter.IsInherited(
                        topicMap[topics[topicIndex]]))
                {
                    continue;
                }

                sb.AppendLine($"{pad}            case {topicIndex}: __foxRunCaptureEncoding_{topicIndex} = encoding; break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitRecordingMethods(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishRecordingSource.FoxgloveLog_IsRecordingReady(");
            sb.AppendLine($"{pad}        int topicIndex, FoxgloveManager mgr, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (mgr == null || mgr.SuppressLivePublishersForReplay) {{ reason = \"MCAP recording is unavailable.\"; return false; }}");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                var topic = StringLiteralEmitter.CSharpStringLiteral(
                    topics[topicIndex]);
                if (!MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                    continue;

                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine(
                    $"{pad}                __foxRunRecordMessagePack_{topicIndex} = mgr.TryPrepareFoxRunMessagePackRecording(\"{topic}\", out _, out reason);");
                sb.AppendLine(
                    $"{pad}                return __foxRunRecordMessagePack_{topicIndex};");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishRecordingSource.FoxgloveLog_RecordCaptured(");
            sb.AppendLine($"{pad}        int topicIndex, FoxgloveManager mgr, ulong nowNs, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (mgr == null) return false;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                var topic = StringLiteralEmitter.CSharpStringLiteral(
                    topics[topicIndex]);
                if (!MessagePackPublishDispatchEmitter
                    .CanEncodeMessagePack(fields))
                    continue;

                sb.AppendLine($"{pad}            case {topicIndex}:");
                sb.AppendLine(
                    $"{pad}                if (!__foxRunRecordMessagePack_{topicIndex}) {{ reason = \"MessagePack recording was not prepared for this capture.\"; return false; }}");
                sb.AppendLine(
                    $"{pad}                return mgr.TryPublishFoxRunMessagePackRecording(\"{topic}\", __foxRunLastMessagePack_{topicIndex}, nowNs, out reason);");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static string PayloadExpr(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields, int topicIndex)
        {
            var dict = new StringBuilder("new Dictionary<string, object> { ");
            for (int j = 0; j < fields.Count; j++)
            {
                if (j > 0) dict.Append(", ");
                dict.Append($"[\"{StringLiteralEmitter.CSharpStringLiteral(fields[j].JsonFieldName)}\"] = {CapturedValueExpr(topicIndex, j, fields[j].TypeName)}");
            }
            dict.Append(" }");
            return dict.ToString();
        }


        internal static bool NeedsCaptureSequence(
            FoxgloveSourceEmitter.TopicMember member)
            => member?.TypeShape?.Kind == FoxRunTypeShapeKind.Object
               && member.TypeShape.CanConstruct;

        /// <summary>
        /// Emits the <c>IFoxgloveLogSource.FoxgloveLog_Publish</c> implementation
        /// that switches on topic index and emits a
        /// <c>FoxgloveManager.PublishJson</c> call for each topic.
    }
}
