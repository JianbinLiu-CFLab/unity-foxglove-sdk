// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Directional FoxRun publish wire policy.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField] private FoxRunWireEncoding _defaultFoxRunPublishEncoding = FoxRunWireEncoding.Protobuf;
        private FoxRunWireEncoding _activeFoxRunPublishEncoding = FoxRunWireEncoding.Protobuf;
        private bool _hasActiveFoxRunPublishEncoding;

        /// <summary>Serialized default used by inherited PublishOnly contracts.</summary>
        public FoxRunWireEncoding DefaultFoxRunPublishEncoding
        {
            get => _defaultFoxRunPublishEncoding == FoxRunWireEncoding.Inherit
                ? FoxRunWireEncoding.Protobuf
                : FoxRunWireEncodingResolver.ValidateManagerDefault(_defaultFoxRunPublishEncoding);
            set => _defaultFoxRunPublishEncoding = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
        }

        /// <summary>Effective publish default for the active server session, or the current configuration while stopped.</summary>
        public FoxRunWireEncoding ActiveFoxRunPublishEncoding => _hasActiveFoxRunPublishEncoding
            ? _activeFoxRunPublishEncoding
            : DefaultFoxRunPublishEncoding;

        internal void CaptureFoxRunPublishEncodingForServer()
        {
            _activeFoxRunPublishEncoding = DefaultFoxRunPublishEncoding;
            _hasActiveFoxRunPublishEncoding = true;
        }

        internal void ClearFoxRunPublishEncodingForServer()
        {
            _hasActiveFoxRunPublishEncoding = false;
            _activeFoxRunPublishEncoding = FoxRunWireEncoding.Protobuf;
        }
    }
}
