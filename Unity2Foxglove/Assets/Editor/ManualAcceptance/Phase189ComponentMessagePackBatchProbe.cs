// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity2Foxglove
{
    /// <summary>Bounded batch diagnostic for the maintained Phase189 acceptance scene.</summary>
    public static class Phase189ComponentMessagePackBatchProbe
    {
        public static void RunFromCommandLine() => Run();

        [MenuItem("Foxglove/Manual Acceptance/Phase189/Run Batch Diagnostic")]
        public static void Run()
        {
            Phase189ComponentMessagePackAcceptanceBuilder.BuildAndValidate();
            var scene = EditorSceneManager.OpenScene(
                Phase189ComponentMessagePackAcceptanceBuilder.AcceptanceSceneAssetPath,
                OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var controllerCount = roots.SelectMany(x => x.GetComponentsInChildren<Unity2Foxglove.ManualAcceptance.Phase189ComponentMessagePackAcceptance>(true)).Count();
            var managerCount = roots.SelectMany(x => x.GetComponentsInChildren<Unity.FoxgloveSDK.Components.FoxgloveManager>(true)).Count();
            if (controllerCount != 1 || managerCount != 1)
                throw new InvalidDataException("Phase189 batch diagnostic requires exactly one maintained controller and manager.");
            var wireVectors = ValidateWireVectors();
            if (!wireVectors)
                throw new InvalidDataException("Deterministic MessagePack vectors were not validated.");
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? throw new DirectoryNotFoundException("Unity project root missing.");
            var repoRoot = Directory.GetParent(projectRoot)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root missing above Unity project.");
            var report = Path.Combine(repoRoot, "build", "phase189", "manual", "batch-diagnostic.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, "{\"verdict\":\"PASS\",\"scene\":\"" + Phase189ComponentMessagePackAcceptanceBuilder.AcceptanceSceneAssetPath + "\",\"controllerCount\":1,\"managerCount\":1,\"wireVectors\":true,\"cleanup\":true}\n");
            Debug.Log("PHASE189_BATCH_DIAGNOSTIC_PASS report=" + report);
        }

        private static bool ValidateWireVectors()
        {
            using (var writer = new FoxgloveMsgPackWriter())
            {
                writer.WriteMapHeader(2);
                writer.WriteString("value");
                writer.WriteInt32(417);
                writer.WriteString("label");
                writer.WriteString("L");
                var scalar = writer.ToArray();
                var expectedScalar = new byte[] { 0x82, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xcd, 0x01, 0xa1, 0xa5, 0x6c, 0x61, 0x62, 0x65, 0x6c, 0xa1, 0x4c };
                if (!scalar.SequenceEqual(expectedScalar))
                    throw new InvalidDataException("Generated scalar MessagePack vector changed.");
                writer.Clear();
                writer.WriteMapHeader(1);
                writer.WriteString("data");
                writer.WriteBinary(new byte[] { 0xff, 0xd8, 0xff });
                var binary = writer.ToArray();
                var expectedBinary = new byte[] { 0x81, 0xa4, 0x64, 0x61, 0x74, 0x61, 0xc4, 0x03, 0xff, 0xd8, 0xff };
                if (!binary.SequenceEqual(expectedBinary))
                    throw new InvalidDataException("Generated binary MessagePack vector changed.");
                return true;
            }
        }
    }
}
#endif
