// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Modifications by Jianbin Liu:
// - Added an Editor-only custom typesupport native plugin registration contract.

using System;
using System.IO;
using UnityEngine;

namespace ROS2
{
/// <summary>
/// Registers the selected Editor custom-typesupport package before ROS 2 native initialization.
/// </summary>
/// <remarks>
/// Player builds deliberately do not inspect Unity Package Manager packages. Their plugin layout is
/// determined by the Player packaging step. This class only records an Editor plugin directory with
/// ros2cs; it never creates ROS entities, executors, or transport endpoints.
/// </remarks>
public static class Ros2ForUnityNativePluginBootstrap
{
    private static readonly object registrationMutex = new object();
    private static bool nativeInitializationStarted;

    /// <summary>
    /// Registers a selected Package Manager add-on's Windows native plugin directory in the Unity Editor.
    /// </summary>
    /// <param name="packageRoot">Resolved package root containing Runtime/Ros2ForUnity/Plugins/Windows/x86_64.</param>
    /// <returns>True when an Editor Windows directory was registered; false for non-Editor or non-Windows runtimes.</returns>
    /// <exception cref="ArgumentException">Thrown when the selected package does not provide the expected plugin directory.</exception>
    /// <exception cref="InvalidOperationException">Thrown when registration occurs after ROS 2 native initialization begins.</exception>
    public static bool RegisterEditorPackagePluginDirectory(string packageRoot)
    {
#if UNITY_EDITOR
        if (Application.platform != RuntimePlatform.WindowsEditor)
        {
            return false;
        }

        if (String.IsNullOrWhiteSpace(packageRoot))
        {
            throw new ArgumentException("Custom typesupport package root cannot be empty.", nameof(packageRoot));
        }

        string pluginDirectory = Path.Combine(
            Path.GetFullPath(packageRoot),
            "Runtime",
            "Ros2ForUnity",
            "Plugins",
            "Windows",
            "x86_64");
        if (!Directory.Exists(pluginDirectory))
        {
            throw new ArgumentException(
                "Selected custom typesupport package has no Windows plugin directory: " + pluginDirectory,
                nameof(packageRoot));
        }

        lock (registrationMutex)
        {
            if (nativeInitializationStarted)
            {
                throw new InvalidOperationException(
                    "Register the selected custom typesupport package before ROS2ForUnity creates its ROS 2 context.");
            }

            GlobalVariables.RegisterNativeLibraryDirectory(pluginDirectory);
            return true;
        }
#else
        return false;
#endif
    }

    /// <summary>Prevents late Editor registrations after ros2cs native initialization begins.</summary>
    internal static void SealNativeLibraryRegistration()
    {
        lock (registrationMutex)
        {
            nativeInitializationStarted = true;
        }
    }

    /// <summary>Allows a later Editor Play session to register before a new ros2cs initialization attempt.</summary>
    internal static void ResetNativeLibraryRegistration()
    {
        lock (registrationMutex)
        {
            nativeInitializationStarted = false;
        }
    }
}
}
