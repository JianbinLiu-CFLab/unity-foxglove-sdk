// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: ROS-free source-package preflight for the Phase181 static interface package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Source-package state only. This enum intentionally does not share or
    /// translate to the later Player-safe typesupport add-on readiness enum.
    /// The similarly named states describe different ownership boundaries.
    /// </summary>
    public enum FoxRunRos2InterfaceSourcePreflightState
    {
        NotRequired = 0,
        ReadyForBuild = 1,
        MissingSource = 2,
        StaleSource = 3,
        InvalidSource = 4,
        RevisionRequired = 5
    }

    public enum FoxRunRos2InterfaceSourcePreflightDiagnosticCode
    {
        None = 0,
        NoCustomContracts = 1,
        SourcePackageMissing = 2,
        SourceLockMissing = 3,
        SourceLockInvalid = 4,
        SourceFileMissing = 5,
        SourceFileChanged = 6,
        SourceMessageUnexpected = 7,
        LockedSchemaChanged = 8,
        InvalidCustomContract = 9
    }

    public sealed class FoxRunRos2InterfaceSourcePreflightContract
    {
        internal FoxRunRos2InterfaceSourcePreflightContract(
            string declaringType,
            string memberName,
            string dtoIdentity,
            string payloadMessageName,
            string envelopeMessageName)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            DtoIdentity = dtoIdentity ?? string.Empty;
            PayloadMessageName = payloadMessageName ?? string.Empty;
            EnvelopeMessageName = envelopeMessageName ?? string.Empty;
        }

        public string DeclaringType { get; }
        public string MemberName { get; }
        public string DtoIdentity { get; }
        public string PayloadMessageName { get; }
        public string EnvelopeMessageName { get; }
    }

    public sealed class FoxRunRos2InterfaceSourcePreflightResult
    {
        internal FoxRunRos2InterfaceSourcePreflightResult(
            FoxRunRos2InterfaceSourcePreflightState state,
            FoxRunRos2InterfaceSourcePreflightDiagnosticCode diagnosticCode,
            string rosPackageName,
            string interfaceDigest,
            string action,
            IReadOnlyList<FoxRunRos2InterfaceSourcePreflightContract> contracts)
        {
            State = state;
            DiagnosticCode = diagnosticCode;
            RosPackageName = rosPackageName ?? string.Empty;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            ShortDigest = InterfaceDigest.Length >= 12 ? InterfaceDigest.Substring(0, 12) : InterfaceDigest;
            Action = action ?? string.Empty;
            Contracts = (contracts ?? Array.Empty<FoxRunRos2InterfaceSourcePreflightContract>()).ToArray();
        }

        public FoxRunRos2InterfaceSourcePreflightState State { get; }
        public FoxRunRos2InterfaceSourcePreflightDiagnosticCode DiagnosticCode { get; }
        public string RosPackageName { get; }
        public string InterfaceDigest { get; }
        public string ShortDigest { get; }
        public string Action { get; }
        public IReadOnlyList<FoxRunRos2InterfaceSourcePreflightContract> Contracts { get; }
        public bool IsReady => State == FoxRunRos2InterfaceSourcePreflightState.NotRequired
                               || State == FoxRunRos2InterfaceSourcePreflightState.ReadyForBuild;
    }

    /// <summary>
    /// Checks the tracked, source-only UPM interface package. It never loads a
    /// native DLL, queries RMW capabilities, or inspects a typesupport add-on.
    /// </summary>
    public static class FoxRunRos2InterfacePackagePreflight
    {
        private const string LockRelativePath = "RuntimeSupport/foxrun-ros2-interface-lock.json";

        public static FoxRunRos2InterfaceSourcePreflightResult Evaluate(
            string packageRoot,
            FoxRunGenerationModel model)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
                throw new ArgumentException("The static interface package root is required.", nameof(packageRoot));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            FoxRunRos2InterfaceRenderedPackage requested;
            try
            {
                requested = FoxRunRos2InterfacePackageRenderer.Render(model);
            }
            catch (FoxRunRos2InterfaceRenderException)
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.InvalidSource,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.InvalidCustomContract,
                    string.Empty,
                    string.Empty,
                    "Fix the custom DTO contract before generating the static ROS2 interface package.",
                    Array.Empty<FoxRunRos2InterfaceSourcePreflightContract>());
            }

            if (!requested.HasCustomContracts)
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.NotRequired,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.NoCustomContracts,
                    string.Empty,
                    string.Empty,
                    "No custom FoxRun ROS2 DTO contracts require a source package.",
                    Array.Empty<FoxRunRos2InterfaceSourcePreflightContract>());
            }

            var requestedContracts = Contracts(requested.Lock);
            if (!Directory.Exists(packageRoot))
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.MissingSource,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourcePackageMissing,
                    requested.Lock.RosPackageName,
                    requested.InterfaceDigest,
                    "Generate the initial locked static ROS2 interface package.",
                    requestedContracts);
            }

            var lockPath = GetPath(packageRoot, LockRelativePath);
            if (!File.Exists(lockPath))
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.InvalidSource,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockMissing,
                    requested.Lock.RosPackageName,
                    requested.InterfaceDigest,
                    "Restore or explicitly generate a valid source-package lock before building typesupport.",
                    requestedContracts);
            }

            FoxRunRos2InterfaceLock currentLock;
            try
            {
                currentLock = FoxRunRos2InterfaceLock.Parse(File.ReadAllText(lockPath));
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.InvalidSource,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockInvalid,
                    requested.Lock.RosPackageName,
                    requested.InterfaceDigest,
                    "Restore a deterministic lock; this source package must not be rebuilt from an invalid lock.",
                    requestedContracts);
            }

            FoxRunRos2InterfaceRenderedPackage current;
            try
            {
                current = FoxRunRos2InterfacePackageRenderer.Render(model, currentLock.RosPackageName);
            }
            catch (FoxRunRos2InterfaceRenderException)
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.InvalidSource,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.InvalidCustomContract,
                    currentLock.RosPackageName,
                    currentLock.InterfaceDigest,
                    "Fix the custom DTO contract before evaluating the existing source package.",
                    Contracts(currentLock));
            }

            if (!string.Equals(currentLock.InterfaceDigest, current.InterfaceDigest, StringComparison.Ordinal))
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.RevisionRequired,
                    FoxRunRos2InterfaceSourcePreflightDiagnosticCode.LockedSchemaChanged,
                    currentLock.RosPackageName,
                    current.InterfaceDigest,
                    "Generate the exact next _vN ROS package revision; do not overwrite the locked wire schema.",
                    Contracts(current.Lock));
            }

            var lockIntegrity = ValidateRecordedMessageDigests(packageRoot, currentLock);
            if (lockIntegrity != FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None)
            {
                return Result(
                    lockIntegrity == FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceFileMissing
                        ? FoxRunRos2InterfaceSourcePreflightState.StaleSource
                        : FoxRunRos2InterfaceSourcePreflightState.InvalidSource,
                    lockIntegrity,
                    currentLock.RosPackageName,
                    currentLock.InterfaceDigest,
                    "Restore the locked generated messages and their recorded digest before building typesupport.",
                    Contracts(currentLock));
            }

            var stale = FindStaleGeneratedPath(packageRoot, current);
            if (stale != null)
            {
                return Result(
                    FoxRunRos2InterfaceSourcePreflightState.StaleSource,
                    stale.Value.Code,
                    currentLock.RosPackageName,
                    currentLock.InterfaceDigest,
                    "Regenerate only after confirming the existing lock still represents the intended wire schema.",
                    Contracts(currentLock));
            }

            return Result(
                FoxRunRos2InterfaceSourcePreflightState.ReadyForBuild,
                FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None,
                currentLock.RosPackageName,
                currentLock.InterfaceDigest,
                "Source package is locked and ready for the separate typesupport build step.",
                Contracts(currentLock));
        }

        private static FoxRunRos2InterfaceSourcePreflightDiagnosticCode ValidateRecordedMessageDigests(
            string packageRoot,
            FoxRunRos2InterfaceLock @lock)
        {
            foreach (var contract in @lock.Contracts)
            {
                if (!string.Equals(
                        contract.EnvelopeMessageName,
                        FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName(contract.PayloadMessageName),
                        StringComparison.Ordinal))
                {
                    return FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockInvalid;
                }

                var payloadPath = GetPath(packageRoot, "Ros2Package~/msg/" + contract.PayloadMessageName + ".msg");
                var envelopePath = GetPath(packageRoot, "Ros2Package~/msg/" + contract.EnvelopeMessageName + ".msg");
                if (!File.Exists(payloadPath) || !File.Exists(envelopePath))
                    return FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceFileMissing;

                var payloadDigest = FoxRunRos2InterfaceDigest.Compute(
                    FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                    new[] { new FoxRunRos2InterfaceDigestInput("Ros2Package~/msg/" + contract.PayloadMessageName + ".msg", File.ReadAllBytes(payloadPath)) });
                var envelopeDigest = FoxRunRos2InterfaceDigest.Compute(
                    FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                    new[] { new FoxRunRos2InterfaceDigestInput("Ros2Package~/msg/" + contract.EnvelopeMessageName + ".msg", File.ReadAllBytes(envelopePath)) });
                if (!string.Equals(payloadDigest, contract.MessageDigest, StringComparison.Ordinal)
                    || !string.Equals(envelopeDigest, contract.EnvelopeDigest, StringComparison.Ordinal))
                {
                    return FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockInvalid;
                }
            }

            return FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None;
        }

        private static StalePath? FindStaleGeneratedPath(
            string packageRoot,
            FoxRunRos2InterfaceRenderedPackage expected)
        {
            foreach (var file in expected.Files)
            {
                // The lock records the provenance that created the static wire
                // package. New contracts may reuse an already locked DTO shape,
                // which leaves every wire artifact and the interface digest
                // unchanged while legitimately adding authoring-only metadata.
                // ValidateRecordedMessageDigests above verifies the persisted
                // lock against the generated message files; do not compare it
                // to the current authoring model here.
                if (string.Equals(file.RelativePath, LockRelativePath, StringComparison.Ordinal))
                    continue;

                var path = GetPath(packageRoot, file.RelativePath);
                if (!File.Exists(path))
                    return new StalePath(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceFileMissing);
                if (!File.ReadAllBytes(path).SequenceEqual(file.Bytes))
                    return new StalePath(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceFileChanged);
            }

            var expectedMessages = new HashSet<string>(
                expected.Files
                    .Where(file => file.RelativePath.StartsWith("Ros2Package~/msg/", StringComparison.Ordinal)
                                   && file.RelativePath.EndsWith(".msg", StringComparison.Ordinal))
                    .Select(file => file.RelativePath),
                StringComparer.Ordinal);
            var messageDirectory = Path.Combine(packageRoot, "Ros2Package~", "msg");
            if (Directory.Exists(messageDirectory))
            {
                foreach (var actualPath in Directory.GetFiles(messageDirectory, "*.msg", SearchOption.TopDirectoryOnly))
                {
                    var relativePath = "Ros2Package~/msg/" + Path.GetFileName(actualPath);
                    if (!expectedMessages.Contains(relativePath))
                        return new StalePath(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceMessageUnexpected);
                }
            }

            return null;
        }

        private static IReadOnlyList<FoxRunRos2InterfaceSourcePreflightContract> Contracts(FoxRunRos2InterfaceLock @lock)
            => (@lock?.Contracts ?? Array.Empty<FoxRunRos2InterfaceContractLock>())
                .Select(contract => new FoxRunRos2InterfaceSourcePreflightContract(
                    contract.DeclaringType,
                    contract.MemberName,
                    contract.DtoIdentity,
                    contract.PayloadMessageName,
                    contract.EnvelopeMessageName))
                .ToArray();

        private static FoxRunRos2InterfaceSourcePreflightResult Result(
            FoxRunRos2InterfaceSourcePreflightState state,
            FoxRunRos2InterfaceSourcePreflightDiagnosticCode diagnosticCode,
            string rosPackageName,
            string interfaceDigest,
            string action,
            IReadOnlyList<FoxRunRos2InterfaceSourcePreflightContract> contracts)
            => new FoxRunRos2InterfaceSourcePreflightResult(
                state,
                diagnosticCode,
                rosPackageName,
                interfaceDigest,
                action,
                contracts);

        private static string GetPath(string root, string relativePath)
            => Path.Combine(root, FoxRunRos2InterfaceDigest.NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));

        private readonly struct StalePath
        {
            public StalePath(FoxRunRos2InterfaceSourcePreflightDiagnosticCode code)
            {
                Code = code;
            }

            public FoxRunRos2InterfaceSourcePreflightDiagnosticCode Code { get; }
        }
    }
}
