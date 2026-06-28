// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Point-cloud publisher diagnostic logging helpers.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxglovePointCloudPublisher
    {
        private void LogPointCloudDiagnosticMessage(string format, object[] args)
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                format,
                args);
        }
    }
}
