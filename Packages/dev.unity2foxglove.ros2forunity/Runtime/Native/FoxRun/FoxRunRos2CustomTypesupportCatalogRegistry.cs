// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Single Player-safe registration point for custom typesupport add-on catalogs.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Add-on catalog registration seam. It owns metadata only: no node,
    /// executor, ros2cs initialization, reflection scan, or native-plugin load
    /// is allowed on this path.
    /// </summary>
    public static class FoxRunRos2CustomTypesupportCatalogRegistry
    {
        private const string SourcePackageId = "dev.unity2foxglove.foxrun.ros2.interfaces";
        private const string RosPackageName = "unity2foxglove_foxrun_interfaces_v1";
        private static readonly object s_gate = new object();
        private static readonly List<IFoxRunRos2CustomTypesupportCatalog> s_catalogs =
            new List<IFoxRunRos2CustomTypesupportCatalog>();
        private static bool s_invalidRegistration;
        private static bool s_nativeSessionStopped;

        /// <summary>
        /// Register one generated add-on catalog. Invalid or duplicate
        /// registrations are retained as a non-ready state rather than throwing
        /// from a generated startup callback.
        /// </summary>
        public static void Register(IFoxRunRos2CustomTypesupportCatalog catalog)
        {
            lock (s_gate)
            {
                if (s_nativeSessionStopped || !IsValid(catalog))
                {
                    s_invalidRegistration = true;
                    return;
                }

                s_catalogs.Add(catalog);
            }
        }

        internal static FoxRunRos2CustomTypesupportReadiness Evaluate(
            string baseRuntimePackageId,
            string interfaceDigest,
            string rmwImplementation)
        {
            lock (s_gate)
            {
                if (s_nativeSessionStopped)
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.NativeSessionStopped);
                if (s_invalidRegistration)
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.InvalidCatalog);
                if (s_catalogs.Count == 0)
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.MissingCatalog);
                if (s_catalogs.Count != 1)
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.DuplicateCatalog);

                var catalog = s_catalogs[0];
                if (!StringEquals(catalog.BaseRuntimePackageId, baseRuntimePackageId))
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.RuntimeMismatch);
                if (!StringEquals(catalog.InterfaceDigest, interfaceDigest))
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.DigestMismatch);
                if (!SupportsRmw(catalog.SupportedRmwImplementations, rmwImplementation))
                    return FoxRunRos2CustomTypesupportReadiness.From(
                        FoxRunRos2CustomTypesupportReadinessCode.UnsupportedRmw);
                return FoxRunRos2CustomTypesupportReadiness.From(
                    FoxRunRos2CustomTypesupportReadinessCode.Ready);
            }
        }

        internal static void MarkNativeSessionStopped()
        {
            lock (s_gate)
                s_nativeSessionStopped = true;
        }

        internal static void ResetForTests()
        {
            lock (s_gate)
            {
                s_catalogs.Clear();
                s_invalidRegistration = false;
                s_nativeSessionStopped = false;
            }
        }

        private static bool IsValid(IFoxRunRos2CustomTypesupportCatalog catalog)
        {
            try
            {
                if (catalog == null
                    || !StringEquals(catalog.SourcePackageId, SourcePackageId)
                    || !StringEquals(catalog.RosPackageName, RosPackageName)
                    || catalog.InterfaceRevision <= 0
                    || !IsSha256(catalog.InterfaceDigest)
                    || String.IsNullOrWhiteSpace(catalog.BaseRuntimePackageId)
                    || !StringEquals(catalog.Platform, "win64")
                    || catalog.SupportedRmwImplementations == null
                    || catalog.TypeMap == null
                    || catalog.SupportedRmwImplementations.Count == 0
                    || catalog.TypeMap.Count == 0)
                    return false;

                var rmws = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < catalog.SupportedRmwImplementations.Count; index++)
                {
                    var rmw = catalog.SupportedRmwImplementations[index];
                    if (String.IsNullOrWhiteSpace(rmw) || !rmws.Add(rmw))
                        return false;
                }

                var canonicalTypes = new HashSet<string>(StringComparer.Ordinal);
                var managedTypes = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < catalog.TypeMap.Count; index++)
                {
                    var entry = catalog.TypeMap[index];
                    if (String.IsNullOrWhiteSpace(entry.CanonicalRosType)
                        || String.IsNullOrWhiteSpace(entry.ManagedTypeName)
                        || !entry.CanonicalRosType.StartsWith(RosPackageName + "/msg/", StringComparison.Ordinal)
                        || !entry.ManagedTypeName.StartsWith(RosPackageName + ".msg.", StringComparison.Ordinal)
                        || !canonicalTypes.Add(entry.CanonicalRosType)
                        || !managedTypes.Add(entry.ManagedTypeName))
                        return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SupportsRmw(IReadOnlyList<string> supported, string requested)
        {
            if (supported == null || String.IsNullOrWhiteSpace(requested))
                return false;
            for (var index = 0; index < supported.Count; index++)
            {
                if (StringEquals(supported[index], requested))
                    return true;
            }
            return false;
        }

        private static bool StringEquals(string left, string right)
        {
            return String.Equals(left, right, StringComparison.Ordinal);
        }

        private static bool IsSha256(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length != 64)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }
    }
}
#endif
