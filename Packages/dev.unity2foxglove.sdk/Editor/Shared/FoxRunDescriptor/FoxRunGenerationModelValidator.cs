// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent FoxRun generation-model diagnostics.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunGenerationModelValidator
    {
        private const string ConditionMissingDiagnosticId = "FOXRUN015";
        private const string ConditionNotBoolDiagnosticId = "FOXRUN016";
        private const string MixedConditionDiagnosticId = "FOXRUN017";
        private const string InvalidWireEncodingDiagnosticId = "FOXRUN602";
        private const string InvalidProtobufFieldNumberDiagnosticId = "FOXRUN603";
        private const string MixedWireEncodingDiagnosticId = "FOXRUN604";
        private const string DuplicateProtobufFieldNumberDiagnosticId = "FOXRUN605";
        private const string BidirectionalInheritedWireEncodingDiagnosticId = "FOXRUN401";
        private const string InvalidSubscriptionProviderDiagnosticId = "FOXRUN204";
        private const string NativeSubscribeDiagnosticId = "FOXRUN205";
        private const string NativeEncodingDiagnosticId = "FOXRUN206";
        private const string NativeProviderPublishDiagnosticId = "FOXRUN214";
        private const string CustomNativeBidirectionalContractDiagnosticId = "FOXRUN402";
        private const string Ros2SchemaMismatchDiagnosticId = "FOXRUN210";
        private const string IgnoredRos2QosDiagnosticId = "FOXRUN213";
        private const string TriggerRateConflictDiagnosticId = "FOXRUN609";

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

        public static IReadOnlyList<FoxRunGenerationDiagnostic> Validate(FoxRunGenerationModel model)
        {
            var diagnostics = new List<FoxRunGenerationDiagnostic>();
            foreach (var type in (model == null ? Array.Empty<FoxRunGenerationType>() : model.Types))
            {
                if (type.DeclaringType.IndexOf('<') >= 0 || type.DeclaringType.IndexOf('`') >= 0)
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", type.DeclaringType, "", "Generic FoxRun declaring types may be unsafe for IL2CPP contract governance."));

                foreach (var member in type.Members)
                    ValidateMember(member, diagnostics);

                ValidateTopicGroups(type, diagnostics);
            }
            return diagnostics;
        }

        private static void ValidateMember(FoxRunGenerationMember member, List<FoxRunGenerationDiagnostic> diagnostics)
        {
            var target = member.DeclaringType + "." + member.MemberName;
            var hasValidNativeCapability = HasValidNativeCapability(member);
            var hasTargetedNativeDiagnostics = HasTargetedNativeDiagnostics(member.Ros2MessageShape)
                                             || HasTargetedNativeDiagnostics(member.Ros2CustomDtoShape);
            var requiresWebSocketShapeValidation = RequiresWebSocketShapeValidation(
                member,
                hasValidNativeCapability);

            if (!member.GeneratesWebSocketCodec
                && !member.GeneratesRos2NativeRegistration
                && !(RequiresNativeShapeValidation(member) && hasTargetedNativeDiagnostics))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN006",
                    target,
                    member.MemberName,
                    "FoxRun member has no supported WebSocket codec or native ROS2 registration capability."));
            }

            if (member.GeneratesRos2NativeRegistration && !hasValidNativeCapability)
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN006",
                    target,
                    member.MemberName,
                    "FoxRun native ROS2 registration requires a validated host-neutral message-copy shape."));
            }

            if (string.IsNullOrWhiteSpace(member.ClassName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN011", target, member.MemberName, "FoxRun declaring class name is required."));

            if (string.IsNullOrWhiteSpace(member.MemberName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN012", target, member.MemberName, "FoxRun member name is required."));

            if (member.Policy != 1 && member.Policy != 2 && member.Policy != 4)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN013", target, member.MemberName, "FoxRun Policy must be FixedRate, Change, or Trigger."));

            if (member.Mode < 1 || member.Mode > 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN600", target, member.MemberName, "FoxRun Mode must be Publish, Subscribe, or PublishAndSubscribe."));

            if (member.Policy == 4 && member.HasExplicitHz)
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    TriggerRateConflictDiagnosticId,
                    target,
                    member.MemberName,
                    "FoxRun Trigger cannot be combined with an explicit Hz."));
            }

            if (!IsKnownDeclaredEncoding(member.Encoding))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(InvalidWireEncodingDiagnosticId, target, member.MemberName, "FoxRun Encoding must be inherit, json, or protobuf."));

            if (!IsKnownSubscriptionProvider(member.SubscriptionProvider))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    InvalidSubscriptionProviderDiagnosticId,
                    target,
                    member.MemberName,
                    "FoxRun SubscriptionProvider must be inherit, foxglove-websocket, or ros2-native."));
            }

            AppendNativeProviderDirectionDiagnostics(member, target, diagnostics);

            if (IsNativeProvider(member.SubscriptionProvider)
                && !string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal)
                && !AllowsNativeBidirectionalOutputEncoding(member))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    NativeEncodingDiagnosticId,
                    target,
                    member.MemberName,
                    "Ros2Native is a typed native subscription and cannot declare JSON or Protobuf Encoding."));
            }

            if (string.Equals(
                    member.SubscriptionProvider,
                    FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider,
                    StringComparison.Ordinal)
                && !string.Equals(member.Ros2Qos, FoxRunGenerationDescriptorConstants.InheritRos2Qos, StringComparison.Ordinal))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    IgnoredRos2QosDiagnosticId,
                    target,
                    member.MemberName,
                    "Ros2Qos is ignored for an explicitly Foxglove WebSocket-only subscription."));
            }

            AppendNativeShapeDiagnostics(member, target, diagnostics);

            if (IsNativeProvider(member.SubscriptionProvider)
                && member.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message
                && member.Ros2MessageShape != null
                && !string.IsNullOrWhiteSpace(member.SchemaName)
                && !string.IsNullOrWhiteSpace(member.Ros2MessageShape.CanonicalRosType)
                && !string.Equals(member.SchemaName, member.Ros2MessageShape.CanonicalRosType, StringComparison.Ordinal))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    Ros2SchemaMismatchDiagnosticId,
                    target,
                    member.MemberName,
                    "Explicit SchemaName '" + member.SchemaName + "' does not match validated ROS type '"
                    + member.Ros2MessageShape.CanonicalRosType + "'."));
            }

            if (requiresWebSocketShapeValidation
                && member.Mode == 3
                && !IsNativeCustomBidirectionalOutputContract(member)
                && string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    BidirectionalInheritedWireEncodingDiagnosticId,
                    target,
                    member.MemberName,
                    "PublishAndSubscribe requires an explicit Protobuf or Json Encoding because it has one shared bidirectional wire contract."));
            }

            if (requiresWebSocketShapeValidation && member.ProtobufFieldNumber != 0)
            {
                try
                {
                    FoxRunProtobufFieldNumber.Resolve(target, member.ProtobufFieldNumber);
                }
                catch (ArgumentOutOfRangeException)
                {
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        InvalidProtobufFieldNumberDiagnosticId,
                        target,
                        member.MemberName,
                        "FoxRun ProtobufFieldNumber must be 0 or a legal Protobuf field number outside 19000..19999."));
                }
            }

            if (requiresWebSocketShapeValidation
                && member.Mode != 1
                && (member.IsAggregateMember
                    || (member.IsArray
                        && !string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN200",
                    target,
                    member.MemberName,
                    "FoxRun inbound collections require explicit Protobuf encoding; aggregate members remain unsupported."));

            if (requiresWebSocketShapeValidation
                && member.Mode != 1
                && string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal)
                && member.ProtobufTypeShape != null
                && !IsInboundAssignable(member.ProtobufTypeShape))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN200",
                    target,
                    member.MemberName,
                    "FoxRun inbound Protobuf DTO members must be writable fields or settable properties."));
            }

            if (member.Mode == 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    "FOXRUN400",
                    target,
                    member.MemberName,
                    "PublishAndSubscribe exposes remote-authoritative state; document ownership and feedback behavior."));

            if (member.Mode == 2 && !LooksLikeInputPort(member.MemberName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    "FOXRUN202",
                    target,
                    member.MemberName,
                    "Subscribe members should use an input-port name such as _incoming, _input, _requested, _command, or _remote."));

            if (!IsKnownMemberKind(member.MemberKind))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN014", target, member.MemberName, "FoxRun member kind must be 'field' or 'property'."));

            if (member.HasExplicitOnlyIf
                && (string.IsNullOrWhiteSpace(member.OnlyIf)
                    || IsInvalidConditionName(member.OnlyIf)
                    || member.ConditionMemberKind == FoxRunConditionMemberKind.Missing))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    ConditionMissingDiagnosticId,
                    target,
                    member.MemberName,
                    "FoxRun OnlyIf condition member name is invalid or missing."));
            }
            else if (member.HasExplicitOnlyIf
                     && member.ConditionMemberKind == FoxRunConditionMemberKind.Invalid)
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    ConditionNotBoolDiagnosticId,
                    target,
                    member.MemberName,
                    "FoxRun OnlyIf must name a bool field, bool property, or zero-argument bool method."));
            }

            if (requiresWebSocketShapeValidation
                && !IsNativeCustomBidirectionalOutputContract(member)
                && !FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType)
                && (!string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal)
                    || member.ProtobufTypeShape == null))
            {
                var raw = member.RawObservedTypeName ?? string.Empty;
                var message = string.IsNullOrWhiteSpace(raw)
                    ? "FoxRun member has an empty type; the generator host produced no observed type name."
                    : IsUnityNativeContainerTypeName(raw)
                    ? "FoxRun member type '" + raw + "' is a Unity native container and is not supported "
                      + "as a FoxRun field; use a managed type instead."
                    : "FoxRun member type '" + raw + "' is not a canonical built-in contract type.";
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN006", target, member.MemberName, message));
            }

            if (requiresWebSocketShapeValidation && member.IsAggregateMember && member.IsArray)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN020",
                    target,
                    member.MemberName,
                    "FoxRun aggregate array fields are not supported yet; publish a scalar aggregate field or keep the array as a field-level topic."));

            if (requiresWebSocketShapeValidation && IsUnsupportedGenericMember(member))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", target, member.MemberName, "Generic FoxRun member type may be unsafe for IL2CPP contract governance."));

            if (string.IsNullOrEmpty(member.Topic) || !member.Topic.StartsWith("/", StringComparison.Ordinal))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN008", target, member.MemberName, "FoxRun topic must be absolute and start with '/'."));

            if (member.HasNonFiniteHz)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "Hz must be finite; use Trigger or a positive finite cadence."));
            if (member.HasNonFiniteTolerance)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "Tolerance must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));

            if (requiresWebSocketShapeValidation
                && (IsBinaryLike(member.RawObservedTypeName) || IsBinaryLike(member.EmissionTypeName) || IsBinaryLike(member.CanonicalType)
                    || (member.IsArray && member.CanonicalType == "uint8")))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN010", target, member.MemberName, "Binary/blob values are not supported in the FoxRun contract path."));
        }

        private static bool HasValidNativeCapability(FoxRunGenerationMember member)
        {
            if (!member.GeneratesRos2NativeRegistration)
                return false;

            return FoxRunRos2ContractCapability.IsNativeRegistrationCapable(
                member.Ros2MessageShape,
                member.Ros2CustomDtoShape);
        }

        private static bool HasTargetedNativeDiagnostics(FoxRunRos2MessageShape shape)
        {
            if (shape == null)
                return false;
            foreach (var value in shape.Diagnostics)
            {
                if (FoxRunRos2ShapeDiagnostic.TryDecode(value, out var id, out _, out _)
                    && id.StartsWith("FOXRUN", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasTargetedNativeDiagnostics(FoxRunRos2CustomDtoShape shape)
        {
            if (shape == null)
                return false;
            foreach (var value in shape.Diagnostics)
            {
                if (FoxRunRos2ShapeDiagnostic.TryDecode(value, out var id, out _, out _)
                    && id.StartsWith("FOXRUN", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AppendNativeShapeDiagnostics(
            FoxRunGenerationMember member,
            string target,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!RequiresNativeShapeValidation(member))
                return;

            var encodedDiagnostics = member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
                ? member.Ros2CustomDtoShape?.Diagnostics
                : member.Ros2MessageShape?.Diagnostics;
            if (encodedDiagnostics == null)
                return;
            foreach (var encoded in encodedDiagnostics)
            {
                if (!FoxRunRos2ShapeDiagnostic.TryDecode(encoded, out var id, out var path, out var message))
                    continue;
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    id,
                    target,
                    member.MemberName,
                    (string.IsNullOrEmpty(path) ? target : path) + ": " + message));
            }
        }

        private static bool RequiresNativeShapeValidation(FoxRunGenerationMember member)
        {
            // Phase181 builds a custom DTO shape for every ordinary DTO so a
            // Publish contract can later participate in the Manager-owned
            // native output route.  That output capability must not turn an
            // inherited subscription declaration into an explicit native-input
            // contract, or a normal unsupported WebSocket field would acquire
            // a second, unrelated custom-ROS diagnostic.  Keep the Phase179
            // packaged-message behavior: an inherited contract with no
            // WebSocket codec can still resolve only through native input and
            // must surface its packaged shape failure.  A custom DTO keeps
            // targeted native diagnostics until its provider is explicit.
            return IsNativeProvider(member?.SubscriptionProvider)
                   || (member?.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message
                       && string.Equals(
                           member.SubscriptionProvider,
                           FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                           StringComparison.Ordinal)
                       && !member.GeneratesWebSocketCodec);
        }

        private static bool RequiresWebSocketShapeValidation(
            FoxRunGenerationMember member,
            bool hasValidNativeCapability)
        {
            if (string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                StringComparison.Ordinal))
            {
                return AllowsNativeBidirectionalOutputEncoding(member)
                       && member.GeneratesWebSocketCodec;
            }

            if (string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                StringComparison.Ordinal))
            {
                // Publish has no inbound provider to resolve.  A valid
                // custom interface may therefore be selected solely by the
                // Manager's native output route, even when its ordinary DTO
                // shape is not a canonical WebSocket field shape.  Keep the
                // existing validation for every inbound/P&S declaration: an
                // inherited provider there can still resolve to WebSocket.
                if (IsNativeCustomPublishOutputContract(member, hasValidNativeCapability))
                    return false;

                return member.GeneratesWebSocketCodec || !hasValidNativeCapability;
            }

            return string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider,
                StringComparison.Ordinal);
        }

        private static void AppendNativeProviderDirectionDiagnostics(
            FoxRunGenerationMember member,
            string target,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!IsNativeProvider(member.SubscriptionProvider))
                return;

            if (member.Mode == 1)
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    NativeProviderPublishDiagnosticId,
                    target,
                    member.MemberName,
                    "Ros2Native is an inbound SubscriptionProvider and is invalid for Publish. Configure ROS2 Native output on the Manager instead."));
                return;
            }

            if (member.Mode != 3)
                return;

            if (member.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message)
            {
                // FOXRUN205 is shipped Phase179 behavior. It remains limited to
                // real packaged ROS2.Message contracts and is never repurposed
                // for a Phase181 custom interface failure.
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    NativeSubscribeDiagnosticId,
                    target,
                    member.MemberName,
                    "Ros2Native subscriptions are supported only for Subscribe members."));
                return;
            }

            if (!HasCompleteCustomBidirectionalContract(member))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    CustomNativeBidirectionalContractDiagnosticId,
                    target,
                    member.MemberName,
                    "Native PublishAndSubscribe requires a supported CustomDto with complete static canonical and payload identities; it never falls back to WebSocket input."));
            }
        }

        private static bool AllowsNativeBidirectionalOutputEncoding(FoxRunGenerationMember member)
            => member != null
               && member.Mode == 3
               && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto;

        private static bool IsNativeCustomBidirectionalOutputContract(FoxRunGenerationMember member)
            => IsNativeProvider(member?.SubscriptionProvider)
               && AllowsNativeBidirectionalOutputEncoding(member);

        private static bool IsNativeCustomPublishOutputContract(
            FoxRunGenerationMember member,
            bool hasValidNativeCapability)
            => hasValidNativeCapability
               && member != null
               && member.Mode == 1
               && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto;

        private static bool HasCompleteCustomBidirectionalContract(FoxRunGenerationMember member)
        {
            var shape = member?.Ros2CustomDtoShape;
            return shape != null
                   && shape.IsSupported
                   && shape.HasPublicParameterlessConstructor
                   && shape.Diagnostics.Count == 0
                   && !string.IsNullOrWhiteSpace(shape.CanonicalIdentity)
                   && !string.IsNullOrWhiteSpace(shape.PayloadIdentity);
        }

        private static void ValidateTopicGroups(FoxRunGenerationType type, List<FoxRunGenerationDiagnostic> diagnostics)
        {
            var byTopic = type.Members
                .Where(member => !string.IsNullOrEmpty(member.Topic))
                .GroupBy(member => member.Topic, StringComparer.Ordinal);

            foreach (var group in byTopic)
            {
                var members = group.ToList();
                var schemas = members
                    .Select(member => member.SchemaName)
                    .Where(schema => !string.IsNullOrEmpty(schema))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (schemas.Count > 1)
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN002",
                        group.Key,
                        "",
                        "Topic has conflicting SchemaName values across FoxRun members."));

                var encodings = members
                    .Select(member => member.Encoding)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (encodings.Count > 1)
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        MixedWireEncodingDiagnosticId,
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed Encoding declarations. Use one policy for every member on the topic."));
                }

                var duplicateProtobufTag = members
                    .Where(member => member.ProtobufFieldNumber > 0)
                    .GroupBy(member => member.ProtobufFieldNumber)
                    .FirstOrDefault(tags => tags.Count() > 1);
                if (duplicateProtobufTag != null)
                {
                    var duplicate = duplicateProtobufTag.Skip(1).First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        DuplicateProtobufFieldNumberDiagnosticId,
                        duplicate.DeclaringType + "." + duplicate.MemberName,
                        duplicate.MemberName,
                        "FoxRun topic '" + group.Key + "' has duplicate ProtobufFieldNumber " + duplicateProtobufTag.Key + "."));
                }

                if (members.Any(member => member.IsAggregateMember) && members.Any(member => !member.IsAggregateMember))
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        "FOXRUN019",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' cannot mix FoxRunMessage aggregate fields with field-level FoxRun members."));
                }

                var duplicateJsonName = members
                    .Where(member => member.IsAggregateMember)
                    .GroupBy(member => member.JsonFieldName, StringComparer.Ordinal)
                    .FirstOrDefault(names => names.Count() > 1);
                if (duplicateJsonName != null)
                {
                    var first = duplicateJsonName.First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        "FOXRUN022",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "FoxRun aggregate topic '" + group.Key + "' has duplicate JSON field name '" + duplicateJsonName.Key + "'."));
                }

                var collision = members
                    .GroupBy(member => member.MemberName.TrimStart('_'), StringComparer.Ordinal)
                    .FirstOrDefault(names => names.Count() > 1);
                if (collision != null)
                {
                    var first = collision.First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN003",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "FoxRun member names collide after stripping leading underscores for topic '" + group.Key + "'."));
                }

                var mixedPolicy = members.Select(member => member.Policy).Distinct().Count() > 1
                    || members.Select(member => member.Hz).Distinct().Count() > 1
                    || members.Select(member => member.Tolerance).Distinct().Count() > 1;
                if (mixedPolicy)
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN005",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed Policy, Hz, or Tolerance values."));
                }

                var mixedConditions = members.Select(member => member.OnlyIf).Distinct(StringComparer.Ordinal).Count() > 1;
                if (mixedConditions)
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        MixedConditionDiagnosticId,
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed OnlyIf values."));
                }
            }
        }

        private static bool IsInvalidConditionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();
            if (value.Length == 0)
                return true;

            if (!IsIdentifierStart(value[0]))
                return true;
            for (var i = 1; i < value.Length; i++)
            {
                if (!IsIdentifierPart(value[i]))
                    return true;
            }

            return false;
        }

        private static bool IsKnownDeclaredEncoding(string encoding)
        {
            return string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal)
                   || string.Equals(encoding, FoxRunGenerationDescriptorConstants.JsonEncoding, StringComparison.Ordinal)
                   || string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal);
        }

        private static bool IsKnownSubscriptionProvider(string provider)
            => string.Equals(provider, FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider, StringComparison.Ordinal)
               || string.Equals(provider, FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider, StringComparison.Ordinal)
               || IsNativeProvider(provider);

        private static bool IsNativeProvider(string provider)
            => string.Equals(provider, FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider, StringComparison.Ordinal);

        private static bool IsInboundAssignable(FoxRunProtobufTypeShape shape)
        {
            if (shape == null || shape.Kind != FoxRunProtobufTypeShapeKind.Object)
                return true;

            foreach (var field in shape.Fields)
            {
                if (!field.CanAssign || !IsInboundAssignable(field.TypeShape))
                    return false;
            }

            return true;
        }

        private static bool LooksLikeInputPort(string memberName)
        {
            var value = (memberName ?? string.Empty).TrimStart('_').ToLowerInvariant();
            return value.StartsWith("incoming", StringComparison.Ordinal)
                   || value.StartsWith("input", StringComparison.Ordinal)
                   || value.StartsWith("requested", StringComparison.Ordinal)
                   || value.StartsWith("command", StringComparison.Ordinal)
                   || value.StartsWith("remote", StringComparison.Ordinal);
        }

        private static bool IsIdentifierStart(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        private static bool IsIdentifierPart(char ch)
        {
            return ch == '_' || char.IsLetterOrDigit(ch);
        }

        private static bool IsUnsupportedGenericMember(FoxRunGenerationMember member)
        {
            if (IsSupportedNullableMember(member))
                return false;

            var looksGeneric = member.EmissionTypeName.IndexOf('<') >= 0
                               || member.RawObservedTypeName.IndexOf('`') >= 0;
            if (!looksGeneric)
                return false;

            return !member.IsArray || !FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType);
        }

        private static bool IsSupportedNullableMember(FoxRunGenerationMember member)
        {
            if (!FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType))
                return false;

            return FoxRunCanonicalTypeNormalizer.IsNullableType(member.EmissionTypeName)
                   || FoxRunCanonicalTypeNormalizer.IsNullableType(member.RawObservedTypeName);
        }

        private static bool IsBinaryLike(string typeName)
        {
            var name = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(typeName);
            return name == "byte[]"
                   || name == "System.Byte[]"
                   || name == "uint8[]"
                   || name.IndexOf("System.IO.Stream", StringComparison.Ordinal) >= 0
                   || name.IndexOf("Memory<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("ReadOnlyMemory<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("Span<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("ReadOnlySpan<byte>", StringComparison.Ordinal) >= 0;
        }

        private static bool IsKnownMemberKind(string memberKind)
        {
            return string.Equals(memberKind, "field", StringComparison.Ordinal)
                   || string.Equals(memberKind, "property", StringComparison.Ordinal);
        }

        private static bool IsUnityNativeContainerTypeName(string rawTypeName)
        {
            if (string.IsNullOrEmpty(rawTypeName))
                return false;

            foreach (var prefix in UnityNativeContainerPrefixes)
            {
                if (rawTypeName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    public sealed class FoxRunGenerationDiagnostic
    {
        public readonly string Id;
        public readonly string Severity;
        public readonly string Target;
        public readonly string MemberName;
        public readonly string Message;

        private FoxRunGenerationDiagnostic(string id, string severity, string target, string memberName, string message)
        {
            Id = id ?? string.Empty;
            Severity = severity ?? string.Empty;
            Target = target ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public static FoxRunGenerationDiagnostic Warning(string id, string target, string memberName, string message)
        {
            return new FoxRunGenerationDiagnostic(id, "Warning", target, memberName, message);
        }

        public static FoxRunGenerationDiagnostic Error(string id, string target, string memberName, string message)
        {
            return new FoxRunGenerationDiagnostic(id, "Error", target, memberName, message);
        }
    }
}
