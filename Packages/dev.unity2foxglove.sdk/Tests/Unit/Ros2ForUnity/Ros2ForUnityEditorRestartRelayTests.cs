// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock the clean-editor-restart relay so a replacement Unity process
// is never started while the current Editor still owns the project lock.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-I")]
    [Trait("Domain", "R2fuEditorRestart")]
    public sealed class Ros2ForUnityEditorRestartRelayTests
    {
        private const string PreviousEditorStartIdentityEnvironmentVariable =
            "UNITY2FOXGLOVE_RESTART_PREVIOUS_EDITOR_START_IDENTITY";

        private static readonly TimeSpan ReplacementLaunchTimeout =
            TimeSpan.FromSeconds(30);

        [Fact]
        public void WindowsRelayWaitsForThePreviousEditorAndProjectLockBeforeLaunchingUnity()
        {
            var previousEditorProcessId = CurrentProcessId();
            var replacement = CreateReplacementStartInfo();

            var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                isWindows: true,
                relayExecutable: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                previousEditorProcessId: previousEditorProcessId,
                editorExecutable: @"C:\Program Files\Unity\Editor\Unity.exe",
                projectDirectory: @"D:\repo\Unity2Foxglove",
                replacementStartInfo: replacement);

            Assert.Equal(
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                relay.FileName);
            Assert.False(relay.UseShellExecute);
            Assert.True(relay.CreateNoWindow);
            Assert.Equal(@"D:\repo\Unity2Foxglove", relay.WorkingDirectory);
            Assert.Equal(
                previousEditorProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                relay.EnvironmentVariables[
                    Ros2ForUnityEditorRestartRelay.PreviousEditorProcessIdEnvironmentVariable]);
            Assert.Equal(@"C:\Program Files\Unity\Editor\Unity.exe", relay.EnvironmentVariables[
                Ros2ForUnityEditorRestartRelay.EditorExecutableEnvironmentVariable]);
            Assert.Equal(@"D:\repo\Unity2Foxglove", relay.EnvironmentVariables[
                Ros2ForUnityEditorRestartRelay.ProjectDirectoryEnvironmentVariable]);
            Assert.Equal("rmw_zenoh_cpp", relay.EnvironmentVariables["RMW_IMPLEMENTATION"]);
            Assert.Equal("tcp/127.0.0.1:8778", relay.EnvironmentVariables["ZENOH_SESSION_CONFIG_URI"]);

            var script = DecodePowerShellScript(relay.Arguments);
            Assert.Contains("Get-Process -Id $previousEditorProcessId", script, StringComparison.Ordinal);
            Assert.Contains("$previousEditor.WaitForExit()", script, StringComparison.Ordinal);
            Assert.Contains("Test-Path -LiteralPath $lockPath", script, StringComparison.Ordinal);
            Assert.Contains("Start-Process -FilePath $editorExecutable", script, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsRelayPinsThePreviousEditorProcessIdentity()
        {
            using (var current = Process.GetCurrentProcess())
            {
                var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                    isWindows: true,
                    relayExecutable: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                    previousEditorProcessId: current.Id,
                    editorExecutable: @"C:\Program Files\Unity\Editor\Unity.exe",
                    projectDirectory: @"D:\repo\Unity2Foxglove",
                    replacementStartInfo: CreateReplacementStartInfo());

                Assert.Equal(
                    current.StartTime.ToFileTimeUtc().ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    relay.EnvironmentVariables[
                        Ros2ForUnityEditorRestartRelay
                            .PreviousEditorStartFileTimeEnvironmentVariable]);
                Assert.Contains(
                    "$previousEditor.StartTime.ToFileTimeUtc()",
                    DecodePowerShellScript(relay.Arguments),
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void WindowsRelayDoesNotWaitForAReusedProcessId()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-editor-restart-relay-identity-" + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "Unity2Foxglove");
            var markerPath = Path.Combine(root, "replacement-started.txt");
            var targetScript = Path.Combine(root, "replacement.cmd");
            Process previousEditor = null;
            Process relayProcess = null;
            try
            {
                Directory.CreateDirectory(projectDirectory);
                File.WriteAllText(
                    targetScript,
                    "@echo off\r\n> \"" + markerPath + "\" echo replacement\r\n");
                previousEditor = StartSleepingPowerShell(
                    WindowsPowerShellExecutable(),
                    seconds: 60);
                var relayStartInfo = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                    isWindows: true,
                    relayExecutable: WindowsPowerShellExecutable(),
                    previousEditorProcessId: previousEditor.Id,
                    editorExecutable: targetScript,
                    projectDirectory: projectDirectory,
                    replacementStartInfo: CreateReplacementStartInfo());
                relayStartInfo.EnvironmentVariables[
                    Ros2ForUnityEditorRestartRelay
                        .PreviousEditorStartFileTimeEnvironmentVariable] = "0";
                relayProcess = StartRelayWithoutWindowSuppression(relayStartInfo);

                Assert.True(
                    SpinWait.SpinUntil(
                        () => File.Exists(markerPath),
                        ReplacementLaunchTimeout),
                    "The relay waited for a different process that reused the previous Editor PID.");
                Assert.False(
                    previousEditor.HasExited,
                    "The process-identity fixture exited before proving the relay skipped it.");
            }
            finally
            {
                if (previousEditor != null)
                {
                    if (!previousEditor.HasExited)
                        previousEditor.Kill();
                    previousEditor.Dispose();
                }
                if (relayProcess != null)
                {
                    if (!relayProcess.HasExited)
                    {
                        if (!relayProcess.WaitForExit(5000))
                            relayProcess.Kill();
                    }
                    relayProcess.Dispose();
                }
                if (Directory.Exists(root))
                    DeleteDirectoryWhenReleased(root);
            }
        }

        [Fact]
        public void WindowsRelayQuotesTheSpacedProjectPathBeforeStartingUnity()
        {
            var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                isWindows: true,
                relayExecutable: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                previousEditorProcessId: CurrentProcessId(),
                editorExecutable: @"C:\Program Files\Unity\Editor\Unity.exe",
                projectDirectory: @"D:\repo with spaces\Unity2Foxglove",
                replacementStartInfo: CreateReplacementStartInfo());

            var script = DecodePowerShellScript(relay.Arguments);
            Assert.Contains("$projectArgument = '\"' + $projectDirectory + '\"'", script, StringComparison.Ordinal);
            Assert.Contains("-ArgumentList ('-projectPath ' + $projectArgument)", script, StringComparison.Ordinal);
        }

        [Fact]
        public void PosixRelayWaitsForThePreviousEditorAndProjectLockBeforeExecingUnity()
        {
            var previousEditorProcessId = CurrentProcessId();
            var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                isWindows: false,
                relayExecutable: "/bin/sh",
                previousEditorProcessId: previousEditorProcessId,
                editorExecutable: "/Applications/Unity/Unity.app/Contents/MacOS/Unity",
                projectDirectory: "/repo/Unity2Foxglove",
                replacementStartInfo: CreateReplacementStartInfo());

            Assert.Equal("/bin/sh", relay.FileName);
            Assert.False(relay.UseShellExecute);
            Assert.True(relay.CreateNoWindow);
            Assert.Contains("kill -0 \"$previous_editor_process_id\"", relay.Arguments, StringComparison.Ordinal);
            Assert.Contains("previous_editor_start_identity", relay.Arguments, StringComparison.Ordinal);
            Assert.Contains("current_editor_start_identity", relay.Arguments, StringComparison.Ordinal);
            Assert.Contains("[ -e \"$lock_path\" ]", relay.Arguments, StringComparison.Ordinal);
            Assert.Contains("exec \"$editor_executable\" -projectPath \"$project_directory\"", relay.Arguments, StringComparison.Ordinal);
            Assert.Equal(
                previousEditorProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                relay.EnvironmentVariables[
                    Ros2ForUnityEditorRestartRelay.PreviousEditorProcessIdEnvironmentVariable]);
        }

        [Fact]
        public void PosixRelayPinsThePreviousEditorProcessIdentity()
        {
            if (OperatingSystem.IsWindows())
                return;

            var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                isWindows: false,
                relayExecutable: "/bin/sh",
                previousEditorProcessId: CurrentProcessId(),
                editorExecutable: PosixTrueExecutable(),
                projectDirectory: Path.GetTempPath(),
                replacementStartInfo: CreateReplacementStartInfo());

            var identity = relay.EnvironmentVariables[
                PreviousEditorStartIdentityEnvironmentVariable];
            Assert.False(
                string.IsNullOrWhiteSpace(identity),
                "The POSIX relay must capture a process-start identity before detaching.");
            Assert.True(
                identity.StartsWith("proc:", StringComparison.Ordinal)
                || identity.StartsWith("ps:", StringComparison.Ordinal),
                "Unexpected POSIX process identity: " + identity);
        }

        [Fact]
        public void PosixRelayDoesNotWaitForAReusedProcessId()
        {
            if (OperatingSystem.IsWindows())
                return;

            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-editor-restart-relay-posix-identity-" + Guid.NewGuid().ToString("N"));
            Process relayProcess = null;
            try
            {
                Directory.CreateDirectory(root);
                var relay = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                    isWindows: false,
                    relayExecutable: "/bin/sh",
                    previousEditorProcessId: CurrentProcessId(),
                    editorExecutable: PosixTrueExecutable(),
                    projectDirectory: root,
                    replacementStartInfo: CreateReplacementStartInfo());
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        relay.EnvironmentVariables[
                            PreviousEditorStartIdentityEnvironmentVariable]),
                    "The behavioral fixture requires a captured POSIX process identity.");
                relay.EnvironmentVariables[PreviousEditorStartIdentityEnvironmentVariable] =
                    "not-the-same-process";

                relayProcess = Process.Start(relay)
                    ?? throw new InvalidOperationException("Could not start the POSIX restart relay fixture.");
                var exited = relayProcess.WaitForExit(5000);
                if (!exited)
                    relayProcess.Kill();

                Assert.True(
                    exited,
                    "The relay waited for a live process that only reused the previous Editor PID.");
                Assert.Equal(0, relayProcess.ExitCode);
            }
            finally
            {
                relayProcess?.Dispose();
                if (Directory.Exists(root))
                    DeleteDirectoryWhenReleased(root);
            }
        }

        [Fact]
        public void WindowsRelayDoesNotLaunchReplacementUntilTheProcessAndLockAreGone()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-editor-restart-relay-" + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "Unity2Foxglove");
            var lockPath = Path.Combine(projectDirectory, "Temp", "UnityLockfile");
            var markerPath = Path.Combine(root, "replacement-started.txt");
            var targetScript = Path.Combine(root, "replacement.cmd");
            Process previousEditor = null;
            Process relayProcess = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
                File.WriteAllText(lockPath, "held");
                File.WriteAllText(targetScript, "@echo off\r\n> \"" + markerPath + "\" echo replacement\r\n");
                previousEditor = StartSleepingPowerShell(WindowsPowerShellExecutable());
                relayProcess = StartRelayWithoutWindowSuppression(
                    Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                        isWindows: true,
                        relayExecutable: WindowsPowerShellExecutable(),
                        previousEditorProcessId: previousEditor.Id,
                        editorExecutable: targetScript,
                        projectDirectory: projectDirectory,
                        replacementStartInfo: CreateReplacementStartInfo()));

                Thread.Sleep(200);
                Assert.False(File.Exists(markerPath), "The relay launched Unity before the previous Editor exited.");

                previousEditor.WaitForExit();
                Thread.Sleep(200);
                Assert.False(File.Exists(markerPath), "The relay launched Unity before the project lock was released.");

                File.Delete(lockPath);
                Assert.True(
                    SpinWait.SpinUntil(() => File.Exists(markerPath), ReplacementLaunchTimeout),
                    "The relay did not launch the replacement after the previous Editor and project lock were gone.");
            }
            finally
            {
                if (previousEditor != null)
                {
                    if (!previousEditor.HasExited)
                        previousEditor.Kill();
                    previousEditor.Dispose();
                }
                if (relayProcess != null)
                {
                    if (!relayProcess.HasExited)
                    {
                        if (!relayProcess.WaitForExit(5000))
                            relayProcess.Kill();
                    }
                    relayProcess.Dispose();
                }
                if (Directory.Exists(root))
                    DeleteDirectoryWhenReleased(root);
            }
        }

        [Fact]
        public void WindowsRelayPassesTheProjectPathAsOneArgument()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-editor-restart-relay-arguments-" + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "Unity Project With Spaces", "Unity2Foxglove");
            var markerPath = Path.Combine(root, "replacement-arguments.txt");
            var targetScript = Path.Combine(root, "replacement.cmd");
            Process previousEditor = null;
            Process relayProcess = null;
            try
            {
                Directory.CreateDirectory(projectDirectory);
                File.WriteAllText(
                    targetScript,
                    "@echo off\r\n(\r\n"
                    + "echo [%~1]\r\n"
                    + "echo [%~2]\r\n"
                    + "echo [%~3]\r\n"
                    + ") > \"" + markerPath + "\"\r\n");
                previousEditor = StartSleepingPowerShell(WindowsPowerShellExecutable());
                relayProcess = StartRelayWithoutWindowSuppression(
                    Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                        isWindows: true,
                        relayExecutable: WindowsPowerShellExecutable(),
                        previousEditorProcessId: previousEditor.Id,
                        editorExecutable: targetScript,
                        projectDirectory: projectDirectory,
                        replacementStartInfo: CreateReplacementStartInfo()));
                previousEditor.WaitForExit();
                Assert.True(
                    SpinWait.SpinUntil(() => File.Exists(markerPath), ReplacementLaunchTimeout),
                    "The relay did not start the replacement argument fixture.");
                Assert.True(
                    relayProcess.WaitForExit(5000),
                    "The relay did not finish after starting the replacement argument fixture.");

                Assert.Equal(
                    new[]
                    {
                        "[-projectPath]",
                        "[" + projectDirectory + "]",
                        "[]",
                    },
                    ReadAllLinesWhenReleased(markerPath));
            }
            finally
            {
                if (previousEditor != null)
                {
                    if (!previousEditor.HasExited)
                        previousEditor.Kill();
                    previousEditor.Dispose();
                }
                if (relayProcess != null)
                {
                    if (!relayProcess.HasExited)
                    {
                        if (!relayProcess.WaitForExit(5000))
                            relayProcess.Kill();
                    }
                    relayProcess.Dispose();
                }
                if (Directory.Exists(root))
                    DeleteDirectoryWhenReleased(root);
            }
        }

        [Fact]
        public void RuntimeSelectionDelegatesReplacementLaunchToTheExitAwareRelay()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var restart = TestSources.Slice(
                source,
                "private static void RestartEditorInCleanProcess",
                "private static string BuildCleanRestartPath");

            Assert.Contains("Ros2ForUnityEditorRestartRelay.CreateStartInfo", restart, StringComparison.Ordinal);
            Assert.DoesNotContain("FileName = editorExecutable", restart, StringComparison.Ordinal);
            Assert.DoesNotContain("Arguments = \"-projectPath \"", restart, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.Exit(0);", restart, StringComparison.Ordinal);
        }

        [Fact]
        public void RestartButtonIsDisabledWhileUnityCompilesOrRefreshesPackages()
        {
            var inspector = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");

            Assert.Contains("var restartBlockedByEditorState =", inspector, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.isCompiling", inspector, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.isUpdating", inspector, StringComparison.Ordinal);
            Assert.Contains("DisabledScope(restartBlockedByEditorState)", inspector, StringComparison.Ordinal);
        }

        private static ProcessStartInfo CreateReplacementStartInfo()
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
            };
            startInfo.EnvironmentVariables["RMW_IMPLEMENTATION"] = "rmw_zenoh_cpp";
            startInfo.EnvironmentVariables["ZENOH_SESSION_CONFIG_URI"] = "tcp/127.0.0.1:8778";
            return startInfo;
        }

        private static string DecodePowerShellScript(string arguments)
        {
            const string marker = "-EncodedCommand ";
            var markerIndex = arguments.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(markerIndex >= 0, "Expected a PowerShell encoded command.");
            var encoded = arguments.Substring(markerIndex + marker.Length).Trim();
            return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        }

        private static Process StartSleepingPowerShell(
            string powershell,
            int seconds = 2)
        {
            var command = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(
                    "Start-Sleep -Seconds "
                    + seconds.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)));
            var startInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + command,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Could not start the isolated relay fixture process.");
        }

        private static Process StartRelayWithoutWindowSuppression(ProcessStartInfo startInfo)
        {
            // Window suppression is asserted above. Execute the same encoded relay script
            // without hidden-process flags so endpoint protection cannot suspend the
            // behavioral fixture before its process/lock assertions run.
            const string hiddenWindowArgument = "-WindowStyle Hidden ";
            Assert.True(startInfo.CreateNoWindow);
            Assert.Contains(hiddenWindowArgument, startInfo.Arguments, StringComparison.Ordinal);
            startInfo.CreateNoWindow = false;
            startInfo.Arguments = startInfo.Arguments.Replace(
                hiddenWindowArgument,
                string.Empty,
                StringComparison.Ordinal);
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Could not start the restart relay fixture.");
        }

        private static int CurrentProcessId()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private static string WindowsPowerShellExecutable()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Assert.True(File.Exists(path), "Windows PowerShell executable was not found: " + path);
            return path;
        }

        private static string PosixTrueExecutable()
        {
            foreach (var path in new[] { "/usr/bin/true", "/bin/true" })
            {
                if (File.Exists(path))
                    return path;
            }

            throw new FileNotFoundException("Could not locate the POSIX true executable.");
        }

        private static string RepositoryBuildTestRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                {
                    return Path.Combine(directory.FullName, "build", "Tests", "EditorRestartRelay");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository build root.");
        }

        private static void DeleteDirectoryWhenReleased(string directory)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static string[] ReadAllLinesWhenReleased(string path)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    return File.ReadAllLines(path);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
