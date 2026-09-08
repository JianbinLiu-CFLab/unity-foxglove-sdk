using System;
using Unity.FoxgloveSDK.Schemas.Imu;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R4-G03")]
    [Trait("Domain", "Sensors")]
    public sealed class G03ImuBuilderTests
    {
        [Fact]
        public void ImuBuilderHasFiniteAdmissionBoundary()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/ImuMessageBuilder.cs");

            Assert.Contains("double.IsNaN", source);
            Assert.Contains("double.IsInfinity", source);
            Assert.Contains("ValidateFiniteVector", source);
        }

        [Fact]
        public void ImuNativeFrameRejectsNonFiniteValues()
        {
            Assert.Throws<ArgumentException>(() => new ImuNativeFrame(
                1, "imu", new System.Numerics.Vector3(float.NaN, 0, 0),
                System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, true));
        }
    }
}
