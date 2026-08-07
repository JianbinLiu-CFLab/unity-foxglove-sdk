// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Launch a replacement Unity Editor only after the current Editor and
// its project lock have fully exited.

using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// Builds a detached, exit-aware relay process for a clean Unity Editor restart.
    /// The relay inherits the selected runtime environment, waits for the current
    /// Editor PID and Unity project lock, then starts the replacement Editor.
    /// </summary>
    internal static class Ros2ForUnityEditorRestartRelay
    {
        internal const string PreviousEditorProcessIdEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID";
        internal const string PreviousEditorStartFileTimeEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_FILETIME";
        internal const string PreviousEditorStartIdentityEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_IDENTITY";
        internal const string EditorExecutableEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE";
        internal const string ProjectDirectoryEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY";

        private const string WindowsRelayScript = @"
$previousEditorProcessId = [int]$env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID
$previousEditorStartFileTime = [long]$env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_FILETIME
$editorExecutable = $env:UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE
$projectDirectory = $env:UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY
$previousEditor = Get-Process -Id $previousEditorProcessId -ErrorAction SilentlyContinue
$previousEditorIdentityMatches = $false
if ($null -ne $previousEditor) {
    try {
        $previousEditorIdentityMatches = $previousEditor.StartTime.ToFileTimeUtc() -eq $previousEditorStartFileTime
    } catch {
        $previousEditorIdentityMatches = $false
    }
}
if ($previousEditorIdentityMatches) {
    $previousEditor.WaitForExit()
}
$lockPath = Join-Path $projectDirectory 'Temp\UnityLockfile'
while (Test-Path -LiteralPath $lockPath) {
    Start-Sleep -Milliseconds 100
}
Remove-Item Env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID -ErrorAction SilentlyContinue
Remove-Item Env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_FILETIME -ErrorAction SilentlyContinue
Remove-Item Env:UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE -ErrorAction SilentlyContinue
Remove-Item Env:UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY -ErrorAction SilentlyContinue
$projectArgument = '""' + $projectDirectory + '""'
Start-Process -FilePath $editorExecutable -ArgumentList ('-projectPath ' + $projectArgument) -WorkingDirectory $projectDirectory | Out-Null
";

        private const string PosixRelayScript = @"
previous_editor_process_id=""$UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID""
previous_editor_start_identity=""$UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_IDENTITY""
editor_executable=""$UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE""
project_directory=""$UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY""
process_start_identity() {
    process_id=""$1""
    proc_stat=""/proc/$process_id/stat""
    if [ -r ""$proc_stat"" ]; then
        proc_tail=$(sed 's/^.*) //' ""$proc_stat"" 2>/dev/null) || return 1
        start_ticks=$(printf '%s\n' ""$proc_tail"" | awk '{print $20}')
        if [ -n ""$start_ticks"" ]; then
            printf 'proc:%s' ""$start_ticks""
            return 0
        fi
    fi
    start_text=$(LC_ALL=C ps -p ""$process_id"" -o lstart= 2>/dev/null \
        | sed 's/^[[:space:]]*//;s/[[:space:]]*$//') || return 1
    [ -n ""$start_text"" ] || return 1
    printf 'ps:%s' ""$start_text""
}
while kill -0 ""$previous_editor_process_id"" 2>/dev/null; do
    current_editor_start_identity=$(process_start_identity ""$previous_editor_process_id"" || true)
    if [ -z ""$previous_editor_start_identity"" ] \
        || [ ""$current_editor_start_identity"" != ""$previous_editor_start_identity"" ]; then
        break
    fi
    sleep 1
done
lock_path=""$project_directory/Temp/UnityLockfile""
while [ -e ""$lock_path"" ]; do
    sleep 1
done
unset UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID
unset UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_FILETIME
unset UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_IDENTITY
unset UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE
unset UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY
exec ""$editor_executable"" -projectPath ""$project_directory""
";

        public static ProcessStartInfo CreateStartInfo(
            bool isWindows,
            string relayExecutable,
            int previousEditorProcessId,
            string editorExecutable,
            string projectDirectory,
            ProcessStartInfo replacementStartInfo)
        {
            if (string.IsNullOrWhiteSpace(relayExecutable))
                throw new ArgumentException("A restart relay executable is required.", nameof(relayExecutable));
            if (previousEditorProcessId <= 0)
                throw new ArgumentOutOfRangeException(nameof(previousEditorProcessId));
            if (string.IsNullOrWhiteSpace(editorExecutable))
                throw new ArgumentException("A Unity Editor executable is required.", nameof(editorExecutable));
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new ArgumentException("A Unity project directory is required.", nameof(projectDirectory));
            if (replacementStartInfo == null)
                throw new ArgumentNullException(nameof(replacementStartInfo));

            long previousEditorStartFileTime = 0;
            string previousEditorStartIdentity = null;
            if (isWindows)
            {
                using (var previousEditor = Process.GetProcessById(previousEditorProcessId))
                {
                    previousEditorStartFileTime = previousEditor.StartTime.ToFileTimeUtc();
                }
            }
            else
            {
                previousEditorStartIdentity = CapturePosixProcessStartIdentity(
                    previousEditorProcessId);
            }

            var relayStartInfo = new ProcessStartInfo
            {
                FileName = relayExecutable,
                WorkingDirectory = projectDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            CopyEnvironment(replacementStartInfo, relayStartInfo);
            relayStartInfo.EnvironmentVariables[PreviousEditorProcessIdEnvironmentVariable] =
                previousEditorProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            relayStartInfo.EnvironmentVariables[PreviousEditorStartFileTimeEnvironmentVariable] =
                isWindows
                    ? previousEditorStartFileTime.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            relayStartInfo.EnvironmentVariables[PreviousEditorStartIdentityEnvironmentVariable] =
                previousEditorStartIdentity ?? string.Empty;
            relayStartInfo.EnvironmentVariables[EditorExecutableEnvironmentVariable] = editorExecutable;
            relayStartInfo.EnvironmentVariables[ProjectDirectoryEnvironmentVariable] = projectDirectory;
            relayStartInfo.Arguments = isWindows
                ? BuildWindowsArguments()
                : "-c " + QuotePosixArgument(PosixRelayScript);
            return relayStartInfo;
        }

        private static string CapturePosixProcessStartIdentity(int processId)
        {
            var processIdText = processId.ToString(CultureInfo.InvariantCulture);
            var procStatPath = "/proc/" + processIdText + "/stat";
            try
            {
                if (File.Exists(procStatPath))
                {
                    var stat = File.ReadAllText(procStatPath);
                    var commandEnd = stat.LastIndexOf(") ", StringComparison.Ordinal);
                    if (commandEnd >= 0)
                    {
                        var fields = stat.Substring(commandEnd + 2).Split(
                            (char[])null,
                            StringSplitOptions.RemoveEmptyEntries);
                        const int startTimeIndexAfterCommand = 19;
                        ulong startTime;
                        if (fields.Length > startTimeIndexAfterCommand
                            && ulong.TryParse(
                                fields[startTimeIndexAfterCommand],
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out startTime))
                        {
                            return "proc:" + startTime.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Fall back to the portable ps identity below.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to the portable ps identity below.
            }

            return CapturePsProcessStartIdentity(processIdText);
        }

        private static string CapturePsProcessStartIdentity(string processId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = File.Exists("/bin/ps") ? "/bin/ps" : "ps",
                    Arguments = "-p " + processId + " -o lstart=",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                startInfo.EnvironmentVariables["LC_ALL"] = "C";
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return string.Empty;
                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // The process exited between the timeout and cleanup.
                        }
                        return string.Empty;
                    }
                    if (process.ExitCode != 0)
                        return string.Empty;

                    var startText = process.StandardOutput.ReadToEnd().Trim();
                    return startText.Length == 0 ? string.Empty : "ps:" + startText;
                }
            }
            catch (Win32Exception)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static void CopyEnvironment(ProcessStartInfo source, ProcessStartInfo destination)
        {
            foreach (DictionaryEntry entry in source.EnvironmentVariables)
            {
                var key = entry.Key as string;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                destination.EnvironmentVariables[key] = entry.Value?.ToString() ?? string.Empty;
            }
        }

        private static string BuildWindowsArguments()
            => "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
               + Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsRelayScript));

        private static string QuotePosixArgument(string value)
            => "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
    }
}
