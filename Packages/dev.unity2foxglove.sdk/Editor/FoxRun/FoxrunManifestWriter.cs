// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Writes FoxRun canonical manifest artifacts for build-time evidence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxrunManifestWriter
    {
        public const string ManifestJsonFileName = "foxrun.manifest.json";
        public const string ManifestHashFileName = "foxrun.manifest.hash";
        public const string ManifestReportFileName = "foxrun.manifest.report.json";
        private const int ReplaceAttempts = 3;
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
            Directory.CreateDirectory(outputDirectory);
            var manifestChanged = WriteIfChanged(Path.Combine(outputDirectory, ManifestJsonFileName), canonical);
            var hashChanged = WriteIfChanged(Path.Combine(outputDirectory, ManifestHashFileName), manifest.GlobalManifestHash + "\n");
            var reportPath = Path.Combine(outputDirectory, ManifestReportFileName);
            if (manifestChanged || hashChanged || !File.Exists(reportPath))
            {
                var report = FoxRunManifestJsonWriter.WriteReport(
                    manifest,
                    generatedAtUtc ?? DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    warnings ?? Array.Empty<string>());
                WriteIfChanged(reportPath, report);
            }
            return manifest;
        }

        private static bool WriteIfChanged(string path, string content)
        {
            var bytes = Utf8NoBom.GetBytes(content ?? string.Empty);
            var existing = new FileInfo(path);
            if (existing.Exists && existing.Length == bytes.Length && FileContentEquals(path, bytes))
                return false;

            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(tempPath, bytes);
                ReplaceFile(tempPath, path);
                return true;
            }
            finally
            {
                TryDeleteTempFile(tempPath);
            }
        }

        private static bool FileContentEquals(string path, byte[] bytes)
        {
            var buffer = new byte[8192];
            using (var stream = File.OpenRead(path))
            {
                for (var offset = 0; offset < bytes.Length;)
                {
                    var expected = Math.Min(buffer.Length, bytes.Length - offset);
                    var read = stream.Read(buffer, 0, expected);
                    if (read == 0)
                        return false;
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] != bytes[offset + i])
                            return false;
                    }
                    offset += read;
                }

                return stream.ReadByte() == -1;
            }
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
                "Failed to replace generated FoxRun manifest artifact '" + path + "'.",
                copyException ?? originalException);
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
                Thread.Yield();
        }

        private static void TryDeleteTempFile(string tempPath)
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
}
