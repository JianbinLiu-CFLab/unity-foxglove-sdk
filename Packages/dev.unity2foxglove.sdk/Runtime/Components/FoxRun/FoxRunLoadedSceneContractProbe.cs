// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Scene-scoped FoxRun contract evidence for optional transport UI and lifecycle guards.

using System;
using System.Collections.Generic;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Transport declarations observed on one loaded FoxRun source.</summary>
    public readonly struct FoxRunLoadedSceneContractDescriptor
    {
        public FoxRunLoadedSceneContractDescriptor(
            string declaringType,
            bool hasExplicitNativePublishContract,
            bool hasExplicitNativeSubscriptionContract,
            bool hasExplicitFoxgloveSubscriptionContract)
        {
            DeclaringType = declaringType ?? string.Empty;
            HasExplicitNativePublishContract = hasExplicitNativePublishContract;
            HasExplicitNativeSubscriptionContract = hasExplicitNativeSubscriptionContract;
            HasExplicitFoxgloveSubscriptionContract = hasExplicitFoxgloveSubscriptionContract;
        }

        public string DeclaringType { get; }
        public bool HasExplicitNativePublishContract { get; }
        public bool HasExplicitNativeSubscriptionContract { get; }
        public bool HasExplicitFoxgloveSubscriptionContract { get; }
    }

    /// <summary>
    /// Immutable scene-scoped evidence. Project-wide generated metadata is
    /// deliberately excluded because an unrelated script or sample must not
    /// expose optional ROS 2 controls for the scene currently being edited.
    /// </summary>
    public sealed class FoxRunLoadedSceneContractSnapshot
    {
        private readonly HashSet<string> _declaringTypes;

        internal FoxRunLoadedSceneContractSnapshot(
            bool hasExplicitNativePublishContract,
            bool hasExplicitNativeSubscriptionContract,
            bool hasExplicitFoxgloveSubscriptionContract,
            HashSet<string> declaringTypes)
        {
            HasExplicitNativePublishContract = hasExplicitNativePublishContract;
            HasExplicitNativeSubscriptionContract = hasExplicitNativeSubscriptionContract;
            HasExplicitFoxgloveSubscriptionContract = hasExplicitFoxgloveSubscriptionContract;
            _declaringTypes = declaringTypes ?? new HashSet<string>(StringComparer.Ordinal);
        }

        public bool HasExplicitNativePublishContract { get; }
        public bool HasExplicitNativeSubscriptionContract { get; }
        public bool HasExplicitFoxgloveSubscriptionContract { get; }

        public bool ContainsDeclaringType(string declaringType)
            => !string.IsNullOrEmpty(declaringType)
               && _declaringTypes.Contains(declaringType);
    }

    /// <summary>
    /// Captures only actual FoxRun source instances in loaded, active scenes.
    /// This is the shared truth for Inspector visibility and the R2FU Play Mode
    /// lifecycle guard.
    /// </summary>
    public static class FoxRunLoadedSceneContractProbe
    {
        private const int MaxTopicsPerSource = 4096;

        public static FoxRunLoadedSceneContractSnapshot InspectContracts(
            IEnumerable<FoxRunLoadedSceneContractDescriptor> contracts)
        {
            var hasNativePublish = false;
            var hasNativeSubscription = false;
            var hasFoxgloveSubscription = false;
            var declaringTypes = new HashSet<string>(StringComparer.Ordinal);

            if (contracts != null)
            {
                foreach (var contract in contracts)
                {
                    if (!string.IsNullOrEmpty(contract.DeclaringType))
                        declaringTypes.Add(contract.DeclaringType);
                    hasNativePublish |= contract.HasExplicitNativePublishContract;
                    hasNativeSubscription |= contract.HasExplicitNativeSubscriptionContract;
                    hasFoxgloveSubscription |= contract.HasExplicitFoxgloveSubscriptionContract;
                }
            }

            return new FoxRunLoadedSceneContractSnapshot(
                hasNativePublish,
                hasNativeSubscription,
                hasFoxgloveSubscription,
                declaringTypes);
        }

#if UNITY_5_3_OR_NEWER
        public static FoxRunLoadedSceneContractSnapshot CaptureLoadedScenes()
        {
            var hasNativePublish = false;
            var hasNativeSubscription = false;
            var hasFoxgloveSubscription = false;
            var declaringTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                    continue;

                var gameObject = behaviour.gameObject;
                if (gameObject == null
                    || !gameObject.scene.IsValid()
                    || !gameObject.scene.isLoaded
                    || !gameObject.activeInHierarchy)
                {
                    continue;
                }

                var publishSource = behaviour as IFoxgloveLogSource;
                var inputSource = behaviour as IFoxgloveInputSource;
                if (publishSource == null && inputSource == null)
                    continue;

                InspectExplicitSubscriptionSources(
                    inputSource,
                    out var sourceHasNativeSubscription,
                    out var sourceHasFoxgloveSubscription);
                hasNativePublish |= HasExplicitNativePublishContract(publishSource);
                hasNativeSubscription |= sourceHasNativeSubscription;
                hasFoxgloveSubscription |= sourceHasFoxgloveSubscription;
                var declaringType = behaviour.GetType().FullName;
                if (!string.IsNullOrEmpty(declaringType))
                    declaringTypes.Add(declaringType);
            }

            return new FoxRunLoadedSceneContractSnapshot(
                hasNativePublish,
                hasNativeSubscription,
                hasFoxgloveSubscription,
                declaringTypes);
        }

        private static bool HasExplicitNativePublishContract(
            IFoxgloveLogSource source)
        {
            if (source == null)
                return false;

            try
            {
                var count = Math.Min(
                    MaxTopicsPerSource,
                    Math.Max(0, source.FoxgloveLog_TopicCount));
                for (var index = 0; index < count; index++)
                {
                    var topic = source.FoxgloveLog_GetTopic(index);
                    if (topic.HasExplicitTargets
                        && (topic.DeclaredTargets & FoxRunEndpoint.Ros2Native) != 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // A malformed generated source is diagnosed by its owning hub.
                // Do not let one Inspector probe break all Manager rendering.
            }

            return false;
        }

        private static void InspectExplicitSubscriptionSources(
            IFoxgloveInputSource source,
            out bool hasNative,
            out bool hasFoxglove)
        {
            hasNative = false;
            hasFoxglove = false;
            if (source == null)
                return;

            try
            {
                var count = Math.Min(
                    MaxTopicsPerSource,
                    Math.Max(0, source.FoxgloveInput_TopicCount));
                for (var index = 0; index < count; index++)
                {
                    var topic = source.FoxgloveInput_GetTopic(index);
                    if (!topic.HasExplicitSource)
                        continue;
                    hasNative |= topic.DeclaredSource == FoxRunEndpoint.Ros2Native;
                    hasFoxglove |= topic.DeclaredSource == FoxRunEndpoint.Foxglove;
                }
            }
            catch (Exception)
            {
                // Keep the Inspector and Play Mode transition stable. The
                // runtime input hub owns detailed generated-source diagnostics.
            }
        }
#endif
    }
}
