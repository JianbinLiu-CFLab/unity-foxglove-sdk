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
    internal static class PublishDispatchEmitter
    {
        private const int ChangePolicy = 2;

        internal static void EmitCaptureAndTargets(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            IReadOnlyDictionary<string, FoxgloveSourceEmitter.TopicMember> nativeBusMembers,
            string pad)
        {
            sb.AppendLine($"{pad}    private readonly string __foxRunOrigin = \"unity2foxglove-\" + global::System.Guid.NewGuid().ToString(\"N\");");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                sb.AppendLine($"{pad}    private bool __foxRunCaptureActive_{topicIndex};");
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                {
                    sb.AppendLine($"{pad}    private ulong __foxRunSequence_{topicIndex};");
                    sb.AppendLine($"{pad}    private ulong __foxRunCaptureSequence_{topicIndex};");
                }
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}    private {CaptureTypeName(fields[fieldIndex].TypeName)} __foxRunCapture_{topicIndex}_{fieldIndex};");

                if (!fields.Any(field => field.Mode == 3))
                    continue;

                sb.AppendLine($"{pad}    private bool __foxRunRemoteOwned_{topicIndex};");
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
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                    sb.AppendLine($"{pad}                if (__foxRunSequence_{topicIndex} == ulong.MaxValue) return false;");
                sb.AppendLine($"{pad}                try");
                sb.AppendLine($"{pad}                {{");
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}                    __foxRunCapture_{topicIndex}_{fieldIndex} = {TypeExprEmitter.MemberAccess(fields[fieldIndex].MemberName)};");
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                    sb.AppendLine($"{pad}                    __foxRunCaptureSequence_{topicIndex} = ++__foxRunSequence_{topicIndex};");
                sb.AppendLine($"{pad}                    __foxRunCaptureActive_{topicIndex} = true;");
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
                sb.AppendLine($"{pad}                catch");
                sb.AppendLine($"{pad}                {{");
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    sb.AppendLine($"{pad}                    __foxRunCapture_{topicIndex}_{fieldIndex} = default;");
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                    sb.AppendLine($"{pad}                    __foxRunCaptureSequence_{topicIndex} = 0;");
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
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                    sb.AppendLine($"{pad}                __foxRunCaptureSequence_{topicIndex} = 0;");
                sb.AppendLine($"{pad}                __foxRunCaptureActive_{topicIndex} = false;");
                if (IsAggregateTopic(fields))
                    sb.AppendLine($"{pad}                __foxRunLastJson_{topicIndex} = null;");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            EmitOriginMethods(sb, topics, topicMap, pad);
            EmitTargetMethods(sb, ns, className, topics, topicMap, nativeBusMembers, pad);
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
                sb.AppendLine($"{pad}        __foxRunRemoteOwned_{topicIndex} = false;");
                if (fields[0].Policy == ChangePolicy)
                {
                    // The local value may return to the exact value stored by
                    // the previous successful local publish. Releasing remote
                    // ownership must therefore invalidate the Change-policy
                    // snapshot before the hub evaluates ShouldPublish.
                    sb.AppendLine($"{pad}        __hasLast_{topicIndex} = false;");
                }
                if (NeedsStructuralOriginSnapshot(fields))
                {
                    sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_0 = null;");
                }
                else
                {
                    for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                        sb.AppendLine($"{pad}        __foxRunRemoteValue_{topicIndex}_{fieldIndex} = default;");
                }
                sb.AppendLine($"{pad}        return true;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __FoxRunMarkRemoteApplied_{topicIndex}()");
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
                sb.AppendLine($"{pad}        __foxRunRemoteOwned_{topicIndex} = true;");
                sb.AppendLine($"{pad}    }}");
            }
        }

        private static void EmitTargetMethods(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            IReadOnlyDictionary<string, FoxgloveSourceEmitter.TopicMember> nativeBusMembers,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishTargetSource.FoxgloveLog_IsTargetReady(");
            sb.AppendLine($"{pad}        int topicIndex, FoxRunEndpoint target, FoxRunResolvedPublishContract resolved,");
            sb.AppendLine($"{pad}        FoxgloveManager mgr, FoxTopicBus bus, FoxTopicSinkRouter router, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (resolved == null || !resolved.Selects(target)) {{ reason = \"Target was not selected.\"; return false; }}");
            sb.AppendLine($"{pad}        if (mgr == null) {{ reason = \"Foxglove Manager is unavailable.\"; return false; }}");
            sb.AppendLine($"{pad}        if (mgr.SuppressLivePublishersForReplay) {{ reason = \"Replay is suppressing live publishers.\"; return false; }}");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Foxglove)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (mgr.IsRunning) return true;");
            sb.AppendLine($"{pad}            reason = \"Foxglove output is unavailable.\"; return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        var __contract = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(topicIndex);");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Ros2Native)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                if (!nativeBusMembers.TryGetValue(topics[topicIndex], out var member))
                    continue;
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex]);
                var dtoType = GlobalTypeName(member.TypeName);
                sb.AppendLine($"{pad}                case {topicIndex}: if (bus != null && bus.HasResultSubscribers<{dtoType}>(\"{topic}\", __foxRunOrigin)) return true; reason = \"ROS 2 custom publisher is unavailable.\"; return false;");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                if (!TryGetBundledCdrMember(topicMap[topics[topicIndex]], out _))
                    continue;
                sb.AppendLine($"{pad}                case {topicIndex}: if (router != null && router.HasReadyTarget(target, __contract)) return true; reason = \"ROS 2 native target is unavailable.\"; return false;");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            reason = \"No exact native ROS 2 serializer is available for this declaration.\"; return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Ros2Bridge)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!TryGetRos2CdrSchema(fields, out var schema))
                    continue;
                sb.AppendLine($"{pad}                case {topicIndex}: return mgr.TryPrepareFoxRunRos2BridgePublish(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{StringLiteralEmitter.CSharpStringLiteral(schema)}\", resolved.BridgeQos, out _, out reason);");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            reason = \"No exact ROS 2 Bridge serializer is available for this declaration.\"; return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        reason = \"Unknown publish target.\"; return false;");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishTargetSource.FoxgloveLog_PublishCaptured(");
            sb.AppendLine($"{pad}        int topicIndex, FoxRunEndpoint target, FoxRunResolvedPublishContract resolved,");
            sb.AppendLine($"{pad}        FoxgloveManager mgr, FoxTopicBus bus, FoxTopicSinkRouter router, ulong nowNs, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Foxglove)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            ((IFoxgloveLogSource)this).FoxgloveLog_Publish(topicIndex, mgr, nowNs);");
            sb.AppendLine($"{pad}            return true;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        var __contract = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(topicIndex);");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Ros2Native)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                if (!nativeBusMembers.TryGetValue(topics[topicIndex], out var member))
                    continue;
                var dtoType = GlobalTypeName(member.TypeName);
                sb.AppendLine($"{pad}                case {topicIndex}:");
                sb.AppendLine($"{pad}                    var __native_{topicIndex} = __foxRunCapture_{topicIndex}_0;");
                sb.AppendLine($"{pad}                    var __nativeResult_{topicIndex} = bus.PublishToResultSubscribers<{dtoType}>(__contract, nowNs, in __native_{topicIndex}, __foxRunOrigin, __foxRunCaptureSequence_{topicIndex});");
                sb.AppendLine($"{pad}                    if (__nativeResult_{topicIndex}.AllSucceeded) return true;");
                sb.AppendLine($"{pad}                    reason = __nativeResult_{topicIndex}.Matched == 0 ? \"ROS 2 custom publisher is unavailable.\" : \"ROS 2 custom publisher rejected the sample.\";");
                sb.AppendLine($"{pad}                    return false;");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Ros2Native)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (router == null) {{ reason = \"Target router is unavailable.\"; return false; }}");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                if (!TryGetBundledCdrMember(topicMap[topics[topicIndex]], out _))
                    continue;
                sb.AppendLine($"{pad}                case {topicIndex}:");
                sb.AppendLine($"{pad}                    var __nativeJson_{topicIndex} = __BuildFoxRunJson_{topicIndex}();");
                sb.AppendLine($"{pad}                    var __result_{topicIndex} = router.PublishTarget(target, __contract, nowNs, __nativeJson_{topicIndex}, __foxRunOrigin);");
                sb.AppendLine($"{pad}                    if (__result_{topicIndex}.Succeeded) return true;");
                sb.AppendLine($"{pad}                    reason = __result_{topicIndex}.HadReadySink ? \"Target rejected the JSON payload.\" : \"Target is unavailable.\";");
                sb.AppendLine($"{pad}                    return false;");
            }
            sb.AppendLine($"{pad}                default: reason = \"No exact native ROS 2 serializer is available for this declaration.\"; return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        if (target == FoxRunEndpoint.Ros2Bridge)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            switch (topicIndex)");
            sb.AppendLine($"{pad}            {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                {
                    var schema = StringLiteralEmitter.CSharpStringLiteral(
                        Ros2CustomDtoMapperEmitter.CanonicalEnvelopeType(fields[0]));
                    sb.AppendLine($"{pad}                case {topicIndex}:");
                    sb.AppendLine($"{pad}                    if (!__TryBuildFoxRunRos2Cdr_{topicIndex}(nowNs, out var __bridgeCdr_{topicIndex}, out reason)) return false;");
                    sb.AppendLine($"{pad}                    return mgr.TryPublishFoxRunRos2BridgeCdr(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{schema}\", __bridgeCdr_{topicIndex}, nowNs, resolved.BridgeQos, out reason);");
                }
                else if (TryGetBundledCdrMember(fields, out var packaged))
                {
                    var schema = StringLiteralEmitter.CSharpStringLiteral(packaged.SchemaName);
                    sb.AppendLine($"{pad}                case {topicIndex}:");
                    sb.AppendLine($"{pad}                    if (!global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrSerializerRegistry.TrySerialize(\"{schema}\", (global::Google.Protobuf.IMessage)(object)__foxRunCapture_{topicIndex}_0, out var __bridgeCdr_{topicIndex})) {{ reason = \"Bundled ROS 2 CDR serializer rejected the sample.\"; return false; }}");
                    sb.AppendLine($"{pad}                    return mgr.TryPublishFoxRunRos2BridgeCdr(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{schema}\", __bridgeCdr_{topicIndex}, nowNs, resolved.BridgeQos, out reason);");
                }
            }
            sb.AppendLine($"{pad}                default: reason = \"No exact ROS 2 Bridge serializer is available for this declaration.\"; return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        reason = \"Unknown publish target.\"; return false;");
            sb.AppendLine($"{pad}    }}");

            EmitRecordingMethods(sb, topics, topicMap, pad);
        }

        private static void EmitRecordingMethods(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishRecordingSource.FoxgloveLog_IsRecordingReady(");
            sb.AppendLine($"{pad}        int topicIndex, FoxRunResolvedPublishContract resolved, FoxgloveManager mgr, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (resolved == null || resolved.Selects(FoxRunEndpoint.Foxglove)) return false;");
            sb.AppendLine($"{pad}        if (mgr == null || mgr.SuppressLivePublishersForReplay) {{ reason = \"MCAP recording is unavailable.\"; return false; }}");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!TryGetRos2CdrSchema(fields, out var schema))
                    continue;
                var schemaContent = fields.Count == 1 && IsSupportedCustomCdr(fields[0])
                    ? "__foxRunRos2Schema_" + topicIndex
                    : "string.Empty";
                sb.AppendLine($"{pad}            case {topicIndex}: return mgr.TryPrepareFoxRunRos2Recording(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{StringLiteralEmitter.CSharpStringLiteral(schema)}\", {schemaContent}, out _, out reason);");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishRecordingSource.FoxgloveLog_RecordCaptured(");
            sb.AppendLine($"{pad}        int topicIndex, FoxRunResolvedPublishContract resolved, FoxgloveManager mgr, ulong nowNs, out string reason)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        reason = string.Empty;");
            sb.AppendLine($"{pad}        if (resolved == null || resolved.Selects(FoxRunEndpoint.Foxglove) || mgr == null) return false;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count == 1 && IsSupportedCustomCdr(fields[0]))
                {
                    var schema = StringLiteralEmitter.CSharpStringLiteral(
                        Ros2CustomDtoMapperEmitter.CanonicalEnvelopeType(fields[0]));
                    sb.AppendLine($"{pad}            case {topicIndex}:");
                    sb.AppendLine($"{pad}                if (!__TryBuildFoxRunRos2Cdr_{topicIndex}(nowNs, out var __recordCdr_{topicIndex}, out reason)) return false;");
                    sb.AppendLine($"{pad}                return mgr.TryPublishFoxRunRos2Recording(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{schema}\", __foxRunRos2Schema_{topicIndex}, __recordCdr_{topicIndex}, nowNs, out reason);");
                }
                else if (TryGetBundledCdrMember(fields, out var packaged))
                {
                    var schema = StringLiteralEmitter.CSharpStringLiteral(packaged.SchemaName);
                    sb.AppendLine($"{pad}            case {topicIndex}:");
                    sb.AppendLine($"{pad}                if (!global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrSerializerRegistry.TrySerialize(\"{schema}\", (global::Google.Protobuf.IMessage)(object)__foxRunCapture_{topicIndex}_0, out var __recordCdr_{topicIndex})) {{ reason = \"Bundled ROS 2 CDR serializer rejected the sample.\"; return false; }}");
                    sb.AppendLine($"{pad}                return mgr.TryPublishFoxRunRos2Recording(\"{StringLiteralEmitter.CSharpStringLiteral(topics[topicIndex])}\", \"{schema}\", string.Empty, __recordCdr_{topicIndex}, nowNs, out reason);");
                }
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static bool TryGetRos2CdrSchema(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields,
            out string schema)
        {
            schema = string.Empty;
            if (fields == null || fields.Count != 1)
                return false;
            if (IsSupportedCustomCdr(fields[0]))
            {
                schema = Ros2CustomDtoMapperEmitter.CanonicalEnvelopeType(fields[0]);
                return true;
            }
            if (!TryGetBundledCdrMember(fields, out var member))
                return false;
            schema = member.SchemaName;
            return true;
        }

        private static bool IsSupportedCustomCdr(FoxgloveSourceEmitter.TopicMember member)
            => member != null
               && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported
               && member.Ros2CustomDtoShape.Diagnostics.Count == 0;

        private static bool TryGetBundledCdrMember(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields,
            out FoxgloveSourceEmitter.TopicMember member)
        {
            member = fields != null && fields.Count == 1 ? fields[0] : null;
            if (member == null
                || string.IsNullOrWhiteSpace(member.SchemaName)
                || !member.SchemaName.StartsWith("foxglove_msgs/msg/", System.StringComparison.Ordinal))
            {
                member = null;
                return false;
            }

            var type = member.TypeName ?? string.Empty;
            if (!type.StartsWith("Foxglove.", System.StringComparison.Ordinal)
                && !type.StartsWith("global::Foxglove.", System.StringComparison.Ordinal))
            {
                member = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Emits the <c>IFoxgloveLogSource.FoxgloveLog_Publish</c> implementation
        /// that switches on topic index and emits a
        /// <c>FoxgloveManager.PublishJson</c> call for each topic.
        /// </summary>
        internal static void EmitPublish(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveLogSource.FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var rawSchema = fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "";
                var schema = StringLiteralEmitter.CSharpStringLiteral(rawSchema);
                var protobufSchema = StringLiteralEmitter.CSharpStringLiteral(
                    FoxRunProtobufContractBuilder.ResolveMessageFullName(rawSchema, declaringType, topics[i]));
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                var protobuf = string.Equals(
                    TopicMetadataEmitter.EffectiveEncoding(fields),
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    System.StringComparison.Ordinal);
                var inherited = TopicMetadataEmitter.IsInherited(fields);
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}            case {i}:");
                    if (inherited)
                    {
                        sb.AppendLine($"{pad}                if (mgr.ResolveFoxRunEncoding((FoxRunEncoding)0, FoxRunFlow.Publish) == FoxRunEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    var __payload_{i} = __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                    __foxRunLastJson_{i} = __payload_{i};");
                        sb.AppendLine($"{pad}                    mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __payload_{i}, nowNs);");
                        sb.AppendLine($"{pad}                }}");
                    }
                    else if (protobuf)
                    {
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                var __payload_{i} = __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                __foxRunLastJson_{i} = __payload_{i};");
                        sb.AppendLine($"{pad}                mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __payload_{i}, nowNs);");
                    }
                    sb.AppendLine($"{pad}                break;");
                }
                else
                {
                    sb.AppendLine($"{pad}            case {i}:");
                    if (inherited)
                    {
                        sb.AppendLine($"{pad}                if (mgr.ResolveFoxRunEncoding((FoxRunEncoding)0, FoxRunFlow.Publish) == FoxRunEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                    mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields, i)}, nowNs);");
                    }
                    else if (protobuf)
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    else
                        sb.AppendLine($"{pad}                mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields, i)}, nowNs);");
                    sb.AppendLine($"{pad}                break;");
                }
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            EmitAggregateJsonWriters(sb, topics, topicMap, pad);
            ProtobufPublishDispatchEmitter.EmitBuilders(sb, declaringType, topics, topicMap, pad);
        }

        /// <summary>
        /// Emits the optional local-bus publish side-channel. The generated
        /// method checks for subscribers before building the payload, so the
        /// existing live path does not allocate extra dictionaries when no
        /// local consumers are attached.
        /// </summary>
        internal static void EmitPublishToBus(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            IReadOnlyDictionary<string, FoxgloveSourceEmitter.TopicMember> nativeBusMembers,
            string pad)
        {
            if (nativeBusMembers != null && nativeBusMembers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{pad}    [Preserve]");
                sb.AppendLine($"{pad}    bool IFoxgloveTopicBusDemandSource.FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        if (bus == null)");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        switch (topicIndex)");
                sb.AppendLine($"{pad}        {{");
                for (int i = 0; i < topics.Count; i++)
                {
                    if (!nativeBusMembers.ContainsKey(topics[i]))
                        continue;
                    var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                    sb.AppendLine($"{pad}            case {i}: return bus.HasSubscribers(\"{topic}\");");
                }
                sb.AppendLine($"{pad}            default: return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");
            }

            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    bool IFoxgloveTopicObserverSource.FoxgloveLog_HasObservers(int topicIndex, FoxTopicBus bus)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (bus == null)");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                if (nativeBusMembers != null
                    && nativeBusMembers.TryGetValue(topics[i], out var customMember))
                {
                    sb.AppendLine(
                        $"{pad}            case {i}: return bus.HasObservers<{GlobalTypeName(customMember.TypeName)}>(\"{topic}\");");
                }
                else if (IsAggregateTopic(fields))
                {
                    sb.AppendLine(
                        $"{pad}            case {i}: return bus.HasObservers<byte[]>(\"{topic}\");");
                }
                else
                {
                    sb.AppendLine(
                        $"{pad}            case {i}: return bus.HasObservers<Dictionary<string, object>>(\"{topic}\");");
                }
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveTopicObserverSource.FoxgloveLog_PublishCapturedToObservers(int topicIndex, FoxTopicBus bus, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (bus == null)");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                sb.AppendLine($"{pad}            case {i}:");
                if (nativeBusMembers != null
                    && nativeBusMembers.TryGetValue(topics[i], out var customMember))
                {
                    var dtoType = GlobalTypeName(customMember.TypeName);
                    sb.AppendLine(
                        $"{pad}                var __foxRunObserverPayload_{i} = __foxRunCapture_{i}_0;");
                    sb.AppendLine(
                        $"{pad}                bus.PublishToObservers<{dtoType}>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, __foxRunCaptureSequence_{i});");
                }
                else if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine(
                        $"{pad}                var __foxRunObserverPayload_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    sb.AppendLine(
                        $"{pad}                bus.PublishToObservers<byte[]>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                }
                else
                {
                    sb.AppendLine(
                        $"{pad}                var __foxRunObserverPayload_{i} = {PayloadExpr(fields, i)};");
                    sb.AppendLine(
                        $"{pad}                bus.PublishToObservers<Dictionary<string, object>>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                }
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveTopicBusSource.FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (bus == null)");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                if (!bus.HasSubscribers(\"{topic}\")) break;");
                if (nativeBusMembers != null && nativeBusMembers.TryGetValue(topics[i], out var customMember))
                {
                    var dtoType = GlobalTypeName(customMember.TypeName);
                    sb.AppendLine($"{pad}                var __foxRunNativePayload_{i} = __foxRunCapture_{i}_0;");
                    sb.AppendLine($"{pad}                bus.Publish<{dtoType}>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunNativePayload_{i}, __foxRunOrigin);");
                }
                else if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __payload_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    sb.AppendLine($"{pad}                bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __payload_{i}, __foxRunOrigin);");
                }
                else
                {
                    sb.AppendLine($"{pad}                bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, {PayloadExpr(fields, i)}, __foxRunOrigin);");
                }
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        /// <summary>
        /// Emits the optional additive sink fanout side-channel. Aggregate topics
        /// reuse the JSON bytes built for the primary live/MCAP publish path.
        /// Legacy field-level topics still keep their primary <c>PublishJson</c>
        /// path, while the side-channel builds equivalent JSON bytes only when a
        /// sink is attached.
        /// </summary>
        internal static void EmitPublishToSinks(StringBuilder sb, string ns, string className, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (router == null || !router.HasSinks)");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                sb.AppendLine($"{pad}            case {i}:");
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __sink_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    sb.AppendLine($"{pad}                __foxRunLastJson_{i} = null;");
                }
                else
                {
                    sb.AppendLine($"{pad}                var __sink_{i} = __BuildFoxRunJson_{i}();");
                }
                sb.AppendLine($"{pad}                router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, __sink_{i}, __foxRunOrigin);");
                sb.AppendLine($"{pad}                break;");
            }
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

        private static string GlobalTypeName(string typeName)
            => string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", System.StringComparison.Ordinal)
                ? typeName
                : "global::" + typeName;

        private static string CaptureTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", System.StringComparison.Ordinal))
                return typeName;
            if (typeName.EndsWith("[]", System.StringComparison.Ordinal))
                return CaptureTypeName(typeName.Substring(0, typeName.Length - 2)) + "[]";
            if (typeName.EndsWith("?", System.StringComparison.Ordinal))
                return CaptureTypeName(typeName.Substring(0, typeName.Length - 1)) + "?";
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
                default:
                    return "global::" + typeName;
            }
        }

        private static string CapturedValueExpr(int topicIndex, int fieldIndex, string type)
        {
            var access = $"__foxRunCapture_{topicIndex}_{fieldIndex}";
            var normalized = type != null && type.StartsWith("UnityEngine.", System.StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
            switch (normalized)
            {
                case "Vector3": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z }}";
                case "Vector2": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y }}";
                case "Quaternion": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z, [\"w\"] = {access}.w }}";
                case "Color": return $"new Dictionary<string, object> {{ [\"r\"] = {access}.r, [\"g\"] = {access}.g, [\"b\"] = {access}.b, [\"a\"] = {access}.a }}";
                default: return access;
            }
        }

        internal static bool NeedsStructuralOriginSnapshot(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields.Any(field => !SupportsDirectOriginSnapshot(field));

        private static bool SupportsDirectOriginSnapshot(
            FoxgloveSourceEmitter.TopicMember field)
        {
            if (field?.ProtobufTypeShape?.Kind == FoxRunProtobufTypeShapeKind.Enum)
                return true;
            var type = NormalizeType((field?.TypeName ?? string.Empty).Trim());
            if (type.EndsWith("?", System.StringComparison.Ordinal))
                type = type.Substring(0, type.Length - 1);
            if (type.StartsWith("System.Nullable<", System.StringComparison.Ordinal)
                || type.StartsWith("Nullable<", System.StringComparison.Ordinal))
                return true;
            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
                case "byte":
                case "Byte":
                case "System.Byte":
                case "sbyte":
                case "SByte":
                case "System.SByte":
                case "short":
                case "Int16":
                case "System.Int16":
                case "ushort":
                case "UInt16":
                case "System.UInt16":
                case "int":
                case "Int32":
                case "System.Int32":
                case "uint":
                case "UInt32":
                case "System.UInt32":
                case "long":
                case "Int64":
                case "System.Int64":
                case "ulong":
                case "UInt64":
                case "System.UInt64":
                case "float":
                case "Single":
                case "System.Single":
                case "double":
                case "Double":
                case "System.Double":
                case "decimal":
                case "Decimal":
                case "System.Decimal":
                case "char":
                case "Char":
                case "System.Char":
                case "string":
                case "String":
                case "System.String":
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                case "Color":
                case "Color32":
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitAggregateJsonWriters(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine();
                    sb.AppendLine($"{pad}    private byte[] __foxRunLastJson_{i};");
                }

                sb.AppendLine();
                sb.AppendLine($"{pad}    private byte[] __BuildFoxRunJson_{i}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var __json = new global::System.Text.StringBuilder(128);");
                sb.AppendLine($"{pad}        __WriteFoxRunJson_{i}(__json);");
                sb.AppendLine($"{pad}        return global::System.Text.Encoding.UTF8.GetBytes(__json.ToString());");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __WriteFoxRunJson_{i}(global::System.Text.StringBuilder __json)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        __json.Append('{{');");
                for (int j = 0; j < fields.Count; j++)
                {
                    var separator = j == 0 ? string.Empty : ",";
                    sb.AppendLine($"{pad}        __json.Append(\"{separator}\\\"{StringLiteralEmitter.CSharpStringLiteral(fields[j].JsonFieldName)}\\\":\");");
                    EmitJsonValueAppend(sb, fields[j], i, j, pad + "        ");
                }
                sb.AppendLine($"{pad}        __json.Append('}}');");
                sb.AppendLine($"{pad}    }}");
            }

            if (topics.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void __AppendFoxRunJsonString(global::System.Text.StringBuilder __json, string value)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (value == null)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            __json.Append(\"null\");");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        __json.Append('\\\"');");
            sb.AppendLine($"{pad}        for (int __i = 0; __i < value.Length; __i++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            var __c = value[__i];");
            sb.AppendLine($"{pad}            switch (__c)");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                case '\\\"': __json.Append(\"\\\\\\\"\"); break;");
            sb.AppendLine($"{pad}                case '\\\\': __json.Append(\"\\\\\\\\\"); break;");
            sb.AppendLine($"{pad}                case '\\b': __json.Append(\"\\\\b\"); break;");
            sb.AppendLine($"{pad}                case '\\f': __json.Append(\"\\\\f\"); break;");
            sb.AppendLine($"{pad}                case '\\n': __json.Append(\"\\\\n\"); break;");
            sb.AppendLine($"{pad}                case '\\r': __json.Append(\"\\\\r\"); break;");
            sb.AppendLine($"{pad}                case '\\t': __json.Append(\"\\\\t\"); break;");
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (__c < ' ' || global::System.Char.IsSurrogate(__c))");
            sb.AppendLine($"{pad}                        __json.Append(\"\\\\u\").Append(((int)__c).ToString(\"x4\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}                    else");
            sb.AppendLine($"{pad}                        __json.Append(__c);");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        __json.Append('\\\"');");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitJsonValueAppend(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember field,
            int topicIndex,
            int fieldIndex,
            string pad)
        {
            var type = NormalizeType(field.TypeName);
            var access = $"__foxRunCapture_{topicIndex}_{fieldIndex}";
            if (field.ProtobufTypeShape != null
                && (field.ProtobufTypeShape.Kind == FoxRunProtobufTypeShapeKind.Object
                    || field.ProtobufTypeShape.Kind == FoxRunProtobufTypeShapeKind.Enum))
            {
                sb.AppendLine(
                    $"{pad}global::Unity.FoxgloveSDK.Components.FoxRunInboundJson.AppendObject(__json, {access});");
                return;
            }
            if (TryGetCollectionElementType(type, out var elementType, out var countProperty))
            {
                EmitCollectionJsonValueAppend(sb, elementType, countProperty, access, fieldIndex, pad);
                return;
            }

            EmitScalarOrObjectJsonValueAppend(sb, type, access, pad);
        }

        private static void EmitCollectionJsonValueAppend(
            StringBuilder sb,
            string elementType,
            string countProperty,
            string access,
            int fieldIndex,
            string pad)
        {
            var indexName = "__foxRunIndex_" + fieldIndex;
            sb.AppendLine($"{pad}if ({access} == null)");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    __json.Append(\"null\");");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine($"{pad}else");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    __json.Append('[');");
            sb.AppendLine($"{pad}    for (int {indexName} = 0; {indexName} < {access}.{countProperty}; {indexName}++)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if ({indexName} > 0) __json.Append(',');");
            EmitScalarOrObjectJsonValueAppend(sb, elementType, access + "[" + indexName + "]", pad + "        ");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}    __json.Append(']');");
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitScalarOrObjectJsonValueAppend(StringBuilder sb, string type, string access, string pad)
        {
            if (TryUnwrapNullableType(type, out var nullableType))
            {
                EmitNullableJsonValueAppend(sb, nullableType, access, pad);
                return;
            }

            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
                    sb.AppendLine($"{pad}__json.Append({access} ? \"true\" : \"false\");");
                    break;
                case "string":
                case "String":
                case "System.String":
                    sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access});");
                    break;
                case "float":
                case "Single":
                case "System.Single":
                    sb.AppendLine($"{pad}if (float.IsNaN({access}) || float.IsInfinity({access})) __json.Append(\"null\"); else __json.Append({access}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "double":
                case "Double":
                case "System.Double":
                    sb.AppendLine($"{pad}if (double.IsNaN({access}) || double.IsInfinity({access})) __json.Append(\"null\"); else __json.Append({access}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "Vector2":
                    AppendVector(sb, pad, access, "x", "y");
                    break;
                case "Vector3":
                    AppendVector(sb, pad, access, "x", "y", "z");
                    break;
                case "Quaternion":
                    AppendVector(sb, pad, access, "x", "y", "z", "w");
                    break;
                case "Color":
                    AppendVector(sb, pad, access, "r", "g", "b", "a");
                    break;
                case "Vector4":
                    AppendVector(sb, pad, access, "x", "y", "z", "w");
                    break;
                case "Color32":
                    AppendColor32(sb, pad, access);
                    break;
                default:
                    if (IsIntegralType(type))
                        sb.AppendLine($"{pad}__json.Append({access}.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    else
                        sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access} == null ? null : {access}.ToString());");
                    break;
            }
        }

        private static void EmitNullableJsonValueAppend(StringBuilder sb, string type, string access, string pad)
        {
            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
                    sb.AppendLine($"{pad}if ({access} == null) __json.Append(\"null\"); else __json.Append({access}.Value ? \"true\" : \"false\");");
                    break;
                case "float":
                case "Single":
                case "System.Single":
                    sb.AppendLine($"{pad}if ({access} == null || float.IsNaN({access}.Value) || float.IsInfinity({access}.Value)) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "double":
                case "Double":
                case "System.Double":
                    sb.AppendLine($"{pad}if ({access} == null || double.IsNaN({access}.Value) || double.IsInfinity({access}.Value)) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                default:
                    if (IsIntegralType(type))
                        sb.AppendLine($"{pad}if ({access} == null) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    else
                        sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access} == null ? null : {access}.Value.ToString());");
                    break;
            }
        }

        private static bool TryGetCollectionElementType(string type, out string elementType, out string countProperty)
        {
            elementType = string.Empty;
            countProperty = string.Empty;

            if (type.EndsWith("[]", System.StringComparison.Ordinal))
            {
                elementType = NormalizeType(type.Substring(0, type.Length - 2));
                countProperty = "Length";
                return true;
            }

            const string listPrefix = "List<";
            const string genericListPrefix = "System.Collections.Generic.List<";
            const string iListPrefix = "IList<";
            const string genericIListPrefix = "System.Collections.Generic.IList<";
            const string readOnlyListPrefix = "IReadOnlyList<";
            const string genericReadOnlyListPrefix = "System.Collections.Generic.IReadOnlyList<";

            if (TryGetSingleGenericArgument(type, listPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, iListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericIListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, readOnlyListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericReadOnlyListPrefix, out elementType))
            {
                countProperty = "Count";
                return true;
            }

            return false;
        }

        private static bool TryGetSingleGenericArgument(string type, string prefix, out string argument)
        {
            argument = string.Empty;
            if (!type.StartsWith(prefix, System.StringComparison.Ordinal) || !type.EndsWith(">", System.StringComparison.Ordinal))
                return false;

            argument = NormalizeType(type.Substring(prefix.Length, type.Length - prefix.Length - 1).Trim());
            return argument.IndexOf(',') < 0;
        }

        private static void AppendVector(StringBuilder sb, string pad, string access, params string[] fields)
        {
            sb.AppendLine($"{pad}__json.Append('{{');");
            for (int i = 0; i < fields.Length; i++)
            {
                var separator = i == 0 ? string.Empty : ",";
                var field = fields[i];
                sb.AppendLine($"{pad}__json.Append(\"{separator}\\\"{field}\\\":\");");
                sb.AppendLine($"{pad}if (float.IsNaN({access}.{field}) || float.IsInfinity({access}.{field})) __json.Append(\"null\"); else __json.Append({access}.{field}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            }
            sb.AppendLine($"{pad}__json.Append('}}');");
        }

        private static void AppendColor32(StringBuilder sb, string pad, string access)
        {
            sb.AppendLine($"{pad}__json.Append('{{');");
            sb.AppendLine($"{pad}__json.Append(\"\\\"r\\\":\").Append(((float){access}.r / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"g\\\":\").Append(((float){access}.g / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"b\\\":\").Append(((float){access}.b / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"a\\\":\").Append(((float){access}.a / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append('}}');");
        }

        private static bool IsAggregateTopic(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields.Any(field => field.IsAggregateMember);

        private static void EnsurePureAggregateTopic(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields, string topic)
        {
            if (fields.Any(field => field.IsAggregateMember) && fields.Any(field => !field.IsAggregateMember))
            {
                throw new System.InvalidOperationException(
                    "FoxRun aggregate topic cannot mix aggregate and field-level members: " + topic);
            }
        }

        private static string NormalizeType(string typeName)
        {
            var type = typeName ?? string.Empty;
            return type.StartsWith("UnityEngine.", System.StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
        }

        private static bool TryUnwrapNullableType(string type, out string innerType)
        {
            innerType = string.Empty;
            type = (type ?? string.Empty).Trim();
            if (type.EndsWith("?", System.StringComparison.Ordinal))
            {
                innerType = NormalizeType(type.Substring(0, type.Length - 1).Trim());
                return innerType.Length > 0;
            }

            return TryGetSingleGenericArgument(type, "Nullable<", out innerType)
                   || TryGetSingleGenericArgument(type, "System.Nullable<", out innerType);
        }

        private static bool IsIntegralType(string type)
        {
            switch (type)
            {
                case "byte":
                case "Byte":
                case "System.Byte":
                case "sbyte":
                case "SByte":
                case "System.SByte":
                case "short":
                case "Int16":
                case "System.Int16":
                case "ushort":
                case "UInt16":
                case "System.UInt16":
                case "int":
                case "Int32":
                case "System.Int32":
                case "uint":
                case "UInt32":
                case "System.UInt32":
                case "long":
                case "Int64":
                case "System.Int64":
                case "ulong":
                case "UInt64":
                case "System.UInt64":
                    return true;
                default:
                    return false;
            }
        }
    }
}
