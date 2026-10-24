// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    /// <summary>
    /// Selects exactly one Phase184 route component before ordinary Unity
    /// lifecycle methods register generated FoxRun contracts. Keeping the
    /// other route GameObjects inactive prevents the Foxglove-only case from
    /// acquiring Native or Bridge demand.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    [AddComponentMenu("Foxglove/Manual Acceptance/Phase184 Profile Acceptance")]
    /// <remarks>Phase192 source sections: Phase184FoxRunProfileAcceptance.Lifecycle.cs, Phase184FoxRunProfileAcceptance.Context.cs, Phase184TransportClientMarkerState.cs, Phase184AcceptanceRoute.cs, Phase184FoxgloveProfileRoute.cs, Phase184MultiTargetRoute.cs, Phase184DegradedTargetRoute.cs, Phase184QosContractRoute.cs, Phase184StreamRoute.cs, Phase184AcceptanceText.cs</remarks>
    public sealed partial class Phase184FoxRunProfileAcceptance : MonoBehaviour
    {
        public const string FoxgloveProfileCase = "foxglove-profile";
        public const string MultiTargetCase = "multi-target";
        public const string DegradedTargetCase = "degraded-target";
        public const string QosContractCase = "qos-contract";
        public const string StreamCase = "stream-640hz";
        public const string NativeTransportId = "unity2foxglove.r2fu";

        private const int MaximumStatusCharacters = 512;
        private const int MaximumConfigBytes = 1024 * 1024;
        private const int MaximumTransportClientMarkerCount = 8;
        private const float TransportClientSampleIntervalSeconds = 0.05f;
        private const string BatchConfigArgument = "-phase184RunConfig";
        private const string ManualPointerRelativePath =
            "build/phase184/acceptance/manual-active.json";

        [Header("Manager Under Test")]
        [SerializeField] private FoxgloveManager _manager;

        [Header("Case-isolated Routes")]
        [SerializeField] private Phase184FoxgloveProfileRoute _foxgloveProfile;
        [SerializeField] private Phase184MultiTargetRoute _multiTarget;
        [SerializeField] private Phase184DegradedTargetRoute _degradedTarget;
        [SerializeField] private Phase184QosContractRoute _qosContract;
        [SerializeField] private Phase184StreamRoute _stream;

        [Header("Read-only Run Evidence")]
        [SerializeField] private string _selectedCase = string.Empty;
        [SerializeField] private string _status = "Waiting for a Phase184-G run context.";
        [SerializeField] private string _requestedPublishProfile = string.Empty;
        [SerializeField] private string _requestedSubscribeProfile = string.Empty;
        [SerializeField] private string _effectivePublishProfile = string.Empty;
        [SerializeField] private string _effectiveSubscribeProfile = string.Empty;
        [SerializeField] private string _tokenDigestPrefix = string.Empty;
        [SerializeField] private bool _contextValidated;

        [NonSerialized] private string _runToken = string.Empty;
        private Phase184AcceptanceRoute _activeRoute;
        private bool _profileEvidenceEmitted;
        private readonly Phase184TransportClientMarkerState
            _transportClientMarkerState =
                new Phase184TransportClientMarkerState(
                    MaximumTransportClientMarkerCount);
        private float _nextTransportClientSampleAt;

    }
}
