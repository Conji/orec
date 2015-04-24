using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ore.Compiler
{
    public static class Extensions
    {
        public static byte ReadUByte(this Stream stream)
        {
            var value = stream.ReadByte();

            return (byte)value;
        }

        public static void WriteUByte(this Stream stream, byte value)
        {
            stream.WriteByte(value);
        }

        public static sbyte ReadByte(this Stream stream)
        {
            return (sbyte)stream.ReadUByte();
        }

        public static void WriteByte(this Stream stream, sbyte value)
        {
            stream.WriteUByte((byte)value);
        }

        public static ushort ReadUShort(this Stream stream)
        {
            return (ushort)(
                (stream.ReadUByte() << 8) |
                stream.ReadUByte());
        }

        public static void WriteUShort(this Stream stream, ushort value)
        {
            stream.Write(new[] {
                (byte)((value & 0xFF00) >> 8),
                (byte)(value & 0xFF)
            }, 0, 2);
        }

        public static short ReadShort(this Stream stream)
        {
            return (short)stream.ReadUShort();
        }

        public static void WriteShort(this Stream stream, short value)
        {
            stream.WriteUShort((ushort)value);
        }

        public static uint ReadUInt(this Stream stream)
        {
            return (uint)(
                (stream.ReadUByte() << 24) |
                (stream.ReadUByte() << 16) |
                (stream.ReadUByte() << 8) |
                stream.ReadUByte());
        }

        public static void WriteUInt(this Stream stream, uint value)
        {
            stream.Write(new[] {
                (byte)((value & 0xFF000000) >> 24),
                (byte)((value & 0xFF0000) >> 16),
                (byte)((value & 0xFF00) >> 8),
                (byte)(value & 0xFF)
            }, 0, 4);
        }

        public static int ReadInt(this Stream stream)
        {
            return (int)stream.ReadUInt();
        }

        public static void WriteInt(this Stream stream, int value)
        {
            stream.WriteUInt((uint)value);
        }

        public static byte[] ReadUByteArray(this Stream stream, int length)
        {
            var result = new byte[length];
            if (length == 0)
                return result;
            int n = length;
            while (true)
            {
                n -= stream.Read(result, length - n, n);
                if (n == 0)
                    break;
                Thread.Sleep(1);
            }
            return result;
        }

        public static void WriteUByteArray(this Stream stream, byte[] value)
        {
            stream.Write(value, 0, value.Length);
        }

        public static void WriteUByteArray(this Stream stream, byte[] value, int offset, int count)
        {
            stream.Write(value, offset, count);
        }

        public static sbyte[] ReadByteArray(this Stream stream, int length)
        {
            return (sbyte[])(Array)stream.ReadUByteArray(length);
        }

        public static void WriteByteArray(this Stream stream, sbyte[] value)
        {
            stream.Write((byte[])(Array)value, 0, value.Length);
        }

        public static ushort[] ReadUShortArray(this Stream stream, int length)
        {
            var result = new ushort[length];
            if (length == 0)
                return result;
            for (var i = 0; i < length; i++)
                result[i] = stream.ReadUShort();
            return result;
        }

        public static void WriteUShortArray(this Stream stream, ushort[] value)
        {
            foreach (var t in value)
                stream.WriteUShort(t);
        }

        public static short[] ReadShortArray(this Stream stream, int length)
        {
            return (short[])(Array)stream.ReadUShortArray(length);
        }

        public static void WriteShortArray(this Stream stream, short[] value)
        {
            stream.WriteUShortArray((ushort[])(Array)value);
        }

        public static uint[] ReadUIntArray(this Stream stream, int length)
        {
            var result = new uint[length];
            if (length == 0)
                return result;
            for (var i = 0; i < length; i++)
                result[i] = stream.ReadUInt();
            return result;
        }

        public static void WriteUIntArray(this Stream stream, uint[] value)
        {
            foreach (var t in value)
                stream.WriteUInt(t);
        }

        public static int[] ReadIntArray(this Stream stream, int length)
        {
            return (int[])(Array)stream.ReadUIntArray(length);
        }

        public static void WriteIntArray(this Stream stream, int[] value)
        {
            stream.WriteUIntArray((uint[])(Array)value);
        }

        public static string ReadString(this Stream stream)
        {
            long length = stream.ReadInt();
            if (length == 0)
                return string.Empty;
            var data = stream.ReadUByteArray((int)length);
            return Encoding.UTF8.GetString(data);
        }

        public static void WriteString(this Stream stream, string value)
        {
            if (value == null) value = "";
            stream.WriteInt(Encoding.UTF8.GetByteCount(value));
            if (value.Length > 0)
                stream.WriteUByteArray(Encoding.UTF8.GetBytes(value));
        }
    }
}
