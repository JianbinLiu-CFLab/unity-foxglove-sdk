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
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            else
            {
                Debug.Log("[FoxrunBuildPreprocess] No [FoxRun] sources found.");
            }
        }
    }
}
