// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Provider-neutral FoxRun and FoxService source generation.

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
    [Generator]
    public sealed class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
        private const string AttrFullName =
            "Unity.FoxgloveSDK.Components.FoxRunAttribute";
        private const string MessageAttrFullName =
            "Unity.FoxgloveSDK.Components.FoxRunMessageAttribute";
        private const string FieldAttrFullName =
            "Unity.FoxgloveSDK.Components.FoxRunFieldAttribute";
        private const string ServiceAttrFullName =
            "Unity.FoxgloveSDK.Components.FoxServiceAttribute";

        public void Initialize(
            IncrementalGeneratorInitializationContext context)
        {
            var members = context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => IsFoxRunCandidate(node),
                    static (ctx, token) => ExtractMember(ctx, token))
                .Where(static member => member != null);
#if FOXRUN_PROVIDER_ANALYZER
            FoxRunProviderAnalyzer.Register(context, members);
#else
            context.RegisterSourceOutput(
                members.Collect(),
                static (spc, items) => Generate(spc, items));

            var services = context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => IsServiceCandidate(node),
                    static (ctx, token) =>
                        ExtractServiceMethod(ctx, token))
                .Where(static method => method != null);
            context.RegisterSourceOutput(
                services.Collect(),
                static (spc, items) =>
                    GenerateServices(spc, items));
#endif
        }

        private static bool IsFoxRunCandidate(SyntaxNode node)
        {
            if (node is FieldDeclarationSyntax field
                && field.AttributeLists.Count > 0)
            {
                // Attribute aliases are resolved only by the semantic model;
                // keep every attributed declaration as a candidate and let
                // ExtractMember perform the canonical metadata-name check.
                return true;
            }

            return node is PropertyDeclarationSyntax property
                   && property.AttributeLists.Count > 0;
        }

        private static bool IsServiceCandidate(SyntaxNode node)
            => node is MethodDeclarationSyntax method
               && method.AttributeLists.Count > 0;

        private static MemberData ExtractMember(
            GeneratorSyntaxContext context,
            System.Threading.CancellationToken token)
        {
            ISymbol symbol;
            if (context.Node is FieldDeclarationSyntax field)
            {
                if (field.Declaration.Variables.Count != 1)
                {
                    // Preserve the multi-declarator diagnostic only when a
                    // semantic FoxRun/FoxRunField attribute is actually
                    // present; broad syntax candidates also include ordinary
                    // attributed fields (including aliases).
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var candidate = context.SemanticModel.GetDeclaredSymbol(variable, token);
                        if (candidate?.GetAttributes().Any(attribute =>
                                attribute.AttributeClass?.ToDisplayString() == AttrFullName
                                || attribute.AttributeClass?.ToDisplayString() == FieldAttrFullName) == true)
                        {
                            return MemberData.ForDiagnostic(field.GetLocation());
                        }
                    }

                    return null;
                }
                symbol = context.SemanticModel.GetDeclaredSymbol(
                    field.Declaration.Variables[0],
                    token);
            }
            else if (context.Node is PropertyDeclarationSyntax property)
            {
                symbol = context.SemanticModel.GetDeclaredSymbol(
                    property,
                    token);
            }
            else
            {
                return null;
            }

            if (symbol?.ContainingType == null)
                return null;

            var containingType = symbol.ContainingType;
            var location = symbol.Locations.FirstOrDefault(
                               item => item.IsInSource)
                           ?? Location.None;
            var topics = new List<TopicEntry>();
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString()
                    != AttrFullName)
                {
                    continue;
                }

                topics.Add(ReadTopic(
                    attribute,
                    containingType,
                    aggregate: false,
                    jsonFieldName: string.Empty,
                    protobufFieldNumberOverride: null));
            }

            var fieldAttribute = symbol.GetAttributes()
                .FirstOrDefault(
                    attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == FieldAttrFullName);
            if (fieldAttribute != null)
            {
                if (symbol.IsStatic)
                {
                    return MemberData.ForDiagnostic(
                        location,
                        "FOXRUN021");
                }

                var messageAttribute = containingType.GetAttributes()
                    .FirstOrDefault(
                        attribute =>
                            attribute.AttributeClass
                                ?.ToDisplayString()
                            == MessageAttrFullName);
                if (messageAttribute == null)
                {
                    return MemberData.ForDiagnostic(
                        location,
                        "FOXRUN018");
                }

                var fieldNumber = 0;
                var hasFieldNumber = false;
                foreach (var named in
                         fieldAttribute.NamedArguments)
                {
                    if (named.Key
                        != "ProtobufFieldNumber"
                        || !TryReadIntConstant(
                            named.Value,
                            out fieldNumber))
                    {
                        continue;
                    }

                    hasFieldNumber = true;
                }

                var topic = ReadTopic(
                    messageAttribute,
                    containingType,
                    aggregate: true,
                    jsonFieldName:
                        ReadStringConstructorArgument(
                            fieldAttribute),
                    protobufFieldNumberOverride:
                        hasFieldNumber
                            ? (int?)fieldNumber
                            : null);
                topics.Add(topic);
            }

            if (topics.Count == 0)
                return null;

            var isPartial =
                containingType.DeclaringSyntaxReferences.Any(
                    reference =>
                        reference.GetSyntax(token)
                            is TypeDeclarationSyntax declaration
                        && declaration.Modifiers.Any(
                            SyntaxKind.PartialKeyword));

            string memberKind;
            ITypeSymbol type;
            switch (symbol)
            {
                case IFieldSymbol fieldSymbol:
                    memberKind = "field";
                    type = fieldSymbol.Type;
                    break;
                case IPropertySymbol propertySymbol:
                    memberKind = "property";
                    type = propertySymbol.Type;
                    break;
                default:
                    memberKind = "field";
                    type = null;
                    break;
            }

            var streamDefinition =
                context.SemanticModel.Compilation
                    .GetTypeByMetadataName(
                        "Unity.FoxgloveSDK.Components.FoxRunStream`1");
            var namedType = type as INamedTypeSymbol;
            var isStream = namedType != null
                           && namedType.IsGenericType
                           && streamDefinition != null
                           && SymbolEqualityComparer.Default
                               .Equals(
                                   namedType.OriginalDefinition,
                                   streamDefinition);
            if (isStream)
            {
                const FoxRunNamedArgumentPresence forbidden =
                    FoxRunNamedArgumentPresence
                        .PublishTransportIds
                    | FoxRunNamedArgumentPresence.Policy
                    | FoxRunNamedArgumentPresence.Hz
                    | FoxRunNamedArgumentPresence.Tolerance
                    | FoxRunNamedArgumentPresence.OnlyIf;
                if (!(symbol is IFieldSymbol streamField)
                    || streamField.IsStatic
                    || topics.Count != 1
                    || topics[0].Mode != 2
                    || (topics[0].NamedArgumentPresence
                        & forbidden) != 0)
                {
                    return MemberData.ForDiagnostic(
                        location,
                        "FOXRUN215");
                }

                if (!HasNonNullStreamInitializer(
                        streamField,
                        token))
                {
                    return MemberData.ForDiagnostic(
                        location,
                        "FOXRUN216");
                }

                type = namedType.TypeArguments[0];
            }

            var hasInbound = topics.Any(
                topic => topic.Mode == 2
                         || topic.Mode == 3);
            if (!isStream
                && hasInbound
                && ((symbol is IFieldSymbol inboundField
                     && inboundField.IsReadOnly)
                    || (symbol
                            is IPropertySymbol inboundProperty
                        && inboundProperty.SetMethod == null)))
            {
                return MemberData.ForDiagnostic(
                    location,
                    "FOXRUN203");
            }

            var memberType = type?.ToDisplayString()
                             ?? "object";
            var emissionType =
                FoxRunEmissionTypeNameFormatter
                    .NormalizeCSharpTypeName(memberType);
            var isArray = TryGetArrayElementType(
                type,
                out var elementType);
            FoxRunRoslynTypeShapeBuilder.TryBuild(
                type,
                out var typeShape);

            var ns =
                containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace
                    .IsGlobalNamespace
                    ? containingType.ContainingNamespace
                        .ToDisplayString()
                    : string.Empty;
            var memberOrder = symbol.Locations
                                  .FirstOrDefault(
                                      item =>
                                          item.IsInSource)
                                  ?.SourceSpan.Start
                              ?? 0;
            var declaredNames = containingType.GetMembers()
                .Where(member =>
                    !member.IsImplicitlyDeclared)
                .Select(member => member.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(
                    name => name,
                    StringComparer.Ordinal)
                .ToArray();
            return new MemberData(
                ns,
                containingType.Name,
                isPartial,
                symbol.Name,
                memberKind,
                memberType,
                emissionType,
                type?.IsValueType == true,
                isArray,
                elementType?.ToDisplayString()
                ?? string.Empty,
                memberOrder,
                location,
                topics.ToArray(),
                typeShape,
                declaredNames,
                isStream);
        }

        private static TopicEntry ReadTopic(
            AttributeData attribute,
            INamedTypeSymbol containingType,
            bool aggregate,
            string jsonFieldName,
            int? protobufFieldNumberOverride)
        {
            var topic =
                ReadStringConstructorArgument(attribute);
            var hz = -1f;
            var schemaName = string.Empty;
            var policy = 1;
            var mode = 1;
            var encoding = 0;
            var protobufFieldNumber =
                protobufFieldNumberOverride ?? 0;
            var tolerance = 0f;
            var onlyIf = string.Empty;
            string[] publishTransportIds = null;
            string subscribeTransportId = null;
            var reliability = 0;
            var durability = 0;
            var history = 0;
            var depth = 0;
            var presence =
                FoxRunNamedArgumentPresence.None;

            foreach (var named in
                     attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Hz":
                        presence |=
                            FoxRunNamedArgumentPresence.Hz;
                        TryReadFloatConstant(
                            named.Value,
                            out hz);
                        break;
                    case "Tolerance":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .Tolerance;
                        TryReadFloatConstant(
                            named.Value,
                            out tolerance);
                        break;
                    case "OnlyIf":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .OnlyIf;
                        onlyIf =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                    case "SchemaName":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .SchemaName;
                        schemaName =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                    case "Policy":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .Policy;
                        TryReadIntConstant(
                            named.Value,
                            out policy);
                        break;
                    case "Mode":
                        presence |=
                            FoxRunNamedArgumentPresence.Mode;
                        TryReadIntConstant(
                            named.Value,
                            out mode);
                        break;
                    case "Encoding":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .Encoding;
                        TryReadIntConstant(
                            named.Value,
                            out encoding);
                        break;
                    case "ProtobufFieldNumber":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .ProtobufFieldNumber;
                        TryReadIntConstant(
                            named.Value,
                            out protobufFieldNumber);
                        break;
                    case "PublishTransportIds":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .PublishTransportIds;
                        publishTransportIds =
                            ReadStringArrayConstant(
                                named.Value);
                        break;
                    case "SubscribeTransportId":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .SubscribeTransportId;
                        subscribeTransportId =
                            named.Value.Value as string;
                        break;
                    case "Reliability":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .Reliability;
                        TryReadIntConstant(
                            named.Value,
                            out reliability);
                        break;
                    case "Durability":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .Durability;
                        TryReadIntConstant(
                            named.Value,
                            out durability);
                        break;
                    case "History":
                        presence |=
                            FoxRunNamedArgumentPresence
                                .History;
                        TryReadIntConstant(
                            named.Value,
                            out history);
                        break;
                    case "Depth":
                        presence |=
                            FoxRunNamedArgumentPresence.Depth;
                        TryReadIntConstant(
                            named.Value,
                            out depth);
                        break;
                }
            }

            if (aggregate
                && string.IsNullOrWhiteSpace(schemaName))
            {
                schemaName = DeclaringTypeName(
                    containingType);
            }

            if (protobufFieldNumberOverride.HasValue)
            {
                presence |=
                    FoxRunNamedArgumentPresence
                        .ProtobufFieldNumber;
                protobufFieldNumber =
                    protobufFieldNumberOverride.Value;
            }

            return new TopicEntry(
                topic,
                hz,
                schemaName,
                policy,
                tolerance,
                onlyIf,
                aggregate,
                jsonFieldName,
                mode,
                encoding,
                protobufFieldNumber,
                presence,
                ResolveConditionMemberKind(
                    containingType,
                    onlyIf,
                    presence),
                publishTransportIds,
                subscribeTransportId,
                reliability,
                durability,
                history,
                depth);
        }

        private static bool HasNonNullStreamInitializer(
            IFieldSymbol field,
            System.Threading.CancellationToken token)
        {
            foreach (var reference in
                     field.DeclaringSyntaxReferences)
            {
                if (!(reference.GetSyntax(token)
                      is VariableDeclaratorSyntax variable))
                {
                    continue;
                }

                var value = variable.Initializer?.Value;
                if (value == null
                    || value.IsKind(
                        SyntaxKind.NullLiteralExpression)
                    || value.IsKind(
                        SyntaxKind.DefaultLiteralExpression)
                    || value is DefaultExpressionSyntax)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static FoxRunConditionMemberKind
            ResolveConditionMemberKind(
                INamedTypeSymbol containingType,
                string onlyIf,
                FoxRunNamedArgumentPresence presence)
        {
            if ((presence
                 & FoxRunNamedArgumentPresence.OnlyIf) == 0)
                return FoxRunConditionMemberKind.None;
            if (containingType == null
                || string.IsNullOrWhiteSpace(onlyIf)
                || !IsConditionIdentifier(onlyIf))
                return FoxRunConditionMemberKind.Missing;

            for (var current = containingType;
                 current != null;
                 current = current.BaseType)
            {
                var declared = current.GetMembers(onlyIf);
                if (declared.Length == 0)
                    continue;

                var declaredOnContainingType =
                    SymbolEqualityComparer.Default.Equals(
                        current,
                        containingType);
                var accessible = declared
                    .Where(
                        member => IsConditionMemberAccessible(
                            member,
                            containingType,
                            declaredOnContainingType))
                    .ToArray();
                if (accessible.Length == 0)
                    return FoxRunConditionMemberKind.Missing;

                foreach (var member in accessible)
                {
                    if (member is IFieldSymbol field
                        && IsBoolType(field.Type))
                    {
                        return FoxRunConditionMemberKind
                            .Field;
                    }

                    if (member
                            is IPropertySymbol property
                        && property.GetMethod != null
                        && IsBoolType(property.Type)
                        && property.Parameters.Length == 0)
                    {
                        return FoxRunConditionMemberKind
                            .Property;
                    }

                    if (member is IMethodSymbol method
                        && method.MethodKind
                        == MethodKind.Ordinary
                        && method.Arity == 0
                        && method.Parameters.Length == 0
                        && IsBoolType(method.ReturnType))
                    {
                        return FoxRunConditionMemberKind
                            .Method;
                    }
                }

                return FoxRunConditionMemberKind.Invalid;
            }

            return FoxRunConditionMemberKind.Missing;
        }

        private static bool IsConditionMemberAccessible(
            ISymbol member,
            INamedTypeSymbol generatedType,
            bool declaredOnGeneratedType)
        {
            if (declaredOnGeneratedType)
                return true;

            if (member is IPropertySymbol property)
                return property.GetMethod != null
                       && IsConditionAccessibilityAllowed(
                           property.GetMethod,
                           generatedType);

            return IsConditionAccessibilityAllowed(
                member,
                generatedType);
        }

        private static bool IsConditionAccessibilityAllowed(
            ISymbol member,
            INamedTypeSymbol generatedType)
        {
            var sameAssembly =
                SymbolEqualityComparer.Default.Equals(
                    member?.ContainingAssembly,
                    generatedType?.ContainingAssembly);
            switch (member?.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    return true;
                case Accessibility.Internal:
                case Accessibility.ProtectedAndInternal:
                    return sameAssembly;
                default:
                    return false;
            }
        }

        private static bool IsConditionIdentifier(
            string value)
        {
            if (string.IsNullOrEmpty(value)
                || !SyntaxFacts.IsIdentifierStartCharacter(
                    value[0]))
            {
                return false;
            }

            for (var index = 1;
                 index < value.Length;
                 index++)
            {
                if (!SyntaxFacts
                    .IsIdentifierPartCharacter(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBoolType(ITypeSymbol type)
            => type?.SpecialType
               == SpecialType.System_Boolean;

#if !FOXRUN_PROVIDER_ANALYZER
        private static void Generate(
            SourceProductionContext context,
            ImmutableArray<MemberData> items)
        {
            var members =
                new List<FoxRunRoslynGenerationMember>();
            var locations =
                new Dictionary<string, Location>(
                    StringComparer.Ordinal);
            var firstByClass =
                new Dictionary<
                    (string Ns, string ClassName),
                    MemberData>();

            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (item.DiagnosticLocation != null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.Member(
                                item.DiagnosticId),
                            item.DiagnosticLocation));
                    continue;
                }

                item.AppendRoslynMembers(members);
                locations[
                    MemberLocationKey(
                        item.Ns,
                        item.ClassName,
                        item.MemberName)]
                    = item.MemberLocation;
                var key =
                    (item.Ns, item.ClassName);
                if (!firstByClass.ContainsKey(key))
                    firstByClass.Add(key, item);
            }

            if (members.Count == 0)
                return;

            var model =
                FoxRunRoslynGenerationModelLowerer
                    .Lower(members);
            var invalid =
                new HashSet<string>(
                    StringComparer.Ordinal);
            foreach (var diagnostic in
                     FoxRunGenerationModelValidator
                         .Validate(model))
            {
                var argument =
                    Diags.SharedUsesDetailedMessage(
                        diagnostic.Id)
                        ? diagnostic.Message
                        : diagnostic.Target;
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Diags.Shared(diagnostic.Id),
                        LocationFor(
                            diagnostic,
                            locations),
                        argument));
                if (diagnostic.Severity == "Error")
                {
                    invalid.Add(
                        DiagnosticDeclaringType(
                            diagnostic));
                }
            }

            foreach (var type in model.Types)
            {
                if (!firstByClass.TryGetValue(
                        (type.Namespace,
                            type.ClassName),
                        out var first))
                {
                    continue;
                }

                var names =
                    new HashSet<string>(
                        first.DeclaredMemberNames,
                        StringComparer.Ordinal);
                foreach (var generatedName in
                         FoxgloveSourceEmitter
                             .GeneratedMethodNames(type))
                {
                    if (!names.Contains(
                            generatedName))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.GeneratedMethodConflict,
                            first.MemberLocation,
                            type.DeclaringType,
                            generatedName));
                    invalid.Add(type.DeclaringType);
                }
            }

            var emitted =
                new List<FoxRunGenerationType>();
            foreach (var type in model.Types)
            {
                if (invalid.Contains(
                        type.DeclaringType)
                    || !firstByClass.TryGetValue(
                        (type.Namespace,
                            type.ClassName),
                        out var first))
                {
                    continue;
                }

                if (!first.IsPartial)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.NotPartial,
                            Location.None,
                            first.ClassName));
                    continue;
                }

                emitted.Add(type);
                context.AddSource(
                    FoxgloveSourceEmitter
                        .GeneratedSourceName(
                            type.Namespace,
                            type.ClassName),
                    FoxgloveSourceEmitter
                        .EmitCoreClass(type));
            }

            var descriptor =
                FoxRunGenerationDescriptorJsonWriter
                    .Write(
                        new FoxRunGenerationModel(
                            emitted,
                            model.DescriptorVersion,
                            model.GeneratorVersion));
            context.AddSource(
                "FoxRunGeneratedDescriptorInfo.g.cs",
                FoxRunDescriptorCarrierEmitter
                    .DescriptorCarrierSource(
                        descriptor));
        }
#endif

        private static string DiagnosticDeclaringType(
            FoxRunGenerationDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return string.Empty;
            var target = diagnostic.Target
                         ?? string.Empty;
            var member = diagnostic.MemberName
                         ?? string.Empty;
            if (member.Length == 0)
                return target;
            var suffix = "." + member;
            return target.EndsWith(
                suffix,
                StringComparison.Ordinal)
                ? target.Substring(
                    0,
                    target.Length - suffix.Length)
                : target;
        }

        private static Location LocationFor(
            FoxRunGenerationDiagnostic diagnostic,
            IReadOnlyDictionary<string, Location>
                locations)
        {
            if (diagnostic == null
                || string.IsNullOrEmpty(
                    diagnostic.MemberName))
            {
                return Location.None;
            }

            var declaringType =
                DiagnosticDeclaringType(diagnostic);
            return locations.TryGetValue(
                declaringType
                + "|"
                + diagnostic.MemberName,
                out var location)
                ? location ?? Location.None
                : Location.None;
        }

        private static string MemberLocationKey(
            string ns,
            string className,
            string memberName)
            => (string.IsNullOrEmpty(ns)
                    ? className
                    : ns + "." + className)
               + "|"
               + memberName;

#if !FOXRUN_PROVIDER_ANALYZER
        private static ServiceMethodData
            ExtractServiceMethod(
                GeneratorSyntaxContext context,
                System.Threading.CancellationToken token)
        {
            if (!(context.Node
                  is MethodDeclarationSyntax declaration))
            {
                return null;
            }

            var symbol =
                context.SemanticModel
                    .GetDeclaredSymbol(
                        declaration,
                        token);
            var attribute = symbol?.GetAttributes()
                .FirstOrDefault(
                    candidate =>
                        candidate.AttributeClass
                            ?.ToDisplayString()
                        == ServiceAttrFullName);
            if (symbol?.ContainingType == null
                || attribute == null)
            {
                return null;
            }

            var containingType =
                symbol.ContainingType;
            var location = symbol.Locations
                               .FirstOrDefault(
                                   item =>
                                       item.IsInSource)
                           ?? Location.None;
            var diagnostics =
                new List<ServiceDiagnostic>();
            var isPartial =
                containingType.DeclaringSyntaxReferences.Any(
                    reference =>
                        reference.GetSyntax(token)
                            is TypeDeclarationSyntax type
                        && type.Modifiers.Any(
                            SyntaxKind.PartialKeyword));
            var invalidSignature = !isPartial
                                   || symbol.IsStatic
                                   || symbol.IsGenericMethod
                                   || declaration.Modifiers.Any(
                                       SyntaxKind.AsyncKeyword)
                                   || symbol.Parameters.Length > 1;

            var serviceName =
                attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0]
                              .Value as string
                          ?? string.Empty
                    : string.Empty;
            var serviceType = string.Empty;
            var description = string.Empty;
            var requestSchemaName = string.Empty;
            var responseSchemaName = string.Empty;
            foreach (var named in
                     attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Type":
                        serviceType =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                    case "Description":
                        description =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                    case "RequestSchemaName":
                        requestSchemaName =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                    case "ResponseSchemaName":
                        responseSchemaName =
                            named.Value.Value as string
                            ?? string.Empty;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(serviceName)
                || !serviceName.StartsWith(
                    "/",
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    new ServiceDiagnostic(
                        "FOXSERVICE001",
                        location,
                        serviceName));
            }

            ITypeSymbol requestType = null;
            if (symbol.Parameters.Length == 1)
            {
                var parameter = symbol.Parameters[0];
                if (parameter.RefKind != RefKind.None
                    || parameter.IsParams)
                {
                    invalidSignature = true;
                }

                requestType = parameter.Type;
                diagnostics.AddRange(
                    FoxServiceRoslynDtoValidator
                        .ValidateServiceDtoType(
                            requestType,
                            FoxServiceDtoRules
                                .RequestSide,
                            "Request",
                            serviceName,
                            location));
            }

            var hasResponse = !symbol.ReturnsVoid;
            var responseType = hasResponse
                ? symbol.ReturnType
                : null;
            if (hasResponse)
            {
                diagnostics.AddRange(
                    FoxServiceRoslynDtoValidator
                        .ValidateServiceDtoType(
                            responseType,
                            FoxServiceDtoRules
                                .ResponseSide,
                            "Response",
                            serviceName,
                            location));
            }

            if (invalidSignature)
            {
                diagnostics.Add(
                    new ServiceDiagnostic(
                        "FOXSERVICE002",
                        location,
                        symbol.Name));
            }

            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(
                    requestSchemaName)
                || string.IsNullOrWhiteSpace(
                    responseSchemaName))
            {
                diagnostics.Add(
                    new ServiceDiagnostic(
                        "FOXSERVICE006",
                        location,
                        serviceName));
            }

            if (string.IsNullOrWhiteSpace(serviceType))
            {
                serviceType =
                    containingType.ToDisplayString()
                    + "."
                    + symbol.Name;
            }

            if (string.IsNullOrWhiteSpace(
                    requestSchemaName))
            {
                requestSchemaName =
                    requestType == null
                        ? serviceType + ".Request"
                        : requestType.ToDisplayString();
            }

            if (string.IsNullOrWhiteSpace(
                    responseSchemaName))
            {
                responseSchemaName =
                    responseType == null
                        ? serviceType + ".Response"
                        : responseType.ToDisplayString();
            }

            var skipPreview = diagnostics.Any(
                FoxServiceRoslynSchemaBuilder
                    .IsBlockingSchemaPreviewDiagnostic);
            var requestSchema = skipPreview
                ? FoxServiceRoslynSchemaBuilder
                    .EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(
                    FoxServiceRoslynSchemaBuilder
                        .Build(
                            requestType,
                            FoxServiceDtoRules
                                .RequestSide,
                            0));
            var responseSchema = skipPreview
                ? FoxServiceRoslynSchemaBuilder
                    .EmptyServiceSchemaPreview()
                : FoxServiceSchemaEmitter.Emit(
                    FoxServiceRoslynSchemaBuilder
                        .Build(
                            responseType,
                            FoxServiceDtoRules
                                .ResponseSide,
                            0));
            var ns =
                containingType.ContainingNamespace != null
                && !containingType.ContainingNamespace
                    .IsGlobalNamespace
                    ? containingType.ContainingNamespace
                        .ToDisplayString()
                    : string.Empty;
            return new ServiceMethodData(
                ns,
                containingType.Name,
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

        private static void GenerateServices(
            SourceProductionContext context,
            ImmutableArray<ServiceMethodData> items)
        {
            if (items.IsDefaultOrEmpty)
                return;

            var valid =
                new List<ServiceMethodData>(
                    items.Length);
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var hasError = false;
                foreach (var diagnostic in
                         item.Diagnostics)
                {
                    var descriptor =
                        Diags.Service(
                            diagnostic.Id);
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            descriptor,
                            diagnostic.Location,
                            diagnostic.Target));
                    hasError |=
                        descriptor.DefaultSeverity
                        == DiagnosticSeverity.Error;
                }

                if (!hasError)
                    valid.Add(item);
            }

            var servicesByName = new Dictionary<string, List<ServiceMethodData>>(StringComparer.Ordinal);
            foreach (var item in valid)
            {
                if (!servicesByName.TryGetValue(
                        item.ServiceName,
                        out var list))
                {
                    list = new List<ServiceMethodData>();
                    servicesByName.Add(
                        item.ServiceName,
                        list);
                }

                list.Add(item);
            }

            var duplicates =
                new HashSet<string>(
                    StringComparer.Ordinal);
            foreach (var duplicate in servicesByName)
            {
                if (duplicate.Value.Count <= 1)
                    continue;
                duplicates.Add(duplicate.Key);
                foreach (var item in duplicate.Value)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.DuplicateServiceName,
                            item.Location,
                            item.ServiceName));
                }
            }

            var methodsByType =
                new Dictionary<
                    (string Ns, string ClassName),
                    List<ServiceMethodData>>();
            foreach (var item in valid)
            {
                if (duplicates.Contains(
                        item.ServiceName))
                    continue;

                var key =
                    (item.Ns, item.ClassName);
                if (!methodsByType.TryGetValue(
                        key,
                        out var list))
                {
                    list = new List<ServiceMethodData>();
                    methodsByType.Add(key, list);
                }

                list.Add(item);
            }

            foreach (var group in methodsByType)
            {
                var methods = group.Value
                    .OrderBy(
                        item => item.ServiceName,
                        StringComparer.Ordinal)
                    .Select(
                        item =>
                            item.ToEmitterMethod())
                    .ToList();
                context.AddSource(
                    FoxServiceSourceEmitter
                        .GeneratedSourceName(
                            group.Key.Ns,
                            group.Key.ClassName),
                    FoxServiceSourceEmitter
                        .EmitClass(
                            group.Key.Ns,
                            group.Key.ClassName,
                            methods));
            }
        }
#endif

        private static string
            ReadStringConstructorArgument(
                AttributeData attribute)
        {
            if (attribute == null
                || attribute.ConstructorArguments
                    .Length == 0)
            {
                return string.Empty;
            }

            return attribute.ConstructorArguments[0]
                       .Value as string
                   ?? string.Empty;
        }

        private static string DeclaringTypeName(
            INamedTypeSymbol type)
        {
            if (type == null)
                return string.Empty;
            var names = new Stack<string>();
            for (var current = type;
                 current != null;
                 current = current.ContainingType)
            {
                names.Push(current.Name);
            }

            var prefix =
                type.ContainingNamespace != null
                && !type.ContainingNamespace
                    .IsGlobalNamespace
                    ? type.ContainingNamespace
                          .ToDisplayString()
                      + "."
                    : string.Empty;
            return prefix + string.Join(
                ".",
                names);
        }

        private static bool TryReadFloatConstant(
            TypedConstant constant,
            out float value)
        {
            value = 0f;
            if (constant.Value == null)
                return false;
            try
            {
                value = Convert.ToSingle(
                    constant.Value);
                return true;
            }
            catch (Exception exception)
                when (exception is OverflowException
                      || exception
                          is InvalidCastException
                      || exception
                          is FormatException)
            {
                return false;
            }
        }

        private static bool TryReadIntConstant(
            TypedConstant constant,
            out int value)
        {
            value = 0;
            if (constant.Value == null)
                return false;
            try
            {
                value = Convert.ToInt32(
                    constant.Value);
                return true;
            }
            catch (Exception exception)
                when (exception is OverflowException
                      || exception
                          is InvalidCastException
                      || exception
                          is FormatException)
            {
                return false;
            }
        }

        private static string[] ReadStringArrayConstant(
            TypedConstant constant)
        {
            if (constant.IsNull)
                return null;
            if (constant.Kind
                != TypedConstantKind.Array)
            {
                return Array.Empty<string>();
            }

            var result =
                new string[constant.Values.Length];
            for (var index = 0;
                 index < result.Length;
                 index++)
            {
                result[index] =
                    constant.Values[index].Value
                        as string;
            }

            return result;
        }

        private static string TypeNameForEmission(
            ITypeSymbol type)
            => type == null
                ? string.Empty
                : type.ToDisplayString(
                    SymbolDisplayFormat
                        .FullyQualifiedFormat);

        private static bool TryGetArrayElementType(
            ITypeSymbol type,
            out ITypeSymbol elementType)
        {
            if (type is IArrayTypeSymbol array
                && array.Rank == 1)
            {
                elementType = array.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1)
            {
                var definition =
                    named.ConstructedFrom
                        .ToDisplayString();
                if (definition
                        == "System.Collections.Generic.List<T>"
                    || definition
                        == "System.Collections.Generic.IReadOnlyList<T>"
                    || definition
                        == "System.Collections.Generic.IList<T>")
                {
                    elementType =
                        named.TypeArguments[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }
    }
}
