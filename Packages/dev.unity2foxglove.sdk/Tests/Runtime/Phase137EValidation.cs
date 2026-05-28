// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137E FoxgloveManagerEditor partial-class guard.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137EValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137E Tests ---");
            _passed = 0;

            var mainFile = "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";
            Check(File.Exists(mainFile), "137E-1: FoxgloveManagerEditor.cs exists");

            var content = File.ReadAllText(mainFile);
            Check(content.Contains("public partial class FoxgloveManagerEditor"),
                "137E-2: class declared as partial");
            Check(content.Contains("[CustomEditor(typeof(Components.FoxgloveManager))]"),
                "137E-3: CustomEditor attribute preserved");
            Check(content.Contains("class AssetRootDefinitionDrawer"),
                "137E-4: AssetRootDefinitionDrawer preserved");

            Console.WriteLine("Phase 137E: " + _passed + " checks passed.\n");
        }

        private static void Check(bool condition, string label)
        {
            if (condition) { Console.WriteLine("[PASS] " + label); _passed++; }
            else Console.WriteLine("[FAIL] " + label);
        }
    }
}
