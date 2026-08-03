// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Regression guard for non-zero FoxRunEncoding values in Unity Inspector popups.

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class FoxRunEncodingInspectorTests
    {
        [Fact]
        public void ManagerPopupMapsSerializedEnumValuesInsteadOfEnumNameIndices()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEncodingEditorLabels.cs");
            var draw = TestSources.ExtractMethod(
                source,
                "public static void DrawFoxRunEncoding");

            Assert.Contains("var selected = property.intValue switch", draw, StringComparison.Ordinal);
            Assert.Contains("(int)FoxRunEncoding.Protobuf => 0", draw, StringComparison.Ordinal);
            Assert.Contains("(int)FoxRunEncoding.JSON => 1", draw, StringComparison.Ordinal);
            Assert.Contains("(int)FoxRunEncoding.MessagePack => 2", draw, StringComparison.Ordinal);
            Assert.Contains("property.intValue = selected switch", draw, StringComparison.Ordinal);
            Assert.Contains("0 => (int)FoxRunEncoding.Protobuf", draw, StringComparison.Ordinal);
            Assert.Contains("1 => (int)FoxRunEncoding.JSON", draw, StringComparison.Ordinal);
            Assert.Contains("2 => (int)FoxRunEncoding.MessagePack", draw, StringComparison.Ordinal);
            Assert.DoesNotContain("enumValueIndex", draw, StringComparison.Ordinal);
        }
    }
}
