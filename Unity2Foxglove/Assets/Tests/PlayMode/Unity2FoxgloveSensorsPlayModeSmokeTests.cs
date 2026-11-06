using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Unity2Foxglove.Tests.PlayMode
{
    public sealed class Unity2FoxgloveSensorsPlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator LidarScanTimingAdvancesProgressWithinBudget()
        {
            yield return null;
            Assert.That(
                Unity.FoxgloveSDK.Sensors.Lidar.LidarScanTiming.AdvanceColumnProgress(
                    0d,
                    3d,
                    2,
                    1d,
                    2),
                Is.EqualTo(4d).Within(1e-10d));
        }
    }
}
