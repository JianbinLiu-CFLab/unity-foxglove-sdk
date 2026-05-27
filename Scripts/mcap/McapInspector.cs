// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/mcap
// Purpose: Standalone diagnostic tool that reads an MCAP file and prints channel metadata, statistics, and /tf /scene messages.
// Note: This file owns a Main entry point and is intentionally not compiled into the runtime test harness.

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

class McapInspector
{
    /// <summary>
    /// Reads an MCAP file and prints channel metadata, statistics, and
    /// the first few /tf and /scene messages to the console.
    /// </summary>
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: McapInspector <mcap-file>");
            return;
        }

        var path = args[0];
        Console.WriteLine($"=== {Path.GetFileName(path)} ===");

        // Read as stream using McapReader
        using var fs = File.OpenRead(path);
        var reader = new McapReader(fs);
        var summary = reader.ReadSummary();

        // Show channel metadata
        if (summary.Channels != null)
        {
            foreach (var ch in summary.Channels)
            {
                Console.Write($"Channel {ch.Id}: topic={ch.Topic}");
                if (ch.Metadata != null && ch.Metadata.TryGetValue("coordinate_mode", out var mode))
                    Console.Write($", coordinate_mode={mode}");
                Console.WriteLine();
            }
        }

        // Show statistics
        if (summary.Statistics != null)
        {
            var s = summary.Statistics;
            Console.WriteLine($"Statistics: messages={s.MessageCount}, channels={s.ChannelCount}, time={s.MessageStartTime}..{s.MessageEndTime}");
        }

        // Walk chunks and print first few /tf messages
        if (summary.ChunkIndexes != null)
        {
            int tfCount = 0;
            foreach (var ci in summary.ChunkIndexes)
            {
                var chunk = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength);
                if (chunk == null) continue;
                int off = 0;
                while (off + 9 <= chunk.Length)
                {
                    var op = chunk[off++];
                    var len = McapBinaryReader.ReadU64LE(chunk, ref off);
                    if (off + (int)len > chunk.Length) break;

                    if (op == 0x05) // message
                    {
                        int msgOff = off;
                        ushort chId = McapBinaryReader.ReadU16LE(chunk, ref off);
                        uint seq = McapBinaryReader.ReadU32LE(chunk, ref off);
                        ulong logNs = McapBinaryReader.ReadU64LE(chunk, ref off);
                        ulong pubNs = McapBinaryReader.ReadU64LE(chunk, ref off);
                        int dataLen = off + (int)len - msgOff - 2 - 4 - 8 - 8;
                        if (dataLen > 0 && dataLen < 500000)
                        {
                            var payload = new byte[dataLen];
                            Buffer.BlockCopy(chunk, off, payload, 0, dataLen);
                            var json = Encoding.UTF8.GetString(payload);

                            if (json.Contains("\"translation\"") && tfCount == 0)
                            {
                                var j = JObject.Parse(json);
                                var t = j["translation"];
                                var r = j["rotation"];
                                string cid = (string)j["child_frame_id"] ?? "?";
                                Console.WriteLine($"  /tf ch={chId} child_frame_id={cid} time={logNs}ns");
                                if (t != null) Console.WriteLine($"    translation=({(double)t["x"]:F3}, {(double)t["y"]:F3}, {(double)t["z"]:F3})");
                                if (r != null) Console.WriteLine($"    rotation=({(double)r["x"]:F3}, {(double)r["y"]:F3}, {(double)r["z"]:F3}, {(double)r["w"]:F3})");
                                tfCount++;
                            }
                            else if (json.Contains("\"entities\"") && json.Contains("\"cubes\"") && tfCount < 3)
                            {
                                var j = JObject.Parse(json);
                                var ents = j["entities"] as JArray;
                                if (ents != null && ents.Count > 0)
                                {
                                    foreach (var ent in ents)
                                    {
                                        var cubes = ent["cubes"] as JArray;
                                        if (cubes != null && cubes.Count > 0)
                                        {
                                            var c = cubes[0];
                                            var pose = c["pose"];
                                            var eid = (string)ent["id"] ?? "?";
                                            Console.WriteLine($"  /scene entity={eid} time={logNs}ns");
                                            if (pose?["position"] != null)
                                                Console.WriteLine($"    position=({(double)pose["position"]["x"]:F3}, {(double)pose["position"]["y"]:F3}, {(double)pose["position"]["z"]:F3})");
                                            if (pose?["orientation"] != null)
                                                Console.WriteLine($"    orientation=({(double)pose["orientation"]["x"]:F3}, {(double)pose["orientation"]["y"]:F3}, {(double)pose["orientation"]["z"]:F3}, {(double)pose["orientation"]["w"]:F3})");
                                            tfCount++;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        off = msgOff + (int)len;
                    }
                    else
                    {
                        off += (int)len;
                    }
                }
            }
        }
    }
}
