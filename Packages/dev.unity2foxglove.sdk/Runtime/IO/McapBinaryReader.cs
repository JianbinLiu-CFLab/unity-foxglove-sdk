using System.Text;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Low-level LE binary read helpers for MCAP records.
    /// Shared by Runtime McapReader and Tests McapRecordReader.
    /// </summary>
    public static class McapBinaryReader
    {
        public static ushort ReadU16LE(byte[] buf, ref int off) { var v = (ushort)(buf[off] | (buf[off + 1] << 8)); off += 2; return v; }
        public static uint ReadU32LE(byte[] buf, ref int off) { var v = (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24)); off += 4; return v; }
        public static ulong ReadU64LE(byte[] buf, ref int off)
        {
            var v = (ulong)buf[off] | ((ulong)buf[off + 1] << 8) | ((ulong)buf[off + 2] << 16) | ((ulong)buf[off + 3] << 24)
                  | ((ulong)buf[off + 4] << 32) | ((ulong)buf[off + 5] << 40) | ((ulong)buf[off + 6] << 48) | ((ulong)buf[off + 7] << 56);
            off += 8; return v;
        }

        public static string ReadString(byte[] buf, ref int off)
        {
            var len = ReadU32LE(buf, ref off);
            var s = Encoding.UTF8.GetString(buf, off, (int)len);
            off += (int)len;
            return s;
        }

        public static byte[] ReadPrefixed(byte[] buf, ref int off)
        {
            var len = ReadU32LE(buf, ref off);
            var data = new byte[len];
            System.Buffer.BlockCopy(buf, off, data, 0, (int)len);
            off += (int)len;
            return data;
        }

        public static bool MatchesMagic(byte[] buf, int off)
        {
            var magic = McapWriter.Magic;
            for (var i = 0; i < magic.Length; i++)
                if (buf[off + i] != magic[i]) return false;
            return true;
        }
    }
}
