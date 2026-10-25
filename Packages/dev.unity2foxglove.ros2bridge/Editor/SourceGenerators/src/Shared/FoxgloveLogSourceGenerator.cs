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
    public sealed partial class FoxgloveLogSourceGenerator : IIncrementalGenerator
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

    }
}
