// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Deterministically renders a source-only UPM/ROS2 interface package from the FoxRun generation model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunRos2InterfaceRenderException : InvalidOperationException
    {
        public FoxRunRos2InterfaceRenderException(string message)
            : base(message)
        {
        }
    }

    public sealed class FoxRunRos2InterfaceRenderedFile
    {
        public FoxRunRos2InterfaceRenderedFile(string relativePath, string text)
        {
            RelativePath = FoxRunRos2InterfaceDigest.NormalizeRelativePath(relativePath);
            Text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            Bytes = FoxRunRos2InterfaceDigest.EncodeText(Text);
        }

        public string RelativePath { get; }
        public string Text { get; }
        public byte[] Bytes { get; }
    }

    public sealed class FoxRunRos2InterfaceRenderedPackage
    {
        internal FoxRunRos2InterfaceRenderedPackage(
            IReadOnlyList<FoxRunRos2InterfaceRenderedFile> files,
            FoxRunRos2InterfaceLock @lock,
            string interfaceDigest,
            bool hasCustomContracts)
        {
            Files = files ?? Array.Empty<FoxRunRos2InterfaceRenderedFile>();
            Lock = @lock;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            HasCustomContracts = hasCustomContracts;
        }

        public IReadOnlyList<FoxRunRos2InterfaceRenderedFile> Files { get; }
        public FoxRunRos2InterfaceLock Lock { get; }
        public string InterfaceDigest { get; }
        public bool HasCustomContracts { get; }

        public string GetText(string relativePath)
        {
            var normalized = FoxRunRos2InterfaceDigest.NormalizeRelativePath(relativePath);
            var file = Files.SingleOrDefault(candidate => string.Equals(
                candidate.RelativePath,
                normalized,
                StringComparison.Ordinal));
            if (file == null)
                throw new KeyNotFoundException("Rendered interface package file is missing: " + normalized);
            return file.Text;
        }
    }

    /// <summary>
    /// Rendering accepts only the Phase181 custom DTO family. Existing packaged
    /// ROS2 messages remain Phase179 runtime artifacts and never appear here.
    /// </summary>
    public static class FoxRunRos2InterfacePackageRenderer
    {
        // UPM accepts prerelease SemVer, while ROS package.xml deliberately uses
        // the portable ROS package version below. Keep the two representations
        // separate rather than making a ROS workspace parse Unity package syntax.
        private const string UnityPackageVersion = "0.1.0-preview.1";
        private const string RosPackageVersion = "0.1.0";

        public static FoxRunRos2InterfaceRenderedPackage Render(
            FoxRunGenerationModel model,
            string rosPackageName = null)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            rosPackageName ??= FoxRunRos2InterfaceIdentity.DefaultRosPackageName;
            if (!FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(rosPackageName, out var revision))
            {
                throw new FoxRunRos2InterfaceRenderException(
                    "The selected ROS package name must use the explicit unity2foxglove_foxrun_interfaces_vN revision grammar.");
            }

            var contracts = SelectContracts(model).ToArray();
            if (contracts.Length == 0)
            {
                return new FoxRunRos2InterfaceRenderedPackage(
                    Array.Empty<FoxRunRos2InterfaceRenderedFile>(),
                    null,
                    string.Empty,
                    hasCustomContracts: false);
            }

            var shapes = CollectShapes(contracts);
            var filesWithoutLock = new List<FoxRunRos2InterfaceRenderedFile>
            {
                new FoxRunRos2InterfaceRenderedFile("package.json", RenderPackageJson()),
                new FoxRunRos2InterfaceRenderedFile("README.md", RenderReadme(rosPackageName)),
                new FoxRunRos2InterfaceRenderedFile(
                    "RuntimeSupport/foxrun-ros2-interface-settings.json",
                    FoxRunRos2InterfaceJsonWriter.WriteSettings(rosPackageName, isLocked: true)),
                new FoxRunRos2InterfaceRenderedFile("Ros2Package~/package.xml", RenderPackageXml(rosPackageName)),
                new FoxRunRos2InterfaceRenderedFile(
                    "Ros2Package~/CMakeLists.txt",
                    RenderCmake(
                        rosPackageName,
                        shapes.Values,
                        contracts.Select(contract => contract.Ros2CustomDtoShape.PayloadIdentity)))
            };

            foreach (var shape in shapes.Values.OrderBy(value => value.PayloadIdentity, StringComparer.Ordinal))
            {
                filesWithoutLock.Add(new FoxRunRos2InterfaceRenderedFile(
                    "Ros2Package~/msg/" + shape.PayloadIdentity + ".msg",
                    RenderPayload(shape)));
            }

            var envelopeFilesByPayloadIdentity = new Dictionary<string, FoxRunRos2InterfaceRenderedFile>(StringComparer.Ordinal);
            var renderedContracts = new List<FoxRunRos2InterfaceContractLock>();
            foreach (var contract in contracts.OrderBy(value => ContractKey(value), StringComparer.Ordinal))
            {
                var shape = contract.Ros2CustomDtoShape;
                var payloadPath = "Ros2Package~/msg/" + shape.PayloadIdentity + ".msg";
                var payloadFile = filesWithoutLock.Single(file => string.Equals(file.RelativePath, payloadPath, StringComparison.Ordinal));
                var envelopeName = FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName(shape.PayloadIdentity);
                if (!envelopeFilesByPayloadIdentity.TryGetValue(shape.PayloadIdentity, out var envelopeFile))
                {
                    var envelopePath = "Ros2Package~/msg/" + envelopeName + ".msg";
                    envelopeFile = new FoxRunRos2InterfaceRenderedFile(envelopePath, RenderEnvelope(shape.PayloadIdentity));
                    envelopeFilesByPayloadIdentity.Add(shape.PayloadIdentity, envelopeFile);
                    filesWithoutLock.Add(envelopeFile);
                }
                renderedContracts.Add(new FoxRunRos2InterfaceContractLock(
                    contract.DeclaringType,
                    contract.MemberName,
                    contract.Topic,
                    shape.CanonicalIdentity,
                    shape.PayloadIdentity,
                    envelopeName,
                    DigestMessage(payloadFile),
                    DigestMessage(envelopeFile)));
            }

            var orderedWithoutLock = filesWithoutLock
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var interfaceDigest = FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                orderedWithoutLock.Select(file => new FoxRunRos2InterfaceDigestInput(file.RelativePath, file.Bytes)));
            var @lock = new FoxRunRos2InterfaceLock(
                FoxRunRos2InterfaceIdentity.LockSchemaVersion,
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                FoxRunRos2InterfaceIdentity.UnityPackageId,
                rosPackageName,
                revision,
                model.GeneratorVersion,
                FoxRunRos2InterfaceIdentity.NamingPolicyVersion,
                interfaceDigest,
                renderedContracts);
            var allFiles = orderedWithoutLock
                .Concat(new[]
                {
                    new FoxRunRos2InterfaceRenderedFile(
                        "RuntimeSupport/foxrun-ros2-interface-lock.json",
                        FoxRunRos2InterfaceJsonWriter.WriteLock(@lock))
                })
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();

            return new FoxRunRos2InterfaceRenderedPackage(allFiles, @lock, interfaceDigest, hasCustomContracts: true);
        }

        private static IEnumerable<FoxRunR2fuTopicMember> SelectContracts(FoxRunGenerationModel model)
        {
            foreach (var member in model.Types.SelectMany(type => type.Members))
            {
                var projected = FoxRunR2fuTopicMember.Create(member);
                if (projected.Ros2ContractKind
                    != FoxRunRos2ContractKind.CustomDto
                    || !projected.GeneratesRos2NativeRegistration)
                {
                    continue;
                }

                var shape = projected.Ros2CustomDtoShape;
                if (shape == null || !shape.IsSupported || !shape.HasPublicParameterlessConstructor || shape.Diagnostics.Count != 0)
                {
                    throw new FoxRunRos2InterfaceRenderException(
                        "Custom ROS2 contract " + projected.DeclaringType + "." + projected.MemberName
                        + " has an invalid DTO graph and cannot produce a static interface package.");
                }
                if (string.IsNullOrWhiteSpace(shape.CanonicalIdentity)
                    || string.IsNullOrWhiteSpace(shape.PayloadIdentity))
                {
                    throw new FoxRunRos2InterfaceRenderException(
                        "Custom ROS2 contract " + projected.DeclaringType + "." + projected.MemberName
                        + " has no stable DTO/message identity.");
                }

                yield return projected;
            }
        }

        private static IReadOnlyDictionary<string, FoxRunRos2CustomDtoShape> CollectShapes(
            IReadOnlyList<FoxRunR2fuTopicMember> contracts)
        {
            var byCanonicalIdentity = new Dictionary<string, FoxRunRos2CustomDtoShape>(StringComparer.Ordinal);
            var payloadIdentityOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var contract in contracts)
                AddShape(contract.Ros2CustomDtoShape, byCanonicalIdentity, payloadIdentityOwners);
            return byCanonicalIdentity;
        }

        private static void AddShape(
            FoxRunRos2CustomDtoShape shape,
            IDictionary<string, FoxRunRos2CustomDtoShape> byCanonicalIdentity,
            IDictionary<string, string> payloadIdentityOwners)
        {
            if (shape == null)
                throw new FoxRunRos2InterfaceRenderException("A nested DTO shape was missing from a custom ROS2 contract.");
            if (byCanonicalIdentity.ContainsKey(shape.CanonicalIdentity))
                return;
            if (payloadIdentityOwners.TryGetValue(shape.PayloadIdentity, out var owner)
                && !string.Equals(owner, shape.CanonicalIdentity, StringComparison.Ordinal))
            {
                throw new FoxRunRos2InterfaceRenderException(
                    "Distinct DTO schemas resolved to the same ROS message identity: " + shape.PayloadIdentity);
            }

            payloadIdentityOwners[shape.PayloadIdentity] = shape.CanonicalIdentity;
            byCanonicalIdentity.Add(shape.CanonicalIdentity, shape);
            foreach (var member in shape.Members)
            {
                if (member.NestedShape != null)
                    AddShape(member.NestedShape, byCanonicalIdentity, payloadIdentityOwners);
            }
        }

        private static string RenderPackageJson()
            => "{\n"
               + "  \"name\": \"" + FoxRunRos2InterfaceIdentity.UnityPackageId + "\",\n"
               + "  \"version\": \"" + UnityPackageVersion + "\",\n"
               + "  \"displayName\": \"Unity2Foxglove FoxRun ROS2 Interfaces\",\n"
               + "  \"license\": \"Apache-2.0\",\n"
               + "  \"description\": \"Deterministic source-only ROS2 interface package generated from FoxRun custom DTO contracts.\",\n"
               + "  \"unity\": \"6000.0\"\n"
               + "}\n";

        private static string RenderReadme(string rosPackageName)
            => "# Unity2Foxglove FoxRun ROS2 Interfaces\n\n"
               + "This is a source-only static interface package. It contains generated `.msg` files, no ros2cs assembly, native DLL, typesupport, CMake build output, or runtime endpoint.\n\n"
               + "## Linux ROS2 workspace\n\n"
               + "Copy or symlink `Ros2Package~` into a normal ROS2 workspace `src/` directory, then build the explicit locked revision:\n\n"
               + "```bash\n"
               + "colcon build --packages-select " + rosPackageName + "\n"
               + "```\n\n"
               + "Before copying, verify the checked-in source bytes from the Unity2Foxglove repository root:\n\n"
               + "```bash\n"
               + "python Scripts/ros2forunity/interfaces/interface_digest.py --package-root Packages/dev.unity2foxglove.foxrun.ros2.interfaces\n"
               + "```\n\n"
               + "The command must print the lock digest before building. A wire-changing DTO edit requires an explicit `_vN` package revision; generation never silently chooses one.\n\n"
               + "Every envelope contains `foxrun_origin_id`, `foxrun_sequence`, `foxrun_stamp`, and the generated payload. The package is intentionally not a runtime binary distribution.\n";

        private static string RenderPackageXml(string rosPackageName)
            => "<?xml version=\"1.0\"?>\n"
               + "<package format=\"3\">\n"
               + "  <name>" + rosPackageName + "</name>\n"
               + "  <version>" + RosPackageVersion + "</version>\n"
               + "  <description>Static FoxRun custom DTO ROS2 interfaces.</description>\n"
               + "  <maintainer email=\"opensource@unity2foxglove.dev\">Unity2Foxglove</maintainer>\n"
               + "  <license>Apache-2.0</license>\n"
               + "  <buildtool_depend>ament_cmake</buildtool_depend>\n"
               + "  <buildtool_depend>rosidl_default_generators</buildtool_depend>\n"
               + "  <exec_depend>rosidl_default_runtime</exec_depend>\n"
               + "  <depend>builtin_interfaces</depend>\n"
               + "  <member_of_group>rosidl_interface_packages</member_of_group>\n"
               + "</package>\n";

        private static string RenderCmake(
            string rosPackageName,
            IEnumerable<FoxRunRos2CustomDtoShape> shapes,
            IEnumerable<string> envelopePayloadNames)
        {
            var payloadMessages = (shapes ?? Array.Empty<FoxRunRos2CustomDtoShape>())
                .Select(shape => shape.PayloadIdentity)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var envelopeMessages = (envelopePayloadNames ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName)
                .ToArray();
            var builder = new StringBuilder();
            builder.AppendLine("cmake_minimum_required(VERSION 3.8)");
            builder.AppendLine("project(" + rosPackageName + ")");
            builder.AppendLine();
            builder.AppendLine("find_package(ament_cmake REQUIRED)");
            builder.AppendLine("find_package(rosidl_default_generators REQUIRED)");
            builder.AppendLine("find_package(builtin_interfaces REQUIRED)");
            builder.AppendLine();
            builder.AppendLine("rosidl_generate_interfaces(${PROJECT_NAME}");
            foreach (var message in payloadMessages)
                builder.AppendLine("  \"msg/" + message + ".msg\"");
            foreach (var message in envelopeMessages)
                builder.AppendLine("  \"msg/" + message + ".msg\"");
            builder.AppendLine("  DEPENDENCIES builtin_interfaces");
            builder.AppendLine(")");
            builder.AppendLine();
            builder.AppendLine("ament_export_dependencies(rosidl_default_runtime)");
            builder.AppendLine("ament_package()");
            return builder.ToString();
        }

        private static string RenderPayload(FoxRunRos2CustomDtoShape shape)
        {
            var builder = new StringBuilder();
            foreach (var member in shape.Members
                .OrderBy(value => value.RosFieldName, StringComparer.Ordinal)
                .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                var type = ResolveRosType(member);
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(member.RosFieldName))
                    throw new FoxRunRos2InterfaceRenderException("Custom DTO contains an incomplete ROS field mapping.");
                builder.Append(type).Append(' ').Append(member.RosFieldName).Append('\n');
                if (member.HasPresence)
                    builder.Append("bool ").Append(member.PresenceFieldName).Append('\n');
            }
            return builder.ToString();
        }

        private static string RenderEnvelope(string payloadMessageName)
            => "string foxrun_origin_id\n"
               + "uint64 foxrun_sequence\n"
               + "builtin_interfaces/Time foxrun_stamp\n"
               + payloadMessageName + " payload\n";

        private static string ResolveRosType(FoxRunRos2CustomDtoMemberShape member)
        {
            if (member.Kind != FoxRunRos2CustomDtoMemberKind.NestedDto)
                return member.RosType;
            if (member.NestedShape == null || string.IsNullOrWhiteSpace(member.NestedShape.PayloadIdentity))
                throw new FoxRunRos2InterfaceRenderException("Nested custom DTO has no generated payload identity.");
            return member.NestedShape.PayloadIdentity;
        }

        private static string DigestMessage(FoxRunRos2InterfaceRenderedFile file)
            => FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[] { new FoxRunRos2InterfaceDigestInput(file.RelativePath, file.Bytes) });

        private static string ContractKey(FoxRunR2fuTopicMember member)
            => member.DeclaringType + "\u001f" + member.MemberName + "\u001f" + member.Topic;
    }
}
