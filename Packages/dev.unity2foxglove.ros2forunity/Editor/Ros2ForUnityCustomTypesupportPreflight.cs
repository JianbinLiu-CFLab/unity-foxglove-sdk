// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Bounded Edit-Mode preflight for selected FoxRun custom ROS2 typesupport.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// This readiness vocabulary is deliberately separate from the 181-B
    /// source-package preflight. It describes a selected runtime/add-on pair
    /// and its metadata, not whether the source package may be generated.
    /// Player endpoint creation remains guarded by the registered runtime
    /// catalog evaluator; this Edit-Mode model never loads it.
    /// </summary>
    internal enum Ros2ForUnityCustomTypesupportPreflightCode
    {
        NotRequired = 0,
        MissingSource = 1,
        StaleSource = 2,
        MissingAddOn = 3,
        MultipleAddOns = 4,
        DistributionMismatch = 5,
        DigestMismatch = 6,
        InvalidManifest = 7,
        InvalidInventory = 8,
        MissingManagedType = 9,
        MissingCatalog = 10,
        DuplicateCatalog = 11,
        UnsupportedRmw = 12,
        Settling = 13,
        Ready = 14,
    }

    public sealed class Ros2ForUnityCustomTypesupportContract
    {
        public Ros2ForUnityCustomTypesupportContract(string canonicalEnvelopeType, string directionalPolicy)
        {
            CanonicalEnvelopeType = canonicalEnvelopeType ?? string.Empty;
            DirectionalPolicy = Ros2ForUnityCustomTypesupportInspectorPresentation.ContractPolicyLabel(directionalPolicy);
        }

        public string CanonicalEnvelopeType { get; }
        public string DirectionalPolicy { get; }
    }

    /// <summary>
    /// Immutable inputs supplied by the authoritative 181-C runtime selector
    /// plus the core Inspector's generated contract metadata. The preflight
    /// consumes these facts and never substitutes its own selector/resolve.
    /// </summary>
    internal sealed class Ros2ForUnityCustomTypesupportPreflightInput
    {
        public Ros2ForUnityCustomTypesupportPreflightInput(
            string projectDirectory,
            bool hasCustomNativeContract,
            string selectedBaseRuntimePackage,
            string selectedDistribution,
            string selectedRmwImplementation,
            bool editorReloadSettled,
            bool customCompileSymbolDefined,
            Ros2ForUnityCustomTypesupportSelectionResult selection,
            IReadOnlyList<string> activeAddOnPackages,
            IReadOnlyList<string> candidateAddOnPackages,
            IReadOnlyList<Ros2ForUnityCustomTypesupportContract> contracts)
        {
            ProjectDirectory = projectDirectory ?? string.Empty;
            HasCustomNativeContract = hasCustomNativeContract;
            SelectedBaseRuntimePackage = selectedBaseRuntimePackage ?? string.Empty;
            SelectedDistribution = selectedDistribution ?? string.Empty;
            SelectedRmwImplementation = selectedRmwImplementation ?? string.Empty;
            EditorReloadSettled = editorReloadSettled;
            CustomCompileSymbolDefined = customCompileSymbolDefined;
            Selection = selection;
            ActiveAddOnPackages = (activeAddOnPackages ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            CandidateAddOnPackages = (candidateAddOnPackages ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Contracts = (contracts ?? Array.Empty<Ros2ForUnityCustomTypesupportContract>())
                .Where(contract => contract != null
                                   && !string.IsNullOrWhiteSpace(contract.CanonicalEnvelopeType))
                .OrderBy(contract => contract.CanonicalEnvelopeType, StringComparer.Ordinal)
                .ToArray();
        }

        public string ProjectDirectory { get; }
        public bool HasCustomNativeContract { get; }
        public string SelectedBaseRuntimePackage { get; }
        public string SelectedDistribution { get; }
        public string SelectedRmwImplementation { get; }
        public bool EditorReloadSettled { get; }
        public bool CustomCompileSymbolDefined { get; }
        public Ros2ForUnityCustomTypesupportSelectionResult Selection { get; }
        public IReadOnlyList<string> ActiveAddOnPackages { get; }
        public IReadOnlyList<string> CandidateAddOnPackages { get; }
        public IReadOnlyList<Ros2ForUnityCustomTypesupportContract> Contracts { get; }
    }

    internal sealed class Ros2ForUnityCustomTypesupportPreflightResult
    {
        internal Ros2ForUnityCustomTypesupportPreflightResult(
            Ros2ForUnityCustomTypesupportPreflightCode code,
            string diagnostic,
            string action,
            string staticPackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string activeAddOnPackage,
            string distribution,
            string rmwImplementation,
            IReadOnlyList<string> candidateAddOnPackages,
            IReadOnlyList<Ros2ForUnityCustomTypesupportContract> contracts)
        {
            Code = code;
            Diagnostic = diagnostic ?? string.Empty;
            Action = action ?? string.Empty;
            StaticPackageId = staticPackageId ?? string.Empty;
            RosPackageName = rosPackageName ?? string.Empty;
            InterfaceRevision = interfaceRevision;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            ShortInterfaceDigest = Ros2ForUnityCustomTypesupportInspectorPresentation.CompactDigest(InterfaceDigest);
            ActiveAddOnPackage = activeAddOnPackage ?? string.Empty;
            Distribution = distribution ?? string.Empty;
            RmwImplementation = rmwImplementation ?? string.Empty;
            CandidateAddOnPackages = (candidateAddOnPackages ?? Array.Empty<string>()).ToArray();
            Contracts = (contracts ?? Array.Empty<Ros2ForUnityCustomTypesupportContract>()).ToArray();
        }

        public Ros2ForUnityCustomTypesupportPreflightCode Code { get; }
        public string Diagnostic { get; }
        public string Action { get; }
        public string StaticPackageId { get; }
        public string RosPackageName { get; }
        public int InterfaceRevision { get; }
        public string InterfaceDigest { get; }
        public string ShortInterfaceDigest { get; }
        public string ActiveAddOnPackage { get; }
        public string Distribution { get; }
        public string RmwImplementation { get; }
        public IReadOnlyList<string> CandidateAddOnPackages { get; }
        public IReadOnlyList<Ros2ForUnityCustomTypesupportContract> Contracts { get; }
        public bool IsReady => Code == Ros2ForUnityCustomTypesupportPreflightCode.Ready;

        public string ToDisplaySummary()
            => Ros2ForUnityCustomTypesupportInspectorPresentation.StatusLabel(Code)
               + ": " + ShortInterfaceDigest
               + " / " + ActiveAddOnPackage
               + " / " + RmwImplementation;
    }

    /// <summary>
    /// Maps metadata from a manifest-resolved runtime/add-on pair to bounded
    /// UI status. This does not write manifest.json or packages-lock.json,
    /// call Client.Resolve, reconcile scripting defines, or initialize native
    /// ROS2. The 181-C selector and define installer retain all of that work.
    /// </summary>
    internal static class Ros2ForUnityCustomTypesupportPreflight
    {
        internal static Ros2ForUnityCustomTypesupportPreflightResult Evaluate(
            Ros2ForUnityCustomTypesupportPreflightInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (!input.HasCustomNativeContract)
            {
                return Result(
                    Ros2ForUnityCustomTypesupportPreflightCode.NotRequired,
                    input,
                    null,
                    activeAddOnPackage: string.Empty,
                    candidateAddOnPackages: Array.Empty<string>());
            }

            var activeAddOnPackage = input.ActiveAddOnPackages.Count == 1
                ? input.ActiveAddOnPackages[0]
                : string.Empty;
            var snapshot = Ros2ForUnityCustomTypesupportDiscovery.Discover(
                input.ProjectDirectory,
                activeAddOnPackage);
            if (!snapshot.Source.Present)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.MissingSource, input, snapshot, activeAddOnPackage);
            if (!snapshot.Source.Valid)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.StaleSource, input, snapshot, activeAddOnPackage);
            if (input.Selection?.IsReady == true
                && !string.IsNullOrWhiteSpace(input.Selection.InterfaceDigest)
                && !StringEquals(input.Selection.InterfaceDigest, snapshot.Source.InterfaceDigest))
            {
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.StaleSource, input, snapshot, activeAddOnPackage);
            }
            if (input.ActiveAddOnPackages.Count == 0)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.MissingAddOn, input, snapshot, activeAddOnPackage);
            if (input.ActiveAddOnPackages.Count != 1)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.MultipleAddOns, input, snapshot, activeAddOnPackage);
            if (!input.EditorReloadSettled)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.Settling, input, snapshot, activeAddOnPackage);

            var addOn = snapshot.AddOn;
            if (addOn == null || !addOn.ManifestValid || input.Selection == null || !input.Selection.IsReady)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.InvalidManifest, input, snapshot, activeAddOnPackage);
            if (!addOn.InventoryValid)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.InvalidInventory, input, snapshot, activeAddOnPackage);
            if (!StringEquals(addOn.Distribution, input.SelectedDistribution)
                || !StringEquals(addOn.BaseRuntimePackage, input.SelectedBaseRuntimePackage))
            {
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.DistributionMismatch, input, snapshot, activeAddOnPackage);
            }
            if (!StringEquals(addOn.InterfaceDigest, snapshot.Source.InterfaceDigest)
                || !StringEquals(input.Selection.InterfaceDigest, snapshot.Source.InterfaceDigest)
                || addOn.InterfaceRevision != snapshot.Source.InterfaceRevision)
            {
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.DigestMismatch, input, snapshot, activeAddOnPackage);
            }
            if (!addOn.SupportedRmws.Contains(input.SelectedRmwImplementation, StringComparer.Ordinal))
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.UnsupportedRmw, input, snapshot, activeAddOnPackage);
            if (input.Contracts.Any(contract =>
                    !addOn.ManagedCanonicalTypes.Contains(contract.CanonicalEnvelopeType, StringComparer.Ordinal)))
            {
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.MissingManagedType, input, snapshot, activeAddOnPackage);
            }
            if (addOn.CatalogCount == 0)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.MissingCatalog, input, snapshot, activeAddOnPackage);
            if (addOn.CatalogCount != 1)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.DuplicateCatalog, input, snapshot, activeAddOnPackage);
            if (!input.EditorReloadSettled || !input.CustomCompileSymbolDefined)
                return Result(Ros2ForUnityCustomTypesupportPreflightCode.Settling, input, snapshot, activeAddOnPackage);

            return Result(Ros2ForUnityCustomTypesupportPreflightCode.Ready, input, snapshot, activeAddOnPackage);
        }

        private static Ros2ForUnityCustomTypesupportPreflightResult Result(
            Ros2ForUnityCustomTypesupportPreflightCode code,
            Ros2ForUnityCustomTypesupportPreflightInput input,
            Ros2ForUnityCustomTypesupportDiscoverySnapshot snapshot,
            string activeAddOnPackage,
            IReadOnlyList<string> candidateAddOnPackages = null)
        {
            var source = snapshot?.Source;
            var candidates = candidateAddOnPackages ?? input.CandidateAddOnPackages;
            return new Ros2ForUnityCustomTypesupportPreflightResult(
                code,
                Ros2ForUnityCustomTypesupportInspectorPresentation.StatusLabel(code),
                Ros2ForUnityCustomTypesupportInspectorPresentation.ActionLabel(code),
                Ros2ForUnityCustomTypesupportSelectionTransaction.StaticInterfacePackageId,
                source?.RosPackageName ?? string.Empty,
                source?.InterfaceRevision ?? 0,
                source?.InterfaceDigest ?? string.Empty,
                activeAddOnPackage,
                input.SelectedDistribution,
                input.SelectedRmwImplementation,
                candidates,
                input.Contracts);
        }

        private static bool StringEquals(string left, string right)
            => string.Equals(left, right, StringComparison.Ordinal);
    }

    internal static class Ros2ForUnityCustomTypesupportInspectorPresentation
    {
        internal static string StatusLabel(Ros2ForUnityCustomTypesupportPreflightCode code)
        {
            switch (code)
            {
                case Ros2ForUnityCustomTypesupportPreflightCode.NotRequired:
                    return "No custom native contract";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingSource:
                    return "Static source package missing";
                case Ros2ForUnityCustomTypesupportPreflightCode.StaleSource:
                    return "Static source package is stale";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingAddOn:
                    return "No resolved typesupport add-on";
                case Ros2ForUnityCustomTypesupportPreflightCode.MultipleAddOns:
                    return "Multiple resolved typesupport add-ons";
                case Ros2ForUnityCustomTypesupportPreflightCode.DistributionMismatch:
                    return "Add-on distribution mismatch";
                case Ros2ForUnityCustomTypesupportPreflightCode.DigestMismatch:
                    return "Interface digest mismatch";
                case Ros2ForUnityCustomTypesupportPreflightCode.InvalidManifest:
                    return "Typesupport metadata is invalid";
                case Ros2ForUnityCustomTypesupportPreflightCode.InvalidInventory:
                    return "Typesupport inventory is invalid";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingManagedType:
                    return "Generated managed type is missing";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingCatalog:
                    return "Generated typesupport catalog is missing";
                case Ros2ForUnityCustomTypesupportPreflightCode.DuplicateCatalog:
                    return "Generated typesupport catalog is ambiguous";
                case Ros2ForUnityCustomTypesupportPreflightCode.UnsupportedRmw:
                    return "Selected RMW is unsupported";
                case Ros2ForUnityCustomTypesupportPreflightCode.Settling:
                    return "Unity is applying typesupport changes";
                case Ros2ForUnityCustomTypesupportPreflightCode.Ready:
                    return "Custom typesupport metadata ready";
                default:
                    return "Custom typesupport status unavailable";
            }
        }

        internal static string ActionLabel(Ros2ForUnityCustomTypesupportPreflightCode code)
        {
            switch (code)
            {
                case Ros2ForUnityCustomTypesupportPreflightCode.NotRequired:
                    return "No custom ROS2 source package action is required.";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingSource:
                case Ros2ForUnityCustomTypesupportPreflightCode.StaleSource:
                    return "Generate or validate the locked static ROS2 interface source package.";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingAddOn:
                case Ros2ForUnityCustomTypesupportPreflightCode.MultipleAddOns:
                case Ros2ForUnityCustomTypesupportPreflightCode.DistributionMismatch:
                    return "Select exactly one matching typesupport add-on for the active R2FU runtime.";
                case Ros2ForUnityCustomTypesupportPreflightCode.DigestMismatch:
                    return "Rebuild a matching add-on from the locked static source package; do not edit the manifest by hand.";
                case Ros2ForUnityCustomTypesupportPreflightCode.InvalidManifest:
                case Ros2ForUnityCustomTypesupportPreflightCode.InvalidInventory:
                    return "Validate the add-on metadata and select a verified add-on through the R2FU selector.";
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingManagedType:
                case Ros2ForUnityCustomTypesupportPreflightCode.MissingCatalog:
                case Ros2ForUnityCustomTypesupportPreflightCode.DuplicateCatalog:
                    return "Rebuild the add-on from the exact locked source package and validate its generated catalog.";
                case Ros2ForUnityCustomTypesupportPreflightCode.UnsupportedRmw:
                    return "Choose an RMW supported by the selected add-on or build a matching add-on.";
                case Ros2ForUnityCustomTypesupportPreflightCode.Settling:
                    return "Wait for Unity package resolution and script reload before entering Play Mode.";
                case Ros2ForUnityCustomTypesupportPreflightCode.Ready:
                    return "Metadata is ready; the Player rechecks the registered catalog before creating custom endpoints.";
                default:
                    return "Inspect the active R2FU runtime and typesupport metadata.";
            }
        }

        internal static string CompactDigest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
                return string.Empty;
            return digest.Length <= 12 ? digest : digest.Substring(0, 12);
        }

        internal static string ContractPolicyLabel(string directionalPolicy)
            => string.IsNullOrWhiteSpace(directionalPolicy)
                ? "Direction unavailable / Default"
                : directionalPolicy.Trim();

        internal static string DirectionalContractPolicyLabel(
            string flow,
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth)
        {
            var qos = QosLabel(profile, reliability, durability, history, depth);
            switch (flow)
            {
                case "Publish":
                    return "Outbound / " + qos;
                case "Subscribe":
                    return "Inbound / " + qos;
                case "PublishAndSubscribe":
                    return "Inbound and outbound / " + qos;
                default:
                    return "Direction unavailable / " + qos;
            }
        }

        private static string QosLabel(
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth)
        {
            var parts = new List<string>();
            if (profile != 0)
                parts.Add(profile == FoxRunQosProfile.SensorData
                    ? "Sensor Data"
                    : profile == FoxRunQosProfile.SystemDefault
                        ? "System Default"
                        : "Default");
            if (reliability != 0)
                parts.Add(reliability == FoxRunQosReliability.BestEffort
                    ? "Best Effort"
                    : reliability == FoxRunQosReliability.SystemDefault
                        ? "System Default Reliability"
                        : "Reliable");
            if (durability != 0)
                parts.Add(durability == FoxRunQosDurability.TransientLocal
                    ? "Transient Local"
                    : durability == FoxRunQosDurability.SystemDefault
                        ? "System Default Durability"
                        : "Volatile");
            if (history != 0)
                parts.Add(history == FoxRunQosHistory.KeepAll
                    ? "Keep All"
                    : history == FoxRunQosHistory.SystemDefault
                        ? "System Default History"
                        : "Keep Last");
            if (depth > 0)
                parts.Add("Depth " + depth);
            return parts.Count == 0 ? "Manager QoS Profile" : string.Join(" / ", parts);
        }
    }
}
