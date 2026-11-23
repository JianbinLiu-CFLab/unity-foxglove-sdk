// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/source_text_delegate_targets
// Purpose: Record method-group delegate conversions in validation sources and enforce the committed manifest.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Unity2Foxglove.SourceTextDelegateTargets
{
    /// <summary>Enforces the source-text delegate-target manifest (Rule E) and emits its entries.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DelegateTargetManifestAnalyzer : DiagnosticAnalyzer
    {
        public const string MismatchId = "FOXDT001";
        public const string IdentityId = "FOXDT002";
        public const string InvalidManifestId = "FOXDT003";
        public const string EntryId = "FOXDT900";
        public const string ManifestFileName = "source_text_delegate_targets.tsv";

        private const string Category = "SourceTextDelegateTargets";

        private static readonly DiagnosticDescriptor MismatchRule = new DiagnosticDescriptor(
            MismatchId, "Delegate-target manifest mismatch", "{0}", Category, DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.CompilationEnd);
        private static readonly DiagnosticDescriptor IdentityRule = new DiagnosticDescriptor(
            IdentityId, "Delegate-target identity error", "{0}", Category, DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.CompilationEnd);
        private static readonly DiagnosticDescriptor InvalidManifestRule = new DiagnosticDescriptor(
            InvalidManifestId, "Invalid delegate-target manifest", "{0}", Category, DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.CompilationEnd);
        private static readonly DiagnosticDescriptor EntryRule = new DiagnosticDescriptor(
            EntryId, "Delegate-target manifest entry", "{0}", Category, DiagnosticSeverity.Info, true, customTags: WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(MismatchRule, IdentityRule, InvalidManifestRule, EntryRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
                options.TryGetValue("build_property.FoxDelegateTargetsRepoRoot", out var root);
                options.TryGetValue("build_property.FoxDelegateTargetsVariant", out var variant);
                options.TryGetValue("build_property.FoxDelegateTargetsEmit", out var emit);
                var state = new CompilationState(root, variant);
                start.RegisterOperationAction(state.Record, OperationKind.DelegateCreation);
                start.RegisterCompilationEndAction(end => state.Finish(
                    end, string.Equals((emit ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase)));
            });
        }

        /// <summary>Collects entries for one compilation and reports Rule E results at its end.</summary>
        private sealed class CompilationState
        {
            private readonly string _root;
            private readonly string _variant;
            private readonly ConcurrentDictionary<(string File, string Member, string Target), int> _entries =
                new ConcurrentDictionary<(string, string, string), int>();

            public CompilationState(string root, string variant)
            {
                _root = string.IsNullOrWhiteSpace(root) ? null : ManifestRules.NormalizeDirectory(root);
                _variant = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim();
            }

            public void Record(OperationAnalysisContext context)
            {
                if (_root == null || !(((IDelegateCreationOperation)context.Operation).Target is IMethodReferenceOperation reference))
                {
                    return;
                }
                var file = ManifestRules.RelativeTo(_root, context.Operation.Syntax.SyntaxTree.FilePath);
                if (file == null || !ManifestRules.IsInScope(file))
                {
                    return;
                }
                var member = SymbolIds.MemberId(context.ContainingSymbol);
                var target = SymbolIds.TargetId(reference.Method);
                if (member == null || target == null)
                {
                    return;
                }
                _entries.AddOrUpdate((file, member, target), 1, (_, count) => count + 1);
            }

            public void Finish(CompilationAnalysisContext context, bool emit)
            {
                var errors = new List<string>();
                if (_root == null)
                {
                    errors.Add("FoxDelegateTargetsRepoRoot is not set for this compilation.");
                }
                if (_variant == null || !ManifestRules.Variants.Contains(_variant))
                {
                    errors.Add($"FoxDelegateTargetsVariant '{_variant}' is not one of: {string.Join(", ", ManifestRules.Variants.OrderBy(v => v, StringComparer.Ordinal))}.");
                }
                var files = new HashSet<string>(StringComparer.Ordinal);
                if (_root != null)
                {
                    foreach (var tree in context.Compilation.SyntaxTrees)
                    {
                        var path = tree.FilePath;
                        if (!ManifestRules.IsRooted(path))
                        {
                            continue;
                        }
                        var file = ManifestRules.RelativeTo(_root, path);
                        if (file == null)
                        {
                            if (ManifestRules.LooksLikeScope(path))
                            {
                                errors.Add($"Validation source '{path}' is outside FoxDelegateTargetsRepoRoot '{_root}'.");
                            }
                            continue;
                        }
                        if (ManifestRules.IsInScope(file) && !files.Add(file))
                        {
                            errors.Add($"Two syntax trees normalize to the same manifest path '{file}'.");
                        }
                    }
                }
                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(IdentityRule, Location.None, error));
                    }
                    return;
                }

                var actual = _entries.ToDictionary(
                    pair => $"{_variant}\t{pair.Key.File}\t{pair.Key.Member}\t{pair.Key.Target}",
                    pair => pair.Value,
                    StringComparer.Ordinal);
                if (emit)
                {
                    foreach (var pair in actual.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(EntryRule, Location.None, pair.Key + "\t" + pair.Value));
                    }
                    return;
                }

                var manifests = context.Options.AdditionalFiles
                    .Where(text => ManifestRules.IsManifestPath(text.Path))
                    .ToArray();
                if (manifests.Length != 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        manifests.Length == 0 ? MismatchRule : InvalidManifestRule, Location.None,
                        manifests.Length == 0
                            ? $"The delegate-target manifest '{ManifestFileName}' is not an AdditionalFiles item of this compilation."
                            : $"The compilation has {manifests.Length} '{ManifestFileName}' AdditionalFiles items."));
                    return;
                }
                var parsed = ManifestRules.Parse(manifests[0].GetText(context.CancellationToken)?.ToString() ?? string.Empty);
                if (parsed.Errors.Count > 0)
                {
                    foreach (var error in parsed.Errors)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InvalidManifestRule, Location.None, error));
                    }
                    return;
                }

                var expected = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var entry in parsed.Entries.Where(e => e.Variant == _variant))
                {
                    if (!files.Contains(entry.File))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(MismatchRule, Location.None,
                            $"Stale manifest line for a file this variant does not compile: {entry.Line}"));
                        continue;
                    }
                    expected[entry.Key] = entry.Count;
                }
                foreach (var key in actual.Keys.Union(expected.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    actual.TryGetValue(key, out var actualCount);
                    expected.TryGetValue(key, out var expectedCount);
                    if (actualCount == expectedCount)
                    {
                        continue;
                    }
                    var message = expectedCount == 0
                        ? $"Missing manifest line: {key}\t{actualCount}"
                        : actualCount == 0
                            ? $"Manifest line has no matching conversion: {key}\t{expectedCount}"
                            : $"Manifest count differs: expected line {key}\t{actualCount}, manifest has {expectedCount}";
                    context.ReportDiagnostic(Diagnostic.Create(MismatchRule, Location.None, message));
                }
            }
        }
    }

    /// <summary>Stable symbol identities used in manifest lines.</summary>
    internal static class SymbolIds
    {
        public static string MemberId(ISymbol symbol)
        {
            var owner = symbol;
            while (owner is IMethodSymbol method
                   && (method.MethodKind == MethodKind.LocalFunction || method.MethodKind == MethodKind.AnonymousFunction))
            {
                owner = owner.ContainingSymbol;
            }
            return owner == null ? null : DocumentationCommentId.CreateDeclarationId(owner);
        }

        public static string TargetId(IMethodSymbol method)
        {
            var target = method.ReducedFrom ?? method;
            if (target.MethodKind == MethodKind.LocalFunction)
            {
                var owner = MemberId(target.ContainingSymbol);
                return owner == null ? null : "L:" + owner + "|" + target.Name;
            }
            var original = target.OriginalDefinition;
            var id = DocumentationCommentId.CreateDeclarationId(original);
            if (string.IsNullOrEmpty(id) || SymbolEqualityComparer.Default.Equals(target, original))
            {
                return string.IsNullOrEmpty(id) ? null : id;
            }
            var arguments = new List<ITypeSymbol>();
            for (var type = target.ContainingType; type != null; type = type.ContainingType)
            {
                arguments.InsertRange(0, type.TypeArguments);
            }
            arguments.AddRange(target.TypeArguments);
            return id + "{" + string.Join(",", arguments.Select(TypeId)) + "}";
        }

        private static string TypeId(ITypeSymbol type)
        {
            var id = DocumentationCommentId.CreateReferenceId(type);
            return string.IsNullOrEmpty(id)
                ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace(" ", string.Empty)
                : id;
        }
    }

    /// <summary>Path scope, identity and manifest well-formedness rules shared by Rule E.</summary>
    internal static class ManifestRules
    {
        public static readonly ImmutableHashSet<string> Variants = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "runtime", "runtime-adapter", "unit", "unit-adapter", "unit-native", "unit-manager", "bridge-unit");

        private static readonly Regex Scope = new Regex(@"^Packages/[^/]+/Tests/(Runtime|Unit)/.+\.cs$", RegexOptions.CultureInvariant);
        private static readonly Regex ScopeAnywhere = new Regex(@"/Packages/[^/]+/Tests/(Runtime|Unit)/.+\.cs$", RegexOptions.CultureInvariant);
        private static readonly Regex Member = new Regex(@"^[A-Z]:\S+$", RegexOptions.CultureInvariant);
        private static readonly Regex Target = new Regex(@"^(M:\S+|L:[A-Z]:\S+\|\S+)$", RegexOptions.CultureInvariant);
        private static readonly Regex Count = new Regex(@"^[1-9][0-9]*$", RegexOptions.CultureInvariant);

        public static bool IsInScope(string relative) => Scope.IsMatch(relative);

        public static bool LooksLikeScope(string path) => ScopeAnywhere.IsMatch(Normalize(path));

        public static bool IsManifestPath(string path) => Normalize(path ?? string.Empty).EndsWith("/" + DelegateTargetManifestAnalyzer.ManifestFileName, StringComparison.Ordinal);

        public static bool IsRooted(string path) =>
            !string.IsNullOrEmpty(path)
            && (path[0] == '/' || path[0] == '\\' || (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\')));

        public static string NormalizeDirectory(string path)
        {
            var normalized = Normalize(path.Trim());
            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }

        public static string RelativeTo(string root, string path)
        {
            if (!IsRooted(path))
            {
                return null;
            }
            var normalized = Normalize(path);
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? normalized.Substring(root.Length) : null;
        }

        private static string Normalize(string path)
        {
            var parts = new List<string>();
            var slashed = path.Replace('\\', '/');
            var prefix = slashed.StartsWith("//", StringComparison.Ordinal) ? "//" : slashed.StartsWith("/", StringComparison.Ordinal) ? "/" : string.Empty;
            foreach (var part in slashed.Substring(prefix.Length).Split('/'))
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }
                if (part == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(part);
            }
            var joined = prefix + string.Join("/", parts);
            return slashed.EndsWith("/", StringComparison.Ordinal) ? joined + "/" : joined;
        }

        public static ParseResult Parse(string text)
        {
            var result = new ParseResult();
            if (text.Length == 0)
            {
                return result;
            }
            if (text.IndexOf('\r') >= 0)
            {
                result.Errors.Add("The manifest contains CR characters; it must use LF line endings.");
            }
            if (!text.EndsWith("\n", StringComparison.Ordinal))
            {
                result.Errors.Add("The manifest must end with a final LF.");
            }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            var count = text.EndsWith("\n", StringComparison.Ordinal) ? lines.Length - 1 : lines.Length;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            for (var index = 0; index < count; index++)
            {
                var line = lines[index];
                var number = index + 1;
                var fields = line.Split('\t');
                if (fields.Length != 5)
                {
                    result.Errors.Add($"Line {number}: expected 5 TAB-separated fields, found {fields.Length}.");
                    continue;
                }
                if (fields.Any(field => field.Length == 0))
                {
                    result.Errors.Add($"Line {number}: empty field.");
                    continue;
                }
                if (!Variants.Contains(fields[0]))
                {
                    result.Errors.Add($"Line {number}: unknown variant '{fields[0]}'.");
                }
                if (!Member.IsMatch(fields[2]))
                {
                    result.Errors.Add($"Line {number}: malformed member id '{fields[2]}'.");
                }
                if (!Target.IsMatch(fields[3]))
                {
                    result.Errors.Add($"Line {number}: malformed target id '{fields[3]}'.");
                }
                if (!Count.IsMatch(fields[4]))
                {
                    result.Errors.Add($"Line {number}: count '{fields[4]}' is not a positive integer without leading zeros.");
                    continue;
                }
                var key = string.Join("\t", fields, 0, 4);
                if (!keys.Add(key))
                {
                    result.Errors.Add($"Line {number}: duplicate key.");
                }
                if (previous != null && string.CompareOrdinal(previous, line) > 0)
                {
                    result.Errors.Add($"Line {number}: lines are not sorted ordinally.");
                }
                previous = line;
                result.Entries.Add(new ManifestEntry(fields[0], fields[1], key, int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture), line));
            }
            return result;
        }
    }

    /// <summary>The parsed manifest lines and any well-formedness errors.</summary>
    internal sealed class ParseResult
    {
        public List<ManifestEntry> Entries { get; } = new List<ManifestEntry>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>One manifest line.</summary>
    internal sealed class ManifestEntry
    {
        public ManifestEntry(string variant, string file, string key, int count, string line)
        {
            Variant = variant;
            File = file;
            Key = key;
            Count = count;
            Line = line;
        }

        public string Variant { get; }

        public string File { get; }

        public string Key { get; }

        public int Count { get; }

        public string Line { get; }
    }
}
