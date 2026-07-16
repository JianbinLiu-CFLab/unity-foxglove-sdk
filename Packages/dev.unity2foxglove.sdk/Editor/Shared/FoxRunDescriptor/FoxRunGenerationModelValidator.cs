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
        private const string UnlessConditionMissingDiagnosticId = "FOXRUN601";
        private const string InvalidWireEncodingDiagnosticId = "FOXRUN602";
        private const string InvalidProtobufFieldNumberDiagnosticId = "FOXRUN603";
        private const string MixedWireEncodingDiagnosticId = "FOXRUN604";
        private const string DuplicateProtobufFieldNumberDiagnosticId = "FOXRUN605";
        private const string BidirectionalInheritedWireEncodingDiagnosticId = "FOXRUN401";
        private const string InvalidSubscriptionProviderDiagnosticId = "FOXRUN204";
        private const string NativeSubscribeOnlyDiagnosticId = "FOXRUN205";
        private const string NativeEncodingDiagnosticId = "FOXRUN206";
        private const string Ros2SchemaMismatchDiagnosticId = "FOXRUN210";
        private const string IgnoredRos2QosDiagnosticId = "FOXRUN213";
        private const float DefaultRateHz = 10f;

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
            var hasTargetedNativeDiagnostics = HasTargetedNativeDiagnostics(member.Ros2MessageShape);
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

            if (member.PublishMode < 0 || member.PublishMode > 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN013", target, member.MemberName, "FoxRun publish mode must be between 0 and 3."));

            if (member.Mode < 0 || member.Mode > 2)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN600", target, member.MemberName, "FoxRun mode must be PublishOnly, SubscribeOnly, or PublishAndSubscribe."));

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

            if (IsNativeProvider(member.SubscriptionProvider) && member.Mode != 1)
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    NativeSubscribeOnlyDiagnosticId,
                    target,
                    member.MemberName,
                    "Ros2Native subscriptions are supported only for SubscribeOnly members."));
            }

            if (IsNativeProvider(member.SubscriptionProvider)
                && !string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal))
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
                && member.Mode == 2
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
                && member.Mode != 0
                && (member.IsAggregateMember
                    || (member.IsArray
                        && !string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                    "FOXRUN200",
                    target,
                    member.MemberName,
                    "FoxRun inbound collections require explicit Protobuf encoding; aggregate members remain unsupported."));

            if (requiresWebSocketShapeValidation
                && member.Mode != 0
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

            if (member.Mode == 1
                && (member.PublishMode != 0
                    || member.ChangeEpsilon > 0f
                    || member.ForceIntervalSeconds > 0f
                    || member.RateHz != DefaultRateHz))
            {
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    "FOXRUN201",
                    target,
                    member.MemberName,
                    "SubscribeOnly ignores RateHz, PublishMode, ChangeEpsilon, and ForceIntervalSeconds."));
            }

            if (member.Mode == 2)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    "FOXRUN400",
                    target,
                    member.MemberName,
                    "PublishAndSubscribe exposes remote-authoritative state; document ownership and feedback behavior."));

            if (member.Mode == 1 && !LooksLikeInputPort(member.MemberName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                    "FOXRUN202",
                    target,
                    member.MemberName,
                    "SubscribeOnly members should use an input-port name such as _incoming, _input, _requested, _command, or _remote."));

            if (!IsKnownMemberKind(member.MemberKind))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN014", target, member.MemberName, "FoxRun member kind must be 'field' or 'property'."));

            if (IsInvalidConditionName(member.When))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(ConditionMissingDiagnosticId, target, member.MemberName, "FoxRun When condition member name is invalid or missing."));

            if (IsInvalidConditionName(member.Unless))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error(UnlessConditionMissingDiagnosticId, target, member.MemberName, "FoxRun Unless condition member name is invalid or missing."));

            if (requiresWebSocketShapeValidation
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

            if (member.HasNonFiniteRateHz)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "RateHz must be finite; use OnTrigger or a positive finite rate for periodic output."));
            else if (member.RateHz <= 0f && member.PublishMode != 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "RateHz <= 0 disables scheduled publishing; use OnTrigger or a positive rate for periodic output."));

            if (member.HasNonFiniteChangeEpsilon)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "ChangeEpsilon must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));

            if (member.HasNonFiniteForceIntervalSeconds)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "ForceIntervalSeconds must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));

            if (requiresWebSocketShapeValidation
                && (IsBinaryLike(member.RawObservedTypeName) || IsBinaryLike(member.EmissionTypeName) || IsBinaryLike(member.CanonicalType)
                    || (member.IsArray && member.CanonicalType == "uint8")))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN010", target, member.MemberName, "Binary/blob values are not supported in the FoxRun contract path."));
        }

        private static bool HasValidNativeCapability(FoxRunGenerationMember member)
        {
            var shape = member.Ros2MessageShape;
            return member.GeneratesRos2NativeRegistration
                && shape != null
                && shape.HasPublicParameterlessConstructor
                && shape.ImplementsRos2Message
                && shape.Diagnostics.Count == 0;
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

        private static void AppendNativeShapeDiagnostics(
            FoxRunGenerationMember member,
            string target,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (!RequiresNativeShapeValidation(member)
                || member.Ros2MessageShape == null)
                return;
            foreach (var encoded in member.Ros2MessageShape.Diagnostics)
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
            => IsNativeProvider(member.SubscriptionProvider)
               || (string.Equals(
                       member.SubscriptionProvider,
                       FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                       StringComparison.Ordinal)
                   && !member.GeneratesWebSocketCodec);

        private static bool RequiresWebSocketShapeValidation(
            FoxRunGenerationMember member,
            bool hasValidNativeCapability)
        {
            if (string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                StringComparison.Ordinal))
            {
                return member.GeneratesWebSocketCodec || !hasValidNativeCapability;
            }

            return string.Equals(
                member.SubscriptionProvider,
                FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider,
                StringComparison.Ordinal);
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

                var mixedPolicy = members.Select(member => member.PublishMode).Distinct().Count() > 1
                    || members.Select(member => member.ChangeEpsilon).Distinct().Count() > 1
                    || members.Select(member => member.ForceIntervalSeconds).Distinct().Count() > 1;
                if (mixedPolicy)
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN005",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed PublishMode, ChangeEpsilon, or ForceIntervalSeconds values."));
                }

                var mixedConditions = members.Select(member => member.When).Distinct(StringComparer.Ordinal).Count() > 1
                    || members.Select(member => member.Unless).Distinct(StringComparer.Ordinal).Count() > 1;
                if (mixedConditions)
                {
                    var first = members[0];
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        MixedConditionDiagnosticId,
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed When or Unless values."));
                }
            }
        }

        private static bool IsInvalidConditionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();
            if (value.EndsWith("()", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 2);
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
