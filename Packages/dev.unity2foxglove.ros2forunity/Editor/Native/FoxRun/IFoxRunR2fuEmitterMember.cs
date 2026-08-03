// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native.Editor/FoxRun
// Purpose: Shared physical/Roslyn input contract for R2FU-only emitters.

namespace Unity.FoxgloveSDK.Editor
{
    internal static class FoxRunR2fuGenerationConstants
    {
        internal const string ProviderId = "unity2foxglove.r2fu";
        internal const string WebSocketProviderId = "foxglove.websocket";
        internal const string Inherit = "inherit";
    }

    internal interface IFoxRunR2fuEmitterMember
    {
        string MemberName { get; }
        string TypeName { get; }
        string Topic { get; }
        float Hz { get; }
        bool HasExplicitHz { get; }
        string SchemaName { get; }
        int Policy { get; }
        int Mode { get; }
        string OnlyIf { get; }
        FoxRunConditionMemberKind ConditionMemberKind { get; }
        string Encoding { get; }
        FoxRunNamedArgumentPresence NamedArgumentPresence { get; }
        bool IsStream { get; }
        string Source { get; }
        string Targets { get; }
        string QosProfile { get; }
        string QosReliability { get; }
        string QosDurability { get; }
        string QosHistory { get; }
        int QosDepth { get; }
        bool GeneratesRos2NativeRegistration { get; }
        FoxRunRos2MessageShape Ros2MessageShape { get; }
        FoxRunRos2CustomDtoShape Ros2CustomDtoShape { get; }
        FoxRunRos2ContractKind Ros2ContractKind { get; }
    }
}
