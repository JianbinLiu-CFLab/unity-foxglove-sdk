using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherChannelContractGuardTests
    {
        [Fact]
        public void SameDescriptorIsIdempotentAndConflictDoesNotAddClaim()
        {
            var guard = new ComponentPublisherChannelContractGuard();
            var a = new ComponentPublisherChannelDescriptor("typed", "/telemetry", PublisherEffectiveEncoding.MsgPack, "demo.Telemetry", "shape.v1", 3);
            Assert.True(guard.TryClaim(a, out _));
            Assert.True(guard.TryClaim(a, out _));
            Assert.Equal(1, guard.Count);
            var conflicting = new ComponentPublisherChannelDescriptor("raw", "/telemetry", PublisherEffectiveEncoding.Json, "", "", 3);
            Assert.False(guard.TryClaim(conflicting, out var reason));
            Assert.Contains("conflict", reason);
            Assert.Equal(1, guard.Count);
        }
        [Fact]
        public void ClearRemovesSidecarsWithoutOwningChannelIds()
        {
            var guard = new ComponentPublisherChannelContractGuard();
            Assert.True(guard.TryClaim(new ComponentPublisherChannelDescriptor("typed", "/a", PublisherEffectiveEncoding.Json, "s", "", 1), out _));
            guard.Clear();
            Assert.Equal(0, guard.Count);
        }
    }

    public sealed class TopicContractLedgerTests
    {
        [Fact]
        public void MatchingClaimsAreIdempotentAndDoNotCreateConflicts()
        {
            var ledger = new TopicContractLedger();
            var contract = new TopicContract("/imu", "foxglove.Imu", "protobuf", "VirtualImu");

            var first = ledger.Register(contract);
            var second = ledger.Register(contract);

            Assert.False(first.IsConflict);
            Assert.False(second.IsConflict);
            Assert.False(first.ShouldWarn);
            Assert.False(second.ShouldWarn);
            Assert.Equal(1, ledger.ClaimCount);
            Assert.Equal(0, ledger.ConflictCount);
        }

        [Fact]
        public void MatchingWireContractFromAnotherOwnerIsNotConflict()
        {
            var ledger = new TopicContractLedger();
            ledger.Register(new TopicContract("/imu", "foxglove.Imu", "protobuf", "VirtualImu"));

            var result = ledger.Register(
                new TopicContract("/imu", "foxglove.Imu", "protobuf", "component"));

            Assert.False(result.IsConflict);
            Assert.False(result.ShouldWarn);
            Assert.Equal(1, ledger.ClaimCount);
            Assert.Equal(0, ledger.ConflictCount);
        }

        [Fact]
        public void RecordingAndLiveContractsUseSeparateScopes()
        {
            var ledger = new TopicContractLedger();
            ledger.Register(new TopicContract(
                "/camera", "", "json", "FoxRun", TopicContractScope.Live));

            var recording = ledger.Register(new TopicContract(
                "/camera", "", "msgpack", "FoxRun", TopicContractScope.Recording));

            Assert.False(recording.IsConflict);
            Assert.False(recording.ShouldWarn);
            Assert.Equal(2, ledger.ClaimCount);
            Assert.Equal(0, ledger.ConflictCount);
        }

        [Fact]
        public void ConflictingPairIsCountedAndWarnedOnlyOnce()
        {
            var ledger = new TopicContractLedger();
            var first = new TopicContract("/imu", "foxglove.Imu", "protobuf", "VirtualImu");
            var conflicting = new TopicContract("/imu", "foxglove.Imu", "msgpack", "component");

            ledger.Register(first);
            var firstConflict = ledger.Register(conflicting);
            var repeatedConflict = ledger.Register(conflicting);

            Assert.True(firstConflict.IsConflict);
            Assert.True(firstConflict.ShouldWarn);
            Assert.True(repeatedConflict.IsConflict);
            Assert.False(repeatedConflict.ShouldWarn);
            Assert.Equal(1, ledger.ClaimCount);
            Assert.Equal(1, ledger.ConflictCount);
            Assert.Equal(first, firstConflict.Existing);
            Assert.Equal(conflicting, firstConflict.Incoming);
        }

        [Fact]
        public void ClearRemovesClaimsAndConflictHistory()
        {
            var ledger = new TopicContractLedger();
            ledger.Register(new TopicContract("/imu", "foxglove.Imu", "protobuf", "VirtualImu"));
            ledger.Register(new TopicContract("/imu", "foxglove.Other", "protobuf", "component"));

            ledger.Clear();

            var result = ledger.Register(new TopicContract("/imu", "foxglove.Other", "protobuf", "component"));
            Assert.False(result.IsConflict);
            Assert.True(result.IsNewClaim);
            Assert.Equal(1, ledger.ClaimCount);
            Assert.Equal(0, ledger.ConflictCount);
        }
    }
}
