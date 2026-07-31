// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137E FoxgloveManagerEditor partial-class split guard.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase137EValidation.
    /// </summary>
    public static class Phase137EValidation
    {
        private static readonly string Dir =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager";

        private static readonly string[] PartialFiles =
        {
            "FoxgloveManagerEditor.cs",
            "FoxgloveManagerEditor.DataTransport.cs",
            "FoxgloveManagerEditor.PublishData.cs",
            "FoxgloveManagerEditor.SubscribeData.cs",
            "FoxgloveManagerEditor.Mcap.cs",
            "FoxgloveManagerEditor.Diagnostics.cs",
            "FoxgloveManagerEditor.Security.cs",
            "FoxgloveManagerEditor.Helpers.cs",
            "FoxgloveManagerEditor.FoxServices.cs",
        };

        private static readonly string[] ProviderDrawerFiles =
        {
            "Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2BridgeProviderDrawer.cs",
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs",
        };

        private static readonly string[] FoldoutStatics =
        {
            "_connectionSecurityExpanded",
            "_dataTransportExpanded",
            "_dataTransportPublishExpanded",
            "_dataTransportSubscribeExpanded",
            "_mcapExpanded",
            "_foxServicesExpanded",
            "_schemaEvidenceAdvancedExpanded",
            "_remoteFileAccessExpanded",
            "_diagnosticsExpanded",
        };

        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137E Tests ---");
            _passed = 0;

            VerifyFilesExist();
            VerifyPartialDeclarations();
            VerifyPartialUsingDependencies();
            VerifyCustomEditorOnlyOnMain();
            VerifyFoldoutStaticCounts();
            VerifySectionMethodCounts();
            VerifyAssetRootDefinitionDrawerPlacement();
            VerifyProviderSpecificEditorsAreExtracted();

            Console.WriteLine("Phase 137E: " + _passed + " checks passed.\n");
        }

        private static void VerifyFilesExist()
        {
            foreach (var f in PartialFiles)
                Check(File.Exists(Path.Combine(Dir, f)), "137E-1: file exists: " + f);
        }

        private static void VerifyPartialDeclarations()
        {
            foreach (var f in PartialFiles)
            {
                var content = File.ReadAllText(Path.Combine(Dir, f));
                Check(content.Contains("public partial class FoxgloveManagerEditor", StringComparison.Ordinal),
                    "137E-2: partial class declaration in " + f);
                Check(content.Contains("namespace Unity.FoxgloveSDK.Editor", StringComparison.Ordinal),
                    "137E-3: namespace in " + f);
            }
        }

        private static void VerifyPartialUsingDependencies()
        {
            foreach (var f in PartialFiles)
            {
                var content = File.ReadAllText(Path.Combine(Dir, f));
                if (!content.Contains("FoxgloveTransportMode", StringComparison.Ordinal))
                    continue;

                Check(content.Contains("using Unity.FoxgloveSDK.Transport;", StringComparison.Ordinal),
                    "137E-3b: " + f + " imports transport namespace for FoxgloveTransportMode");
            }
        }

        private static void VerifyCustomEditorOnlyOnMain()
        {
            var main = File.ReadAllText(Path.Combine(Dir, "FoxgloveManagerEditor.cs"));
            Check(main.Contains("[CustomEditor(typeof(Components.FoxgloveManager))]", StringComparison.Ordinal),
                "137E-4: CustomEditor only on main file");

            foreach (var f in PartialFiles)
            {
                if (f == "FoxgloveManagerEditor.cs") continue;
                Check(!File.ReadAllText(Path.Combine(Dir, f)).Contains("[CustomEditor", StringComparison.Ordinal),
                    "137E-5: no CustomEditor on " + f);
            }
        }

        private static void VerifyFoldoutStaticCounts()
        {
            foreach (var field in FoldoutStatics)
            {
                var count = 0;
                foreach (var f in PartialFiles)
                    if (File.ReadAllText(Path.Combine(Dir, f)).Contains("private bool " + field, StringComparison.Ordinal))
                        count++;
                Check(count == 1, "137E-6: " + field + " declared exactly once (found " + count + ")");
            }
        }

        private static void VerifySectionMethodCounts()
        {
            var methods = new[] {
                "DrawConnectionSecuritySection",
                "DrawDataTransportSection",
                "DrawPublishDataSection",
                "DrawSubscribeDataSection",
                "DrawMcapSection",
                "DrawDiagnosticsSection",
                "DrawFoxServicesSection",
                "DrawRemoteFileAccessSection",
                "DrawSchemaEvidenceSection",
                "DrawSecureWebSocketSection",
            };
            foreach (var m in methods)
            {
                var count = 0;
                foreach (var f in PartialFiles)
                {
                    var content = File.ReadAllText(Path.Combine(Dir, f));
                    var idx = 0;
                    while ((idx = content.IndexOf("void " + m + "(", idx, StringComparison.Ordinal)) >= 0)
                    {
                        count++;
                        idx++;
                    }
                }
                Check(count == 1, "137E-7: " + m + " declared exactly once (found " + count + ")");
            }
        }

        private static void VerifyAssetRootDefinitionDrawerPlacement()
        {
            var main = File.ReadAllText(Path.Combine(Dir, "FoxgloveManagerEditor.cs"));
            Check(main.Contains("class AssetRootDefinitionDrawer", StringComparison.Ordinal),
                "137E-8: AssetRootDefinitionDrawer in main file");
            foreach (var f in PartialFiles)
            {
                if (f == "FoxgloveManagerEditor.cs") continue;
                Check(!File.ReadAllText(Path.Combine(Dir, f)).Contains("AssetRootDefinitionDrawer", StringComparison.Ordinal),
                    "137E-9: AssetRootDefinitionDrawer NOT in " + f);
            }
        }

        private static void VerifyProviderSpecificEditorsAreExtracted()
        {
            foreach (var file in ProviderDrawerFiles)
            {
                Check(File.Exists(file), "137E-10: Provider drawer exists: " + Path.GetFileName(file));
                var content = File.ReadAllText(file);
                Check(content.Contains("IFoxRunTransportProviderDrawer", StringComparison.Ordinal)
                      && content.Contains("FoxRunTransportProviderDrawerRegistry.Register", StringComparison.Ordinal),
                    "137E-11: Provider drawer registers through the generic editor seam: " + Path.GetFileName(file));
            }

            var managerEditors = string.Empty;
            foreach (var file in PartialFiles)
                managerEditors += File.ReadAllText(Path.Combine(Dir, file));

            Check(!managerEditors.Contains("Ros2BridgeTransportProvider", StringComparison.Ordinal)
                  && !managerEditors.Contains("FoxRunRos2TransportProvider", StringComparison.Ordinal)
                  && !managerEditors.Contains("DrawRos2BridgeSection", StringComparison.Ordinal)
                  && !managerEditors.Contains("DrawR2fuRuntimeSection", StringComparison.Ordinal),
                "137E-12: core Manager editor partials remain Provider-neutral");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
            {
                Console.WriteLine("[FAIL] " + label);
                throw new InvalidOperationException("Phase 137E validation failed: " + label);
            }

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
