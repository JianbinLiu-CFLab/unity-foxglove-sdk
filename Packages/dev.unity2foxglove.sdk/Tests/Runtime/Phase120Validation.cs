// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 120 MCAP official compatibility gate validation and evidence report generation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase120Validation
    {
        private static int _passed;
        private static readonly List<ReportCheck> CoreChecks = new List<ReportCheck>();
        private static readonly List<ReportCheck> OptionalChecks = new List<ReportCheck>();
        private static readonly List<ReportFixture> Fixtures = new List<ReportFixture>();
        private static readonly List<string> Limitations = new List<string>();

        public static void Validate()
        {
            Run(includeOfficialPython: false);
        }

        public static void ValidateOfficial()
        {
            Run(includeOfficialPython: true);
        }

        private static void Run(bool includeOfficialPython)
        {
            Console.WriteLine();
            Console.WriteLine(includeOfficialPython
                ? "=== Phase 120: MCAP Official Compatibility Gate + Python Interop ==="
                : "=== Phase 120: MCAP Official Compatibility Gate ===");
            _passed = 0;
            CoreChecks.Clear();
            OptionalChecks.Clear();
            Fixtures.Clear();
            Limitations.Clear();

            Directory.CreateDirectory(CompatDir());
            VerifyEvidenceNote();
            VerifyValidationWiring();
            VerifyPriorPhaseHooks();
            VerifyParityMatrix();
            VerifyClaimLedgerDocs();

            var unityChunked = CreateUnityChunkedFixture();
            var unityDirect = CreateUnityDirectFixture();
            VerifyLocalFixtureReaders(unityChunked, "unity_chunked_all_indexes", expectMetadata: true, expectAttachment: true);
            VerifyLocalFixtureReaders(unityDirect, "unity_summaryless_or_direct_fixture", expectMetadata: false, expectAttachment: false);

            if (includeOfficialPython)
                VerifyOfficialPythonInterop(unityChunked.path);
            else
                AddOptional("official-python-interop", "skipped", "Run --phase120-official to execute Python mcap interop.");

            AddOptional("foxglove-desktop-manual-open", "skipped",
                "Manual Foxglove Desktop open is deferred; verdict remains PASS WITH NOTED LIMITATIONS.");
            Limitations.Add("Foxglove Desktop manual visual open is deferred.");
            Limitations.Add("Production Remote Data Loader, cloud cache, range serving, organization auth, and Remote Access Gateway remain out of scope.");

            WriteReport(includeOfficialPython);
            Console.WriteLine($"Phase 120: {_passed} checks passed.");
        }

        private static void VerifyEvidenceNote()
        {
            var note = ReadRepoText("Developer/MCAP Official Compatibility Gate.md");
            foreach (var required in new[]
            {
                "Final Verdict",
                "PASS WITH NOTED LIMITATIONS",
                "Environment",
                "Official Interop",
                "Foxglove Desktop Manual Open",
                "Performance",
                "Skipped Checks",
                "Claim Ledger",
                "https://mcap.dev/spec",
                "https://mcap.dev/docs/python/",
                "https://docs.foxglove.dev/docs/visualization/connecting/cloud-data/remote-data-loader"
            })
            {
                Check(note.Contains(required, StringComparison.Ordinal),
                    "120-A1: compatibility gate note contains " + required);
            }

            AddCore("evidence-note", "passed", "Developer compatibility gate note has required sections.");
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(program.Contains("--phase120", StringComparison.Ordinal)
                  && program.Contains("--phase120-official", StringComparison.Ordinal)
                  && program.Contains("RunPhase120Only", StringComparison.Ordinal)
                  && program.Contains("RunPhase120OfficialOnly", StringComparison.Ordinal),
                "120-B1: Program.cs wires --phase120 and --phase120-official");
            Check(project.Contains("Phase120Validation.cs", StringComparison.Ordinal),
                "120-B2: runtime test project compiles Phase120Validation");
            AddCore("phase120-wiring", "passed", "Phase 120 validation entry points are wired.");
        }

        private static void VerifyPriorPhaseHooks()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            foreach (var phase in new[] { "116", "117", "118", "119" })
            {
                Check(program.Contains("--phase" + phase, StringComparison.Ordinal),
                    "120-C1: prior phase hook exists for " + phase);
            }

            AddCore("phase116-119-hooks", "passed", "Prior phase validation hooks are present.");
        }

        private static void VerifyParityMatrix()
        {
            var matrix = ReadRepoText("Developer/MCAP Spec Parity Matrix.md");
            foreach (var opcode in new[]
            {
                "0x01", "0x02", "0x03", "0x04", "0x05", "0x06", "0x07", "0x08",
                "0x09", "0x0A", "0x0B", "0x0C", "0x0D", "0x0E", "0x0F",
                "0x80-0xFF", "0x00"
            })
            {
                Check(matrix.Contains(opcode, StringComparison.Ordinal),
                    "120-D1: parity matrix covers opcode " + opcode);
            }

            Check(matrix.Contains("https://mcap.dev/spec", StringComparison.Ordinal)
                  && matrix.Contains("No remote DataLoader", StringComparison.Ordinal),
                "120-D2: parity matrix preserves official reference and remote non-goal");
            AddCore("spec-parity-matrix", "passed", "Opcode coverage and non-goals are present.");
        }

        private static void VerifyClaimLedgerDocs()
        {
            var note = ReadRepoText("Developer/MCAP Official Compatibility Gate.md");
            Check(note.Contains("complete official MCAP library replacement", StringComparison.Ordinal)
                  && note.Contains("production Foxglove Remote Data Loader infrastructure", StringComparison.Ordinal)
                  && note.Contains("Remote Access Gateway support", StringComparison.Ordinal),
                "120-E1: claim ledger records explicit non-claims");
            Check(note.Contains("local DataLoader-shaped facade", StringComparison.Ordinal)
                  && note.Contains("official Python MCAP reader", StringComparison.Ordinal),
                "120-E2: claim ledger records allowed local compatibility claims");
            AddCore("claim-ledger", "passed", "Claim ledger distinguishes supported claims from non-claims.");
        }

        private static ReportFixture CreateUnityChunkedFixture()
        {
            var path = Path.Combine(CompatDir(), "unity_chunked_all_indexes.mcap");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var recorder = new McapRecorder(fs, null, 256, "zstd"))
            {
                recorder.AddChannel(1, "/phase120/a", "json", "phase120.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase120/b", "json", "phase120.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                recorder.WriteMetadata("phase120.compatibility", "{\"value\":\"unity-authored\"}");
                recorder.AddAttachment("phase120.txt", "text/plain", Encoding.UTF8.GetBytes("phase120"), 30, 30);
                recorder.WriteMessage(1, 40, Encoding.UTF8.GetBytes("{\"a\":40}"));
                recorder.Close();
            }

            var fixture = new ReportFixture
            {
                name = "unity_chunked_all_indexes",
                path = path,
                kind = "unity-authored chunked zstd with summary/indexes",
                generated = true
            };
            Fixtures.Add(fixture);
            return fixture;
        }

        private static ReportFixture CreateUnityDirectFixture()
        {
            var path = Path.Combine(CompatDir(), "unity_summaryless_or_direct_fixture.mcap");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new McapWriter(fs))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase120-direct");
                writer.WriteSchema(1, "phase120.Direct", "jsonschema", Encoding.UTF8.GetBytes("{\"type\":\"object\"}"));
                writer.WriteChannel(1, 1, "/phase120/direct", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{\"d\":10}"));
                writer.WriteMessage(1, 2, 20, 20, Encoding.UTF8.GetBytes("{\"d\":20}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            var fixture = new ReportFixture
            {
                name = "unity_summaryless_or_direct_fixture",
                path = path,
                kind = "unity-authored summary-less direct messages",
                generated = true
            };
            Fixtures.Add(fixture);
            return fixture;
        }

        private static void VerifyLocalFixtureReaders(
            ReportFixture fixture,
            string name,
            bool expectMetadata,
            bool expectAttachment)
        {
            using (var fs = File.OpenRead(fixture.path))
            {
                var summary = new McapReader(fs).ReadSummary();
                Check(summary.Channels.Count > 0 && summary.Schemas.Count > 0,
                    "120-F1: McapReader opens " + name);
                VerifyJsonSchemaRoots(summary, name);
                if (expectMetadata)
                    Check(summary.MetadataIndexes.Count > 0, "120-F2: metadata index exists for " + name);
                if (expectAttachment)
                    Check(summary.AttachmentIndexes.Count > 0, "120-F3: attachment index exists for " + name);
            }

            using (var fs = File.OpenRead(fixture.path))
            using (var reader = new McapIndexedReader(fs, leaveOpen: true))
            {
                Check(reader.ReadMessages().Count > 0, "120-F4: McapIndexedReader reads " + name);
            }

            using (var fs = File.OpenRead(fixture.path))
            using (var loader = new McapDataLoader(fs, leaveOpen: true))
            {
                var init = loader.Initialize();
                Check(init.Channels.Count > 0 && loader.CreateIterator(new McapDataLoaderQuery()).Count() > 0,
                    "120-F5: McapDataLoader initializes and iterates " + name);
                Check(init.Problems.Any(p => p.Code.StartsWith("FoxRunSchemaMetadata", StringComparison.Ordinal)),
                    "120-F6: FoxRun schema-governance diagnostics remain visible for " + name);
            }

            AddCore(name + "-local-readers", "passed", "Local readers opened generated fixture.");
        }

        private static void VerifyJsonSchemaRoots(McapFileSummary summary, string fixtureName)
        {
            var schemas = summary.Schemas.ToDictionary(schema => schema.Id);
            foreach (var channel in summary.Channels)
            {
                if (!string.Equals(channel.MessageEncoding, "json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!schemas.TryGetValue(channel.SchemaId, out var schema))
                    continue;
                if (!string.Equals(schema.Encoding, "jsonschema", StringComparison.OrdinalIgnoreCase))
                    continue;

                var json = Encoding.UTF8.GetString(schema.Data ?? new byte[0]);
                var parsed = JObject.Parse(json);
                Check(string.Equals(parsed["type"]?.ToString(), "object", StringComparison.Ordinal),
                    "120-F7: Foxglove Desktop-compatible JSON schema root is object for " + fixtureName + " " + channel.Topic);
            }
        }

        private static void VerifyOfficialPythonInterop(string unityFixturePath)
        {
            var python = FindPython();
            if (string.IsNullOrEmpty(python))
            {
                AddOptional("official-python-interop", "skipped", "Python executable not found.");
                Limitations.Add("Official Python interop skipped because Python was not found.");
                return;
            }

            var officialChunked = Path.Combine(CompatDir(), "official_python_chunked_zstd.mcap");
            var officialDirect = Path.Combine(CompatDir(), "official_python_unchunked_no_summary.mcap");
            var script = Path.Combine(CompatDir(), "phase120_official_interop.py");
            File.WriteAllText(script, BuildOfficialPythonScript(), Encoding.UTF8);

            var result = RunProcess(python,
                Quote(script) + " " + Quote(unityFixturePath) + " " + Quote(officialChunked) + " " + Quote(officialDirect));
            Check(result.ExitCode == 0, "120-G1: official Python mcap helper exits successfully");
            Check(result.Output.Contains("\"unity_message_count\": 3", StringComparison.Ordinal),
                "120-G2: official Python reader sees Unity-authored messages");
            Check(File.Exists(officialChunked) && File.Exists(officialDirect),
                "120-G3: official Python writer creates supported fixtures");

            VerifyLocalFixtureReaders(new ReportFixture
            {
                name = "official_python_chunked_zstd",
                path = officialChunked,
                kind = "official Python chunked zstd",
                generated = true
            }, "official_python_chunked_zstd", expectMetadata: true, expectAttachment: true);

            VerifyLocalFixtureReaders(new ReportFixture
            {
                name = "official_python_unchunked_no_summary",
                path = officialDirect,
                kind = "official Python unchunked no-summary",
                generated = true
            }, "official_python_unchunked_no_summary", expectMetadata: false, expectAttachment: false);

            Fixtures.Add(new ReportFixture { name = "official_python_chunked_zstd", path = officialChunked, kind = "official Python chunked zstd", generated = true });
            Fixtures.Add(new ReportFixture { name = "official_python_unchunked_no_summary", path = officialDirect, kind = "official Python unchunked no-summary", generated = true });
            AddOptional("official-python-interop", "passed", result.Output.Trim());
        }

        private static string BuildOfficialPythonScript()
        {
            return @"
import json
import sys
from mcap import __version__ as mcap_version
from mcap.reader import make_reader
from mcap.writer import Writer, CompressionType, IndexType

unity_path, official_chunked, official_direct = sys.argv[1:4]

with open(unity_path, 'rb') as f:
    reader = make_reader(f)
    unity_messages = list(reader.iter_messages())
    unity_metadata = list(reader.iter_metadata())
    unity_attachments = list(reader.iter_attachments())

with open(official_chunked, 'wb') as f:
    writer = Writer(f, compression=CompressionType.ZSTD, index_types=IndexType.ALL, use_chunking=True, use_statistics=True, use_summary_offsets=True)
    writer.start(library='python mcap phase120')
    schema_id = writer.register_schema('phase120.Official', 'jsonschema', b'{""type"":""object""}')
    channel_id = writer.register_channel('/phase120/official', 'json', schema_id)
    writer.add_metadata('phase120.compatibility', {'value': 'official-python'})
    writer.add_message(channel_id, log_time=10, publish_time=10, data=b'{""official"":1}', sequence=1)
    writer.add_attachment(create_time=20, log_time=20, name='official.txt', media_type='text/plain', data=b'official')
    writer.add_message(channel_id, log_time=30, publish_time=30, data=b'{""official"":2}', sequence=2)
    writer.finish()

with open(official_direct, 'wb') as f:
    writer = Writer(f, compression=CompressionType.NONE, index_types=IndexType.NONE, use_chunking=False, repeat_channels=False, repeat_schemas=False, use_statistics=False, use_summary_offsets=False)
    writer.start(library='python mcap phase120 direct')
    schema_id = writer.register_schema('phase120.OfficialDirect', 'jsonschema', b'{""type"":""object""}')
    channel_id = writer.register_channel('/phase120/official/direct', 'json', schema_id)
    writer.add_message(channel_id, log_time=10, publish_time=10, data=b'{""direct"":1}', sequence=1)
    writer.add_message(channel_id, log_time=20, publish_time=20, data=b'{""direct"":2}', sequence=2)
    writer.finish()

print(json.dumps({
    'mcap_version': mcap_version,
    'unity_message_count': len(unity_messages),
    'unity_metadata_count': len(unity_metadata),
    'unity_attachment_count': len(unity_attachments),
    'official_chunked': official_chunked,
    'official_direct': official_direct,
}, sort_keys=True))
";
        }

        private static void WriteReport(bool includeOfficialPython)
        {
            var report = new Phase120Report
            {
                verdict = "PASS WITH NOTED LIMITATIONS",
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                commit = RunProcess("git", "rev-parse --short HEAD").Output.Trim(),
                coreChecks = CoreChecks,
                optionalChecks = OptionalChecks,
                officialTooling = includeOfficialPython ? "official Python mcap executed" : "official Python mcap skipped in --phase120",
                fixtures = Fixtures,
                manualChecks = "Foxglove Desktop manual open skipped/deferred",
                limitations = Limitations
            };

            var path = Path.Combine(CompatDir(), "phase120-report.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented), Encoding.UTF8);
            Check(File.Exists(path), "120-H1: phase120-report.json is written");
        }

        private static string FindPython()
        {
            foreach (var candidate in new[] { "python", "py" })
            {
                var result = RunProcess(candidate, "--version");
                if (result.ExitCode == 0)
                    return candidate;
            }

            return string.Empty;
        }

        private static ProcessResult RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = RepoRoot()
                };
                using var process = Process.Start(psi);
                var stdout = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                var stderr = process?.StandardError.ReadToEnd() ?? string.Empty;
                process?.WaitForExit();
                return new ProcessResult(process?.ExitCode ?? -1, stdout, stderr);
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, string.Empty, ex.Message);
            }
        }

        private static string Quote(string value)
            => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string CompatDir()
            => Path.Combine(RepoRoot(), "build", "mcap-compat");

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase120 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void AddCore(string name, string status, string details)
            => CoreChecks.Add(new ReportCheck { name = name, status = status, details = details });

        private static void AddOptional(string name, string status, string details)
            => OptionalChecks.Add(new ReportCheck { name = name, status = status, details = details });

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class ProcessResult
        {
            public readonly int ExitCode;
            public readonly string Output;
            public readonly string Error;

            public ProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
                Error = error ?? string.Empty;
            }
        }

        private sealed class Phase120Report
        {
            public string verdict { get; set; }
            public string generatedAtUtc { get; set; }
            public string commit { get; set; }
            public List<ReportCheck> coreChecks { get; set; }
            public List<ReportCheck> optionalChecks { get; set; }
            public string officialTooling { get; set; }
            public List<ReportFixture> fixtures { get; set; }
            public string manualChecks { get; set; }
            public List<string> limitations { get; set; }
        }

        private sealed class ReportCheck
        {
            public string name { get; set; }
            public string status { get; set; }
            public string details { get; set; }
        }

        private sealed class ReportFixture
        {
            public string name { get; set; }
            public string path { get; set; }
            public string kind { get; set; }
            public bool generated { get; set; }
        }
    }
}
