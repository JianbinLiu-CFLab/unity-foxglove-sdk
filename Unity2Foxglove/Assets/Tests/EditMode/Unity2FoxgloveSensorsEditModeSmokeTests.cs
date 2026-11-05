using NUnit.Framework;

namespace Unity2Foxglove.Tests.EditMode
{
    public sealed class Unity2FoxgloveSensorsEditModeSmokeTests
    {
        [Test]
        public void TestAssemblyLoads()
        {
            Assert.That(typeof(Unity.FoxgloveSDK.Sensors.Lidar.LidarScanTiming).Assembly.GetName().Name, Is.EqualTo("Unity.FoxgloveSDK.Sensors"));
        }
    }
}
