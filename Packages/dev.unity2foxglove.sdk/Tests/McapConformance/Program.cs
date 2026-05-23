// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/McapConformance
// Purpose: Standalone CLI bridge from the official foxglove/mcap conformance harness to Unity2Foxglove's C# MCAP reader.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests.McapConformance
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.Error.WriteLine("Usage: read-streamed <mcap-path> | read-indexed <mcap-path> | write <testcase-json-path>");
                return 2;
            }

            try
            {
                switch (args[0])
                {
                    case "read-streamed":
                        WriteUtf8(McapConformanceJson.WriteStreamed(McapConformanceReader.ReadStreamed(args[1])));
                        return 0;
                    case "read-indexed":
                        WriteUtf8(McapConformanceJson.WriteIndexed(McapConformanceReader.ReadIndexed(args[1])));
                        return 0;
                    case "write":
                        return McapConformanceWriter.WriteUnsupported(args[1], Console.Error);
                    default:
                        Console.Error.WriteLine("Unknown mode: " + args[0]);
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static void WriteUtf8(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
        }
    }
}
