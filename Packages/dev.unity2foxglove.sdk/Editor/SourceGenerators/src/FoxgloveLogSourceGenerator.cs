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
                diagnostics.AddRange(ValidateServiceDtoType(
                    requestType,
                    FoxServiceDtoRules.RequestSide,
                    "Request",
                    serviceName,
                    location));
            }

            var hasResponse = !symbol.ReturnsVoid;
            var responseType = hasResponse ? symbol.ReturnType : null;
            if (hasResponse)
                diagnostics.AddRange(ValidateServiceDtoType(
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

            var shouldSkipSchemaPreview = diagnostics.Any(IsBlockingSchemaPreviewDiagnostic);
            var requestSchema = shouldSkipSchemaPreview
                ? EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(BuildServiceSchema(requestType, FoxServiceDtoRules.RequestSide, 0));
            var responseSchema = shouldSkipSchemaPreview
                ? EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(BuildServiceSchema(responseType, FoxServiceDtoRules.ResponseSide, 0));

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

        private static FoxServiceSchemaModel BuildServiceSchema(ITypeSymbol type, string side, int depth)
            => BuildServiceSchema(
                type,
                side,
                depth,
                new Dictionary<string, FoxServiceSchemaModel>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

        private static string EmptyServiceSchemaPreview()
            => FoxServiceSchemaEmitter.Emit(FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>()));

        private static bool IsBlockingSchemaPreviewDiagnostic(ServiceDiagnostic diagnostic)
            => diagnostic != null
               && Diags.Service(diagnostic.Id).DefaultSeverity == DiagnosticSeverity.Error;

        private static FoxServiceSchemaModel BuildServiceSchema(
            ITypeSymbol type,
            string side,
            int depth,
            IDictionary<string, FoxServiceSchemaModel> memo,
            ISet<string> stack)
        {
            if (type == null || type.SpecialType == SpecialType.System_Void)
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (depth > FoxServiceDtoRules.MaxDepth)
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            type = UnwrapNullable(type);
            if (type is IArrayTypeSymbol array)
                return FoxServiceSchemaModel.ArrayOf(BuildServiceSchema(array.ElementType, side, depth + 1, memo, stack));

            if (!(type is INamedTypeSymbol named))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (TryGetJsonScalarType(named, out var scalar))
                return FoxServiceSchemaModel.Scalar(scalar);

            if (named.TypeKind == TypeKind.Enum)
                return FoxServiceSchemaModel.Scalar("integer");

            if (IsUnsupportedSchemaPreviewType(named))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (TryGetDictionaryValueType(named, out _, out var valueType))
                return FoxServiceSchemaModel.Dictionary(BuildServiceSchema(valueType, side, depth + 1, memo, stack));

            if (TryGetListElementType(named, side, out var elementType))
                return FoxServiceSchemaModel.ArrayOf(BuildServiceSchema(elementType, side, depth + 1, memo, stack));

            var typeKey = FullTypeName(named);
            if (memo.TryGetValue(typeKey, out var cached))
                return cached;
            if (!stack.Add(typeKey))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            var properties = new List<FoxServiceSchemaProperty>();
            foreach (var member in InheritedAndDeclaredMembers(named))
            {
                if (member.IsStatic || HasIgnoredSerializationAttribute(member))
                    continue;
                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(field), BuildServiceSchema(field.Type, side, depth + 1, memo, stack)));
                }
                else if (member is IPropertySymbol property)
                {
                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsIndexer
                        || property.GetMethod == null)
                        continue;
                    properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(property), BuildServiceSchema(property.Type, side, depth + 1, memo, stack)));
                }
            }

            var model = FoxServiceSchemaModel.Object(properties);
            stack.Remove(typeKey);
            memo[typeKey] = model;
            return model;
        }

        private static string JsonPropertyName(ISymbol member)
        {
            foreach (var attribute in member.GetAttributes())
            {
                var attributeName = FullTypeName(attribute.AttributeClass);
                if (attributeName != "Newtonsoft.Json.JsonPropertyAttribute")
                    continue;

                foreach (var namedArgument in attribute.NamedArguments)
                {
                    if (namedArgument.Key == "PropertyName"
                        && namedArgument.Value.Value is string namedValue
                        && !string.IsNullOrWhiteSpace(namedValue))
                        return namedValue;
                }

                if (attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is string constructorValue
                    && !string.IsNullOrWhiteSpace(constructorValue))
                    return constructorValue;
            }

            return member.Name;
        }

        private static bool IsUnsupportedSchemaPreviewType(INamedTypeSymbol named)
        {
            var fullName = FullTypeName(named);
            return named.TypeKind == TypeKind.Delegate
                   || named.TypeKind == TypeKind.Interface
                   || fullName == "System.Object"
                   || FoxServiceDtoTypeNames.IsTaskLike(fullName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                   || IsUnityObjectType(named);
        }

        private static bool TryGetJsonScalarType(INamedTypeSymbol named, out string jsonType)
        {
            jsonType = null;
            switch (named.SpecialType)
            {
                case SpecialType.System_Boolean:
                    jsonType = "boolean";
                    return true;
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    jsonType = "integer";
                    return true;
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    jsonType = "number";
                    return true;
                case SpecialType.System_String:
                case SpecialType.System_Char:
                    jsonType = "string";
                    return true;
            }

            var fullName = FullTypeName(named);
            if (fullName == "System.DateTime"
                || fullName == "System.DateTimeOffset"
                || fullName == "System.Guid"
                || fullName == "System.TimeSpan")
            {
                jsonType = "string";
                return true;
            }

            return false;
        }

        private static IEnumerable<ServiceDiagnostic> ValidateServiceDtoType(
            ITypeSymbol type,
            string side,
            string rootPath,
            string serviceName,
            Location location)
        {
            var diagnostics = new List<FoxServiceDtoDiagnostic>();
            var stack = new HashSet<string>(StringComparer.Ordinal);
            var validatedTypes = new HashSet<string>(StringComparer.Ordinal);
            ValidateServiceDtoType(type, side, rootPath, type, diagnostics, stack, validatedTypes, 0);
            return diagnostics.Select(diagnostic => new ServiceDiagnostic(
                diagnostic.Id,
                location,
                diagnostic.FormatTarget(serviceName)));
        }

        private static void ValidateServiceDtoType(
            ITypeSymbol type,
            string side,
            string path,
            ITypeSymbol rootType,
            List<FoxServiceDtoDiagnostic> diagnostics,
            HashSet<string> stack,
            HashSet<string> validatedTypes,
            int depth)
        {
            if (type == null || type.SpecialType == SpecialType.System_Void)
                return;

            type = UnwrapNullable(type);
            var typeName = DiagnosticTypeName(type);
            var rootName = DisplayTypeName(rootType);

            if (depth > FoxServiceDtoRules.MaxDepth)
            {
                AddDtoDiagnostic(FoxServiceDtoRules.DepthDiagnosticId, side, rootName, path, typeName, "DTO graph exceeds the supported traversal depth.", diagnostics);
                return;
            }

            if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.TypeParameter)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Pointer and open generic DTO members cannot be serialized safely.", diagnostics);
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1)
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Only single-dimensional arrays are supported.", diagnostics);
                    return;
                }

                ValidateServiceDtoType(array.ElementType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (!(type is INamedTypeSymbol named))
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "DTO member type is not a supported named type.", diagnostics);
                return;
            }

            if (named.IsUnboundGenericType
                || named.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter)
                || named.IsRefLikeType)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Open generic and by-ref-like DTO members are unsupported.", diagnostics);
                return;
            }

            if (IsScalarDtoType(named) || named.TypeKind == TypeKind.Enum)
                return;

            var fullName = FullTypeName(named);
            var stackKey = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (validatedTypes.Contains(stackKey))
                return;

            if (FoxServiceDtoTypeNames.IsTaskLike(fullName)
                || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                || FoxServiceDtoTypeNames.IsFunctionPointerLike(fullName)
                || IsDelegateType(named)
                || IsUnityObjectType(named)
                || named.SpecialType == SpecialType.System_Object)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "DTO member type is not JSON DTO serializable.", diagnostics);
                return;
            }

            if (TryGetDictionaryValueType(named, out var keyType, out var valueType))
            {
                if (!IsStringDtoType(UnwrapNullable(keyType)))
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Dictionary DTO members must use string keys.", diagnostics);
                    return;
                }

                ValidateServiceDtoType(valueType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (TryGetListElementType(named, side, out var elementType))
            {
                ValidateServiceDtoType(elementType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (named.TypeKind == TypeKind.Interface)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Interface DTO members are unsupported unless they are a known collection contract.", diagnostics);
                return;
            }

            if (!stack.Add(stackKey))
            {
                AddDtoDiagnostic(FoxServiceDtoRules.CycleDiagnosticId, side, rootName, path, typeName, "DTO graph contains a recursive reference.", diagnostics);
                return;
            }

            var diagnosticCountBeforeMembers = diagnostics.Count;
            foreach (var member in InheritedAndDeclaredMembers(named))
            {
                if (member.IsStatic)
                    continue;

                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    if (HasIgnoredSerializationAttribute(field))
                    {
                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.Type), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (field.IsReadOnly)
                    {
                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.Type), "Readonly fields may serialize but may not round-trip from request JSON.", diagnostics);
                        continue;
                    }
                    ValidateServiceDtoType(field.Type, side, path + "." + field.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                    continue;
                }

                if (member is IPropertySymbol property)
                {
                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsIndexer
                        || property.GetMethod == null)
                        continue;
                    if (HasIgnoredSerializationAttribute(property))
                    {
                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.Type), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (property.SetMethod == null)
                    {
                        if (TryGetListElementType(property.Type, side, out var getOnlyElementType)
                            && IsMutableCollectionContract(property.Type))
                        {
                            ValidateServiceDtoType(getOnlyElementType, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                            continue;
                        }
                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.Type), "Get-only properties are not populated during request deserialization.", diagnostics);
                        continue;
                    }
                    ValidateServiceDtoType(property.Type, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                }
            }

            stack.Remove(stackKey);
            if (diagnostics.Count == diagnosticCountBeforeMembers)
                validatedTypes.Add(stackKey);
        }

        private static IEnumerable<ISymbol> InheritedAndDeclaredMembers(INamedTypeSymbol type)
        {
            var seenJsonNames = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                var members = new List<ISymbol>(current.GetMembers().Length);
                foreach (var member in current.GetMembers())
                    members.Add(member);
                members.Sort((left, right) => MemberOrder(left).CompareTo(MemberOrder(right)));

                foreach (var member in members)
                {
                    if (member is IFieldSymbol field)
                    {
                        if (!CanParticipateInJsonNameDedup(field))
                            continue;
                        if (seenJsonNames.Add(JsonPropertyName(field)))
                            yield return field;
                    }
                    else if (member is IPropertySymbol property)
                    {
                        if (!CanParticipateInJsonNameDedup(property))
                            continue;
                        if (seenJsonNames.Add(JsonPropertyName(property)))
                            yield return property;
                    }
                }
            }
        }

        private static bool CanParticipateInJsonNameDedup(ISymbol member)
        {
            if (member.IsStatic)
                return false;

            if (member is IFieldSymbol field)
                return !field.IsConst && field.DeclaredAccessibility == Accessibility.Public;

            if (member is IPropertySymbol property)
                return property.DeclaredAccessibility == Accessibility.Public;

            return false;
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
            return type;
        }

        private static bool IsScalarDtoType(INamedTypeSymbol named)
            => IsPrimitiveDtoType(named) || FoxServiceDtoTypeNames.IsScalar(FullTypeName(named));

        private static bool IsPrimitiveDtoType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                case SpecialType.System_Char:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsStringDtoType(ITypeSymbol type)
            => type != null && type.SpecialType == SpecialType.System_String;

        private static bool TryGetListElementType(INamedTypeSymbol named, out ITypeSymbol elementType)
            => TryGetListElementType(named, FoxServiceDtoRules.RequestSide, out elementType);

        private static bool TryGetListElementType(ITypeSymbol type, string side, out ITypeSymbol elementType)
            => TryGetListElementType(type as INamedTypeSymbol, side, out elementType);

        private static bool TryGetListElementType(INamedTypeSymbol named, string side, out ITypeSymbol elementType)
        {
            elementType = null;
            if (named == null || named.TypeArguments.Length != 1)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            if (!FoxServiceDtoTypeNames.IsListContract(contract, side))
                return false;

            elementType = named.TypeArguments[0];
            return true;
        }

        private static bool IsMutableCollectionContract(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named) || named.TypeArguments.Length != 1)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return FoxServiceDtoTypeNames.IsMutableCollectionContract(contract);
        }

        private static bool TryGetDictionaryValueType(INamedTypeSymbol named, out ITypeSymbol keyType, out ITypeSymbol valueType)
        {
            keyType = null;
            valueType = null;
            if (named.TypeArguments.Length != 2)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            if (!FoxServiceDtoTypeNames.IsDictionaryContract(contract))
                return false;

            keyType = named.TypeArguments[0];
            valueType = named.TypeArguments[1];
            return true;
        }

        private static bool IsDelegateType(INamedTypeSymbol named)
        {
            for (var current = named; current != null; current = current.BaseType)
            {
                var fullName = FullTypeName(current);
                if (fullName == "System.Delegate" || fullName == "System.MulticastDelegate")
                    return true;
            }
            return false;
        }

        private static bool IsUnityObjectType(INamedTypeSymbol named)
        {
            for (var current = named; current != null; current = current.BaseType)
            {
                if (FullTypeName(current) == "UnityEngine.Object")
                    return true;
            }
            return false;
        }

        private static bool HasIgnoredSerializationAttribute(ISymbol symbol)
            => symbol.GetAttributes().Any(attribute =>
            {
                var name = attribute.AttributeClass == null ? string.Empty : FullTypeName(attribute.AttributeClass);
                return name == "Newtonsoft.Json.JsonIgnoreAttribute"
                       || name == "System.Text.Json.Serialization.JsonIgnoreAttribute"
                       || name == "System.NonSerializedAttribute";
            });

        private static int MemberOrder(ISymbol symbol)
        {
            foreach (var candidate in symbol.Locations)
            {
                if (candidate.IsInSource)
                    return candidate.SourceSpan.Start;
            }

            return int.MaxValue;
        }

        private static string FullTypeName(ITypeSymbol type)
            => type == null
                ? string.Empty
                : FoxServiceDtoTypeNames.Normalize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty));

        private static string DisplayTypeName(ITypeSymbol type)
            => type == null
                ? string.Empty
                : FoxServiceDtoTypeNames.Normalize(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        private static string DiagnosticTypeName(ITypeSymbol type)
        {
            if (type == null)
                return string.Empty;

            if (type.SpecialType == SpecialType.System_Object)
                return "object";

            if (IsPrimitiveDtoType(type))
                return DisplayTypeName(type);

            return FullTypeName(type);
        }

        private static void AddUnsupportedDtoDiagnostic(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.UnsupportedDiagnosticId(side), side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoWarning(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.WarningDiagnosticId, side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoDiagnostic(
            string id,
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => diagnostics.Add(new FoxServiceDtoDiagnostic(
                id,
                id == FoxServiceDtoRules.WarningDiagnosticId || id == FoxServiceDtoRules.DepthDiagnosticId,
                side,
                rootType,
                path,
                offendingType,
                reason));

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
            spc.AddSource("FoxRunGeneratedDescriptorInfo.g.cs", DescriptorCarrierSource(descriptor));
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

        private static string DescriptorCarrierSource(string descriptorJson)
        {
            var escaped = EscapeStringLiteral(descriptorJson);
            if (escaped.Length > 60000)
                return ChunkedDescriptorCarrierSource(escaped);

            return "// <auto-generated/>\n"
                   + "namespace Unity.FoxgloveSDK.Generated\n"
                   + "{\n"
                   + "    internal static class FoxRunGeneratedDescriptorInfo\n"
                   + "    {\n"
                   + "        public static readonly string DescriptorJson = \"" + escaped + "\";\n"
                   + "    }\n"
                   + "}\n";
        }

        private static string ChunkedDescriptorCarrierSource(string escapedDescriptorJson)
        {
            const int chunkSize = 16000;
            var chunkCount = (escapedDescriptorJson.Length + chunkSize - 1) / chunkSize;
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("namespace Unity.FoxgloveSDK.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class FoxRunGeneratedDescriptorInfo");
            sb.AppendLine("    {");
            sb.Append("        public static readonly string DescriptorJson = string.Concat(");
            for (var i = 0; i < chunkCount; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append("DescriptorJsonPart").Append(i.ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine(");");
            for (var i = 0; i < chunkCount; i++)
            {
                var start = i * chunkSize;
                var length = Math.Min(chunkSize, escapedDescriptorJson.Length - start);
                sb.Append("        private const string DescriptorJsonPart")
                    .Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append(" = \"")
                    .Append(escapedDescriptorJson.Substring(start, length))
                    .AppendLine("\";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
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
                        if (ch < 0x20 || char.IsHighSurrogate(ch) || char.IsLowSurrogate(ch))
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

        private sealed class ServiceDiagnostic
        {
            public ServiceDiagnostic(string id, Location location, string target)
            {
                Id = id;
                Location = location ?? Location.None;
                Target = target ?? string.Empty;
            }

            public string Id { get; }
            public Location Location { get; }
            public string Target { get; }
        }

        private sealed class ServiceMethodData
        {
            public ServiceMethodData(
                string ns,
                string className,
                string methodName,
                string serviceName,
                string serviceType,
                string description,
                string requestSchemaName,
                string responseSchemaName,
                string requestSchema,
                string responseSchema,
                string requestTypeName,
                string responseTypeName,
                bool hasRequest,
                bool hasResponse,
                Location location,
                ServiceDiagnostic[] diagnostics)
            {
                Ns = ns ?? string.Empty;
                ClassName = className ?? string.Empty;
                MethodName = methodName ?? string.Empty;
                ServiceName = serviceName ?? string.Empty;
                ServiceType = serviceType ?? string.Empty;
                Description = description ?? string.Empty;
                RequestSchemaName = requestSchemaName ?? string.Empty;
                ResponseSchemaName = responseSchemaName ?? string.Empty;
                RequestSchema = requestSchema ?? string.Empty;
                ResponseSchema = responseSchema ?? string.Empty;
                RequestTypeName = requestTypeName ?? string.Empty;
                ResponseTypeName = responseTypeName ?? string.Empty;
                HasRequest = hasRequest;
                HasResponse = hasResponse;
                Location = location ?? Location.None;
                Diagnostics = diagnostics ?? Array.Empty<ServiceDiagnostic>();
            }

            public string Ns { get; }
            public string ClassName { get; }
            public string MethodName { get; }
            public string ServiceName { get; }
            public string ServiceType { get; }
            public string Description { get; }
            public string RequestSchemaName { get; }
            public string ResponseSchemaName { get; }
            public string RequestSchema { get; }
            public string ResponseSchema { get; }
            public string RequestTypeName { get; }
            public string ResponseTypeName { get; }
            public bool HasRequest { get; }
            public bool HasResponse { get; }
            public Location Location { get; }
            public ServiceDiagnostic[] Diagnostics { get; }

            public FoxServiceSourceEmitter.ServiceMethod ToEmitterMethod()
            {
                return new FoxServiceSourceEmitter.ServiceMethod(
                    MethodName,
                    ServiceName,
                    ServiceType,
                    Description,
                    RequestSchemaName,
                    ResponseSchemaName,
                    RequestSchema,
                    ResponseSchema,
                    RequestTypeName,
                    ResponseTypeName,
                    HasRequest,
                    HasResponse);
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
            public readonly string DiagnosticId;

            /// <summary>
            /// Factory for diagnostic-only instances (e.g. multi-variable declaration error).
            /// </summary>
            public static MemberData ForDiagnostic(Location location, string diagnosticId = "FOXRUN004") =>
                new MemberData("", "", false, "", "", "", "", false, false, "", 0, Location.None, Array.Empty<TopicEntry>(), location, diagnosticId);

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
                : this(ns, cn, partial, mn, memberKind, mt, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, t, diagnosticLocation, string.Empty)
            {
            }

            private MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t, Location diagnosticLocation, string diagnosticId)
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
                DiagnosticId = string.IsNullOrEmpty(diagnosticId) ? "FOXRUN004" : diagnosticId;
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
                    topic.Unless,
                    topic.IsAggregateMember,
                    topic.JsonFieldName,
                    topic.Mode);
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
            public readonly int Mode;
            /// <summary>Change epsilon.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval.</summary>
            public readonly float ForceIntervalSeconds;
            public readonly string When;
            public readonly string Unless;
            public readonly bool IsAggregateMember;
            public readonly string JsonFieldName;

            /// <summary>
            /// Creates a topic entry with the given topic, rate, and schema (backward compat).
            /// </summary>
            public TopicEntry(string topic, float rate, string schema)
                : this(topic, rate, schema, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic entry with publish policy.
            /// </summary>
            public TopicEntry(string topic, float rate, string schema,
                int publishMode, float changeEpsilon, float forceIntervalSeconds, string when = "", string unless = "",
                bool isAggregateMember = false, string jsonFieldName = "", int mode = 0)
            {
                Topic = topic; RateHz = rate; SchemaName = schema;
                PublishMode = publishMode;
                Mode = mode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
                When = when ?? string.Empty;
                Unless = unless ?? string.Empty;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? string.Empty;
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

            public static readonly DiagnosticDescriptor InvalidFoxRunMode = new DiagnosticDescriptor(
                "FOXRUN023", "FoxRun mode out of range",
                "{0}: FoxRun mode must be PublishOnly, SubscribeOnly, or PublishAndSubscribe",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor UnsupportedInboundShape = new DiagnosticDescriptor(
                "FOXRUN024", "Unsupported FoxRun inbound shape",
                "{0}: FoxRun inbound arrays and aggregate members are not supported",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor IgnoredSubscribePolicy = new DiagnosticDescriptor(
                "FOXRUN025", "SubscribeOnly ignores publish policy",
                "{0}: SubscribeOnly ignores publish timing options",
                "FoxRun", DiagnosticSeverity.Warning, true);

            public static readonly DiagnosticDescriptor BidirectionalAuthority = new DiagnosticDescriptor(
                "FOXRUN026", "PublishAndSubscribe authority",
                "{0}: PublishAndSubscribe requires explicit authority ownership",
                "FoxRun", DiagnosticSeverity.Warning, true);

            public static readonly DiagnosticDescriptor InboundNaming = new DiagnosticDescriptor(
                "FOXRUN027", "FoxRun inbound naming",
                "{0}: SubscribeOnly member name should communicate input-port authority",
                "FoxRun", DiagnosticSeverity.Warning, true);

            public static readonly DiagnosticDescriptor InboundTargetNotWritable = new DiagnosticDescriptor(
                "FOXRUN028", "FoxRun inbound target is not writable",
                "FoxRun inbound fields must not be readonly and properties must have a setter",
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

            public static readonly DiagnosticDescriptor UnlessConditionMissing = new DiagnosticDescriptor(
                "FOXRUN029", "FoxRun Unless condition member missing",
                "{0}: FoxRun Unless condition member could not be resolved",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static DiagnosticDescriptor UnknownFoxRunDiagnostic(string id)
            {
                return new DiagnosticDescriptor(
                    "FOXRUN000",
                    "Unmapped FoxRun generator diagnostic",
                    "{0}: internal FoxRun generator diagnostic '" + (id ?? string.Empty) + "' is not mapped to a public descriptor",
                    "FoxRun",
                    DiagnosticSeverity.Error,
                    true);
            }

            public static DiagnosticDescriptor UnknownFoxServiceDiagnostic(string id)
            {
                return new DiagnosticDescriptor(
                    "FOXSERVICE000",
                    "Unmapped FoxService generator diagnostic",
                    "{0}: internal FoxService generator diagnostic '" + (id ?? string.Empty) + "' is not mapped to a public descriptor",
                    "FoxService",
                    DiagnosticSeverity.Error,
                    true);
            }

            public static readonly DiagnosticDescriptor AggregateFieldWithoutMessage = new DiagnosticDescriptor(
                "FOXRUN018", "FoxRunField requires FoxRunMessage",
                "[FoxRunField] member must be declared inside a type annotated with [FoxRunMessage]",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor MixedAggregateTopic = new DiagnosticDescriptor(
                "FOXRUN019", "Mixed aggregate and field-level topic",
                "{0}: topic cannot mix FoxRunMessage aggregate fields with field-level FoxRun members",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor AggregateArrayUnsupported = new DiagnosticDescriptor(
                "FOXRUN020", "Aggregate array fields unsupported",
                "{0}: FoxRun aggregate array fields are not supported yet",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor StaticAggregateMember = new DiagnosticDescriptor(
                "FOXRUN021", "Static aggregate member unsupported",
                "[FoxRunField] cannot be applied to static members",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor DuplicateAggregateJsonName = new DiagnosticDescriptor(
                "FOXRUN022", "Duplicate aggregate JSON field",
                "{0}: aggregate topic has duplicate JSON field names",
                "FoxRun", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor InvalidServiceName = new DiagnosticDescriptor(
                "FOXSERVICE001", "FoxService name must be absolute",
                "FoxService '{0}' must be non-empty and start with '/'",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor InvalidServiceSignature = new DiagnosticDescriptor(
                "FOXSERVICE002", "Unsupported FoxService method signature",
                "{0}: FoxService methods must be non-static, non-generic, synchronous, partial-class instance methods with zero or one by-value parameter",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor UnsupportedServiceRequestType = new DiagnosticDescriptor(
                "FOXSERVICE003", "Unsupported FoxService request type",
                "{0}: FoxService request type is not supported by the declarative RPC generator",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor UnsupportedServiceResponseType = new DiagnosticDescriptor(
                "FOXSERVICE004", "Unsupported FoxService response type",
                "{0}: FoxService response type is not supported by the declarative RPC generator",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor DuplicateServiceName = new DiagnosticDescriptor(
                "FOXSERVICE005", "Duplicate FoxService name",
                "FoxService name '{0}' is declared more than once in the generated service graph",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor MissingExplicitServiceSchemaMetadata = new DiagnosticDescriptor(
                "FOXSERVICE006", "FoxService schema metadata omitted",
                "FoxService '{0}' omits Type, RequestSchemaName, or ResponseSchemaName; generated stable defaults will be used",
                "FoxService", DiagnosticSeverity.Warning, true);

            public static readonly DiagnosticDescriptor ServiceDtoWarning = new DiagnosticDescriptor(
                "FOXSERVICE007", "FoxService DTO member may not serialize",
                "{0}",
                "FoxService", DiagnosticSeverity.Warning, true);

            public static readonly DiagnosticDescriptor RecursiveServiceDto = new DiagnosticDescriptor(
                "FOXSERVICE008", "FoxService DTO graph is recursive",
                "{0}",
                "FoxService", DiagnosticSeverity.Error, true);

            public static readonly DiagnosticDescriptor DeepServiceDto = new DiagnosticDescriptor(
                "FOXSERVICE009", "FoxService DTO graph is too deep",
                "{0}",
                "FoxService", DiagnosticSeverity.Warning, true);

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
                    case "FOXRUN029": return UnlessConditionMissing;
                    case "FOXRUN019": return MixedAggregateTopic;
                    case "FOXRUN020": return AggregateArrayUnsupported;
                    case "FOXRUN022": return DuplicateAggregateJsonName;
                    case "FOXRUN023": return InvalidFoxRunMode;
                    case "FOXRUN024": return UnsupportedInboundShape;
                    case "FOXRUN025": return IgnoredSubscribePolicy;
                    case "FOXRUN026": return BidirectionalAuthority;
                    case "FOXRUN027": return InboundNaming;
                    default:
                        return UnknownFoxRunDiagnostic(id);
                }
            }

            public static DiagnosticDescriptor Member(string id)
            {
                switch (id)
                {
                    case "FOXRUN004": return MultiVariableDeclaration;
                    case "FOXRUN015": return ConditionMissing;
                    case "FOXRUN016": return ConditionNotBool;
                    case "FOXRUN029": return UnlessConditionMissing;
                    case "FOXRUN018": return AggregateFieldWithoutMessage;
                    case "FOXRUN021": return StaticAggregateMember;
                    case "FOXRUN028": return InboundTargetNotWritable;
                    default:
                        return UnknownFoxRunDiagnostic(id);
                }
            }

            public static DiagnosticDescriptor Service(string id)
            {
                switch (id)
                {
                    case "FOXSERVICE001": return InvalidServiceName;
                    case "FOXSERVICE002": return InvalidServiceSignature;
                    case "FOXSERVICE003": return UnsupportedServiceRequestType;
                    case "FOXSERVICE004": return UnsupportedServiceResponseType;
                    case "FOXSERVICE005": return DuplicateServiceName;
                    case "FOXSERVICE006": return MissingExplicitServiceSchemaMetadata;
                    case "FOXSERVICE007": return ServiceDtoWarning;
                    case "FOXSERVICE008": return RecursiveServiceDto;
                    case "FOXSERVICE009": return DeepServiceDto;
                    default:
                        return UnknownFoxServiceDiagnostic(id);
                }
            }
        }
    }
}
