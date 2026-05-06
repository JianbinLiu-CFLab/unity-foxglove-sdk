// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor
// Purpose: IPreprocessBuildWithReport hook — generates physical .g.cs
// fallback files for [FoxRun] annotated classes before IL2CPP Player build.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Before Player build, generates real .g.cs files for [FoxRun] annotated classes
    /// so IL2CPP has the IFoxgloveLogSource implementation without relying on Roslyn analyzer.
    /// </summary>
    public class FoxrunBuildPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[FoxrunBuildPreprocess] Generating FoxRun source files...");
            var files = FoxrunCodeGenerator.GenerateSourceFiles();

            if (files.Count > 0)
            {
                var names = string.Join(", ", files);
                Debug.Log($"[FoxrunBuildPreprocess] Generated {files.Count} file(s): {names}");

                // Generate FoxRun_link.xml to preserve user types in IL2CPP builds.
                // Without this, the linker may strip IFoxgloveLogSource implementations
                // even though the generated .g.cs has [Preserve].
                var types = FoxrunCodeGenerator.CollectFoxRunTypes();
                if (types.Count > 0)
                {
                    var linkXml = FoxrunCodeGenerator.EmitLinkXml(types);
                    var linkPath = Path.Combine(Application.dataPath, "FoxRun_link.xml");
                    File.WriteAllText(linkPath, linkXml);
                    Debug.Log($"[FoxrunBuildPreprocess] Wrote FoxRun_link.xml with {types.Count} type(s)");
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            else
            {
                Debug.Log("[FoxrunBuildPreprocess] No [FoxRun] sources found.");
            }
        }
    }
}
