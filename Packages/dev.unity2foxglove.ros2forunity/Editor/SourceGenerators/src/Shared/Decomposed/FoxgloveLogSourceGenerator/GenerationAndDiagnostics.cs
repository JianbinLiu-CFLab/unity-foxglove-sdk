// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Provider-neutral FoxRun and FoxService source generation.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    public sealed partial class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
#if !FOXRUN_PROVIDER_ANALYZER
        private static void Generate(
            SourceProductionContext context,
            ImmutableArray<MemberData> items)
        {
            var members =
                new List<FoxRunRoslynGenerationMember>();
            var locations =
                new Dictionary<string, Location>(
                    StringComparer.Ordinal);
            var firstByClass =
                new Dictionary<
                    (string Ns, string ClassName),
                    MemberData>();

            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (item.DiagnosticLocation != null)
                {
                    var diagnosticDescriptor =
                        Diags.Member(item.DiagnosticId);
                    if (string.IsNullOrEmpty(item.DiagnosticMessage))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                diagnosticDescriptor,
                                item.DiagnosticLocation));
                    }
                    else
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                diagnosticDescriptor,
                                item.DiagnosticLocation,
                                item.DiagnosticMessage));
                    }
                    continue;
                }

                item.AppendRoslynMembers(members);
                locations[
                    MemberLocationKey(
                        item.Ns,
                        item.ClassName,
                        item.MemberName)]
                    = item.MemberLocation;
                var key =
                    (item.Ns, item.ClassName);
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
                    StringComparer.Ordinal);
            foreach (var diagnostic in
                     FoxRunGenerationModelValidator
                         .Validate(model))
            {
                var argument =
                    Diags.SharedUsesDetailedMessage(
                        diagnostic.Id)
                        ? diagnostic.Message
                        : diagnostic.Target;
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Diags.Shared(diagnostic.Id),
                        LocationFor(
                            diagnostic,
                            locations),
                        argument));
                if (diagnostic.Severity == "Error")
                {
                    invalid.Add(
                        DiagnosticDeclaringType(
                            diagnostic));
                }
            }

            ReportFoxRunHintCollisions(
                context,
                model.Types,
                firstByClass,
                invalid);

            foreach (var type in model.Types)
            {
                if (!firstByClass.TryGetValue(
                        (type.Namespace,
                            type.ClassName),
                        out var first))
                {
                    continue;
                }

                var names =
                    new HashSet<string>(
                        first.DeclaredMemberNames,
                        StringComparer.Ordinal);
                foreach (var generatedName in
                         FoxgloveSourceEmitter
                             .GeneratedMethodNames(type))
                {
                    if (!names.Contains(
                            generatedName))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.GeneratedMethodConflict,
                            first.MemberLocation,
                            type.DeclaringType,
                            generatedName));
                    invalid.Add(type.DeclaringType);
                }
            }

            var emitted =
                new List<FoxRunGenerationType>();
            foreach (var type in model.Types)
            {
                if (invalid.Contains(
                        type.DeclaringType)
                    || !firstByClass.TryGetValue(
                        (type.Namespace,
                            type.ClassName),
                        out var first))
                {
                    continue;
                }

                if (!first.IsPartial)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.NotPartial,
                            Location.None,
                            first.ClassName));
                    continue;
                }

                emitted.Add(type);
                context.AddSource(
                    FoxgloveSourceEmitter
                        .GeneratedSourceName(
                            type.Namespace,
                            type.ClassName),
                    FoxgloveSourceEmitter
                        .EmitCoreClass(type));
            }

            var descriptor =
                FoxRunGenerationDescriptorJsonWriter
                    .Write(
                        new FoxRunGenerationModel(
                            emitted,
                            model.DescriptorVersion,
                            model.GeneratorVersion));
            context.AddSource(
                "FoxRunGeneratedDescriptorInfo.g.cs",
                FoxRunDescriptorCarrierEmitter
                    .DescriptorCarrierSource(
                        descriptor));
        }
#endif

#if !FOXRUN_PROVIDER_ANALYZER
        private static void ReportFoxRunHintCollisions(
            SourceProductionContext context,
            IReadOnlyList<FoxRunGenerationType> types,
            IReadOnlyDictionary<(string Ns, string ClassName), MemberData> firstByClass,
            ISet<string> invalid)
        {
            var owners = new Dictionary<string, FoxRunGenerationType>(StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types ?? Array.Empty<FoxRunGenerationType>())
            {
                if (type == null)
                    continue;
                var hint = FoxgloveSourceEmitter.GeneratedSourceName(
                    type.Namespace,
                    type.ClassName);
                if (!owners.TryGetValue(hint, out var owner))
                {
                    owners.Add(hint, type);
                    continue;
                }

                if (string.Equals(owner.DeclaringType, type.DeclaringType, StringComparison.Ordinal))
                    continue;

                foreach (var conflict in new[] { owner, type })
                {
                    invalid.Add(conflict.DeclaringType);
                    if (!reported.Add(conflict.DeclaringType))
                        continue;
                    if (firstByClass.TryGetValue(
                            (conflict.Namespace, conflict.ClassName),
                            out var first))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Diags.UnsupportedHostIdentity,
                                first.MemberLocation,
                                "generated source hint '"
                                + hint
                                + "' collides for declaring hosts '"
                                + owner.DeclaringType
                                + "' and '"
                                + type.DeclaringType
                                + "'"));
                    }
                }
            }
        }
#endif

        private static string DiagnosticDeclaringType(
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

        private static Location LocationFor(
            FoxRunGenerationDiagnostic diagnostic,
            IReadOnlyDictionary<string, Location>
                locations)
        {
            if (diagnostic == null
                || string.IsNullOrEmpty(
                    diagnostic.MemberName))
            {
                return Location.None;
            }

            var declaringType =
                DiagnosticDeclaringType(diagnostic);
            return locations.TryGetValue(
                declaringType
                + "|"
                + diagnostic.MemberName,
                out var location)
                ? location ?? Location.None
                : Location.None;
        }

        private static string MemberLocationKey(
            string ns,
            string className,
            string memberName)
            => (string.IsNullOrEmpty(ns)
                    ? className
                    : ns + "." + className)
               + "|"
               + memberName;

    }
}
