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

            context.RegisterSourceOutput(
                members.Collect(),
                static (spc, items) => Generate(spc, items));

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
                float rateHz = 10f;
                string schemaName = "";
                int publishMode = 0;
                int mode = 0;
                float changeEpsilon = 0f;
                float forceIntervalSeconds = 0f;
                string when = "";
                string unless = "";
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "RateHz" && TryReadFloatConstant(named.Value, out var rate)) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "PublishMode" && TryReadIntConstant(named.Value, out var pm)) publishMode = pm;
                    if (named.Key == "Mode" && TryReadIntConstant(named.Value, out var flowMode)) mode = flowMode;
                    if (named.Key == "ChangeEpsilon" && TryReadFloatConstant(named.Value, out var eps)) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && TryReadFloatConstant(named.Value, out var fis)) forceIntervalSeconds = fis;
                    if (named.Key == "When" && named.Value.Value is string whenValue) when = whenValue;
                    if (named.Key == "Unless" && named.Value.Value is string unlessValue) unless = unlessValue;
                }
                topics.Add(new TopicEntry(topic, rateHz, schemaName, publishMode, changeEpsilon, forceIntervalSeconds, when, unless, mode: mode));
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
                var rateHz = 10f;
                var schemaName = "";
                var publishMode = 0;
                var changeEpsilon = 0f;
                var forceIntervalSeconds = 0f;
                var when = "";
                var unless = "";
                foreach (var named in messageAttr.NamedArguments)
                {
                    if (named.Key == "RateHz" && TryReadFloatConstant(named.Value, out var rate)) rateHz = rate;
                    if (named.Key == "SchemaName" && named.Value.Value is string sn) schemaName = sn;
                    if (named.Key == "PublishMode" && TryReadIntConstant(named.Value, out var pm)) publishMode = pm;
                    if (named.Key == "ChangeEpsilon" && TryReadFloatConstant(named.Value, out var eps)) changeEpsilon = eps;
                    if (named.Key == "ForceIntervalSeconds" && TryReadFloatConstant(named.Value, out var fis)) forceIntervalSeconds = fis;
                    if (named.Key == "When" && named.Value.Value is string whenValue) when = whenValue;
                    if (named.Key == "Unless" && named.Value.Value is string unlessValue) unless = unlessValue;
                }

                if (string.IsNullOrWhiteSpace(schemaName))
                    schemaName = DeclaringTypeName(containingType);

                var jsonFieldName = ReadStringConstructorArgument(aggregateFieldAttr);
                topics.Add(new TopicEntry(
                    topic,
                    rateHz,
                    schemaName,
                    publishMode,
                    changeEpsilon,
                    forceIntervalSeconds,
                    when,
                    unless,
                    isAggregateMember: true,
                    jsonFieldName: jsonFieldName));
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

            var hasInboundTopic = topics.Any(topic => topic.Mode == 1 || topic.Mode == 2);
            if (hasInboundTopic
                && ((symbol is IFieldSymbol inboundField && inboundField.IsReadOnly)
                    || (symbol is IPropertySymbol inboundProperty && inboundProperty.SetMethod == null)))
            {
                return MemberData.ForDiagnostic(memberLocation, "FOXRUN028");
            }

            var memberType = typeSymbol == null ? "object" : typeSymbol.ToDisplayString();
            var emissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(memberType);
            var isValueType = typeSymbol?.IsValueType == true;
            var isArray = TryGetArrayElementType(typeSymbol, out var elementType);
            var elementTypeName = elementType == null ? "" : elementType.ToDisplayString();
            var rawMemberOrder = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? 0;

            string ns = containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace.IsGlobalNamespace
                ? containingType.ContainingNamespace.ToDisplayString() : "";

            return new MemberData(ns, containingType.Name, isPartial, memberName, memberKind, memberType, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, topics.ToArray());
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
                    || TryGetConditionDiagnostic(containingType, topic.Unless, "FOXRUN029", out diagnosticId))
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
        private static void Generate(SourceProductionContext spc, ImmutableArray<MemberData> items)
        {
            var roslynMemberCapacity = 0;
            foreach (var item in items)
                if (item?.DiagnosticLocation == null)
                    roslynMemberCapacity += item.Topics?.Length ?? 0;

            var roslynMembers = new List<FoxRunRoslynGenerationMember>(
                roslynMemberCapacity > 0 ? roslynMemberCapacity : items.Length);
            var memberLocations = new Dictionary<string, Location>(items.Length);
            var firstMemberByClass = new Dictionary<(string Ns, string ClassName), MemberData>();
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

                var key = (item.Ns, item.ClassName);
                if (!firstMemberByClass.ContainsKey(key))
                    firstMemberByClass.Add(key, item);
            }

            if (roslynMembers.Count == 0) return;

            var model = FoxRunRoslynGenerationModelLowerer.Lower(roslynMembers);
            var sharedDiagnostics = FoxRunGenerationModelValidator.Validate(model);
            var invalidDeclaringTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var diagnostic in sharedDiagnostics)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diags.Shared(diagnostic.Id), LocationFor(diagnostic, memberLocations), diagnostic.Target));
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
                EmitClass(spc, type);
            }

            var descriptor = FoxRunGenerationDescriptorJsonWriter.Write(
                new FoxRunGenerationModel(emittedTypes, model.DescriptorVersion, model.GeneratorVersion));
            spc.AddSource("FoxRunGeneratedDescriptorInfo.g.cs", FoxRunDescriptorCarrierEmitter.DescriptorCarrierSource(descriptor));
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
    }
}
