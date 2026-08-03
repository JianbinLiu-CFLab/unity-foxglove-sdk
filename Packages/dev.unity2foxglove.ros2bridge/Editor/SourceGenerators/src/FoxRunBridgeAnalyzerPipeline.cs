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
            foreach (var item in items)
            {
                if (item == null
                    || item.DiagnosticLocation != null)
                {
                    continue;
                }

                item.AppendRoslynMembers(members);
                var key = (item.Ns, item.ClassName);
                if (!firstByClass.ContainsKey(key))
                    firstByClass.Add(key, item);
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
    }
}
