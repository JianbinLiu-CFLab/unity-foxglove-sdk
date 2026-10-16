// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// Owns only the process environment values written by editor runtime
    /// selection. Values changed by another owner are left untouched on restore.
    /// </summary>
    internal static class Ros2ForUnityEditorEnvironmentLease
    {
#if UNITY_EDITOR_WIN
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int _wputenv_s(string name, string value);
#endif

        private sealed class Entry
        {
            internal readonly string Previous;
            internal string Applied;
            internal bool HasApplied;

            internal Entry(string previous)
            {
                Previous = previous;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static bool restorePending;

        internal static void Set(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Environment variable name is required.", nameof(name));

            lock (Gate)
            {
                if (restorePending)
                {
                    throw new InvalidOperationException(
                        "The ROS2 For Unity editor environment lease is still pending cleanup.");
                }

                if (!Entries.TryGetValue(name, out var entry))
                {
                    entry = new Entry(Environment.GetEnvironmentVariable(name));
                    Entries.Add(name, entry);
                }

                var rollbackValue = Environment.GetEnvironmentVariable(name);
                var previousApplied = entry.Applied;
                var hadApplied = entry.HasApplied;
                entry.Applied = value;
                entry.HasApplied = true;
                try
                {
                    Apply(name, value);
                }
                catch
                {
                    try
                    {
                        Apply(name, rollbackValue);
                        if ((hadApplied
                                && string.Equals(
                                    rollbackValue,
                                    previousApplied,
                                    StringComparison.Ordinal))
                            || (!hadApplied
                                && string.Equals(
                                    rollbackValue,
                                    entry.Previous,
                                    StringComparison.Ordinal)))
                        {
                            entry.Applied = rollbackValue;
                            entry.HasApplied = hadApplied;
                        }
                        else
                        {
                            Entries.Remove(name);
                        }
                    }
                    catch
                    {
                        restorePending = true;
                    }
                    throw;
                }
            }
        }

        internal static bool Restore()
        {
            lock (Gate)
            {
                if (Entries.Count == 0)
                {
                    restorePending = false;
                    return true;
                }

                var completed = new List<string>();
                var success = true;
                foreach (var pair in Entries)
                {
                    var current = Environment.GetEnvironmentVariable(pair.Key);
                    if (!pair.Value.HasApplied
                        || !string.Equals(current, pair.Value.Applied, StringComparison.Ordinal))
                    {
                        // A caller changed the managed value. Preserve it while
                        // synchronizing the native CRT view on Windows.
                        try
                        {
                            Apply(pair.Key, current);
                            completed.Add(pair.Key);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            restorePending = true;
                            UnityEngine.Debug.LogException(exception);
                        }
                        continue;
                    }

                    try
                    {
                        Apply(pair.Key, pair.Value.Previous);
                        completed.Add(pair.Key);
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        UnityEngine.Debug.LogException(exception);
                    }
                }

                foreach (var name in completed)
                    Entries.Remove(name);

                restorePending = Entries.Count != 0;
                return success && !restorePending;
            }
        }

        private static void Apply(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
#if UNITY_EDITOR_WIN
            var result = _wputenv_s(name, value ?? string.Empty);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '"
                    + name
                    + "' (ucrtbase _wputenv_s returned "
                    + result
                    + ").");
            }
#endif
        }
    }
}
#endif
