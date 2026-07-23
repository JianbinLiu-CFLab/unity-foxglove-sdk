// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-local Zenoh router endpoint selection shared by Editor R2FU sessions and local smoke helpers.

#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal readonly struct Ros2ForUnityZenohRouterEndpoint : IEquatable<Ros2ForUnityZenohRouterEndpoint>
    {
        public const string DefaultZenohRouterAddress = "localhost";
        public const int DefaultZenohRouterPort = 8778;

        public Ros2ForUnityZenohRouterEndpoint(string address, int port)
        {
            Address = address;
            Port = port;
        }

        public string Address { get; }
        public int Port { get; }
        public string Endpoint => "tcp/" + FormatAddressForEndpoint(Address) + ":" + Port.ToString(CultureInfo.InvariantCulture);

        public bool Equals(Ros2ForUnityZenohRouterEndpoint other)
            => string.Equals(Address, other.Address, StringComparison.OrdinalIgnoreCase)
               && Port == other.Port;

        public override bool Equals(object obj)
            => obj is Ros2ForUnityZenohRouterEndpoint other && Equals(other);

        public override int GetHashCode()
            => StringComparer.OrdinalIgnoreCase.GetHashCode(Address ?? string.Empty) ^ Port;

        public override string ToString() => Endpoint;

        public static bool TryCreate(
            string address,
            int port,
            out Ros2ForUnityZenohRouterEndpoint endpoint,
            out string error)
        {
            var normalizedAddress = (address ?? string.Empty).Trim();
            endpoint = default;
            error = string.Empty;
            if (normalizedAddress.Length == 0 || normalizedAddress.Length > 255)
            {
                error = "Router Address must contain one host name or IP address.";
                return false;
            }

            if (normalizedAddress.StartsWith("[", StringComparison.Ordinal)
                || normalizedAddress.EndsWith("]", StringComparison.Ordinal))
            {
                if (!normalizedAddress.StartsWith("[", StringComparison.Ordinal)
                    || !normalizedAddress.EndsWith("]", StringComparison.Ordinal)
                    || normalizedAddress.Length <= 2)
                {
                    error = "Router Address has unmatched IPv6 brackets.";
                    return false;
                }

                normalizedAddress = normalizedAddress.Substring(1, normalizedAddress.Length - 2);
            }

            if (normalizedAddress.IndexOfAny(new[] { '/', '\\', '"', '\'', ';', ' ', '\t', '\r', '\n' }) >= 0)
            {
                error = "Router Address must not contain a URI scheme, path, whitespace, or configuration syntax.";
                return false;
            }

            if (!IPAddress.TryParse(normalizedAddress, out _)
                && Uri.CheckHostName(normalizedAddress) == UriHostNameType.Unknown)
            {
                error = "Router Address must be a valid host name or IP address.";
                return false;
            }

            if (port < 1 || port > 65535)
            {
                error = "Router Port must be between 1 and 65535.";
                return false;
            }

            endpoint = new Ros2ForUnityZenohRouterEndpoint(normalizedAddress, port);
            return true;
        }

        private static string FormatAddressForEndpoint(string address)
        {
            if (IPAddress.TryParse(address, out var parsed)
                && parsed.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return "[" + address + "]";
            }

            return address;
        }
    }

    internal static class Ros2ForUnityZenohRouterSettings
    {
#if UNITY_EDITOR_WIN
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int _wputenv_s(string name, string value);
#endif

        private const string ZenohRouterAddressEditorUserSettingsKey = "Unity2Foxglove.R2FU.ZenohRouterAddress";
        private const string ZenohRouterPortEditorUserSettingsKey = "Unity2Foxglove.R2FU.ZenohRouterPort";
        private const string RouterSettingsRelativePath = "Library/Unity2Foxglove/R2fuZenohRouterSettings.json";
        private const string GeneratedSessionRelativeDirectory = "Library/Unity2Foxglove/Zenoh";
        private const string SessionTemplateRelativePath =
            "Runtime/Ros2ForUnity/StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5";
        private const string DefaultSessionTemplateEndpoint = "tcp/localhost:7447";
        private const string ZenohRouterConfigEnvironmentVariable = "ZENOH_ROUTER_CONFIG_URI";
        private const string ZenohSessionConfigEnvironmentVariable = "ZENOH_SESSION_CONFIG_URI";
        private const string ZenohConfigOverrideEnvironmentVariable = "ZENOH_CONFIG_OVERRIDE";

        public static Ros2ForUnityZenohRouterEndpoint Get(Ros2ForUnityRuntimeDescriptor runtime)
        {
            var address = EditorUserSettings.GetConfigValue(GetAddressSettingsKey(runtime));
            var portText = EditorUserSettings.GetConfigValue(GetPortSettingsKey(runtime));
            if (string.IsNullOrWhiteSpace(address))
                address = Ros2ForUnityZenohRouterEndpoint.DefaultZenohRouterAddress;
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                port = Ros2ForUnityZenohRouterEndpoint.DefaultZenohRouterPort;

            return Ros2ForUnityZenohRouterEndpoint.TryCreate(address, port, out var endpoint, out _)
                ? endpoint
                : new Ros2ForUnityZenohRouterEndpoint(
                    Ros2ForUnityZenohRouterEndpoint.DefaultZenohRouterAddress,
                    Ros2ForUnityZenohRouterEndpoint.DefaultZenohRouterPort);
        }

        public static bool TrySet(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string address,
            string portText,
            out string error)
        {
            error = string.Empty;
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            {
                error = "Router Port must be a whole number between 1 and 65535.";
                return false;
            }

            if (!Ros2ForUnityZenohRouterEndpoint.TryCreate(address, port, out var endpoint, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                error = "Could not resolve the Unity project directory.";
                return false;
            }

            EditorUserSettings.SetConfigValue(GetAddressSettingsKey(runtime), endpoint.Address);
            EditorUserSettings.SetConfigValue(
                GetPortSettingsKey(runtime),
                endpoint.Port.ToString(CultureInfo.InvariantCulture));
            WriteProjectSettings(projectDirectory, endpoint);
            Ros2ForUnityRuntimeSelection.ApplyCommunicationModeEnvironment(projectDirectory);
            return true;
        }

        public static void ApplyToCurrentProcess(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string rmwImplementation)
        {
            ClearCurrentProcessZenohConfiguration();
            if (!string.Equals(
                    rmwImplementation,
                    Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
                    StringComparison.Ordinal))
            {
                return;
            }

            var endpoint = Get(runtime);
            WriteProjectSettings(projectDirectory, endpoint);
            SetProcessEnvironmentVariable(ZenohSessionConfigEnvironmentVariable,
                EnsureSessionConfiguration(projectDirectory, runtime, endpoint));
        }

        public static void ApplyToRestartProcess(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string rmwImplementation,
            ProcessStartInfo startInfo)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));

            ClearRestartProcessZenohConfiguration(startInfo);
            if (!string.Equals(
                    rmwImplementation,
                    Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
                    StringComparison.Ordinal))
            {
                return;
            }

            var endpoint = Get(runtime);
            WriteProjectSettings(projectDirectory, endpoint);
            startInfo.EnvironmentVariables[ZenohSessionConfigEnvironmentVariable] =
                EnsureSessionConfiguration(projectDirectory, runtime, endpoint);
        }

        private static string EnsureSessionConfiguration(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            Ros2ForUnityZenohRouterEndpoint endpoint)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.PackageName))
                throw new InvalidOperationException("Select one ROS2 For Unity runtime before configuring Zenoh.");

            var package = UnityEditor.PackageManager.PackageInfo.FindForPackageName(runtime.PackageName);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidOperationException(
                    "Could not resolve the selected ROS2 For Unity runtime package for Zenoh configuration.");
            }

            var templatePath = Path.Combine(package.resolvedPath, SessionTemplateRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!FileExistsForVerification(templatePath))
                throw new InvalidOperationException("The selected ROS2 For Unity runtime does not contain its Zenoh session template.");

            string template;
            try
            {
                template = ReadAllTextForVerification(templatePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Could not read the selected ROS2 For Unity Zenoh session template.", exception);
            }

            if (!template.Contains(DefaultSessionTemplateEndpoint))
            {
                throw new InvalidOperationException(
                    "The selected ROS2 For Unity Zenoh session template has no supported router endpoint marker.");
            }

            var sessionConfiguration = template.Replace(DefaultSessionTemplateEndpoint, endpoint.Endpoint);
            var fileName = runtime.PackageName + ".session.json5";
            var outputPath = Path.Combine(
                Path.GetFullPath(projectDirectory),
                GeneratedSessionRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
                fileName);
            WriteTextAtomically(outputPath, sessionConfiguration);
            return outputPath;
        }

        private static void WriteProjectSettings(
            string projectDirectory,
            Ros2ForUnityZenohRouterEndpoint endpoint)
        {
            var path = Path.Combine(
                Path.GetFullPath(projectDirectory),
                RouterSettingsRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var document = new JObject
            {
                ["schemaVersion"] = 1,
                ["routerAddress"] = endpoint.Address,
                ["routerPort"] = endpoint.Port,
                ["endpoint"] = endpoint.Endpoint,
            };
            WriteTextAtomically(path, document.ToString(Formatting.None));
        }

        private static bool FileExistsForVerification(string path)
        {
            try
            {
                using (OpenForVerificationRead(path))
                    return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadAllTextForVerification(string path)
        {
            using (var stream = OpenForVerificationRead(path))
            using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                return reader.ReadToEnd();
        }

        private static FileStream OpenForVerificationRead(string path)
            => new FileStream(
                NormalizeWindowsLongPathForRead(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        private static string NormalizeWindowsLongPathForRead(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\'
                || fullPath.Length < 248
                || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return fullPath;
            }

            return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + fullPath.Substring(2)
                : @"\\?\" + fullPath;
        }

        private static string GetAddressSettingsKey(Ros2ForUnityRuntimeDescriptor runtime)
            => GetSettingsKey(ZenohRouterAddressEditorUserSettingsKey, runtime);

        private static string GetPortSettingsKey(Ros2ForUnityRuntimeDescriptor runtime)
            => GetSettingsKey(ZenohRouterPortEditorUserSettingsKey, runtime);

        private static string GetSettingsKey(string key, Ros2ForUnityRuntimeDescriptor runtime)
            => runtime == null || string.IsNullOrWhiteSpace(runtime.PackageName)
                ? key
                : key + "." + runtime.PackageName;

        private static void ClearCurrentProcessZenohConfiguration()
        {
            SetProcessEnvironmentVariable(ZenohRouterConfigEnvironmentVariable, null);
            SetProcessEnvironmentVariable(ZenohSessionConfigEnvironmentVariable, null);
            SetProcessEnvironmentVariable(ZenohConfigOverrideEnvironmentVariable, null);
        }

        private static void SetProcessEnvironmentVariable(string name, string value)
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

        private static void ClearRestartProcessZenohConfiguration(ProcessStartInfo startInfo)
        {
            startInfo.EnvironmentVariables.Remove(ZenohRouterConfigEnvironmentVariable);
            startInfo.EnvironmentVariables.Remove(ZenohSessionConfigEnvironmentVariable);
            startInfo.EnvironmentVariables.Remove(ZenohConfigOverrideEnvironmentVariable);
        }

        private static void WriteTextAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Could not resolve the Zenoh configuration output directory.");

            Directory.CreateDirectory(directory);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, content ?? string.Empty);
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, path, overwrite: true);
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, path, overwrite: true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
#endif
