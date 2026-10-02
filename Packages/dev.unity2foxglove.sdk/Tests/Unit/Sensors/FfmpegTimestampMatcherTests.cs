using Foxglove.Schemas.Video;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class FfmpegTimestampMatcherTests
    {
        [Fact]
        public void MatchesMpegTsPtsToCaptureTimestampsWithoutFifoAssumption()
        {
            var matcher = new FfmpegTimestampMatcher();
            matcher.Reset(30);
            matcher.Track(100);
            matcher.Track(200);

            Assert.True(matcher.TryResolve(0, out var first));
            Assert.Equal(100UL, first);
            Assert.True(matcher.TryResolve(3000, out var second));
            Assert.Equal(200UL, second);
            Assert.Equal(0, matcher.PendingCount);
        }

        [Fact]
        public void PreservesCaptureTimestampWhenAnEncodedFrameIsDropped()
        {
            var matcher = new FfmpegTimestampMatcher();
            matcher.Reset(30);
            matcher.Track(100);
            matcher.Track(200);
            matcher.Track(300);

            Assert.True(matcher.TryResolve(0, out var first));
            Assert.Equal(100UL, first);
            Assert.True(matcher.TryResolve(6000, out var third));
            Assert.Equal(300UL, third);
            Assert.Equal(0, matcher.PendingCount);
        }


        [Fact]
        public void MissingFirstOutputKeepsTheSecondOutputAtFrameIndexOne()
        {
            var matcher = new FfmpegTimestampMatcher();
            matcher.Reset(30);
            matcher.Track(100);
            matcher.Track(200);

            Assert.True(matcher.TryResolve(3000, out var second));
            Assert.Equal(200UL, second);
            Assert.Equal(0, matcher.PendingCount);
        }

        [Fact]
        public async Task RealFfmpegStartsMpegTsPtsAtZeroWhenAvailable()
        {
            // xUnit v2 cannot skip dynamically (SkipException only works in v3), so
            // hosts without FFmpeg return early instead of failing.
            var check = FfmpegExecutableCheck.Check("", 1000);
            if (check.Status != FfmpegExecutableStatus.Found)
                return;

            var options = new FfmpegH264EncoderOptions
            {
                Width = 16,
                Height = 16,
                FrameRate = 30,
                BitrateKbps = 64,
                KeyframeInterval = 30,
                FfmpegPath = check.ExecutablePath
            };
            using (var process = new Process { StartInfo = options.CreateStartInfo(check.VersionLine) })
            {
                Assert.True(process.Start());
                var stdoutTask = Task.Run(() =>
                {
                    using (var output = new MemoryStream())
                    {
                        process.StandardOutput.BaseStream.CopyTo(output);
                        return output.ToArray();
                    }
                });
                var stderrTask = process.StandardError.ReadToEndAsync();
                try
                {
                    var frame = new byte[16 * 16 * 3];
                    for (var index = 0; index < 3; index++)
                        process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
                    process.StandardInput.Close();
                    if (!await Task.Run(() => process.WaitForExit(5000)))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                        catch
                        {
                        }

                        throw new InvalidOperationException("FFmpeg did not exit within the timestamp integration timeout.");
                    }

                    var stream = await stdoutTask;
                    await stderrTask;
                    Assert.Equal(0, process.ExitCode);

                    var demuxer = new MpegTsVideoDemuxer();
                    demuxer.Append(stream, 0, stream.Length);
                    demuxer.Flush();
                    Assert.True(demuxer.TryDequeue(out var first));
                    Assert.Equal(0L, first.Pts90k);
                }
                finally
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        [Theory]
        [InlineData("libx264")]
        [InlineData("libx265")]
        public void RealFfmpegSidecarPairsEveryAccessUnitWithItsCaptureTimestamp(string encoder)
        {
            var check = FfmpegExecutableCheck.Check("", 1000);
            if (check.Status != FfmpegExecutableStatus.Found || !HasEncoder(check.ExecutablePath, encoder))
                return;

            const int frameCount = 30;
            const int width = 64;
            const int height = 64;
            var expected = new List<ulong>();
            var received = new List<ulong>();
            long underflows;
            string lastError;
            if (encoder == "libx264")
            {
                var sidecar = new FfmpegH264EncoderSidecar();
                var options = new FfmpegH264EncoderOptions
                {
                    Width = width,
                    Height = height,
                    FrameRate = 30,
                    BitrateKbps = 256,
                    KeyframeInterval = 30,
                    MaxOutputQueue = 64,
                    FfmpegPath = check.ExecutablePath
                };
                Assert.True(sidecar.Start(options), sidecar.LastError);
                try
                {
                    RunEncodeSession(
                        options.FrameByteCount,
                        frameCount,
                        sidecar.TrySubmitFrame,
                        () => sidecar.InputQueueDepth,
                        () => sidecar.TryDequeueEncodedAccessUnit(out var unit) ? unit.TimestampNs : (ulong?)null,
                        expected,
                        received);
                }
                finally
                {
                    underflows = sidecar.TimestampQueueUnderflows;
                    lastError = sidecar.LastError
                        + " submitted=" + sidecar.FramesSubmitted
                        + " produced=" + sidecar.AccessUnitsProduced
                        + " dropped=" + sidecar.AccessUnitsDropped
                        + " underflows=" + sidecar.TimestampQueueUnderflows
                        + " inputDepth=" + sidecar.InputQueueDepth
                        + " pendingTimestamps=" + sidecar.PendingTimestampCountForTests;
                    sidecar.Stop();
                }
            }
            else
            {
                var sidecar = new FfmpegH265EncoderSidecar();
                var options = new FfmpegH265EncoderOptions
                {
                    Width = width,
                    Height = height,
                    FrameRate = 30,
                    BitrateKbps = 256,
                    KeyframeInterval = 30,
                    MaxOutputQueue = 64,
                    FfmpegPath = check.ExecutablePath
                };
                Assert.True(sidecar.Start(options), sidecar.LastError);
                try
                {
                    RunEncodeSession(
                        options.FrameByteCount,
                        frameCount,
                        sidecar.TrySubmitFrame,
                        () => sidecar.InputQueueDepth,
                        () => sidecar.TryDequeueEncodedAccessUnit(out var unit) ? unit.TimestampNs : (ulong?)null,
                        expected,
                        received);
                }
                finally
                {
                    underflows = sidecar.TimestampQueueUnderflows;
                    lastError = sidecar.LastError
                        + " submitted=" + sidecar.FramesSubmitted
                        + " produced=" + sidecar.AccessUnitsProduced
                        + " dropped=" + sidecar.AccessUnitsDropped
                        + " underflows=" + sidecar.TimestampQueueUnderflows
                        + " inputDepth=" + sidecar.InputQueueDepth
                        + " pendingTimestamps=" + sidecar.PendingTimestampCountForTests;
                    sidecar.Stop();
                }
            }

            // The demuxer completes a PES when the next one starts, so the final
            // access unit may still be pending when the session is stopped.
            Assert.True(received.Count >= frameCount - 1,
                "received " + received.Count + " of " + frameCount + " access units; lastError=" + lastError);
            Assert.Equal(expected.GetRange(0, received.Count), received);
            Assert.Equal(0L, underflows);
        }

        private static void RunEncodeSession(
            int frameBytes,
            int frameCount,
            Func<byte[], ulong, bool> trySubmit,
            Func<int> inputQueueDepth,
            Func<ulong?> tryDequeueTimestamp,
            List<ulong> expected,
            List<ulong> received)
        {
            var deadline = Stopwatch.StartNew();
            void Drain()
            {
                while (tryDequeueTimestamp() is ulong timestamp)
                    received.Add(timestamp);
            }

            for (var index = 0; index < frameCount; index++)
            {
                var frame = new byte[frameBytes];
                for (var offset = 0; offset < frame.Length; offset++)
                    frame[offset] = (byte)(index * 7 + offset);

                // Capture times are deliberately irregular so a FIFO or
                // frame-rate-derived guess cannot reproduce them.
                var timestampNs = 1_000_000_000UL + (ulong)index * 33_333_333UL + (ulong)(index % 3) * 1_234UL;

                // The input queue is drop-oldest by design (latest camera frame wins).
                // Pace like a camera: submit only after the writer took the previous
                // frame, so every submitted frame actually reaches FFmpeg.
                while (inputQueueDepth() > 0)
                {
                    Assert.True(deadline.ElapsedMilliseconds < 20000, "FFmpeg writer did not take frame " + (index - 1) + ".");
                    Drain();
                    System.Threading.Thread.Sleep(1);
                }

                while (!trySubmit(frame, timestampNs))
                {
                    Assert.True(deadline.ElapsedMilliseconds < 20000, "FFmpeg did not accept frame " + index + ".");
                    Drain();
                    System.Threading.Thread.Sleep(1);
                }

                expected.Add(timestampNs);
                Drain();
            }

            while (received.Count < frameCount - 1 && deadline.ElapsedMilliseconds < 20000)
            {
                Drain();
                System.Threading.Thread.Sleep(5);
            }

            Drain();
        }

        private static bool HasEncoder(string ffmpegPath, string encoder)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }))
                {
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return false;
                    }

                    stderrTask.Wait(1000);
                    return output.IndexOf(" " + encoder + " ", StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return false;
            }
        }

        [Fact]
        public void MatchesAcrossThirtyThreeBitPtsWrap()
        {
            var matcher = new FfmpegTimestampMatcher();
            matcher.Reset(30);
            matcher.Track(123);
            matcher.Track(456);

            Assert.True(matcher.TryResolve((1L << 33) - 1000, out var first));
            Assert.Equal(123UL, first);
            Assert.True(matcher.TryResolve(2000, out var second));
            Assert.Equal(456UL, second);
        }
    }
}
