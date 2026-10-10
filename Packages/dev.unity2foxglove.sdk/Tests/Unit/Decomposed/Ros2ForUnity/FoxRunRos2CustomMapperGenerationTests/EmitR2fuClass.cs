// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins direct Phase181 DTO-to-custom-ROS2 generated mapper output.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{


public sealed partial class FoxRunRos2CustomMapperGenerationTests
    {
        private static string EmitR2fuClass(
            string ns,
            string className,
            IReadOnlyList<TestR2fuMember> members)
        {
            var inputMembers = members
                .Where(
                    member =>
                        member.Mode == (int)FoxRunFlow.Subscribe
                        || member.Mode
                        == (int)FoxRunFlow.PublishAndSubscribe)
                .Cast<IFoxRunR2fuEmitterMember>()
                .ToList();
            var publishTopics = members
                .Where(
                    member =>
                        member.Mode != (int)FoxRunFlow.Subscribe)
                .Select(member => member.Topic)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var customPublishMembers = members
                .Where(
                    member =>
                        member.Mode != (int)FoxRunFlow.Subscribe
                        && IsSupportedCustom(member))
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .Cast<IFoxRunR2fuEmitterMember>()
                .ToList();
            var mapperMembers = inputMembers
                .Concat(customPublishMembers)
                .Distinct()
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ToList();

            var output = new StringBuilder();
            Ros2InputDispatchEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                Array.Empty<IFoxRunR2fuEmitterMember>(),
                publishTopics);
            Ros2CustomDtoMapperEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                mapperMembers,
                inputMembers,
                publishTopics);
            Ros2CustomPublishEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                customPublishMembers,
                mapperMembers);
            return output.ToString();
        }

        private static bool IsSupportedCustom(
            TestR2fuMember member)
            => member != null
               && member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind
               == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported;

        private sealed class TestR2fuMember :
            IFoxRunR2fuEmitterMember
        {
            internal TestR2fuMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName,
                int policy,
                float tolerance,
                int mode,
                string canonicalType,
                string encoding,
                string source,
                string qosProfile,
                bool generatesWebSocketCodec,
                bool generatesRos2NativeRegistration,
                FoxRunRos2MessageShape ros2MessageShape,
                FoxRunRos2CustomDtoShape ros2CustomDtoShape,
                FoxRunRos2ContractKind ros2ContractKind,
                FoxRunNamedArgumentPresence namedArgumentPresence,
                string qosReliability,
                string qosDurability,
                string qosHistory,
                int qosDepth,
                bool isStream = false)
            {
                MemberName = memberName;
                TypeName = typeName;
                Topic = topic;
                Hz = hz;
                SchemaName = schemaName;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                Source = source;
                QosProfile = qosProfile;
                GeneratesRos2NativeRegistration =
                    generatesRos2NativeRegistration;
                Ros2MessageShape = ros2MessageShape;
                Ros2CustomDtoShape = ros2CustomDtoShape;
                Ros2ContractKind = ros2ContractKind;
                NamedArgumentPresence = namedArgumentPresence;
                QosReliability = qosReliability;
                QosDurability = qosDurability;
                QosHistory = qosHistory;
                QosDepth = qosDepth;
                IsStream = isStream;
            }

            public string MemberName { get; }
            public string TypeName { get; }
            public string Topic { get; }
            public float Hz { get; }
            public bool HasExplicitHz => true;
            public string SchemaName { get; }
            public int Policy { get; }
            public int Mode { get; }
            public string OnlyIf => string.Empty;
            public FoxRunConditionMemberKind ConditionMemberKind =>
                FoxRunConditionMemberKind.None;
            public string Encoding { get; }
            public FoxRunNamedArgumentPresence NamedArgumentPresence
            {
                get;
            }
            public bool IsStream { get; }
            public string Source { get; }
            public string Targets =>
                FoxRunR2fuGenerationConstants.Inherit;
            public string QosProfile { get; }
            public string QosReliability { get; }
            public string QosDurability { get; }
            public string QosHistory { get; }
            public int QosDepth { get; }
            public bool GeneratesRos2NativeRegistration { get; }
            public FoxRunRos2MessageShape Ros2MessageShape { get; }
            public FoxRunRos2CustomDtoShape Ros2CustomDtoShape
            {
                get;
            }
            public FoxRunRos2ContractKind Ros2ContractKind { get; }
        }
    }
}
