// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Journaled handle-relative publication and recovery of the OpenH264 DLL/helper pair.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class OpenH264ArtifactPairPublisher
    {
        private const string BackupSuffix = ".bak";

        /// <summary>Publishes a verified staged DLL and a staged helper through journaled renames inside one directory.</summary>
        /// <remarks>
        /// Takes ownership of <paramref name="stagedDll"/>. Any failure runs <see cref="TryRecover"/>, which restores the
        /// old pair before commit and finishes the new pair after it. Consumers may observe an intermediate pair between
        /// the DLL and helper renames.
        /// </remarks>
        internal static void Publish(
            OpenH264WindowsSafeDirectory directory,
            OpenH264WindowsSafeFile stagedDll,
            string stagedHelperName,
            string dllName,
            string helperName,
            string[] stagingNames,
            Action<OpenH264InstallJournalState> afterJournal = null)
        {
            try
            {
                var dllOriginal = Exists(directory, dllName);
                var helperOriginal = Exists(directory, helperName);
                Require(directory.TryDeleteFile(dllName + BackupSuffix, out var staleDll), staleDll);
                Require(directory.TryDeleteFile(helperName + BackupSuffix, out var staleHelper), staleHelper);
                Record(directory, OpenH264InstallJournalState.BackupsMoved, dllOriginal, helperOriginal, afterJournal);
                if (dllOriginal)
                    MoveToBackup(directory, dllName);
                if (helperOriginal)
                    MoveToBackup(directory, helperName);

                Record(directory, OpenH264InstallJournalState.DllPublished, dllOriginal, helperOriginal, afterJournal);
                Require(stagedDll.RenameTo(directory, dllName, replaceExisting: false, out var dllReason), dllReason);
                stagedDll.Dispose();

                Record(directory, OpenH264InstallJournalState.HelperPublished, dllOriginal, helperOriginal, afterJournal);
                using (var stagedHelper = Open(directory, stagedHelperName))
                    Require(stagedHelper.RenameTo(directory, helperName, replaceExisting: false, out var helperReason), helperReason);

                Record(directory, OpenH264InstallJournalState.Committed, dllOriginal, helperOriginal, afterJournal);
            }
            catch
            {
                stagedDll.Dispose();
                TryRecover(directory, dllName, helperName, stagingNames, out _);
                throw;
            }

            if (TryRecover(directory, dllName, helperName, stagingNames, out var cleanupReason))
                return;

            var message = "[Foxglove] OpenH264 is installed, but backup cleanup will finish on the next install: " + cleanupReason;
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning(message);
#else
            System.Diagnostics.Debug.WriteLine(message);
#endif
        }

        /// <summary>Applies the journal: removes staging before backups, rolls back before commit and rolls forward after it.</summary>
        internal static bool TryRecover(
            OpenH264WindowsSafeDirectory directory,
            string dllName,
            string helperName,
            string[] stagingNames,
            out string reason)
        {
            if (!OpenH264InstallJournal.TryRead(directory, out var present, out var record, out reason))
                return false;
            if (!present)
                return directory.TryDeleteFile(OpenH264InstallJournal.TemporaryFileName, out reason);

            if (record.State == OpenH264InstallJournalState.Committed)
                return directory.TryDeleteFile(dllName + BackupSuffix, out reason)
                       && directory.TryDeleteFile(helperName + BackupSuffix, out reason)
                       && OpenH264InstallJournal.TryDelete(directory, out reason);

            if (record.State >= OpenH264InstallJournalState.BackupsMoved
                && (!TryRestore(directory, dllName, record.DllOriginal, out reason)
                    || !TryRestore(directory, helperName, record.HelperOriginal, out reason)))
                return false;

            foreach (var name in stagingNames)
                if (!directory.TryDeleteFile(name, out reason))
                    return false;

            return OpenH264InstallJournal.TryDelete(directory, out reason);
        }

        /// <summary>Restores one artifact to its pre-publication state.</summary>
        private static bool TryRestore(
            OpenH264WindowsSafeDirectory directory,
            string name,
            bool original,
            out string reason)
        {
            if (!original)
                return directory.TryDeleteFile(name, out reason);

            if (!directory.TryOpenFile(name + BackupSuffix, out var backup, out reason))
            {
                if (!OpenH264InstallJournal.IsMissing(reason))
                    return false;

                reason = "";
                return true;
            }

            using (backup)
                return backup.RenameTo(directory, name, replaceExisting: true, out reason);
        }

        /// <summary>Records one journal state and then runs the interruption hook.</summary>
        private static void Record(
            OpenH264WindowsSafeDirectory directory,
            OpenH264InstallJournalState state,
            bool dllOriginal,
            bool helperOriginal,
            Action<OpenH264InstallJournalState> afterJournal)
        {
            Require(
                OpenH264InstallJournal.TryWrite(
                    directory,
                    new OpenH264InstallJournalRecord(state, dllOriginal, helperOriginal),
                    out var reason),
                reason);
            afterJournal?.Invoke(state);
        }

        /// <summary>Returns whether a direct child file exists; any other open failure fails closed.</summary>
        private static bool Exists(OpenH264WindowsSafeDirectory directory, string name)
        {
            if (directory.TryOpenFile(name, out var file, out var reason))
            {
                file.Dispose();
                return true;
            }

            Require(OpenH264InstallJournal.IsMissing(reason), reason);
            return false;
        }

        /// <summary>Moves a final artifact to its backup name, replacing a stale backup.</summary>
        private static void MoveToBackup(OpenH264WindowsSafeDirectory directory, string name)
        {
            using (var file = Open(directory, name))
                Require(file.RenameTo(directory, name + BackupSuffix, replaceExisting: true, out var reason), reason);
        }

        /// <summary>Opens an existing direct child file or fails closed.</summary>
        private static OpenH264WindowsSafeFile Open(OpenH264WindowsSafeDirectory directory, string name)
        {
            Require(directory.TryOpenFile(name, out var file, out var reason), reason);
            return file;
        }

        /// <summary>Throws when a safe-file operation failed.</summary>
        private static void Require(bool succeeded, string reason)
        {
            if (!succeeded)
                throw new IOException("OpenH264 pair publication failed: " + reason);
        }
    }
}
