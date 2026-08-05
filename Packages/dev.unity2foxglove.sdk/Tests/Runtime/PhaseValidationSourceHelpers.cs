// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationSourceHelpers
    {
        public static string FindRequiredRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root for source validation.");
            return root;
        }

        public static string RepoPath(string relativePath)
        {
            var root = FindRequiredRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return path;
        }

        public static string ReadRequiredRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        public static string ReadCameraPublisherSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Camera publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMediaFoundationH264EncoderSidecarSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Video");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Media Foundation H.264 sidecar directory was not found.");

            var main = Path.Combine(dir, "MediaFoundationH264EncoderSidecar.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing Media Foundation H.264 sidecar facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "MediaFoundationH264EncoderSidecar.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveServiceHubSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "FoxService");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveServiceHub directory was not found.");

            var main = Path.Combine(dir, "FoxgloveServiceHub.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing FoxgloveServiceHub facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "FoxgloveServiceHub.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadReplayControllerSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Replay");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Replay controller directory was not found.");

            var files = Directory.GetFiles(dir, "ReplayController*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveRuntimeSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Runtime");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveRuntime directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveRuntime*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMcapRecorderSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Recording");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("MCAP recorder directory was not found.");

            var files = Directory.GetFiles(dir, "McapRecorder*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMcapDataLoaderSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "DataLoader");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("MCAP DataLoader directory was not found.");

            var main = Path.Combine(dir, "McapDataLoader.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing MCAP DataLoader facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "McapDataLoader.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadRemoteMcapHttpRouterSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Remote");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Remote MCAP router directory was not found.");

            var main = Path.Combine(dir, "RemoteMcapHttpRouter.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing Remote MCAP router facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "RemoteMcapHttpRouter.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveLogSourceGeneratorSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Source generator src directory was not found.");

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerEditorSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManagerEditor directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerPublishingSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManager publishing directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManager.Publishing*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerServerSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManager server directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManager.Server*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static bool SourceMethodContains(string source, string methodName, string needle)
            => SourceMethod(source, methodName).Contains(needle, StringComparison.Ordinal);

        public static int InvocationCountInMethod(
            string source,
            string methodName,
            string invocationName)
            => QualifiedInvocationCountInMethod(
                source,
                methodName,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCountInMethod(
            string source,
            string methodName,
            string receiverName,
            string invocationName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    string.Equals(
                        method.Identifier.ValueText,
                        methodName,
                        StringComparison.Ordinal))
                .ToArray();
            if (methods.Length != 1)
                return -1;

            return methods[0]
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));
        }

        public static int InvocationCount(
            string source,
            string invocationName)
            => QualifiedInvocationCount(
                source,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCount(
            string source,
            string receiverName,
            string invocationName)
            => CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));

        public static bool TypeHasAttribute(
            string source,
            string typeName,
            string attributeName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException(
                    "Type name cannot be empty.",
                    nameof(typeName));
            if (string.IsNullOrWhiteSpace(attributeName))
                throw new ArgumentException(
                    "Attribute name cannot be empty.",
                    nameof(attributeName));

            var shortName = attributeName.EndsWith(
                "Attribute",
                StringComparison.Ordinal)
                ? attributeName.Substring(
                    0,
                    attributeName.Length - "Attribute".Length)
                : attributeName;
            var fullName = shortName + "Attribute";
            return CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(type =>
                    string.Equals(
                        type.Identifier.ValueText,
                        typeName,
                        StringComparison.Ordinal))
                .SelectMany(type =>
                    type.AttributeLists)
                .SelectMany(list => list.Attributes)
                .Any(attribute =>
                {
                    var identifier = attribute.Name
                        .DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .LastOrDefault()
                        ?.Identifier.ValueText;
                    return string.Equals(
                               identifier,
                               shortName,
                               StringComparison.Ordinal)
                           || string.Equals(
                               identifier,
                               fullName,
                               StringComparison.Ordinal);
                });
        }

        public static string SourceMethod(string source, string methodName)
            => SourceDeclaration(
                source,
                methodName,
                IsSourceMethodDeclaration,
                CSharpParseOptions.Default);

        public static string SourceMethodWithPreprocessorSymbols(
            string source,
            string methodName,
            params string[] preprocessorSymbols)
        {
            if (preprocessorSymbols == null || preprocessorSymbols.Length == 0)
                return string.Empty;

            return SourceDeclaration(
                source,
                methodName,
                IsSourceMethodDeclaration,
                CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols));
        }

        public static string SourceType(string source, string typeName)
            => SourceDeclaration(
                source,
                typeName,
                node => node is TypeDeclarationSyntax,
                CSharpParseOptions.Default);

        public static string SourceProperty(string source, string propertyName)
            => SourceDeclaration(
                source,
                propertyName,
                node => node is PropertyDeclarationSyntax,
                CSharpParseOptions.Default);

        private static string SourceDeclaration(
            string source,
            string requestedDeclaration,
            Func<SyntaxNode, bool> declarationFilter,
            CSharpParseOptions parseOptions)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(requestedDeclaration))
                return string.Empty;

            var matches = CSharpSyntaxTree.ParseText(source, parseOptions)
                .GetRoot()
                .DescendantNodes()
                .Where(declarationFilter)
                .Where(declaration => !declaration.ContainsDiagnostics)
                .Where(declaration => SourceDeclarationMatches(source, declaration, requestedDeclaration))
                .ToArray();
            if (matches.Length != 1)
                return string.Empty;

            var match = matches[0];
            return source.Substring(match.SpanStart, match.Span.Length);
        }

        private static bool IsSourceMethodDeclaration(SyntaxNode node)
            => node is MethodDeclarationSyntax
               || node is ConstructorDeclarationSyntax
               || node is LocalFunctionStatementSyntax;

        private static bool SourceDeclarationMatches(
            string source,
            SyntaxNode declaration,
            string requestedDeclaration)
        {
            var identifier = declaration switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
                TypeDeclarationSyntax type => type.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                _ => string.Empty
            };

            if (SyntaxFacts.IsValidIdentifier(requestedDeclaration))
                return string.Equals(identifier, requestedDeclaration, StringComparison.Ordinal);
            if (!ContainsIdentifierToken(requestedDeclaration, identifier))
                return false;

            var headerEnd = declaration switch
            {
                MethodDeclarationSyntax method => SourceMethodHeaderEnd(
                    method.Body?.OpenBraceToken.SpanStart,
                    method.ExpressionBody?.ArrowToken.SpanStart,
                    method.SemicolonToken.SpanStart),
                ConstructorDeclarationSyntax constructor => SourceMethodHeaderEnd(
                    constructor.Body?.OpenBraceToken.SpanStart,
                    constructor.ExpressionBody?.ArrowToken.SpanStart,
                    constructor.SemicolonToken.SpanStart),
                LocalFunctionStatementSyntax localFunction => SourceMethodHeaderEnd(
                    localFunction.Body?.OpenBraceToken.SpanStart,
                    localFunction.ExpressionBody?.ArrowToken.SpanStart,
                    localFunction.SemicolonToken.SpanStart),
                TypeDeclarationSyntax type => type.OpenBraceToken.SpanStart,
                PropertyDeclarationSyntax property => SourceMethodHeaderEnd(
                    property.AccessorList?.OpenBraceToken.SpanStart,
                    property.ExpressionBody?.ArrowToken.SpanStart,
                    property.SemicolonToken.SpanStart),
                _ => -1
            };
            if (headerEnd < declaration.SpanStart)
                return false;

            var header = source.Substring(declaration.SpanStart, headerEnd - declaration.SpanStart);
            return CollapseSourceWhitespace(header)
                .Contains(CollapseSourceWhitespace(requestedDeclaration), StringComparison.Ordinal);
        }

        private static int SourceMethodHeaderEnd(int? bodyStart, int? expressionBodyStart, int semicolonStart)
            => bodyStart ?? expressionBodyStart ?? semicolonStart;

        private static string CollapseSourceWhitespace(string value)
        {
            var result = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var current in value)
            {
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                    result.Append(' ');
                result.Append(current);
                pendingSpace = false;
            }

            return result.ToString();
        }

        private static bool ContainsIdentifierToken(string value, string identifier)
        {
            var offset = 0;
            while (offset < value.Length)
            {
                var index = value.IndexOf(identifier, offset, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                var beforeIsIdentifier = index > 0
                                         && SyntaxFacts.IsIdentifierPartCharacter(value[index - 1]);
                var afterIndex = index + identifier.Length;
                var afterIsIdentifier = afterIndex < value.Length
                                        && SyntaxFacts.IsIdentifierPartCharacter(value[afterIndex]);
                if (!beforeIsIdentifier && !afterIsIdentifier)
                    return true;

                offset = index + identifier.Length;
            }

            return false;
        }

        private static bool InvocationMatches(
            InvocationExpressionSyntax invocation,
            string receiverName,
            string invocationName)
        {
            if (invocation?.Expression
                is IdentifierNameSyntax identifier)
            {
                return receiverName == null
                       && identifier.Identifier.ValueText
                       == invocationName;
            }

            if (invocation?.Expression
                is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            return memberAccess.Name.Identifier.ValueText
                   == invocationName
                   && (receiverName == null
                       || string.Equals(
                           memberAccess.Expression.ToString(),
                           receiverName,
                           StringComparison.Ordinal));
        }

    }
}
