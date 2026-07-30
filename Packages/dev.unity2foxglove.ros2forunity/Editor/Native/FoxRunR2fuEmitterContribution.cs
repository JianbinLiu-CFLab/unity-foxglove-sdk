// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native.Editor
// Purpose: R2FU physical-generation contribution for typed FoxRun bindings.

#if UNITY_EDITOR
using System.Text;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;

namespace Unity2Foxglove.Ros2ForUnity.Native.Editor
{
    [InitializeOnLoad]
    internal static class FoxRunR2fuEmitterContributionRegistration
    {
        static FoxRunR2fuEmitterContributionRegistration()
            => FoxRunTransportEmitterContributionRegistry.Register(
                FoxRunR2fuEmitterContribution.Instance);
    }

    internal sealed class FoxRunR2fuEmitterContribution :
        IFoxRunTransportEmitterContribution
    {
        internal static readonly FoxRunR2fuEmitterContribution Instance =
            new FoxRunR2fuEmitterContribution();

        private FoxRunR2fuEmitterContribution()
        {
        }

        public string ProviderId => FoxRunRos2TransportProvider.IdValue;

        public string HintNameSuffix => "typed-ros2";

        public void Emit(
            in FoxRunTransportEmitterContext context,
            StringBuilder output)
            => output.Append(
                FoxRunR2fuSourceEmitter.Emit(
                    context.Type));
    }
}
#endif
