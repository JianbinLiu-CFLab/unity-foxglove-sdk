using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal sealed class ComponentMessagePackTypeModel
    {
        internal ComponentMessagePackTypeModel(INamedTypeSymbol type, string schemaName, bool ignored, IReadOnlyList<ComponentMessagePackMemberModel> members)
        { Type = type; SchemaName = schemaName ?? string.Empty; Ignored = ignored; Members = members; }
        internal INamedTypeSymbol Type { get; }
        internal string SchemaName { get; }
        internal bool Ignored { get; }
        internal IReadOnlyList<ComponentMessagePackMemberModel> Members { get; }
    }
    internal sealed class ComponentMessagePackMemberModel
    {
        internal ComponentMessagePackMemberModel(string memberName, string wireName, string typeName, string canonicalType)
        { MemberName = memberName; WireName = wireName; TypeName = typeName; CanonicalType = canonicalType; }
        internal string MemberName { get; }
        internal string WireName { get; }
        internal string TypeName { get; }
        internal string CanonicalType { get; }
    }
}
