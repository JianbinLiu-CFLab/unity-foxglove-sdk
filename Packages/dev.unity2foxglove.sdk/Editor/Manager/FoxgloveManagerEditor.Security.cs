// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
using Unity.FoxgloveSDK.Core;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private void DrawSecureWebSocketSection(bool isSecure)
        {
            DrawCertificateGeneratorBackendControls();

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Local Dev Certificate"))
                GenerateLocalDevCertificate();

            var host = GetString("_host", "127.0.0.1");
            var port = GetInt("_port", 8765);
            var token = GetString("_sharedToken", "");
            RefreshWebUrlCache(host, port, isSecure: true, token);
            var secureUrl = _cachedSecureUrl;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Secure URL", secureUrl);
            }

            if (!isSecure)
                EditorGUILayout.HelpBox("Select SecureWebSocket transport mode to enable WSS settings.", MessageType.Info);

            var distributorHost = GetString("_rootCaDistributorHost", "127.0.0.1");
            if (distributorHost != "127.0.0.1" && distributorHost != "localhost")
            {
                EditorGUILayout.HelpBox(
                    "Root CA distributor is not bound to loopback. Only use this on trusted networks.",
                    MessageType.Warning);
            }

            var rootPath = GetString("_rootCaFilePath", "");
            var fingerprint = GetCachedRootCaFingerprint(ResolveProjectPath(rootPath));
            if (!string.IsNullOrEmpty(fingerprint))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Root CA SHA-256", fingerprint);
                EditorGUILayout.HelpBox(
                    "Import the generated root CA manually only after comparing this SHA-256 fingerprint through a trusted channel.",
                    MessageType.Info);
            }

            if (GetBool("_rootCaDistributorEnabled"))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Root CA URL",
                        $"http://{distributorHost}:{GetInt("_rootCaDistributorPort", 8766)}/rootCA.crt");
                }
            }

            DrawCertificateUtilityButtons(fingerprint, secureUrl);
        }

        private void DrawSecureWebSocketFields(bool isSecure)
        {
            using (new EditorGUI.DisabledScope(!isSecure))
            {
                var pfx = FindCachedProperty("_certificatePfxPath");
                if (pfx != null)
                    DrawPathBrowse(pfx, "Select WSS PFX Certificate", "pfx", true, GetSmartDefault(pfx.stringValue, true));
                else
                    DrawMissingProperty("_certificatePfxPath");

                DrawPasswordProperty("_certificatePassword", "Certificate Password");
                DrawPasswordProperty("_sharedToken", "Shared Token");
                EditorGUILayout.HelpBox(
                    "Certificate passwords and shared tokens entered here are serialized with the scene or prefab. Prefer FOXGLOVE_CERTIFICATE_PASSWORD and FOXGLOVE_SHARED_TOKEN for credentials that must not be committed.",
                    MessageType.Warning);
                DrawProperty("_rootCaDistributorEnabled");
                DrawProperty("_rootCaDistributorHost");
                DrawProperty("_rootCaDistributorPort");

                var rootCa = FindCachedProperty("_rootCaFilePath");
                if (rootCa != null)
                    DrawPathBrowse(rootCa, "Select Root CA File", "crt", true, GetSmartDefault(rootCa.stringValue, true));
                else
                    DrawMissingProperty("_rootCaFilePath");
            }
        }

        private static void DrawCertificateGeneratorBackendControls()
        {
            var backend = GetCertificateBackendPreference();
            var selected = (FoxgloveLocalDevCertificateBackend)EditorGUILayout.EnumPopup(
                "Certificate Generator",
                backend);
            if (selected != backend)
            {
                EditorPrefs.SetString(CertificateBackendEditorPrefKey, selected.ToString());
                backend = selected;
            }

            if (backend != FoxgloveLocalDevCertificateBackend.OpenSsl)
                return;

            var configuredPath = EditorPrefs.GetString(OpenSslPathEditorPrefKey, string.Empty);
            var resolved = OpenSslResolver.Resolve(configuredPath);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("OpenSSL", string.IsNullOrEmpty(resolved) ? "Not found" : resolved);
            }

            if (string.IsNullOrEmpty(resolved))
            {
                EditorGUILayout.HelpBox(
                    "OpenSSL is optional. Install it, add it to PATH, set UNITY2FOXGLOVE_OPENSSL, or choose an executable before using the OpenSSL backend.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Choose OpenSSL"))
                {
                    var selectedPath = EditorUtility.OpenFilePanel(
                        "Choose OpenSSL executable",
                        GetOpenSslPickerDirectory(configuredPath),
                        Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(selectedPath))
                        EditorPrefs.SetString(OpenSslPathEditorPrefKey, selectedPath);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(configuredPath)))
                {
                    if (GUILayout.Button("Clear OpenSSL"))
                        EditorPrefs.DeleteKey(OpenSslPathEditorPrefKey);
                }
            }
        }

        private static FoxgloveLocalDevCertificateBackend GetCertificateBackendPreference()
        {
            var value = EditorPrefs.GetString(
                CertificateBackendEditorPrefKey,
                FoxgloveLocalDevCertificateBackend.BuiltIn.ToString());
            return value == FoxgloveLocalDevCertificateBackend.OpenSsl.ToString()
                ? FoxgloveLocalDevCertificateBackend.OpenSsl
                : FoxgloveLocalDevCertificateBackend.BuiltIn;
        }

        private static FoxgloveLocalDevCertificateOptions BuildCertificateGeneratorOptions()
        {
            var backend = GetCertificateBackendPreference();
            if (backend == FoxgloveLocalDevCertificateBackend.OpenSsl)
                return FoxgloveLocalDevCertificateOptions.OpenSsl(
                    EditorPrefs.GetString(OpenSslPathEditorPrefKey, string.Empty));

            return FoxgloveLocalDevCertificateOptions.BuiltIn;
        }

        private static string GetOpenSslPickerDirectory(string configuredPath)
        {
            if (!string.IsNullOrEmpty(configuredPath))
            {
                if (Directory.Exists(configuredPath))
                    return configuredPath;

                var directory = Path.GetDirectoryName(configuredPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    return directory;
            }

            return GetDefaultDir();
        }

        private static string BuildCertificateFailureMessage(FoxgloveLocalDevCertificateException ex)
        {
            switch (ex.Kind)
            {
                case FoxgloveLocalDevCertificateFailureKind.BuiltInUnavailable:
                    return "Built-in certificate generation failed in this Unity Editor. The default SDK path does not require OpenSSL; OpenSSL is only a manual fallback. "
                        + "Details: " + ex.Message
                        + "\n\nFallback: select the OpenSSL certificate generator, then install OpenSSL or choose an OpenSSL executable.";
                case FoxgloveLocalDevCertificateFailureKind.OpenSslNotFound:
                    return "OpenSSL was not found. Install OpenSSL, install Git for Windows, add openssl.exe to PATH, set UNITY2FOXGLOVE_OPENSSL to an OpenSSL executable or bin directory, or click Choose OpenSSL in the Inspector.";
                case FoxgloveLocalDevCertificateFailureKind.OpenSslFailed:
                    return ex.Message;
                default:
                    return string.IsNullOrEmpty(ex.Message)
                        ? "Local development certificate generation failed."
                        : ex.Message;
            }
        }
    }
}
