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
        private static TestR2fuMember CreateCustomMember(
            string memberName,
            string typeName,
            string topic,
            int mode,
            string source,
            FoxRunRos2CustomDtoShape shape,
            bool isStream = false)
            => new TestR2fuMember(
                memberName,
                typeName,
                topic,
                10f,
                typeName,
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                mode: mode,
                canonicalType: typeName,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: source,
                qosProfile: FoxRunR2fuGenerationConstants.Inherit,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Reliability
                    | FoxRunNamedArgumentPresence.Durability
                    | FoxRunNamedArgumentPresence.History
                    | FoxRunNamedArgumentPresence.Depth,
                qosReliability: "reliable",
                qosDurability: "volatile",
                qosHistory: "keep-last",
                qosDepth: 10,
                isStream: isStream);

        private const string CustomMapperDynamicSupport = @"
namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : global::System.Attribute { }
}
namespace Phase181
{
    public enum StateKind : ushort { Zero = 0, One = 1, Two = 2 }
    public sealed class NestedState
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
    }
    public sealed class State
    {
        public byte[] Bytes { get; set; }
        public int Count { get; set; }
        public global::System.Collections.Generic.List<NestedState> Children { get; set; }
        public StateKind Kind { get; set; }
        public string[] Labels { get; set; }
        public string Message { get; set; }
        public NestedState Nested { get; set; }
        public int? OptionalCount { get; set; }
        public string OptionalText { get; set; }
        public global::System.Collections.Generic.List<long> Values { get; set; }
    }
}
namespace Phase184
{
    public enum OptionalKind : ushort { Zero = 0, One = 1, Two = 2 }
    public sealed class OtherState
    {
        public global::Phase181.NestedState[] Children { get; set; }
        public int? OptionalCount { get; set; }
        public OptionalKind? OptionalKind { get; set; }
    }
    public partial class GeneratedMatrix
    {
        private readonly global::Phase181.State PublishReadonly = new global::Phase181.State();
        public global::Phase181.State PublishGetter { get; } = new global::Phase181.State();
        public global::Phase184.OtherState SubscribeOther { get; set; } = new global::Phase184.OtherState();
        public global::Phase181.State DuplexState { get; set; } = new global::Phase181.State();
        public global::Phase184.OtherState PublishOther { get; } = new global::Phase184.OtherState();
        private void __FoxRunMarkRemoteApplied_2() { }
    }
}
namespace builtin_interfaces.msg
{
    public sealed class Time : global::ROS2.Message, global::System.IDisposable
    {
        public int Sec { get; set; }
        public uint Nanosec { get; set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public bool IsDisposed { get; private set; }
        public void Dispose() { DisposeCalls++; IsDisposed = true; if (ThrowOnDispose) throw new global::System.InvalidOperationException(""time-cleanup""); }
    }
}
namespace Unity2Foxglove.FoxRun.CustomRos2Typesupport
{
    public static class FoxRunRos2CustomTypesupportMetadata
    {
        public const int InterfaceRevision = 1;
        public const string InterfaceDigest = ""digest"";
        public const string BaseRuntimePackageId = ""runtime"";
    }
}
namespace unity2foxglove_foxrun_interfaces_v1.msg
{
    public abstract class DisposableMessage : global::ROS2.Message, global::System.IDisposable
    {
        public int DisposeCalls { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public string DisposeFailureMessage { get; set; }
        public bool IsDisposed { get; private set; }
        public virtual void Dispose() { DisposeCalls++; IsDisposed = true; if (ThrowOnDispose) throw new global::System.InvalidOperationException(DisposeFailureMessage ?? ""cleanup""); }
    }
    public sealed class Phase181NestedState3281D0E21244 : DisposableMessage
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
        public bool Foxrun_has_label { get; set; }
    }
    public sealed class Phase181State48D288ED82F1 : DisposableMessage
    {
        public byte[] Bytes { get; set; }
        public bool Foxrun_has_bytes { get; set; }
        public int Count { get; set; }
        public Phase181NestedState3281D0E21244[] Children { get; set; }
        public bool Foxrun_has_children { get; set; }
        public ushort Kind { get; set; }
        public string[] Labels { get; set; }
        public bool Foxrun_has_labels { get; set; }
        public string Message { get; set; }
        public bool Foxrun_has_message { get; set; }
        public Phase181NestedState3281D0E21244 Nested { get; set; }
        public bool Foxrun_has_nested { get; set; }
        public int Optional_count { get; set; }
        public bool Foxrun_has_optional_count { get; set; }
        public string Optional_text { get; set; }
        public bool Foxrun_has_optional_text { get; set; }
        public long[] Values { get; set; }
        public bool Foxrun_has_values { get; set; }
    }
    public sealed class Phase181State48D288ED82F1Envelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase181State48D288ED82F1 Payload { get; set; }
    }
    public sealed class Phase184OtherStateA184D001 : DisposableMessage
    {
        public Phase181NestedState3281D0E21244[] Children { get; set; }
        public bool Foxrun_has_children { get; set; }
        public int Optional_count { get; set; }
        public bool Foxrun_has_optional_count { get; set; }
        public ushort Optional_kind { get; set; }
        public bool Foxrun_has_optional_kind { get; set; }
    }
    public sealed class Phase184OtherStateA184D001Envelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase184OtherStateA184D001 Payload { get; set; }
    }
}";

        private const string CustomStreamDynamicSupport = CustomMapperDynamicSupport + @"
namespace Phase184
{
    public sealed class StreamProbeState
    {
        private int _value;
        public StreamProbeState()
        {
            ConstructorThreadId = global::System.Environment.CurrentManagedThreadId;
        }
        public static int ConstructorThreadId { get; private set; }
        public static int SetterThreadId { get; private set; }
        public int Value
        {
            get => _value;
            set
            {
                SetterThreadId = global::System.Environment.CurrentManagedThreadId;
                _value = value;
            }
        }
    }

    public partial class GeneratedStream
    {
        private readonly global::Unity.FoxgloveSDK.Components.FoxRunStream<StreamProbeState> State =
            new global::Unity.FoxgloveSDK.Components.FoxRunStream<StreamProbeState>();
        public int StreamCount => State.Count;
        public int LastValue { get; private set; }
        public int DrainStream() => State.Drain(value => LastValue = value.Value);
    }

    public sealed class CapturingStreamRegistrar :
        global::Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar
    {
        private global::System.Func<bool> _tryAdmit;
        private global::System.Func<
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope,
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext,
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope> _materialize;
        private global::System.Action<
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope> _transfer;

        public global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope LastOwnedEnvelope { get; private set; }
        public global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184E LastOwnedPayload { get; private set; }

        public void Register<T>(
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract contract,
            global::System.Func<T, global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext, T> copy,
            global::System.Action<T> dispose,
            global::System.Action<T> apply,
            global::System.Func<T, bool> clearIfOwned,
            global::System.Func<T, T, bool> valuesEqual,
            global::System.Func<bool> consumeTrigger,
            global::System.Func<bool> canApply)
            where T : global::ROS2.Message, new()
        {
            throw new global::System.InvalidOperationException(""Expected stream registration."");
        }

        public void RegisterStream<TTransport, TSample>(
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract contract,
            global::System.Func<bool> tryAdmitInput,
            global::System.Func<TTransport, global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext, TSample> materializeOwned,
            global::System.Action<TSample> transferOwned,
            global::System.Action clearOwned,
            global::System.Func<bool> cancelAdmissionCredit = null)
            where TTransport : global::ROS2.Message, new()
        {
            _tryAdmit = tryAdmitInput;
            _materialize = (source, budget) =>
                (global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope)(object)
                    materializeOwned((TTransport)(object)source, budget);
            _transfer = owned => transferOwned((TSample)(object)owned);
        }

        public void Emit(
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope borrowed)
        {
            if (!_tryAdmit()) return;
            LastOwnedEnvelope = _materialize(
                borrowed,
                new global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext(1024 * 1024));
            LastOwnedPayload = LastOwnedEnvelope.Payload;
            _transfer(LastOwnedEnvelope);
        }
    }
}

namespace unity2foxglove_foxrun_interfaces_v1.msg
{
    public sealed class Phase184StreamProbeState184E : DisposableMessage
    {
        public int Value { get; set; }
    }

    public sealed class Phase184StreamProbeState184EEnvelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase184StreamProbeState184E Payload { get; set; }
    }
}";

        private static TestR2fuMember CreateCustomMember(
            int mode = (int)FoxRunFlow.PublishAndSubscribe,
            string source = FoxRunR2fuGenerationConstants.ProviderId)
        {
            var nested = new FoxRunRos2CustomDtoShape(
                "Phase181.NestedState",
                "phase181/NestedState",
                "Phase181NestedState3281D0E21244",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Enabled", "enabled", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Boolean", "bool", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Label", "label", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                },
                diagnostics: Array.Empty<string>());
            var state = new FoxRunRos2CustomDtoShape(
                "Phase181.State",
                "phase181/State",
                "Phase181State48D288ED82F1",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape("Bytes", "bytes", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Byte[]", "uint8[]", "System.Byte", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.Array),
                    new FoxRunRos2CustomDtoMemberShape("Count", "count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Int32", "int32", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Children", "children", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<Phase181.NestedState>",
                        "Phase181NestedState3281D0E21244[]",
                        "Phase181.NestedState",
                        nested.CanonicalIdentity,
                        true,
                        true,
                        true,
                        FoxRunRos2CustomDtoSequenceRepresentation.List,
                        nested),
                    new FoxRunRos2CustomDtoMemberShape("Kind", "kind", FoxRunRos2CustomDtoMemberKind.Enum,
                        "Phase181.StateKind", "uint16", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Labels", "labels", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.String[]", "string[]", "System.String", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.Array),
                    new FoxRunRos2CustomDtoMemberShape("Message", "message", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Nested", "nested", FoxRunRos2CustomDtoMemberKind.NestedDto,
                        "Phase181.NestedState", "Phase181NestedState3281D0E21244", "", nested.CanonicalIdentity,
                        true, true, true, nestedShape: nested),
                    new FoxRunRos2CustomDtoMemberShape("OptionalCount", "optional_count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Nullable<System.Int32>", "int32", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("OptionalText", "optional_text", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Values", "values", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<System.Int64>", "int64[]", "System.Int64", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.List),
                },
                diagnostics: Array.Empty<string>());
            return new TestR2fuMember(
                "State",
                "Phase181.State",
                "/phase181/custom-state",
                10f,
                "phase181.State",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                mode: mode,
                canonicalType: "phase181/State",
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: source,
                qosProfile: FoxRunR2fuGenerationConstants.Inherit,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: state,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Reliability
                    | FoxRunNamedArgumentPresence.Durability
                    | FoxRunNamedArgumentPresence.History
                    | FoxRunNamedArgumentPresence.Depth,
                qosReliability: "reliable",
                qosDurability: "volatile",
                qosHistory: "keep-last",
                qosDepth: 10);
        }

    }
}
