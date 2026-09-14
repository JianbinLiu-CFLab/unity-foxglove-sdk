using System;

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    /// <summary>Generated-code bootstrap only; not a manual codec registration SPI.</summary>
    public static class ComponentMessagePackGeneratedBootstrap
    {
        public static void RegisterGenerated(ComponentMessagePackGeneratedManifest manifest)
            => ComponentMessagePackCodecRegistry.RegisterGenerated(manifest);

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        private static void ResetOnSubsystemRegistration()
            => ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
    }
}
