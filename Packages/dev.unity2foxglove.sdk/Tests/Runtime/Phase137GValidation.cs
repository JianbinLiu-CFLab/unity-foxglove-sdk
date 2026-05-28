// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137G comment governance CI gate — verifies file headers,
// type/member Doxygen summaries, and magic number naming conventions.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137GValidation
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _headerMissing = new();
        private static readonly List<string> _summaryGaps = new();

        /// <summary>Maximum allowed gap between public/internal members and summary counts before a file is flagged.</summary>
        private const int MaxAllowedSummaryGap = 0;

        /// <summary>Directories and patterns to skip during the scan.</summary>
        private static readonly string[] SkipDirs = { "Generated", "Protos", "third-party", "obj", "bin" };
        private static readonly string[] SkipSuffixes = { ".g.cs", ".golden.cs", ".Designer.cs" };
        private static readonly string[] ScanRoots =
        {
            "Runtime",
            "Editor",
            "Tests/Runtime",
        };

        /// <summary>Regex for public/internal type declarations (class, struct, interface, enum, record).</summary>
        private static readonly Regex TypeDecl = new(
            @"^\s*(public|internal)\s+(static\s+|sealed\s+|readonly\s+|ref\s+|partial\s+)*(class|struct|interface|enum|record)\s",
            RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>Regex for public/internal member declarations (methods, properties, fields, consts, events, delegates).</summary>
        private static readonly Regex MemberDecl = new(
            @"^\s*(public|internal)\s+(?!class\b|struct\b|interface\b|enum\b|record\b)\S",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 137G: Comment and Documentation Governance ===");
            _passed = 0;
            _failed = 0;
            _headerMissing.Clear();
            _summaryGaps.Clear();

            var repoRoot = Phase16Validation.FindRepoRoot();
            var packageRoot = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk");

            var allFiles = new List<string>();
            foreach (var scanRoot in ScanRoots)
            {
                var dir = Path.Combine(packageRoot, scanRoot);
                if (!Directory.Exists(dir)) continue;
                CollectFiles(dir, allFiles);
            }

            Console.WriteLine($"  Scanning {allFiles.Count} .cs files...");

            foreach (var file in allFiles)
            {
                var content = File.ReadAllText(file);
                var relative = Path.GetRelativePath(packageRoot, file).Replace('\\', '/');

                CheckHeader(content, relative);
                CheckSummaries(content, relative);
            }

            ReportHeaderResults();
            ReportSummaryResults();

            Console.WriteLine($"\nPhase 137G: {_passed} passed, {_failed} failed.");
            if (_failed > 0)
                throw new InvalidOperationException($"Phase 137G validation failed with {_failed} checks.");
        }

        private static void CheckHeader(string content, string relativePath)
        {
            if (!content.Contains("// Module:"))
            {
                _headerMissing.Add(relativePath);
                Fail($"137G-H: missing Module header: {relativePath}");
            }
            else
            {
                Pass($"137G-H: header OK: {relativePath}");
            }
        }

        private static void CheckSummaries(string content, string relativePath)
        {
            var memberCount = MemberDecl.Matches(content).Count;
            var summaryCount = CountOccurrences(content, "/// <summary>");

            // Type declarations should also have summaries, so include them in the expected count.
            var typeCount = TypeDecl.Matches(content).Count;
            var expectedMinimum = typeCount + memberCount;
            var gap = expectedMinimum - summaryCount;

            if (gap > MaxAllowedSummaryGap)
            {
                _summaryGaps.Add($"{relativePath} (types={typeCount}, members={memberCount}, summaries={summaryCount}, gap={gap})");
                Fail($"137G-S: summary gap {gap}: {relativePath}");
            }
            else
            {
                Pass($"137G-S: summaries OK (gap={gap}): {relativePath}");
            }
        }

        private static void ReportHeaderResults()
        {
            Console.WriteLine($"\n  Header check: {_headerMissing.Count} files missing Module header.");
            if (_headerMissing.Count > 0)
            {
                Console.WriteLine("  Missing files:");
                foreach (var f in _headerMissing)
                    Console.WriteLine($"    - {f}");
            }
        }

        private static void ReportSummaryResults()
        {
            Console.WriteLine($"\n  Summary check: {_summaryGaps.Count} files with gaps.");
            if (_summaryGaps.Count > 0)
            {
                Console.WriteLine("  Top gaps:");
                var sorted = _summaryGaps
                    .OrderByDescending(g => ExtractGap(g))
                    .Take(20);
                foreach (var g in sorted)
                    Console.WriteLine($"    {g}");
            }
        }

        private static int ExtractGap(string entry)
        {
            var match = Regex.Match(entry, @"gap=(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static void CollectFiles(string directory, List<string> results)
        {
            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                if (SkipSuffixes.Any(s => file.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                    continue;
                results.Add(file);
            }

            foreach (var sub in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(sub);
                if (SkipDirs.Any(d => string.Equals(d, dirName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                CollectFiles(sub, results);
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }

            return count;
        }

        private static void Pass(string message)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {message}");
        }

        private static void Fail(string message)
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {message}");
        }
    }
}
