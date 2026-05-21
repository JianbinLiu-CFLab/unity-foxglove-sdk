// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SchemaManifest
// Purpose: Editor entry point for SDK schema manifest aggregate generation.

using System;
using System.IO;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Generates deterministic SDK schema manifest aggregate artifacts under
    /// <c>Assets/Generated/Unity2Foxglove</c>.
    /// </summary>
    public static class Unity2FoxgloveSchemaManifestGenerator
    {
        private const string OutputSubdirectory = "Generated/Unity2Foxglove";

        /// <summary>
        /// Refreshes FoxRun manifest/schema-info artifacts first, then writes
        /// the SDK aggregate manifest. This method is safe for Editor batch
        /// use and does not write physical <c>_FoxRun.g.cs</c> fallback files.
        /// </summary>
        public static Unity2FoxgloveSchemaManifest GenerateArtifacts()
        {
            var foxRunManifest = FoxrunCodeGenerator.GenerateManifestAndSchemaInfoFilesOnly();
            return GenerateArtifacts(foxRunManifest);
        }

        /// <summary>
        /// Writes the SDK aggregate manifest from an already refreshed FoxRun
        /// manifest object. Play Mode uses this overload to preserve the
        /// order: FoxRun manifest, FoxRun schema info, then SDK aggregate.
        /// </summary>
        public static Unity2FoxgloveSchemaManifest GenerateArtifacts(FoxRunCanonicalManifest foxRunManifest)
        {
            if (foxRunManifest == null)
                throw new ArgumentNullException(nameof(foxRunManifest));

            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(foxRunManifest);
            return Unity2FoxgloveSchemaManifestWriter.WriteManifestFiles(
                GetOutputDirectory(),
                aggregate);
        }

        private static string GetOutputDirectory()
        {
            return Path.Combine(Application.dataPath, OutputSubdirectory);
        }
    }
}
