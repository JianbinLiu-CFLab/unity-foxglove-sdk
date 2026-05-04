using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    [Generator]
    public class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
        private const string AttrShortName = "FoxgloveLog";
        private const string AttrFullName = "Unity.FoxgloveSDK.Components.FoxgloveLogAttribute";

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

        private static bool IsCandidate(SyntaxNode node)
        {
            if (node is FieldDeclarationSyntax f && f.AttributeLists.Count > 0)
                return HasFoxgloveLogAttr(f.AttributeLists);
            if (node is PropertyDeclarationSyntax p && p.AttributeLists.Count > 0)
                return HasFoxgloveLogAttr(p.AttributeLists);
            return false;
        }

        private static bool HasFoxgloveLogAttr(SyntaxList<AttributeListSyntax> lists)
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

        private static MemberData ExtractMember(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            ISymbol symbol = null;
            if (ctx.Node is FieldDeclarationSyntax fieldDecl)
            {
                foreach (var v in fieldDecl.Declaration.Variables)
                {
                    symbol = ctx.SemanticModel.GetDeclaredSymbol(v, ct);
                    if (symbol != null) break;
                }
            }
            else if (ctx.Node is PropertyDeclarationSyntax propDecl)
            {
                symbol = ctx.SemanticModel.GetDeclaredSymbol(propDecl, ct);
            }
            if (symbol == null) return null;

            // Verify attribute is actually FoxgloveLogAttribute
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

        private static void Generate(SourceProductionContext spc, ImmutableArray<MemberData> items)
        {
            var valid = items.Where(m => m != null).ToList();
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
                sb.AppendLine($"{pad}            case {i}: return new FoxgloveLogTopicInfo(\"{topics[i]}\", {rate}f);");
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

            spc.AddSource($"{className}_FoxgloveLog.g.cs", sb.ToString());
        }

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

        private sealed class MemberData
        {
            public readonly string Ns, ClassName, MemberName, MemberType;
            public readonly bool IsPartial;
            public readonly TopicEntry[] Topics;
            public MemberData(string ns, string cn, bool partial, string mn, string mt, TopicEntry[] t)
            { Ns = ns; ClassName = cn; IsPartial = partial; MemberName = mn; MemberType = mt; Topics = t; }
        }

        private sealed class TopicEntry
        {
            public readonly string Topic, SchemaName;
            public readonly float RateHz;
            public TopicEntry(string topic, float rate, string schema)
            { Topic = topic; RateHz = rate; SchemaName = schema; }
        }

        private static class Diags
        {
            public static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
                "FXLOG001", "Class not partial",
                "Class '{0}' must be declared partial to use [FoxgloveLog]",
                "FoxgloveLog", DiagnosticSeverity.Error, true);
            public static readonly DiagnosticDescriptor TopicConflict = new DiagnosticDescriptor(
                "FXLOG002", "Topic schema conflict",
                "Topic '{0}' has conflicting SchemaName values across fields",
                "FoxgloveLog", DiagnosticSeverity.Warning, true);
        }
    }
}
