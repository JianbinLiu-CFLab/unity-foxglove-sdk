using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    [Generator]
    public sealed class ComponentMessagePackSourceGenerator : IIncrementalGenerator
    {
        private const string SchemaAttribute = "Unity.FoxgloveSDK.Protocol.FoxgloveSchemaAttribute";
        private const string JsonPropertyAttribute = "Newtonsoft.Json.JsonPropertyAttribute";
        private const string JsonIgnoreAttribute = "Newtonsoft.Json.JsonIgnoreAttribute";
        private const string IgnoreAttribute = "Unity.FoxgloveSDK.Components.ComponentMessagePackIgnoreAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var types = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                static (ctx, _) => Extract(ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) as INamedTypeSymbol))
                .Where(static value => value != null)
                .Collect();
            var input = context.CompilationProvider.Combine(types);
            context.RegisterSourceOutput(input, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
        }

        private static ComponentMessagePackTypeModel Extract(INamedTypeSymbol type)
        {
            if (type == null) return null;
            var schema = type.GetAttributes().FirstOrDefault(a => string.Equals(a.AttributeClass?.ToDisplayString(), SchemaAttribute, StringComparison.Ordinal));
            if (schema == null) return null;
            var schemaName = schema.ConstructorArguments.Length == 1 ? schema.ConstructorArguments[0].Value as string : string.Empty;
            var ignored = type.GetAttributes().Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), IgnoreAttribute, StringComparison.Ordinal));
            var members = new List<ComponentMessagePackMemberModel>();
            foreach (var member in type.GetMembers().Where(m => m.Kind == SymbolKind.Field || m.Kind == SymbolKind.Property).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (member.IsStatic || member.IsImplicitlyDeclared) continue;
                var field = member as IFieldSymbol;
                var property = member as IPropertySymbol;
                if (property != null && property.IsIndexer) continue;
                var memberType = field?.Type ?? property?.Type;
                if (memberType == null) continue;
                var jsonIgnore = member.GetAttributes().Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), JsonIgnoreAttribute, StringComparison.Ordinal));
                if (jsonIgnore) continue;
                var json = member.GetAttributes().FirstOrDefault(a => string.Equals(a.AttributeClass?.ToDisplayString(), JsonPropertyAttribute, StringComparison.Ordinal));
                var wire = json?.NamedArguments.FirstOrDefault(x => x.Key == "PropertyName").Value.Value as string;
                if (string.IsNullOrEmpty(wire) && json?.ConstructorArguments.Length == 1) wire = json.ConstructorArguments[0].Value as string;
                members.Add(new ComponentMessagePackMemberModel(member.Name, wire ?? member.Name, memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Canonical(memberType)));
            }
            return new ComponentMessagePackTypeModel(type, schemaName ?? string.Empty, ignored, members);
        }

        private static string Canonical(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Boolean) return "bool";
            if (type.SpecialType == SpecialType.System_Int32) return "int32";
            if (type.SpecialType == SpecialType.System_UInt32) return "uint32";
            if (type.SpecialType == SpecialType.System_Int64) return "int64";
            if (type.SpecialType == SpecialType.System_UInt64) return "uint64";
            if (type.SpecialType == SpecialType.System_Single) return "float32";
            if (type.SpecialType == SpecialType.System_Double) return "float64";
            if (type.SpecialType == SpecialType.System_String) return "string";
            if (type is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_Byte) return "binary";
            return null;
        }

        private static void Emit(SourceProductionContext context, Compilation compilation, ImmutableArray<ComponentMessagePackTypeModel> items)
        {
            var types = items.Where(t => t != null).OrderBy(t => t.Type.ToDisplayString(), StringComparer.Ordinal).ToArray();
            foreach (var type in types)
            {
                if (string.IsNullOrWhiteSpace(type.SchemaName))
                    context.ReportDiagnostic(Diagnostic.Create(ComponentMessagePackSourceGeneratorDiagnostics.InvalidSchemaRule, type.Type.Locations.FirstOrDefault(), type.Type.Name));
                if (type.Type.IsGenericType)
                    context.ReportDiagnostic(Diagnostic.Create(ComponentMessagePackSourceGeneratorDiagnostics.OpenShapeRule, type.Type.Locations.FirstOrDefault(), type.Type.Name));
                foreach (var duplicate in type.Members.GroupBy(m => m.WireName, StringComparer.Ordinal).Where(g => g.Count() > 1))
                    context.ReportDiagnostic(Diagnostic.Create(ComponentMessagePackSourceGeneratorDiagnostics.DuplicateWireNameRule, type.Type.Locations.FirstOrDefault(), type.Type.Name, duplicate.Key));
                if (!type.Ignored && (type.Members.Count == 0 || type.Members.Any(m => m.CanonicalType == null)))
                    context.ReportDiagnostic(Diagnostic.Create(ComponentMessagePackSourceGeneratorDiagnostics.UnsupportedShapeRule, type.Type.Locations.FirstOrDefault(), type.Type.Name));
            }
            var manifestHash = ComputeManifestHash(types);
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using Unity.FoxgloveSDK.Components.Publishing.MessagePack;");
            sb.AppendLine("namespace Unity.FoxgloveSDK.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class __Unity2FoxgloveComponentMessagePackManifest");
            sb.AppendLine("    {");
            sb.AppendLine("        internal static ComponentMessagePackGeneratedManifest Create()");
            sb.AppendLine("        {");
            sb.AppendLine("            return new ComponentMessagePackGeneratedManifest(\"" + Escape(compilation.AssemblyName ?? string.Empty) + "\", \"" + manifestHash + "\", new ComponentMessagePackGeneratedEntry[]");
            sb.AppendLine("            {");
            foreach (var type in types)
            {
                var method = "Serialize_" + Sanitize(type.Type.Name);
                var supported = !type.Ignored && !string.IsNullOrWhiteSpace(type.SchemaName) && !type.Type.IsGenericType && type.Members.Count > 0 && type.Members.All(m => m.CanonicalType != null) && !type.Members.GroupBy(m => m.WireName, StringComparer.Ordinal).Any(g => g.Count() > 1);
                var reason = type.Ignored ? "excluded by ComponentMessagePackIgnoreAttribute" : supported ? string.Empty : "unsupported typed MessagePack member or shape";
                sb.Append("                new ComponentMessagePackGeneratedEntry(typeof(").Append(type.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append("), \"").Append(Escape(type.SchemaName)).Append("\", \"").Append(Escape(type.Type.ToDisplayString())).Append("\", ");
                if (supported)
                    sb.Append("true, true, \"\", ").Append(method);
                else
                    sb.Append("false, false, \"").Append(Escape(reason)).Append("\", null");
                sb.AppendLine("),");
            }
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            sb.AppendLine("#if UNITY_5_3_OR_NEWER");
            sb.AppendLine("        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            sb.AppendLine("        private static void RegisterRuntime() => ComponentMessagePackGeneratedBootstrap.RegisterGenerated(Create());");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        [UnityEditor.InitializeOnLoadMethod]");
            sb.AppendLine("        private static void RegisterEditor() => ComponentMessagePackGeneratedBootstrap.RegisterGenerated(Create());");
            sb.AppendLine("#endif");
            sb.AppendLine("#endif");
            foreach (var type in types)
            {
                var supported = !type.Ignored && !string.IsNullOrWhiteSpace(type.SchemaName) && !type.Type.IsGenericType && type.Members.Count > 0 && type.Members.All(m => m.CanonicalType != null) && !type.Members.GroupBy(m => m.WireName, StringComparer.Ordinal).Any(g => g.Count() > 1);
                if (!supported) continue;
                var method = "Serialize_" + Sanitize(type.Type.Name);
                sb.Append("        private static byte[] ").Append(method).Append("(object value)\n        {\n            var typed = (" ).Append(type.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(")value;\n            using (var writer = new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackWriter())\n            {\n                writer.WriteMapHeader(" + type.Members.Count + ");");
                foreach (var member in type.Members.OrderBy(m => m.WireName, StringComparer.Ordinal).ThenBy(m => m.MemberName, StringComparer.Ordinal))
                {
                    sb.Append("                writer.WriteString(\"").Append(Escape(member.WireName)).AppendLine("\");");
                    var shape = member.CanonicalType == "binary" ? FoxRunTypeShape.Collection(FoxRunCollectionKind.Binary, FoxRunTypeShape.Canonical("uint8")) : FoxRunTypeShape.Canonical(member.CanonicalType);
                    var code = new StringBuilder();
                    TypedMessagePackWriterEmitter.EmitValue(code, shape, "typed." + member.MemberName, "writer", "                ");
                    sb.Append(code);
                }
                sb.AppendLine("                return writer.ToArray();\n            }\n        }");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            context.AddSource("ComponentMessagePackManifest.g.cs", sb.ToString());
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string Sanitize(string value) => new string((value ?? "Type").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        private static string ComputeManifestHash(IEnumerable<ComponentMessagePackTypeModel> types)
        {
            var canonical = string.Join("\n", types.Select(t => t.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" + t.SchemaName + "|" + string.Join(",", t.Members.OrderBy(m => m.WireName, StringComparer.Ordinal).ThenBy(m => m.MemberName, StringComparer.Ordinal).Select(m => m.WireName + ":" + (m.CanonicalType ?? "unsupported")))));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
