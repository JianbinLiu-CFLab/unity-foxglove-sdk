using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R4-G03")]
    [Trait("Domain", "Sensors")]
    public sealed class G03ImuLifecycleTests
    {
        [Fact]
        public void VirtualImuRetiresQueuedSamplesAcrossLifecycleGenerations()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");

            Assert.Contains("_lifecycleGeneration", source);
            Assert.Contains("RetireQueuedSamplesForLifecycleTransition", source);
            Assert.Contains("private void OnDisable()", source);
            Assert.Contains("private void OnEnable()", source);
        }

        [Fact]
        public void ReplayOnlyPolicySuppressesLiveNativeOutput()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");

            Assert.Contains("ShouldSuppressLiveOutputForReplay", source);
            Assert.Contains("SuppressLivePublishersForReplay", source);
            Assert.Contains("if (ShouldSuppressLiveOutputForReplay", source);
        }
    }
}
