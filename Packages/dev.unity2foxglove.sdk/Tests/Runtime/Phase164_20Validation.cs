using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_20Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-20 Tests ---");
            _passed = 0;

            VerifyBridgeFrameWriterAvoidsExtraPublicCopy();
            VerifyTcpClientUsesStreamWriterPath();
            VerifyR2fuTypedBindingsCachePublishersPerContract();
            VerifyEditorGuardsAvoidUnnecessaryWork();
            VerifyQosProfileIsValueTyped();
            VerifyRegistry();

            Console.WriteLine("Phase 164-20: " + _passed + " checks passed.\n");
        }

        private static void VerifyBridgeFrameWriterAvoidsExtraPublicCopy()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            var write = PhaseValidationSourceHelpers.SourceMethod(source, "public static byte[] Write");

            Check(write.Contains("var buffer = new byte[checked(16 + headerBytes.Length + frame.PayloadLength)];", StringComparison.Ordinal)
                  && write.Contains("new MemoryStream(buffer, 0, buffer.Length, writable: true, publiclyVisible: true)", StringComparison.Ordinal)
                  && write.Contains("return buffer;", StringComparison.Ordinal),
                "164-20A-1: public bridge frame writer uses one exact output buffer");
            Check(!write.Contains("stream.ToArray()", StringComparison.Ordinal)
                  && !write.Contains("new MemoryStream(16 + headerBytes.Length + frame.PayloadLength)", StringComparison.Ordinal),
                "164-20A-2: public bridge frame writer avoids an extra MemoryStream buffer copy");
        }

        private static void VerifyTcpClientUsesStreamWriterPath()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeTcpClient.cs");
            var send = PhaseValidationSourceHelpers.SourceMethod(source, "public void Send");

            Check(send.Contains("var stream = client.GetStream();", StringComparison.Ordinal)
                  && send.Contains("Ros2BridgeFrameWriter.Write(frame, stream);", StringComparison.Ordinal)
                  && send.Contains("stream.Flush();", StringComparison.Ordinal),
                "164-20B-1: TCP bridge sender writes frames directly to the network stream");
            Check(!send.Contains("Ros2BridgeFrameWriter.Write(frame);", StringComparison.Ordinal)
                  && !send.Contains("byte[]", StringComparison.Ordinal),
                "164-20B-2: TCP bridge sender does not allocate a full frame byte array per send");
        }

        private static void VerifyR2fuTypedBindingsCachePublishersPerContract()
        {
            var hub = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherHub.cs");
            var binding = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherBinding.cs");
            var start = PhaseValidationSourceHelpers.SourceMethod(
                binding,
                "internal FoxRunRos2RegistrationResult TryStart");
            var publish = PhaseValidationSourceHelpers.SourceMethod(
                binding,
                "private bool OnBusEnvelope");

            Check(hub.Contains("private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _bindings", StringComparison.Ordinal)
                  && hub.Contains("var identity = source.GetInstanceID() + \"|\" + contract.Id;", StringComparison.Ordinal)
                  && hub.Contains("_existing.Contains(identity)", StringComparison.Ordinal)
                  && hub.Contains("_bindings.Add(new HostedBinding", StringComparison.Ordinal),
                "164-20C-1: the R2FU Provider hub creates one typed publisher binding per source and contract identity");
            Check(start.Contains("_backend.Register<TEnvelope>(_contract, _qos)", StringComparison.Ordinal)
                  && start.Contains("_token = registration.Token;", StringComparison.Ordinal)
                  && publish.Contains("Volatile.Read(ref _token)", StringComparison.Ordinal)
                  && publish.Contains("_backend.TryPublish(token, mapped)", StringComparison.Ordinal)
                  && !publish.Contains("_backend.Register", StringComparison.Ordinal),
                "164-20C-2: the typed R2FU publish path reuses its registered publisher token");
        }

        private static void VerifyEditorGuardsAvoidUnnecessaryWork()
        {
            var defineInstaller = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var reconcile = PhaseValidationSourceHelpers.SourceMethod(defineInstaller, "private static void ReconcileCompileSymbol()");
            var playGuard = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            var onPlayMode = PhaseValidationSourceHelpers.SourceMethod(playGuard, "private static void OnPlayModeStateChanged");

            Check(reconcile.Contains("if (!changed)", StringComparison.Ordinal)
                  && reconcile.Contains("return;", StringComparison.Ordinal)
                  && reconcile.IndexOf("if (!changed)", StringComparison.Ordinal) < reconcile.IndexOf("PlayerSettings.SetScriptingDefineSymbols", StringComparison.Ordinal),
                "164-20D-1: R2FU define installer skips PlayerSettings writes when symbols are unchanged");
            Check(onPlayMode.Contains("if (state == PlayModeStateChange.ExitingEditMode)", StringComparison.Ordinal)
                  && onPlayMode.Contains("OnExitingEditMode();", StringComparison.Ordinal)
                  && onPlayMode.Contains("return;", StringComparison.Ordinal)
                  && !playGuard.Contains("File.ReadAllText", StringComparison.Ordinal)
                  && !playGuard.Contains("Directory.GetFiles", StringComparison.Ordinal),
                "164-20D-2: R2FU play mode guard isolates pre-Play work and avoids broad file I/O");
        }

        private static void VerifyQosProfileIsValueTyped()
        {
            var source = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/FoxRunRos2Qos.cs");

            Check(source.Contains("public readonly struct FoxRunResolvedQos", StringComparison.Ordinal),
                "164-20E-1: Provider-owned R2FU QoS contract stays value typed");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-20\"", StringComparison.Ordinal), "164-20F-1: validation registry exposes Phase164-20");
            Check(project.Contains("Phase164_20Validation.cs", StringComparison.Ordinal), "164-20F-2: runtime validation project compiles Phase164-20");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
