// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native.Editor/FoxRun
// Purpose: R2FU-owned projection of the neutral FoxRun generation model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Provider-local view over one neutral declaration. ROS shapes never
    /// enter the SDK descriptor; the installed R2FU Editor package builds
    /// them independently from the declaration's payload type.
    /// </summary>
    internal sealed class FoxRunR2fuTopicMember :
        IFoxRunR2fuEmitterMember
    {
        private readonly FoxRunGenerationMember _generation;
        private readonly FoxgloveSourceEmitter.TopicMember _core;

        private FoxRunR2fuTopicMember(
            FoxRunGenerationMember generation,
            FoxgloveSourceEmitter.TopicMember core,
            bool selectedForPublish,
            bool selectedForSubscribe,
            FoxRunRos2MessageShape messageShape,
            FoxRunRos2CustomDtoShape customDtoShape)
        {
            _generation = generation
                ?? throw new ArgumentNullException(nameof(generation));
            _core = core ?? throw new ArgumentNullException(nameof(core));
            SelectedForPublish = selectedForPublish;
            SelectedForSubscribe = selectedForSubscribe;
            Ros2MessageShape = messageShape;
            Ros2CustomDtoShape = customDtoShape;
            Ros2ContractKind =
                messageShape != null && messageShape.ImplementsRos2Message
                    ? FoxRunRos2ContractKind.PackagedRos2Message
                    : customDtoShape != null
                        ? FoxRunRos2ContractKind.CustomDto
                        : FoxRunRos2ContractKind.Unsupported;
            GeneratesRos2NativeRegistration =
                (selectedForPublish || selectedForSubscribe)
                && IsUsableShape(
                    Ros2ContractKind,
                    messageShape,
                    customDtoShape);
        }

        internal static FoxRunR2fuTopicMember Create(
            FoxRunGenerationMember member)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            var publishes =
                member.Mode == 1 || member.Mode == 3;
            var subscribes =
                member.Mode == 2 || member.Mode == 3;
            var selectedForPublish =
                publishes
                && (member.PublishTransportIds == null
                    || member.PublishTransportIds.Any(
                        IsR2fuProvider));
            var selectedForSubscribe =
                subscribes
                && (string.IsNullOrWhiteSpace(
                        member.SubscribeTransportId)
                    || IsR2fuProvider(
                        member.SubscribeTransportId));

            var payloadType = ResolvePayloadType(member);
            FoxRunRos2MessageShape messageShape = null;
            FoxRunRos2CustomDtoShape customDtoShape = null;
            if (payloadType != null)
            {
                var candidate =
                    FoxRunReflectionRos2MessageShapeBuilder.Build(
                        payloadType);
                if (candidate.ImplementsRos2Message)
                {
                    messageShape = candidate;
                }
                else
                {
                    customDtoShape =
                        FoxRunReflectionRos2CustomDtoShapeBuilder.Build(
                            payloadType);
                }
            }

            return new FoxRunR2fuTopicMember(
                member,
                member.ToTopicMember(),
                selectedForPublish,
                selectedForSubscribe,
                messageShape,
                customDtoShape);
        }

        internal bool SelectedForPublish { get; }
        internal bool SelectedForSubscribe { get; }
        internal string DeclaringType => _generation.DeclaringType;
        public string MemberName => _core.MemberName;
        public string TypeName => _core.TypeName;
        public string Topic => _core.Topic;
        public float Hz => _core.Hz;
        public bool HasExplicitHz => _core.HasExplicitHz;
        public string SchemaName => _core.SchemaName;
        public int Policy => _core.Policy;
        public int Mode => _core.Mode;
        public string OnlyIf => _core.OnlyIf;
        public FoxRunConditionMemberKind ConditionMemberKind =>
            _core.ConditionMemberKind;
        public string Encoding => _core.Encoding;
        public FoxRunNamedArgumentPresence NamedArgumentPresence =>
            _core.NamedArgumentPresence;
        public bool IsStream => _core.IsStream;
        public string Source =>
            string.IsNullOrWhiteSpace(_core.SubscribeTransportId)
                ? FoxRunR2fuGenerationConstants.Inherit
                : _core.SubscribeTransportId;
        public string Targets =>
            _core.PublishTransportIds == null
                ? FoxRunR2fuGenerationConstants.Inherit
                : string.Join(",", _core.PublishTransportIds);
        public string QosProfile =>
            FoxRunR2fuGenerationConstants.Inherit;
        public string QosReliability => _core.Reliability;
        public string QosDurability => _core.Durability;
        public string QosHistory => _core.History;
        public int QosDepth => _core.Depth;
        public bool GeneratesRos2NativeRegistration { get; }
        public FoxRunRos2MessageShape Ros2MessageShape { get; }
        public FoxRunRos2CustomDtoShape Ros2CustomDtoShape { get; }
        public FoxRunRos2ContractKind Ros2ContractKind { get; }

        private static bool IsUsableShape(
            FoxRunRos2ContractKind kind,
            FoxRunRos2MessageShape messageShape,
            FoxRunRos2CustomDtoShape customDtoShape)
        {
            switch (kind)
            {
                case FoxRunRos2ContractKind.PackagedRos2Message:
                    return messageShape != null
                           && messageShape.ImplementsRos2Message
                           && messageShape
                               .HasPublicParameterlessConstructor
                           && messageShape.Diagnostics.Count == 0;
                case FoxRunRos2ContractKind.CustomDto:
                    return customDtoShape != null
                           && customDtoShape.IsSupported
                           && customDtoShape
                               .HasPublicParameterlessConstructor
                           && customDtoShape.Diagnostics.Count == 0
                           && !string.IsNullOrWhiteSpace(
                               customDtoShape.PayloadIdentity);
                default:
                    return false;
            }
        }

        private static bool IsR2fuProvider(string value)
            => string.Equals(
                value,
                FoxRunR2fuGenerationConstants.ProviderId,
                StringComparison.Ordinal);

        private static Type ResolvePayloadType(
            FoxRunGenerationMember member)
        {
            foreach (var candidate in new[]
                     {
                         member.RawObservedTypeName,
                         member.EmissionTypeName,
                         member.CanonicalType
                     })
            {
                var normalized = NormalizeTypeName(candidate);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                var direct = Type.GetType(
                    normalized,
                    throwOnError: false);
                if (direct != null)
                    return direct;

                foreach (var assembly in
                         AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type resolved;
                    try
                    {
                        resolved = assembly.GetType(
                            normalized,
                            throwOnError: false);
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        resolved = null;
                    }

                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }

        private static string NormalizeTypeName(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            const string globalPrefix = "global::";
            return normalized.StartsWith(
                globalPrefix,
                StringComparison.Ordinal)
                ? normalized.Substring(globalPrefix.Length)
                : normalized;
        }
    }

    internal static class FoxRunR2fuSourceEmitter
    {
        internal static string Emit(FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var members = type.Members
                .Select(FoxRunR2fuTopicMember.Create)
                .ToList();
            var inputMembers = members
                .Where(
                    member =>
                        member.SelectedForSubscribe
                        && (member.Mode == 2
                            || member.Mode == 3))
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(
                    member => member.MemberName,
                    StringComparer.Ordinal)
                .ToList();
            var packagedInputMembers = inputMembers
                .Where(
                    member =>
                        member.GeneratesRos2NativeRegistration
                        && member.Ros2ContractKind
                        == FoxRunRos2ContractKind
                            .PackagedRos2Message)
                .ToList();
            var customInputMembers = inputMembers
                .Where(IsCustomMember)
                .ToList();
            var publishing = members
                .Where(
                    member =>
                        member.SelectedForPublish
                        && member.Mode != 2)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var publishTopics = type.Members
                .Where(member => member.Mode != 2)
                .Select(member => member.Topic)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var customPublishMembers = publishing
                .Where(
                    pair =>
                        pair.Value.Count == 1
                        && IsCustomMember(pair.Value[0]))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value[0])
                .ToList();
            var mapperMembers = customInputMembers
                .Concat(customPublishMembers)
                .Distinct()
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(
                    member => member.MemberName,
                    StringComparer.Ordinal)
                .ToList();

            var output = new StringBuilder();
            Ros2InputDispatchEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                packagedInputMembers,
                publishTopics);
            Ros2CustomDtoMapperEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                mapperMembers,
                customInputMembers,
                publishTopics);
            Ros2CustomPublishEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                customPublishMembers,
                mapperMembers);
            return output.ToString();
        }

        private static bool IsCustomMember(
            FoxRunR2fuTopicMember member)
            => member != null
               && member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind
               == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported
               && member.Ros2CustomDtoShape
                   .HasPublicParameterlessConstructor
               && member.Ros2CustomDtoShape.Diagnostics.Count == 0
               && !string.IsNullOrWhiteSpace(
                   member.Ros2CustomDtoShape.PayloadIdentity);
    }
}
