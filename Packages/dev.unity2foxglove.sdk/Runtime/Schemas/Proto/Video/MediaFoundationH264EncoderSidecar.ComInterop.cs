// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Private Media Foundation COM interop declarations for the Windows H.264 sidecar.

using System;
using System.Runtime.InteropServices;

namespace Foxglove.Schemas.Video
{
    public sealed partial class MediaFoundationH264EncoderSidecar
    {
        // Media Foundation and CodecAPI GUIDs used by the Windows H.264 transform setup.
        private static class MfGuids
        {
            public static readonly Guid CLSID_CMSH264EncoderMFT = new Guid("6CA50344-051A-4DED-9779-A43305165E35");
            public static readonly Guid IID_IMFTransform = new Guid("BF94C121-5B05-4E6F-8000-BA598961414D");
            public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
            public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
            public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
            public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
            public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
            public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
            public static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("AD76A80B-2D5C-4E0B-B375-64E520137036");
            public static readonly Guid MF_MT_MPEG_SEQUENCE_HEADER = new Guid("3C036DE7-3AD0-4C9E-9216-EE6D6AC21CB3");
            public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
            public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
            public static readonly Guid CODECAPI_AVLowLatencyMode = new Guid("9C27891A-ED7A-40E1-88E8-B22727A024EE");
            public static readonly Guid CODECAPI_AVEncCommonLowLatency = new Guid("9D3ECD55-89E8-490A-970A-0C9548D5A56E");
            public static readonly Guid CODECAPI_AVEncCommonRealTime = new Guid("143A0FF6-A131-43DA-B81E-98FBB8EC378E");
            public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new Guid("1C0608E9-370C-4710-8A58-CB6181C42423");
            public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new Guid("F7222374-2144-4815-B550-A37F8E12EE52");
            public static readonly Guid CODECAPI_AVEncMPVGOPSize = new Guid("95F31B26-95A4-41AA-9303-246A7FC6EEF1");
            public static readonly Guid CODECAPI_AVEncMPVDefaultBPictureCount = new Guid("8D390AAC-DC5C-4200-B57F-814D04BABAB2");
        }

        private static class NativeMethods
        {
            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern void CoUninitialize();

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern int CoCreateInstance(
                ref Guid rclsid,
                IntPtr pUnkOuter,
                int dwClsContext,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)] out IMFTransform ppv);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFStartup(int version, int dwFlags);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFShutdown();

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateMediaType([MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMFType);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateMemoryBuffer(int cbMaxLength, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateSample([MarshalAs(UnmanagedType.Interface)] out IMFSample ppIMFSample);

        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            private readonly long _a;
            private readonly long _b;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Variant
        {
            private readonly ushort _variantType;
            private readonly ushort _reserved1;
            private readonly ushort _reserved2;
            private readonly ushort _reserved3;
            private readonly long _value;

            private Variant(ushort variantType, long value)
            {
                _variantType = variantType;
                _reserved1 = 0;
                _reserved2 = 0;
                _reserved3 = 0;
                _value = value;
            }

            public static Variant FromBool(bool value)
                => new Variant(VtBool, value ? VariantTrue : 0);

            public static Variant FromUInt32(uint value)
                => new Variant(VtUI4, value);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftInputStreamInfo
        {
            public long hnsMaxLatency;
            public int dwFlags;
            public int cbSize;
            public int cbMaxLookahead;
            public int cbAlignment;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputStreamInfo
        {
            public int dwFlags;
            public int cbSize;
            public int cbAlignment;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputDataBuffer
        {
            public int dwStreamID;
            public IntPtr pSample;
            public int dwStatus;
            public IntPtr pEvents;
        }

        [ComImport]
        [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(
                ref Guid guidKey,
                [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize,
                out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, ref PropVariant value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(
                ref Guid guidKey,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        }

        [ComImport]
        [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
        }

        [ComImport]
        [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
            [PreserveSig] int SetCurrentLength(int cbCurrentLength);
            [PreserveSig] int GetMaxLength(out int pcbMaxLength);
        }

        [ComImport]
        [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            // IMFAttributes vtable prefix. IMFSample extends IMFAttributes in native code,
            // so the derived COM interface must be flattened for reliable C# interop.
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(
                ref Guid guidKey,
                [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize,
                out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, ref PropVariant value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(
                ref Guid guidKey,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);

            [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);
            [PreserveSig] int SetSampleFlags(int dwSampleFlags);
            [PreserveSig] int GetSampleTime(out long phnsSampleTime);
            [PreserveSig] int SetSampleTime(long hnsSampleTime);
            [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
            [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
            [PreserveSig] int GetBufferCount(out int pdwBufferCount);
            [PreserveSig] int GetBufferByIndex(int dwIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
            [PreserveSig] int ConvertToContiguousBuffer([MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
            [PreserveSig] int AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
            [PreserveSig] int RemoveBufferByIndex(int dwIndex);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out int pcbTotalLength);
            [PreserveSig] int CopyToBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
        }

        [ComImport]
        [Guid("BF94C121-5B05-4E6F-8000-BA598961414D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFTransform
        {
            [PreserveSig] int GetStreamLimits(out int pdwInputMinimum, out int pdwInputMaximum, out int pdwOutputMinimum, out int pdwOutputMaximum);
            [PreserveSig] int GetStreamCount(out int pcInputStreams, out int pcOutputStreams);
            [PreserveSig] int GetStreamIDs(int dwInputIDArraySize, [Out] int[] pdwInputIDs, int dwOutputIDArraySize, [Out] int[] pdwOutputIDs);
            [PreserveSig] int GetInputStreamInfo(int dwInputStreamID, out MftInputStreamInfo pStreamInfo);
            [PreserveSig] int GetOutputStreamInfo(int dwOutputStreamID, out MftOutputStreamInfo pStreamInfo);
            [PreserveSig] int GetAttributes([MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int GetInputStreamAttributes(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int GetOutputStreamAttributes(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int DeleteInputStream(int dwStreamID);
            [PreserveSig] int AddInputStreams(int cStreams, [In] int[] adwStreamIDs);
            [PreserveSig] int GetInputAvailableType(int dwInputStreamID, int dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetOutputAvailableType(int dwOutputStreamID, int dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int SetInputType(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, int dwFlags);
            [PreserveSig] int SetOutputType(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, int dwFlags);
            [PreserveSig] int GetInputCurrentType(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetOutputCurrentType(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetInputStatus(int dwInputStreamID, out int pdwFlags);
            [PreserveSig] int GetOutputStatus(out int pdwFlags);
            [PreserveSig] int SetOutputBounds(long hnsLowerBound, long hnsUpperBound);
            [PreserveSig] int ProcessEvent(int dwInputStreamID, IntPtr pEvent);
            [PreserveSig] int ProcessMessage(int eMessage, IntPtr ulParam);
            [PreserveSig] int ProcessInput(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFSample pSample, int dwFlags);
            [PreserveSig] int ProcessOutput(int dwFlags, int cOutputBufferCount, IntPtr pOutputSamples, out int pdwStatus);
        }

        [ComImport]
        [Guid("901DB4C7-31CE-41A2-85DC-8FA0BF41B8DA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICodecAPI
        {
            [PreserveSig] int IsSupported(ref Guid api);
            [PreserveSig] int IsModifiable(ref Guid api);
            [PreserveSig] int GetParameterRange(ref Guid api, out Variant valueMin, out Variant valueMax, out Variant steppingDelta);
            [PreserveSig] int GetParameterValues(ref Guid api, out IntPtr values, out int valuesCount);
            [PreserveSig] int GetDefaultValue(ref Guid api, out Variant value);
            [PreserveSig] int GetValue(ref Guid api, out Variant value);
            [PreserveSig] int SetValue(ref Guid api, ref Variant value);
            [PreserveSig] int RegisterForEvent(ref Guid api, IntPtr userData);
            [PreserveSig] int UnregisterForEvent(ref Guid api);
            [PreserveSig] int SetAllDefaults();
            [PreserveSig] int SetValueWithNotify(ref Guid api, ref Variant value, out IntPtr changedParam, out int changedParamCount);
            [PreserveSig] int SetAllDefaultsWithNotify(out IntPtr changedParam, out int changedParamCount);
            [PreserveSig] int GetAllSettings(IntPtr stream);
            [PreserveSig] int SetAllSettings(IntPtr stream);
            [PreserveSig] int SetAllSettingsWithNotify(IntPtr stream, out IntPtr changedParam, out int changedParamCount);
        }
    }
}
