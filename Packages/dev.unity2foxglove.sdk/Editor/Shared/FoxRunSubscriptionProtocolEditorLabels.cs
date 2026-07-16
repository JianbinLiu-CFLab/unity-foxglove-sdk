// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Maps the combined Subscribe Data Inspector choice onto independent FoxRun policy axes.

using Unity.FoxgloveSDK.Components;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Unity-free model for the Manager Inspector's combined subscription-protocol popup.
    /// The stored WebSocket encoding remains meaningful while the native provider is selected.
    /// </summary>
    internal static class FoxRunSubscriptionProtocolEditorModel
    {
        internal const int WebSocketProtobuf = 0;
        internal const int WebSocketJson = 1;
        internal const int Ros2Native = 2;

        /// <summary>
        /// Normalizes old or malformed serialized values before the popup is drawn and returns
        /// the option that represents the resulting independent provider/encoding fields.
        /// </summary>
        internal static int NormalizeForDrawing(
            ref FoxRunSubscriptionProvider provider,
            ref FoxRunWireEncoding webSocketEncoding)
        {
            provider = provider == FoxRunSubscriptionProvider.Ros2Native
                ? FoxRunSubscriptionProvider.Ros2Native
                : FoxRunSubscriptionProvider.FoxgloveWebSocket;
            webSocketEncoding = webSocketEncoding == FoxRunWireEncoding.Json
                ? FoxRunWireEncoding.Json
                : FoxRunWireEncoding.Protobuf;

            if (provider == FoxRunSubscriptionProvider.Ros2Native)
                return Ros2Native;

            return webSocketEncoding == FoxRunWireEncoding.Json
                ? WebSocketJson
                : WebSocketProtobuf;
        }

        /// <summary>
        /// Applies an Inspector option. Selecting native deliberately preserves the stored
        /// WebSocket encoding so a later return to WebSocket restores the user's choice.
        /// </summary>
        internal static void ApplySelection(
            int selection,
            ref FoxRunSubscriptionProvider provider,
            ref FoxRunWireEncoding webSocketEncoding)
        {
            switch (selection)
            {
                case Ros2Native:
                    provider = FoxRunSubscriptionProvider.Ros2Native;
                    return;
                case WebSocketJson:
                    provider = FoxRunSubscriptionProvider.FoxgloveWebSocket;
                    webSocketEncoding = FoxRunWireEncoding.Json;
                    return;
                default:
                    provider = FoxRunSubscriptionProvider.FoxgloveWebSocket;
                    webSocketEncoding = FoxRunWireEncoding.Protobuf;
                    return;
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>Unity Inspector presentation for <see cref="FoxRunSubscriptionProtocolEditorModel"/>.</summary>
    internal static class FoxRunSubscriptionProtocolEditorLabels
    {
        private static readonly string[] ProtocolLabels =
        {
            "Foxglove WebSocket / " + FoxRunEncodingEditorLabels.ToDisplayLabel(FoxRunWireEncoding.Protobuf),
            "Foxglove WebSocket / " + FoxRunEncodingEditorLabels.ToDisplayLabel(FoxRunWireEncoding.Json),
            "ROS2 Native (R2FU)"
        };

        internal static void Draw(
            SerializedProperty providerProperty,
            SerializedProperty webSocketEncodingProperty,
            string label)
        {
            if (providerProperty == null || webSocketEncodingProperty == null)
                return;

            var provider = (FoxRunSubscriptionProvider)providerProperty.enumValueIndex;
            var webSocketEncoding = (FoxRunWireEncoding)webSocketEncodingProperty.enumValueIndex;
            var selected = FoxRunSubscriptionProtocolEditorModel.NormalizeForDrawing(
                ref provider,
                ref webSocketEncoding);
            var changedSelection = EditorGUILayout.Popup(label, selected, ProtocolLabels);
            FoxRunSubscriptionProtocolEditorModel.ApplySelection(
                changedSelection,
                ref provider,
                ref webSocketEncoding);

            providerProperty.enumValueIndex = (int)provider;
            webSocketEncodingProperty.enumValueIndex = (int)webSocketEncoding;
        }
    }
#endif
}
