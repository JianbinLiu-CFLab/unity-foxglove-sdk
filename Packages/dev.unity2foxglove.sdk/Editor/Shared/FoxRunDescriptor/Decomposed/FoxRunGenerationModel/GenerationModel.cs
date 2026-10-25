// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModel
// Purpose: Provider-neutral semantic model section consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Provider-neutral semantic model shared by FoxRun emission, descriptor
    /// evidence, and validation so all hosts report the same contract they emit.
    /// </summary>
    public sealed class FoxRunGenerationModel
    {
        public readonly int DescriptorVersion;
        public readonly string GeneratorVersion;
        public readonly IReadOnlyList<FoxRunGenerationType> Types;

        public FoxRunGenerationModel(
            IReadOnlyList<FoxRunGenerationType> types,
            int descriptorVersion =
                FoxRunGenerationDescriptorConstants.DescriptorVersion,
            string generatorVersion =
                FoxRunGenerationDescriptorConstants.GeneratorVersion)
        {
            DescriptorVersion = descriptorVersion;
            GeneratorVersion = generatorVersion ?? string.Empty;
            Types = CopyTypes(types);
        }

        public static FoxRunGenerationModel FromMembers(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            var normalized = ApplyMessagePackVariantAvailability(
                members ?? Array.Empty<FoxRunGenerationMember>());
            var types = normalized
                .GroupBy(
                    member => new TypeKey(
                        member.Namespace,
                        member.ClassName))
                .OrderBy(
                    group => group.Key.DeclaringType,
                    StringComparer.Ordinal)
                .Select(
                    group => new FoxRunGenerationType(
                        group.Key.Namespace,
                        group.Key.ClassName,
                        group.ToList()))
                .ToList();
            return new FoxRunGenerationModel(types);
        }

        private static IReadOnlyList<FoxRunGenerationMember>
            ApplyMessagePackVariantAvailability(
                IReadOnlyList<FoxRunGenerationMember> members)
        {
            var replacements =
                new Dictionary<FoxRunGenerationMember, FoxRunGenerationMember>();
            foreach (var topicGroup in members
                         .Where(
                             member =>
                                 member != null
                                 && !string.IsNullOrEmpty(member.Topic))
                         .GroupBy(
                             member =>
                                 member.DeclaringType
                                 + "\n"
                                 + member.Topic,
                             StringComparer.Ordinal))
            {
                var values = topicGroup.ToList();
                var publishing = values
                    .Where(member => member.Mode == 1 || member.Mode == 3)
                    .ToList();
                var subscribing = values
                    .Where(member => member.Mode == 2 || member.Mode == 3)
                    .ToList();
                var streamCount =
                    subscribing.Count(member => member.IsStream);
                var invalidSubscribeTopology =
                    streamCount > 1
                    || (streamCount > 0
                        && subscribing.Count > streamCount);
                var invalidPublishSchedule =
                    HasMixedNormalizedSchedule(publishing);
                var invalidSubscribeSchedule =
                    HasMixedNormalizedSchedule(subscribing);

                foreach (var member in values)
                {
                    replacements[member] =
                        member.WithMessagePackVariantAvailability(
                            invalidPublishSchedule,
                            invalidSubscribeTopology,
                            invalidSubscribeSchedule,
                            !FoxRunMessagePackTypeShapeRules
                                .IsPublishSupported(
                                    member.TypeShape,
                                    member.CanonicalType),
                            !FoxRunMessagePackTypeShapeRules
                                .IsSubscribeSupported(
                                    member.TypeShape,
                                    member.CanonicalType));
                }
            }

            return members
                .Select(
                    member =>
                        replacements.TryGetValue(member, out var replacement)
                            ? replacement
                            : member)
                .ToList()
                .AsReadOnly();
        }

        private static bool HasMixedNormalizedSchedule(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            if (members == null || members.Count < 2)
                return false;
            var first = members[0].NormalizedSchedule;
            for (var index = 1; index < members.Count; index++)
            {
                if (!Equals(first, members[index].NormalizedSchedule))
                    return true;
            }

            return false;
        }

        private static IReadOnlyList<FoxRunGenerationType> CopyTypes(
            IReadOnlyList<FoxRunGenerationType> types)
            => (types ?? Array.Empty<FoxRunGenerationType>())
                .OrderBy(type => type.DeclaringType, StringComparer.Ordinal)
                .Select(
                    type => new FoxRunGenerationType(
                        type.Namespace,
                        type.ClassName,
                        type.Members))
                .ToList()
                .AsReadOnly();

        private readonly struct TypeKey
        {
            public TypeKey(string ns, string className)
            {
                Namespace = ns ?? string.Empty;
                ClassName = className ?? string.Empty;
                DeclaringType = string.IsNullOrEmpty(Namespace)
                    ? ClassName
                    : Namespace + "." + ClassName;
            }

            public readonly string Namespace;
            public readonly string ClassName;
            public readonly string DeclaringType;
        }
    }

}
