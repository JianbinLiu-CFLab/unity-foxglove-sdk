// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137D McapReader decode split guard.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137DValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137D Tests ---");
            _passed = 0;

            VerifyMcapRecordDecoderExists();
            VerifyStaticMethodsExtracted();
            VerifyPublicDecodeMethodsPreserved();
            VerifyInstanceMethodsPreserved();
            VerifyOldStaticGone();
            VerifyNoInstanceState();

            Console.WriteLine("Phase 137D: " + _passed + " checks passed.\n");
        }

        private static void VerifyMcapRecordDecoderExists()
        {
            var path = "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapRecordDecoder.cs";
            Check(File.Exists(path), "137D-1: McapRecordDecoder.cs exists");
            Check(typeof(McapRecordDecoder).IsAbstract && typeof(McapRecordDecoder).IsSealed,
                "137D-2: McapRecordDecoder is a static class");
        }

        private static void VerifyStaticMethodsExtracted()
        {
            var methods = new[] {
                "DecodeHeader", "DecodeMessage",
                "DecodeChunkIndex", "DecodeStatistics", "DecodeMetadataIndex",
                "DecodeMetadata", "DecodeAttachment", "DecodeAttachmentIndex",
                "DecodeFooter", "DecodeChunkRecordsContent"
            };
            foreach (var m in methods)
                Check(typeof(McapRecordDecoder).GetMethod(m) != null,
                    "137D-3: McapRecordDecoder." + m + " exists");

            // Overloaded methods — check via GetMethods
            var allMethods = typeof(McapRecordDecoder).GetMethods()
                .Select(m => m.Name).ToHashSet();
            foreach (var m in new[] { "DecodeSchema", "DecodeChannel" })
                Check(allMethods.Contains(m),
                    "137D-3: McapRecordDecoder." + m + " exists (overloaded)");
        }

        private static void VerifyPublicDecodeMethodsPreserved()
        {
            foreach (var m in new[] { "DecodeHeader", "DecodeFooter", "DecodeMessage",
                "DecodeChunkIndex", "DecodeStatistics", "DecodeAttachment" })
            {
                var method = typeof(McapRecordDecoder).GetMethod(m);
                Check(method != null && method.IsPublic,
                    "137D-4: " + m + " is public static");
            }
        }

        private static void VerifyInstanceMethodsPreserved()
        {
            Check(typeof(McapReader).GetMethod("ReadAttachmentAt") != null,
                "137D-5: ReadAttachmentAt preserved on McapReader");
            Check(typeof(McapReader).GetMethod("ReadMetadataAt") != null,
                "137D-6: ReadMetadataAt preserved on McapReader");
            Check(typeof(McapReader).GetMethod("ReadSummary") != null,
                "137D-7: ReadSummary preserved on McapReader");
            Check(typeof(McapReader).GetMethod("ReadSequentialMessages") != null,
                "137D-8: ReadSequentialMessages preserved on McapReader");
        }

        private static void VerifyOldStaticGone()
        {
            Check(typeof(McapReader).GetMethod("DecodeHeader") == null,
                "137D-9: DecodeHeader removed from McapReader");
            Check(typeof(McapReader).GetMethod("DecodeFooter") == null,
                "137D-10: DecodeFooter removed from McapReader");
        }

        private static void VerifyNoInstanceState()
        {
            Check(typeof(McapRecordDecoder).GetFields().Length == 0
                  || typeof(McapRecordDecoder).GetFields().All(f => f.IsLiteral),
                "137D-11: McapRecordDecoder has no mutable instance state");
        }

        private static void Check(bool condition, string label)
        {
            if (condition) { Console.WriteLine("[PASS] " + label); _passed++; }
            else Console.WriteLine("[FAIL] " + label);
        }
    }
}
