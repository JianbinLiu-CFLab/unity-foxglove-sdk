// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify callback lifetime isolation and bounded generated copies.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2Ownership")]
    public sealed class FoxRunRos2MessageOwnershipTests
    {
        [Fact]
        public void RawCallbackReferenceExpiresButGeneratedOwnedCopyRemainsReadable()
        {
            var rawSlot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            var callback = new FakeCallbackScope(new FakeMessage("callback", new[] { 1, 2, 3 }));
            Assert.True(rawSlot.TryPublish(() => callback.Message));
            callback.ReturnFromCallback();
            Assert.Throws<ObjectDisposedException>(() => rawSlot.TryApplyLatest(
                message => _ = message.Text,
                _ => false));
            rawSlot.Stop(_ => false);

            var ownedSlot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            callback = new FakeCallbackScope(new FakeMessage("owned", new[] { 4, 5, 6 }));
            var budget = new FoxRunRos2CopyBudget(64);
            Assert.True(ownedSlot.TryPublish(() => GeneratedCopy(callback.Message, budget)));
            callback.ReturnFromCallback();

            FakeMessage applied = null;
            Assert.True(ownedSlot.TryApplyLatest(message => applied = message, _ => false));
            Assert.Equal("owned", applied.Text);
            Assert.Equal(new[] { 4, 5, 6 }, applied.Values);
            Assert.Equal(22, budget.ConsumedBytes);
            ownedSlot.Stop(_ => false);
        }

        [Fact]
        public void CopyBudgetCountsManagedStringAndSequenceStorageOnly()
        {
            var budget = new FoxRunRos2CopyBudget(24);

            budget.RequireString("four");
            budget.RequireSequenceElements(4, sizeof(int));

            Assert.Equal(24, budget.ConsumedBytes);
            Assert.Equal(0, budget.RemainingBytes);
            Assert.Equal(24, budget.MaximumBytes);
            Assert.Throws<InvalidOperationException>(() => budget.RequireBytes(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.RequireSequenceElements(-1, sizeof(int)));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.RequireSequenceElements(1, -1));
        }

        [Fact]
        public void CopyFailureOrBudgetOverflowDoesNotPublishAnOwnedValue()
        {
            var slot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            Assert.False(slot.TryPublish(
                () => throw new InvalidOperationException("copy failed"),
                out var copyFailure));
            Assert.IsType<InvalidOperationException>(copyFailure);

            var callback = new FakeCallbackScope(new FakeMessage("too large", new[] { 1 }));
            Assert.False(slot.TryPublish(
                () => GeneratedCopy(callback.Message, new FoxRunRos2CopyBudget(1)),
                out var budgetFailure));
            Assert.IsType<InvalidOperationException>(budgetFailure);

            Assert.Equal(2, slot.ReceivedCount);
            Assert.Equal(2, slot.CopyFailedCount);
            Assert.Equal(0, slot.PendingCount);
            Assert.Equal(0, slot.ReplacedCount);
        }

        [Fact]
        public void GeneratedCopyCleansPartiallyConstructedNestedGraphOnFailure()
        {
            NestedGraph partial = null;
            var slot = new FoxRunRos2OwnedLatestSlot<NestedGraph>(graph => graph.Dispose());

            Assert.False(slot.TryPublish(() =>
            {
                partial = new NestedGraph();
                try
                {
                    partial.Children.Add(new NestedChild(1));
                    partial.Children.Add(new NestedChild(2));
                    throw new InvalidOperationException("nested copy failed");
                }
                catch
                {
                    partial.Dispose();
                    throw;
                }
            }, out var failure));

            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(1, partial.DisposeCount);
            Assert.All(partial.Children, child => Assert.Equal(1, child.DisposeCount));
            Assert.Equal(1, slot.CopyFailedCount);
            slot.Stop(_ => false);
            Assert.Equal(1, partial.DisposeCount);
        }

        private static FakeMessage GeneratedCopy(FakeMessage source, FoxRunRos2CopyBudget budget)
        {
            var text = source.Text;
            var values = source.Values;
            budget.RequireString(text);
            budget.RequireSequenceElements(values.Length, sizeof(int));
            return new FakeMessage(text, (int[])values.Clone());
        }

        private sealed class FakeCallbackScope
        {
            public FakeCallbackScope(FakeMessage message) => Message = message;

            public FakeMessage Message { get; }

            public void ReturnFromCallback() => Message.InvalidateCallbackOwner();
        }

        private sealed class FakeMessage
        {
            private readonly string _text;
            private readonly int[] _values;
            private int _invalid;
            private int _disposeCount;

            public FakeMessage(string text, int[] values)
            {
                _text = text;
                _values = values;
            }

            public string Text
            {
                get
                {
                    ThrowIfInvalid();
                    return _text;
                }
            }

            public int[] Values
            {
                get
                {
                    ThrowIfInvalid();
                    return _values;
                }
            }

            public void InvalidateCallbackOwner() => Volatile.Write(ref _invalid, 1);

            public void Dispose() => Interlocked.Increment(ref _disposeCount);

            private void ThrowIfInvalid()
            {
                if (Volatile.Read(ref _invalid) != 0)
                    throw new ObjectDisposedException(nameof(FakeMessage), "Callback-owned message expired.");
            }
        }

        private sealed class NestedGraph
        {
            private int _disposeCount;

            public List<NestedChild> Children { get; } = new List<NestedChild>();
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) != 1)
                    throw new InvalidOperationException("Graph disposed more than once.");
                foreach (var child in Children)
                    child.Dispose();
            }
        }

        private sealed class NestedChild
        {
            private int _disposeCount;

            public NestedChild(int value) => Value = value;

            public int Value { get; }
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) != 1)
                    throw new InvalidOperationException("Child disposed more than once.");
            }
        }
    }
}
#endif
