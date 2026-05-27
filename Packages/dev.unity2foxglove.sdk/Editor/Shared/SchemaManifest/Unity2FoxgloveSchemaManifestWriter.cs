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
using System.Threading;

namespace Unity.FoxgloveSDK.Editor
{
    public static class Unity2FoxgloveSchemaManifestWriter
    {
        public const string ManifestJsonFileName = "unity2foxglove.schema-manifest.json";
        public const string ManifestHashFileName = "unity2foxglove.schema-manifest.hash";
        public const string ManifestReportFileName = "unity2foxglove.schema-manifest.report.json";
        private const int ReplaceAttempts = 3;
        private const int ReplaceRetryDelayMilliseconds = 50;
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
            WriteIfChanged(Path.Combine(outputDirectory, ManifestReportFileName), report);
            WriteIfChanged(Path.Combine(outputDirectory, ManifestHashFileName), manifest.SdkSchemaManifestHash + "\n");
            return manifest;
        }

        private static void WriteIfChanged(string path, string content)
        {
            var bytes = Utf8NoBom.GetBytes(content ?? string.Empty);
            if (TryReadExistingBytes(path, out var existingBytes) && existingBytes.SequenceEqual(bytes))
                return;

            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(tempPath, bytes);
                ReplaceFile(tempPath, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static bool TryReadExistingBytes(string path, out byte[] bytes)
        {
            bytes = null;
            if (!File.Exists(path))
                return false;

            for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
            {
                try
                {
                    bytes = File.ReadAllBytes(path);
                    return true;
                }
                catch (IOException)
                {
                    DelayBeforeRetry(attempt);
                }
                catch (UnauthorizedAccessException)
                {
                    DelayBeforeRetry(attempt);
                }
            }

            return false;
        }

        private static void ReplaceFile(string tempPath, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            Exception replaceException = null;
            for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
            {
                try
                {
                    ClearReadOnly(path);
                    File.Replace(tempPath, path, null);
                    return;
                }
                catch (PlatformNotSupportedException ex)
                {
                    replaceException = ex;
                    break;
                }
                catch (IOException ex)
                {
                    replaceException = ex;
                    DelayBeforeRetry(attempt);
                }
                catch (UnauthorizedAccessException ex)
                {
                    replaceException = ex;
                    DelayBeforeRetry(attempt);
                }
            }

            CopyTempOverDestination(tempPath, path, replaceException);
        }

        private static void CopyTempOverDestination(string tempPath, string path, Exception originalException)
        {
            Exception copyException = null;
            for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
            {
                try
                {
                    ClearReadOnly(path);
                    File.Copy(tempPath, path, overwrite: true);
                    return;
                }
                catch (IOException ex)
                {
                    copyException = ex;
                    DelayBeforeRetry(attempt);
                }
                catch (UnauthorizedAccessException ex)
                {
                    copyException = ex;
                    DelayBeforeRetry(attempt);
                }
            }

            throw new IOException(
                "Failed to replace generated SDK schema manifest artifact '" + path + "'.",
                copyException == null || originalException == null
                    ? copyException ?? originalException
                    : new AggregateException(originalException, copyException));
        }

        private static void ClearReadOnly(string path)
        {
            if (!File.Exists(path))
                return;

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        private static void DelayBeforeRetry(int attempt)
        {
            if (attempt + 1 < ReplaceAttempts)
                Thread.Sleep(ReplaceRetryDelayMilliseconds);
        }
    }
}
