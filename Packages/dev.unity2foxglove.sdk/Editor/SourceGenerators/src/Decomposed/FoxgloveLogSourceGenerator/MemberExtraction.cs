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
    public sealed partial class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
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
            var attributes = symbol.GetAttributes().ToArray();
            var hasFoxRunAttribute = attributes.Any(
                attribute =>
                    attribute.AttributeClass?.ToDisplayString()
                    == AttrFullName);
            var fieldAttribute = attributes.FirstOrDefault(
                attribute =>
                    attribute.AttributeClass?.ToDisplayString()
                    == FieldAttrFullName);
            // The syntax candidate intentionally includes arbitrary attributed
            // members so aliases can be resolved semantically. Do not validate a
            // host identity until a canonical FoxRun/FoxRunField attribute exists.
            if (!hasFoxRunAttribute && fieldAttribute == null)
                return null;

            if (TryGetUnsupportedHostIdentity(
                    containingType,
                    out var hostReason))
            {
                return MemberData.ForDiagnostic(
                    location,
                    "FOXRUN623",
                    hostReason);
            }

            var topics = new List<TopicEntry>();
            foreach (var attribute in attributes)
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

    }
}
