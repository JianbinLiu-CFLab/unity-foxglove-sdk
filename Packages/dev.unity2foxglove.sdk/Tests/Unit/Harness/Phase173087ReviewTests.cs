// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-087 Unity review regression checks.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    internal sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Windows CRT branch requires Windows.";
        }
    }

}
