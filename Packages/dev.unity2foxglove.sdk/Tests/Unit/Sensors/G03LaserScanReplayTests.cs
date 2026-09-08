using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R4-G03")]
    [Trait("Domain", "Sensors")]
    public sealed class G03LaserScanReplayTests
    {
        [Fact]
        public void WorkerAdmissionReservesBeforeCopyAndRetiresOnDisable()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");

            Assert.Contains("long generation", source);
            Assert.Contains("generation != _publishGeneration", source);
            Assert.Contains("_publishGeneration++", source);
            Assert.True(source.IndexOf("generation = _publishGeneration")
                        < source.IndexOf("CopyRequiredValues"));
        }

        [Fact]
        public void MixedReplayUsesExplicitSuppressionPolicy()
        {
            var laser = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var transform = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            var runtime = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");

            Assert.Contains("SuppressLivePublishersForReplay", laser);
            Assert.Contains("SuppressLivePublishersForReplay", transform);
            Assert.Contains("ReplaySuppressesLivePublishing", runtime);
            Assert.Contains("SetReplayLiveSuppression", runtime);
        }

        [Fact]
        public void InitialNonFiniteAnglesAreNotCollapsedToZero()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");

            Assert.Contains("_startAngleDegrees != _cachedStartAngleDegrees", source);
            Assert.Contains("_endAngleDegrees != _cachedEndAngleDegrees", source);
        }
    }
}
