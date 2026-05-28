// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137E FoxgloveManagerEditor partial-class split guard.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137EValidation
    {
        private static readonly string Dir =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager";

        private static readonly string[] PartialFiles =
        {
            "FoxgloveManagerEditor.cs",
            "FoxgloveManagerEditor.PublishData.cs",
            "FoxgloveManagerEditor.Mcap.cs",
            "FoxgloveManagerEditor.Ros2Bridge.cs",
            "FoxgloveManagerEditor.Diagnostics.cs",
        };

        private static readonly string[] FoldoutStatics =
        {
            "_connectionSecurityExpanded",
            "_publishDataExpanded",
            "_ros2BridgeExpanded",
            "_mcapExpanded",
            "_schemaEvidenceAdvancedExpanded",
            "_diagnosticsExpanded",
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137E Tests ---");
            _passed = 0;

            VerifyFilesExist();
            VerifyPartialDeclarations();
            VerifyCustomEditorOnlyOnMain();
            VerifyFoldoutStaticCounts();
            VerifySectionMethodCounts();
            VerifyAssetRootDefinitionDrawerPlacement();

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
                Check(content.Contains("public partial class FoxgloveManagerEditor"),
                    "137E-2: partial class declaration in " + f);
                Check(content.Contains("namespace Unity.FoxgloveSDK.Editor"),
                    "137E-3: namespace in " + f);
            }
        }

        private static void VerifyCustomEditorOnlyOnMain()
        {
            var main = File.ReadAllText(Path.Combine(Dir, "FoxgloveManagerEditor.cs"));
            Check(main.Contains("[CustomEditor(typeof(Components.FoxgloveManager))]"),
                "137E-4: CustomEditor only on main file");

            foreach (var f in PartialFiles)
            {
                if (f == "FoxgloveManagerEditor.cs") continue;
                Check(!File.ReadAllText(Path.Combine(Dir, f)).Contains("[CustomEditor"),
                    "137E-5: no CustomEditor on " + f);
            }
        }

        private static void VerifyFoldoutStaticCounts()
        {
            foreach (var field in FoldoutStatics)
            {
                var count = 0;
                foreach (var f in PartialFiles)
                    if (File.ReadAllText(Path.Combine(Dir, f)).Contains("private static bool " + field + ";"))
                        count++;
                Check(count == 1, "137E-6: " + field + " declared exactly once (found " + count + ")");
            }
        }

        private static void VerifySectionMethodCounts()
        {
            var methods = new[] {
                "DrawPublishDataSection",
                "DrawMcapSection",
                "DrawDiagnosticsSection",
                "DrawRos2BridgeSection",
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
            Check(main.Contains("class AssetRootDefinitionDrawer"),
                "137E-8: AssetRootDefinitionDrawer in main file");
            foreach (var f in PartialFiles)
            {
                if (f == "FoxgloveManagerEditor.cs") continue;
                Check(!File.ReadAllText(Path.Combine(Dir, f)).Contains("AssetRootDefinitionDrawer"),
                    "137E-9: AssetRootDefinitionDrawer NOT in " + f);
            }
        }

        private static void Check(bool condition, string label)
        {
            if (condition) { Console.WriteLine("[PASS] " + label); _passed++; }
            else Console.WriteLine("[FAIL] " + label);
        }
    }
}
