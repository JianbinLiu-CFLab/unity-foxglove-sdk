// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards the FoxRun native subscription policy and dependency boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunRos2NativeSubscriptionValidation
    {
        /// <remarks>Phase192 source sections: WireAndDependency.cs, LifecycleAndProvider.cs, DiagnosticsAndCompilation.cs, RuntimeDiagnostics.cs, InteropAcceptance.cs, ProjectWiring.cs</remarks>
        private const string CorePackageRoot = "Packages/dev.unity2foxglove.sdk";
        private const string OptionalRuntimeAsmdefGuid = "f8feed905315b394d8d0f92bf2441283";
        private const string OptionalRuntimeAsmdefPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef";
        private const string ValidationSource =
            CorePackageRoot + "/Tests/Runtime/FoxRunRos2NativeSubscriptionValidation.cs";
        private const string ValidationMeta = ValidationSource + ".meta";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Native ROS2 Subscription Generation Boundary ---");
            _passed = 0;

            VerifyWireEncodingAndProviderAxes();
            VerifyDependencyInspectionCoverage();
            VerifyCoreDependencyBoundary();
            VerifyIndependentManagerSubscriptionSession();
            VerifyLegacyProviderMigrationDefaultsToWebSocket();
            VerifyExistingR2fuSinkRemainsOutboundOnly();
            VerifyTypedGenerationAndNativeCatalogExclusion();
            VerifyDiagnosticIdSemanticRanges();
            VerifyDiagnosticIdLegacyLedger();
            VerifyOptionalCompilationLanes();
            VerifyNativeHostLifecycleBoundary();
            VerifyRuntimeDiagnosticsSurface();
            VerifyManualOwnershipProbe();
            VerifyInteropAcceptanceSurface();
            VerifyPermanentOptionalCompilationCiGates();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine(
                "FoxRun native ROS2 subscription generation boundary: " + _passed + " checks passed.\n");
        }

    }
}
