// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Shared source fixtures for runtime validation optimization tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    internal static partial class TestSources
    {
        public static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(index >= 0, "Could not locate method signature: " + signature);
            var brace = source.IndexOf('{', index);
            Assert.True(brace >= 0, "Could not locate method body: " + signature);

            var depth = 0;
            var inString = false;
            var inVerbatimString = false;
            var inCharacter = false;
            var inLineComment = false;
            var inBlockComment = false;
            var inBacktickString = false;
            var rawStringDelimiterLength = 0;
            for (var i = brace; i < source.Length; i++)
            {
                var current = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (current == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (rawStringDelimiterLength > 0)
                {
                    if (current == '"' && CountRun(source, i, '"') >= rawStringDelimiterLength)
                    {
                        i += rawStringDelimiterLength - 1;
                        rawStringDelimiterLength = 0;
                    }
                    continue;
                }

                if (inString)
                {
                    if (inVerbatimString && current == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"' && (inVerbatimString || !IsEscaped(source, i)))
                    {
                        inString = false;
                        inVerbatimString = false;
                    }
                    continue;
                }

                if (inCharacter)
                {
                    if (current == '\'' && !IsEscaped(source, i))
                        inCharacter = false;
                    continue;
                }

                if (inBacktickString)
                {
                    if (current == '`' && !IsEscaped(source, i))
                        inBacktickString = false;
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    var quoteRun = CountRun(source, i, '"');
                    if (quoteRun >= 3)
                    {
                        rawStringDelimiterLength = quoteRun;
                        i += quoteRun - 1;
                    }
                    else
                    {
                        inString = true;
                        inVerbatimString = i > 0 && source[i - 1] == '@';
                    }
                    continue;
                }

                if (current == '\'')
                {
                    inCharacter = true;
                    continue;
                }

                if (current == '`')
                {
                    inBacktickString = true;
                    continue;
                }

                if (current == '{') depth++;
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static int CountRun(string source, int index, char value)
        {
            var count = 0;
            while (index + count < source.Length && source[index + count] == value)
                count++;
            return count;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        public static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string NormalizeLineEndings(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        internal static string RepoRoot
            => CachedRepoRoot;

    }
}
