// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: FoxRun and FoxService Roslyn diagnostic descriptors.

using Microsoft.CodeAnalysis;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    /// <summary>
    /// Container for all FoxRun-specific Roslyn diagnostic descriptors.
    /// </summary>
    internal static class Diags
    {
        /// <summary>FOXRUN001: class must be <c>partial</c> to host <c>[FoxRun]</c> members.</summary>
        public static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
            "FOXRUN001", "Class not partial",
            "Class '{0}' must be declared partial to use [FoxRun]",
            "FoxRun", DiagnosticSeverity.Error, true);

        /// <summary>FOXRUN002: same topic has conflicting <c>SchemaName</c> across different fields.</summary>
        public static readonly DiagnosticDescriptor TopicConflict = new DiagnosticDescriptor(
            "FOXRUN002", "Topic schema conflict",
            "Topic '{0}' has conflicting SchemaName values across fields",
            "FoxRun", DiagnosticSeverity.Warning, true);

        /// <summary>FOXRUN003: field names collide after stripping leading underscores.</summary>
        public static readonly DiagnosticDescriptor NameConflict = new DiagnosticDescriptor(
            "FOXRUN003", "Field name collision",
            "{0}: field names collide after stripping underscores",
            "FoxRun", DiagnosticSeverity.Warning, true);

        /// <summary>FOXRUN004: multi-variable field declaration with <c>[FoxRun]</c> is unsupported.</summary>
        public static readonly DiagnosticDescriptor MultiVariableDeclaration = new DiagnosticDescriptor(
            "FOXRUN004", "Multi-variable field declaration",
            "[FoxRun] on a field declaration with multiple variables is not supported. Split into separate declarations.",
            "FoxRun", DiagnosticSeverity.Error, true);

        /// <summary>FOXRUN005: same-topic members have mixed publish policy settings.</summary>
        public static readonly DiagnosticDescriptor MixedTopicPolicy = new DiagnosticDescriptor(
            "FOXRUN005", "Mixed same-topic PublishMode policy",
            "Topic '{0}' has mixed PublishMode, ChangeEpsilon, or ForceIntervalSeconds values. Generated code uses OnTrigger precedence before scheduled policy settings.",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor UnsupportedCanonicalType = new DiagnosticDescriptor(
            "FOXRUN006", "Unsupported FoxRun type",
            "{0}: member type is not a canonical built-in FoxRun contract type",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor GenericType = new DiagnosticDescriptor(
            "FOXRUN007", "Generic FoxRun type",
            "{0}: generic FoxRun types may be unsafe for IL2CPP contract governance",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor NonAbsoluteTopic = new DiagnosticDescriptor(
            "FOXRUN008", "FoxRun topic must be absolute",
            "{0}: FoxRun topic must start with '/'",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor DisabledRate = new DiagnosticDescriptor(
            "FOXRUN009", "FoxRun scheduled publishing disabled",
            "{0}: RateHz <= 0 disables scheduled publishing unless the topic is trigger-only",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor BinaryType = new DiagnosticDescriptor(
            "FOXRUN010", "Binary FoxRun values unsupported",
            "{0}: binary/blob values are not supported in the FoxRun contract path",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor MissingClassName = new DiagnosticDescriptor(
            "FOXRUN011", "FoxRun declaring class name required",
            "{0}: FoxRun declaring class name is required",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MissingMemberName = new DiagnosticDescriptor(
            "FOXRUN012", "FoxRun member name required",
            "{0}: FoxRun member name is required",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidPublishMode = new DiagnosticDescriptor(
            "FOXRUN013", "FoxRun publish mode out of range",
            "{0}: FoxRun publish mode must be between 0 and 3",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidFoxRunMode = new DiagnosticDescriptor(
            "FOXRUN023", "FoxRun mode out of range",
            "{0}: FoxRun mode must be PublishOnly, SubscribeOnly, or PublishAndSubscribe",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor UnsupportedInboundShape = new DiagnosticDescriptor(
            "FOXRUN024", "Unsupported FoxRun inbound shape",
            "{0}: FoxRun inbound arrays and aggregate members are not supported",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor IgnoredSubscribePolicy = new DiagnosticDescriptor(
            "FOXRUN025", "SubscribeOnly ignores publish policy",
            "{0}: SubscribeOnly ignores publish timing options",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor BidirectionalAuthority = new DiagnosticDescriptor(
            "FOXRUN026", "PublishAndSubscribe authority",
            "{0}: PublishAndSubscribe requires explicit authority ownership",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor InboundNaming = new DiagnosticDescriptor(
            "FOXRUN027", "FoxRun inbound naming",
            "{0}: SubscribeOnly member name should communicate input-port authority",
            "FoxRun", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor InboundTargetNotWritable = new DiagnosticDescriptor(
            "FOXRUN028", "FoxRun inbound target is not writable",
            "FoxRun inbound fields must not be readonly and properties must have a setter",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidMemberKind = new DiagnosticDescriptor(
            "FOXRUN014", "FoxRun member kind invalid",
            "{0}: FoxRun member kind must be field or property",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor ConditionMissing = new DiagnosticDescriptor(
            "FOXRUN015", "FoxRun condition member missing",
            "{0}: FoxRun condition member could not be resolved",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor ConditionNotBool = new DiagnosticDescriptor(
            "FOXRUN016", "FoxRun condition member must be bool",
            "{0}: FoxRun condition member must be bool",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MixedTopicConditions = new DiagnosticDescriptor(
            "FOXRUN017", "Mixed same-topic conditional gates",
            "Topic '{0}' has mixed When or Unless values across FoxRun members",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor UnlessConditionMissing = new DiagnosticDescriptor(
            "FOXRUN029", "FoxRun Unless condition member missing",
            "{0}: FoxRun Unless condition member could not be resolved",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static DiagnosticDescriptor UnknownFoxRunDiagnostic(string id)
        {
            return new DiagnosticDescriptor(
                "FOXRUN000",
                "Unmapped FoxRun generator diagnostic",
                "{0}: internal FoxRun generator diagnostic '" + (id ?? string.Empty) + "' is not mapped to a public descriptor",
                "FoxRun",
                DiagnosticSeverity.Error,
                true);
        }

        public static DiagnosticDescriptor UnknownFoxServiceDiagnostic(string id)
        {
            return new DiagnosticDescriptor(
                "FOXSERVICE000",
                "Unmapped FoxService generator diagnostic",
                "{0}: internal FoxService generator diagnostic '" + (id ?? string.Empty) + "' is not mapped to a public descriptor",
                "FoxService",
                DiagnosticSeverity.Error,
                true);
        }

        public static readonly DiagnosticDescriptor AggregateFieldWithoutMessage = new DiagnosticDescriptor(
            "FOXRUN018", "FoxRunField requires FoxRunMessage",
            "[FoxRunField] member must be declared inside a type annotated with [FoxRunMessage]",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MixedAggregateTopic = new DiagnosticDescriptor(
            "FOXRUN019", "Mixed aggregate and field-level topic",
            "{0}: topic cannot mix FoxRunMessage aggregate fields with field-level FoxRun members",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor AggregateArrayUnsupported = new DiagnosticDescriptor(
            "FOXRUN020", "Aggregate array fields unsupported",
            "{0}: FoxRun aggregate array fields are not supported yet",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor StaticAggregateMember = new DiagnosticDescriptor(
            "FOXRUN021", "Static aggregate member unsupported",
            "[FoxRunField] cannot be applied to static members",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor DuplicateAggregateJsonName = new DiagnosticDescriptor(
            "FOXRUN022", "Duplicate aggregate JSON field",
            "{0}: aggregate topic has duplicate JSON field names",
            "FoxRun", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidServiceName = new DiagnosticDescriptor(
            "FOXSERVICE001", "FoxService name must be absolute",
            "FoxService '{0}' must be non-empty and start with '/'",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidServiceSignature = new DiagnosticDescriptor(
            "FOXSERVICE002", "Unsupported FoxService method signature",
            "{0}: FoxService methods must be non-static, non-generic, synchronous, partial-class instance methods with zero or one by-value parameter",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor UnsupportedServiceRequestType = new DiagnosticDescriptor(
            "FOXSERVICE003", "Unsupported FoxService request type",
            "{0}: FoxService request type is not supported by the declarative RPC generator",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor UnsupportedServiceResponseType = new DiagnosticDescriptor(
            "FOXSERVICE004", "Unsupported FoxService response type",
            "{0}: FoxService response type is not supported by the declarative RPC generator",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor DuplicateServiceName = new DiagnosticDescriptor(
            "FOXSERVICE005", "Duplicate FoxService name",
            "FoxService name '{0}' is declared more than once in the generated service graph",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MissingExplicitServiceSchemaMetadata = new DiagnosticDescriptor(
            "FOXSERVICE006", "FoxService schema metadata omitted",
            "FoxService '{0}' omits Type, RequestSchemaName, or ResponseSchemaName; generated stable defaults will be used",
            "FoxService", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor ServiceDtoWarning = new DiagnosticDescriptor(
            "FOXSERVICE007", "FoxService DTO member may not serialize",
            "{0}",
            "FoxService", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor RecursiveServiceDto = new DiagnosticDescriptor(
            "FOXSERVICE008", "FoxService DTO graph is recursive",
            "{0}",
            "FoxService", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor DeepServiceDto = new DiagnosticDescriptor(
            "FOXSERVICE009", "FoxService DTO graph is too deep",
            "{0}",
            "FoxService", DiagnosticSeverity.Warning, true);

        public static DiagnosticDescriptor Shared(string id)
        {
            switch (id)
            {
                case "FOXRUN002": return TopicConflict;
                case "FOXRUN003": return NameConflict;
                case "FOXRUN005": return MixedTopicPolicy;
                case "FOXRUN006": return UnsupportedCanonicalType;
                case "FOXRUN007": return GenericType;
                case "FOXRUN008": return NonAbsoluteTopic;
                case "FOXRUN009": return DisabledRate;
                case "FOXRUN010": return BinaryType;
                case "FOXRUN011": return MissingClassName;
                case "FOXRUN012": return MissingMemberName;
                case "FOXRUN013": return InvalidPublishMode;
                case "FOXRUN014": return InvalidMemberKind;
                case "FOXRUN015": return ConditionMissing;
                case "FOXRUN016": return ConditionNotBool;
                case "FOXRUN017": return MixedTopicConditions;
                case "FOXRUN029": return UnlessConditionMissing;
                case "FOXRUN019": return MixedAggregateTopic;
                case "FOXRUN020": return AggregateArrayUnsupported;
                case "FOXRUN022": return DuplicateAggregateJsonName;
                case "FOXRUN023": return InvalidFoxRunMode;
                case "FOXRUN024": return UnsupportedInboundShape;
                case "FOXRUN025": return IgnoredSubscribePolicy;
                case "FOXRUN026": return BidirectionalAuthority;
                case "FOXRUN027": return InboundNaming;
                default:
                    return UnknownFoxRunDiagnostic(id);
            }
        }

        public static DiagnosticDescriptor Member(string id)
        {
            switch (id)
            {
                case "FOXRUN004": return MultiVariableDeclaration;
                case "FOXRUN015": return ConditionMissing;
                case "FOXRUN016": return ConditionNotBool;
                case "FOXRUN029": return UnlessConditionMissing;
                case "FOXRUN018": return AggregateFieldWithoutMessage;
                case "FOXRUN021": return StaticAggregateMember;
                case "FOXRUN028": return InboundTargetNotWritable;
                default:
                    return UnknownFoxRunDiagnostic(id);
            }
        }

        public static DiagnosticDescriptor Service(string id)
        {
            switch (id)
            {
                case "FOXSERVICE001": return InvalidServiceName;
                case "FOXSERVICE002": return InvalidServiceSignature;
                case "FOXSERVICE003": return UnsupportedServiceRequestType;
                case "FOXSERVICE004": return UnsupportedServiceResponseType;
                case "FOXSERVICE005": return DuplicateServiceName;
                case "FOXSERVICE006": return MissingExplicitServiceSchemaMetadata;
                case "FOXSERVICE007": return ServiceDtoWarning;
                case "FOXSERVICE008": return RecursiveServiceDto;
                case "FOXSERVICE009": return DeepServiceDto;
                default:
                    return UnknownFoxServiceDiagnostic(id);
            }
        }
    }
}
