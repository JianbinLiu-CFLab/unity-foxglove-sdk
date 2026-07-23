// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Launch a replacement Unity Editor only after the current Editor and
// its project lock have fully exited.

using System;
using System.Collections;
using System.Diagnostics;
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
        internal const string EditorExecutableEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE";
        internal const string ProjectDirectoryEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY";

        private const string WindowsRelayScript = @"
$previousEditorProcessId = [int]$env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID
$editorExecutable = $env:UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE
$projectDirectory = $env:UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY
$previousEditor = Get-Process -Id $previousEditorProcessId -ErrorAction SilentlyContinue
if ($null -ne $previousEditor) {
    $previousEditor.WaitForExit()
}
$lockPath = Join-Path $projectDirectory 'Temp\UnityLockfile'
while (Test-Path -LiteralPath $lockPath) {
    Start-Sleep -Milliseconds 100
}
Remove-Item Env:UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID -ErrorAction SilentlyContinue
Remove-Item Env:UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE -ErrorAction SilentlyContinue
Remove-Item Env:UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY -ErrorAction SilentlyContinue
$projectArgument = '""' + $projectDirectory + '""'
Start-Process -FilePath $editorExecutable -ArgumentList ('-projectPath ' + $projectArgument) -WorkingDirectory $projectDirectory | Out-Null
";

        private const string PosixRelayScript = @"
previous_editor_process_id=""$UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID""
editor_executable=""$UNITY2FOXGLOVE_RESTART_EDITOR_EXECUTABLE""
project_directory=""$UNITY2FOXGLOVE_RESTART_PROJECT_DIRECTORY""
while kill -0 ""$previous_editor_process_id"" 2>/dev/null; do
    sleep 1
done
lock_path=""$project_directory/Temp/UnityLockfile""
while [ -e ""$lock_path"" ]; do
    sleep 1
done
unset UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_PID
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
            relayStartInfo.EnvironmentVariables[EditorExecutableEnvironmentVariable] = editorExecutable;
            relayStartInfo.EnvironmentVariables[ProjectDirectoryEnvironmentVariable] = projectDirectory;
            relayStartInfo.Arguments = isWindows
                ? BuildWindowsArguments()
                : "-c " + QuotePosixArgument(PosixRelayScript);
            return relayStartInfo;
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
