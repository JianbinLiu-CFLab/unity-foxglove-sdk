// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Writes FoxRun canonical manifest artifacts for build-time evidence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxrunManifestWriter
    {
        public const string ManifestJsonFileName = "foxrun.manifest.json";
        public const string ManifestHashFileName = "foxrun.manifest.hash";
        public const string ManifestReportFileName = "foxrun.manifest.report.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static FoxRunCanonicalManifest WriteManifestFiles(
            string outputDirectory,
            IReadOnlyList<FoxRunManifestMember> members,
            string generatedAtUtc = null,
            IReadOnlyList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            var manifest = FoxRunManifestBuilder.Build(members ?? Array.Empty<FoxRunManifestMember>());
            var canonical = FoxRunManifestJsonWriter.WriteCanonical(manifest);
            var report = FoxRunManifestJsonWriter.WriteReport(
                manifest,
                generatedAtUtc ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                warnings ?? Array.Empty<string>());

            Directory.CreateDirectory(outputDirectory);
            WriteIfChanged(Path.Combine(outputDirectory, ManifestJsonFileName), canonical);
            WriteIfChanged(Path.Combine(outputDirectory, ManifestHashFileName), manifest.GlobalManifestHash + "\n");
            WriteIfChanged(Path.Combine(outputDirectory, ManifestReportFileName), report);
            return manifest;
        }

        private static void WriteIfChanged(string path, string content)
        {
            var bytes = Utf8NoBom.GetBytes(content);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return;
            File.WriteAllBytes(path, bytes);
        }
    }
}
