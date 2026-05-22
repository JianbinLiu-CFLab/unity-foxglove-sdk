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
        public const int DescriptorVersion = 1;
        public const string GeneratorVersion = "1.0.0";
        public const string JsonEncoding = "json";
        public const string DescriptorFileName = "foxrun.generation-descriptor.json";
    }
}
