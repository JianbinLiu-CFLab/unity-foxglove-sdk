// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-10 regression coverage for MCAP DataLoader query budgets.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_10Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-10: MCAP Replay DataLoader Remote ===");
            _passed = 0;

            DefaultQueryAppliesMessageBudget();
            ExplicitQueryCapStillWins();
            ExplicitUnlimitedQueryRemainsAvailable();

            Console.WriteLine($"Phase 134-10: {_passed} checks passed.");
        }

        private static void DefaultQueryAppliesMessageBudget()
        {
            const int extraMessages = 3;
            var totalMessages = McapDataLoaderQuery.DefaultMaxMessages + extraMessages;
            using var stream = CreateDirectFixture(totalMessages);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery()).ToList();
            Check(messages.Count == McapDataLoaderQuery.DefaultMaxMessages,
                "134-10A-1: default DataLoader query applies message-count budget");
            Check(messages[0].LogTime == (ulong)(extraMessages + 1),
                "134-10A-2: default DataLoader budget keeps latest chronological messages");
        }

        private static void ExplicitQueryCapStillWins()
        {
            using var stream = CreateDirectFixture(12);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 2 }).ToList();
            Check(messages.Count == 2,
                "134-10B-1: explicit DataLoader MaxMessages cap is honored");
            Check(messages[0].LogTime == 11 && messages[1].LogTime == 12,
                "134-10B-2: explicit DataLoader cap keeps latest chronological messages");
        }

        private static void ExplicitUnlimitedQueryRemainsAvailable()
        {
            var totalMessages = McapDataLoaderQuery.DefaultMaxMessages + 1;
            using var stream = CreateDirectFixture(totalMessages);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 0 }).ToList();
            Check(messages.Count == totalMessages,
                "134-10C-1: explicit MaxMessages=0 remains an opt-in unlimited query");
        }

        private static MemoryStream CreateDirectFixture(int messageCount)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-10-dataloader");
                writer.WriteSchema(1, "phase134_10.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_10", "json", new Dictionary<string, string>());
                for (var i = 1; i <= messageCount; i++)
                    writer.WriteMessage(1, (uint)i, (ulong)i, (ulong)i, Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
