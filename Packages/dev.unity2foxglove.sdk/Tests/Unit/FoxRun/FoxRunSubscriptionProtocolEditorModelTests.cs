// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins the Inspector-only protocol choice to independent provider and wire fields.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "179-D")]
    [Trait("Domain", "FoxRunSubscriptionInspector")]
    public sealed class FoxRunSubscriptionProtocolEditorModelTests
    {
        [Theory]
        [InlineData(
            FoxRunSubscriptionProtocolEditorModel.WebSocketProtobuf,
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunWireEncoding.Protobuf)]
        [InlineData(
            FoxRunSubscriptionProtocolEditorModel.WebSocketJson,
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunWireEncoding.Json)]
        public void WebSocketSelectionsWriteBothIndependentFields(
            int selection,
            FoxRunSubscriptionProvider expectedProvider,
            FoxRunWireEncoding expectedEncoding)
        {
            var provider = FoxRunSubscriptionProvider.Ros2Native;
            var encoding = FoxRunWireEncoding.Json;

            FoxRunSubscriptionProtocolEditorModel.ApplySelection(selection, ref provider, ref encoding);

            Assert.Equal(expectedProvider, provider);
            Assert.Equal(expectedEncoding, encoding);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Protobuf)]
        [InlineData(FoxRunWireEncoding.Json)]
        public void NativeSelectionPreservesTheStoredWebSocketEncoding(FoxRunWireEncoding storedEncoding)
        {
            var provider = FoxRunSubscriptionProvider.FoxgloveWebSocket;
            var encoding = storedEncoding;

            FoxRunSubscriptionProtocolEditorModel.ApplySelection(
                FoxRunSubscriptionProtocolEditorModel.Ros2Native,
                ref provider,
                ref encoding);

            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, provider);
            Assert.Equal(storedEncoding, encoding);
        }

        [Fact]
        public void SelectionNormalizesLegacyAndInvalidManagerValuesToWebSocketProtobuf()
        {
            var provider = FoxRunSubscriptionProvider.Inherit;
            var encoding = FoxRunWireEncoding.Inherit;

            var selection = FoxRunSubscriptionProtocolEditorModel.NormalizeForDrawing(
                ref provider,
                ref encoding);

            Assert.Equal(FoxRunSubscriptionProtocolEditorModel.WebSocketProtobuf, selection);
            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, provider);
            Assert.Equal(FoxRunWireEncoding.Protobuf, encoding);
        }

        [Fact]
        public void Ros2NativeNormalizesOnlyTheProviderAndLeavesStoredWireChoiceUntouched()
        {
            var provider = FoxRunSubscriptionProvider.Ros2Native;
            var encoding = FoxRunWireEncoding.Json;

            var selection = FoxRunSubscriptionProtocolEditorModel.NormalizeForDrawing(
                ref provider,
                ref encoding);

            Assert.Equal(FoxRunSubscriptionProtocolEditorModel.Ros2Native, selection);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, provider);
            Assert.Equal(FoxRunWireEncoding.Json, encoding);
        }
    }
}
