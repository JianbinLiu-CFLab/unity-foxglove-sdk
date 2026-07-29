// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Scans loaded assemblies for FoxRun members and FoxService methods.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxrunCodeGenerator
    {
        private static FoxRunAndServiceScanResult ScanFoxRunMembersAndServices(bool ignoreReflectionTypeLoadExceptions)
        {
            var byClass = new Dictionary<(string Ns, string ClassName), List<MemberData>>();
            var manifestMembers = new List<FoxRunManifestMember>();
            var reflectionMembers = new List<FoxRunReflectionGenerationMember>();
            var serviceEntries = new List<FoxServiceScanEntry>();
            var foxRunTypes = new List<(string AsmName, string Ns, string ClassName)>();

            VisitLoadedFoxRunComponentTypes(ignoreReflectionTypeLoadExceptions, (asm, type) =>
            {
                var ns = type.Namespace ?? "";
                var key = (ns, type.Name);

                var members = ScanType(type);
                if (members.Count > 0)
                {
                    foxRunTypes.Add((asm.GetName().Name, ns, type.Name));
                    AddFoxRunMembers(key, members, byClass, manifestMembers, reflectionMembers);
                }

                var methods = ScanServiceType(type);
                if (methods.Count > 0)
                {
                    var owner = string.IsNullOrEmpty(ns) ? type.Name : ns + "." + type.Name;
                    foreach (var method in methods)
                        serviceEntries.Add(new FoxServiceScanEntry(key, owner, method));
                }
            });

            return new FoxRunAndServiceScanResult(
                new FoxRunScanResult(byClass, manifestMembers, reflectionMembers),
                BuildFoxServiceScanResult(serviceEntries),
                foxRunTypes);
        }

        private static FoxRunScanResult ScanFoxRunMembers(bool ignoreReflectionTypeLoadExceptions)
        {
            var byClass = new Dictionary<(string Ns, string ClassName), List<MemberData>>();
            var manifestMembers = new List<FoxRunManifestMember>();
            var reflectionMembers = new List<FoxRunReflectionGenerationMember>();

            VisitLoadedFoxRunComponentTypes(ignoreReflectionTypeLoadExceptions, (asm, type) =>
            {
                var ns = type.Namespace ?? "";
                AddFoxRunMembers(
                    (ns, type.Name),
                    ScanType(type),
                    byClass,
                    manifestMembers,
                    reflectionMembers);
            });

            return new FoxRunScanResult(byClass, manifestMembers, reflectionMembers);
        }

        private static void AddFoxRunMembers(
            (string Ns, string ClassName) key,
            List<MemberData> members,
            Dictionary<(string Ns, string ClassName), List<MemberData>> byClass,
            List<FoxRunManifestMember> manifestMembers,
            List<FoxRunReflectionGenerationMember> reflectionMembers)
        {
            if (members == null || members.Count == 0)
                return;

            if (!byClass.TryGetValue(key, out var list))
                byClass[key] = list = new List<MemberData>();

            foreach (var member in members)
            {
                list.Add(member);
                manifestMembers.Add(member.ToManifestMember());
                reflectionMembers.Add(member.ToReflectionMember());
            }
        }

        private static void VisitLoadedFoxRunComponentTypes(
            bool ignoreReflectionTypeLoadExceptions,
            Action<Assembly, Type> visitor)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!type.IsClass || type.IsAbstract) continue;
                        if (!AssumePartialWasEnforcedBySourceGenerator(type)) continue;
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                        visitor(asm, type);
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (!ignoreReflectionTypeLoadExceptions)
                        throw;
                    WarnSkippedAssembly(asm, ex);
                    // Source fallback generation is best-effort because the Roslyn
                    // path already reports authoring errors in the Editor. The
                    // link.xml scan is fail-fast and catches preservation risk.
                }
            }
        }

        /// <summary>
        /// Documents the build-time partial-class enforcement boundary.
        /// <para>Runtime detection is not possible because the CLR erases partial
        /// metadata. The build pipeline assumes every <c>MonoBehaviour</c> with
        /// <c>[FoxRun]</c> members was declared partial; the Roslyn ISG enforces this
        /// at Editor compile time via FOXRUN001.</para>
        /// </summary>
        static bool AssumePartialWasEnforcedBySourceGenerator(Type type)
        {
            // Partial classes have no CLR metadata. This is intentionally a named
            // no-op so callers do not mistake it for a runtime filter: FOXRUN001
            // is enforced by the Roslyn generator during Editor compilation.
            return true;
        }

        /// <summary>
        /// Reflects over all instance fields and properties (public and non-public,
        /// declared only) on the given type and collects <c>[FoxRun]</c> attributed
        /// members as <c>MemberData</c> entries.
        /// </summary>
        static List<MemberData> ScanType(Type type)
        {
            var result = new List<MemberData>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            var ns = type.Namespace ?? "";
            var cn = type.Name;
            var aggregateMessage = ReadFoxRunMessageAttributeSnapshot(type);
            var aggregateSchema = aggregateMessage == null
                ? string.Empty
                : string.IsNullOrWhiteSpace(aggregateMessage.SchemaName)
                    ? (string.IsNullOrEmpty(ns) ? cn : ns + "." + cn)
                    : aggregateMessage.SchemaName;

            foreach (var fi in type.GetFields(flags))
            {
                var attrs = ReadFoxRunAttributeSnapshots(fi);
                foreach (var a in attrs)
                {
                    if (a.Mode != (int)FoxRunFlow.Publish
                        && fi.IsInitOnly
                        && !(fi.FieldType.IsGenericType
                             && fi.FieldType.GetGenericTypeDefinition() == typeof(FoxRunStream<>)))
                        throw CreateInboundTargetNotWritableException(type, fi.Name, "field", "readonly fields");
                    result.Add(new MemberData(
                        fi.Name, fi.FieldType, "field", ns, cn, a.Topic, a.Hz, a.SchemaName,
                        a.Policy, a.Tolerance, fi.MetadataToken, "",
                        a.OnlyIf, mode: a.Mode, encoding: a.Encoding, protobufFieldNumber: a.ProtobufFieldNumber,
                        source: a.Source, qosProfile: a.QosProfile, targets: a.Targets,
                        qosReliability: a.QosReliability, qosDurability: a.QosDurability,
                        qosHistory: a.QosHistory, qosDepth: a.QosDepth,
                        publishTransportIds: a.PublishTransportIds,
                        subscribeTransportId: a.SubscribeTransportId,
                        namedArgumentPresence: a.NamedArgumentPresence,
                        conditionMemberKind: ResolveConditionMemberKind(
                            type,
                            a.OnlyIf,
                            a.NamedArgumentPresence)));
                }

                var aggregateField = fi.GetCustomAttribute<FoxRunFieldAttribute>();
                if (aggregateMessage != null && aggregateField != null)
                {
                    var aggregatePresence = aggregateMessage.NamedArgumentPresence
                        | ReadFoxRunFieldPresence(fi);
                    result.Add(new MemberData(
                        fi.Name, fi.FieldType, "field", ns, cn, aggregateMessage.Topic, aggregateMessage.Hz, aggregateSchema,
                        aggregateMessage.Policy, aggregateMessage.Tolerance, fi.MetadataToken, "",
                        aggregateMessage.OnlyIf, isAggregateMember: true, jsonFieldName: aggregateField.JsonName,
                        encoding: aggregateMessage.Encoding, protobufFieldNumber: aggregateField.ProtobufFieldNumber,
                        targets: aggregateMessage.Targets,
                        publishTransportIds: aggregateMessage.PublishTransportIds,
                        qosProfile: aggregateMessage.QosProfile,
                        qosReliability: aggregateMessage.QosReliability,
                        qosDurability: aggregateMessage.QosDurability,
                        qosHistory: aggregateMessage.QosHistory,
                        qosDepth: aggregateMessage.QosDepth,
                        namedArgumentPresence: aggregatePresence,
                        conditionMemberKind: ResolveConditionMemberKind(
                            type,
                            aggregateMessage.OnlyIf,
                            aggregateMessage.NamedArgumentPresence)));
                }
            }
            foreach (var pi in type.GetProperties(flags))
            {
                var attrs = ReadFoxRunAttributeSnapshots(pi);
                foreach (var a in attrs)
                {
                    if (a.Mode != (int)FoxRunFlow.Publish && !pi.CanWrite)
                        throw CreateInboundTargetNotWritableException(type, pi.Name, "property", "properties without setters");
                    result.Add(new MemberData(
                        pi.Name, pi.PropertyType, "property", ns, cn, a.Topic, a.Hz, a.SchemaName,
                        a.Policy, a.Tolerance, pi.MetadataToken, "",
                        a.OnlyIf, mode: a.Mode, encoding: a.Encoding, protobufFieldNumber: a.ProtobufFieldNumber,
                        source: a.Source, qosProfile: a.QosProfile, targets: a.Targets,
                        qosReliability: a.QosReliability, qosDurability: a.QosDurability,
                        qosHistory: a.QosHistory, qosDepth: a.QosDepth,
                        publishTransportIds: a.PublishTransportIds,
                        subscribeTransportId: a.SubscribeTransportId,
                        namedArgumentPresence: a.NamedArgumentPresence,
                        conditionMemberKind: ResolveConditionMemberKind(
                            type,
                            a.OnlyIf,
                            a.NamedArgumentPresence)));
                }

                var aggregateField = pi.GetCustomAttribute<FoxRunFieldAttribute>();
                if (aggregateMessage != null && aggregateField != null)
                {
                    var aggregatePresence = aggregateMessage.NamedArgumentPresence
                        | ReadFoxRunFieldPresence(pi);
                    result.Add(new MemberData(
                        pi.Name, pi.PropertyType, "property", ns, cn, aggregateMessage.Topic, aggregateMessage.Hz, aggregateSchema,
                        aggregateMessage.Policy, aggregateMessage.Tolerance, pi.MetadataToken, "",
                        aggregateMessage.OnlyIf, isAggregateMember: true, jsonFieldName: aggregateField.JsonName,
                        encoding: aggregateMessage.Encoding, protobufFieldNumber: aggregateField.ProtobufFieldNumber,
                        targets: aggregateMessage.Targets,
                        publishTransportIds: aggregateMessage.PublishTransportIds,
                        qosProfile: aggregateMessage.QosProfile,
                        qosReliability: aggregateMessage.QosReliability,
                        qosDurability: aggregateMessage.QosDurability,
                        qosHistory: aggregateMessage.QosHistory,
                        qosDepth: aggregateMessage.QosDepth,
                        namedArgumentPresence: aggregatePresence,
                        conditionMemberKind: ResolveConditionMemberKind(
                            type,
                            aggregateMessage.OnlyIf,
                            aggregateMessage.NamedArgumentPresence)));
                }
            }
            return result;
        }

        private static FoxRunConditionMemberKind ResolveConditionMemberKind(
            Type declaringType,
            string conditionName,
            FoxRunNamedArgumentPresence presence)
            => FoxRunReflectionConditionMemberResolver.Resolve(
                declaringType,
                conditionName,
                presence);

        private static InvalidOperationException CreateInboundTargetNotWritableException(
            Type type,
            string memberName,
            string memberKind,
            string unsupportedShape)
        {
            var target = (type == null ? "<unknown>" : type.FullName) + "." + (memberName ?? "<unknown>");
            return new InvalidOperationException(
                "FOXRUN203 Error: " + target
                + ": FoxRun inbound " + memberKind
                + " target must be writable; " + unsupportedShape
                + " cannot receive Subscribe or PublishAndSubscribe messages.");
        }

        private sealed class FoxRunScanResult
        {
            public readonly Dictionary<(string Ns, string ClassName), List<MemberData>> ByClass;
            public readonly List<FoxRunManifestMember> ManifestMembers;
            public readonly List<FoxRunReflectionGenerationMember> ReflectionMembers;

            public FoxRunScanResult(
                Dictionary<(string Ns, string ClassName), List<MemberData>> byClass,
                List<FoxRunManifestMember> manifestMembers,
                List<FoxRunReflectionGenerationMember> reflectionMembers)
            {
                ByClass = byClass;
                ManifestMembers = manifestMembers;
                ReflectionMembers = reflectionMembers;
            }
        }

        private sealed class FoxRunAndServiceScanResult
        {
            public readonly FoxRunScanResult FoxRun;
            public readonly FoxServiceScanResult Services;
            public readonly List<(string AsmName, string Ns, string ClassName)> FoxRunTypes;

            public FoxRunAndServiceScanResult(
                FoxRunScanResult foxRun,
                FoxServiceScanResult services,
                List<(string AsmName, string Ns, string ClassName)> foxRunTypes)
            {
                FoxRun = foxRun;
                Services = services;
                FoxRunTypes = foxRunTypes;
            }
        }

        private static void WarnSkippedAssembly(Assembly asm, ReflectionTypeLoadException ex)
        {
            var assemblyName = asm == null ? "<unknown>" : asm.GetName().Name;
            Debug.LogWarning(
                "[FoxrunCodeGenerator] Skipped assembly '" + assemblyName + "' while scanning [FoxRun] members because type loading failed. " +
                LoaderExceptionSummary(ex));
        }

        private static string LoaderExceptionSummary(ReflectionTypeLoadException ex)
        {
            if (ex == null || ex.LoaderExceptions == null || ex.LoaderExceptions.Length == 0)
                return "No LoaderExceptions were provided.";

            var messages = ex.LoaderExceptions
                .Where(loader => loader != null)
                .Select(loader => loader.GetType().Name + ": " + loader.Message)
                .Take(3)
                .ToList();
            if (messages.Count == 0)
                return "LoaderExceptions contained no exception details.";

            if (ex.LoaderExceptions.Length > messages.Count)
                messages.Add("... " + (ex.LoaderExceptions.Length - messages.Count).ToString() + " more");
            return "LoaderExceptions: " + string.Join(" | ", messages);
        }
    }
}
