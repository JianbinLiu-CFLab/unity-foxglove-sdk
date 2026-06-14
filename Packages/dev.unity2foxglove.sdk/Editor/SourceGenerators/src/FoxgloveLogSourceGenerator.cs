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
            if (!isPartial)
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE002", location, className));

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
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE002", location, symbol.Name));

            if (symbol.Parameters.Length > 1)
                diagnostics.Add(new ServiceDiagnostic("FOXSERVICE002", location, symbol.Name));

            ITypeSymbol requestType = null;
            if (symbol.Parameters.Length == 1)
            {
                var parameter = symbol.Parameters[0];
                if (parameter.RefKind != RefKind.None || parameter.IsParams)
                    diagnostics.Add(new ServiceDiagnostic("FOXSERVICE002", location, symbol.Name));
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

            return new ServiceMethodData(
                ns,
                className,
                symbol.Name,
                serviceName,
                serviceType,
                description,
                requestSchemaName,
                responseSchemaName,
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
            foreach (var member in InheritedAndDeclaredMembers(named).OrderBy(MemberOrder))
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
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var hierarchy = new Stack<INamedTypeSymbol>();
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
                hierarchy.Push(current);

            while (hierarchy.Count > 0)
            {
                foreach (var member in hierarchy.Pop().GetMembers())
                {
                    if (member is IFieldSymbol field)
                    {
                        if (seen.Add("F:" + field.Name))
                            yield return field;
                    }
                    else if (member is IPropertySymbol property)
                    {
                        if (seen.Add("P:" + property.Name))
                            yield return property;
                    }
                }
            }
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
            => symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource)?.SourceSpan.Start ?? int.MaxValue;

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

            var duplicateServices = valid
                .GroupBy(item => item.ServiceName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToList();
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var duplicate in duplicateServices)
            {
                duplicateNames.Add(duplicate.Key);
                foreach (var item in duplicate)
                    spc.ReportDiagnostic(Diagnostic.Create(Diags.DuplicateServiceName, item.Location, item.ServiceName));
            }

            foreach (var group in valid
                         .Where(item => !duplicateNames.Contains(item.ServiceName))
                         .GroupBy(item => (item.Ns, item.ClassName)))
            {
                var methods = group
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
                    default:
                        throw new ArgumentOutOfRangeException(nameof(id), id, "Unmapped shared FoxRun diagnostic id.");
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
                        throw new ArgumentOutOfRangeException(nameof(id), id, "Unmapped FoxService diagnostic id.");
                }
            }
        }
    }
}
