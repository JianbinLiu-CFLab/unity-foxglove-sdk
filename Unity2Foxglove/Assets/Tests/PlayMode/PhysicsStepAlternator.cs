using System.Collections.Generic;
using UnityEngine;

namespace Unity2Foxglove.Tests.PlayMode
{
    [DefaultExecutionOrder(-32000)]
    public sealed class PhysicsStepAlternator : MonoBehaviour
    {
        public const float ShortStepSeconds = 0.015f;
        public const float LongStepSeconds = 0.035f;

        public List<double> Times { get; } = new List<double>();
        public List<float> NominalSteps { get; } = new List<float>();

        private void FixedUpdate()
        {
            Times.Add(Time.fixedTimeAsDouble);
            var nextStep = Times.Count % 2 == 0 ? ShortStepSeconds : LongStepSeconds;
            Time.fixedDeltaTime = nextStep;
            NominalSteps.Add(nextStep);
        }
    }
}
