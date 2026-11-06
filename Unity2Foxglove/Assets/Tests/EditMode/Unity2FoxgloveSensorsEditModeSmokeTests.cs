using NUnit.Framework;

namespace Unity2Foxglove.Tests.EditMode
{
    public sealed class Unity2FoxgloveSensorsEditModeSmokeTests
    {
        [Test]
        public void LidarScanTimingConvertsNormalizedOffset()
        {
            Assert.That(
                Unity.FoxgloveSDK.Sensors.Lidar.LidarScanTiming.NormalizedOffsetToSeconds(0.5f, 10d),
                Is.EqualTo(0.05f).Within(1e-6f));
        }
    }
}
