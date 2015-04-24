// ZlibBaseStream.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2009 Dino Chiesa and Microsoft Corporation.
// All rights reserved.
//
// This code module is part of DotNetZip, a zipfile class library.
//
// ------------------------------------------------------------------
//
// This code is licensed under the Microsoft Public License.
// See the file License.txt for the license details.
// More info on: http://dotnetzip.codeplex.com
//
// ------------------------------------------------------------------
//
// last saved (in emacs):
// Time-stamp: <2011-August-06 21:22:38>
//
// ------------------------------------------------------------------
//
// This module defines the ZlibBaseStream class, which is an intnernal
// base class for DeflateStream, ZlibStream and GZipStream.
//
// ------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ore.Compiler.Zlib
{

    internal enum ZlibStreamFlavor { Zlib = 1950, Deflate = 1951, Gzip = 1952 }

    internal class ZlibBaseStream : Stream
    {
        protected internal ZlibCodec Z = null; // deferred init... new ZlibCodec();

        protected internal StreamMode _streamMode = StreamMode.Undefined;
        protected internal FlushType FlushMode;
        protected internal ZlibStreamFlavor Flavor;
        protected internal CompressionMode CompressionMode;
        protected internal CompressionLevel Level;
        protected internal bool LeaveOpen;
        protected internal byte[] WorkingBuffer;
        protected internal int BufferSize = ZlibConstants.WorkingBufferSizeDefault;
        protected internal byte[] Buf1 = new byte[1];

        protected internal Stream Stream;
        protected internal CompressionStrategy Strategy = CompressionStrategy.Default;

        // workitem 7159
        Crc32 _crc;
        protected internal string GzipFileName;
        protected internal string GzipComment;
        protected internal DateTime GzipMtime;
        protected internal int GzipHeaderByteCount;

        internal int Crc32 { get { if (_crc == null) return 0; return _crc.Crc32Result; } }

        public ZlibBaseStream(Stream stream,
                              CompressionMode compressionMode,
                              CompressionLevel level,
                              ZlibStreamFlavor flavor,
                              bool leaveOpen)
            : base()
        {
            this.FlushMode = FlushType.None;
            //this._workingBuffer = new byte[WORKING_BUFFER_SIZE_DEFAULT];
            this.Stream = stream;
            this.LeaveOpen = leaveOpen;
            this.CompressionMode = compressionMode;
            this.Flavor = flavor;
            this.Level = level;
            // workitem 7159
            if (flavor == ZlibStreamFlavor.Gzip)
            {
                this._crc = new Crc32();
            }
        }


        protected internal bool WantCompress => (this.CompressionMode == CompressionMode.Compress);

        private ZlibCodec z
        {
            get
            {
                if (Z == null)
                {
                    bool wantRfc1950Header = (this.Flavor == ZlibStreamFlavor.Zlib);
                    Z = new ZlibCodec();
                    if (this.CompressionMode == CompressionMode.Decompress)
                    {
                        Z.InitializeInflate(wantRfc1950Header);
                    }
                    else
                    {
                        Z.Strategy = Strategy;
                        Z.InitializeDeflate(this.Level, wantRfc1950Header);
                    }
                }
                return Z;
            }
        }



        private byte[] workingBuffer
        {
            get
            {
                if (WorkingBuffer == null)
                    WorkingBuffer = new byte[BufferSize];
                return WorkingBuffer;
            }
        }



        public override void Write(Byte[] buffer, int offset, int count)
        {
            // workitem 7159
            // calculate the CRC on the unccompressed data  (before writing)
            if (_crc != null)
                _crc.SlurpBlock(buffer, offset, count);

            if (_streamMode == StreamMode.Undefined)
                _streamMode = StreamMode.Writer;
            else if (_streamMode != StreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (count == 0)
                return;

            // first reference of z property will initialize the private var _z
            z.InputBuffer = buffer;
            Z.NextIn = offset;
            Z.AvailableBytesIn = count;
            bool done = false;
            do
            {
                Z.OutputBuffer = workingBuffer;
                Z.NextOut = 0;
                Z.AvailableBytesOut = WorkingBuffer.Length;
                int rc = (WantCompress)
                    ? Z.Deflate(FlushMode)
                    : Z.Inflate(FlushMode);
                if (rc != ZlibConstants.ZOk && rc != ZlibConstants.ZStreamEnd)
                    throw new ZlibException((WantCompress ? "de" : "in") + "flating: " + Z.Message);

                //if (_workingBuffer.Length - _z.AvailableBytesOut > 0)
                Stream.Write(WorkingBuffer, 0, WorkingBuffer.Length - Z.AvailableBytesOut);

                done = Z.AvailableBytesIn == 0 && Z.AvailableBytesOut != 0;

                // If GZIP and de-compress, we're done when 8 bytes remain.
                if (Flavor == ZlibStreamFlavor.Gzip && !WantCompress)
                    done = (Z.AvailableBytesIn == 8 && Z.AvailableBytesOut != 0);

            }
            while (!done);
        }



        private void Finish()
        {
            if (Z == null) return;

            if (_streamMode == StreamMode.Writer)
            {
                bool done = false;
                do
                {
                    Z.OutputBuffer = workingBuffer;
                    Z.NextOut = 0;
                    Z.AvailableBytesOut = WorkingBuffer.Length;
                    int rc = (WantCompress)
                        ? Z.Deflate(FlushType.Finish)
                        : Z.Inflate(FlushType.Finish);

                    if (rc != ZlibConstants.ZStreamEnd && rc != ZlibConstants.ZOk)
                    {
                        string verb = (WantCompress ? "de" : "in") + "flating";
                        if (Z.Message == null)
                            throw new ZlibException(String.Format("{0}: (rc = {1})", verb, rc));
                        else
                            throw new ZlibException(verb + ": " + Z.Message);
                    }

                    if (WorkingBuffer.Length - Z.AvailableBytesOut > 0)
                    {
                        Stream.Write(WorkingBuffer, 0, WorkingBuffer.Length - Z.AvailableBytesOut);
                    }

                    done = Z.AvailableBytesIn == 0 && Z.AvailableBytesOut != 0;
                    // If GZIP and de-compress, we're done when 8 bytes remain.
                    if (Flavor == ZlibStreamFlavor.Gzip && !WantCompress)
                        done = (Z.AvailableBytesIn == 8 && Z.AvailableBytesOut != 0);

                }
                while (!done);

                Flush();

                // workitem 7159
                if (Flavor == ZlibStreamFlavor.Gzip)
                {
                    if (WantCompress)
                    {
                        // Emit the GZIP trailer: CRC32 and  size mod 2^32
                        int c1 = _crc.Crc32Result;
                        Stream.Write(BitConverter.GetBytes(c1), 0, 4);
                        int c2 = (Int32)(_crc.TotalBytesRead & 0x00000000FFFFFFFF);
                        Stream.Write(BitConverter.GetBytes(c2), 0, 4);
                    }
                    else
                    {
                        throw new ZlibException("Writing with decompression is not supported.");
                    }
                }
            }
            // workitem 7159
            else if (_streamMode == StreamMode.Reader)
            {
                if (Flavor == ZlibStreamFlavor.Gzip)
                {
                    if (!WantCompress)
                    {
                        // workitem 8501: handle edge case (decompress empty stream)
                        if (Z.TotalBytesOut == 0L)
                            return;

                        // Read and potentially verify the GZIP trailer:
                        // CRC32 and size mod 2^32
                        byte[] trailer = new byte[8];

                        // workitems 8679 & 12554
                        if (Z.AvailableBytesIn < 8)
                        {
                            // Make sure we have read to the end of the stream
                            Array.Copy(Z.InputBuffer, Z.NextIn, trailer, 0, Z.AvailableBytesIn);
                            int bytesNeeded = 8 - Z.AvailableBytesIn;
                            int bytesRead = Stream.Read(trailer,
                                                         Z.AvailableBytesIn,
                                                         bytesNeeded);
                            if (bytesNeeded != bytesRead)
                            {
                                throw new ZlibException(String.Format("Missing or incomplete GZIP trailer. Expected 8 bytes, got {0}.",
                                                                      Z.AvailableBytesIn + bytesRead));
                            }
                        }
                        else
                        {
                            Array.Copy(Z.InputBuffer, Z.NextIn, trailer, 0, trailer.Length);
                        }

                        Int32 crc32Expected = BitConverter.ToInt32(trailer, 0);
                        Int32 crc32Actual = _crc.Crc32Result;
                        Int32 isizeExpected = BitConverter.ToInt32(trailer, 4);
                        Int32 isizeActual = (Int32)(Z.TotalBytesOut & 0x00000000FFFFFFFF);

                        if (crc32Actual != crc32Expected)
                            throw new ZlibException(String.Format("Bad CRC32 in GZIP trailer. (actual({0:X8})!=expected({1:X8}))", crc32Actual, crc32Expected));

                        if (isizeActual != isizeExpected)
                            throw new ZlibException(String.Format("Bad size in GZIP trailer. (actual({0})!=expected({1}))", isizeActual, isizeExpected));

                    }
                    else
                    {
                        throw new ZlibException("Reading with compression is not supported.");
                    }
                }
            }
        }


        private void End()
        {
            if (z == null)
                return;
            if (WantCompress)
            {
                Z.EndDeflate();
            }
            else
            {
                Z.EndInflate();
            }
            Z = null;
        }


        public override void Close()
        {
            if (Stream == null) return;
            try
            {
                Finish();
            }
            finally
            {
                End();
                if (!LeaveOpen) Stream.Close();
                Stream = null;
            }
        }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
            //_outStream.Seek(offset, origin);
        }
        public override void SetLength(Int64 value)
        {
            Stream.SetLength(value);
        }


#if NOT
        public int Read()
        {
            if (Read(_buf1, 0, 1) == 0)
                return 0;
            // calculate CRC after reading
            if (crc!=null)
                crc.SlurpBlock(_buf1,0,1);
            return (_buf1[0] & 0xFF);
        }
#endif

        private bool _nomoreinput = false;



        private string ReadZeroTerminatedString()
        {
            var list = new List<byte>();
            bool done = false;
            do
            {
                // workitem 7740
                int n = Stream.Read(Buf1, 0, 1);
                if (n != 1)
                    throw new ZlibException("Unexpected EOF reading GZIP header.");
                else
                {
                    if (Buf1[0] == 0)
                        done = true;
                    else
                        list.Add(Buf1[0]);
                }
            } while (!done);
            byte[] a = list.ToArray();
            return GZipStream.Iso8859Dash1.GetString(a, 0, a.Length);
        }


        private int _ReadAndValidateGzipHeader()
        {
            int totalBytesRead = 0;
            // read the header on the first read
            byte[] header = new byte[10];
            int n = Stream.Read(header, 0, header.Length);

            // workitem 8501: handle edge case (decompress empty stream)
            if (n == 0)
                return 0;

            if (n != 10)
                throw new ZlibException("Not a valid GZIP stream.");

            if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
                throw new ZlibException("Bad GZIP header.");

            Int32 timet = BitConverter.ToInt32(header, 4);
            GzipMtime = GZipStream.UnixEpoch.AddSeconds(timet);
            totalBytesRead += n;
            if ((header[3] & 0x04) == 0x04)
            {
                // read and discard extra field
                n = Stream.Read(header, 0, 2); // 2-byte length field
                totalBytesRead += n;

                Int16 extraLength = (Int16)(header[0] + header[1] * 256);
                byte[] extra = new byte[extraLength];
                n = Stream.Read(extra, 0, extra.Length);
                if (n != extraLength)
                    throw new ZlibException("Unexpected end-of-file reading GZIP header.");
                totalBytesRead += n;
            }
            if ((header[3] & 0x08) == 0x08)
                GzipFileName = ReadZeroTerminatedString();
            if ((header[3] & 0x10) == 0x010)
                GzipComment = ReadZeroTerminatedString();
            if ((header[3] & 0x02) == 0x02)
                Read(Buf1, 0, 1); // CRC16, ignore

            return totalBytesRead;
        }



        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
        {
            // According to MS documentation, any implementation of the IO.Stream.Read function must:
            // (a) throw an exception if offset & count reference an invalid part of the buffer,
            //     or if count < 0, or if buffer is null
            // (b) return 0 only upon EOF, or if count = 0
            // (c) if not EOF, then return at least 1 byte, up to <count> bytes

            if (_streamMode == StreamMode.Undefined)
            {
                if (!this.Stream.CanRead) throw new ZlibException("The stream is not readable.");
                // for the first read, set up some controls.
                _streamMode = StreamMode.Reader;
                // (The first reference to _z goes through the private accessor which
                // may initialize it.)
                z.AvailableBytesIn = 0;
                if (Flavor == ZlibStreamFlavor.Gzip)
                {
                    GzipHeaderByteCount = _ReadAndValidateGzipHeader();
                    // workitem 8501: handle edge case (decompress empty stream)
                    if (GzipHeaderByteCount == 0)
                        return 0;
                }
            }

            if (_streamMode != StreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");

            if (count == 0) return 0;
            if (_nomoreinput && WantCompress) return 0;  // workitem 8557
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (offset < buffer.GetLowerBound(0)) throw new ArgumentOutOfRangeException("offset");
            if ((offset + count) > buffer.GetLength(0)) throw new ArgumentOutOfRangeException("count");

            int rc = 0;

            // set up the output of the deflate/inflate codec:
            Z.OutputBuffer = buffer;
            Z.NextOut = offset;
            Z.AvailableBytesOut = count;

            // This is necessary in case _workingBuffer has been resized. (new byte[])
            // (The first reference to _workingBuffer goes through the private accessor which
            // may initialize it.)
            Z.InputBuffer = workingBuffer;

            do
            {
                // need data in _workingBuffer in order to deflate/inflate.  Here, we check if we have any.
                if ((Z.AvailableBytesIn == 0) && (!_nomoreinput))
                {
                    // No data available, so try to Read data from the captive stream.
                    Z.NextIn = 0;
                    Z.AvailableBytesIn = Stream.Read(WorkingBuffer, 0, WorkingBuffer.Length);
                    if (Z.AvailableBytesIn == 0)
                        _nomoreinput = true;

                }
                // we have data in InputBuffer; now compress or decompress as appropriate
                rc = (WantCompress)
                    ? Z.Deflate(FlushMode)
                    : Z.Inflate(FlushMode);

                if (_nomoreinput && (rc == ZlibConstants.ZBufError))
                    return 0;

                if (rc != ZlibConstants.ZOk && rc != ZlibConstants.ZStreamEnd)
                    throw new ZlibException(String.Format("{0}flating:  rc={1}  msg={2}", (WantCompress ? "de" : "in"), rc, Z.Message));

                if ((_nomoreinput || rc == ZlibConstants.ZStreamEnd) && (Z.AvailableBytesOut == count))
                    break; // nothing more to read
            }
            //while (_z.AvailableBytesOut == count && rc == ZlibConstants.Z_OK);
            while (Z.AvailableBytesOut > 0 && !_nomoreinput && rc == ZlibConstants.ZOk);


            // workitem 8557
            // is there more room in output?
            if (Z.AvailableBytesOut > 0)
            {
                if (rc == ZlibConstants.ZOk && Z.AvailableBytesIn == 0)
                {
                    // deferred
                }

                // are we completely done reading?
                if (_nomoreinput)
                {
                    // and in compression?
                    if (WantCompress)
                    {
                        // no more input data available; therefore we flush to
                        // try to complete the read
                        rc = Z.Deflate(FlushType.Finish);

                        if (rc != ZlibConstants.ZOk && rc != ZlibConstants.ZStreamEnd)
                            throw new ZlibException(String.Format("Deflating:  rc={0}  msg={1}", rc, Z.Message));
                    }
                }
            }


            rc = (count - Z.AvailableBytesOut);

            // calculate CRC after reading
            if (_crc != null)
                _crc.SlurpBlock(buffer, offset, rc);

            return rc;
        }



        public override Boolean CanRead => this.Stream.CanRead;

        public override Boolean CanSeek => this.Stream.CanSeek;

        public override Boolean CanWrite => this.Stream.CanWrite;

        public override Int64 Length => Stream.Length;

        public override long Position
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        internal enum StreamMode
        {
            Writer,
            Reader,
            Undefined,
        }


        public static void CompressString(String s, Stream compressor)
        {
            byte[] uncompressed = Encoding.UTF8.GetBytes(s);
            using (compressor)
            {
                compressor.Write(uncompressed, 0, uncompressed.Length);
            }
        }

        public static void CompressBuffer(byte[] b, Stream compressor)
        {
            // workitem 8460
            using (compressor)
            {
                compressor.Write(b, 0, b.Length);
            }
        }

        public static String UncompressString(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            byte[] working = new byte[1024];
            var encoding = Encoding.UTF8;
            using (var output = new MemoryStream())
            {
                using (decompressor)
                {
                    int n;
                    while ((n = decompressor.Read(working, 0, working.Length)) != 0)
                    {
                        output.Write(working, 0, n);
                    }
                }

                // reset to allow read from start
                output.Seek(0, SeekOrigin.Begin);
                var sr = new StreamReader(output, encoding);
                return sr.ReadToEnd();
            }
        }

        public static byte[] UncompressBuffer(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            byte[] working = new byte[1024];
            using (var output = new MemoryStream())
            {
                using (decompressor)
                {
                    int n;
                    while ((n = decompressor.Read(working, 0, working.Length)) != 0)
                    {
                        output.Write(working, 0, n);
                    }
                }
                return output.ToArray();
            }
        }

    }


}
