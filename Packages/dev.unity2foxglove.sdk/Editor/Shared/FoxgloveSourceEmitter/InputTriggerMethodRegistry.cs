// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Allocate collision-free generated inbound trigger method names.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    internal sealed class InputTriggerMethodRegistry
    {
        private readonly Dictionary<FoxgloveSourceEmitter.TopicMember, string> _methodNames =
            new Dictionary<FoxgloveSourceEmitter.TopicMember, string>();
        private readonly HashSet<FoxgloveSourceEmitter.TopicMember> _claimedMembers =
            new HashSet<FoxgloveSourceEmitter.TopicMember>();

        internal InputTriggerMethodRegistry(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            if (members == null)
                return;

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (member == null || member.Policy != 4 || _methodNames.ContainsKey(member))
                    continue;

                var baseName = "FoxRun_Apply_"
                               + IdentifierUtils.SanitizeIdentifier(
                                   (member.MemberName ?? string.Empty).TrimStart('_'));
                var methodName = baseName;
                var suffix = 2;
                while (!usedNames.Add(methodName))
                    methodName = baseName + "_" + suffix++;
                _methodNames.Add(member, methodName);
            }
        }

        internal bool TryClaim(
            FoxgloveSourceEmitter.TopicMember member,
            out string methodName)
        {
            if (member == null
                || !_methodNames.TryGetValue(member, out methodName)
                || !_claimedMembers.Add(member))
            {
                methodName = string.Empty;
                return false;
            }

            return true;
        }
    }
}
