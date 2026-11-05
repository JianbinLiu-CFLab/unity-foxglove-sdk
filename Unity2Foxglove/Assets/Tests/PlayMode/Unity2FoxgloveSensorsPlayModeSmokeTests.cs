using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Unity2Foxglove.Tests.PlayMode
{
    public sealed class Unity2FoxgloveSensorsPlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator TestAssemblyLoads()
        {
            yield return null;
            Assert.That(typeof(Unity.FoxgloveSDK.Sensors.Lidar.LidarScanTiming).Assembly.GetName().Name, Is.EqualTo("Unity.FoxgloveSDK.Sensors"));
        }
    }
}
