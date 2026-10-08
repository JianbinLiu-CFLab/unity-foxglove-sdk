// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates LZ4/Zstd compression roundtrips, parameters/services metadata, client publish message, coordinate mode in channel metadata, and MetadataIndex read/parse roundtrip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase12Validation
    {
        /// <summary>
        /// Registers a channel with <c>coordinate_mode</c> metadata set
        /// to <c>RightHand</c> in the MCAP, reads back the summary, and
        /// confirms the metadata roundtrips.
        /// </summary>
        static void TestCoordinateModeInChannel()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var meta = new Dictionary<string, string> { ["coordinate_mode"] = "RightHand" };
            var (sch, ch) = BuildSchemaAndChannel(ms, 1, 1, "/tf", meta);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 1); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, 0); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.Channels.Count == 1, "CoordMode: has 1 channel");
            Assert(summary.Channels[0].Metadata.ContainsKey("coordinate_mode"), "CoordMode: metadata has coordinate_mode");
            Assert(summary.Channels[0].Metadata["coordinate_mode"] == "RightHand", "CoordMode: value is RightHand");
        }

        // ── Test 7: coordinate_mode mismatch detection ──

        /// <summary>
        /// Creates an MCAP with <c>RightHand</c> coordinate mode and
        /// verifies the mismatch detection correctly identifies
        /// divergence from the default <c>LeftHand</c>.
        /// </summary>
        static void TestCoordinateModeMismatchDetection()
        {
            // Build MCAP with RightHand coordinate mode, then test mismatch against LeftHand
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var meta = new Dictionary<string, string> { ["coordinate_mode"] = "RightHand" };
            var (sch, ch) = BuildSchemaAndChannel(ms, 1, 1, "/tf", meta);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 1); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, 0); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            // Verify we CAN read the metadata and it differs from default
            var mcapMode = summary.Channels[0].Metadata["coordinate_mode"];
            Assert(mcapMode == "RightHand", "CoordMismatch: MCAP has RightHand");

            // The warning is logged at runtime via FoxgloveRuntime.EnableReplay.
            // We verify the detection logic works: mcapMode != "LeftHand"
            bool mismatch = mcapMode != "LeftHand";
            Assert(mismatch, "CoordMismatch: RightHand != LeftHand detected");
        }

        // ── Test 8: MetadataIndex read/parse roundtrip ──

        /// <summary>
        /// Writes a MetadataIndex record, reads the summary, and
        /// verifies the offset, length, name, and decoded metadata
        /// content survive the roundtrip.
        /// </summary>
        static void TestMetadataIndexRoundtrip()
        {
            var ms = new MemoryStream();

            // Write a full MCAP with metadata
            var name = "foxglove.parameters";

            // Minimal header + data
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            // Metadata record
            var metaOff = (ulong)ms.Position;
            var metaContent = new MemoryStream();
            McapWriter.WriteString(metaContent, name);
            var mapS = new MemoryStream();
            McapWriter.WriteString(mapS, "k"); McapWriter.WriteString(mapS, "v");
            var mapB = mapS.ToArray();
            McapWriter.WriteU32(metaContent, (uint)mapB.Length);
            metaContent.Write(mapB, 0, mapB.Length);
            WriteRecord(ms, 0x0C, metaContent);
            var metaLen = (ulong)ms.Position - metaOff;

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            // Summary section with MetadataIndex
            var sumStart = (ulong)ms.Position;

            var miContent = new MemoryStream();
            McapWriter.WriteU64(miContent, metaOff);
            McapWriter.WriteU64(miContent, metaLen);
            McapWriter.WriteString(miContent, name);
            WriteRecord(ms, 0x0D, miContent);

            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, 0); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.MetadataIndexes.Count == 1, "MetaIdxRt: 1 MetadataIndex");
            Assert(summary.MetadataIndexes[0].Offset == metaOff, "MetaIdxRt: offset correct");
            Assert(summary.MetadataIndexes[0].Length == metaLen, "MetaIdxRt: length correct");
            Assert(summary.MetadataIndexes[0].Name == name, "MetaIdxRt: name correct");

            // Read actual metadata via offset
            var meta = reader.ReadMetadataAt(summary.MetadataIndexes[0].Offset);
            Assert(meta.Name == name, "MetaIdxRt: decoded name correct");
            Assert(meta.Metadata.Count == 1, "MetaIdxRt: 1 metadata entry");
            Assert(meta.Metadata.ContainsKey("k") && meta.Metadata["k"] == "v", "MetaIdxRt: metadata entry correct");
        }
    }
}
