using Foxglove.Schemas.Video;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

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
            var check = FfmpegExecutableCheck.Check("", 1000);
            if (check.Status != FfmpegExecutableStatus.Found)
                throw SkipException.ForSkip("FFmpeg is not available for the timestamp integration test.");

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
