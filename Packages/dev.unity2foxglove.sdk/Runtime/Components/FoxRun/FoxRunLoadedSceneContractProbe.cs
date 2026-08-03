// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Scene-scoped neutral transport declaration evidence.

using System;
using System.Collections.Generic;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxRunLoadedSceneContractDescriptor
    {
        public FoxRunLoadedSceneContractDescriptor(
            string declaringType,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId)
        {
            DeclaringType = declaringType ?? string.Empty;
            PublishTransportIds = publishTransportIds;
            SubscribeTransportId = subscribeTransportId;
        }

        public string DeclaringType { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
    }

    /// <summary>
    /// Immutable scene-scoped transport evidence. Project-wide generated
    /// metadata is deliberately excluded so unrelated scripts and samples
    /// cannot expose Provider controls for the scene being edited.
    /// </summary>
    public sealed class FoxRunLoadedSceneContractSnapshot
    {
        private readonly HashSet<string> _declaringTypes;
        private readonly HashSet<string> _publishTransportIds;
        private readonly HashSet<string> _subscribeTransportIds;

        internal FoxRunLoadedSceneContractSnapshot(
            HashSet<string> declaringTypes,
            HashSet<string> publishTransportIds,
            HashSet<string> subscribeTransportIds)
        {
            _declaringTypes = declaringTypes
                              ?? new HashSet<string>(StringComparer.Ordinal);
            _publishTransportIds = publishTransportIds
                                   ?? new HashSet<string>(StringComparer.Ordinal);
            _subscribeTransportIds = subscribeTransportIds
                                     ?? new HashSet<string>(StringComparer.Ordinal);
        }

        public bool ContainsDeclaringType(string declaringType)
            => !string.IsNullOrEmpty(declaringType)
               && _declaringTypes.Contains(declaringType);

        public bool HasExplicitPublishTransport(string transportId)
            => !string.IsNullOrWhiteSpace(transportId)
               && _publishTransportIds.Contains(transportId.Trim());

        internal IEnumerable<string> ExplicitPublishTransportIds =>
            _publishTransportIds;

        public bool HasExplicitSubscribeTransport(string transportId)
            => !string.IsNullOrWhiteSpace(transportId)
               && _subscribeTransportIds.Contains(transportId.Trim());
    }

    public static class FoxRunLoadedSceneContractProbe
    {
        private const int MaxTopicsPerSource = 4096;

        public static FoxRunLoadedSceneContractSnapshot InspectContracts(
            IEnumerable<FoxRunLoadedSceneContractDescriptor> contracts)
        {
            var declaringTypes = NewSet();
            var publishIds = NewSet();
            var subscribeIds = NewSet();
            if (contracts != null)
            {
                foreach (var contract in contracts)
                {
                    Add(declaringTypes, contract.DeclaringType);
                    AddRange(publishIds, contract.PublishTransportIds);
                    Add(subscribeIds, contract.SubscribeTransportId);
                }
            }

            return new FoxRunLoadedSceneContractSnapshot(
                declaringTypes,
                publishIds,
                subscribeIds);
        }

#if UNITY_5_3_OR_NEWER
        public static FoxRunLoadedSceneContractSnapshot CaptureLoadedScenes()
        {
            var declaringTypes = NewSet();
            var publishIds = NewSet();
            var subscribeIds = NewSet();
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (!IsLoadedActiveSceneObject(behaviour))
                    continue;

                var publishSource = behaviour as IFoxgloveLogSource;
                var inputSource = behaviour as IFoxgloveInputSource;
                if (publishSource == null && inputSource == null)
                    continue;

                Add(declaringTypes, behaviour.GetType().FullName);
                InspectPublishIds(publishSource, publishIds);
                InspectSubscribeIds(inputSource, subscribeIds);
            }

            return new FoxRunLoadedSceneContractSnapshot(
                declaringTypes,
                publishIds,
                subscribeIds);
        }

        private static bool IsLoadedActiveSceneObject(MonoBehaviour behaviour)
        {
            if (behaviour == null)
                return false;
            var gameObject = behaviour.gameObject;
            return gameObject != null
                   && gameObject.scene.IsValid()
                   && gameObject.scene.isLoaded
                   && gameObject.activeInHierarchy;
        }

        private static void InspectPublishIds(
            IFoxgloveLogSource source,
            ISet<string> destination)
        {
            if (source == null)
                return;
            try
            {
                var count = Math.Min(
                    MaxTopicsPerSource,
                    Math.Max(0, source.FoxgloveLog_TopicCount));
                for (var index = 0; index < count; index++)
                    AddRange(
                        destination,
                        source.FoxgloveLog_GetTopic(index)
                            .PublishTransportIds);
            }
            catch (Exception)
            {
                // The owning runtime hub reports malformed generated sources.
            }
        }

        private static void InspectSubscribeIds(
            IFoxgloveInputSource source,
            ISet<string> destination)
        {
            if (source == null)
                return;
            try
            {
                var count = Math.Min(
                    MaxTopicsPerSource,
                    Math.Max(0, source.FoxgloveInput_TopicCount));
                for (var index = 0; index < count; index++)
                    Add(
                        destination,
                        source.FoxgloveInput_GetTopic(index)
                            .SubscribeTransportId);
            }
            catch (Exception)
            {
                // The owning runtime hub reports malformed generated sources.
            }
        }
#endif

        private static HashSet<string> NewSet()
            => new HashSet<string>(StringComparer.Ordinal);

        private static void Add(ISet<string> destination, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                destination.Add(value.Trim());
        }

        private static void AddRange(
            ISet<string> destination,
            IEnumerable<string> values)
        {
            if (values == null)
                return;
            foreach (var value in values)
                Add(destination, value);
        }
    }
}
