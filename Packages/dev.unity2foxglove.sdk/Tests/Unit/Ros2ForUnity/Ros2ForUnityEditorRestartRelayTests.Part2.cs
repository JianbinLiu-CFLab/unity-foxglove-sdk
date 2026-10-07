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


public sealed partial class Ros2ForUnityEditorRestartRelayTests
    {
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
