// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Pinned Cisco OpenH264 binary metadata for explicit user installs.

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Pinned Cisco OpenH264 binary metadata used by the explicit installer UI.</summary>
    public static class OpenH264OfficialBinaryManifest
    {
        public const string Version = "v2.6.0";
        public const string AssetName = "openh264-2.6.0-win64.dll.bz2";
        public const string DllFileName = "openh264-2.6.0-win64.dll";
        public const string HelperFileName = "openh264_probe_encoder.exe";
        public const string HelperSourceRelativePath = "Editor/Native/OpenH264/openh264_probe_encoder.cpp";
        public const string HeaderIncludeRelativePath = "Editor/Native/OpenH264/v2.6.0/include/wels";
        /// <summary>Official Cisco binary URL for the pinned OpenH264 artifact.</summary>
        public const string DownloadUrl = "https://ciscobinary.openh264.org/openh264-2.6.0-win64.dll.bz2";
        public const string CompressedAssetSha256 = "DAB5F2A872777F9A58B69BFA9FBCF20D9F82F2D6EC91383FD70BFF49BD34AC9F";
        public const string DllSha256 = "2076CB5675EC6C1A4C70E7A2A322552F547B6EEED649D6DFCD9E02A543B24691";
        public const string ReleasePageUrl = "https://github.com/cisco/openh264/releases/tag/v2.6.0";
        public const string BinaryLicenseUrl = "https://www.openh264.org/BINARY_LICENSE.txt";
        public const string ApproximateSizeLabel = "452 KB compressed";
        public const string Attribution = "OpenH264 Video Codec provided by Cisco Systems, Inc.";
    }
}
