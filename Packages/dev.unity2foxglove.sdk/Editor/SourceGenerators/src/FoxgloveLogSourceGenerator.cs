// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn Incremental Source Generator that scans for [FoxRun]
// attributed fields and emits IFoxgloveLogSource implementations.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        /// declaration that has any attribute lists — cheap enough to run on every node.
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
                    // are ambiguous — report a diagnostic rather than silently skipping.
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
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "RateHz" && named.Value.Value is float rate) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                }
                topics.Add(new TopicEntry(topic, rateHz, schemaName));
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
        /// schema conflict warnings, name collision detection, and code formatting.
        /// </summary>
        private static void EmitClass(SourceProductionContext spc, string ns, string className, MemberData[] members)
        {
            var topicMap = new Dictionary<string, List<(string name, string type, float rate, string schema)>>();
            foreach (var m in members)
                foreach (var t in m.Topics)
                {
                    if (!topicMap.TryGetValue(t.Topic, out var list))
                        topicMap[t.Topic] = list = new List<(string, string, float, string)>();
                    list.Add((m.MemberName, m.MemberType, t.RateHz, t.SchemaName));
                }

            // Warn on schema conflicts
            foreach (var kv in topicMap)
            {
                var schemas = kv.Value.Select(f => f.schema).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
                if (schemas.Count > 1)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.TopicConflict, Location.None, kv.Key));
            }

            // Detect underscore-truncation name collisions
            foreach (var kv in topicMap)
            {
                var cleanNames = kv.Value.Select(f => f.name.TrimStart('_')).ToList();
                if (cleanNames.Distinct().Count() < cleanNames.Count)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.NameConflict, Location.None, className, kv.Key));
            }

            var topics = topicMap.Keys.ToList();
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.Scripting;");
            sb.AppendLine("using Unity.FoxgloveSDK.Components;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns)) { sb.AppendLine($"namespace {ns}"); sb.AppendLine("{"); }

            sb.AppendLine($"{pad}[Preserve]");
            sb.AppendLine($"{pad}partial class {className} : IFoxgloveLogSource");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    int IFoxgloveLogSource.FoxgloveLog_TopicCount => {topics.Count};");
            sb.AppendLine();

            // GetTopic
            sb.AppendLine($"{pad}    FoxgloveLogTopicInfo IFoxgloveLogSource.FoxgloveLog_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var rate = topicMap[topics[i]].Max(f => f.rate);
                sb.AppendLine(FormattableString.Invariant(
                    $"{pad}            case {i}: return new FoxgloveLogTopicInfo(\"{topics[i]}\", {rate}f);"));
            }
            sb.AppendLine($"{pad}            default: return default;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();

            // Publish
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveLogSource.FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var schema = fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.schema)).schema ?? "";
                sb.Append($"{pad}            case {i}: mgr.PublishJson(\"{topics[i]}\", \"{schema}\", new {{ ");
                for (int j = 0; j < fields.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var cleanName = fields[j].name.TrimStart('_');
                    sb.Append($"{cleanName} = {ValueExpr(fields[j].name, fields[j].type)}");
                }
                sb.AppendLine($" }}, nowNs); break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");

            spc.AddSource($"{className}_FoxRun.g.cs", sb.ToString());
        }

        /// <summary>
        /// Returns a C# anonymous-object expression string for a Unity type
        /// (<c>Vector3</c>, <c>Vector2</c>, <c>Quaternion</c>, <c>Color</c>), or the
        /// raw member name for all other types.
        /// </summary>
        private static string ValueExpr(string name, string type)
        {
            var t = type;
            if (t.StartsWith("UnityEngine.")) t = t.Substring(12);
            switch (t)
            {
                case "Vector3": return $"new {{ x = {name}.x, y = {name}.y, z = {name}.z }}";
                case "Vector2": return $"new {{ x = {name}.x, y = {name}.y }}";
                case "Quaternion": return $"new {{ x = {name}.x, y = {name}.y, z = {name}.z, w = {name}.w }}";
                case "Color": return $"new {{ r = {name}.r, g = {name}.g, b = {name}.b, a = {name}.a }}";
                default: return name;
            }
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

            /// <summary>
            /// Creates a topic entry with the given topic, rate, and schema.
            /// </summary>
            public TopicEntry(string topic, float rate, string schema)
            { Topic = topic; RateHz = rate; SchemaName = schema; }
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
