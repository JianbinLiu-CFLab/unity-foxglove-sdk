// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Shared side labels for declarative FoxService DTO validation.

using System;

namespace Unity.FoxgloveSDK.Editor
{
    public enum FoxServiceDtoSide
    {
        Request,
        Response
    }

    public static class FoxServiceDtoSideExtensions
    {
        public static string ToRuleSide(this FoxServiceDtoSide side)
            => side switch
            {
                FoxServiceDtoSide.Request => FoxServiceDtoRules.RequestSide,
                FoxServiceDtoSide.Response => FoxServiceDtoRules.ResponseSide,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unsupported FoxService DTO side.")
            };
    }
}
