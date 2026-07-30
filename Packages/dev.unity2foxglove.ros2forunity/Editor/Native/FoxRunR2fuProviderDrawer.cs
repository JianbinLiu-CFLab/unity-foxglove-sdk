// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native.Editor
// Purpose: Manager Inspector contribution for the R2FU Provider.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Native.Editor
{
    [InitializeOnLoad]
    internal sealed class FoxRunR2fuProviderDrawer :
        IFoxRunTransportProviderDrawer
    {
        static FoxRunR2fuProviderDrawer()
        {
            FoxRunTransportProviderDrawerRegistry.Register(
                new FoxRunR2fuProviderDrawer());
        }

        public string TransportId =>
            FoxRunRos2TransportProvider.IdValue;

        public string DisplayName =>
            "ROS 2 Native (R2FU)";

        public FoxRunTransportCapabilities Capabilities =>
            FoxRunTransportCapabilities.Publish
            | FoxRunTransportCapabilities.Subscribe;

        public void EnsureProvider(FoxgloveManager manager)
        {
            if (manager == null
                || manager.GetComponent<
                    FoxRunRos2TransportProvider>() != null)
            {
                return;
            }

            var provider =
                Undo.AddComponent<
                    FoxRunRos2TransportProvider>(
                    manager.gameObject);
            PrefabUtility
                .RecordPrefabInstancePropertyModifications(
                    provider);
            if (manager.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(
                    manager.gameObject.scene);
            }
        }

        public void Draw(
            FoxgloveManager manager,
            SerializedObject managerObject)
        {
            _ = managerObject;
            if (manager == null)
                return;

            var provider =
                manager.GetComponent<
                    FoxRunRos2TransportProvider>();
            if (provider == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                DisplayName,
                EditorStyles.boldLabel);
            using (var providerObject =
                   new SerializedObject(provider))
            {
                providerObject.Update();
                DrawProperty(
                    providerObject,
                    "_publishQos",
                    "Publish QoS");
                DrawProperty(
                    providerObject,
                    "_subscribeQos",
                    "Subscribe QoS");
                DrawProperty(
                    providerObject,
                    "_nativeCopyBudgetBytes",
                    "Native Copy Budget (bytes)");
                providerObject.ApplyModifiedProperties();
            }

            FoxRunRos2CustomTypesupportInspector
                .DrawCustomTypesupportPreflight(
                    CollectCustomTypesupportContracts());
            FoxRunRos2SubscriptionDiagnosticsInspector
                .DrawFoxRunNativeSubscriptionDiagnostics();
        }

        private static IReadOnlyList<
            Ros2ForUnityCustomTypesupportContract>
            CollectCustomTypesupportContracts()
        {
            var model =
                FoxrunCodeGenerator
                    .CollectReflectionGenerationModelForTransportProviders();
            return model.Types
                .SelectMany(type => type.Members)
                .Select(FoxRunR2fuTopicMember.Create)
                .Where(member =>
                    member.GeneratesRos2NativeRegistration
                    && member.Ros2ContractKind
                    == FoxRunRos2ContractKind.CustomDto
                    && member.Ros2CustomDtoShape != null
                    && !string.IsNullOrWhiteSpace(
                        member.Ros2CustomDtoShape
                            .PayloadIdentity))
                .Select(member => new
                    Ros2ForUnityCustomTypesupportContract(
                        FoxRunRos2InterfaceIdentity
                            .BuildEnvelopeMessageName(
                                member.Ros2CustomDtoShape
                                    .PayloadIdentity),
                        DirectionalPolicyLabel(member)))
                .GroupBy(
                    contract =>
                        contract.CanonicalEnvelopeType
                        + "\u001f"
                        + contract.DirectionalPolicy,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    contract =>
                        contract.CanonicalEnvelopeType,
                    StringComparer.Ordinal)
                .ThenBy(
                    contract =>
                        contract.DirectionalPolicy,
                    StringComparer.Ordinal)
                .ToArray();
        }

        private static string DirectionalPolicyLabel(
            FoxRunR2fuTopicMember member)
        {
            var direction = member.Mode == 1
                ? "Outbound"
                : member.Mode == 2
                    ? "Inbound"
                    : member.Mode == 3
                        ? "Inbound and outbound"
                        : "Direction unavailable";
            var policies = new[]
                {
                    member.QosProfile,
                    member.QosReliability,
                    member.QosDurability,
                    member.QosHistory,
                    member.QosDepth > 0
                        ? "depth " + member.QosDepth
                        : string.Empty
                }
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value)
                    && !string.Equals(
                        value,
                        FoxRunR2fuGenerationConstants
                            .Inherit,
                        StringComparison.Ordinal))
                .ToArray();
            return direction
                   + " / "
                   + (policies.Length == 0
                       ? "Default"
                       : string.Join(", ", policies));
        }

        private static void DrawProperty(
            SerializedObject serializedObject,
            string propertyName,
            string label)
        {
            var property =
                serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(
                    property,
                    new GUIContent(label),
                    includeChildren: true);
            }
        }
    }
}
