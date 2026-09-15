// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits direct, deterministic typed MessagePack writers for FoxRun.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class MessagePackPublishDispatchEmitter
    {
        internal static bool UsesMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => string.Equals(
                TopicMetadataEmitter.EffectiveEncoding(fields),
                FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                StringComparison.Ordinal);

        internal static bool MayUseMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => CanEncodeMessagePack(fields)
               && (UsesMessagePack(fields)
                   || TopicMetadataEmitter.IsInherited(fields));

        internal static bool CanEncodeMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields != null
               && fields.Count > 0
               && fields.All(field =>
                   FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                       field.TypeShape,
                       field.CanonicalType));

        internal static void EmitFieldsAndBuilders(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var objectShapes = new List<TypedMessagePackWriterEmitter.TypedMessagePackObjectShape>();
            foreach (var topic in topics)
            {
                if (!CanEncodeMessagePack(topicMap[topic]))
                    continue;
                foreach (var field in topicMap[topic])
                    TypedMessagePackWriterEmitter.CollectTypedMessagePackObjectShapes(field.TypeShape, objectShapes);
            }

            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!CanEncodeMessagePack(fields))
                    continue;

                sb.AppendLine($"{pad}    private byte[] __foxRunLastMessagePack_{topicIndex};");
                EmitTopicBuilder(sb, fields, topicIndex, pad, objectShapes);
            }

            for (var shapeIndex = 0; shapeIndex < objectShapes.Count; shapeIndex++)
                TypedMessagePackWriterEmitter.EmitObjectWriter(sb, objectShapes[shapeIndex].Shape, shapeIndex, pad, objectShapes);
        }

        private static void EmitTopicBuilder(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields,
            int topicIndex,
            string pad,
            IReadOnlyList<TypedMessagePackWriterEmitter.TypedMessagePackObjectShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private byte[] __BuildFoxRunMessagePack_{topicIndex}()");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine(
                $"{pad}        using (var __writer = new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackWriter())");
            sb.AppendLine($"{pad}        {{");

            var ordered = fields
                .Select((field, index) => new { Field = field, Index = index })
                .OrderBy(candidate => candidate.Field.JsonFieldName, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Field.MemberName, StringComparer.Ordinal)
                .ToList();
            sb.AppendLine($"{pad}            __writer.WriteMapHeader({ordered.Count});");
            var counter = new TypedMessagePackWriterEmitter.TypedMessagePackCounter();
            foreach (var candidate in ordered)
            {
                sb.AppendLine(
                    $"{pad}            __writer.WriteString(\"{StringLiteralEmitter.CSharpStringLiteral(candidate.Field.JsonFieldName)}\");");
                TypedMessagePackWriterEmitter.EmitValue(
                    sb,
                    candidate.Field.TypeShape
                    ?? FoxRunTypeShape.Canonical(candidate.Field.CanonicalType),
                    "__foxRunCapture_" + topicIndex + "_" + candidate.Index,
                    "__writer",
                    pad + "            ",
                    objectShapes,
                    counter);
            }

            sb.AppendLine($"{pad}            return __writer.ToArray();");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

    }
}
