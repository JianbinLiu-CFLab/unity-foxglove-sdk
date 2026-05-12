// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn Incremental Source Generator that scans for [FoxRun]
// attributed fields and emits IFoxgloveLogSource implementations.

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
    /// <summary>
    /// Roslyn incremental source generator that scans user assemblies for
    /// <c>[FoxRun]</c> attributed fields/properties on partial classes and emits
    /// <c>IFoxgloveLogSource</c> implementation source at Editor compile time.
    /// </summary>
    [Generator]
    public class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
        private const string AttrShortName = "FoxRun";
        private const string AttrFullName = "Unity.FoxgloveSDK.Components.FoxRunAttribute";

        /// <summary>
        /// Registers a syntax-based pipeline that filters candidate members,
        /// extracts metadata, and emits generated source files.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Roslyn 4.2: use CreateSyntaxProvider (ForAttributeWithMetadataName requires 4.3+)
            var members = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => ExtractMember(ctx, ct))
                .Where(static m => m != null);

            context.RegisterSourceOutput(
                members.Collect(),
                static (spc, items) => Generate(spc, items));
        }

        /// <summary>
        /// Quick syntax filter: returns <c>true</c> if the node is a field or property
        /// declaration that has any attribute lists; cheap enough to run on every node.
        /// </summary>
        private static bool IsCandidate(SyntaxNode node)
        {
            if (node is FieldDeclarationSyntax f && f.AttributeLists.Count > 0)
                return HasFoxRunAttr(f.AttributeLists);
            if (node is PropertyDeclarationSyntax p && p.AttributeLists.Count > 0)
                return HasFoxRunAttr(p.AttributeLists);
            return false;
        }

        /// <summary>
        /// Checks whether any attribute in the given lists matches <c>FoxRun</c> by
        /// short or fully-qualified name.
        /// </summary>
        private static bool HasFoxRunAttr(SyntaxList<AttributeListSyntax> lists)
        {
            foreach (var al in lists)
                foreach (var a in al.Attributes)
                {
                    var name = a.Name.ToString();
                    if (name == AttrShortName || name == AttrShortName + "Attribute"
                        || name.EndsWith("." + AttrShortName) || name.EndsWith("." + AttrShortName + "Attribute"))
                        return true;
                }
            return false;
        }

        /// <summary>
        /// Resolves semantic symbols from a candidate syntax node and builds a
        /// <c>MemberData</c> record with namespace, class name, the
        /// <c>[FoxRun]</c> topic entries, and partial-type check.
        /// </summary>
        private static MemberData ExtractMember(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            ISymbol symbol = null;
            if (ctx.Node is FieldDeclarationSyntax fieldDecl)
            {
                if (fieldDecl.Declaration.Variables.Count > 1)
                {
                    // Multi-variable field declarations like `[FoxRun] float _a, _b;`
                    // are ambiguous: the attribute target cannot be mapped to one
                    // topic member, so report a diagnostic instead of guessing.
                    return MemberData.ForDiagnostic(fieldDecl.GetLocation());
                }
                symbol = ctx.SemanticModel.GetDeclaredSymbol(fieldDecl.Declaration.Variables[0], ct);
            }
            else if (ctx.Node is PropertyDeclarationSyntax propDecl)
            {
                symbol = ctx.SemanticModel.GetDeclaredSymbol(propDecl, ct);
            }
            if (symbol == null) return null;

            // Verify attribute is actually FoxRunAttribute
            var attrs = symbol.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == AttrFullName)
                .ToList();
            if (attrs.Count == 0) return null;

            var containingType = symbol.ContainingType;
            if (containingType == null) return null;

            bool isPartial = containingType.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(ct) is TypeDeclarationSyntax tds &&
                          tds.Modifiers.Any(SyntaxKind.PartialKeyword));

            string memberName = symbol.Name;
            string memberType;
            if (symbol is IFieldSymbol fs)
                memberType = fs.Type.ToDisplayString();
            else if (symbol is IPropertySymbol ps)
                memberType = ps.Type.ToDisplayString();
            else
                memberType = "object";

            var topics = new List<TopicEntry>();
            foreach (var attr in attrs)
            {
                string topic = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string ?? "" : "";
                float rateHz = 10f;
                string schemaName = "";
                int publishMode = 0;
                float changeEpsilon = 0f;
                float forceIntervalSeconds = 0f;
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "RateHz" && named.Value.Value is float rate) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "PublishMode" && named.Value.Value is int pm) publishMode = pm;
                    if (named.Key == "ChangeEpsilon" && named.Value.Value is float eps) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && named.Value.Value is float fis) forceIntervalSeconds = fis;
                }
                topics.Add(new TopicEntry(topic, rateHz, schemaName, publishMode, changeEpsilon, forceIntervalSeconds));
            }

            string ns = containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString() : "";

            return new MemberData(ns, containingType.Name, isPartial, memberName, memberType, topics.ToArray());
        }

        /// <summary>
        /// Entry point for source output: reports diagnostics, groups members by
        /// enclosing class, and emits one generated partial class per valid group.
        /// </summary>
        private static void Generate(SourceProductionContext spc, ImmutableArray<MemberData> items)
        {
            foreach (var item in items.Where(m => m?.DiagnosticLocation != null))
                spc.ReportDiagnostic(Diagnostic.Create(Diags.MultiVariableDeclaration, item.DiagnosticLocation));

            var valid = items.Where(m => m != null && m.DiagnosticLocation == null).ToList();
            if (valid.Count == 0) return;

            var byClass = valid.GroupBy(m => (m.Ns, m.ClassName));
            foreach (var grp in byClass)
            {
                var first = grp.First();
                if (!first.IsPartial)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.NotPartial, Location.None, first.ClassName));
                    continue;
                }
                EmitClass(spc, first.Ns, first.ClassName, grp.ToArray());
            }
        }

        /// <summary>
        /// Emits the generated partial class implementing <c>IFoxgloveLogSource</c>
        /// for one class name/namespace pair. Handles topic-to-member grouping,
        /// schema conflict warnings, name collision detection, and delegates code
        /// generation to <c>FoxgloveSourceEmitter.EmitClass</c> for output
        /// consistency with the build-time physical fallback path.
        /// </summary>
        private static void EmitClass(SourceProductionContext spc, string ns, string className, MemberData[] members)
        {
            var args = new List<FoxgloveSourceEmitter.TopicMember>();
            foreach (var m in members)
                foreach (var t in m.Topics)
                    args.Add(new FoxgloveSourceEmitter.TopicMember(m.MemberName, m.MemberType, t.Topic, t.RateHz, t.SchemaName,
                        t.PublishMode, t.ChangeEpsilon, t.ForceIntervalSeconds));

            // Warn on schema conflicts
            var byTopic = args.GroupBy(a => a.Topic);
            foreach (var grp in byTopic)
            {
                var schemas = grp.Select(a => a.SchemaName).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
                if (schemas.Count > 1)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.TopicConflict, Location.None, grp.Key));
            }

            // Detect underscore-truncation name collisions
            foreach (var grp in byTopic)
            {
                var cleanNames = grp.Select(a => a.MemberName.TrimStart('_')).ToList();
                if (cleanNames.Distinct().Count() < cleanNames.Count)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.NameConflict, Location.None, className, grp.Key));
            }

            var source = FoxgloveSourceEmitter.EmitClass(ns, className, args);
            spc.AddSource($"{className}_FoxRun.g.cs", source);
        }

        /// <summary>
        /// Internal record produced by <c>ExtractMember</c>. Carries namespace, class
        /// name, member identity, topic entries, partial status, and optional
        /// diagnostic location for error reporting.
        /// </summary>
        private sealed class MemberData
        {
            /// <summary>Containing namespace (empty for global).</summary>
            public readonly string Ns;
            /// <summary>Containing class name.</summary>
            public readonly string ClassName;
            /// <summary>Field or property name.</summary>
            public readonly string MemberName;
            /// <summary>Field or property type as fully-qualified string.</summary>
            public readonly string MemberType;
            /// <summary>Whether the containing class is declared <c>partial</c>.</summary>
            public readonly bool IsPartial;
            /// <summary>Extracted topic entries from <c>[FoxRun]</c> attributes.</summary>
            public readonly TopicEntry[] Topics;
            /// <summary>Non-null when this represents a diagnostic-only placeholder.</summary>
            public readonly Location DiagnosticLocation;

            /// <summary>
            /// Factory for diagnostic-only instances (e.g. multi-variable declaration error).
            /// </summary>
            public static MemberData ForDiagnostic(Location location) =>
                new MemberData("", "", false, "", "", Array.Empty<TopicEntry>(), location);

            /// <summary>
            /// Creates a valid member-data record with no diagnostic.
            /// </summary>
            public MemberData(string ns, string cn, bool partial, string mn, string mt, TopicEntry[] t)
                : this(ns, cn, partial, mn, mt, t, null)
            {
            }

            /// <summary>
            /// Core constructor used by both the public constructor and
            /// <c>ForDiagnostic</c>.
            /// </summary>
            private MemberData(string ns, string cn, bool partial, string mn, string mt, TopicEntry[] t, Location diagnosticLocation)
            { Ns = ns; ClassName = cn; IsPartial = partial; MemberName = mn; MemberType = mt; Topics = t; DiagnosticLocation = diagnosticLocation; }
        }

        /// <summary>
        /// Immutable tuple representing one <c>[FoxRun]</c> attribute's topic, rate,
        /// and optional schema name.
        /// </summary>
        private sealed class TopicEntry
        {
            /// <summary>Topic string from the attribute's constructor argument.</summary>
            public readonly string Topic;
            /// <summary>Optional schema name from the attribute's named argument.</summary>
            public readonly string SchemaName;
            /// <summary>Publishing rate in Hz (default 10).</summary>
            public readonly float RateHz;
            /// <summary>Publish mode enum value.</summary>
            public readonly int PublishMode;
            /// <summary>Change epsilon.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval.</summary>
            public readonly float ForceIntervalSeconds;

            /// <summary>
            /// Creates a topic entry with the given topic, rate, and schema (backward compat).
            /// </summary>
            public TopicEntry(string topic, float rate, string schema)
                : this(topic, rate, schema, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic entry with publish policy.
            /// </summary>
            public TopicEntry(string topic, float rate, string schema,
                int publishMode, float changeEpsilon, float forceIntervalSeconds)
            {
                Topic = topic; RateHz = rate; SchemaName = schema;
                PublishMode = publishMode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
            }
        }

        /// <summary>
        /// Container for all FoxRun-specific Roslyn diagnostic descriptors.
        /// </summary>
        private static class Diags
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
                "Class '{0}' topic '{1}' has field names that collide after stripping underscores",
                "FoxRun", DiagnosticSeverity.Warning, true);

            /// <summary>FOXRUN004: multi-variable field declaration with <c>[FoxRun]</c> is unsupported.</summary>
            public static readonly DiagnosticDescriptor MultiVariableDeclaration = new DiagnosticDescriptor(
                "FOXRUN004", "Multi-variable field declaration",
                "[FoxRun] on a field declaration with multiple variables is not supported. Split into separate declarations.",
                "FoxRun", DiagnosticSeverity.Error, true);
        }
    }
}
