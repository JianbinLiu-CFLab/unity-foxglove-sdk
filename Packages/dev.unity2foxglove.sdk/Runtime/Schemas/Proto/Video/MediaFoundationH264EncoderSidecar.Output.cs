// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Experimental Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Experimental Media Foundation H.264 encoder sidecar. RGB frames enter a
    /// bounded worker queue so TrySubmitFrame remains non-blocking like the
    /// other video sidecars.
    /// </summary>
    public sealed partial class MediaFoundationH264EncoderSidecar
    {
        private void DrainEncoderOutput()
        {
            var consecutiveStreamChanges = 0;
            while (true)
            {
                var info = GetCachedOutputStreamInfo();

                IMFSample sample = null;
                IMFMediaBuffer buffer = null;
                var output = new MftOutputDataBuffer();
                var outputPtr = IntPtr.Zero;
                var samplePtr = IntPtr.Zero;
                IMFSample outputSample = null;
                int hr;
                try
                {
                    if ((info.dwFlags & MftOutputStreamProvidesSamples) == 0)
                    {
                        var size = Math.Max(info.cbSize, Math.Max(1, _options.Nv12FrameByteCount));
                        hr = NativeMethods.MFCreateMemoryBuffer(size, out buffer);
                        ThrowForHr(hr, "MFCreateMemoryBuffer output failed.");
                        hr = NativeMethods.MFCreateSample(out sample);
                        ThrowForHr(hr, "MFCreateSample output failed.");
                        hr = sample.AddBuffer(buffer);
                        ThrowForHr(hr, "Output IMFSample.AddBuffer failed.");
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path.
                        samplePtr = Marshal.GetIUnknownForObject(sample);
#pragma warning restore CA1416
                        output.pSample = samplePtr;
                    }

                    outputPtr = Marshal.AllocHGlobal(s_mftOutputDataBufferSize);
                    Marshal.StructureToPtr(output, outputPtr, false);
                    hr = _transform.ProcessOutput(0, 1, outputPtr, out _);
                    output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputPtr);
                    if (hr == MfETransformNeedMoreInput)
                        return;

                    if (hr == MfETransformStreamChange)
                    {
                        consecutiveStreamChanges++;
                        HandleOutputStreamChange(consecutiveStreamChanges);
                        continue;
                    }

                    consecutiveStreamChanges = 0;
                    ThrowForHr(hr, "Media Foundation H.264 ProcessOutput failed.");
                    if (output.pSample != IntPtr.Zero)
                    {
                        if (output.pSample == samplePtr)
                        {
                            outputSample = sample;
                        }
                        else
                        {
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path.
                            outputSample = (IMFSample)Marshal.GetObjectForIUnknown(output.pSample);
#pragma warning restore CA1416
                        }
                    }

                    ExtractOutputSample(outputSample ?? sample);
                }
                finally
                {
                    if (outputPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(outputPtr);
                    if (output.pEvents != IntPtr.Zero)
                        Marshal.Release(output.pEvents);
                    if (output.pSample != IntPtr.Zero && output.pSample != samplePtr)
                        Marshal.Release(output.pSample);
                    if (samplePtr != IntPtr.Zero)
                        Marshal.Release(samplePtr);
                    if (outputSample != null && !ReferenceEquals(outputSample, sample))
                        ReleaseComObject(outputSample);
                    ReleaseComObject(sample);
                    ReleaseComObject(buffer);
                }
            }
        }

        private void HandleOutputStreamChange(int consecutiveStreamChanges)
        {
            if (consecutiveStreamChanges > MaxConsecutiveOutputStreamChanges)
                throw new InvalidOperationException("Media Foundation H.264 output stream change did not settle.");

            IMFMediaType outputType = null;
            try
            {
                var hr = _transform.GetOutputAvailableType(0, 0, out outputType);
                if (hr < 0 || outputType == null)
                {
                    ReleaseComObject(outputType);
                    outputType = CreateH264OutputType(_options);
                }

                hr = _transform.SetOutputType(0, outputType, 0);
                ThrowForHr(hr, "Media Foundation H.264 SetOutputType after stream change failed.");
                CacheOutputSequenceHeader();
                RefreshOutputStreamInfo();
            }
            finally
            {
                ReleaseComObject(outputType);
            }
        }

        private void ExtractOutputSample(IMFSample sample)
        {
            if (sample == null)
                return;

            IMFMediaBuffer buffer = null;
            try
            {
                var hr = sample.ConvertToContiguousBuffer(out buffer);
                ThrowForHr(hr, "ConvertToContiguousBuffer failed.");
                var bytes = ReadBuffer(buffer);
                var timestampNs = ResolveOutputTimestamp(sample);
                if (_normalizer.TryNormalizeSample(bytes, out var accessUnit))
                    EnqueueAccessUnit(accessUnit, timestampNs);
            }
            finally
            {
                ReleaseComObject(buffer);
            }
        }

        private void CacheOutputSequenceHeader()
        {
            IMFMediaType currentType = null;
            try
            {
                var hr = _transform.GetOutputCurrentType(0, out currentType);
                if (hr < 0 || currentType == null)
                    return;

                var key = MfGuids.MF_MT_MPEG_SEQUENCE_HEADER;
                hr = currentType.GetBlobSize(ref key, out var size);
                if (hr < 0 || size <= 0)
                    return;

                var blob = new byte[size];
                hr = currentType.GetBlob(ref key, blob, blob.Length, out _);
                if (hr >= 0)
                    _normalizer.CacheParameterSets(blob);
            }
            finally
            {
                ReleaseComObject(currentType);
            }
        }

        private void EnqueueAccessUnit(byte[] accessUnit, ulong timestampNs)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    LastDiagnosticLine = "Media Foundation H.264 output queue full; capture admission is holding new frames.";
                    return;
                }

                _outputAccessUnits.Enqueue(new EncodedVideoAccessUnit(accessUnit, timestampNs));
                _outputCount++;
            }
        }

        private byte[] EnsureNv12Scratch()
        {
            var expectedBytes = _options != null ? _options.Nv12FrameByteCount : 0;
            if (expectedBytes <= 0)
                throw new InvalidOperationException("Media Foundation H.264 NV12 frame byte count is invalid.");

            if (_nv12Scratch == null || _nv12Scratch.Length != expectedBytes)
                _nv12Scratch = new byte[expectedBytes];

            return _nv12Scratch;
        }

        private void RegisterSampleTimestamp(long sampleTime, ulong timestampNs)
        {
            lock (_outputLock)
            {
                if (_sampleTimestampNsByTime.Count >= MaxTrackedSampleTimestamps)
                    EvictOldestSampleTimestamp();

                if (_sampleTimestampNodesByTime.TryGetValue(sampleTime, out var existingNode))
                    _sampleTimestampOrder.Remove(existingNode);

                _sampleTimestampNsByTime[sampleTime] = timestampNs;
                _sampleTimestampNodesByTime[sampleTime] = _sampleTimestampOrder.AddLast(sampleTime);
            }
        }

        private ulong ResolveOutputTimestamp(IMFSample sample)
        {
            var hr = sample.GetSampleTime(out var sampleTime);
            if (hr < 0)
                return 0UL;

            lock (_outputLock)
            {
                if (!_sampleTimestampNsByTime.TryGetValue(sampleTime, out var timestampNs))
                    return 0UL;

                _sampleTimestampNsByTime.Remove(sampleTime);
                if (_sampleTimestampNodesByTime.TryGetValue(sampleTime, out var node))
                {
                    _sampleTimestampOrder.Remove(node);
                    _sampleTimestampNodesByTime.Remove(sampleTime);
                }

                return timestampNs;
            }
        }

        private void EvictOldestSampleTimestamp()
        {
            var node = _sampleTimestampOrder.First;
            if (node == null)
                return;

            var oldestSampleTime = node.Value;
            _sampleTimestampOrder.RemoveFirst();
            _sampleTimestampNodesByTime.Remove(oldestSampleTime);
            _sampleTimestampNsByTime.Remove(oldestSampleTime);
            if (Interlocked.Increment(ref _evictedTimestampCount) == 1)
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "Media Foundation H.264 evicted old sample timestamps; output timestamps may fall back to zero under sustained backlog.");
        }

        private MftOutputStreamInfo GetCachedOutputStreamInfo()
        {
            if (!_hasOutputStreamInfo)
                RefreshOutputStreamInfo();
            return _outputStreamInfo;
        }

        private void RefreshOutputStreamInfo()
        {
            var hr = _transform.GetOutputStreamInfo(0, out _outputStreamInfo);
            ThrowForHr(hr, "Media Foundation H.264 GetOutputStreamInfo failed.");
            _hasOutputStreamInfo = true;
        }

        private void ClearSampleTimestampMap()
        {
            lock (_outputLock)
            {
                _sampleTimestampNsByTime.Clear();
                _sampleTimestampNodesByTime.Clear();
                _sampleTimestampOrder.Clear();
            }
        }

        private static void WriteBuffer(IMFMediaBuffer buffer, byte[] data)
        {
            IntPtr ptr = IntPtr.Zero;
            var locked = false;
            try
            {
                var hr = buffer.Lock(out ptr, out _, out _);
                ThrowForHr(hr, "IMFMediaBuffer.Lock failed.");
                locked = true;
                Marshal.Copy(data, 0, ptr, data.Length);
                hr = buffer.SetCurrentLength(data.Length);
                ThrowForHr(hr, "IMFMediaBuffer.SetCurrentLength failed.");
            }
            finally
            {
                if (locked)
                    buffer.Unlock();
            }
        }

        private static byte[] ReadBuffer(IMFMediaBuffer buffer)
        {
            IntPtr ptr = IntPtr.Zero;
            var locked = false;
            try
            {
                var hr = buffer.Lock(out ptr, out _, out var currentLength);
                ThrowForHr(hr, "IMFMediaBuffer.Lock output failed.");
                locked = true;
                var bytes = new byte[Math.Max(0, currentLength)];
                if (bytes.Length > 0)
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (locked)
                    buffer.Unlock();
            }
        }

        private static void SetGuid(IMFAttributes attributes, Guid key, Guid value)
        {
            var k = key;
            var v = value;
            ThrowForHr(attributes.SetGUID(ref k, ref v), "IMFAttributes.SetGUID failed.");
        }

        private static void SetUInt32(IMFAttributes attributes, Guid key, int value)
        {
            var k = key;
            ThrowForHr(attributes.SetUINT32(ref k, value), "IMFAttributes.SetUINT32 failed.");
        }

        private static void SetFrameSize(IMFAttributes attributes, int width, int height)
        {
            var k = MfGuids.MF_MT_FRAME_SIZE;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(width, height)), "IMFAttributes.SetUINT64 frame size failed.");
        }

        private static void SetFrameRate(IMFAttributes attributes, int numerator, int denominator)
        {
            var k = MfGuids.MF_MT_FRAME_RATE;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(numerator, denominator)), "IMFAttributes.SetUINT64 frame rate failed.");
        }

        private static void SetPixelAspectRatio(IMFAttributes attributes, int numerator, int denominator)
        {
            var k = MfGuids.MF_MT_PIXEL_ASPECT_RATIO;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(numerator, denominator)), "IMFAttributes.SetUINT64 pixel aspect failed.");
        }

        private static long PackUInt32PairAsUInt64(int high, int low)
        {
            return ((long)(uint)high << 32) | (uint)low;
        }

        private static void ThrowForHr(int hr, string message)
        {
            if (hr >= 0)
                return;

            throw new InvalidOperationException(message + " HRESULT=0x" + hr.ToString("X8"));
        }

        private static string DescribeException(Exception ex)
        {
            if (ex == null)
                return "Unknown Media Foundation H.264 encoder failure.";

            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().FullName
                : ex.GetType().FullName + ": " + ex.Message;
            if (ex.HResult != 0)
                message += " HResult=0x" + ex.HResult.ToString("X8");
            if (ex.TargetSite != null)
                message += " Target=" + ex.TargetSite.Name;
            if (ex.InnerException != null)
                message += " Inner=" + DescribeException(ex.InnerException);
            return message;
        }

        private static string AppendDiagnostic(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next))
                return current;
            return string.IsNullOrWhiteSpace(current) ? next : current + " " + next;
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
                return;

            try
            {
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path; release is best-effort cleanup.
                Marshal.ReleaseComObject(comObject);
#pragma warning restore CA1416
            }
            catch
            {
                // Ignore release failures during cleanup.
            }
        }
    }
}
