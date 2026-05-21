// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/SchemaManifest
// Purpose: Writes SDK schema manifest aggregate artifacts.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class Unity2FoxgloveSchemaManifestWriter
    {
        public const string ManifestJsonFileName = "unity2foxglove.schema-manifest.json";
        public const string ManifestHashFileName = "unity2foxglove.schema-manifest.hash";
        public const string ManifestReportFileName = "unity2foxglove.schema-manifest.report.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static Unity2FoxgloveSchemaManifest WriteManifestFiles(
            string outputDirectory,
            Unity2FoxgloveSchemaManifest manifest,
            string generatedAtUtc = null,
            IReadOnlyList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            var canonical = Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(manifest);
            var report = Unity2FoxgloveSchemaManifestJsonWriter.WriteReport(
                manifest,
                generatedAtUtc ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                warnings ?? Array.Empty<string>());

            Directory.CreateDirectory(outputDirectory);
            WriteIfChanged(Path.Combine(outputDirectory, ManifestJsonFileName), canonical);
            WriteIfChanged(Path.Combine(outputDirectory, ManifestHashFileName), manifest.SdkSchemaManifestHash + "\n");
            WriteIfChanged(Path.Combine(outputDirectory, ManifestReportFileName), report);
            return manifest;
        }

        private static void WriteIfChanged(string path, string content)
        {
            var bytes = Utf8NoBom.GetBytes(content ?? string.Empty);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return;
            File.WriteAllBytes(path, bytes);
        }
    }
}
