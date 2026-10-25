// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Provider-neutral semantic model consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunGenerationType
    {
        public FoxRunGenerationType(
            string ns,
            string className,
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace)
                ? ClassName
                : Namespace + "." + ClassName;
            Members = (members ?? Array.Empty<FoxRunGenerationMember>())
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ThenBy(member => member.SchemaName, StringComparer.Ordinal)
                .ThenBy(member => member.CanonicalType, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string DeclaringType;
        public readonly IReadOnlyList<FoxRunGenerationMember> Members;
    }

}
