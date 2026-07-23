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
    /// <c>[FoxRun]</c> fields/properties and <c>[FoxService]</c> methods on
    /// partial classes, then emits FoxRun log-source and FoxService wrapper
    /// implementation source at Editor compile time.
    /// </summary>
    [Generator]
    public class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
        private const string AttrShortName = "FoxRun";
        private const string AttrAttributeName = "FoxRunAttribute";
        private const string AttrFullName = "Unity.FoxgloveSDK.Components.FoxRunAttribute";
        private const string AttrQualifiedNameSuffix = ".FoxRun";
        private const string AttrQualifiedAttributeNameSuffix = ".FoxRunAttribute";
        private const string MessageAttrShortName = "FoxRunMessage";
        private const string MessageAttrAttributeName = "FoxRunMessageAttribute";
        private const string MessageAttrFullName = "Unity.FoxgloveSDK.Components.FoxRunMessageAttribute";
        private const string MessageAttrQualifiedNameSuffix = ".FoxRunMessage";
        private const string MessageAttrQualifiedAttributeNameSuffix = ".FoxRunMessageAttribute";
        private const string FieldAttrShortName = "FoxRunField";
        private const string FieldAttrAttributeName = "FoxRunFieldAttribute";
        private const string FieldAttrFullName = "Unity.FoxgloveSDK.Components.FoxRunFieldAttribute";
        private const string FieldAttrQualifiedNameSuffix = ".FoxRunField";
        private const string FieldAttrQualifiedAttributeNameSuffix = ".FoxRunFieldAttribute";
        private const string ServiceAttrShortName = "FoxService";
        private const string ServiceAttrAttributeName = "FoxServiceAttribute";
        private const string ServiceAttrFullName = "Unity.FoxgloveSDK.Components.FoxServiceAttribute";
        private const string ServiceAttrQualifiedNameSuffix = ".FoxService";
        private const string ServiceAttrQualifiedAttributeNameSuffix = ".FoxServiceAttribute";

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

            var nativeCompilationEvidence = context.CompilationProvider.Select(
                static (compilation, _) => NativeCompilationEvidence.FromCompilation(compilation));

            context.RegisterSourceOutput(
                members.Collect().Combine(nativeCompilationEvidence),
                static (spc, input) => Generate(spc, input.Left, input.Right));

            var services = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsServiceCandidate(node),
                transform: static (ctx, ct) => ExtractServiceMethod(ctx, ct))
                .Where(static m => m != null);

            context.RegisterSourceOutput(
                services.Collect(),
                static (spc, items) => GenerateServices(spc, items));
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
        /// Checks whether any attribute in the given lists matches <c>FoxRun</c>
        /// or <c>FoxRunField</c> by short or fully-qualified name.
        /// </summary>
        private static bool HasFoxRunAttr(SyntaxList<AttributeListSyntax> lists)
        {
            foreach (var al in lists)
                foreach (var a in al.Attributes)
                {
                    var name = a.Name.ToString();
                    if (name == AttrShortName || name == AttrAttributeName
                        || name.EndsWith(AttrQualifiedNameSuffix, StringComparison.Ordinal)
                        || name.EndsWith(AttrQualifiedAttributeNameSuffix, StringComparison.Ordinal)
                        || name == FieldAttrShortName || name == FieldAttrAttributeName
                        || name.EndsWith(FieldAttrQualifiedNameSuffix, StringComparison.Ordinal)
                        || name.EndsWith(FieldAttrQualifiedAttributeNameSuffix, StringComparison.Ordinal))
                        return true;
                }
            return false;
        }

        private static bool IsServiceCandidate(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax method
                   && method.AttributeLists.Count > 0
                   && HasFoxServiceAttr(method.AttributeLists);
        }

        private static bool HasFoxServiceAttr(SyntaxList<AttributeListSyntax> lists)
        {
            foreach (var al in lists)
                foreach (var a in al.Attributes)
                {
                    var name = a.Name.ToString();
                    if (name == ServiceAttrShortName || name == ServiceAttrAttributeName
                        || name.EndsWith(ServiceAttrQualifiedNameSuffix, StringComparison.Ordinal)
                        || name.EndsWith(ServiceAttrQualifiedAttributeNameSuffix, StringComparison.Ordinal))
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

            var containingType = symbol.ContainingType;
            if (containingType == null) return null;

            var memberLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
            var topics = new List<TopicEntry>();
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != AttrFullName)
                    continue;

                string topic = ReadStringConstructorArgument(attr);
                float rateHz = -1f;
                string schemaName = "";
                int policy = 1;
                int mode = 1;
                int encoding = 0;
                int subscriptionProvider = 0;
                int ros2Qos = 0;
                int protobufFieldNumber = 0;
                float changeEpsilon = 0f;
                float forceIntervalSeconds = 0f;
                string when = "";
                string unless = "";
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "RateHz" && TryReadFloatConstant(named.Value, out var rate)) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "Policy" && TryReadIntConstant(named.Value, out var pm)) policy = pm;
                    if (named.Key == "Mode" && TryReadIntConstant(named.Value, out var flow)) mode = flow;
                    if (named.Key == "Encoding" && TryReadIntConstant(named.Value, out var wireEncoding)) encoding = wireEncoding;
                    if (named.Key == "SubscriptionProvider" && TryReadIntConstant(named.Value, out var provider)) subscriptionProvider = provider;
                    if (named.Key == "Ros2Qos" && TryReadIntConstant(named.Value, out var qos)) ros2Qos = qos;
                    if (named.Key == "ProtobufFieldNumber" && TryReadIntConstant(named.Value, out var fieldNumber)) protobufFieldNumber = fieldNumber;
                    if (named.Key == "ChangeEpsilon" && TryReadFloatConstant(named.Value, out var eps)) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && TryReadFloatConstant(named.Value, out var fis)) forceIntervalSeconds = fis;
                    if (named.Key == "When" && named.Value.Value is string whenValue) when = whenValue;
                    if (named.Key == "Unless" && named.Value.Value is string unlessValue) unless = unlessValue;
                }
                topics.Add(new TopicEntry(topic, rateHz, schemaName, policy, changeEpsilon, forceIntervalSeconds, when, unless, mode: mode, encoding: encoding, protobufFieldNumber: protobufFieldNumber, subscriptionProvider: subscriptionProvider, ros2Qos: ros2Qos));
            }

            var aggregateFieldAttr = symbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == FieldAttrFullName);
            if (aggregateFieldAttr != null)
            {
                if (symbol.IsStatic)
                    return MemberData.ForDiagnostic(memberLocation, "FOXRUN021");

                var messageAttr = containingType.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttrFullName);
                if (messageAttr == null)
                    return MemberData.ForDiagnostic(memberLocation, "FOXRUN018");

                var topic = ReadStringConstructorArgument(messageAttr);
                var rateHz = -1f;
                var schemaName = "";
                var policy = 1;
                var encoding = 0;
                var changeEpsilon = 0f;
                var forceIntervalSeconds = 0f;
                var when = "";
                var unless = "";
                foreach (var named in messageAttr.NamedArguments)
                {
                    if (named.Key == "RateHz" && TryReadFloatConstant(named.Value, out var rate)) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "Policy" && TryReadIntConstant(named.Value, out var pm)) policy = pm;
                    if (named.Key == "Encoding" && TryReadIntConstant(named.Value, out var wireEncoding)) encoding = wireEncoding;
                    if (named.Key == "ChangeEpsilon" && TryReadFloatConstant(named.Value, out var eps)) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && TryReadFloatConstant(named.Value, out var fis)) forceIntervalSeconds = fis;
                    if (named.Key == "When" && named.Value.Value is string whenValue) when = whenValue;
                    if (named.Key == "Unless" && named.Value.Value is string unlessValue) unless = unlessValue;
                }

                if (string.IsNullOrWhiteSpace(schemaName))
                    schemaName = DeclaringTypeName(containingType);

                var jsonFieldName = ReadStringConstructorArgument(aggregateFieldAttr);
                var protobufFieldNumber = 0;
                foreach (var named in aggregateFieldAttr.NamedArguments)
                {
                    if (named.Key == "ProtobufFieldNumber" && TryReadIntConstant(named.Value, out var fieldNumber))
                        protobufFieldNumber = fieldNumber;
                }
                topics.Add(new TopicEntry(
                    topic,
                    rateHz,
                    schemaName,
                    policy,
                    changeEpsilon,
                    forceIntervalSeconds,
                    when,
                    unless,
                    isAggregateMember: true,
                    jsonFieldName: jsonFieldName,
                    encoding: encoding,
                    protobufFieldNumber: protobufFieldNumber));
            }
            if (topics.Count == 0) return null;

            if (TryGetConditionDiagnostic(containingType, topics, out var conditionDiagnosticId))
                return MemberData.ForDiagnostic(memberLocation, conditionDiagnosticId);

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

            var hasInboundTopic = topics.Any(topic => topic.Mode == 2 || topic.Mode == 3);
            if (hasInboundTopic
                && ((symbol is IFieldSymbol inboundField && inboundField.IsReadOnly)
                    || (symbol is IPropertySymbol inboundProperty && inboundProperty.SetMethod == null)))
            {
                return MemberData.ForDiagnostic(memberLocation, "FOXRUN203");
            }

            var memberType = typeSymbol == null ? "object" : typeSymbol.ToDisplayString();
            var emissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(memberType);
            var isValueType = typeSymbol?.IsValueType == true;
            var isArray = TryGetArrayElementType(typeSymbol, out var elementType);
            var elementTypeName = elementType == null ? "" : elementType.ToDisplayString();
            var rawMemberOrder = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? 0;
            FoxRunRoslynProtobufTypeShapeBuilder.TryBuild(
                isArray ? elementType : typeSymbol,
                out var protobufTypeShape);
            var ros2MessageShape = FoxRunRoslynRos2MessageShapeBuilder.Build(
                typeSymbol,
                ctx.SemanticModel.Compilation);
            var isTopLevelPackagedCollection = IsTopLevelPackagedRos2MessageCollection(
                typeSymbol,
                ctx.SemanticModel.Compilation);
            // Native output is a Manager route, not a subscription-provider
            // declaration.  Build the portable custom DTO shape for every
            // ordinary FoxRun DTO so Publish contracts can opt into the
            // custom native publisher without pretending to be native input.
            var ros2CustomDtoShape = !ros2MessageShape.ImplementsRos2Message
                                     && !isTopLevelPackagedCollection
                ? FoxRunRoslynRos2CustomDtoShapeBuilder.Build(
                    typeSymbol,
                    ctx.SemanticModel.Compilation)
                : null;
            if (!ros2MessageShape.ImplementsRos2Message && !isTopLevelPackagedCollection)
                ros2MessageShape = null;

            var ros2ContractKind = ros2MessageShape != null
                ? FoxRunRos2ContractKind.PackagedRos2Message
                : ros2CustomDtoShape != null
                    ? FoxRunRos2ContractKind.CustomDto
                    : FoxRunRos2ContractKind.Unsupported;

            string ns = containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString() : "";

            return new MemberData(ns, containingType.Name, isPartial, memberName, memberKind, memberType, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, topics.ToArray(), protobufTypeShape, ros2MessageShape, ros2CustomDtoShape, ros2ContractKind);
        }

        private static string DeclaringTypeName(INamedTypeSymbol containingType)
        {
            if (containingType == null)
                return string.Empty;

            var ns = containingType.ContainingNamespace != null
                     && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString()
                : string.Empty;
            return string.IsNullOrEmpty(ns)
                ? containingType.Name
                : ns + "." + containingType.Name;
        }

        private static string ReadStringConstructorArgument(AttributeData attr)
        {
            if (attr == null)
                return string.Empty;

            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string value)
            {
                return value;
            }

            if (attr.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
                && syntax.ArgumentList?.Arguments.Count > 0
                && syntax.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.Value is string syntaxValue)
            {
                return syntaxValue;
            }

            return string.Empty;
        }

        private static bool TryGetConditionDiagnostic(
            INamedTypeSymbol containingType,
            IEnumerable<TopicEntry> topics,
            out string diagnosticId)
        {
            foreach (var topic in topics)
            {
                if (TryGetConditionDiagnostic(containingType, topic.When, "FOXRUN015", out diagnosticId)
                    || TryGetConditionDiagnostic(containingType, topic.Unless, "FOXRUN601", out diagnosticId))
                {
                    return true;
                }
            }

            diagnosticId = string.Empty;
            return false;
        }

        private static bool TryGetConditionDiagnostic(INamedTypeSymbol containingType, string conditionName, string missingDiagnosticId, out string diagnosticId)
        {
            diagnosticId = string.Empty;
            if (containingType == null || string.IsNullOrWhiteSpace(conditionName))
                return false;

            if (!SyntaxFacts.IsValidIdentifier(conditionName))
            {
                diagnosticId = missingDiagnosticId;
                return true;
            }

            var candidates = containingType.GetMembers(conditionName);
            if (candidates.Length == 0)
            {
                diagnosticId = missingDiagnosticId;
                return true;
            }

            if (candidates.Any(IsBoolConditionMember))
                return false;

            diagnosticId = "FOXRUN016";
            return true;
        }

        private static bool IsBoolConditionMember(ISymbol symbol)
        {
            switch (symbol)
            {
                case IFieldSymbol field:
                    return IsBoolType(field.Type);
                case IPropertySymbol property:
                    return IsBoolType(property.Type);
                default:
                    return false;
            }
        }

        private static bool IsBoolType(ITypeSymbol type)
            => type != null && type.SpecialType == SpecialType.System_Boolean;

        private static ServiceMethodData ExtractServiceMethod(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            if (!(ctx.Node is MethodDeclarationSyntax methodDecl))
                return null;

            var symbol = ctx.SemanticModel.GetDeclaredSymbol(methodDecl, ct);
            if (symbol == null)
                return null;

            AttributeData serviceAttr = null;
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ServiceAttrFullName)
                {
                    serviceAttr = attr;
                    break;
                }
            }
            if (serviceAttr == null)
                return null;

            var containingType = symbol.ContainingType;
            if (containingType == null)
                return null;

            string ns = containingType.ContainingNamespace != null
                        && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString()
                : string.Empty;
            var declaringTypeName = containingType.ToDisplayString();
            var className = containingType.Name;
            var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? Location.None;
            var diagnostics = new List<ServiceDiagnostic>();

            var isPartial = containingType.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(ct) is TypeDeclarationSyntax tds &&
                          tds.Modifiers.Any(SyntaxKind.PartialKeyword));
            var hasInvalidSignature = !isPartial;

            var serviceName = serviceAttr.ConstructorArguments.Length > 0
                ? serviceAttr.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;
            var serviceType = string.Empty;
            var description = string.Empty;
            var requestSchemaName = string.Empty;
            var responseSchemaName = string.Empty;
            foreach (var named in serviceAttr.NamedArguments)
            {
                if (named.Key == "Type" && named.Value.Value is string typeValue) serviceType = typeValue;
                if (named.Key == "Description" && named.Value.Value is string descValue) description = descValue;
                if (named.Key == "RequestSchemaName" && named.Value.Value is string reqValue) requestSchemaName = reqValue;
                if (named.Key == "ResponseSchemaName" && named.Value.Value is string respValue) responseSchemaName = respValue;
            }

            if (string.IsNullOrWhiteSpace(serviceName) || !serviceName.StartsWith("/", StringComparison.Ordinal))
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE001", location, serviceName));

            if (symbol.IsStatic || symbol.IsGenericMethod || methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword))
                hasInvalidSignature = true;

            if (symbol.Parameters.Length > 1)
                hasInvalidSignature = true;

            ITypeSymbol requestType = null;
            if (symbol.Parameters.Length == 1)
            {
                var parameter = symbol.Parameters[0];
                if (parameter.RefKind != RefKind.None || parameter.IsParams)
                    hasInvalidSignature = true;
                requestType = parameter.Type;
                diagnostics.AddRange(FoxServiceRoslynDtoValidator.ValidateServiceDtoType(
                    requestType,
                    FoxServiceDtoRules.RequestSide,
                    "Request",
                    serviceName,
                    location));
            }

            var hasResponse = !symbol.ReturnsVoid;
            var responseType = hasResponse ? symbol.ReturnType : null;
            if (hasResponse)
                diagnostics.AddRange(FoxServiceRoslynDtoValidator.ValidateServiceDtoType(
                    responseType,
                    FoxServiceDtoRules.ResponseSide,
                    "Response",
                    serviceName,
                    location));

            if (hasInvalidSignature)
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE002", location, symbol.Name));

            var hasExplicitMetadata = !string.IsNullOrWhiteSpace(serviceType)
                                      && !string.IsNullOrWhiteSpace(requestSchemaName)
                                      && !string.IsNullOrWhiteSpace(responseSchemaName);
            if (!hasExplicitMetadata)
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE006", location, serviceName));

            if (string.IsNullOrWhiteSpace(serviceType))
                serviceType = declaringTypeName + "." + symbol.Name;
            if (string.IsNullOrWhiteSpace(requestSchemaName))
                requestSchemaName = requestType == null
                    ? serviceType + ".Request"
                    : requestType.ToDisplayString();
            if (string.IsNullOrWhiteSpace(responseSchemaName))
                responseSchemaName = responseType == null
                    ? serviceType + ".Response"
                    : responseType.ToDisplayString();

            var shouldSkipSchemaPreview = diagnostics.Any(FoxServiceRoslynSchemaBuilder.IsBlockingSchemaPreviewDiagnostic);
            var requestSchema = shouldSkipSchemaPreview
                ? FoxServiceRoslynSchemaBuilder.EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(FoxServiceRoslynSchemaBuilder.Build(requestType, FoxServiceDtoRules.RequestSide, 0));
            var responseSchema = shouldSkipSchemaPreview
                ? FoxServiceRoslynSchemaBuilder.EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(FoxServiceRoslynSchemaBuilder.Build(responseType, FoxServiceDtoRules.ResponseSide, 0));

            return new ServiceMethodData(
                ns,
                className,
                symbol.Name,
                serviceName,
                serviceType,
                description,
                requestSchemaName,
                responseSchemaName,
                requestSchema,
                responseSchema,
                TypeNameForEmission(requestType),
                TypeNameForEmission(responseType),
                requestType != null,
                hasResponse,
                location,
                diagnostics.ToArray());
        }

        private static string TypeNameForEmission(ITypeSymbol type)
            => type == null
                ? string.Empty
                : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
            catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException)
            {
                return false;
            }
        }

        private static bool TryReadIntConstant(TypedConstant constant, out int value)
        {
            value = 0;
            if (constant.Value == null)
                return false;

            try
            {
                value = Convert.ToInt32(constant.Value);
                return true;
            }
            catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Entry point for source output: reports diagnostics, groups members by
        /// enclosing class, and emits one generated partial class per valid group.
        /// </summary>
        private static void Generate(
            SourceProductionContext spc,
            ImmutableArray<MemberData> items,
            NativeCompilationEvidence nativeCompilationEvidence)
        {
            var roslynMemberCapacity = 0;
            foreach (var item in items)
                if (item?.DiagnosticLocation == null)
                    roslynMemberCapacity += item.Topics?.Length ?? 0;

            var roslynMembers = new List<FoxRunRoslynGenerationMember>(
                roslynMemberCapacity > 0 ? roslynMemberCapacity : items.Length);
            var memberLocations = new Dictionary<string, Location>(items.Length);
            var firstMemberByClass = new Dictionary<(string Ns, string ClassName), MemberData>();
            var missingNativeReferenceTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                if (item.DiagnosticLocation != null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.Member(item.DiagnosticId), item.DiagnosticLocation));
                    continue;
                }

                item.AppendRoslynMembers(roslynMembers);
                memberLocations[MemberLocationKey(item.Ns, item.ClassName, item.MemberName)] = item.MemberLocation;

                if (nativeCompilationEvidence.HasNativeDefine
                    && !nativeCompilationEvidence.HasNativeAssemblyReference
                    && item.Ros2MessageShape != null
                    && item.Ros2MessageShape.ImplementsRos2Message
                    && item.Ros2MessageShape.HasPublicParameterlessConstructor
                    && item.Ros2MessageShape.Diagnostics.Count == 0
                    && item.Topics.Any(topic =>
                        topic.Mode == 2
                        && (topic.SubscriptionProvider == 0
                            || topic.SubscriptionProvider == 2)))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diags.MissingNativeAssemblyReference,
                        item.MemberLocation,
                        item.MemberName));
                    missingNativeReferenceTypes.Add(
                        string.IsNullOrEmpty(item.Ns)
                            ? item.ClassName
                            : item.Ns + "." + item.ClassName);
                }

                var key = (item.Ns, item.ClassName);
                if (!firstMemberByClass.ContainsKey(key))
                    firstMemberByClass.Add(key, item);
            }

            if (roslynMembers.Count == 0) return;

            var model = FoxRunRoslynGenerationModelLowerer.Lower(roslynMembers);
            var sharedDiagnostics = FoxRunGenerationModelValidator.Validate(model);
            // A missing optional Native reference invalidates only the dependent
            // conditional ROS2 partial. Keep emitting the ROS-free/WebSocket
            // portion so FOXRUN212 does not create a missing-type diagnostic
            // cascade or erase otherwise-valid generated behavior.
            var invalidDeclaringTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var diagnostic in sharedDiagnostics)
            {
                var messageArgument = Diags.SharedUsesDetailedMessage(diagnostic.Id)
                    ? diagnostic.Message
                    : diagnostic.Target;
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diags.Shared(diagnostic.Id),
                    LocationFor(diagnostic, memberLocations),
                    messageArgument));
                if (diagnostic.Severity == "Error")
                    invalidDeclaringTypes.Add(DiagnosticDeclaringType(diagnostic));
            }

            var emittedTypes = new List<FoxRunGenerationType>();
            foreach (var type in model.Types)
            {
                if (invalidDeclaringTypes.Contains(type.DeclaringType))
                    continue;

                var key = (type.Namespace, type.ClassName);
                if (!firstMemberByClass.TryGetValue(key, out var first))
                    continue;
                if (!first.IsPartial)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.NotPartial, Location.None, first.ClassName));
                    continue;
                }
                emittedTypes.Add(type);
                EmitClass(
                    spc,
                    type,
                    emitRos2NativePartial: !missingNativeReferenceTypes.Contains(type.DeclaringType));
            }

            var descriptor = FoxRunGenerationDescriptorJsonWriter.Write(
                new FoxRunGenerationModel(emittedTypes, model.DescriptorVersion, model.GeneratorVersion));
            spc.AddSource("FoxRunGeneratedDescriptorInfo.g.cs", FoxRunDescriptorCarrierEmitter.DescriptorCarrierSource(descriptor));
        }

        private readonly struct NativeCompilationEvidence
        {
            private const string NativeAssemblyName = "Unity2Foxglove.Ros2ForUnity.Native";
            private const string SubscriptionSourceMetadataName =
                "Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionSource";
            private const string SubscriptionRegistrarMetadataName =
                "Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar";
            private const string GeneratedContractMetadataName =
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract";
            private const string CopyContextMetadataName =
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext";
            private const string Ros2MessageMetadataName = "ROS2.Message";
            private const string FoxRunFlowMetadataName =
                "Unity.FoxgloveSDK.Components.FoxRunFlow";
            private const string FoxRunPolicyMetadataName =
                "Unity.FoxgloveSDK.Components.FoxRunPolicy";
            private const string SubscriptionProviderMetadataName =
                "Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider";
            private const string Ros2QosMetadataName =
                "Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset";

            public NativeCompilationEvidence(bool hasNativeDefine, bool hasNativeAssemblyReference)
            {
                HasNativeDefine = hasNativeDefine;
                HasNativeAssemblyReference = hasNativeAssemblyReference;
            }

            public bool HasNativeDefine { get; }
            public bool HasNativeAssemblyReference { get; }

            public static NativeCompilationEvidence FromCompilation(Compilation compilation)
            {
                var hasNativeDefine = compilation.SyntaxTrees.Any(
                    tree => tree.Options is CSharpParseOptions options
                            && options.PreprocessorSymbolNames.Contains(
                                "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                                StringComparer.Ordinal));
                var source = compilation.GetTypeByMetadataName(SubscriptionSourceMetadataName);
                var registrar = compilation.GetTypeByMetadataName(SubscriptionRegistrarMetadataName);
                var contract = compilation.GetTypeByMetadataName(GeneratedContractMetadataName);
                var context = compilation.GetTypeByMetadataName(CopyContextMetadataName);
                var ros2Message = compilation.GetTypeByMetadataName(Ros2MessageMetadataName);
                var hasNativeAssemblyReference = HasPublicNativeType(source, TypeKind.Interface)
                    && HasPublicNativeType(registrar, TypeKind.Interface)
                    && HasPublicNativeType(contract, TypeKind.Class)
                    && HasPublicNativeType(context, TypeKind.Class)
                    && ros2Message != null
                    && HasExactSourceSeam(compilation, source, registrar)
                    && HasExactRegistrarSeam(compilation, registrar, contract, context, ros2Message)
                    && HasExactContractConstructor(compilation, contract)
                    && HasExactCopyContextSeam(compilation, context);
                return new NativeCompilationEvidence(hasNativeDefine, hasNativeAssemblyReference);
            }

            private static bool HasPublicNativeType(INamedTypeSymbol symbol, TypeKind typeKind)
                => symbol != null
                   && symbol.TypeKind == typeKind
                   && symbol.DeclaredAccessibility == Accessibility.Public
                   && string.Equals(
                       symbol.ContainingAssembly?.Identity.Name,
                       NativeAssemblyName,
                       StringComparison.Ordinal);

            private static bool HasExactSourceSeam(
                Compilation compilation,
                INamedTypeSymbol source,
                INamedTypeSymbol registrar)
            {
                var count = source.GetMembers("FoxRunRos2SubscriptionCount")
                    .OfType<IPropertySymbol>()
                    .SingleOrDefault();
                if (count == null
                    || count.IsStatic
                    || count.IsIndexer
                    || count.DeclaredAccessibility != Accessibility.Public
                    || count.GetMethod == null
                    || count.SetMethod != null
                    || count.Type.SpecialType != SpecialType.System_Int32)
                    return false;

                return source.GetMembers("FoxRunRos2RegisterSubscriptions")
                    .OfType<IMethodSymbol>()
                    .Any(method => IsPublicInstanceOrdinaryVoid(method)
                                   && method.Arity == 0
                                   && method.Parameters.Length == 1
                                   && method.Parameters[0].RefKind == RefKind.None
                                   && SymbolEqualityComparer.Default.Equals(
                                       method.Parameters[0].Type,
                                       registrar));
            }

            private static bool HasExactRegistrarSeam(
                Compilation compilation,
                INamedTypeSymbol registrar,
                INamedTypeSymbol contract,
                INamedTypeSymbol context,
                INamedTypeSymbol ros2Message)
            {
                var func1 = compilation.GetTypeByMetadataName("System.Func`1");
                var func3 = compilation.GetTypeByMetadataName("System.Func`3");
                var func2 = compilation.GetTypeByMetadataName("System.Func`2");
                var action1 = compilation.GetTypeByMetadataName("System.Action`1");
                if (func1 == null || func3 == null || func2 == null || action1 == null)
                    return false;

                foreach (var method in registrar.GetMembers("Register").OfType<IMethodSymbol>())
                {
                    if (!IsPublicInstanceOrdinaryVoid(method)
                        || method.Arity != 1
                        || method.Parameters.Length != 7)
                        continue;
                    var typeParameter = method.TypeParameters[0];
                    if (!typeParameter.HasConstructorConstraint
                        || typeParameter.HasReferenceTypeConstraint
                        || typeParameter.HasValueTypeConstraint
                        || typeParameter.HasUnmanagedTypeConstraint
                        || typeParameter.ConstraintTypes.Length != 1
                        || !SymbolEqualityComparer.Default.Equals(
                            typeParameter.ConstraintTypes[0],
                            ros2Message))
                        continue;
                    var expected = new ITypeSymbol[]
                    {
                        contract,
                        func3.Construct(typeParameter, context, typeParameter),
                        action1.Construct(typeParameter),
                        action1.Construct(typeParameter),
                        func2.Construct(typeParameter, compilation.GetSpecialType(SpecialType.System_Boolean)),
                        func3.Construct(
                            typeParameter,
                            typeParameter,
                            compilation.GetSpecialType(SpecialType.System_Boolean)),
                        func1.Construct(compilation.GetSpecialType(SpecialType.System_Boolean))
                    };
                    var matches = true;
                    for (var i = 0; i < expected.Length; i++)
                    {
                        if (method.Parameters[i].RefKind != RefKind.None
                            || !SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, expected[i]))
                        {
                            matches = false;
                            break;
                        }
                    }
                    if (matches)
                        return true;
                }
                return false;
            }

            private static bool HasExactContractConstructor(
                Compilation compilation,
                INamedTypeSymbol contract)
            {
                var stringType = compilation.GetSpecialType(SpecialType.System_String);
                var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
                var mode = compilation.GetTypeByMetadataName(FoxRunFlowMetadataName);
                var policy = compilation.GetTypeByMetadataName(FoxRunPolicyMetadataName);
                var provider = compilation.GetTypeByMetadataName(SubscriptionProviderMetadataName);
                var qos = compilation.GetTypeByMetadataName(Ros2QosMetadataName);
                if (mode == null || policy == null || provider == null || qos == null)
                    return false;
                var floatType = compilation.GetSpecialType(SpecialType.System_Single);
                var expected = new ITypeSymbol[]
                {
                    stringType,
                    stringType,
                    stringType,
                    stringType,
                    stringType,
                    mode,
                    provider,
                    qos,
                    boolType,
                    policy,
                    floatType,
                    boolType,
                    floatType
                };
                return contract.InstanceConstructors.Any(constructor =>
                    constructor.DeclaredAccessibility == Accessibility.Public
                    && constructor.Parameters.Length == expected.Length
                    && constructor.Parameters.Select(parameter => parameter.Type)
                        .SequenceEqual(expected, SymbolEqualityComparer.Default)
                    && constructor.Parameters.All(parameter =>
                        parameter.RefKind == RefKind.None && !parameter.IsOptional));
            }

            private static bool HasExactCopyContextSeam(
                Compilation compilation,
                INamedTypeSymbol context)
                => context.GetMembers("RequireBytes")
                    .OfType<IMethodSymbol>()
                    .Any(method => IsPublicInstanceOrdinaryVoid(method)
                                   && method.Arity == 0
                                   && method.Parameters.Length == 1
                                   && method.Parameters[0].RefKind == RefKind.None
                                   && method.Parameters[0].Type.SpecialType == SpecialType.System_Int64);

            private static bool IsPublicInstanceOrdinaryVoid(IMethodSymbol method)
                => method != null
                   && !method.IsStatic
                   && method.MethodKind == MethodKind.Ordinary
                   && method.DeclaredAccessibility == Accessibility.Public
                   && method.ReturnsVoid;
        }

        private static string DiagnosticDeclaringType(FoxRunGenerationDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return string.Empty;

            var target = diagnostic.Target ?? string.Empty;
            var memberName = diagnostic.MemberName ?? string.Empty;
            if (memberName.Length == 0)
                return target;

            var memberSuffix = "." + memberName;
            return target.EndsWith(memberSuffix, StringComparison.Ordinal)
                ? target.Substring(0, target.Length - memberSuffix.Length)
                : target;
        }

        private static void GenerateServices(SourceProductionContext spc, ImmutableArray<ServiceMethodData> items)
        {
            if (items.IsDefaultOrEmpty)
                return;

            var valid = new List<ServiceMethodData>(items.Length);
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var hasError = false;
                foreach (var diagnostic in item.Diagnostics)
                {
                    var descriptor = Diags.Service(diagnostic.Id);
                    spc.ReportDiagnostic(Diagnostic.Create(descriptor, diagnostic.Location, diagnostic.Target));
                    if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                        hasError = true;
                }

                if (!hasError)
                    valid.Add(item);
            }

            var servicesByName = new Dictionary<string, List<ServiceMethodData>>(StringComparer.Ordinal);
            foreach (var item in valid)
            {
                if (!servicesByName.TryGetValue(item.ServiceName, out var list))
                {
                    list = new List<ServiceMethodData>();
                    servicesByName.Add(item.ServiceName, list);
                }
                list.Add(item);
            }

            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var duplicate in servicesByName)
            {
                if (duplicate.Value.Count <= 1)
                    continue;

                duplicateNames.Add(duplicate.Key);
                foreach (var item in duplicate.Value)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.DuplicateServiceName, item.Location, item.ServiceName));
            }

            var methodsByType = new Dictionary<(string Ns, string ClassName), List<ServiceMethodData>>();
            foreach (var item in valid)
            {
                if (duplicateNames.Contains(item.ServiceName))
                    continue;

                var key = (item.Ns, item.ClassName);
                if (!methodsByType.TryGetValue(key, out var list))
                {
                    list = new List<ServiceMethodData>();
                    methodsByType.Add(key, list);
                }
                list.Add(item);
            }

            foreach (var group in methodsByType)
            {
                var methods = group.Value
                    .OrderBy(item => item.ServiceName, StringComparer.Ordinal)
                    .Select(item => item.ToEmitterMethod())
                    .ToList();
                if (methods.Count == 0)
                    continue;

                var source = FoxServiceSourceEmitter.EmitClass(group.Key.Ns, group.Key.ClassName, methods);
                spc.AddSource(FoxServiceSourceEmitter.GeneratedSourceName(group.Key.Ns, group.Key.ClassName), source);
            }
        }

        /// <summary>
        /// Emits the generated partial class implementing <c>IFoxgloveLogSource</c>
        /// for one class name/namespace pair. Shared model validation handles
        /// topic warnings before this method delegates code generation to
        /// <c>FoxgloveSourceEmitter.EmitClass</c> for output
        /// consistency with the build-time physical fallback path.
        /// </summary>
        private static void EmitClass(
            SourceProductionContext spc,
            FoxRunGenerationType type,
            bool emitRos2NativePartial)
        {
            var ns = type.Namespace;
            var className = type.ClassName;
            var source = FoxgloveSourceEmitter.EmitClass(type, emitRos2NativePartial);
            spc.AddSource(FoxgloveSourceEmitter.GeneratedSourceName(ns, className), source);
        }

        private static Location LocationFor(FoxRunGenerationDiagnostic diagnostic, Dictionary<string, Location> memberLocations)
        {
            if (diagnostic == null || memberLocations == null)
                return Location.None;

            if (!string.IsNullOrEmpty(diagnostic.MemberName))
            {
                var declaringType = diagnostic.Target ?? string.Empty;
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

        private static bool IsTopLevelPackagedRos2MessageCollection(
            ITypeSymbol type,
            Compilation compilation)
        {
            if (!TryGetArrayElementType(type, out var elementType))
                return false;

            return FoxRunRoslynRos2MessageShapeBuilder.Build(elementType, compilation)
                .ImplementsRos2Message;
        }
    }
}
