// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Persist the OpenH264 install transaction state through a verified version-directory handle.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Ordered OpenH264 install transaction states; each is recorded before its step runs.</summary>
    internal enum OpenH264InstallJournalState
    {
        Prepared,
        Downloaded,
        Decompressed,
        HelperBuilt,
        BackupsMoved,
        DllPublished,
        HelperPublished,
        Committed
    }

    /// <summary>One persisted install journal record.</summary>
    internal readonly struct OpenH264InstallJournalRecord
    {
        /// <summary>Creates a record for a state and the original-artifact flags.</summary>
        internal OpenH264InstallJournalRecord(
            OpenH264InstallJournalState state,
            bool dllOriginal,
            bool helperOriginal)
        {
            State = state;
            DllOriginal = dllOriginal;
            HelperOriginal = helperOriginal;
        }

        internal OpenH264InstallJournalState State { get; }
        internal bool DllOriginal { get; }
        internal bool HelperOriginal { get; }
    }

    /// <summary>Reads, writes and deletes the install journal relative to a verified directory handle.</summary>
    internal static class OpenH264InstallJournal
    {
        internal const string FileName = "openh264-install.journal";
        internal const string TemporaryFileName = FileName + ".tmp";
        private const int MaxBytes = 256;
        private const string MissingStatus = "0xC0000034";

        /// <summary>Writes and flushes one record to a temporary file, then renames it over the journal.</summary>
        internal static bool TryWrite(
            OpenH264WindowsSafeDirectory directory,
            OpenH264InstallJournalRecord record,
            out string reason)
        {
            if (!directory.TryDeleteFile(TemporaryFileName, out reason)
                || !directory.TryCreateFile(TemporaryFileName, out var file, out reason))
                return false;

            using (file)
                return file.WriteAllBytes(Encoding.ASCII.GetBytes(Format(record)), out reason)
                       && file.RenameTo(directory, FileName, replaceExisting: true, out reason);
        }

        /// <summary>Reads the journal; a missing journal succeeds with <paramref name="present"/> false.</summary>
        internal static bool TryRead(
            OpenH264WindowsSafeDirectory directory,
            out bool present,
            out OpenH264InstallJournalRecord record,
            out string reason)
        {
            present = false;
            record = default;
            if (!directory.TryOpenFile(FileName, out var file, out reason))
            {
                if (!IsMissing(reason))
                    return false;

                reason = "";
                return true;
            }

            using (file)
            {
                if (!file.ReadAllBytes(MaxBytes, out var bytes, out reason))
                    return false;
                if (!TryParse(Encoding.ASCII.GetString(bytes), out record))
                {
                    reason = "The OpenH264 install journal is malformed.";
                    return false;
                }
            }

            present = true;
            return true;
        }

        /// <summary>Deletes the journal and any leftover temporary journal; missing files succeed.</summary>
        internal static bool TryDelete(OpenH264WindowsSafeDirectory directory, out string reason)
            => directory.TryDeleteFile(TemporaryFileName, out reason)
               && directory.TryDeleteFile(FileName, out reason);

        /// <summary>Returns whether a safe-file open failure means the name does not exist.</summary>
        internal static bool IsMissing(string reason)
            => reason.IndexOf(MissingStatus, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Formats a record as the fixed three-line journal text.</summary>
        private static string Format(OpenH264InstallJournalRecord record)
            => "state=" + record.State
               + "\ndll_original=" + (record.DllOriginal ? "1" : "0")
               + "\nhelper_original=" + (record.HelperOriginal ? "1" : "0")
               + "\n";

        /// <summary>Parses the fixed three-line journal text and rejects every other form.</summary>
        private static bool TryParse(string text, out OpenH264InstallJournalRecord record)
        {
            record = default;
            var lines = text.Split('\n');
            if (lines.Length != 4 || lines[3].Length != 0 || !lines[0].StartsWith("state=", StringComparison.Ordinal))
                return false;

            var stateName = lines[0].Substring("state=".Length);
            if (!Enum.TryParse(stateName, ignoreCase: false, out OpenH264InstallJournalState state)
                || !Enum.IsDefined(typeof(OpenH264InstallJournalState), state)
                || !string.Equals(state.ToString(), stateName, StringComparison.Ordinal)
                || !TryParseFlag(lines[1], "dll_original=", out var dllOriginal)
                || !TryParseFlag(lines[2], "helper_original=", out var helperOriginal))
                return false;

            record = new OpenH264InstallJournalRecord(state, dllOriginal, helperOriginal);
            return true;
        }

        /// <summary>Parses one <c>name=0|1</c> line.</summary>
        private static bool TryParseFlag(string line, string prefix, out bool value)
        {
            value = line == prefix + "1";
            return value || line == prefix + "0";
        }
    }
}
