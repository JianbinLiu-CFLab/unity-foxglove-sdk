// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Constrained Inspector presentation for FoxRun endpoint profiles.

using Unity.FoxgloveSDK.Components;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Unity-free normalization for Manager endpoint profile controls.</summary>
    internal static class FoxRunEndpointEditorModel
    {
        private const FoxRunEndpoint KnownTargets =
            FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge;

        internal static FoxRunEndpoint NormalizeSource(FoxRunEndpoint source)
            => source == FoxRunEndpoint.Ros2Native
                ? FoxRunEndpoint.Ros2Native
                : FoxRunEndpoint.Foxglove;

        internal static FoxRunEndpoint NormalizeTargets(FoxRunEndpoint targets)
        {
            targets &= KnownTargets;
            return targets == 0 ? FoxRunEndpoint.Foxglove : targets;
        }

        internal static bool Includes(FoxRunEndpoint targets, FoxRunEndpoint endpoint)
            => (NormalizeTargets(targets) & endpoint) != 0;
    }

#if UNITY_EDITOR
    /// <summary>Unity Inspector presentation for <see cref="FoxRunEndpointEditorModel"/>.</summary>
    internal static class FoxRunEndpointEditorLabels
    {
        private static readonly string[] SourceLabels =
        {
            "Foxglove",
            "ROS 2 Native (R2FU)"
        };

        internal static FoxRunEndpoint DrawSource(SerializedProperty sourceProperty, string label)
        {
            if (sourceProperty == null)
                return FoxRunEndpoint.Foxglove;

            var source = FoxRunEndpointEditorModel.NormalizeSource(
                (FoxRunEndpoint)sourceProperty.intValue);
            var selected = source == FoxRunEndpoint.Ros2Native ? 1 : 0;
            selected = EditorGUILayout.Popup(label, selected, SourceLabels);
            source = selected == 1 ? FoxRunEndpoint.Ros2Native : FoxRunEndpoint.Foxglove;
            sourceProperty.intValue = (int)source;
            return source;
        }

    }
#endif
}
