// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Optional R2FU runtime selection for both native publish and subscribe demand.

using System.Collections;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        // The core Editor assembly intentionally has no optional R2FU asmdef reference.
        // These are its only late-bound endpoints; keep each type-and-assembly name whole
        // so boundary review can audit the reflection seam without concealing it in fragments.
        private const string R2fuRuntimeSelectorInspectorTypeName =
            "Unity2Foxglove.Ros2ForUnity.Editor.Ros2ForUnityRuntimeSelectorInspector, Unity2Foxglove.Ros2ForUnity.Editor";
        private const string R2fuNativeSubscriptionDiagnosticsInspectorTypeName =
            "Unity2Foxglove.Ros2ForUnity.Native.Editor.FoxRunRos2SubscriptionDiagnosticsInspector, Unity2Foxglove.Ros2ForUnity.Native.Editor";
        private const string GeneratedFoxRunSchemaInfoTypeName =
            "Unity.FoxgloveSDK.Generated.FoxRunSchemaInfo";

        private static bool _r2fuRuntimeSelectorResolved;
        private static MethodInfo _r2fuRuntimeSelectorDrawMethod;
        private static bool _r2fuNativeSubscriptionDiagnosticsResolved;
        private static MethodInfo _r2fuNativeSubscriptionDiagnosticsDrawMethod;
        private static bool _generatedFoxRunSubscriptionBindingsResolved;
        private static FieldInfo _generatedFoxRunSubscriptionBindingsField;

        private static void ResetOptionalR2fuRuntimeSelectorCache()
        {
            _r2fuRuntimeSelectorResolved = false;
            _r2fuRuntimeSelectorDrawMethod = null;
            _r2fuNativeSubscriptionDiagnosticsResolved = false;
            _r2fuNativeSubscriptionDiagnosticsDrawMethod = null;
            _generatedFoxRunSubscriptionBindingsResolved = false;
            _generatedFoxRunSubscriptionBindingsField = null;
        }

        private bool HasR2fuNativeRuntimeDemand()
            => GetBool("_ros2NativeEnabled") || HasR2fuNativeSubscriptionDemand();

        private bool HasR2fuNativeSubscriptionDemand()
        {
            var providerProperty = FindCachedProperty("_defaultFoxRunSubscriptionProvider");
            var provider = providerProperty != null
                           && providerProperty.enumValueIndex == (int)FoxRunSubscriptionProvider.Ros2Native
                ? FoxRunSubscriptionProvider.Ros2Native
                : FoxRunSubscriptionProvider.FoxgloveWebSocket;
            return FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                defaultSubscriptionProvider: provider,
                hasExplicitNativeContract: HasGeneratedExplicitSubscriptionProvider(
                    FoxRunSubscriptionProvider.Ros2Native));
        }

        private void DrawR2fuRuntimeSection()
        {
            var outputDemand = GetBool("_ros2NativeEnabled");
            var subscriptionDemand = HasR2fuNativeSubscriptionDemand();
            if (outputDemand && subscriptionDemand)
            {
                EditorGUILayout.HelpBox(
                    "This runtime/RMW selection is shared by ROS2 Native Publish Data and Subscribe Data.",
                    MessageType.Info);
            }
            else if (subscriptionDemand)
            {
                EditorGUILayout.HelpBox(
                    "ROS2 Native Subscribe Data requires this runtime/RMW selection even when native Publish Data output is off.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "ROS2 Native Publish Data requires this runtime/RMW selection.",
                    MessageType.Info);
            }

            DrawOptionalR2fuRuntimeSelector();
        }

        private void DrawOptionalR2fuRuntimeSelector()
        {
            var drawMethod = ResolveR2fuRuntimeSelectorDrawMethod();
            if (drawMethod == null)
            {
                EditorGUILayout.HelpBox(
                    "Install the Unity2Foxglove ROS2 For Unity adapter package to select an active R2FU runtime. Native demand remains blocked until that package is available.",
                    MessageType.Warning);
                return;
            }

            try
            {
                drawMethod.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                DrawOptionalR2fuInspectorFailure("ROS2 For Unity runtime selector");
            }
            catch (System.Exception)
            {
                DrawOptionalR2fuInspectorFailure("ROS2 For Unity runtime selector");
            }
        }

        private static MethodInfo ResolveR2fuRuntimeSelectorDrawMethod()
        {
            if (_r2fuRuntimeSelectorResolved)
                return _r2fuRuntimeSelectorDrawMethod;

            _r2fuRuntimeSelectorResolved = true;
            var selectorType = System.Type.GetType(R2fuRuntimeSelectorInspectorTypeName);
            _r2fuRuntimeSelectorDrawMethod = selectorType?.GetMethod(
                "DrawActiveRuntimeSelector",
                BindingFlags.Public | BindingFlags.Static);
            return _r2fuRuntimeSelectorDrawMethod;
        }

        private void DrawOptionalR2fuNativeSubscriptionDiagnostics()
        {
            var drawMethod = ResolveR2fuNativeSubscriptionDiagnosticsDrawMethod();
            if (drawMethod == null)
            {
                EditorGUILayout.HelpBox(
                    "Native subscription diagnostics become available after the ROS2 For Unity adapter and an active native runtime are loaded.",
                    MessageType.Info);
                return;
            }

            try
            {
                drawMethod.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                DrawOptionalR2fuInspectorFailure("ROS2 Native subscription diagnostics");
            }
            catch (System.Exception)
            {
                DrawOptionalR2fuInspectorFailure("ROS2 Native subscription diagnostics");
            }
        }

        private static void DrawOptionalR2fuInspectorFailure(string feature)
        {
            EditorGUILayout.HelpBox(
                feature
                + " failed. Native runtime details are withheld to avoid exposing ROS2 or Zenoh configuration.",
                MessageType.Warning);
        }

        private static MethodInfo ResolveR2fuNativeSubscriptionDiagnosticsDrawMethod()
        {
            if (_r2fuNativeSubscriptionDiagnosticsResolved)
                return _r2fuNativeSubscriptionDiagnosticsDrawMethod;

            _r2fuNativeSubscriptionDiagnosticsResolved = true;
            var diagnosticsType = System.Type.GetType(R2fuNativeSubscriptionDiagnosticsInspectorTypeName);
            _r2fuNativeSubscriptionDiagnosticsDrawMethod = diagnosticsType?.GetMethod(
                "DrawFoxRunNativeSubscriptionDiagnostics",
                BindingFlags.Public | BindingFlags.Static);
            return _r2fuNativeSubscriptionDiagnosticsDrawMethod;
        }

        private static bool HasGeneratedExplicitSubscriptionProvider(FoxRunSubscriptionProvider provider)
        {
            var current = FoxRunSchemaInfoRegistry.Current;
            if (current != null && HasExplicitProvider(current.SubscriptionBindings, provider))
                return true;

            var bindingsField = ResolveGeneratedFoxRunSubscriptionBindingsField();
            if (!(bindingsField?.GetValue(null) is IEnumerable bindings))
                return false;

            foreach (var item in bindings)
            {
                if (item is FoxRunSchemaSubscriptionBindingInfo binding
                    && binding.DeclaredProvider == provider)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasExplicitProvider(
            System.Collections.Generic.IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> bindings,
            FoxRunSubscriptionProvider provider)
        {
            if (provider == FoxRunSubscriptionProvider.Ros2Native)
                return FoxRunNativeDemandPolicy.HasExplicitNativeContract(bindings);

            if (bindings == null)
                return false;

            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null && bindings[i].DeclaredProvider == provider)
                    return true;
            }

            return false;
        }

        private static FieldInfo ResolveGeneratedFoxRunSubscriptionBindingsField()
        {
            if (_generatedFoxRunSubscriptionBindingsResolved)
                return _generatedFoxRunSubscriptionBindingsField;

            _generatedFoxRunSubscriptionBindingsResolved = true;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var generatedType = assembly.GetType(GeneratedFoxRunSchemaInfoTypeName, throwOnError: false);
                if (generatedType == null)
                    continue;

                _generatedFoxRunSubscriptionBindingsField = generatedType.GetField(
                    "SubscriptionBindings",
                    BindingFlags.Public | BindingFlags.Static);
                break;
            }

            return _generatedFoxRunSubscriptionBindingsField;
        }
    }
}
