// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins syntax-safe chunking for generated FoxRun descriptor carriers.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunDescriptorCarrierEmitterTests
    {
        [Fact]
        public void ChunkedDescriptorCarrierDoesNotSplitAStringEscape()
        {
            var escapedDescriptorJson = new string('x', 15999) + "\\\"tail";
            var source = FoxRunDescriptorCarrierEmitter.ChunkedDescriptorCarrierSource(escapedDescriptorJson);

            var errors = CSharpSyntaxTree.ParseText(source)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("\u0001")]
        [InlineData("\U0001F600")]
        public void DescriptorCarrierDoesNotSplitUnicodeEscapes(string unicodeValue)
        {
            var descriptorJson = new string('x', 15998) + unicodeValue + new string('y', 44000);
            var source = FoxRunDescriptorCarrierEmitter.DescriptorCarrierSource(descriptorJson);

            var errors = CSharpSyntaxTree.ParseText(source)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Empty(errors);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void DescriptorCarrierPreservesTheSharedMessagePackSpelling()
        {
            const string descriptorJson =
                "{\"descriptorVersion\":5,\"generatorVersion\":\"5.0.0\",\"encoding\":\"msgpack\"}";

            var source = FoxRunDescriptorCarrierEmitter.DescriptorCarrierSource(descriptorJson);

            Assert.Contains("\\\"encoding\\\":\\\"msgpack\\\"", source, StringComparison.Ordinal);
        }
    }
}
