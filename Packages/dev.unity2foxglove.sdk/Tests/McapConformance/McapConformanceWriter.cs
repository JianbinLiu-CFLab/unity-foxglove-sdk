// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/McapConformance
// Purpose: Conservative C# writer bridge placeholder for official MCAP conformance writer tests.

using System.IO;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests.McapConformance
{
    internal static class McapConformanceWriter
    {
        public static int WriteUnsupported(string testcaseJsonPath, TextWriter stderr)
        {
            var testcase = JObject.Parse(File.ReadAllText(testcaseJsonPath));
            var records = testcase["records"] as JArray;
            stderr.WriteLine(
                "Unsupported: C# official writer option parity is deferred to Phase 122. " +
                "Parsed " + (records?.Count ?? 0) + " input record(s), but did not convert them to MCAP bytes: " + testcaseJsonPath);
            return 2;
        }
    }
}
