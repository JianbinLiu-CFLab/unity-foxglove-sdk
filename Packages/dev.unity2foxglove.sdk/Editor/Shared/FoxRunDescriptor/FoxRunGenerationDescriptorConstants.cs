// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Shared constants for FoxRun generation-model descriptors.

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Version and naming constants shared by Roslyn, build-time, and tests.
    /// </summary>
    public static class FoxRunGenerationDescriptorConstants
    {
        // Integer schema version embedded in descriptor JSON and recorded MCAP
        // metadata. Bump this together with GeneratorVersion for incompatible
        // descriptor-shape changes.
        public const int DescriptorVersion = 1;
        // Descriptor/generator format version, not the package release version.
        // Bump when descriptor JSON changes in a backward-incompatible way.
        public const string GeneratorVersion = "1.0.0";
        public const string JsonEncoding = "json";
        public const string DescriptorFileName = "foxrun.generation-descriptor.json";
    }
}
