// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 116 validation for the local MCAP DataLoader facade.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase116Validation
    {
        private const string CurrentHash = "1111111111111111111111111111111111111111111111111111111111111111";
        private const string MismatchedHash = "2222222222222222222222222222222222222222222222222222222222222222";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 116: Local MCAP DataLoader v1 ===");
            _passed = 0;

            VerifyPublicApiShape();
            VerifyInitializationAndIteratorQueries();
            VerifyBackfillSemantics();
            VerifySchemaGovernanceDiagnostics();
            VerifyReaderBoundaries();
            VerifyValidationWiringAndDocs();

            Console.WriteLine($"Phase 116: {_passed} checks passed.");
        }

        private static void VerifyPublicApiShape()
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            var init = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderInitialization");
            var channel = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderChannel");
            var schema = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderSchema");
            var problem = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderProblem");
            var timeRange = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderTimeRange");
            var message = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderMessage");
            var query = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            var backfill = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");

            Check(typeof(IDisposable).IsAssignableFrom(loader), "116-A1: McapDataLoader implements IDisposable");
            Check(loader.GetConstructor(new[] { typeof(string) }) != null,
                "116-A2: McapDataLoader exposes path constructor");
            Check(loader.GetConstructor(new[] { typeof(Stream), typeof(bool) }) != null,
                "116-A3: McapDataLoader exposes Stream/leaveOpen constructor");
            Check(loader.GetMethod("Initialize", Type.EmptyTypes)?.ReturnType == init,
                "116-A4: Initialize returns initialization DTO");
            Check(loader.GetMethod("CreateIterator", new[] { query })?.ReturnType == typeof(IEnumerable<>).MakeGenericType(message),
                "116-A5: CreateIterator returns typed message enumerable");
            Check(loader.GetMethod("GetBackfill", new[] { backfill })?.ReturnType == typeof(IReadOnlyList<>).MakeGenericType(message),
                "116-A6: GetBackfill returns typed message list");

            foreach (var dto in new[] { init, channel, schema, problem, timeRange, message, query, backfill })
            {
                Check(!dto.Assembly.GetReferencedAssemblies().Any(a => a.Name == "UnityEngine"),
                    "116-A7: DataLoader DTO avoids UnityEngine dependency: " + dto.Name);
            }
        }

        private static void VerifyInitializationAndIteratorQueries()
        {
            using var ms = CreateFixture();
            using var loader = (IDisposable)CreateLoader(ms);

            var first = Invoke(loader, "Initialize");
            var second = Invoke(loader, "Initialize");
            Check(object.ReferenceEquals(first, second), "116-B1: Initialize is idempotent and cached");

            Check(Count(Member(first, "Channels")) == 3, "116-B2: initialization exposes three channels");
            Check(Count(Member(first, "Schemas")) == 3, "116-B3: initialization exposes three schemas");
            Check(Count(Member(first, "MetadataIndexes")) == 1, "116-B4: initialization exposes metadata index summaries");
            Check(Count(Member(first, "AttachmentIndexes")) == 1, "116-B5: initialization exposes attachment index summaries");
            Check(BoolMember(Member(first, "TimeRange"), "HasRange")
                  && ULongMember(Member(first, "TimeRange"), "StartTimeNs") == 10
                  && ULongMember(Member(first, "TimeRange"), "EndTimeNs") == 80,
                "116-B6: initialization exposes inclusive message time range");
            Check(BoolMember(first, "HasTotalMessageCount")
                  && ULongMember(first, "TotalMessageCount") == 9,
                "116-B7: initialization exposes message count when statistics are available");

            var topicQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(topicQuery, "Topics", new List<string> { "/phase116/a" });
            CheckTimes(InvokeMessages(loader, "CreateIterator", topicQuery), new ulong[] { 10, 30, 50 },
                "116-C1: iterator filters by topic");

            var channelQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(channelQuery, "ChannelIds", new List<ushort> { 2 });
            CheckTimes(InvokeMessages(loader, "CreateIterator", channelQuery), new ulong[] { 20, 40, 60 },
                "116-C2: iterator filters by channel ID");

            var rangeQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(rangeQuery, "StartTimeNs", 30UL);
            Set(rangeQuery, "EndTimeNs", 60UL);
            CheckTimes(InvokeMessages(loader, "CreateIterator", rangeQuery), new ulong[] { 30, 40, 50, 50, 60 },
                "116-C3: iterator filters by inclusive time range");

            var latestQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(latestQuery, "MaxMessages", 2);
            CheckTimes(InvokeMessages(loader, "CreateIterator", latestQuery), new ulong[] { 80, 80 },
                "116-C4: iterator MaxMessages keeps latest matches like McapIndexedReader");
        }

        private static void VerifyBackfillSemantics()
        {
            using var ms = CreateFixture();
            using var loader = (IDisposable)CreateLoader(ms);
            Invoke(loader, "Initialize");

            var query = New("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");
            Set(query, "TimeNs", 55UL);
            Set(query, "ChannelIds", new List<ushort> { 1, 2, 3 });
            var backfill = InvokeMessages(loader, "GetBackfill", query);
            Check(ChannelTimes(backfill).SequenceEqual(new[] { "1:50", "2:40", "3:50" }),
                "116-D1: backfill returns one latest message per requested channel");

            var tiedQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");
            Set(tiedQuery, "TimeNs", 80UL);
            Set(tiedQuery, "Topics", new List<string> { "/phase116/tie" });
            var tied = InvokeMessages(loader, "GetBackfill", tiedQuery);
            Check(tied.Count == 1
                  && UShortMember(tied[0], "ChannelId") == 3
                  && ULongMember(tied[0], "LogTime") == 80
                  && UIntMember(tied[0], "Sequence") == 2,
                "116-D2: same-channel same-time backfill tie uses deterministic sequence/publish ordering");
        }

        private static void VerifySchemaGovernanceDiagnostics()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            FoxRunSchemaInfoRegistry.RegisterGenerated(CreateSchemaInfo(CurrentHash));
            try
            {
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(CurrentHash), out var matchingJson);
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(MismatchedHash), out var mismatchedJson);

                using (var matching = CreateFixture(matchingJson))
                using (var loader = (IDisposable)CreateLoader(matching))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMatch"),
                        "116-E1: matching FoxRun metadata is surfaced as an info diagnostic");
                }

                using (var missing = CreateFixture(includeFoxRunMetadata: false))
                using (var loader = (IDisposable)CreateLoader(missing))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMissing"),
                        "116-E2: missing FoxRun metadata initializes with a diagnostic");
                }

                using (var malformed = CreateFixture("{not-json}"))
                using (var loader = (IDisposable)CreateLoader(malformed))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMalformed"),
                        "116-E3: malformed FoxRun metadata initializes with a warning diagnostic");
                }

                using (var mismatch = CreateFixture(mismatchedJson))
                using (var loader = (IDisposable)CreateLoader(mismatch))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p =>
                              StringMember(p, "Code") == "FoxRunSchemaMetadataMismatch"
                              && StringMember(p, "Severity") == "Error"),
                        "116-E4: confirmed FoxRun mismatch is diagnostic-only but high severity");
                    Check(InvokeMessages(loader, "CreateIterator", New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery")).Count > 0,
                        "116-E5: DataLoader continues raw local iteration after FoxRun mismatch");
                }
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static void VerifyReaderBoundaries()
        {
            Check(Throws<NotSupportedException>(() => CreateLoader(new NonSeekableStream(CreateFixture()))),
                "116-F1: DataLoader stream constructor preserves seekable-stream boundary");

            var invalidPath = TempInvalidMcap();
            try
            {
                Check(Throws<EndOfStreamException>(() => CreatePathLoader(invalidPath)),
                    "116-F2: truncated MCAP fails with EndOfStreamException through existing reader behavior");
            }
            finally
            {
                TryDelete(invalidPath);
            }
        }

        private static void VerifyValidationWiringAndDocs()
        {
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(validationRegistry.Contains("--phase116", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase116Validation.Validate", StringComparison.Ordinal),
                "116-G1: validation registry wires --phase116");
            Check(project.Contains("Phase116Validation.cs", StringComparison.Ordinal),
                "116-G2: runtime test project compiles Phase116Validation");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md")
                    .Contains("McapDataLoader", StringComparison.Ordinal),
                "116-G3: English MCAP docs describe local DataLoader v1");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/08_MCAP录制回放.md")
                    .Contains("McapDataLoader", StringComparison.Ordinal),
                "116-G4: Chinese MCAP docs describe local DataLoader v1");
        }

        private static MemoryStream CreateFixture(string foxRunMetadataJson = null, bool includeFoxRunMetadata = true)
        {
            if (includeFoxRunMetadata && foxRunMetadataJson == null)
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(CurrentHash), out foxRunMetadataJson);

            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase116/a", "json", "phase116.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase116/b", "json", "phase116.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(3, "/phase116/tie", "json", "phase116.Tie", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                recorder.WriteMessage(2, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                recorder.WriteMessage(1, 50, Encoding.UTF8.GetBytes("{\"a\":50}"));
                recorder.WriteMessage(3, 50, Encoding.UTF8.GetBytes("{\"tie\":50}"));
                recorder.WriteMessage(2, 60, Encoding.UTF8.GetBytes("{\"b\":60}"));
                recorder.WriteMessage(3, 80, Encoding.UTF8.GetBytes("{\"tie\":80a}"));
                recorder.WriteMessage(3, 80, Encoding.UTF8.GetBytes("{\"tie\":80b}"));
                recorder.AddAttachment("phase116.txt", "text/plain", Encoding.UTF8.GetBytes("phase116"), 75);
                if (includeFoxRunMetadata)
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, foxRunMetadataJson);
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static FoxRunSchemaManifestInfo CreateSchemaInfo(string hash)
            => new FoxRunSchemaManifestInfo(
                1,
                "dev.unity2foxglove.sdk",
                "Phase116Fixture",
                1,
                hash,
                hash,
                new List<FoxRunSchemaTypeInfo>());

        private static object CreateLoader(Stream stream)
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            return Activator.CreateInstance(loader, stream, true);
        }

        private static object CreatePathLoader(string path)
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            return Activator.CreateInstance(loader, path);
        }

        private static string TempInvalidMcap()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase116-invalid-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllText(path, "not an mcap");
            return path;
        }

        private static Type RequiredType(string name)
        {
            var type = Type.GetType(name + ", FoxgloveSdk.Tests");
            Check(type != null, "116-type: required type exists: " + name);
            return type;
        }

        private static object New(string name)
            => Activator.CreateInstance(RequiredType(name));

        private static object Invoke(object target, string method, params object[] args)
        {
            var candidates = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
                .ToList();
            var methodInfo = candidates.SingleOrDefault(candidate => ParametersAssignable(candidate.GetParameters(), args));
            if (methodInfo == null)
                throw new MissingMethodException(target.GetType().FullName, method + "(" + args.Length + " args)");
            return methodInfo.Invoke(target, args);
        }

        private static bool ParametersAssignable(ParameterInfo[] parameters, object[] args)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                var arg = args[i];
                if (arg == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                        return false;
                    continue;
                }

                if (!parameterType.IsInstanceOfType(arg))
                    return false;
            }

            return true;
        }

        private static List<object> InvokeMessages(object loader, string method, object query)
            => ((IEnumerable)Invoke(loader, method, query)).Cast<object>().ToList();

        private static List<object> Problems(object initialization)
            => ((IEnumerable)Member(initialization, "Problems")).Cast<object>().ToList();

        private static object Member(object target, string name)
        {
            var type = target.GetType();
            var field = type.GetField(name);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(name);
            if (property != null)
                return property.GetValue(target);

            throw new MissingMemberException(type.FullName, name);
        }

        private static void Set(object target, string name, object value)
        {
            var type = target.GetType();
            var field = type.GetField(name);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var property = type.GetProperty(name);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            throw new MissingMemberException(type.FullName, name);
        }

        private static int Count(object enumerable)
            => ((IEnumerable)enumerable).Cast<object>().Count();

        private static bool BoolMember(object target, string name)
            => (bool)Member(target, name);

        private static uint UIntMember(object target, string name)
            => (uint)Member(target, name);

        private static ushort UShortMember(object target, string name)
            => (ushort)Member(target, name);

        private static ulong ULongMember(object target, string name)
            => (ulong)Member(target, name);

        private static string StringMember(object target, string name)
        {
            var value = Member(target, name);
            return value == null ? string.Empty : value.ToString();
        }

        private static void CheckTimes(List<object> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => ULongMember(m, "LogTime")).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static IEnumerable<string> ChannelTimes(List<object> messages)
            => messages.Select(m => UShortMember(m, "ChannelId") + ":" + ULongMember(m, "LogTime"));

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                return true;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for validation temp files.
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase116 file: " + relativePath, path);
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

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
