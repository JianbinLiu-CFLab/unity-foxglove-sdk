// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Core
{
    internal static class ReplayFileValidator
    {
        internal static void ValidateReplayFileForLoad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidDataException("Replay MCAP file path is empty.");

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Replay MCAP file does not exist: {fullPath}", fullPath);

            var info = new FileInfo(fullPath);
            const int minFileBytes =
                McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            if (info.Length < minFileBytes)
                throw new InvalidDataException(
                    $"Replay MCAP file is too small to be finalized: {fullPath} ({info.Length} bytes).");

            var expectedMagic = McapWriter.Magic;
            var actualMagic = new byte[McapWriter.MagicLength];
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReadExactReplayMagic(stream, actualMagic);
            if (!MatchesReplayMagic(actualMagic, expectedMagic))
                throw new InvalidDataException($"Replay MCAP file does not start with MCAP magic: {fullPath}.");

            stream.Seek(-McapWriter.MagicLength, SeekOrigin.End);
            ReadExactReplayMagic(stream, actualMagic);
            if (!MatchesReplayMagic(actualMagic, expectedMagic))
                throw new InvalidDataException(
                    $"Replay MCAP file is not finalized or is truncated (missing trailing magic): {fullPath} ({info.Length} bytes). Stop recording cleanly and select a finalized .mcap file.");
        }

        private static void ReadExactReplayMagic(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("Replay MCAP file ended while reading magic bytes.");
                offset += read;
            }
        }

        private static bool MatchesReplayMagic(byte[] actual, byte[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
                return false;
            for (var i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i])
                    return false;
            return true;
        }
    }
}
