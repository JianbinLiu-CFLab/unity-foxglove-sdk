// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Host-independent DTOs for the FoxRun canonical manifest.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunManifestMember
    {
        public string Namespace { get; }
        public string ClassName { get; }
        public string MemberName { get; }
        public string MemberKind { get; }
        public string TypeName { get; }
        public bool IsValueType { get; }
        public bool IsArray { get; }
        public string ElementTypeName { get; }
        public string Topic { get; }
        public float RateHz { get; }
        public string SchemaName { get; }
        public int PublishMode { get; }
        public float ChangeEpsilon { get; }
        public float ForceIntervalSeconds { get; }

        public FoxRunManifestMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string typeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float rateHz,
            string schemaName,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
            Topic = topic ?? string.Empty;
            RateHz = rateHz;
            SchemaName = schemaName ?? string.Empty;
            PublishMode = publishMode;
            ChangeEpsilon = changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
        }
    }

    public sealed class FoxRunCanonicalManifest
    {
        public int ManifestVersion { get; }
        public string Package { get; }
        public FoxRunManifestGenerator Generator { get; }
        public FoxRunManifestSections Sections { get; }
        public string GlobalManifestHash { get; }

        public FoxRunCanonicalManifest(
            int manifestVersion,
            string packageName,
            FoxRunManifestGenerator generator,
            FoxRunManifestSections sections,
            string globalManifestHash)
        {
            ManifestVersion = manifestVersion;
            Package = packageName ?? string.Empty;
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
            GlobalManifestHash = globalManifestHash ?? string.Empty;
        }
    }

    public sealed class FoxRunManifestGenerator
    {
        public string Name { get; }
        public int MajorVersion { get; }

        public FoxRunManifestGenerator(string name, int majorVersion)
        {
            Name = name ?? string.Empty;
            MajorVersion = majorVersion;
        }
    }

    public sealed class FoxRunManifestSections
    {
        public FoxRunManifestFoxRunSection FoxRun { get; }

        public FoxRunManifestSections(FoxRunManifestFoxRunSection foxRun)
        {
            FoxRun = foxRun ?? throw new ArgumentNullException(nameof(foxRun));
        }
    }

    public sealed class FoxRunManifestFoxRunSection
    {
        public string ManifestHash { get; }
        public IReadOnlyList<FoxRunManifestType> Types { get; }

        public FoxRunManifestFoxRunSection(string manifestHash, IReadOnlyList<FoxRunManifestType> types)
        {
            ManifestHash = manifestHash ?? string.Empty;
            Types = Copy(types);
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new List<T>(values ?? Array.Empty<T>()).AsReadOnly();
        }
    }

    public sealed class FoxRunManifestType
    {
        public string DeclaringType { get; }
        public IReadOnlyList<FoxRunManifestContract> Contracts { get; }

        public FoxRunManifestType(string declaringType, IReadOnlyList<FoxRunManifestContract> contracts)
        {
            DeclaringType = declaringType ?? string.Empty;
            Contracts = new List<FoxRunManifestContract>(contracts ?? Array.Empty<FoxRunManifestContract>()).AsReadOnly();
        }
    }

    public sealed class FoxRunManifestContract
    {
        public string DeclaringType { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string ContractHash { get; }
        public string BindingHash { get; }
        public string PolicyHash { get; }
        public IReadOnlyList<FoxRunManifestField> Fields { get; }
        public FoxRunManifestPolicy Policy { get; }

        public FoxRunManifestContract(
            string declaringType,
            string topic,
            string schemaName,
            string encoding,
            string contractHash,
            string bindingHash,
            string policyHash,
            IReadOnlyList<FoxRunManifestField> fields,
            FoxRunManifestPolicy policy)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            ContractHash = contractHash ?? string.Empty;
            BindingHash = bindingHash ?? string.Empty;
            PolicyHash = policyHash ?? string.Empty;
            Fields = new List<FoxRunManifestField>(fields ?? Array.Empty<FoxRunManifestField>()).AsReadOnly();
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }
    }

    public sealed class FoxRunManifestField
    {
        public string JsonName { get; }
        public string MemberName { get; }
        public string MemberKind { get; }
        public string Type { get; }
        public bool Nullable { get; }
        public bool Array { get; }

        public FoxRunManifestField(
            string jsonName,
            string memberName,
            string memberKind,
            string type,
            bool nullable,
            bool array)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            Type = type ?? string.Empty;
            Nullable = nullable;
            Array = array;
        }
    }

    public sealed class FoxRunManifestPolicy
    {
        public string Mode { get; }
        public float RateHz { get; }
        public float ChangeEpsilon { get; }
        public float ForceIntervalSeconds { get; }

        public FoxRunManifestPolicy(
            string mode,
            float rateHz,
            float changeEpsilon,
            float forceIntervalSeconds)
        {
            Mode = mode ?? string.Empty;
            RateHz = rateHz;
            ChangeEpsilon = changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
        }
    }
}
