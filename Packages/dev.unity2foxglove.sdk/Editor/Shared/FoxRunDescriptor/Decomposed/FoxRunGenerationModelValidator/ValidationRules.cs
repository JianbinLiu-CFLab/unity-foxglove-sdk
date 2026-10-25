// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent validation for the Provider-neutral FoxRun model.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxRunGenerationModelValidator
    {
        private const string InvalidEncodingId = "FOXRUN602";
        private const string InvalidFieldNumberId = "FOXRUN603";
        private const string MixedEncodingId = "FOXRUN604";
        private const string DuplicateFieldNumberId = "FOXRUN605";
        private const string TriggerRateConflictId = "FOXRUN609";
        private const string UnsupportedMessagePackShapeId = "FOXRUN616";
        private const string MessagePackFieldNumberId = "FOXRUN617";
        private const string MessagePackInboundTopologyId = "FOXRUN618";
        private const string MessagePackScheduleId = "FOXRUN619";
        private const string InvalidTransportSelectionId = "FOXRUN620";
        private const string InvalidDirectionalTransportId = "FOXRUN621";
        private const string InvalidDeliveryPolicyId = "FOXRUN622";
        private const string InvalidStreamId = "FOXRUN215";

        private static readonly string[] UnityNativeContainerPrefixes =
        {
            "NativeArray<",
            "NativeList<",
            "NativeHashMap<",
            "NativeMultiHashMap<",
            "NativeParallelHashMap<",
            "NativeParallelMultiHashMap<",
            "NativeSlice<",
            "NativeQueue<",
            "NativeReference<",
            "NativeText<",
            "Unity.Collections.NativeArray<",
            "Unity.Collections.NativeList<",
            "Unity.Collections.NativeHashMap<",
            "Unity.Collections.NativeMultiHashMap<",
            "Unity.Collections.NativeParallelHashMap<",
            "Unity.Collections.NativeParallelMultiHashMap<",
            "Unity.Collections.NativeSlice<",
            "Unity.Collections.NativeQueue<",
            "Unity.Collections.NativeReference<",
            "Unity.Collections.NativeText<"
        };

        public static IReadOnlyList<FoxRunGenerationDiagnostic> Validate(
            FoxRunGenerationModel model)
        {
            var diagnostics = new List<FoxRunGenerationDiagnostic>();
            foreach (var type in model?.Types
                                 ?? Array.Empty<FoxRunGenerationType>())
            {
                if (type.DeclaringType.IndexOf('<') >= 0
                    || type.DeclaringType.IndexOf('`') >= 0)
                {
                    diagnostics.Add(
                        FoxRunGenerationDiagnostic.Warning(
                            "FOXRUN007",
                            type.DeclaringType,
                            string.Empty,
                            "Generic FoxRun declaring types may be unsafe for generated contract governance."));
                }

                foreach (var member in type.Members)
                    ValidateMember(member, diagnostics);

                foreach (var streamGroup in type.Members
                             .Where(member => member.IsStream)
                             .GroupBy(
                                 member => member.MemberName,
                                 StringComparer.Ordinal)
                             .Where(group => group.Count() != 1))
                {
                    var first = streamGroup.First();
                    AddError(
                        diagnostics,
                        InvalidStreamId,
                        first,
                        "FoxRunStream<T> fields require exactly one FoxRun declaration.");
                }

                ValidateTopicGroups(type, diagnostics);
            }

            return diagnostics;
        }

        private static void ValidateMember(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (member == null)
                return;

            if (string.IsNullOrWhiteSpace(member.ClassName))
                AddError(diagnostics, "FOXRUN011", member, "FoxRun declaring class name is required.");
            if (string.IsNullOrWhiteSpace(member.MemberName))
                AddError(diagnostics, "FOXRUN012", member, "FoxRun member name is required.");
            if (string.IsNullOrWhiteSpace(member.Topic))
                AddError(diagnostics, "FOXRUN008", member, "FoxRun topic is required.");
            else if (!member.Topic.StartsWith("/", StringComparison.Ordinal))
                AddError(diagnostics, "FOXRUN008", member, "FoxRun topic must be absolute and start with '/'.");
            if (member.Policy != 1 && member.Policy != 2 && member.Policy != 4)
                AddError(diagnostics, "FOXRUN013", member, "FoxRun Policy must be FixedRate, Change, or Trigger.");
            if (member.Mode < 1 || member.Mode > 3)
                AddError(diagnostics, "FOXRUN600", member, "FoxRun Mode must be Publish, Subscribe, or PublishAndSubscribe.");
            if (!IsKnownMemberKind(member.MemberKind))
                AddError(diagnostics, "FOXRUN014", member, "FoxRun member kind must be 'field' or 'property'.");

            if (member.Policy == 4 && member.HasExplicitHz)
                AddError(diagnostics, TriggerRateConflictId, member, "FoxRun Trigger cannot be combined with an explicit Hz.");

            ValidateCondition(member, diagnostics);
            ValidateStream(member, diagnostics);
            ValidateTransportSelection(member, diagnostics);
            ValidateDeliveryPolicy(member, diagnostics);
            ValidateEncoding(member, diagnostics);
            ValidateTypeShape(member, diagnostics);

            if (member.HasNonFiniteHz)
            {
                diagnostics.Add(
                    FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN009",
                        Target(member),
                        member.MemberName,
                        "Hz must be finite; use Trigger or a positive finite cadence."));
            }
            if (member.HasNonFiniteTolerance)
            {
                diagnostics.Add(
                    FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN009",
                        Target(member),
                        member.MemberName,
                        "Tolerance must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));
            }

            if (member.Mode == 2 && !LooksLikeInputPort(member.MemberName))
            {
                diagnostics.Add(
                    FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN202",
                        Target(member),
                        member.MemberName,
                        "Subscribe members should use an input-port name such as _incoming, _input, _requested, _command, or _remote."));
            }
        }

        private static void ValidateCondition(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!member.HasExplicitOnlyIf)
                return;

            if (string.IsNullOrWhiteSpace(member.OnlyIf)
                || IsInvalidConditionName(member.OnlyIf)
                || member.ConditionMemberKind
                == FoxRunConditionMemberKind.Missing)
            {
                AddError(
                    diagnostics,
                    "FOXRUN015",
                    member,
                    "FoxRun OnlyIf condition member name is invalid or missing.");
            }
            else if (member.ConditionMemberKind
                     == FoxRunConditionMemberKind.Invalid)
            {
                AddError(
                    diagnostics,
                    "FOXRUN016",
                    member,
                    "FoxRun OnlyIf must name a bool field, bool property, or zero-argument bool method.");
            }
        }

        private static void ValidateStream(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            const FoxRunNamedArgumentPresence forbidden =
                FoxRunNamedArgumentPresence.PublishTransportIds
                | FoxRunNamedArgumentPresence.Policy
                | FoxRunNamedArgumentPresence.Hz
                | FoxRunNamedArgumentPresence.Tolerance
                | FoxRunNamedArgumentPresence.OnlyIf;
            if (member.IsStream
                && (member.Mode != 2
                    || !string.Equals(
                        member.MemberKind,
                        "field",
                        StringComparison.Ordinal)
                    || member.IsAggregateMember
                    || (member.NamedArgumentPresence & forbidden) != 0))
            {
                AddError(
                    diagnostics,
                    InvalidStreamId,
                    member,
                    "FoxRunStream<T> must be a Subscribe field without ordinary scheduling or publish-transport arguments.");
            }
        }

        private static void ValidateTransportSelection(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            var publishes = member.Mode == 1 || member.Mode == 3;
            var subscribes = member.Mode == 2 || member.Mode == 3;
            var explicitPublish = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.PublishTransportIds);
            var explicitSubscribe = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.SubscribeTransportId);

            if (explicitPublish && !publishes)
            {
                AddError(
                    diagnostics,
                    InvalidDirectionalTransportId,
                    member,
                    "PublishTransportIds is valid only for Publish or PublishAndSubscribe.");
            }
            if (explicitSubscribe && !subscribes)
            {
                AddError(
                    diagnostics,
                    InvalidDirectionalTransportId,
                    member,
                    "SubscribeTransportId is valid only for Subscribe or PublishAndSubscribe.");
            }

            if (explicitPublish
                && (member.PublishTransportIds == null
                    || member.PublishTransportIds.Count == 0
                    || member.PublishTransportIds.Any(
                        string.IsNullOrWhiteSpace)
                    || member.PublishTransportIds.Distinct(
                            StringComparer.Ordinal)
                        .Count()
                       != member.PublishTransportIds.Count))
            {
                AddError(
                    diagnostics,
                    InvalidTransportSelectionId,
                    member,
                    "PublishTransportIds must be a non-empty set of unique non-blank Provider IDs.");
            }

            if (explicitSubscribe
                && string.IsNullOrWhiteSpace(
                    member.SubscribeTransportId))
            {
                AddError(
                    diagnostics,
                    InvalidTransportSelectionId,
                    member,
                    "SubscribeTransportId must be one non-blank Provider ID.");
            }

            if (member.PublishTransportIds != null)
            {
                foreach (var id in member.PublishTransportIds)
                    ValidateTransportId(id, member, diagnostics);
            }
            if (!string.IsNullOrWhiteSpace(member.SubscribeTransportId))
            {
                ValidateTransportId(
                    member.SubscribeTransportId,
                    member,
                    diagnostics);
            }
        }

        private static void ValidateTransportId(
            string id,
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!IsCanonicalTransportId(id))
            {
                AddError(
                    diagnostics,
                    InvalidTransportSelectionId,
                    member,
                    "FoxRun Provider IDs must use canonical dotted lowercase identifiers.");
            }
        }

        private static bool IsCanonicalTransportId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 128
                || !string.Equals(
                    value,
                    value.Trim(),
                    StringComparison.Ordinal))
            {
                return false;
            }

            var segmentCount = 1;
            var segmentLength = 0;
            var previous = '\0';
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current == '.')
                {
                    if (segmentLength == 0 || previous == '-')
                        return false;
                    segmentCount++;
                    segmentLength = 0;
                    previous = current;
                    continue;
                }

                var valid =
                    current >= 'a' && current <= 'z'
                    || current >= '0' && current <= '9'
                    || current == '-';
                if (!valid
                    || (segmentLength == 0 && current == '-'))
                {
                    return false;
                }

                segmentLength++;
                previous = current;
            }

            return segmentCount >= 2
                   && segmentLength > 0
                   && previous != '-';
        }

        private static void ValidateDeliveryPolicy(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            var hasReliability = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.Reliability);
            var hasDurability = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.Durability);
            var hasHistory = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.History);
            var hasDepth = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.Depth);
            if (!KnownAxis(
                    member.Reliability,
                    hasReliability,
                    "reliable",
                    "best-effort")
                || !KnownAxis(
                    member.Durability,
                    hasDurability,
                    "volatile",
                    "transient-local")
                || !KnownAxis(
                    member.History,
                    hasHistory,
                    "keep-last",
                    "keep-all")
                || (hasDepth && member.Depth <= 0)
                || (hasDepth
                    && hasHistory
                    && !string.Equals(
                        member.History,
                        "keep-last",
                        StringComparison.Ordinal)))
            {
                AddError(
                    diagnostics,
                    InvalidDeliveryPolicyId,
                    member,
                    "FoxRun delivery policy contains an invalid axis or depth.");
            }
        }

        private static bool KnownAxis(
            string value,
            bool isExplicit,
            string first,
            string second)
            => isExplicit
                ? string.Equals(value, "provider-default", StringComparison.Ordinal)
                  || string.Equals(value, first, StringComparison.Ordinal)
                  || string.Equals(value, second, StringComparison.Ordinal)
                  || string.Equals(value, "system-default", StringComparison.Ordinal)
                : string.Equals(value, "inherit", StringComparison.Ordinal);

        private static void ValidateEncoding(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            var explicitEncoding = member.HasNamedArgument(
                FoxRunNamedArgumentPresence.Encoding);
            if (explicitEncoding
                ? !string.Equals(member.Encoding, "protobuf", StringComparison.Ordinal)
                  && !string.Equals(member.Encoding, "json", StringComparison.Ordinal)
                  && !string.Equals(member.Encoding, "msgpack", StringComparison.Ordinal)
                : !string.Equals(member.Encoding, "inherit", StringComparison.Ordinal))
            {
                AddError(
                    diagnostics,
                    InvalidEncodingId,
                    member,
                    "FoxRun Encoding must be omitted, Protobuf, JSON, or MessagePack.");
            }

            if (explicitEncoding && !CanEncodingReachWebSocket(member))
            {
                AddError(
                    diagnostics,
                    InvalidDirectionalTransportId,
                    member,
                    "FoxRun Encoding applies only to the foxglove.websocket Provider.");
            }

            var fieldNumber = member.ProtobufMetadata?.FieldNumber ?? 0;
            if (explicitEncoding
                && string.Equals(
                    member.Encoding,
                    "msgpack",
                    StringComparison.Ordinal)
                && fieldNumber != 0)
            {
                AddError(
                    diagnostics,
                    MessagePackFieldNumberId,
                    member,
                    "FoxRun ProtobufFieldNumber is Protobuf-only metadata and cannot be combined with MessagePack.");
            }
            else if (fieldNumber != 0)
            {
                try
                {
                    FoxRunProtobufFieldNumber.Resolve(
                        Target(member),
                        fieldNumber);
                }
                catch (ArgumentOutOfRangeException)
                {
                    AddError(
                        diagnostics,
                        InvalidFieldNumberId,
                        member,
                        "FoxRun ProtobufFieldNumber must be legal and outside the reserved range.");
                }
            }
        }

        private static bool CanEncodingReachWebSocket(
            FoxRunGenerationMember member)
        {
            var publishes = member.Mode == 1 || member.Mode == 3;
            var subscribes = member.Mode == 2 || member.Mode == 3;
            var publish = publishes
                          && (!member.HasNamedArgument(
                                  FoxRunNamedArgumentPresence.PublishTransportIds)
                              || member.PublishTransportIds.Contains(
                                  FoxRunGenerationDescriptorConstants
                                      .FoxgloveWebSocketTransportId,
                                  StringComparer.Ordinal));
            var subscribe = subscribes
                            && (!member.HasNamedArgument(
                                    FoxRunNamedArgumentPresence.SubscribeTransportId)
                                || string.Equals(
                                    member.SubscribeTransportId,
                                    FoxRunGenerationDescriptorConstants
                                        .FoxgloveWebSocketTransportId,
                                    StringComparison.Ordinal));
            return publish || subscribe;
        }

        private static void ValidateTypeShape(
            FoxRunGenerationMember member,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!member.GeneratesWebSocketCodec)
                return;

            var explicitMessagePack =
                member.HasNamedArgument(
                    FoxRunNamedArgumentPresence.Encoding)
                && string.Equals(
                    member.Encoding,
                    "msgpack",
                    StringComparison.Ordinal);
            if (explicitMessagePack
                && (((member.Mode == 1 || member.Mode == 3)
                     && !FoxRunMessagePackTypeShapeRules
                         .IsPublishSupported(
                             member.TypeShape,
                             member.CanonicalType))
                    || ((member.Mode == 2 || member.Mode == 3)
                        && !FoxRunMessagePackTypeShapeRules
                            .IsSubscribeSupported(
                                member.TypeShape,
                                member.CanonicalType))))
            {
                AddError(
                    diagnostics,
                    UnsupportedMessagePackShapeId,
                    member,
                    "FoxRun typed MessagePack requires a bounded readable shape; Subscribe additionally requires constructible writable objects.");
            }

            if (!FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(
                    member.CanonicalType)
                && (member.TypeShape == null
                    || (!string.Equals(
                            member.Encoding,
                            FoxRunGenerationDescriptorConstants
                                .ProtobufEncoding,
                            StringComparison.Ordinal)
                        && member.TypeShape.Kind
                        != FoxRunTypeShapeKind.Object
                        && member.TypeShape.Kind
                        != FoxRunTypeShapeKind.Enum
                        && member.TypeShape.Kind
                        != FoxRunTypeShapeKind.Collection)))
            {
                var raw = member.RawObservedTypeName
                          ?? string.Empty;
                var message = string.IsNullOrWhiteSpace(raw)
                    ? "FoxRun member has an empty type; the generator host produced no observed type name."
                    : IsUnityNativeContainerTypeName(raw)
                        ? "FoxRun member type '" + raw
                          + "' is a Unity native container and is not supported as a FoxRun field; use a managed type instead."
                        : "FoxRun member type '" + raw
                          + "' is not a canonical built-in contract type.";
                AddError(
                    diagnostics,
                    "FOXRUN006",
                    member,
                    message);
            }

            if (member.Mode != 1
                && (member.IsAggregateMember
                    || (member.IsArray
                        && !string.Equals(
                            member.Encoding,
                            FoxRunGenerationDescriptorConstants
                                .ProtobufEncoding,
                            StringComparison.Ordinal)
                        && !string.Equals(
                            member.Encoding,
                            FoxRunGenerationDescriptorConstants
                                .MessagePackEncoding,
                            StringComparison.Ordinal))))
            {
                AddError(
                    diagnostics,
                    "FOXRUN200",
                    member,
                    "FoxRun inbound collections require explicit Protobuf or MessagePack encoding; aggregate members remain unsupported.");
            }

            if (member.Mode != 1
                && member.TypeShape != null
                && !IsInboundAssignable(member.TypeShape))
            {
                AddError(
                    diagnostics,
                    "FOXRUN200",
                    member,
                    "FoxRun inbound object members must be writable fields or settable properties.");
            }

            if (member.IsAggregateMember
                && member.IsArray)
            {
                AddError(
                    diagnostics,
                    "FOXRUN020",
                    member,
                    "FoxRun aggregate array fields are not supported yet; publish a scalar aggregate field or keep the array as a field-level topic.");
            }

            if (IsUnsupportedGenericMember(member))
            {
                diagnostics.Add(
                    FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN007",
                        Target(member),
                        member.MemberName,
                        "Generic FoxRun member type may be unsafe for IL2CPP contract governance."));
            }

            if (!string.Equals(
                    member.Encoding,
                    FoxRunGenerationDescriptorConstants
                        .MessagePackEncoding,
                    StringComparison.Ordinal)
                && (IsBinaryLike(member.RawObservedTypeName)
                    || IsBinaryLike(member.EmissionTypeName)
                    || IsBinaryLike(member.CanonicalType)
                    || (member.IsArray
                        && string.Equals(
                            member.CanonicalType,
                            "uint8",
                            StringComparison.Ordinal))))
            {
                diagnostics.Add(
                    FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN010",
                        Target(member),
                        member.MemberName,
                        "Binary/blob values are not supported in the FoxRun contract path."));
            }

            if (IsUnityNativeContainerTypeName(
                    member.RawObservedTypeName))
            {
                AddError(
                    diagnostics,
                    "FOXRUN006",
                    member,
                    "FoxRun does not support native container members in generated wire contracts.");
            }
        }

    }
}
