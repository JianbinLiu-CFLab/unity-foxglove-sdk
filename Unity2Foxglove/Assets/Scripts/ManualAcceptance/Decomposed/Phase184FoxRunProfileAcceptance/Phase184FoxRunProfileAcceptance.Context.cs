// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    public sealed partial class Phase184FoxRunProfileAcceptance
    {
        private void FailContext(string reason)
        {
            _contextValidated = false;
            _status = Phase184AcceptanceText.Bound(reason, MaximumStatusCharacters);
            Debug.LogError(
                "PHASE184G_CONTEXT_FAIL reason="
                + Phase184AcceptanceText.SafeMarker(_status),
                this);
        }

        private static bool TryLoadContext(
            out Phase184AcceptanceRunContext context,
            out string error)
        {
            context = default;
            error = string.Empty;
            var isBatchContext = false;
            try
            {
                var hasBatchConfigArgument = TryReadCommandLineValue(
                    BatchConfigArgument,
                    out var configPath);
                isBatchContext = Application.isBatchMode || hasBatchConfigArgument;
                var pointerToken = string.Empty;
                if (isBatchContext && string.IsNullOrWhiteSpace(configPath))
                {
                    error = Phase184ContextDiagnostic.Format(
                        "The Batch Phase184 run config argument is missing or blank.",
                        isManual: false);
                    return false;
                }

                if (!isBatchContext)
                {
                    var repository = Directory.GetParent(
                        Directory.GetParent(Application.dataPath)?.FullName
                        ?? string.Empty)?.FullName;
                    if (string.IsNullOrWhiteSpace(repository))
                    {
                        error = Phase184ContextDiagnostic.Format(
                            "Could not resolve the repository root for manual-active.json.",
                            isManual: !isBatchContext);
                        return false;
                    }

                    var pointerPath = Path.Combine(
                        repository,
                        ManualPointerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!TryResolveManualPointer(
                            pointerPath,
                            repository,
                            out configPath,
                            out pointerToken,
                            out error))
                    {
                        error = Phase184ContextDiagnostic.Format(
                            error,
                            isManual: !isBatchContext);
                        return false;
                    }
                }

                var fullPath = Path.GetFullPath(configPath);
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumConfigBytes)
                {
                    error = Phase184ContextDiagnostic.Format(
                        "The Phase184 run config is missing, empty, or oversized.",
                        isManual: !isBatchContext);
                    return false;
                }

                var json = JObject.Parse(File.ReadAllText(fullPath));
                var caseId = (string)json["case"] ?? string.Empty;
                var token = (string)json["token"] ?? string.Empty;
                var executionMode = (string)json["executionMode"] ?? string.Empty;
                var windows = json["observationWindows"] as JObject;
                var negativeSeconds = (int?)windows?["negativeSeconds"] ?? 3;
                if (!IsKnownCase(caseId)
                    || !Phase184AcceptanceText.IsSafeToken(token)
                    || (!string.IsNullOrEmpty(pointerToken)
                        && !string.Equals(token, pointerToken, StringComparison.Ordinal))
                    || (isBatchContext && executionMode != "batch")
                    || (!isBatchContext && executionMode != "manual")
                    || negativeSeconds < 1
                    || negativeSeconds > 30)
                {
                    error = Phase184ContextDiagnostic.Format(
                        "The Phase184 run config has an invalid case, token, mode, or window.",
                        isManual: !isBatchContext);
                    return false;
                }

                context = new Phase184AcceptanceRunContext(
                    caseId,
                    token,
                    Math.Max(1, negativeSeconds));
                return true;
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is Newtonsoft.Json.JsonException
                || exception is ArgumentException
                || exception is InvalidOperationException)
            {
                error = Phase184ContextDiagnostic.Format(
                    exception.GetType().Name + ": " + exception.Message,
                    isManual: !isBatchContext);
                return false;
            }
        }

        private static bool TryResolveManualPointer(
            string pointerPath,
            string repository,
            out string configPath,
            out string pointerToken,
            out string error)
        {
            configPath = string.Empty;
            pointerToken = string.Empty;
            error = string.Empty;
            var info = new FileInfo(pointerPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumConfigBytes)
            {
                error = "No valid Phase184 manual-active pointer is present.";
                return false;
            }

            var pointer = JObject.Parse(File.ReadAllText(pointerPath));
            configPath = (string)pointer["runConfig"] ?? string.Empty;
            pointerToken = (string)pointer["token"] ?? string.Empty;
            var helperPid = (int?)pointer["helperPid"] ?? 0;
            var helperCreated = (double?)pointer["helperCreationUnixSeconds"] ?? 0d;
            var expires = (string)pointer["expiresUtc"] ?? string.Empty;
            if (!Phase184AcceptanceText.IsSafeToken(pointerToken)
                || helperPid <= 0
                || helperCreated <= 0d
                || !DateTimeOffset.TryParse(
                    expires,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var expiry)
                || expiry <= DateTimeOffset.UtcNow)
            {
                error = "The Phase184 manual-active pointer is stale or malformed.";
                return false;
            }

            try
            {
                using var helper = Process.GetProcessById(helperPid);
                var actualCreated =
                    new DateTimeOffset(helper.StartTime.ToUniversalTime())
                        .ToUnixTimeMilliseconds() / 1000d;
                if (Math.Abs(actualCreated - helperCreated) > 2d)
                {
                    error = "The Phase184 helper identity no longer matches the pointer.";
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is System.ComponentModel.Win32Exception)
            {
                error = "The Phase184 helper process is no longer alive.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                error = "The Phase184 manual-active pointer has no runConfig path.";
                return false;
            }
            var fullConfigPath = Path.GetFullPath(configPath);
            var acceptanceRoot = Path.GetFullPath(
                Path.Combine(repository, "build", "phase184", "acceptance"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullConfigPath.StartsWith(
                    acceptanceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The Phase184 run config escaped the owned acceptance root.";
                return false;
            }
            configPath = fullConfigPath;
            return true;
        }

        private static bool IsKnownCase(string value)
            => value == FoxgloveProfileCase
               || value == MultiTargetCase
               || value == DegradedTargetCase
               || value == QosContractCase
               || value == StreamCase;

        private static bool TryReadCommandLineValue(string name, out string value)
        {
            value = string.Empty;
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    if (index + 1 < arguments.Length)
                        value = arguments[index + 1];
                    return true;
                }
            }
            return false;
        }

        private void Emit(string marker, string fields)
            => Debug.Log(marker + " " + fields, this);
    }
}
