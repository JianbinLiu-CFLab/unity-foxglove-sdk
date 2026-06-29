using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_36Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-36 Tests ---");
            _passed = 0;

            VerifyEditorSceneLoadAvoidsForcedGc();
            VerifyRenderPipelineAvoidsOpaqueCopy();
            VerifyBuildStrippingForUnusedLightingVariants();
            VerifySmokeScenesAvoidUnusedLightingWork();
            VerifyDeferredMaterialExtractionBoundary();
            VerifyRegistry();

            Console.WriteLine("Phase 164-36: " + _passed + " checks passed.\n");
        }

        private static void VerifyEditorSceneLoadAvoidsForcedGc()
        {
            var editorSettings = Read("Unity2Foxglove/ProjectSettings/EditorSettings.asset");
            Check(editorSettings.Contains("m_ForceAssetUnloadAndGCOnSceneLoad: 0", StringComparison.Ordinal),
                "164-36A-1: Editor scene loads do not force asset unload and full GC");
        }

        private static void VerifyRenderPipelineAvoidsOpaqueCopy()
        {
            var pcAsset = Read("Unity2Foxglove/Assets/Settings/PC_RPAsset.asset");
            Check(pcAsset.Contains("m_RequireOpaqueTexture: 0", StringComparison.Ordinal),
                "164-36B-1: URP asset avoids the per-frame opaque texture copy");
        }

        private static void VerifyBuildStrippingForUnusedLightingVariants()
        {
            var graphics = Read("Unity2Foxglove/ProjectSettings/GraphicsSettings.asset");
            Check(graphics.Contains("m_LightmapStripping: 1", StringComparison.Ordinal),
                "164-36C-1: build strips unused lightmap shader variants");
            Check(graphics.Contains("m_FogStripping: 1", StringComparison.Ordinal),
                "164-36C-2: build strips unused fog shader variants");
        }

        private static void VerifySmokeScenesAvoidUnusedLightingWork()
        {
            foreach (var scenePath in SmokeScenePaths)
            {
                var scene = Read(scenePath);
                Check(scene.Contains("m_AmbientMode: 3", StringComparison.Ordinal),
                    "164-36D-1: " + scenePath + " uses color ambient instead of skybox ambient");
                Check(scene.Contains("m_EnableBakedLightmaps: 0", StringComparison.Ordinal),
                    "164-36D-2: " + scenePath + " disables baked lightmaps without lighting data");
                Check(scene.Contains("m_LightingDataAsset: {fileID: 20201, guid: 0000000000000000f000000000000000, type: 0}", StringComparison.Ordinal),
                    "164-36D-3: " + scenePath + " still has no baked lighting data asset");
            }
        }

        private static void VerifyDeferredMaterialExtractionBoundary()
        {
            var phase138 = Read("Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity");
            Check(phase138.Contains("--- !u!21", StringComparison.Ordinal),
                "164-36E-1: inline material extraction remains deferred for Unity-generated asset GUIDs");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-36\"", StringComparison.Ordinal), "164-36F-1: validation registry exposes Phase164-36");
            Check(project.Contains("Phase164_36Validation.cs", StringComparison.Ordinal), "164-36F-2: runtime validation project compiles Phase164-36");
        }

        private static readonly string[] SmokeScenePaths =
        {
            "Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity",
            "Unity2Foxglove/Assets/Scenes/Phase110Acceptance.unity",
            "Unity2Foxglove/Assets/Scenes/RViz2_Smokes.unity"
        };

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
