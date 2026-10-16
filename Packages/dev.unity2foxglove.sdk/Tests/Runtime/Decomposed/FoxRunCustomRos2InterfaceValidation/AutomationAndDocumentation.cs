// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Phase181 custom ROS2 DTO interface boundary.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunCustomRos2InterfaceValidation
    {
        private static void VerifyInteropAutomationReleaseGate(
            string peerProtocol,
            string peerHelper,
            string linuxPeer,
            string matrixProfiles,
            string runCi,
            string dotnetWorkflow,
            string packageWorkflow)
        {
            Check(peerProtocol.Contains("SUMMARY_SCHEMA_VERSION", StringComparison.Ordinal)
                  && peerProtocol.Contains("write_summary_atomic", StringComparison.Ordinal)
                  && peerProtocol.Contains("FAIL_INTERFACE_DIGEST", StringComparison.Ordinal)
                  && !peerProtocol.Contains("shell=True", StringComparison.Ordinal)
                  && !peerProtocol.Contains("os.system", StringComparison.Ordinal),
                "181F-5: common peer protocol persists redacted correlated evidence without shell execution");

            Check(peerHelper.Contains("build_player_environment", StringComparison.Ordinal)
                  && peerHelper.Contains("require_player_exit_code", StringComparison.Ordinal)
                  && peerHelper.Contains("--probe-role", StringComparison.Ordinal)
                  && peerHelper.Contains("--surface", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_GRAPH_EVIDENCE", StringComparison.Ordinal)
                  && peerHelper.Contains("create_typed_worker_endpoints", StringComparison.Ordinal)
                  && peerHelper.Contains("observe_no_late_unity_apply", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_LATE_APPLY", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_PEER_RUNTIME", StringComparison.Ordinal)
                  && !peerHelper.Contains("shell=True", StringComparison.Ordinal)
                  && !peerHelper.Contains("os.system", StringComparison.Ordinal),
                "181F-6: Windows Editor and Player paths reuse the strict generated-envelope protocol and bounded endpoint teardown");

            Check(linuxPeer.Contains("stage_or_verify_locked_ros_source", StringComparison.Ordinal)
                  && linuxPeer.Contains("build_linux_worker_environment", StringComparison.Ordinal)
                  && linuxPeer.Contains("--role", StringComparison.Ordinal)
                  && linuxPeer.Contains("--surface", StringComparison.Ordinal)
                  && !linuxPeer.Contains("shell=True", StringComparison.Ordinal)
                  && !linuxPeer.Contains("os.system", StringComparison.Ordinal),
                "181F-7: Linux peer keeps source/workspace ownership explicit and imports only its built generated package");

            Check(matrixProfiles.Contains("DEFAULT_READY_TIMEOUT_SECONDS = 300", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_HUMBLE_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("write_profile_failure_summary", StringComparison.Ordinal)
                  && !matrixProfiles.Contains("shell=True", StringComparison.Ordinal),
                "181F-8: four named Windows-local wrappers preserve a bounded String-first and owned-Zenoh path");

            Check(runCi.Contains("phase181-ros2-regression", StringComparison.Ordinal)
                  && runCi.Contains("PHASE181_ROS2_PEER_REGRESSION", StringComparison.Ordinal)
                  && runCi.Contains("validate_foxrun_custom_typesupport_addon.py", StringComparison.Ordinal)
                  && dotnetWorkflow.Contains("Run Phase181 custom ROS2 acceptance helper regressions", StringComparison.Ordinal)
                  && packageWorkflow.Contains("Validate Phase181 custom ROS2 typesupport add-ons", StringComparison.Ordinal),
                "181F-10: public CI runs protocol regressions and all tracked custom typesupport preflight validators");
        }

        private static string ReadSplitPythonSource(string relativePath)
            => PhaseValidationSourceHelpers.ReadSplitPythonSource(relativePath);

        private static void VerifyRuntimeBatchSelection(string runtimeBatchSelection)
        {
            Check(runtimeBatchSelection.Contains("public static void SelectFromCommandLine()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2Distro", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2CommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityCustomTypesupportSelectionTransaction.Apply", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeSelection.SetCommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Client.Resolve()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Events.registeredPackages", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("PackageInfo.FindForPackageName", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("[InitializeOnLoadMethod]", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("SessionState.SetString", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("EditorApplication.Exit(0);", StringComparison.Ordinal),
                "181F-21: an explicit Unity Batch selector applies one validated runtime/add-on and communication-mode transaction before each isolated acceptance row");
        }

        private static void VerifyPublicOperationalDocumentation(
            string acceptanceSampleReadme,
            string ros2SmokeReadme,
            string r2fuPackageJson)
        {
            Check(acceptanceSampleReadme.Contains("exactly one matching", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("static interface lock", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("PublishAndSubscribe", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("FoxRun Custom ROS2 Interface", StringComparison.Ordinal),
                "181F-11: package sample exposes the locked custom DTO workflow without treating typesupport as an implicit runtime fallback");

            Check(ros2SmokeReadme.Contains("Phase181 Windows-local Editor bring-up", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("exactly one matching add-on", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("echo-on-apply", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("same-origin", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("FixedRate", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("not individually recorded to MCAP", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("Linux", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("Player", StringComparison.Ordinal),
                "181F-12: public custom-interface instructions distinguish local bring-up, certification, origin policy, and MCAP semantics");
        }

        private static string ReadRequiredJsonString(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException(
                    "[FAIL] Phase181 static interface lock lacks a non-empty string property: " + propertyName);
            }

            return property.GetString();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}

