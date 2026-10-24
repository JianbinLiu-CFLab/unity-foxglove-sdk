// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase186
// Purpose: Owns one token-correlated Unity Play Mode acceptance run.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove
{
    public static partial class Phase186BatchModeRos2BridgeProbe
    {
        private static Phase186RunConfiguration LoadFromCommandLine()
        {
            var path = ReadArgumentValue(RunConfigArgument);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException(
                    "Phase186 Batch probe requires -phase186RunConfig.");
            return Phase186RunConfiguration.Load(path);
        }

        private static bool HasArgument(string name)
            => Array.Exists(
                Environment.GetCommandLineArgs(),
                value => string.Equals(value, name, StringComparison.Ordinal));

        private static string ReadArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];
            }
            return null;
        }

        private static bool TryGetStartedAt(out DateTime startedAt)
        {
            startedAt = default;
            var value = SessionState.GetString(Key("started-at"), string.Empty);
            return long.TryParse(
                       value,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out var ticks)
                   && ticks > 0
                   && TryCreateUtc(ticks, out startedAt);
        }

        private static bool TryCreateUtc(long ticks, out DateTime value)
        {
            try
            {
                value = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                value = default;
                return false;
            }
        }

        private static string Key(string suffix) => SessionPrefix + suffix;

        private static string ProjectRoot()
            => Path.GetDirectoryName(Application.dataPath)
               ?? throw new DirectoryNotFoundException(
                   "Unity project root could not be resolved.");
    }

    }
}
#endif
