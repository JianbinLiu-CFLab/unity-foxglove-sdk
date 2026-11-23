// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Behavior coverage for the source-text delegate-target manifest analyzer.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Unity2Foxglove.SourceTextDelegateTargets;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class DelegateTargetManifestAnalyzerTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "foxdt-root") + Path.DirectorySeparatorChar;
        private const string FixturePath = "Packages/fixture/Tests/Unit/Fixture.cs";

        private const string Prelude =
            "using System;\nusing System.Collections.Generic;\nusing System.Threading.Tasks;\n" +
            "namespace Fixture\n{\n    public delegate void ValidationDelegate();\n    public unsafe delegate void PointerDelegate(int* value);\n" +
            "    public interface IProbe { void Probe(); }\n" +
            "    public static class Holder { public static void Used() { } public static void Noop() { } }\n" +
            "    public static class Outer { public static class Inner { public static void Deep() { } } }\n" +
            "    public sealed class Probe : IProbe { void IProbe.Probe() { } }\n" +
            "    public static class Ext { public static void Twice(this string value) { } }\n";

        private static string Fixture(string members) =>
            Prelude +
            "    public static unsafe class FixtureValidation\n    {\n" +
            "        private static void Used() { }\n        private static void Used(int value) { }\n" +
            "        private static void Noop() { }\n        private static T Id<T>(T value) => value;\n" +
            "        private static int Sum((int, int) pair) => pair.Item1 + pair.Item2;\n" +
            "        private static void Arr(int[] values) { }\n        private static void Ptr(int* value) { }\n" +
            "        private static void Nullable(int? value) { }\n" +
            "        private static void Run(Action action) => action();\n        private static void Run(int value) { }\n" +
            "        private static void RunMany(Action action, int mode = 0, params int[] rest) => action();\n" +
            members + "\n    }\n}\n";

        [Fact]
        public void EveryMethodGroupConversionFormIsRecordedWithItsResolvedTarget()
        {
            var cases = new (string Members, string[] Targets)[]
            {
                ("public static void Validate() { Action a = Used; }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var a = (Action)Used; }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var a = (Action)Holder.Used; }", new[] { "M:Fixture.Holder.Used" }),
                ("public static void Validate() { var a = new Action(Used); }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { ValidationDelegate d = Used; }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var d = (ValidationDelegate)Used; var a = (Action)(Used); }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var f = (Func<int, int>)Id<int>; var s = (Func<(int, int), int>)Sum; }", new[] { "M:Fixture.FixtureValidation.Id``1(``0)~``0{System.Int32}", "M:Fixture.FixtureValidation.Sum(System.ValueTuple{System.Int32,System.Int32})~System.Int32" }),
                ("public static void Validate() { Action a = @Used; Action b = (Action)@Used; }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var many = new Action[] { Used, Noop }; }", new[] { "M:Fixture.FixtureValidation.Noop", "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { var many = new List<Action> { Used }; }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate(bool c) { Action a = c ? Used : Noop; }", new[] { "M:Fixture.FixtureValidation.Noop", "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { Run(action: Used); RunMany(Used); Task.Run(Used); }", new[] { "M:Fixture.FixtureValidation.Used" }),
                ("public static void Validate() { void Local() { } Action a = Local; }", new[] { "L:M:Fixture.FixtureValidation.Validate|Local" }),
                ("public static void Validate() { Action a = Outer.Inner.Deep; IProbe p = new Probe(); Action b = p.Probe; }", new[] { "M:Fixture.IProbe.Probe", "M:Fixture.Outer.Inner.Deep" }),
                ("public static void Validate() { Action a = \"x\".Twice; }", new[] { "M:Fixture.Ext.Twice(System.String)" }),
            };
            foreach (var (members, targets) in cases)
            {
                var lines = EntryLines(members);
                Assert.Equal(targets, lines.Select(line => line.Split('\t')[3]).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray());
                Assert.All(lines, line => Assert.StartsWith("unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate", line));
            }
        }

        [Fact]
        public void TargetIdentityIsDistinctForEveryOverloadAndShape()
        {
            var lines = EntryLines(
                "public static void Validate() {\n" +
                "    Action a = Used; Action<int> b = Used; Func<int, int> c = Id<int>; Func<string, string> d = Id<string>;\n" +
                "    Func<(int, int), int> e = Sum; Action<int[]> f = Arr; Action<int?> g = Nullable;\n" +
                "    PointerDelegate h = Ptr; }");
            var targets = lines.Select(line => line.Split('\t')[3]).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            var expected = new[]
            {
                "M:Fixture.FixtureValidation.Arr(System.Int32[])",
                "M:Fixture.FixtureValidation.Id``1(``0)~``0{System.Int32}",
                "M:Fixture.FixtureValidation.Id``1(``0)~``0{System.String}",
                "M:Fixture.FixtureValidation.Nullable(System.Nullable{System.Int32})",
                "M:Fixture.FixtureValidation.Ptr(System.Int32*)",
                "M:Fixture.FixtureValidation.Sum(System.ValueTuple{System.Int32,System.Int32})~System.Int32",
                "M:Fixture.FixtureValidation.Used",
                "M:Fixture.FixtureValidation.Used(System.Int32)",
            };
            Assert.Equal(string.Join("\n", expected), string.Join("\n", targets));
        }

        [Fact]
        public void NonConversionsAndOutOfContractFormsProduceNoEntry()
        {
            var members =
                "private static int Foo() => 1;\n" +
                "public static void Validate(int mode = 0) {\n" +
                "    Run(1); var c = Foo(); Action a = () => Used(); Action b = delegate { Noop(); };\n" +
                "    typeof(FixtureValidation).GetMethod(\"Noop\").Invoke(null, null);\n" +
                "    var d = Delegate.CreateDelegate(typeof(Action), typeof(FixtureValidation).GetMethod(\"Noop\")); }";
            Assert.Empty(EntryLines(members));
        }

        [Fact]
        public void FilesOutsideTheValidationScopeAreIgnored()
        {
            var result = Run(new[] { ("Packages/fixture/Runtime/Product.cs", Fixture("public static void Validate() { Action a = Used; }")) }, manifest: "", emit: true);
            Assert.DoesNotContain(result, diagnostic => diagnostic.Id == "FOXDT900");
            Assert.DoesNotContain(result, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void RuleEAcceptsAnExactManifestAndRejectsEveryMismatch()
        {
            var source = Fixture("public static void Validate() { Action a = Used; Action b = Used; Action c = Noop; }");
            var used = "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Used\t2";
            var noop = "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Noop\t1";
            var exact = Manifest(noop, used);
            Assert.DoesNotContain(Run(new[] { (FixturePath, source) }, exact), IsError);

            var cases = new Dictionary<string, string>
            {
                ["missing entry"] = Manifest(used),
                ["extra entry"] = Manifest(noop, used, "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.Holder.Used\t1"),
                ["changed count"] = Manifest(noop, used.Replace("\t2", "\t1")),
                ["stale file"] = Manifest(noop, used, "unit\tPackages/fixture/Tests/Unit/Gone.cs\tM:Fixture.Gone.Validate\tM:Fixture.Gone.Used\t1"),
            };
            foreach (var pair in cases)
            {
                Assert.True(Run(new[] { (FixturePath, source) }, pair.Value).Any(d => d.Id == "FOXDT001"), pair.Key);
            }
            Assert.Contains(Run(new[] { (FixturePath, source) }, manifest: null), d => d.Id == "FOXDT001");
        }

        [Fact]
        public void RuleEComparesOnlyTheEntriesOfTheCompilingVariant()
        {
            var source = Fixture(
                "public static void Validate() {\n#if MANAGER_PRODUCTION_BOUNDARY\n    Action a = Noop;\n#else\n    Action a = Used;\n#endif\n}");
            var manifest = Manifest(
                "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Used\t1",
                "unit-manager\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Noop\t1");
            Assert.DoesNotContain(Run(new[] { (FixturePath, source) }, manifest, variant: "unit"), IsError);
            Assert.DoesNotContain(Run(new[] { (FixturePath, source) }, manifest, variant: "unit-manager", symbols: new[] { "MANAGER_PRODUCTION_BOUNDARY" }), IsError);
        }

        [Fact]
        public void IdentityProblemsFailWithFoxdt002()
        {
            var source = Fixture("public static void Validate() { }");
            var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Packages", "fixture", "Tests", "Unit", "Fixture.cs");
            Assert.Contains(Run(new[] { (FixturePath, source) }, "", root: null), d => d.Id == "FOXDT002");
            Assert.Contains(Run(new[] { (FixturePath, source) }, "", variant: "runtime-native"), d => d.Id == "FOXDT002");
            Assert.Contains(Run(new[] { (FixturePath, source) }, "", variant: null), d => d.Id == "FOXDT002");
            Assert.Contains(Run(new[] { (FixturePath, source), (FixturePath, source) }, ""), d => d.Id == "FOXDT002");
            Assert.Contains(RunAbsolute(new[] { (outside, source) }, ""), d => d.Id == "FOXDT002");
        }

        [Fact]
        public void EmitModeReportsEntriesWithoutComparingTheManifest()
        {
            var result = Run(new[] { (FixturePath, Fixture("public static void Validate() { Action a = Used; }")) }, manifest: null, emit: true);
            Assert.DoesNotContain(result, d => d.Id == "FOXDT001");
            Assert.Contains(result, d => d.Id == "FOXDT900"
                && d.GetMessage() == "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Used\t1");
        }

        [Fact]
        public void MalformedManifestsFailClosedWithFoxdt003()
        {
            var source = Fixture("public static void Validate() { Action a = Used; }");
            var good = "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Used\t1";
            var other = "unit\t" + FixturePath + "\tM:Fixture.FixtureValidation.Zeta\tM:Fixture.FixtureValidation.Used\t1";
            var cases = new Dictionary<string, string>
            {
                ["wrong column count"] = good.Replace("\t1", "") + "\n",
                ["empty field"] = good.Replace("\tM:Fixture.FixtureValidation.Validate", "\t") + "\n",
                ["zero count"] = good.Replace("\t1", "\t0") + "\n",
                ["negative count"] = good.Replace("\t1", "\t-1") + "\n",
                ["leading zero count"] = good.Replace("\t1", "\t01") + "\n",
                ["non-numeric count"] = good.Replace("\t1", "\tx") + "\n",
                ["unknown variant"] = good.Replace("unit\t", "runtime-native\t") + "\n",
                ["malformed member"] = good.Replace("\tM:Fixture.FixtureValidation.Validate", "\tValidate") + "\n",
                ["malformed target"] = good.Replace("\tM:Fixture.FixtureValidation.Used", "\tUsed") + "\n",
                ["duplicate key"] = good + "\n" + good + "\n",
                ["unsorted"] = other + "\n" + good + "\n",
                ["crlf"] = good + "\r\n",
                ["missing final lf"] = good,
            };
            foreach (var pair in cases)
            {
                Assert.True(Run(new[] { (FixturePath, source) }, pair.Value).Any(d => d.Id == "FOXDT003"), pair.Key);
            }
            Assert.DoesNotContain(Run(new[] { (FixturePath, source) }, good + "\n"), IsError);
        }

        private static bool IsError(Diagnostic diagnostic) => diagnostic.Severity == DiagnosticSeverity.Error;

        private static string Manifest(params string[] lines) =>
            string.Concat(lines.OrderBy(line => line, StringComparer.Ordinal).Select(line => line + "\n"));

        private static string[] EntryLines(string members) =>
            Run(new[] { (FixturePath, Fixture(members)) }, manifest: null, emit: true)
                .Where(d => d.Id == "FOXDT900")
                .Select(d => d.GetMessage())
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();

        private static ImmutableArray<Diagnostic> Run(
            (string Path, string Text)[] sources,
            string manifest,
            string variant = "unit",
            bool emit = false,
            string root = "<default>",
            string[] symbols = null) =>
            RunAbsolute(
                sources.Select(source => (Path.Combine(Root, source.Path.Replace('/', Path.DirectorySeparatorChar)), source.Text)).ToArray(),
                manifest, variant, emit, root == "<default>" ? Root : root, symbols);

        private static ImmutableArray<Diagnostic> RunAbsolute(
            (string Path, string Text)[] sources,
            string manifest,
            string variant = "unit",
            bool emit = false,
            string root = "<default>",
            string[] symbols = null)
        {
            var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols ?? Array.Empty<string>());
            var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, parse, source.Path)).ToArray();
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "DelegateTargetFixture", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var properties = new Dictionary<string, string>();
            if (root != null)
            {
                properties["build_property.FoxDelegateTargetsRepoRoot"] = root == "<default>" ? Root : root;
            }
            if (variant != null)
            {
                properties["build_property.FoxDelegateTargetsVariant"] = variant;
            }
            properties["build_property.FoxDelegateTargetsEmit"] = emit ? "true" : "false";
            var additional = manifest == null
                ? ImmutableArray<AdditionalText>.Empty
                : ImmutableArray.Create<AdditionalText>(new ManifestText(Path.Combine(Root, "Scripts", "validation", "source_text_delegate_targets.tsv"), manifest));
            var options = new AnalyzerOptions(additional, new GlobalOptionsProvider(properties));
            return compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DelegateTargetManifestAnalyzer()), options)
                .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private sealed class ManifestText : AdditionalText
        {
            private readonly string _text;

            public ManifestText(string path, string text)
            {
                Path = path;
                _text = text;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(_text);
        }

        private sealed class GlobalOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _global;

            public GlobalOptionsProvider(Dictionary<string, string> values) => _global = new DictionaryOptions(values);

            public override AnalyzerConfigOptions GlobalOptions => _global;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => DictionaryOptions.Empty;
        }

        private sealed class DictionaryOptions : AnalyzerConfigOptions
        {
            public static readonly DictionaryOptions Empty = new DictionaryOptions(new Dictionary<string, string>());
            private readonly Dictionary<string, string> _values;

            public DictionaryOptions(Dictionary<string, string> values) => _values = values;

            public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value);
        }
    }
}
