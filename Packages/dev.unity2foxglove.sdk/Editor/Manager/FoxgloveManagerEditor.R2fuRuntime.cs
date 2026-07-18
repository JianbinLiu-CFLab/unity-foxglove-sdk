// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Optional R2FU runtime selection for both native publish and subscribe demand.

using System.Collections;
using System.Collections.Generic;
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
        private const string R2fuCustomTypesupportInspectorTypeName =
            "Unity2Foxglove.Ros2ForUnity.Editor.FoxRunRos2CustomTypesupportInspector, Unity2Foxglove.Ros2ForUnity.Editor";
        private const string GeneratedFoxRunSchemaInfoTypeName =
            "Unity.FoxgloveSDK.Generated.FoxRunSchemaInfo";

        private static bool _r2fuRuntimeSelectorResolved;
        private static MethodInfo _r2fuRuntimeSelectorDrawMethod;
        private static bool _r2fuNativeSubscriptionDiagnosticsResolved;
        private static MethodInfo _r2fuNativeSubscriptionDiagnosticsDrawMethod;
        private static bool _r2fuCustomTypesupportInspectorResolved;
        private static MethodInfo _r2fuCustomTypesupportInspectorDrawMethod;
        private static bool _generatedFoxRunSubscriptionBindingsResolved;
        private static FieldInfo _generatedFoxRunSubscriptionBindingsField;
        private static IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> _generatedFoxRunSubscriptionBindings;
        private static bool _generatedFoxRunCustomNativeContractsResolved;
        private static FieldInfo _generatedFoxRunCustomNativeContractsField;
        private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> _generatedFoxRunCustomNativeContracts;

        private static void ResetOptionalR2fuRuntimeSelectorCache()
        {
            _r2fuRuntimeSelectorResolved = false;
            _r2fuRuntimeSelectorDrawMethod = null;
            _r2fuNativeSubscriptionDiagnosticsResolved = false;
            _r2fuNativeSubscriptionDiagnosticsDrawMethod = null;
            _r2fuCustomTypesupportInspectorResolved = false;
            _r2fuCustomTypesupportInspectorDrawMethod = null;
            _generatedFoxRunSubscriptionBindingsResolved = false;
            _generatedFoxRunSubscriptionBindingsField = null;
            _generatedFoxRunSubscriptionBindings = null;
            _generatedFoxRunCustomNativeContractsResolved = false;
            _generatedFoxRunCustomNativeContractsField = null;
            _generatedFoxRunCustomNativeContracts = null;
        }

        private bool HasR2fuNativeRuntimeDemand()
            => GetBool("_ros2NativeEnabled")
               || HasR2fuNativeSubscriptionDemand()
               || HasCustomNativeSubscriptionDemand();

        private bool HasR2fuNativeSubscriptionDemand()
        {
            var provider = GetDefaultSubscriptionProvider();
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
            var subscriptionDemand = HasR2fuNativeSubscriptionDemand() || HasCustomNativeSubscriptionDemand();
            if (outputDemand && subscriptionDemand)
            {
                EditorGUILayout.HelpBox(
                    "This shared runtime/RMW selection is used by native Publish Data and Subscribe Data. Subscribe Data does not enable Publish Data.",
                    MessageType.Info);
            }
            else if (subscriptionDemand)
            {
                EditorGUILayout.HelpBox(
                    "This shared runtime/RMW selection is currently required by Subscribe Data. Subscribe Data does not enable Publish Data.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "This shared runtime/RMW selection is currently required by Publish Data.",
                    MessageType.Info);
            }

            DrawOptionalR2fuRuntimeSelector();
            if (HasCustomNativeContractDemand())
                DrawOptionalR2fuCustomTypesupportInspector();
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

        private void DrawOptionalR2fuCustomTypesupportInspector()
        {
            var drawMethod = ResolveR2fuCustomTypesupportInspectorDrawMethod();
            if (drawMethod == null)
            {
                EditorGUILayout.HelpBox(
                    "Custom FoxRun ROS 2 interface readiness becomes available after the ROS2 For Unity adapter package is installed.",
                    MessageType.Info);
                return;
            }

            try
            {
                drawMethod.Invoke(null, new object[] { GetGeneratedCustomNativeContracts() });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                DrawOptionalR2fuInspectorFailure("Custom FoxRun ROS 2 interface readiness");
            }
            catch (System.Exception)
            {
                DrawOptionalR2fuInspectorFailure("Custom FoxRun ROS 2 interface readiness");
            }
        }

        private static MethodInfo ResolveR2fuCustomTypesupportInspectorDrawMethod()
        {
            if (_r2fuCustomTypesupportInspectorResolved)
                return _r2fuCustomTypesupportInspectorDrawMethod;

            _r2fuCustomTypesupportInspectorResolved = true;
            var inspectorType = System.Type.GetType(R2fuCustomTypesupportInspectorTypeName);
            _r2fuCustomTypesupportInspectorDrawMethod = inspectorType?.GetMethod(
                "DrawCustomTypesupportPreflight",
                BindingFlags.Public | BindingFlags.Static);
            return _r2fuCustomTypesupportInspectorDrawMethod;
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
            return HasExplicitProvider(GetGeneratedSubscriptionBindings(), provider);
        }

        private FoxRunSubscriptionProvider GetDefaultSubscriptionProvider()
        {
            var providerProperty = FindCachedProperty("_defaultFoxRunSubscriptionProvider");
            return providerProperty != null
                   && providerProperty.enumValueIndex == (int)FoxRunSubscriptionProvider.Ros2Native
                ? FoxRunSubscriptionProvider.Ros2Native
                : FoxRunSubscriptionProvider.FoxgloveWebSocket;
        }

        private bool HasCustomNativeContractDemand()
            => FoxRunCustomNativeContractDemandPolicy.HasDemand(
                GetGeneratedCustomNativeContracts(),
                nativeOutputEnabled: GetBool("_ros2NativeEnabled"),
                subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                defaultSubscriptionProvider: GetDefaultSubscriptionProvider());

        private bool HasCustomNativeSubscriptionDemand()
            => FoxRunCustomNativeContractDemandPolicy.HasDemand(
                GetGeneratedCustomNativeContracts(),
                nativeOutputEnabled: false,
                subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                defaultSubscriptionProvider: GetDefaultSubscriptionProvider());

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

        private static IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> GetGeneratedSubscriptionBindings()
        {
            if (_generatedFoxRunSubscriptionBindings != null)
                return _generatedFoxRunSubscriptionBindings;

            var current = FoxRunSchemaInfoRegistry.Current;
            if (current?.SubscriptionBindings != null)
            {
                _generatedFoxRunSubscriptionBindings = current.SubscriptionBindings;
                return _generatedFoxRunSubscriptionBindings;
            }

            if (ResolveGeneratedFoxRunSubscriptionBindingsField()?.GetValue(null)
                is IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> bindings)
            {
                _generatedFoxRunSubscriptionBindings = bindings;
                return _generatedFoxRunSubscriptionBindings;
            }

            _generatedFoxRunSubscriptionBindings = System.Array.Empty<FoxRunSchemaSubscriptionBindingInfo>();
            return _generatedFoxRunSubscriptionBindings;
        }

        private static FieldInfo ResolveGeneratedFoxRunCustomNativeContractsField()
        {
            if (_generatedFoxRunCustomNativeContractsResolved)
                return _generatedFoxRunCustomNativeContractsField;

            _generatedFoxRunCustomNativeContractsResolved = true;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var generatedType = assembly.GetType(GeneratedFoxRunSchemaInfoTypeName, throwOnError: false);
                if (generatedType == null)
                    continue;

                _generatedFoxRunCustomNativeContractsField = generatedType.GetField(
                    "CustomNativeContracts",
                    BindingFlags.Public | BindingFlags.Static);
                break;
            }

            return _generatedFoxRunCustomNativeContractsField;
        }

        private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> GetGeneratedCustomNativeContracts()
        {
            if (_generatedFoxRunCustomNativeContracts != null)
                return _generatedFoxRunCustomNativeContracts;

            var current = FoxRunSchemaInfoRegistry.Current;
            if (current?.CustomNativeContracts != null)
            {
                _generatedFoxRunCustomNativeContracts = current.CustomNativeContracts;
                return _generatedFoxRunCustomNativeContracts;
            }

            if (ResolveGeneratedFoxRunCustomNativeContractsField()?.GetValue(null)
                is IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> contracts)
            {
                _generatedFoxRunCustomNativeContracts = contracts;
                return _generatedFoxRunCustomNativeContracts;
            }

            _generatedFoxRunCustomNativeContracts = System.Array.Empty<FoxRunSchemaCustomNativeContractInfo>();
            return _generatedFoxRunCustomNativeContracts;
        }
    }
}
