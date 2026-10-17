// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits ROS-free XCDR1 writers for Phase181 custom FoxRun DTO envelopes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    /// <remarks>Phase192 source sections: Generation.cs, NamingAndUtilities.cs, Readers.cs, Shapes.cs, ValidationAndProjection.cs, Writers.cs</remarks>
    internal static partial class FoxRunBridgeSourceEmitter
    {
        private const string RosPackageName =
            "unity2foxglove_foxrun_interfaces_v1";
        private const string BridgeProviderId =
            "unity2foxglove.ros2bridge";

#if FOXRUN_BRIDGE_ANALYZER
        internal static string GeneratedSourceName(
            string ns,
            string className)
        {
            var identity = string.IsNullOrEmpty(ns)
                ? className
                : ns + "." + className;
            return IdentifierUtils.SanitizeFileStem(identity)
                   + "_unity2foxglove_ros2bridge_typed_cdr_FoxRun.g.cs";
        }
#endif

        internal static string EmitBridgeContribution(
            FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var topicMap = type.Members
                .Where(member => member.Mode != 2)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var topics = topicMap.Keys
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var subscribeTopicMap = type.Members
                .Where(member => member.Mode != 1)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);
            var subscribeTopics = subscribeTopicMap.Keys
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var hasPublish = topics.Any(topic =>
                    topicMap[topic].Count == 1
                    && IsSupportedPublish(topicMap[topic][0]));
            var subscribeBindings = BuildSubscribeBindings(
                type,
                topics,
                subscribeTopics,
                subscribeTopicMap);
            var hasSubscribe = subscribeBindings.Count != 0;
            if (!hasPublish && !hasSubscribe)
            {
                return string.Empty;
            }

            var body = new StringBuilder();
            var pad = string.IsNullOrEmpty(type.Namespace) ? string.Empty : "    ";

            var escapedNamespace =
                IdentifierUtils.EscapeQualifiedName(type.Namespace);
            if (!string.IsNullOrEmpty(escapedNamespace))
            {
                body.Append("namespace ")
                    .Append(escapedNamespace)
                    .AppendLine();
                body.AppendLine("{");
            }

            body.Append(pad)
                .Append("public partial class ")
                .Append(EscapeIdentifier(type.ClassName))
                .Append(" : ");
            var interfaces = new List<string>();
            if (hasPublish)
            {
                interfaces.Add(
                    "global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedPublishSource");
            }
            if (hasSubscribe)
            {
                interfaces.Add(
                    "global::Unity2Foxglove.Ros2Bridge.IFoxRunBridgeGeneratedSubscribeSource");
            }
            body.AppendLine(string.Join(", ", interfaces));
            body.Append(pad).AppendLine("{");
            if (hasPublish)
            {
                EmitPublishDispatch(
                    body,
                    type,
                    topics,
                    topicMap,
                    pad);
            }
            if (hasSubscribe)
            {
                EmitSubscribeDispatch(
                    body,
                    subscribeBindings,
                    pad);
            }
            if (hasPublish)
                EmitBuilders(body, topics, topicMap, pad);
            if (hasSubscribe)
            {
                EmitReaders(
                    body,
                    subscribeBindings,
                    pad);
            }
            body.Append(pad).AppendLine("}");

            if (!string.IsNullOrEmpty(escapedNamespace))
                body.AppendLine("}");

            return body.ToString();
        }

    }
}
