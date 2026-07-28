// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Optional R2FU runtime selection for both native publish and subscribe demand.

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
        private static bool _r2fuRuntimeSelectorResolved;
        private static MethodInfo _r2fuRuntimeSelectorDrawMethod;
        private static bool _r2fuNativeSubscriptionDiagnosticsResolved;
        private static MethodInfo _r2fuNativeSubscriptionDiagnosticsDrawMethod;
        private static bool _r2fuCustomTypesupportInspectorResolved;
        private static MethodInfo _r2fuCustomTypesupportInspectorDrawMethod;
        private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> _currentFoxRunCustomNativeContracts;
        private FoxRunLoadedSceneContractSnapshot _loadedSceneContractsThisInspectorDraw;

        private static void ResetOptionalR2fuRuntimeSelectorCache()
        {
            _r2fuRuntimeSelectorResolved = false;
            _r2fuRuntimeSelectorDrawMethod = null;
            _r2fuNativeSubscriptionDiagnosticsResolved = false;
            _r2fuNativeSubscriptionDiagnosticsDrawMethod = null;
            _r2fuCustomTypesupportInspectorResolved = false;
            _r2fuCustomTypesupportInspectorDrawMethod = null;
            _currentFoxRunCustomNativeContracts = null;
        }

        private bool HasR2fuNativeRuntimeDemand()
        {
            var loadedScene = GetLoadedSceneContractsForInspectorDraw();
            var customContracts = GetLoadedSceneCustomNativeContractsForInspector(loadedScene);
            return FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                       nativeOutputEnabled: GetBool("_ros2NativeEnabled"),
                       defaultPublishTargets: GetDefaultPublishTargets(),
                       hasExplicitNativePublishContract:
                           loadedScene.HasExplicitNativePublishContract,
                       subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                       defaultSubscriptionSource: GetDefaultSubscriptionSource(),
                       hasExplicitNativeContract:
                           loadedScene.HasExplicitNativeSubscriptionContract)
                   || FoxRunCustomNativeContractDemandPolicy.HasDemand(
                       customContracts,
                       GetDefaultPublishTargets(),
                       GetBool("_enableFoxRunInbound"),
                       GetDefaultSubscriptionSource());
        }

        private bool HasR2fuNativeSubscriptionDemand()
        {
            var loadedScene = GetLoadedSceneContractsForInspectorDraw();
            return HasR2fuNativeSubscriptionDemand(loadedScene);
        }

        private bool HasR2fuNativeSubscriptionDemand(
            FoxRunLoadedSceneContractSnapshot loadedScene)
        {
            var provider = GetDefaultSubscriptionSource();
            return FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                defaultSubscriptionSource: provider,
                hasExplicitNativeContract:
                    loadedScene.HasExplicitNativeSubscriptionContract);
        }

        private void DrawR2fuRuntimeSection()
        {
            var loadedScene = GetLoadedSceneContractsForInspectorDraw();
            var customContracts = GetLoadedSceneCustomNativeContractsForInspector(loadedScene);
            var outputDemand = GetBool("_ros2NativeEnabled")
                               || (GetDefaultPublishTargets() & FoxRunEndpoint.Ros2Native) != 0
                               || loadedScene.HasExplicitNativePublishContract;
            var subscriptionDemand = HasR2fuNativeSubscriptionDemand(loadedScene)
                                     || FoxRunCustomNativeContractDemandPolicy.HasSubscriptionDemand(
                                         customContracts,
                                         subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                                         defaultSubscriptionSource: GetDefaultSubscriptionSource());
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
            if (FoxRunCustomNativeContractDemandPolicy.HasDemand(
                    customContracts,
                    defaultPublishTargets: GetDefaultPublishTargets(),
                    subscriptionsEnabled: GetBool("_enableFoxRunInbound"),
                    defaultSubscriptionSource: GetDefaultSubscriptionSource()))
            {
                DrawOptionalR2fuCustomTypesupportInspector(customContracts);
            }
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

        private void DrawOptionalR2fuCustomTypesupportInspector(
            IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> customContracts)
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
                drawMethod.Invoke(
                    null,
                    new object[]
                    {
                        customContracts
                        ?? System.Array.Empty<FoxRunSchemaCustomNativeContractInfo>()
                    });
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

        private bool HasLoadedSceneExplicitSource(FoxRunEndpoint provider)
        {
            var loadedScene = GetLoadedSceneContractsForInspectorDraw();
            return provider == FoxRunEndpoint.Ros2Native
                ? loadedScene.HasExplicitNativeSubscriptionContract
                : loadedScene.HasExplicitFoxgloveSubscriptionContract;
        }

        private FoxRunLoadedSceneContractSnapshot GetLoadedSceneContractsForInspectorDraw()
            => _loadedSceneContractsThisInspectorDraw
               ??= FoxRunLoadedSceneContractProbe.CaptureLoadedScenes();

        private void ResetLoadedSceneContractsForInspectorDraw()
            => _loadedSceneContractsThisInspectorDraw = null;

        private FoxRunEndpoint GetDefaultSubscriptionSource()
        {
            var sourceProperty = FindCachedProperty("_defaultFoxRunSubscriptionSource");
            return sourceProperty == null
                ? FoxRunEndpoint.Foxglove
                : FoxRunEndpointEditorModel.NormalizeSource(
                    (FoxRunEndpoint)sourceProperty.intValue);
        }

        private FoxRunEndpoint GetDefaultPublishTargets()
            => FoxRunPublishTargetPolicy.FromPublishDestinations(
                foxgloveEnabled: GetBool("_foxgloveOutputEnabled"),
                ros2NativeEnabled: GetBool("_ros2NativeEnabled"),
                ros2BridgeEnabled: GetBool("_ros2BridgeEnabled"));

        private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo>
            GetLoadedSceneCustomNativeContractsForInspector(
                FoxRunLoadedSceneContractSnapshot loadedScene)
        {
            var allContracts = GetCurrentCustomNativeContractsForInspector();
            if (loadedScene == null || allContracts == null || allContracts.Count == 0)
                return System.Array.Empty<FoxRunSchemaCustomNativeContractInfo>();

            var filtered = new List<FoxRunSchemaCustomNativeContractInfo>();
            for (var i = 0; i < allContracts.Count; i++)
            {
                var contract = allContracts[i];
                if (contract != null
                    && loadedScene.ContainsDeclaringType(contract.DeclaringType))
                {
                    filtered.Add(contract);
                }
            }

            return filtered;
        }

        private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> GetCurrentCustomNativeContractsForInspector()
        {
            if (_currentFoxRunCustomNativeContracts != null)
                return _currentFoxRunCustomNativeContracts;

            // Edit Mode readiness must not depend on FoxRunSchemaInfo.g.cs: that
            // evidence is refreshed before Play Mode, while this preflight is how
            // an operator chooses the add-on required to enter Play Mode.
            _currentFoxRunCustomNativeContracts = FoxrunCodeGenerator.CollectCustomNativeContractsForInspector();
            return _currentFoxRunCustomNativeContracts;
        }
    }
}
