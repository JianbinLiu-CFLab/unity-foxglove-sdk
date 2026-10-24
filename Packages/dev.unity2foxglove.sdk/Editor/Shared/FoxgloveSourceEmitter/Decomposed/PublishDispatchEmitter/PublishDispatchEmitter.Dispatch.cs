// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Phase192 partial source section for PublishDispatchEmitter.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class PublishDispatchEmitter
    {
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
                var encoding = TopicMetadataEmitter.EffectiveEncoding(fields);
                var protobuf = string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    System.StringComparison.Ordinal);
                var messagePack = string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    System.StringComparison.Ordinal);
                var inherited = TopicMetadataEmitter.IsInherited(fields);
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}            case {i}:");
                    if (inherited)
                    {
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        {
                            sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                            sb.AppendLine($"{pad}                {{");
                            sb.AppendLine($"{pad}                    var __payload_{i} = __foxRunLastMessagePack_{i} ?? throw new global::System.InvalidOperationException(\"Frozen MessagePack capture is unavailable.\");");
                            sb.AppendLine($"{pad}                    mgr.PublishFoxRunMessagePackBytes(\"{topic}\", __payload_{i}, nowNs);");
                            sb.AppendLine($"{pad}                }}");
                        }
                        else
                        {
                            sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                            sb.AppendLine($"{pad}                    throw new global::System.InvalidOperationException(\"Typed MessagePack publication is unavailable for this declaration.\");");
                        }
                        sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.JSON)");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    var __payload_{i} = __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                    __foxRunLastJson_{i} = __payload_{i};");
                        sb.AppendLine($"{pad}                    mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __payload_{i}, nowNs);");
                        sb.AppendLine($"{pad}                }}");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                    throw new global::System.InvalidOperationException(\"Frozen FoxRun publish encoding is unsupported.\");");
                    }
                    else if (protobuf)
                    {
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    }
                    else if (messagePack
                             && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                    {
                        sb.AppendLine($"{pad}                mgr.PublishFoxRunMessagePackBytes(\"{topic}\", __foxRunLastMessagePack_{i}, nowNs);");
                    }
                    else if (messagePack)
                    {
                        sb.AppendLine($"{pad}                throw new global::System.InvalidOperationException(\"Typed MessagePack publication is unavailable for this declaration.\");");
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
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        {
                            sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                            sb.AppendLine($"{pad}                {{");
                            sb.AppendLine($"{pad}                    var __payload_{i} = __foxRunLastMessagePack_{i} ?? throw new global::System.InvalidOperationException(\"Frozen MessagePack capture is unavailable.\");");
                            sb.AppendLine($"{pad}                    mgr.PublishFoxRunMessagePackBytes(\"{topic}\", __payload_{i}, nowNs);");
                            sb.AppendLine($"{pad}                }}");
                        }
                        else
                        {
                            sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                            sb.AppendLine($"{pad}                    throw new global::System.InvalidOperationException(\"Typed MessagePack publication is unavailable for this declaration.\");");
                        }
                        sb.AppendLine($"{pad}                else if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.JSON)");
                        sb.AppendLine($"{pad}                    mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields, i)}, nowNs);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                    throw new global::System.InvalidOperationException(\"Frozen FoxRun publish encoding is unsupported.\");");
                    }
                    else if (protobuf)
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    else if (messagePack
                             && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        sb.AppendLine($"{pad}                mgr.PublishFoxRunMessagePackBytes(\"{topic}\", __foxRunLastMessagePack_{i}, nowNs);");
                    else if (messagePack)
                        sb.AppendLine($"{pad}                throw new global::System.InvalidOperationException(\"Typed MessagePack publication is unavailable for this declaration.\");");
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
            string pad)
        {
            IReadOnlyDictionary<string, FoxgloveSourceEmitter.TopicMember>
                nativeBusMembers = null;
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
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine($"{pad}            case {i}: return bus.HasSubscribers(\"{topic}\");");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

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
                else if (TopicMetadataEmitter.IsInherited(fields))
                {
                    if (IsAggregateTopic(fields))
                    {
                        sb.AppendLine(
                            $"{pad}            case {i}: return bus.HasObservers<byte[]>(\"{topic}\");");
                    }
                    else if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                    {
                        sb.AppendLine(
                            $"{pad}            case {i}: return bus.HasObservers<byte[]>(\"{topic}\") || bus.HasObservers<Dictionary<string, object>>(\"{topic}\");");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{pad}            case {i}: return bus.HasObservers<Dictionary<string, object>>(\"{topic}\");");
                    }
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields)
                         && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                {
                    sb.AppendLine(
                        $"{pad}            case {i}: return bus.HasObservers<byte[]>(\"{topic}\");");
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                {
                    sb.AppendLine($"{pad}            case {i}: return false;");
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
                else if (TopicMetadataEmitter.IsInherited(fields))
                {
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                    {
                        sb.AppendLine(
                            $"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine(
                            $"{pad}                    var __foxRunObserverPayload_{i} = __foxRunLastMessagePack_{i};");
                        sb.AppendLine(
                            $"{pad}                    var __foxRunObserverContract_{i} = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}).ForWireEncoding(FoxRunEncoding.MessagePack);");
                        sb.AppendLine(
                            $"{pad}                    bus.PublishToObservers<byte[]>(__foxRunObserverContract_{i}, nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                        sb.AppendLine($"{pad}                }}");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                {{");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine(
                            $"{pad}                    throw new global::System.InvalidOperationException(\"Typed MessagePack observer publication is unavailable for this declaration.\");");
                    }

                    if (IsAggregateTopic(fields))
                    {
                        EnsurePureAggregateTopic(fields, topics[i]);
                        sb.AppendLine(
                            $"{pad}                    var __foxRunObserverPayload_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                        sb.AppendLine(
                            $"{pad}                    bus.PublishToObservers<byte[]>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{pad}                    var __foxRunObserverPayload_{i} = {PayloadExpr(fields, i)};");
                        sb.AppendLine(
                            $"{pad}                    bus.PublishToObservers<Dictionary<string, object>>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                    }
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        sb.AppendLine($"{pad}                }}");
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields)
                         && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                {
                    sb.AppendLine(
                        $"{pad}                var __foxRunObserverPayload_{i} = __foxRunLastMessagePack_{i};");
                    sb.AppendLine(
                        $"{pad}                var __foxRunObserverContract_{i} = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}).ForWireEncoding(FoxRunEncoding.MessagePack);");
                    sb.AppendLine(
                        $"{pad}                bus.PublishToObservers<byte[]>(__foxRunObserverContract_{i}, nowNs, in __foxRunObserverPayload_{i}, __foxRunOrigin, 0UL);");
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                {
                    sb.AppendLine(
                        $"{pad}                throw new global::System.InvalidOperationException(\"Typed MessagePack observer publication is unavailable for this declaration.\");");
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
                else if (TopicMetadataEmitter.IsInherited(fields))
                {
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                    {
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    var __payload_{i} = __foxRunLastMessagePack_{i};");
                        sb.AppendLine(
                            $"{pad}                    var __foxRunBusContract_{i} = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}).ForWireEncoding(FoxRunEncoding.MessagePack);");
                        sb.AppendLine($"{pad}                    bus.Publish(__foxRunBusContract_{i}, nowNs, in __payload_{i}, __foxRunOrigin);");
                        sb.AppendLine($"{pad}                }}");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                {{");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine(
                            $"{pad}                    throw new global::System.InvalidOperationException(\"Typed MessagePack bus publication is unavailable for this declaration.\");");
                    }

                    if (IsAggregateTopic(fields))
                    {
                        EnsurePureAggregateTopic(fields, topics[i]);
                        sb.AppendLine($"{pad}                    var __payload_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                    bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __payload_{i}, __foxRunOrigin);");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                    bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, {PayloadExpr(fields, i)}, __foxRunOrigin);");
                    }
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        sb.AppendLine($"{pad}                }}");
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields)
                         && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                {
                    sb.AppendLine($"{pad}                var __payload_{i} = __foxRunLastMessagePack_{i};");
                    sb.AppendLine(
                        $"{pad}                var __foxRunBusContract_{i} = ((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}).ForWireEncoding(FoxRunEncoding.MessagePack);");
                    sb.AppendLine($"{pad}                bus.Publish(__foxRunBusContract_{i}, nowNs, in __payload_{i}, __foxRunOrigin);");
                }
                else if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                {
                    sb.AppendLine(
                        $"{pad}                throw new global::System.InvalidOperationException(\"Typed MessagePack bus publication is unavailable for this declaration.\");");
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
                if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields)
                    && MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                {
                    sb.AppendLine($"{pad}                router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), FoxRunEncoding.MessagePack, nowNs, __foxRunLastMessagePack_{i}, __foxRunOrigin);");
                    sb.AppendLine($"{pad}                break;");
                    continue;
                }
                if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                {
                    sb.AppendLine($"{pad}                throw new global::System.InvalidOperationException(\"Typed MessagePack sink publication is unavailable for this declaration.\");");
                    continue;
                }
                if (TopicMetadataEmitter.IsInherited(fields))
                {
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                    {
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine($"{pad}                    router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), FoxRunEncoding.MessagePack, nowNs, __foxRunLastMessagePack_{i}, __foxRunOrigin);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                {{");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                if (__foxRunCaptureEncoding_{i} == FoxRunEncoding.MessagePack)");
                        sb.AppendLine($"{pad}                    throw new global::System.InvalidOperationException(\"Typed MessagePack sink publication is unavailable for this declaration.\");");
                    }
                    if (IsAggregateTopic(fields))
                    {
                        EnsurePureAggregateTopic(fields, topics[i]);
                        sb.AppendLine($"{pad}                    var __sink_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                    var __sink_{i} = __BuildFoxRunJson_{i}();");
                    }
                    sb.AppendLine($"{pad}                    router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), FoxRunEncoding.JSON, nowNs, __sink_{i}, __foxRunOrigin);");
                    if (MessagePackPublishDispatchEmitter.MayUseMessagePack(fields))
                        sb.AppendLine($"{pad}                }}");
                    sb.AppendLine($"{pad}                break;");
                    continue;
                }
                else if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __sink_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    sb.AppendLine($"{pad}                __foxRunLastJson_{i} = null;");
                }
                else
                {
                    sb.AppendLine($"{pad}                var __sink_{i} = __BuildFoxRunJson_{i}();");
                }
                sb.AppendLine($"{pad}                router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), FoxRunEncoding.JSON, nowNs, __sink_{i}, __foxRunOrigin);");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }
    }
}
