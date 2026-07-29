// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Builds and emits explicit directional publish methods (per member and
    /// <c>FoxRun_PublishAll</c>) for publish-capable FoxRun declarations.
    /// </summary>
    internal static class TriggerEmitter
    {
        /// <summary>
        /// Describes a single trigger method: its name and the set of topic
        /// indexes it publishes.
        /// </summary>
        internal sealed class PublishMember
        {
            public readonly string MethodName;
            public readonly List<int> TopicIndexes;

            /// <summary>
            /// Creates a <see cref="PublishMember"/> with the given method name and
            /// topic index list.
            /// </summary>
            public PublishMember(string methodName, List<int> topicIndexes)
            {
                MethodName = methodName;
                TopicIndexes = topicIndexes;
            }
        }

        internal sealed class ApplyMember
        {
            public readonly FoxgloveSourceEmitter.TopicMember Member;
            public readonly string MethodName;

            public ApplyMember(
                FoxgloveSourceEmitter.TopicMember member,
                string methodName)
            {
                Member = member;
                MethodName = methodName;
            }
        }

        /// <summary>
        /// Groups publish-capable members by origin member name and produces a
        /// list of <see cref="PublishMember"/> descriptors with deduplicated
        /// method names.
        /// </summary>
        internal static List<PublishMember> BuildPublishMembers(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> topics)
        {
            var usedNames = new HashSet<string>();
            var result = new List<PublishMember>();

            foreach (var group in members
                         .Where(member => member.Policy == 4)
                         .GroupBy(m => m.MemberName)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var topicIndexes = group
                    .Select(m => IndexOfTopic(topics, m.Topic))
                    .Where(i => i >= 0)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();
                if (topicIndexes.Count == 0)
                    continue;

                var baseName = "FoxRun_Publish_" + IdentifierUtils.SanitizeIdentifier(group.Key.TrimStart('_'));
                var methodName = baseName;
                var suffix = 2;
                while (!usedNames.Add(methodName))
                    methodName = baseName + "_" + suffix++;

                result.Add(new PublishMember(methodName, topicIndexes));
            }

            return result;
        }

        internal static List<ApplyMember> BuildApplyMembers(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> inputMembers)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ApplyMember>();
            if (inputMembers == null)
                return result;

            for (var inputIndex = 0; inputIndex < inputMembers.Count; inputIndex++)
            {
                var member = inputMembers[inputIndex];
                if (member == null || member.Policy != 4)
                    continue;

                var baseName = "FoxRun_Apply_"
                               + IdentifierUtils.SanitizeIdentifier(
                                   (member.MemberName ?? string.Empty).TrimStart('_'));
                var methodName = baseName;
                var suffix = 2;
                while (!usedNames.Add(methodName))
                    methodName = baseName + "_" + suffix++;
                result.Add(new ApplyMember(member, methodName));
            }

            return result;
        }

        internal static IReadOnlyList<string> BuildGeneratedMethodNames(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
        {
            if (members == null)
                return Array.Empty<string>();

            var publishMembers = members
                .Where(member => member != null && member.Mode != 2)
                .ToList();
            var topics = publishMembers
                .Select(member => member.Topic)
                .Where(topic => !string.IsNullOrWhiteSpace(topic))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var inputMembers = members
                .Where(member => member != null && (member.Mode == 2 || member.Mode == 3))
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ToList();
            var publishMethods = BuildPublishMembers(publishMembers, topics);
            var applyMethods = BuildApplyMembers(inputMembers);
            var result = new List<string>(publishMethods.Count + applyMethods.Count + 2);
            result.AddRange(publishMethods.Select(method => method.MethodName));
            if (publishMethods.Count > 0)
                result.Add("FoxRun_PublishAll");
            result.AddRange(applyMethods.Select(method => method.MethodName));
            if (applyMethods.Count > 0)
                result.Add("FoxRun_ApplyAll");
            return result;
        }

        /// <summary>
        /// Emits per-member publish methods and a <c>FoxRun_PublishAll</c> method
        /// that request immediate publication for all publish-capable topics.
        /// </summary>
        internal static void EmitPublishMethods(
            StringBuilder sb,
            IReadOnlyList<PublishMember> publishMembers,
            string pad)
        {
            if (publishMembers.Count == 0)
                return;

            sb.AppendLine();
            foreach (var publish in publishMembers)
            {
                sb.AppendLine($"{pad}    public bool {publish.MethodName}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var published = false;");
                foreach (var topicIndex in publish.TopicIndexes)
                    sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
                sb.AppendLine($"{pad}        return published;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{pad}    public bool FoxRun_PublishAll()");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var published = false;");
            foreach (var topicIndex in publishMembers
                         .SelectMany(member => member.TopicIndexes)
                         .Distinct()
                         .OrderBy(index => index))
                sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
            sb.AppendLine($"{pad}        return published;");
            sb.AppendLine($"{pad}    }}");
        }

        /// <summary>
        /// Emits one direction-specific apply method for every Trigger input
        /// declaration plus a single bulk method. WebSocket staging remains in
        /// the core partial; optional native trigger fields are referenced only
        /// behind the exact symbols that emit their owning partials.
        /// </summary>
        internal static void EmitApplyMethods(
            StringBuilder sb,
            IReadOnlyList<ApplyMember> applyMembers,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> legacyWebSocketInputMembers,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> transactionalInputMembers,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> packagedNativeInputMembers,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> customNativeInputMembers,
            string pad,
            bool emitRos2NativePartial)
        {
            for (var inputIndex = 0; inputIndex < applyMembers.Count; inputIndex++)
            {
                var apply = applyMembers[inputIndex];
                var member = apply.Member;
                var methodName = apply.MethodName;

                var webSocketIndex = IndexOfMember(
                    legacyWebSocketInputMembers,
                    member);
                var hasMessagePackTransaction =
                    MessagePackInputDispatchEmitter.TryGetTransactionIndex(
                        transactionalInputMembers,
                        member,
                        out var messagePackTransactionIndex);
                var hasPackagedNative = emitRos2NativePartial
                                        && IndexOfMember(packagedNativeInputMembers, member) >= 0;
                var hasCustomNative = emitRos2NativePartial
                                      && IndexOfMember(customNativeInputMembers, member) >= 0;

                sb.AppendLine();
                sb.AppendLine($"{pad}    public bool {methodName}()");
                sb.AppendLine($"{pad}    {{");
                if (!string.IsNullOrWhiteSpace(member.OnlyIf))
                {
                    sb.AppendLine(
                        $"{pad}        if (!{ConditionEmitter.ConditionAccess(member.OnlyIf, member.ConditionMemberKind)})");
                    sb.AppendLine($"{pad}        {{");
                    if (webSocketIndex >= 0)
                    {
                        sb.AppendLine($"{pad}            __foxRunInputHasPending_{webSocketIndex} = false;");
                        sb.AppendLine($"{pad}            __foxRunInputHasApplied_{webSocketIndex} = false;");
                    }
                    if (hasMessagePackTransaction)
                    {
                        sb.AppendLine(
                            $"{pad}            global::System.Threading.Interlocked.Exchange(ref __foxRunMessagePackPending_{messagePackTransactionIndex}, null);");
                        sb.AppendLine(
                            $"{pad}            __foxRunMessagePackApplied_{messagePackTransactionIndex} = null;");
                    }
                    sb.AppendLine($"{pad}            return false;");
                    sb.AppendLine($"{pad}        }}");
                }
                sb.AppendLine($"{pad}        var applied = false;");
                if (webSocketIndex >= 0)
                    sb.AppendLine(
                        $"{pad}        applied |= __FoxRunApplyInput_{webSocketIndex}(0d, 0f);");
                if (hasMessagePackTransaction)
                {
                    sb.AppendLine(
                        $"{pad}        applied |= __FoxRunApplyMessagePackTransaction_{messagePackTransactionIndex}(0d, 0f);");
                }

                if (hasPackagedNative || hasCustomNative)
                {
                    var fieldSuffix = TriggerFieldSuffix(member);
                    sb.AppendLine($"{pad}        var nativeTriggerRequested = false;");
                    if (hasPackagedNative)
                    {
                        sb.AppendLine($"{pad}#if UNITY2FOXGLOVE_ROS2_FOR_UNITY");
                        sb.AppendLine(
                            $"{pad}        global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_{fieldSuffix}, 1);");
                        sb.AppendLine($"{pad}        nativeTriggerRequested = true;");
                        sb.AppendLine($"{pad}#endif");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{pad}#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES");
                        sb.AppendLine(
                            $"{pad}        global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_{fieldSuffix}, 1);");
                        sb.AppendLine($"{pad}        nativeTriggerRequested = true;");
                        sb.AppendLine($"{pad}#endif");
                    }
                    sb.AppendLine($"{pad}        applied |= nativeTriggerRequested;");
                }

                sb.AppendLine($"{pad}        return applied;");
                sb.AppendLine($"{pad}    }}");
            }

            if (applyMembers.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    public bool FoxRun_ApplyAll()");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var applied = false;");
            foreach (var apply in applyMembers)
                sb.AppendLine($"{pad}        applied |= {apply.MethodName}();");
            sb.AppendLine($"{pad}        return applied;");
            sb.AppendLine($"{pad}    }}");
        }

        private static int IndexOfTopic(IReadOnlyList<string> topics, string topic)
        {
            for (var i = 0; i < topics.Count; i++)
                if (topics[i] == topic)
                    return i;
            return -1;
        }

        private static int IndexOfMember(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            FoxgloveSourceEmitter.TopicMember member)
        {
            for (var index = 0; index < members.Count; index++)
                if (ReferenceEquals(members[index], member))
                    return index;
            return -1;
        }

        private static string TriggerFieldSuffix(FoxgloveSourceEmitter.TopicMember member)
            => IdentifierUtils.SanitizeIdentifier(
                   (member.MemberName ?? string.Empty).TrimStart('_'))
               + "_"
               + TopicMetadataEmitter.Sha256Hex(
                       (member.MemberName ?? string.Empty)
                       + "|"
                       + (member.Topic ?? string.Empty))
                   .Substring(0, 8);
    }
}
