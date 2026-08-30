// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ROS2 Bridge FoxRun source generator
// Purpose: Register and emit only the Bridge-owned physical contribution.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2Bridge.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunProviderAnalyzer
    {
        internal static void Register(
            IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<MemberData> members)
        {
            context.RegisterSourceOutput(
                members.Collect(),
                static (productionContext, items) =>
                    Generate(productionContext, items));
        }

        private static void Generate(
            SourceProductionContext context,
            ImmutableArray<MemberData> items)
        {
            var members =
                new List<FoxRunRoslynGenerationMember>();
            var firstByClass =
                new Dictionary<
                    (string Ns, string ClassName),
                    MemberData>();
            var locationsByMember =
                new Dictionary<string, Location>(
                    StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (item.DiagnosticLocation != null)
                {
                    if (string.Equals(
                            item.DiagnosticId,
                            "FOXRUN623",
                            StringComparison.Ordinal))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                FoxRunBridgeDiagnostics.HostIdentity,
                                item.DiagnosticLocation,
                                "FoxRun declaring host identity cannot be represented by the Bridge partial-class contract."));
                    }
                    continue;
                }

                item.AppendRoslynMembers(members);
                var key = (item.Ns, item.ClassName);
                if (!firstByClass.ContainsKey(key))
                    firstByClass.Add(key, item);
                locationsByMember[MemberKey(
                    item.Ns,
                    item.ClassName,
                    item.MemberName)] = item.MemberLocation;
            }

            if (members.Count == 0)
                return;

            var model =
                FoxRunRoslynGenerationModelLowerer
                    .Lower(members);
            var invalid =
                new HashSet<string>(
                    FoxRunGenerationModelValidator
                        .Validate(model)
                        .Where(diagnostic =>
                            string.Equals(
                                diagnostic.Severity,
                                "Error",
                                StringComparison.Ordinal))
                        .Select(DeclaringType),
                    StringComparer.Ordinal);
            var reportedBridgeDiagnostics =
                new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in model.Types)
            foreach (var member in type.Members)
            {
                if (FoxRunBridgeSourceEmitter
                    .TryValidateExplicitBridgeMember(
                        member,
                        out var diagnosticId,
                        out var reason))
                {
                    continue;
                }

                invalid.Add(type.DeclaringType);
                var memberKey = MemberKey(
                    type.Namespace,
                    type.ClassName,
                    member.MemberName);
                if (!reportedBridgeDiagnostics.Add(
                        memberKey + "\n" + diagnosticId))
                {
                    continue;
                }

                var location = locationsByMember.TryGetValue(
                    memberKey,
                    out var exactLocation)
                    ? exactLocation
                    : firstByClass.TryGetValue(
                        (type.Namespace, type.ClassName),
                        out var first)
                        ? first.MemberLocation
                        : Location.None;
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        FoxRunBridgeDiagnostics.For(
                            diagnosticId),
                        location,
                        reason));
            }

            ReportHintCollisions(
                context,
                model.Types,
                firstByClass,
                invalid);

            foreach (var type in model.Types)
            {
                if (invalid.Contains(type.DeclaringType)
                    || !firstByClass.TryGetValue(
                        (type.Namespace, type.ClassName),
                        out var first)
                    || !first.IsPartial)
                {
                    continue;
                }

                var source =
                    FoxRunBridgeSourceEmitter
                        .EmitBridgeContribution(type);
                if (string.IsNullOrWhiteSpace(source))
                    continue;
                context.AddSource(
                    FoxRunBridgeSourceEmitter
                        .GeneratedSourceName(
                            type.Namespace,
                            type.ClassName),
                    source);
            }
        }

        private static string DeclaringType(
            FoxRunGenerationDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return string.Empty;
            var target = diagnostic.Target
                         ?? string.Empty;
            var member = diagnostic.MemberName
                         ?? string.Empty;
            if (member.Length == 0)
                return target;
            var suffix = "." + member;
            return target.EndsWith(
                suffix,
                StringComparison.Ordinal)
                ? target.Substring(
                    0,
                    target.Length - suffix.Length)
                : target;
        }

        private static string MemberKey(
            string ns,
            string className,
            string memberName)
            => (string.IsNullOrEmpty(ns)
                    ? className ?? string.Empty
                    : ns + "." + className)
               + "\n"
               + (memberName ?? string.Empty);

        private static void ReportHintCollisions(
            SourceProductionContext context,
            IReadOnlyList<FoxRunGenerationType> types,
            IReadOnlyDictionary<(string Ns, string ClassName), MemberData> firstByClass,
            ISet<string> invalid)
        {
            var owners = new Dictionary<string, FoxRunGenerationType>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types ?? Array.Empty<FoxRunGenerationType>())
            {
                if (type == null)
                    continue;
                var hint = FoxRunBridgeSourceEmitter.GeneratedSourceName(
                    type.Namespace,
                    type.ClassName);
                if (!owners.TryGetValue(hint, out var owner))
                {
                    owners.Add(hint, type);
                    continue;
                }

                if (string.Equals(
                        owner.DeclaringType,
                        type.DeclaringType,
                        StringComparison.Ordinal))
                    continue;

                foreach (var conflict in new[] { owner, type })
                {
                    invalid.Add(conflict.DeclaringType);
                    if (!reported.Add(conflict.DeclaringType))
                        continue;
                    if (!firstByClass.TryGetValue(
                            (conflict.Namespace, conflict.ClassName),
                            out var first))
                        continue;
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            FoxRunBridgeDiagnostics.HostIdentity,
                            first.MemberLocation,
                            "FoxRun declaring host identity collides with another Bridge generated hint."));
                }
            }
        }
    }
}
