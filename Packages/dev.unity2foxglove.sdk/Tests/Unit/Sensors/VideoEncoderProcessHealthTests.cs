// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Foxglove.Schemas.Video;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class VideoEncoderProcessHealthTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task SubmitPausedBeforeAdmissionRejectsAfterActualStop(int codec)
        {
            using var fixture = new WorkerFixture(codec, "silent");
            await fixture.ExpectLine("READY");
            bool accepted = true;
            Exception failure = null;
            var submitter = new Thread(() =>
            {
                try { accepted = fixture.Submit(100); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            try
            {
                lock (fixture.InputLock)
                {
                    submitter.Start();
                    // The only managed wait in this submit path is input admission.
                    Assert.True(SpinWait.SpinUntil(
                        () => (submitter.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                        TimeSpan.FromSeconds(10)), "Submit did not reach the admission lock.");
                    fixture.Stop(); // Monitor is reentrant: Stop drains before the submitter resumes.
                    Assert.False(fixture.Sidecar.IsRunning);
                    Assert.Equal(0, fixture.InputCount);
                }
                Assert.True(submitter.Join(TimeSpan.FromSeconds(10)));
                Assert.Null(failure);
                Assert.False(accepted);
                Assert.Equal(0, fixture.FramesSubmitted);
                Assert.Equal(0, fixture.InputCount);
            }
            finally
            {
                if (submitter.IsAlive) submitter.Join(TimeSpan.FromSeconds(10));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task WriterFailureRetiresLiveChildAndDrainsOwnedQueues(int codec)
        {
            using var fixture = new WorkerFixture(codec, "close-stdin");
            await fixture.ExpectLine("READY");
            var writer = fixture.StartWriterTask();
            Assert.True(fixture.Submit(100));
            await fixture.ExpectLine("FRAME_READ_1");
            await fixture.ExpectLine("STDIN_CLOSED");
            Assert.False(fixture.Observer.HasExited);
            Assert.True(fixture.Submit(200));
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(string.IsNullOrWhiteSpace(fixture.Sidecar.LastError));
            await fixture.Observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.WaitUntilDrained();
            Assert.Equal(2, fixture.FramesSubmitted);
            Assert.False(fixture.Submit(300));
            Assert.Equal(2, fixture.FramesSubmitted);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task SilentOutputRejectsAdmissionAtConfiguredTimestampBudget(int codec)
        {
            using var fixture = new WorkerFixture(codec, "silent");
            await fixture.ExpectLine("READY");
            fixture.StartWriter();
            const int budget = 2 + 4;
            for (var i = 1; i <= budget; i++)
            {
                Assert.True(fixture.Submit((ulong)i));
                await fixture.ExpectLine("FRAME_READ_" + i);
            }
            Assert.Equal(budget, fixture.PendingTimestamps);
            Assert.Equal(0, fixture.InputCount);
            Assert.False(fixture.Submit(7));
            Assert.False(fixture.Submit(8));
            Assert.Equal(budget, fixture.FramesSubmitted);
            Assert.Equal(budget, fixture.PendingTimestamps);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task StdoutEndRetiresChildAfterOutputClosure(int codec)
        {
            using var fixture = new WorkerFixture(codec, "close-stdout");
            await fixture.ExpectLine("READY");
            fixture.Process.StandardInput.WriteLine("CLOSE");
            fixture.Process.StandardInput.Flush();
            var reader = fixture.StartReader();
            await reader.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.WaitUntilDrained();
            Assert.False(fixture.Submit(100));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void LateReaderCannotPublishIntoANewSession(int codec)
        {
            var sidecar = codec == 0 ? (object)new FfmpegH264EncoderSidecar()
                : codec == 1 ? new FfmpegH265EncoderSidecar()
                : new OpenH264EncoderSidecar();
            var process = Process.GetCurrentProcess();
            var type = sidecar.GetType();
            var gate = type.GetMethod("IsCurrentSessionForTests", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(gate);
            Set(sidecar, "_process", process);
            Set(sidecar, "_sessionId", 11L);

            Assert.True((bool)gate.Invoke(sidecar, new object[] { process, 11L }));
            Assert.False((bool)gate.Invoke(sidecar, new object[] { process, 10L }));
            Set(sidecar, "_sessionId", 12L);
            Assert.False((bool)gate.Invoke(sidecar, new object[] { process, 11L }));
            Set(sidecar, "_process", null);
            Assert.False((bool)gate.Invoke(sidecar, new object[] { process, 12L }));
        }

        private sealed class WorkerFixture : IDisposable
        {
            public readonly ICameraVideoEncoderSidecar Sidecar;
            public readonly Process Process;
            public readonly Process Observer;
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly int bytes;
            private readonly string directory;
            public long FramesSubmitted => (long)Sidecar.GetType().GetProperty("FramesSubmitted").GetValue(Sidecar);
            public int InputCount => (int)Get("_inputCount");
            public object InputLock => Get("_inputLock");
            public void Stop() => Sidecar.GetType().GetMethod("Stop", Type.EmptyTypes).Invoke(Sidecar, null);
            public int PendingTimestamps => (int)Sidecar.GetType().GetProperty(
                "PendingTimestampCountForTests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Sidecar);

            public WorkerFixture(int codec, string mode)
            {
                bytes = codec == 2 ? 6 : 12;
                Sidecar = codec == 0 ? (ICameraVideoEncoderSidecar)new FfmpegH264EncoderSidecar()
                    : codec == 1 ? new FfmpegH265EncoderSidecar() : new OpenH264EncoderSidecar();
                object options = codec == 0 ? (object)new FfmpegH264EncoderOptions { Width = 2, Height = 2 }
                    : codec == 1 ? new FfmpegH265EncoderOptions { Width = 2, Height = 2 }
                    : new OpenH264EncoderOptions { Width = 2, Height = 2 };
                Set("_options", options);
                Set("_maxInputQueue", 2);
                Set("_maxOutputQueue", 4);
                if (codec < 2)
                    Set("_packetizer", codec == 0 ? (object)new H264AnnexBAccessUnitPacketizer() : new H265AnnexBAccessUnitPacketizer());
                directory = Path.Combine(Path.GetTempPath(), "video-worker-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var dll = Path.Combine(directory, "Child.dll");
                File.WriteAllBytes(dll, HelperAssembly.Value);
                File.WriteAllText(Path.ChangeExtension(dll, ".runtimeconfig.json"),
                    "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                {
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                start.ArgumentList.Add(dll);
                start.ArgumentList.Add(mode);
                start.ArgumentList.Add(bytes.ToString());
                Process = System.Diagnostics.Process.Start(start);
                Observer = System.Diagnostics.Process.GetProcessById(Process.Id);
                Assert.Equal(Process.StartTime, Observer.StartTime);
                Set("_process", Process);
                Set("_stop", stop);
            }

            public void StartWriter()
            {
                _ = StartWriterTask();
            }

            public Task StartWriterTask()
            {
                var method = Sidecar.GetType().GetMethod("RunStdinWriter", BindingFlags.Instance | BindingFlags.NonPublic);
                var args = bytes == 6 ? new object[] { Process, stop.Token } : new object[] { Process, stop.Token, bytes };
                var task = (Task)method.Invoke(Sidecar, args);
                Set("_stdinTask", task);
                return task;
            }

            public Task StartReader()
            {
                var task = (Task)Sidecar.GetType().GetMethod("RunStdoutReader", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Sidecar, new object[] { Process, stop.Token });
                Set("_stdoutTask", task);
                return task;
            }

            public bool Submit(ulong timestamp)
                => ((ITimestampedCameraVideoEncoderSidecar)Sidecar).TrySubmitFrame(new byte[bytes], timestamp);
            public async Task ExpectLine(string line)
                => Assert.Equal(line, await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            public async Task WaitUntilDrained()
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (Sidecar.IsRunning || InputCount != 0 || PendingTimestamps != 0)
                    await Task.Delay(10, deadline.Token);
            }
            private object Get(string name) => Sidecar.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Sidecar);
            private void Set(string name, object value) => Sidecar.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Sidecar, value);
            public void Dispose()
            {
                try { if (!Observer.HasExited) Observer.Kill(); } catch (InvalidOperationException) { }
                Observer.WaitForExit(10000);
                Sidecar.Dispose();
                Observer.Dispose();
                Process.Dispose();
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static readonly Lazy<byte[]> HelperAssembly = new Lazy<byte[]>(() =>
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("Child", new[] { CSharpSyntaxTree.ParseText(HelperSource) },
                references, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            using var output = new MemoryStream();
            var emitted = compilation.Emit(output);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            return output.ToArray();
        });

        private const string HelperSource = @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
class Child {
    [DllImport(""kernel32.dll"")] static extern IntPtr GetStdHandle(int n);
    [DllImport(""kernel32.dll"")] static extern bool CloseHandle(IntPtr h);
    [DllImport(""libc"")] static extern int close(int fd);
    static void Close(int fd) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) CloseHandle(GetStdHandle(fd == 0 ? -10 : -11));
        else close(fd);
    }
    static void CloseInputDescriptors() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { Close(0); return; }
        var target = File.ResolveLinkTarget(""/proc/self/fd/0"", false)?.ToString();
        foreach (var entry in Directory.GetFiles(""/proc/self/fd"")) {
            try {
                if (File.ResolveLinkTarget(entry, false)?.ToString() == target
                    && int.TryParse(Path.GetFileName(entry), out var fd)) close(fd);
            } catch { }
        }
    }
    static void Main(string[] args) {
        Console.WriteLine(""READY""); Console.Out.Flush();
        if (args[0] == ""close-stdout"") {
            Console.ReadLine(); Close(1); return;
        }
        var stream = Console.OpenStandardInput(); var frame = new byte[int.Parse(args[1])];
        for (int i = 1;; i++) {
            int n = 0; while(n < frame.Length) { int r = stream.Read(frame, n, frame.Length-n); if(r == 0) return; n += r; }
            Console.WriteLine(""FRAME_READ_""+i); Console.Out.Flush();
            if(args[0] == ""close-stdin"") {
                stream.Dispose(); CloseInputDescriptors(); Console.WriteLine(""STDIN_CLOSED""); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return;
            }
        }
    }
}";
    }
}
