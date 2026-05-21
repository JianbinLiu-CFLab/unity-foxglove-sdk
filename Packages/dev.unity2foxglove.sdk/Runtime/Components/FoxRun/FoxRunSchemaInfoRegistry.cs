// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime registry for generated FoxRun schema metadata.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Holds the generated FoxRun manifest snapshot for runtime evidence consumers.</summary>
    public static class FoxRunSchemaInfoRegistry
    {
        private static FoxRunSchemaManifestInfo _current;
        private static bool _hasConflict;
        private static string _conflictMessage = string.Empty;
        private static string _conflictingHash = string.Empty;

        public static bool HasGeneratedSchemaInfo => _current != null;
        public static bool HasConflict => _hasConflict;
        public static string ConflictMessage => _conflictMessage;
        public static string ConflictingHash => _conflictingHash;
        public static FoxRunSchemaManifestInfo Current => _current;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntimeLoad()
        {
            ResetState();
        }
#endif

        public static void RegisterGenerated(FoxRunSchemaManifestInfo manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (_current == null)
            {
                _current = manifest;
                return;
            }

            if (string.Equals(_current.GlobalManifestHash, manifest.GlobalManifestHash, StringComparison.Ordinal))
                return;

            _hasConflict = true;
            _conflictingHash = manifest.GlobalManifestHash ?? string.Empty;
            _conflictMessage =
                "A generated FoxRun schema info snapshot with a different manifest hash attempted to register. " +
                "The first snapshot remains active.";
        }

        /// <summary>Clears generated registry state for validation tests.</summary>
        public static void ClearForTests()
        {
            ResetState();
        }

        private static void ResetState()
        {
            _current = null;
            _hasConflict = false;
            _conflictMessage = string.Empty;
            _conflictingHash = string.Empty;
        }
    }
}
