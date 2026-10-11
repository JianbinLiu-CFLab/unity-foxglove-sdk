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
                IReadOnlyList<BridgeDtoMemberShape> members,
                bool isValueType)
            {
                FullyQualifiedTypeName =
                    fullyQualifiedTypeName ?? string.Empty;
                CanonicalIdentity = canonicalIdentity ?? string.Empty;
                PayloadIdentity = payloadIdentity ?? string.Empty;
                Members = members
                          ?? Array.Empty<BridgeDtoMemberShape>();
                IsValueType = isValueType;
            }

            internal string FullyQualifiedTypeName { get; }
            internal string CanonicalIdentity { get; }
            internal string PayloadIdentity { get; }
            internal IReadOnlyList<BridgeDtoMemberShape> Members { get; }
            internal bool IsValueType { get; }
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
