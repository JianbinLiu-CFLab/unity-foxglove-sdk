// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: R2FU FoxRun source generator
// Purpose: Scan neutral declarations and emit only the R2FU-owned partial.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunProviderAnalyzer
    {
        private const string ProviderId = "unity2foxglove.r2fu";

        internal static void Register(
            IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<MemberData> members)
        {
            var compilationEvidence =
                context.CompilationProvider.Select(
                    static (compilation, _) =>
                        NativeCompilationEvidence.FromCompilation(
                            compilation));
            context.RegisterSourceOutput(
                members.Collect().Combine(compilationEvidence),
                static (productionContext, input) =>
                    Generate(
                        productionContext,
                        input.Left,
                        input.Right));
        }

        private static void Generate(
            SourceProductionContext context,
            ImmutableArray<MemberData> items,
            NativeCompilationEvidence compilationEvidence)
        {
            var neutralMembers =
                new List<FoxRunRoslynGenerationMember>();
            var firstByClass =
                new Dictionary<
                    (string Ns, string ClassName),
                    MemberData>();
            var shapesByMember =
                new Dictionary<string, RoslynShapeData>(
                    StringComparer.Ordinal);
            var unavailableTypes =
                new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (item == null
                    || item.DiagnosticLocation != null)
                {
                    continue;
                }

                item.AppendRoslynMembers(neutralMembers);
                var classKey = (item.Ns, item.ClassName);
                if (!firstByClass.ContainsKey(classKey))
                    firstByClass.Add(classKey, item);
                shapesByMember[MemberKey(
                    item.Ns,
                    item.ClassName,
                    item.MemberName)] =
                    new RoslynShapeData(
                        item.Ros2MessageShape,
                        item.Ros2CustomDtoShape,
                        item.Ros2ContractKind);

                if (!UsesR2fu(item))
                    continue;

                ReportShapeDiagnostics(
                    context,
                    item.MemberLocation,
                    item.Ros2ContractKind
                    == FoxRunRos2ContractKind
                        .PackagedRos2Message
                        ? item.Ros2MessageShape?.Diagnostics
                        : item.Ros2CustomDtoShape?.Diagnostics);

                if (HasUsableShape(item)
                    && item.Topics.Any(topic =>
                        topic.Mode == 2 || topic.Mode == 3)
                    && (!compilationEvidence
                            .HasNativeAssemblyReference
                        || (item.IsStream
                            && !compilationEvidence
                                .HasStreamRegistrarSeam)))
                {
                    unavailableTypes.Add(
                        DeclaringType(
                            item.Ns,
                            item.ClassName));
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            FoxRunR2fuDiagnostics
                                .MissingNativeReference,
                            item.MemberLocation,
                            "R2FU generation requires the optional "
                            + "Unity2Foxglove.Ros2ForUnity.Native "
                            + "assembly."));
                }
            }

            if (neutralMembers.Count == 0)
                return;

            var model =
                FoxRunRoslynGenerationModelLowerer
                    .Lower(neutralMembers);
            var invalidTypes =
                new HashSet<string>(
                    FoxRunGenerationModelValidator
                        .Validate(model)
                        .Where(diagnostic =>
                            string.Equals(
                                diagnostic.Severity,
                                "Error",
                                StringComparison.Ordinal))
                        .Select(DiagnosticDeclaringType),
                    StringComparer.Ordinal);

            foreach (var type in model.Types)
            {
                if (invalidTypes.Contains(type.DeclaringType)
                    || unavailableTypes.Contains(
                        type.DeclaringType)
                    || !firstByClass.TryGetValue(
                        (type.Namespace, type.ClassName),
                        out var first)
                    || !first.IsPartial)
                {
                    continue;
                }

                var source = FoxRunR2fuAnalyzerEmitter.Emit(
                    type,
                    shapesByMember);
                if (string.IsNullOrWhiteSpace(source))
                    continue;
                context.AddSource(
                    FoxRunR2fuAnalyzerEmitter
                        .GeneratedSourceName(
                            type.Namespace,
                            type.ClassName),
                    source);
            }
        }

        private static bool UsesR2fu(MemberData item)
            => item?.Topics != null
               && item.Topics.Any(topic =>
                   ((topic.Mode == 1 || topic.Mode == 3)
                    && topic.PublishTransportIds != null
                    && topic.PublishTransportIds.Contains(
                        ProviderId,
                        StringComparer.Ordinal))
                   || ((topic.Mode == 2 || topic.Mode == 3)
                       && string.Equals(
                           topic.SubscribeTransportId,
                           ProviderId,
                           StringComparison.Ordinal)));

        private static bool HasUsableShape(MemberData item)
            => item.Ros2MessageShape != null
                   && item.Ros2MessageShape
                       .ImplementsRos2Message
                   && item.Ros2MessageShape
                       .HasPublicParameterlessConstructor
                   && item.Ros2MessageShape.Diagnostics.Count == 0
               || item.Ros2CustomDtoShape != null
                   && item.Ros2CustomDtoShape.IsSupported
                   && item.Ros2CustomDtoShape
                       .HasPublicParameterlessConstructor
                   && item.Ros2CustomDtoShape.Diagnostics.Count == 0;

        private static void ReportShapeDiagnostics(
            SourceProductionContext context,
            Location location,
            IReadOnlyList<string> encodedDiagnostics)
        {
            if (encodedDiagnostics == null)
                return;

            foreach (var encoded in encodedDiagnostics)
            {
                if (!FoxRunRos2ShapeDiagnostic.TryDecode(
                        encoded,
                        out var id,
                        out var path,
                        out var message)
                    || !FoxRunR2fuDiagnostics.TryGet(
                        id,
                        out var descriptor))
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor,
                        location,
                        string.IsNullOrEmpty(path)
                            ? message
                            : path + ": " + message));
            }
        }

        private static string DiagnosticDeclaringType(
            FoxRunGenerationDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return string.Empty;
            var target = diagnostic.Target ?? string.Empty;
            var member = diagnostic.MemberName ?? string.Empty;
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

        private static string MemberKey(
            string ns,
            string className,
            string memberName)
            => DeclaringType(ns, className)
               + "\n"
               + (memberName ?? string.Empty);

        private static string DeclaringType(
            string ns,
            string className)
            => string.IsNullOrEmpty(ns)
                ? className ?? string.Empty
                : ns + "." + className;

        private readonly struct NativeCompilationEvidence
        {
            private NativeCompilationEvidence(
                bool hasNativeAssemblyReference,
                bool hasStreamRegistrarSeam)
            {
                HasNativeAssemblyReference =
                    hasNativeAssemblyReference;
                HasStreamRegistrarSeam =
                    hasStreamRegistrarSeam;
            }

            internal bool HasNativeAssemblyReference { get; }
            internal bool HasStreamRegistrarSeam { get; }

            internal static NativeCompilationEvidence
                FromCompilation(Compilation compilation)
            {
                var source = compilation.GetTypeByMetadataName(
                    "Unity2Foxglove.Ros2ForUnity.Native."
                    + "IFoxRunRos2SubscriptionSource");
                var registrar =
                    compilation.GetTypeByMetadataName(
                        "Unity2Foxglove.Ros2ForUnity.Native."
                        + "IFoxRunRos2SubscriptionRegistrar");
                var contract =
                    compilation.GetTypeByMetadataName(
                        "Unity2Foxglove.Ros2ForUnity.Native."
                        + "FoxRunRos2GeneratedContract");
                var copyContext =
                    compilation.GetTypeByMetadataName(
                        "Unity2Foxglove.Ros2ForUnity.Native."
                        + "FoxRunRos2CopyContext");
                var rosMessage =
                    compilation.GetTypeByMetadataName(
                        "ROS2.Message");
                var hasNative =
                    IsPublicNativeType(source)
                    && IsPublicNativeType(registrar)
                    && IsPublicNativeType(contract)
                    && IsPublicNativeType(copyContext)
                    && rosMessage != null;
                var hasStream =
                    hasNative
                    && registrar.GetMembers("RegisterStream")
                        .OfType<IMethodSymbol>()
                        .Any(method =>
                            !method.IsStatic
                            && method.DeclaredAccessibility
                            == Accessibility.Public);
                return new NativeCompilationEvidence(
                    hasNative,
                    hasStream);
            }

            private static bool IsPublicNativeType(
                INamedTypeSymbol symbol)
                => symbol != null
                   && symbol.DeclaredAccessibility
                   == Accessibility.Public
                   && string.Equals(
                       symbol.ContainingAssembly?.Identity.Name,
                       "Unity2Foxglove.Ros2ForUnity.Native",
                       StringComparison.Ordinal);
        }

        internal readonly struct RoslynShapeData
        {
            internal RoslynShapeData(
                FoxRunRos2MessageShape messageShape,
                FoxRunRos2CustomDtoShape customDtoShape,
                FoxRunRos2ContractKind contractKind)
            {
                MessageShape = messageShape;
                CustomDtoShape = customDtoShape;
                ContractKind = contractKind;
            }

            internal FoxRunRos2MessageShape MessageShape { get; }
            internal FoxRunRos2CustomDtoShape CustomDtoShape { get; }
            internal FoxRunRos2ContractKind ContractKind { get; }
        }
    }

    internal static class FoxRunR2fuAnalyzerEmitter
    {
        private const string ProviderId = "unity2foxglove.r2fu";
        private const string Inherit = "inherit";

        internal static string Emit(
            FoxRunGenerationType type,
            IReadOnlyDictionary<
                string,
                FoxRunProviderAnalyzer.RoslynShapeData>
                shapesByMember)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var members = type.Members
                .Select(member =>
                    FoxRunR2fuRoslynTopicMember.Create(
                        member,
                        ShapeFor(member, shapesByMember)))
                .ToList();
            var inputMembers = members
                .Where(member =>
                    member.SelectedForSubscribe
                    && (member.Mode == 2
                        || member.Mode == 3))
                .OrderBy(
                    member => member.Topic,
                    StringComparer.Ordinal)
                .ThenBy(
                    member => member.MemberName,
                    StringComparer.Ordinal)
                .ToList();
            var packagedInputMembers = inputMembers
                .Where(member =>
                    member.GeneratesRos2NativeRegistration
                    && member.Ros2ContractKind
                    == FoxRunRos2ContractKind
                        .PackagedRos2Message)
                .Cast<IFoxRunR2fuEmitterMember>()
                .ToList();
            var customInputMembers = inputMembers
                .Where(IsCustomMember)
                .ToList();
            var publishing = members
                .Where(member =>
                    member.SelectedForPublish
                    && member.Mode != 2)
                .GroupBy(
                    member => member.Topic,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var publishTopics = type.Members
                .Where(member => member.Mode != 2)
                .Select(member => member.Topic)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(
                    topic => topic,
                    StringComparer.Ordinal)
                .ToList();
            var customPublishMembers = publishing
                .Where(pair =>
                    pair.Value.Count == 1
                    && IsCustomMember(pair.Value[0]))
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(pair => pair.Value[0])
                .ToList();
            var mapperMembers = customInputMembers
                .Concat(customPublishMembers)
                .Distinct()
                .OrderBy(
                    member => member.Topic,
                    StringComparer.Ordinal)
                .ThenBy(
                    member => member.MemberName,
                    StringComparer.Ordinal)
                .ToList();

            var output = new StringBuilder();
            Ros2InputDispatchEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                packagedInputMembers,
                publishTopics);
            Ros2CustomDtoMapperEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                mapperMembers,
                customInputMembers,
                publishTopics);
            Ros2CustomPublishEmitter.EmitConditionalPartial(
                output,
                type.Namespace,
                type.ClassName,
                customPublishMembers,
                mapperMembers);
            return output.ToString();
        }

        internal static string GeneratedSourceName(
            string ns,
            string className)
        {
            var identity = string.IsNullOrEmpty(ns)
                ? className
                : ns + "." + className;
            return IdentifierUtils.SanitizeFileStem(identity)
                   + "_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs";
        }

        private static FoxRunProviderAnalyzer.RoslynShapeData
            ShapeFor(
                FoxRunGenerationMember member,
                IReadOnlyDictionary<
                    string,
                    FoxRunProviderAnalyzer.RoslynShapeData>
                    shapesByMember)
        {
            var key = member.DeclaringType
                      + "\n"
                      + member.MemberName;
            return shapesByMember != null
                   && shapesByMember.TryGetValue(
                       key,
                       out var shape)
                ? shape
                : default;
        }

        private static bool IsCustomMember(
            FoxRunR2fuRoslynTopicMember member)
            => member != null
               && member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind
               == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported
               && member.Ros2CustomDtoShape
                   .HasPublicParameterlessConstructor
               && member.Ros2CustomDtoShape.Diagnostics.Count == 0
               && !string.IsNullOrWhiteSpace(
                   member.Ros2CustomDtoShape.PayloadIdentity);

        private sealed class FoxRunR2fuRoslynTopicMember :
            IFoxRunR2fuEmitterMember
        {
            private readonly FoxRunGenerationMember _member;

            private FoxRunR2fuRoslynTopicMember(
                FoxRunGenerationMember member,
                FoxRunProviderAnalyzer.RoslynShapeData shape)
            {
                _member = member;
                Ros2MessageShape = shape.MessageShape;
                Ros2CustomDtoShape = shape.CustomDtoShape;
                Ros2ContractKind = shape.ContractKind;
                SelectedForPublish =
                    (member.Mode == 1 || member.Mode == 3)
                    && (member.PublishTransportIds == null
                        || member.PublishTransportIds.Contains(
                            ProviderId,
                            StringComparer.Ordinal));
                SelectedForSubscribe =
                    (member.Mode == 2 || member.Mode == 3)
                    && (string.IsNullOrWhiteSpace(
                            member.SubscribeTransportId)
                        || string.Equals(
                            member.SubscribeTransportId,
                            ProviderId,
                            StringComparison.Ordinal));
                GeneratesRos2NativeRegistration =
                    (SelectedForPublish
                     || SelectedForSubscribe)
                    && IsUsableShape(
                        Ros2ContractKind,
                        Ros2MessageShape,
                        Ros2CustomDtoShape);
            }

            internal static FoxRunR2fuRoslynTopicMember Create(
                FoxRunGenerationMember member,
                FoxRunProviderAnalyzer.RoslynShapeData shape)
                => new FoxRunR2fuRoslynTopicMember(
                    member
                    ?? throw new ArgumentNullException(
                        nameof(member)),
                    shape);

            internal bool SelectedForPublish { get; }
            internal bool SelectedForSubscribe { get; }
            public string MemberName => _member.MemberName;
            public string TypeName => _member.EmissionTypeName;
            public string Topic => _member.Topic;
            public float Hz => _member.Hz;
            public bool HasExplicitHz => _member.HasExplicitHz;
            public string SchemaName => _member.SchemaName;
            public int Policy => _member.Policy;
            public int Mode => _member.Mode;
            public string OnlyIf => _member.OnlyIf;
            public FoxRunConditionMemberKind ConditionMemberKind =>
                _member.ConditionMemberKind;
            public string Encoding => _member.Encoding;
            public FoxRunNamedArgumentPresence NamedArgumentPresence =>
                _member.NamedArgumentPresence;
            public bool IsStream => _member.IsStream;
            public string Source =>
                string.IsNullOrWhiteSpace(
                    _member.SubscribeTransportId)
                    ? Inherit
                    : _member.SubscribeTransportId;
            public string Targets =>
                _member.PublishTransportIds == null
                    ? Inherit
                    : string.Join(
                        ",",
                        _member.PublishTransportIds);
            public string QosProfile => Inherit;
            public string QosReliability => _member.Reliability;
            public string QosDurability => _member.Durability;
            public string QosHistory => _member.History;
            public int QosDepth => _member.Depth;
            public bool GeneratesRos2NativeRegistration { get; }
            public FoxRunRos2MessageShape Ros2MessageShape { get; }
            public FoxRunRos2CustomDtoShape Ros2CustomDtoShape { get; }
            public FoxRunRos2ContractKind Ros2ContractKind { get; }

            private static bool IsUsableShape(
                FoxRunRos2ContractKind kind,
                FoxRunRos2MessageShape messageShape,
                FoxRunRos2CustomDtoShape customDtoShape)
            {
                switch (kind)
                {
                    case FoxRunRos2ContractKind
                        .PackagedRos2Message:
                        return messageShape != null
                               && messageShape
                                   .ImplementsRos2Message
                               && messageShape
                                   .HasPublicParameterlessConstructor
                               && messageShape.Diagnostics.Count == 0;
                    case FoxRunRos2ContractKind.CustomDto:
                        return customDtoShape != null
                               && customDtoShape.IsSupported
                               && customDtoShape
                                   .HasPublicParameterlessConstructor
                               && customDtoShape.Diagnostics.Count == 0
                               && !string.IsNullOrWhiteSpace(
                                   customDtoShape
                                       .PayloadIdentity);
                    default:
                        return false;
                }
            }
        }
    }
}
