// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Candidate and base-runtime discovery..

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityCustomTypesupportSelectionTransaction
    {
        private static bool TryReadBaseRuntime(
            string packagesDirectory,
            string packageId,
            out BaseRuntime baseRuntime)
        {
            baseRuntime = null;
            if (!packageId.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                return false;
            var directory = Path.Combine(packagesDirectory, packageId);
            if (!IsChildPath(directory, packagesDirectory))
                return false;
            if (!TryReadObject(Path.Combine(directory, "RuntimeSupport", "runtime-manifest.json"), out var manifest))
                return false;
            var packageVersion = Text(manifest["packageVersion"]);
            var distro = Text(manifest["rosDistro"]);
            if (!StringEquals(Text(manifest["packageName"]), packageId)
                || string.IsNullOrWhiteSpace(packageVersion)
                || string.IsNullOrWhiteSpace(distro)
                || !StringEquals(Text(manifest["platform"]), "win64")
                || !StringEquals(Text(manifest["architecture"]), "x86_64"))
            {
                return false;
            }

            baseRuntime = new BaseRuntime(
                packageId,
                packageVersion,
                distro,
                NormalizedJsonSha256(manifest),
                directory);
            return true;
        }

        private static bool TryReadCandidate(
            string directory,
            BaseRuntime baseRuntime,
            out Candidate candidate,
            out Ros2ForUnityCustomTypesupportCandidateValidationCode validationCode)
        {
            candidate = null;
            validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Unexpected;
            try
            {
                var packageId = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(packageId)
                    || !packageId.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal)
                    || !TryReadObject(Path.Combine(directory, "package.json"), out var package)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-manifest.json"), out var manifest)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-inventory.json"), out var inventory))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.PackageMetadata;
                    return false;
                }

                var dependencies = package["dependencies"] as JObject;
                if (!StringEquals(Text(package["name"]), packageId)
                    || package["unity2foxgloveFoxRunCustomTypesupportAddOn"]?.Value<bool>() != true
                    || dependencies == null
                    || Text(dependencies[OptionalFacadePackageId]).Length == 0
                    || !StringEquals(Text(dependencies[baseRuntime.PackageId]), baseRuntime.PackageVersion))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.PackageMetadata;
                    return false;
                }

                var source = manifest["source"] as JObject;
                var baseRuntimeToken = manifest["baseRuntime"] as JObject;
                if (source == null || baseRuntimeToken == null
                    || !TryReadStaticSourceLock(directory, out var staticSource))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.StaticSource;
                    return false;
                }

                if (!StringEquals(Text(source["upmPackageId"]), StaticInterfacePackageId)
                    || !StringEquals(Text(source["rosPackageName"]), staticSource.RosPackageName)
                    || source["interfaceRevision"]?.Value<int?>() != staticSource.InterfaceRevision
                    || !StringEquals(Text(source["interfaceDigest"]), staticSource.InterfaceDigest)
                    || !StringEquals(Text(manifest["distro"]), baseRuntime.Distro)
                    || !StringEquals(Text(manifest["platform"]), "win64")
                    || !StringEquals(Text(manifest["architecture"]), "x86_64")
                    || !StringEquals(Text(baseRuntimeToken["packageId"]), baseRuntime.PackageId)
                    || !StringEquals(Text(baseRuntimeToken["runtimeManifestSha256"]), baseRuntime.ManifestDigest)
                    || baseRuntimeToken["runtimeManifestVersion"]?.Value<int?>() != 1)
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.SourceIdentity;
                    return false;
                }

                if (!HasMatchingRos2csIdentity(directory, manifest, baseRuntime))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.ManagedIdentity;
                    return false;
                }

                if (!HasVerifiedNativeClosure(directory, manifest, inventory, baseRuntime))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.NativeClosure;
                    return false;
                }

                candidate = new Candidate(
                    packageId,
                    Text(source["interfaceDigest"]),
                    Path.Combine(directory, NativePluginRelativeDirectory));
                validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Ready;
                return true;
            }
            catch (Exception)
            {
                // Package contents are untrusted input to this selector.
                candidate = null;
                validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Unexpected;
                return false;
            }
        }

    }
}
