// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Registers the Bridge-owned physical FoxRun emitter definition.

using System.Text;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    [InitializeOnLoad]
    internal static class FoxRunBridgeEmitterContributionRegistration
    {
        static FoxRunBridgeEmitterContributionRegistration()
            => FoxRunTransportEmitterContributionRegistry.Register(
                FoxRunBridgeEmitterContribution.Instance);
    }

    internal sealed class FoxRunBridgeEmitterContribution :
        IFoxRunTransportEmitterContribution
    {
        internal static readonly FoxRunBridgeEmitterContribution Instance =
            new FoxRunBridgeEmitterContribution();

        private FoxRunBridgeEmitterContribution()
        {
        }

        public string ProviderId =>
            Ros2BridgeTransportProvider.ProviderId;

        public string HintNameSuffix => "typed-cdr";

        public void Emit(
            in FoxRunTransportEmitterContext context,
            StringBuilder output)
            => output.Append(
                FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                    context.Type));
    }
}
