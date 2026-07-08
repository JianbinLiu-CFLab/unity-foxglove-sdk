// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    [Trait("Phase", "174-001")]
    [Trait("Domain", "Replay")]
    public sealed class ReplayFileValidatorTests
    {
        [Fact]
        public void ValidateReplayFileForLoadRejectsEmptyMissingAndUnfinalizedFiles()
        {
            var empty = Assert.Throws<InvalidDataException>(() => ReplayFileValidator.ValidateReplayFileForLoad(" "));
            Assert.Contains("empty", empty.Message);

            var missingPath = Path.Combine(Path.GetTempPath(), "u2f_missing_" + Guid.NewGuid().ToString("N") + ".mcap");
            var missing = Assert.Throws<FileNotFoundException>(() => ReplayFileValidator.ValidateReplayFileForLoad(missingPath));
            Assert.Equal(Path.GetFullPath(missingPath), missing.FileName);

            var truncatedPath = WriteReplayFile(new byte[] { 1, 2, 3 });
            try
            {
                var truncated = Assert.Throws<InvalidDataException>(() => ReplayFileValidator.ValidateReplayFileForLoad(truncatedPath));
                Assert.Contains("too small", truncated.Message);
            }
            finally
            {
                TryDelete(truncatedPath);
            }
        }

        [Fact]
        public void ValidateReplayFileForLoadAcceptsFinalizedMcapEnvelope()
        {
            var minBytes = McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            var bytes = new byte[minBytes];
            Buffer.BlockCopy(McapWriter.Magic, 0, bytes, 0, McapWriter.MagicLength);
            Buffer.BlockCopy(McapWriter.Magic, 0, bytes, minBytes - McapWriter.MagicLength, McapWriter.MagicLength);
            var path = WriteReplayFile(bytes);

            try
            {
                ReplayFileValidator.ValidateReplayFileForLoad(path);
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static string WriteReplayFile(byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), "u2f_replay_validator_" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
