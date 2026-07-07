// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-029")]
    [Trait("Domain", "MCAP")]
    public sealed class McapReplayEngineReviewTests
    {
        [Fact]
        public void ReplayEngineDocumentsOwnedTickBufferAndReaderOwnership()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var reader = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");

            Assert.Contains("The returned list is owned and reused by this engine", source, StringComparison.Ordinal);
            Assert.Contains("Consume it", source, StringComparison.Ordinal);
            Assert.Contains("McapReader borrows the stream", source, StringComparison.Ordinal);
            Assert.Contains("reader borrows the supplied stream", reader, StringComparison.Ordinal);
            Assert.DoesNotContain("public class McapReader : IDisposable", reader, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayHistoryUsesSortThenTrimInsteadOfPerMessageInsert()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var history = SourceMethod(source, "public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result, int maxMessages)");

            Assert.Contains("result.Add(new McapMessage", history, StringComparison.Ordinal);
            Assert.Contains("result.Sort(CompareMessages)", history, StringComparison.Ordinal);
            Assert.Contains("TrimHistoryToLatestMessages(result, maxMessages)", history, StringComparison.Ordinal);
            Assert.DoesNotContain("result.Insert(", history, StringComparison.Ordinal);
            Assert.DoesNotContain("FindHistoryInsertIndex", source, StringComparison.Ordinal);
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            Assert.True(brace >= 0, "Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }
    }
}
