using System;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ROS2
{

/// <summary>Tracks and conditionally restores process-wide environment values owned by the runtime.</summary>
internal static class Ros2ForUnityProcessEnvironmentLease
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
#endif
#endif
    private sealed class Entry
    {
        internal readonly string Previous;
        internal string Applied;
        internal bool HasApplied;
        internal string PendingRestore;
        internal bool HasPendingRestore;
        internal string PathEntry;
        internal char PathSeparator;

        internal Entry(string previous)
        {
            Previous = previous;
        }
    }

    private static readonly object Gate = new object();
    private static readonly Dictionary<string, Entry> Entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static bool active;
    private static bool restorePending;

    internal static void Begin()
    {
        lock (Gate)
        {
            if (active || restorePending)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is still pending cleanup.");

            Entries.Clear();
            active = true;
        }
    }

    internal static void Set(string name, string value, bool windows)
    {
        if (String.IsNullOrEmpty(name))
            throw new ArgumentException("Environment variable name is required.", nameof(name));

        lock (Gate)
        {
            if (!active)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is not active.");

            if (restorePending)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is still pending cleanup.");

            if (!Entries.TryGetValue(name, out var entry))
            {
                entry = new Entry(Environment.GetEnvironmentVariable(name));
                Entries.Add(name, entry);
            }

            string rollbackValue = Environment.GetEnvironmentVariable(name);
            string previousApplied = entry.Applied;
            bool hadApplied = entry.HasApplied;
            entry.Applied = value;
            entry.HasApplied = true;
            try
            {
                Apply(name, value, windows);
                entry.HasPendingRestore = false;
                entry.PendingRestore = null;
            }
            catch
            {
                try
                {
                    Apply(name, rollbackValue, windows);
                    if ((hadApplied && String.Equals(rollbackValue, previousApplied, StringComparison.Ordinal))
                        || (!hadApplied && String.Equals(rollbackValue, entry.Previous, StringComparison.Ordinal)))
                    {
                        entry.Applied = rollbackValue;
                        entry.HasApplied = hadApplied;
                        entry.HasPendingRestore = false;
                        entry.PendingRestore = null;
                    }
                    else
                    {
                        Entries.Remove(name);
                    }
                }
                catch
                {
                    restorePending = true;
                    entry.PendingRestore = rollbackValue;
                    entry.HasPendingRestore = true;
                }
                throw;
            }
        }
    }

    internal static void SetPath(
        string name,
        string value,
        string pathEntry,
        char separator,
        bool windows)
    {
        Set(name, value, windows);
        lock (Gate)
        {
            if (Entries.TryGetValue(name, out var entry)
                && !ContainsPathEntry(entry.Previous, pathEntry, separator))
            {
                entry.PathEntry = pathEntry;
                entry.PathSeparator = separator;
            }
        }
    }

    internal static bool Restore(bool windows)
    {
        lock (Gate)
        {
            if (!active)
                return true;

            bool success = true;
            var completed = new List<string>();
            foreach (var pair in Entries)
            {
                var entry = pair.Value;
                var current = Environment.GetEnvironmentVariable(pair.Key);
                if (entry.HasPendingRestore
                    && String.Equals(current, entry.PendingRestore, StringComparison.Ordinal))
                {
                    try
                    {
                        ApplyNativeIfSupported(pair.Key, entry.PendingRestore, windows);
                        completed.Add(pair.Key);
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        restorePending = true;
                        Debug.LogException(exception);
                    }
                    continue;
                }

                entry.HasPendingRestore = false;
                entry.PendingRestore = null;
                if (!entry.HasApplied
                    || !String.Equals(current, entry.Applied, StringComparison.Ordinal))
                {
                    if (entry.PathEntry != null
                        && TryRemovePathEntry(
                            current,
                            entry.PathEntry,
                            entry.PathSeparator,
                            out var restoredPath))
                    {
                        try
                        {
                            Apply(pair.Key, restoredPath, windows);
                            entry.HasPendingRestore = false;
                            entry.PendingRestore = null;
                            completed.Add(pair.Key);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            restorePending = true;
                            entry.PendingRestore = restoredPath;
                            entry.HasPendingRestore = true;
                            Debug.LogException(exception);
                        }
                        continue;
                    }

                    // A caller changed the managed value. Preserve that change,
                    // and synchronize the native CRT view on Windows as well.
                    if (windows)
                    {
                        try
                        {
                            ApplyNative(pair.Key, current);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            restorePending = true;
                            entry.PendingRestore = current;
                            entry.HasPendingRestore = true;
                            Debug.LogException(exception);
                            continue;
                        }
                    }
                    completed.Add(pair.Key);
                    continue;
                }

                try
                {
                    Apply(pair.Key, pair.Value.Previous, windows);
                    entry.HasPendingRestore = false;
                    entry.PendingRestore = null;
                    completed.Add(pair.Key);
                }
                catch (Exception exception)
                {
                    success = false;
                    restorePending = true;
                    entry.PendingRestore = pair.Value.Previous;
                    entry.HasPendingRestore = true;
                    Debug.LogException(exception);
                }
            }

            foreach (var name in completed)
                Entries.Remove(name);

            if (Entries.Count == 0)
            {
                active = false;
                restorePending = false;
            }

            return success && !active;
        }
    }

    private static bool ContainsPathEntry(string path, string entry, char separator)
    {
        if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(entry))
            return false;

        foreach (var part in path.Split(new[] { separator }, StringSplitOptions.None))
        {
            if (String.Equals(part.Trim(), entry, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryRemovePathEntry(
        string path,
        string entry,
        char separator,
        out string restoredPath)
    {
        restoredPath = path;
        if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(entry))
            return false;

        var parts = path.Split(new[] { separator }, StringSplitOptions.None);
        var kept = new List<string>(parts.Length);
        var removed = false;
        foreach (var part in parts)
        {
            if (!removed
                && String.Equals(part.Trim(), entry, StringComparison.OrdinalIgnoreCase))
            {
                removed = true;
                continue;
            }
            kept.Add(part);
        }

        if (!removed)
            return false;

        restoredPath = kept.Count == 0 ? String.Empty : String.Join(separator.ToString(), kept);
        return !String.Equals(restoredPath, path, StringComparison.Ordinal);
    }

    internal static bool Abort(bool windows)
    {
        return Restore(windows);
    }

    private static void Apply(string name, string value, bool windows)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (windows)
            ApplyNative(name, value);
    }

    private static void ApplyNative(string name, string value)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        int result = _wputenv_s(name, value ?? String.Empty);
        if (result != 0)
        {
            throw new InvalidOperationException(
                "Failed to set Windows CRT environment variable '" + name
                + "' (ucrtbase _wputenv_s returned " + result + ")");
        }
#else
        throw new PlatformNotSupportedException("Windows CRT environment updates are only supported on Windows.");
#endif
    }

    private static void ApplyNativeIfSupported(string name, string value, bool windows)
    {
        if (windows)
            ApplyNative(name, value);
    }
}

}  // namespace ROS2
