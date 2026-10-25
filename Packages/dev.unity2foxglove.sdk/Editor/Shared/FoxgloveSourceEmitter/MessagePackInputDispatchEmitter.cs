// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits bounded topic-level transactional MessagePack input.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <remarks>Phase192 source sections: MessagePackInputDispatchEmitter.Core.cs, MessagePackInputDispatchEmitter.Readers.cs, MessagePackInputDispatchEmitter.Shapes.cs</remarks>
    internal static partial class MessagePackInputDispatchEmitter
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
            var isMessagePack = string.Equals(
                member.Encoding,
                FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                StringComparison.Ordinal);
            var isInherited = string.Equals(
                member.Encoding,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                StringComparison.Ordinal);
            return (isMessagePack || isInherited)
                   && FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                       member.TypeShape,
                       member.CanonicalType);
        }
    }
}
