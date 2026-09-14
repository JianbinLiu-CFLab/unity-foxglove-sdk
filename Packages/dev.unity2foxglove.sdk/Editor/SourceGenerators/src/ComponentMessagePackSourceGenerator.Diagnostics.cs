using Microsoft.CodeAnalysis;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class ComponentMessagePackSourceGeneratorDiagnostics
    {
        internal const string DuplicateWireName = "FOXCOMP001";
        internal const string UnsupportedShape = "FOXCOMP002";
        internal const string InvalidSchema = "FOXCOMP003";
        internal const string OpenShape = "FOXCOMP004";
        internal const string InvariantFailure = "FOXCOMP005";

        internal static readonly DiagnosticDescriptor DuplicateWireNameRule = new DiagnosticDescriptor(
            DuplicateWireName, "Duplicate MessagePack wire name",
            "Type '{0}' declares duplicate effective wire name '{1}'", "ComponentMessagePack", DiagnosticSeverity.Warning, true);
        internal static readonly DiagnosticDescriptor UnsupportedShapeRule = new DiagnosticDescriptor(
            UnsupportedShape, "Unsupported MessagePack shape",
            "Type '{0}' contains a member shape that is not supported by the generated codec", "ComponentMessagePack", DiagnosticSeverity.Info, true);
        internal static readonly DiagnosticDescriptor InvalidSchemaRule = new DiagnosticDescriptor(
            InvalidSchema, "Invalid MessagePack schema", "Type '{0}' must declare a non-empty Foxglove schema name", "ComponentMessagePack", DiagnosticSeverity.Warning, true);
        internal static readonly DiagnosticDescriptor OpenShapeRule = new DiagnosticDescriptor(
            OpenShape, "Open MessagePack shape", "Type '{0}' contains an open generic shape", "ComponentMessagePack", DiagnosticSeverity.Info, true);
        internal static readonly DiagnosticDescriptor InvariantFailureRule = new DiagnosticDescriptor(
            InvariantFailure, "MessagePack generator invariant failure", "Type '{0}' could not be lowered to a deterministic codec", "ComponentMessagePack", DiagnosticSeverity.Error, true);
    }
}
