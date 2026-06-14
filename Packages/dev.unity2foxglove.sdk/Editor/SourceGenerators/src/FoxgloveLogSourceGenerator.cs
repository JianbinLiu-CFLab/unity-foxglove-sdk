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
        private const string AttrAttributeName = "FoxRunAttribute";
        private const string AttrFullName = "Unity.FoxgloveSDK.Components.FoxRunAttribute";
        private const string AttrQualifiedNameSuffix = ".FoxRun";
        private const string AttrQualifiedAttributeNameSuffix = ".FoxRunAttribute";

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
                    if (name == AttrShortName || name == AttrAttributeName
                        || name.EndsWith(AttrQualifiedNameSuffix, StringComparison.Ordinal)
                        || name.EndsWith(AttrQualifiedAttributeNameSuffix, StringComparison.Ordinal))
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

            var topics = new List<TopicEntry>();
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != AttrFullName)
                    continue;

                string topic = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string ?? "" : "";
                float rateHz = 10f;
                string schemaName = "";
                int publishMode = 0;
                float changeEpsilon = 0f;
                float forceIntervalSeconds = 0f;
                string when = "";
                string unless = "";
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "RateHz" && TryReadFloatConstant(named.Value, out var rate)) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "PublishMode" && named.Value.Value is int pm) publishMode = pm;
                    if (named.Key == "ChangeEpsilon" && TryReadFloatConstant(named.Value, out var eps)) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && TryReadFloatConstant(named.Value, out var fis)) forceIntervalSeconds = fis;
                    if (named.Key == "When" && named.Value.Value is string whenValue) when = whenValue;
                    if (named.Key == "Unless" && named.Value.Value is string unlessValue) unless = unlessValue;
                }
                topics.Add(new TopicEntry(topic, rateHz, schemaName, publishMode, changeEpsilon, forceIntervalSeconds, when, unless));
            }
            if (topics.Count == 0) return null;

            var containingType = symbol.ContainingType;
            if (containingType == null) return null;

            bool isPartial = containingType.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(ct) is TypeDeclarationSyntax tds &&
                          tds.Modifiers.Any(SyntaxKind.PartialKeyword));

            string memberName = symbol.Name;
            string memberKind;
            ITypeSymbol typeSymbol;
            if (symbol is IFieldSymbol fs)
            {
                memberKind = "field";
                typeSymbol = fs.Type;
            }
            else if (symbol is IPropertySymbol ps)
            {
                memberKind = "property";
                typeSymbol = ps.Type;
            }
            else
            {
                memberKind = "field";
                typeSymbol = null;
            }

            var memberType = typeSymbol == null ? "object" : typeSymbol.ToDisplayString();
            var emissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(memberType);
            var isValueType = typeSymbol?.IsValueType == true;
            var isArray = TryGetArrayElementType(typeSymbol, out var elementType);
            var elementTypeName = elementType == null ? "" : elementType.ToDisplayString();
            var rawMemberOrder = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? 0;
            var memberLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

            string ns = containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString() : "";

            return new MemberData(ns, containingType.Name, isPartial, memberName, memberKind, memberType, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, topics.ToArray());
        }

        private static bool TryReadFloatConstant(TypedConstant constant, out float value)
        {
            value = 0f;
            if (constant.Value == null)
                return false;

            try
            {
                value = Convert.ToSingle(constant.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Entry point for source output: reports diagnostics, groups members by
        /// enclosing class, and emits one generated partial class per valid group.
        /// </summary>
        private static void Generate(SourceProductionContext spc, ImmutableArray<MemberData> items)
        {
            var roslynMembers = new List<FoxRunRoslynGenerationMember>(items.Length);
            var memberLocations = new Dictionary<string, Location>(items.Length);
            var firstMemberByClass = new Dictionary<(string Ns, string ClassName), MemberData>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                if (item.DiagnosticLocation != null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.MultiVariableDeclaration, item.DiagnosticLocation));
                    continue;
                }

                item.AppendRoslynMembers(roslynMembers);
                memberLocations[MemberLocationKey(item.Ns, item.ClassName, item.MemberName)] = item.MemberLocation;

                var key = (item.Ns, item.ClassName);
                if (!firstMemberByClass.ContainsKey(key))
                    firstMemberByClass.Add(key, item);
            }

            if (roslynMembers.Count == 0) return;

            var model = FoxRunRoslynGenerationModelLowerer.Lower(roslynMembers);
            foreach (var diagnostic in FoxRunGenerationModelValidator.Validate(model))
                spc.ReportDiagnostic(Diagnostic.Create(Diags.Shared(diagnostic.Id), LocationFor(diagnostic, memberLocations), diagnostic.Target));

            var emittedTypes = new List<FoxRunGenerationType>();
            foreach (var type in model.Types)
            {
                var key = (type.Namespace, type.ClassName);
                if (!firstMemberByClass.TryGetValue(key, out var first))
                    continue;
                if (!first.IsPartial)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.NotPartial, Location.None, first.ClassName));
                    continue;
                }
                emittedTypes.Add(type);
                EmitClass(spc, type);
            }

            var descriptor = FoxRunGenerationDescriptorJsonWriter.Write(
                new FoxRunGenerationModel(emittedTypes, model.DescriptorVersion, model.GeneratorVersion));
            spc.AddSource("FoxRunGeneratedDescriptorInfo.g.cs", DescriptorCarrierSource(descriptor));
        }

        /// <summary>
        /// Emits the generated partial class implementing <c>IFoxgloveLogSource</c>
        /// for one class name/namespace pair. Shared model validation handles
        /// topic warnings before this method delegates code generation to
        /// <c>FoxgloveSourceEmitter.EmitClass</c> for output
        /// consistency with the build-time physical fallback path.
        /// </summary>
        private static void EmitClass(SourceProductionContext spc, FoxRunGenerationType type)
        {
            var ns = type.Namespace;
            var className = type.ClassName;
            var source = FoxgloveSourceEmitter.EmitClass(type);
            spc.AddSource(FoxgloveSourceEmitter.GeneratedSourceName(ns, className), source);
        }

        private static Location LocationFor(FoxRunGenerationDiagnostic diagnostic, Dictionary<string, Location> memberLocations)
        {
            if (diagnostic == null || memberLocations == null)
                return Location.None;

            if (!string.IsNullOrEmpty(diagnostic.MemberName))
            {
                var declaringType = diagnostic.Target;
                var memberSuffix = "." + diagnostic.MemberName;
                if (declaringType.EndsWith(memberSuffix, StringComparison.Ordinal))
                    declaringType = declaringType.Substring(0, declaringType.Length - memberSuffix.Length);

                if (memberLocations.TryGetValue(declaringType + "|" + diagnostic.MemberName, out var location))
                    return location ?? Location.None;
            }

            return Location.None;
        }

        private static string MemberLocationKey(string ns, string className, string memberName)
        {
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            return declaringType + "|" + memberName;
        }

        private static bool TryGetArrayElementType(ITypeSymbol type, out ITypeSymbol elementType)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                elementType = array.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
            {
                var fullName = named.ConstructedFrom.ToDisplayString();
                if (fullName == "System.Collections.Generic.List<T>"
                    || fullName == "System.Collections.Generic.IReadOnlyList<T>"
                    || fullName == "System.Collections.Generic.IList<T>")
                {
                    elementType = named.TypeArguments[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        private static string DescriptorCarrierSource(string descriptorJson)
        {
            return "// <auto-generated/>\n"
                   + "namespace Unity.FoxgloveSDK.Generated\n"
                   + "{\n"
                   + "    internal static class FoxRunGeneratedDescriptorInfo\n"
                   + "    {\n"
                   + "        public const string DescriptorJson = \"" + EscapeStringLiteral(descriptorJson) + "\";\n"
                   + "    }\n"
                   + "}\n";
        }

        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
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
            public readonly string EmissionTypeName;
            public readonly string MemberKind;
            public readonly bool IsValueType;
            public readonly bool IsArray;
            public readonly string ElementTypeName;
            public readonly int RawMemberOrder;
            public readonly Location MemberLocation;
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
                new MemberData("", "", false, "", "", "", "", false, false, "", 0, Location.None, Array.Empty<TopicEntry>(), location);

            /// <summary>
            /// Creates a valid member-data record with no diagnostic.
            /// </summary>
            public MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t)
                : this(ns, cn, partial, mn, memberKind, mt, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, t, null)
            {
            }

            /// <summary>
            /// Core constructor used by both the public constructor and
            /// <c>ForDiagnostic</c>.
            /// </summary>
            private MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t, Location diagnosticLocation)
            {
                Ns = ns;
                ClassName = cn;
                IsPartial = partial;
                MemberName = mn;
                MemberKind = memberKind;
                MemberType = mt;
                EmissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(emissionTypeName);
                IsValueType = isValueType;
                IsArray = isArray;
                ElementTypeName = elementTypeName;
                RawMemberOrder = rawMemberOrder;
                MemberLocation = memberLocation;
                Topics = t;
                DiagnosticLocation = diagnosticLocation;
            }

            public IReadOnlyList<FoxRunRoslynGenerationMember> ToRoslynMembers()
            {
                var members = new List<FoxRunRoslynGenerationMember>(Topics.Length);
                AppendRoslynMembers(members);
                return members;
            }

            public void AppendRoslynMembers(List<FoxRunRoslynGenerationMember> members)
            {
                if (members == null)
                    throw new ArgumentNullException(nameof(members));

                foreach (var topic in Topics)
                    members.Add(ToRoslynMember(topic));
            }

            private FoxRunRoslynGenerationMember ToRoslynMember(TopicEntry topic)
            {
                return new FoxRunRoslynGenerationMember(
                    Ns,
                    ClassName,
                    MemberName,
                    MemberKind,
                    MemberType,
                    EmissionTypeName,
                    IsValueType,
                    IsArray,
                    ElementTypeName,
                    topic.Topic,
                    topic.SchemaName,
                    topic.RateHz,
                    topic.PublishMode,
                    topic.ChangeEpsilon,
                    topic.ForceIntervalSeconds,
                    RawMemberOrder,
                    string.Empty,
                    topic.When,
                    topic.Unless);
            }
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
            public readonly string When;
            public readonly string Unless;

            /// <summary>
            /// Creates a topic entry with the given topic, rate, and schema (backward compat).
            /// </summary>
            public TopicEntry(string topic, float rate, string schema)
                : this(topic, rate, schema, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic entry with publish policy.
            /// </summary>
            public TopicEntry(string topic, float rate, string schema,
                int publishMode, float changeEpsilon, float forceIntervalSeconds, string when = "", string unless = "")
            {
                Topic = topic; RateHz = rate; SchemaName = schema;
                PublishMode = publishMode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
                When = when ?? string.Empty;
                Unless = unless ?? string.Empty;
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
                    default:
                        throw new ArgumentOutOfRangeException(nameof(id), id, "Unmapped shared FoxRun diagnostic id.");
                }
            }
        }
    }
}
