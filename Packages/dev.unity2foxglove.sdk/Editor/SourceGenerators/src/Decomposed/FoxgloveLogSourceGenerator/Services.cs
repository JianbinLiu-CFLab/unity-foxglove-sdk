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
            if (TryGetUnsupportedHostIdentity(
                    containingType,
                    out var serviceHostReason))
            {
                diagnostics.Add(
                    new ServiceDiagnostic(
                        "FOXSERVICE010",
                        location,
                        serviceHostReason));
            }

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

            var invalidServiceHosts =
                new HashSet<(string Ns, string ClassName)>();
            var serviceHintOwners =
                new Dictionary<string, (string Ns, string ClassName)>(StringComparer.OrdinalIgnoreCase);
            var reportedServiceHosts =
                new HashSet<(string Ns, string ClassName)>();
            foreach (var group in methodsByType)
            {
                var hint = FoxServiceSourceEmitter.GeneratedSourceName(
                    group.Key.Ns,
                    group.Key.ClassName);
                if (!serviceHintOwners.TryGetValue(hint, out var owner))
                {
                    serviceHintOwners.Add(hint, group.Key);
                    continue;
                }

                if (owner == group.Key)
                    continue;
                invalidServiceHosts.Add(owner);
                invalidServiceHosts.Add(group.Key);
                foreach (var host in new[] { owner, group.Key })
                {
                    if (!reportedServiceHosts.Add(host))
                        continue;
                    var first = methodsByType[host][0];
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Diags.UnsupportedServiceHostIdentity,
                            first.Location,
                            "generated service source hint '"
                            + hint
                            + "' collides for declaring hosts '"
                            + (string.IsNullOrEmpty(owner.Ns)
                                ? owner.ClassName
                                : owner.Ns + "." + owner.ClassName)
                            + "' and '"
                            + (string.IsNullOrEmpty(group.Key.Ns)
                                ? group.Key.ClassName
                                : group.Key.Ns + "." + group.Key.ClassName)
                            + "'"));
                }
            }

            foreach (var group in methodsByType)
            {
                if (invalidServiceHosts.Contains(group.Key))
                    continue;
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

    }
}
