// Inflate.cs
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
// Time-stamp: <2010-January-08 18:32:12>
//
// ------------------------------------------------------------------
//
// This module defines classes for decompression. This code is derived
// from the jzlib implementation of zlib, but significantly modified.
// The object model is not the same, and many of the behaviors are
// different.  Nonetheless, in keeping with the license for jzlib, I am
// reproducing the copyright to that code here.
//
// ------------------------------------------------------------------
//
// Copyright (c) 2000,2001,2002,2003 ymnk, JCraft,Inc. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in
// the documentation and/or other materials provided with the distribution.
//
// 3. The names of the authors may not be used to endorse or promote products
// derived from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED ``AS IS'' AND ANY EXPRESSED OR IMPLIED WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL JCRAFT,
// INC. OR ANY CONTRIBUTORS TO THIS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
// LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
// EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// -----------------------------------------------------------------------
//
// This program is based on zlib-1.1.3; credit to authors
// Jean-loup Gailly(jloup@gzip.org) and Mark Adler(madler@alumni.caltech.edu)
// and contributors of zlib.
//
// -----------------------------------------------------------------------


using System;

namespace Ore.Compiler.Zlib
{
    sealed class InflateBlocks
    {
        private const int Many = 1440;

        // Table for deflate from PKZIP's appnote.txt.
        internal static readonly int[] Border = new int[]
        { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        private enum InflateBlockMode
        {
            Type   = 0,                     // get type bits (3, including end bit)
            Lens   = 1,                     // get lengths for stored
            Stored = 2,                     // processing stored block
            Table  = 3,                     // get table lengths
            Btree  = 4,                     // get bit lengths tree for a dynamic block
            Dtree  = 5,                     // get length, distance trees for a dynamic block
            Codes  = 6,                     // processing fixed or dynamic block
            Dry    = 7,                     // output remaining window bytes
            Done   = 8,                     // finished last block, done
            Bad    = 9,                     // ot a data error--stuck here
        }

        private InflateBlockMode _mode;                    // current inflate_block mode

        internal int Left;                                // if STORED, bytes left to copy

        internal int Table;                               // table lengths (14 bits)
        internal int Index;                               // index into blens (or border)
        internal int[] Blens;                             // bit lengths of codes
        internal int[] Bb = new int[1];                   // bit length tree depth
        internal int[] Tb = new int[1];                   // bit length decoding tree

        internal InflateCodes Codes = new InflateCodes(); // if CODES, current state

        internal int Last;                                // true if this block is the last block

        internal ZlibCodec Codec;                        // pointer back to this zlib stream

                                                          // mode independent information
        internal int Bitk;                                // bits in bit buffer
        internal int Bitb;                                // bit buffer
        internal int[] Hufts;                             // single malloc for tree space
        internal byte[] Window;                           // sliding window
        internal int End;                                 // one byte after sliding window
        internal int ReadAt;                              // window read pointer
        internal int WriteAt;                             // window write pointer
        internal Object Checkfn;                   // check function
        internal uint Check;                              // check on output

        internal InfTree Inftree = new InfTree();

        internal InflateBlocks(ZlibCodec codec, Object checkfn, int w)
        {
            Codec = codec;
            Hufts = new int[Many * 3];
            Window = new byte[w];
            End = w;
            this.Checkfn = checkfn;
            _mode = InflateBlockMode.Type;
            Reset();
        }

        internal uint Reset()
        {
            uint oldCheck = Check;
            _mode = InflateBlockMode.Type;
            Bitk = 0;
            Bitb = 0;
            ReadAt = WriteAt = 0;

            if (Checkfn != null)
                Codec._Adler32 = Check = Adler.Adler32(0, null, 0, 0);
            return oldCheck;
        }


        internal int Process(int r)
        {
            int t; // temporary storage
            int b; // bit buffer
            int k; // bits in bit buffer
            int p; // input data pointer
            int n; // bytes available there
            int q; // output window write pointer
            int m; // bytes to end of window or read pointer

            // copy input/output information to locals (UPDATE macro restores)

            p = Codec.NextIn;
            n = Codec.AvailableBytesIn;
            b = Bitb;
            k = Bitk;

            q = WriteAt;
            m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);


            // process input based on current state
            while (true)
            {
                switch (_mode)
                {
                    case InflateBlockMode.Type:

                        while (k < (3))
                        {
                            if (n != 0)
                            {
                                r = ZlibConstants.ZOk;
                            }
                            else
                            {
                                Bitb = b; Bitk = k;
                                Codec.AvailableBytesIn = n;
                                Codec.TotalBytesIn += p - Codec.NextIn;
                                Codec.NextIn = p;
                                WriteAt = q;
                                return Flush(r);
                            }

                            n--;
                            b |= (Codec.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }
                        t = (int)(b & 7);
                        Last = t & 1;

                        switch ((uint)t >> 1)
                        {
                            case 0:  // stored
                                b >>= 3; k -= (3);
                                t = k & 7; // go to byte boundary
                                b >>= t; k -= t;
                                _mode = InflateBlockMode.Lens; // get length of stored block
                                break;

                            case 1:  // fixed
                                int[] bl = new int[1];
                                int[] bd = new int[1];
                                int[][] tl = new int[1][];
                                int[][] td = new int[1][];
                                InfTree.inflate_trees_fixed(bl, bd, tl, td, Codec);
                                Codes.Init(bl[0], bd[0], tl[0], 0, td[0], 0);
                                b >>= 3; k -= 3;
                                _mode = InflateBlockMode.Codes;
                                break;

                            case 2:  // dynamic
                                b >>= 3; k -= 3;
                                _mode = InflateBlockMode.Table;
                                break;

                            case 3:  // illegal
                                b >>= 3; k -= 3;
                                _mode = InflateBlockMode.Bad;
                                Codec.Message = "invalid block type";
                                r = ZlibConstants.ZDataError;
                                Bitb = b; Bitk = k;
                                Codec.AvailableBytesIn = n;
                                Codec.TotalBytesIn += p - Codec.NextIn;
                                Codec.NextIn = p;
                                WriteAt = q;
                                return Flush(r);
                        }
                        break;

                    case InflateBlockMode.Lens:

                        while (k < (32))
                        {
                            if (n != 0)
                            {
                                r = ZlibConstants.ZOk;
                            }
                            else
                            {
                                Bitb = b; Bitk = k;
                                Codec.AvailableBytesIn = n;
                                Codec.TotalBytesIn += p - Codec.NextIn;
                                Codec.NextIn = p;
                                WriteAt = q;
                                return Flush(r);
                            }
                            ;
                            n--;
                            b |= (Codec.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        if ( ( ((~b)>>16) & 0xffff) != (b & 0xffff))
                        {
                            _mode = InflateBlockMode.Bad;
                            Codec.Message = "invalid stored block lengths";
                            r = ZlibConstants.ZDataError;

                            Bitb = b; Bitk = k;
                            Codec.AvailableBytesIn = n;
                            Codec.TotalBytesIn += p - Codec.NextIn;
                            Codec.NextIn = p;
                            WriteAt = q;
                            return Flush(r);
                        }
                        Left = (b & 0xffff);
                        b = k = 0; // dump bits
                        _mode = Left != 0 ? InflateBlockMode.Stored : (Last != 0 ? InflateBlockMode.Dry : InflateBlockMode.Type);
                        break;

                    case InflateBlockMode.Stored:
                        if (n == 0)
                        {
                            Bitb = b; Bitk = k;
                            Codec.AvailableBytesIn = n;
                            Codec.TotalBytesIn += p - Codec.NextIn;
                            Codec.NextIn = p;
                            WriteAt = q;
                            return Flush(r);
                        }

                        if (m == 0)
                        {
                            if (q == End && ReadAt != 0)
                            {
                                q = 0; m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);
                            }
                            if (m == 0)
                            {
                                WriteAt = q;
                                r = Flush(r);
                                q = WriteAt; m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);
                                if (q == End && ReadAt != 0)
                                {
                                    q = 0; m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);
                                }
                                if (m == 0)
                                {
                                    Bitb = b; Bitk = k;
                                    Codec.AvailableBytesIn = n;
                                    Codec.TotalBytesIn += p - Codec.NextIn;
                                    Codec.NextIn = p;
                                    WriteAt = q;
                                    return Flush(r);
                                }
                            }
                        }
                        r = ZlibConstants.ZOk;

                        t = Left;
                        if (t > n)
                            t = n;
                        if (t > m)
                            t = m;
                        Array.Copy(Codec.InputBuffer, p, Window, q, t);
                        p += t; n -= t;
                        q += t; m -= t;
                        if ((Left -= t) != 0)
                            break;
                        _mode = Last != 0 ? InflateBlockMode.Dry : InflateBlockMode.Type;
                        break;

                    case InflateBlockMode.Table:

                        while (k < (14))
                        {
                            if (n != 0)
                            {
                                r = ZlibConstants.ZOk;
                            }
                            else
                            {
                                Bitb = b; Bitk = k;
                                Codec.AvailableBytesIn = n;
                                Codec.TotalBytesIn += p - Codec.NextIn;
                                Codec.NextIn = p;
                                WriteAt = q;
                                return Flush(r);
                            }

                            n--;
                            b |= (Codec.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        Table = t = (b & 0x3fff);
                        if ((t & 0x1f) > 29 || ((t >> 5) & 0x1f) > 29)
                        {
                            _mode = InflateBlockMode.Bad;
                            Codec.Message = "too many length or distance symbols";
                            r = ZlibConstants.ZDataError;

                            Bitb = b; Bitk = k;
                            Codec.AvailableBytesIn = n;
                            Codec.TotalBytesIn += p - Codec.NextIn;
                            Codec.NextIn = p;
                            WriteAt = q;
                            return Flush(r);
                        }
                        t = 258 + (t & 0x1f) + ((t >> 5) & 0x1f);
                        if (Blens == null || Blens.Length < t)
                        {
                            Blens = new int[t];
                        }
                        else
                        {
                            Array.Clear(Blens, 0, t);
                            // for (int i = 0; i < t; i++)
                            // {
                            //     blens[i] = 0;
                            // }
                        }

                        b >>= 14;
                        k -= 14;


                        Index = 0;
                        _mode = InflateBlockMode.Btree;
                        goto case InflateBlockMode.Btree;

                    case InflateBlockMode.Btree:
                        while (Index < 4 + (Table >> 10))
                        {
                            while (k < (3))
                            {
                                if (n != 0)
                                {
                                    r = ZlibConstants.ZOk;
                                }
                                else
                                {
                                    Bitb = b; Bitk = k;
                                    Codec.AvailableBytesIn = n;
                                    Codec.TotalBytesIn += p - Codec.NextIn;
                                    Codec.NextIn = p;
                                    WriteAt = q;
                                    return Flush(r);
                                }

                                n--;
                                b |= (Codec.InputBuffer[p++] & 0xff) << k;
                                k += 8;
                            }

                            Blens[Border[Index++]] = b & 7;

                            b >>= 3; k -= 3;
                        }

                        while (Index < 19)
                        {
                            Blens[Border[Index++]] = 0;
                        }

                        Bb[0] = 7;
                        t = Inftree.inflate_trees_bits(Blens, Bb, Tb, Hufts, Codec);
                        if (t != ZlibConstants.ZOk)
                        {
                            r = t;
                            if (r == ZlibConstants.ZDataError)
                            {
                                Blens = null;
                                _mode = InflateBlockMode.Bad;
                            }

                            Bitb = b; Bitk = k;
                            Codec.AvailableBytesIn = n;
                            Codec.TotalBytesIn += p - Codec.NextIn;
                            Codec.NextIn = p;
                            WriteAt = q;
                            return Flush(r);
                        }

                        Index = 0;
                        _mode = InflateBlockMode.Dtree;
                        goto case InflateBlockMode.Dtree;

                    case InflateBlockMode.Dtree:
                        while (true)
                        {
                            t = Table;
                            if (!(Index < 258 + (t & 0x1f) + ((t >> 5) & 0x1f)))
                            {
                                break;
                            }

                            int i, j, c;

                            t = Bb[0];

                            while (k < t)
                            {
                                if (n != 0)
                                {
                                    r = ZlibConstants.ZOk;
                                }
                                else
                                {
                                    Bitb = b; Bitk = k;
                                    Codec.AvailableBytesIn = n;
                                    Codec.TotalBytesIn += p - Codec.NextIn;
                                    Codec.NextIn = p;
                                    WriteAt = q;
                                    return Flush(r);
                                }

                                n--;
                                b |= (Codec.InputBuffer[p++] & 0xff) << k;
                                k += 8;
                            }

                            t = Hufts[(Tb[0] + (b & InternalInflateConstants.InflateMask[t])) * 3 + 1];
                            c = Hufts[(Tb[0] + (b & InternalInflateConstants.InflateMask[t])) * 3 + 2];

                            if (c < 16)
                            {
                                b >>= t; k -= t;
                                Blens[Index++] = c;
                            }
                            else
                            {
                                // c == 16..18
                                i = c == 18 ? 7 : c - 14;
                                j = c == 18 ? 11 : 3;

                                while (k < (t + i))
                                {
                                    if (n != 0)
                                    {
                                        r = ZlibConstants.ZOk;
                                    }
                                    else
                                    {
                                        Bitb = b; Bitk = k;
                                        Codec.AvailableBytesIn = n;
                                        Codec.TotalBytesIn += p - Codec.NextIn;
                                        Codec.NextIn = p;
                                        WriteAt = q;
                                        return Flush(r);
                                    }

                                    n--;
                                    b |= (Codec.InputBuffer[p++] & 0xff) << k;
                                    k += 8;
                                }

                                b >>= t; k -= t;

                                j += (b & InternalInflateConstants.InflateMask[i]);

                                b >>= i; k -= i;

                                i = Index;
                                t = Table;
                                if (i + j > 258 + (t & 0x1f) + ((t >> 5) & 0x1f) || (c == 16 && i < 1))
                                {
                                    Blens = null;
                                    _mode = InflateBlockMode.Bad;
                                    Codec.Message = "invalid bit length repeat";
                                    r = ZlibConstants.ZDataError;

                                    Bitb = b; Bitk = k;
                                    Codec.AvailableBytesIn = n;
                                    Codec.TotalBytesIn += p - Codec.NextIn;
                                    Codec.NextIn = p;
                                    WriteAt = q;
                                    return Flush(r);
                                }

                                c = (c == 16) ? Blens[i-1] : 0;
                                do
                                {
                                    Blens[i++] = c;
                                }
                                while (--j != 0);
                                Index = i;
                            }
                        }

                        Tb[0] = -1;
                        {
                            int[] bl = new int[] { 9 };  // must be <= 9 for lookahead assumptions
                            int[] bd = new int[] { 6 }; // must be <= 9 for lookahead assumptions
                            int[] tl = new int[1];
                            int[] td = new int[1];

                            t = Table;
                            t = Inftree.inflate_trees_dynamic(257 + (t & 0x1f), 1 + ((t >> 5) & 0x1f), Blens, bl, bd, tl, td, Hufts, Codec);

                            if (t != ZlibConstants.ZOk)
                            {
                                if (t == ZlibConstants.ZDataError)
                                {
                                    Blens = null;
                                    _mode = InflateBlockMode.Bad;
                                }
                                r = t;

                                Bitb = b; Bitk = k;
                                Codec.AvailableBytesIn = n;
                                Codec.TotalBytesIn += p - Codec.NextIn;
                                Codec.NextIn = p;
                                WriteAt = q;
                                return Flush(r);
                            }
                            Codes.Init(bl[0], bd[0], Hufts, tl[0], Hufts, td[0]);
                        }
                        _mode = InflateBlockMode.Codes;
                        goto case InflateBlockMode.Codes;

                    case InflateBlockMode.Codes:
                        Bitb = b; Bitk = k;
                        Codec.AvailableBytesIn = n;
                        Codec.TotalBytesIn += p - Codec.NextIn;
                        Codec.NextIn = p;
                        WriteAt = q;

                        r = Codes.Process(this, r);
                        if (r != ZlibConstants.ZStreamEnd)
                        {
                            return Flush(r);
                        }

                        r = ZlibConstants.ZOk;
                        p = Codec.NextIn;
                        n = Codec.AvailableBytesIn;
                        b = Bitb;
                        k = Bitk;
                        q = WriteAt;
                        m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);

                        if (Last == 0)
                        {
                            _mode = InflateBlockMode.Type;
                            break;
                        }
                        _mode = InflateBlockMode.Dry;
                        goto case InflateBlockMode.Dry;

                    case InflateBlockMode.Dry:
                        WriteAt = q;
                        r = Flush(r);
                        q = WriteAt; m = (int)(q < ReadAt ? ReadAt - q - 1 : End - q);
                        if (ReadAt != WriteAt)
                        {
                            Bitb = b; Bitk = k;
                            Codec.AvailableBytesIn = n;
                            Codec.TotalBytesIn += p - Codec.NextIn;
                            Codec.NextIn = p;
                            WriteAt = q;
                            return Flush(r);
                        }
                        _mode = InflateBlockMode.Done;
                        goto case InflateBlockMode.Done;

                    case InflateBlockMode.Done:
                        r = ZlibConstants.ZStreamEnd;
                        Bitb = b;
                        Bitk = k;
                        Codec.AvailableBytesIn = n;
                        Codec.TotalBytesIn += p - Codec.NextIn;
                        Codec.NextIn = p;
                        WriteAt = q;
                        return Flush(r);

                    case InflateBlockMode.Bad:
                        r = ZlibConstants.ZDataError;

                        Bitb = b; Bitk = k;
                        Codec.AvailableBytesIn = n;
                        Codec.TotalBytesIn += p - Codec.NextIn;
                        Codec.NextIn = p;
                        WriteAt = q;
                        return Flush(r);


                    default:
                        r = ZlibConstants.ZStreamError;

                        Bitb = b; Bitk = k;
                        Codec.AvailableBytesIn = n;
                        Codec.TotalBytesIn += p - Codec.NextIn;
                        Codec.NextIn = p;
                        WriteAt = q;
                        return Flush(r);
                }
            }
        }


        internal void Free()
        {
            Reset();
            Window = null;
            Hufts = null;
        }

        internal void SetDictionary(byte[] d, int start, int n)
        {
            Array.Copy(d, start, Window, 0, n);
            ReadAt = WriteAt = n;
        }

        // Returns true if inflate is currently at the end of a block generated
        // by Z_SYNC_FLUSH or Z_FULL_FLUSH.
        internal int SyncPoint()
        {
            return _mode == InflateBlockMode.Lens ? 1 : 0;
        }

        // copy as much as possible from the sliding window to the output area
        internal int Flush(int r)
        {
            int nBytes;

            for (int pass=0; pass < 2; pass++)
            {
                if (pass==0)
                {
                    // compute number of bytes to copy as far as end of window
                    nBytes = (int)((ReadAt <= WriteAt ? WriteAt : End) - ReadAt);
                }
                else
                {
                    // compute bytes to copy
                    nBytes = WriteAt - ReadAt;
                }

                // workitem 8870
                if (nBytes == 0)
                {
                    if (r == ZlibConstants.ZBufError)
                        r = ZlibConstants.ZOk;
                    return r;
                }

                if (nBytes > Codec.AvailableBytesOut)
                    nBytes = Codec.AvailableBytesOut;

                if (nBytes != 0 && r == ZlibConstants.ZBufError)
                    r = ZlibConstants.ZOk;

                // update counters
                Codec.AvailableBytesOut -= nBytes;
                Codec.TotalBytesOut += nBytes;

                // update check information
                if (Checkfn != null)
                    Codec._Adler32 = Check = Adler.Adler32(Check, Window, ReadAt, nBytes);

                // copy as far as end of window
                Array.Copy(Window, ReadAt, Codec.OutputBuffer, Codec.NextOut, nBytes);
                Codec.NextOut += nBytes;
                ReadAt += nBytes;

                // see if more to copy at beginning of window
                if (ReadAt == End && pass == 0)
                {
                    // wrap pointers
                    ReadAt = 0;
                    if (WriteAt == End)
                        WriteAt = 0;
                }
                else pass++;
            }

            // done
            return r;
        }
    }


    internal static class InternalInflateConstants
    {
        // And'ing with mask[n] masks the lower n bits
        internal static readonly int[] InflateMask = new int[] {
            0x00000000, 0x00000001, 0x00000003, 0x00000007,
            0x0000000f, 0x0000001f, 0x0000003f, 0x0000007f,
            0x000000ff, 0x000001ff, 0x000003ff, 0x000007ff,
            0x00000fff, 0x00001fff, 0x00003fff, 0x00007fff, 0x0000ffff };
    }


    sealed class InflateCodes
    {
        // waiting for "i:"=input,
        //             "o:"=output,
        //             "x:"=nothing
        private const int Start   = 0; // x: set up for LEN
        private const int Len     = 1; // i: get length/literal/eob next
        private const int Lenext  = 2; // i: getting length extra (have base)
        private const int Dist    = 3; // i: get distance next
        private const int Distext = 4; // i: getting distance extra
        private const int Copy    = 5; // o: copying bytes in window, waiting for space
        private const int Lit     = 6; // o: got literal, waiting for output space
        private const int Wash    = 7; // o: got eob, possibly still output waiting
        private const int End     = 8; // x: got eob and all data flushed
        private const int Badcode = 9; // x: got error

        internal int Mode;        // current inflate_codes mode

        // mode dependent information
        internal int len;

        internal int[] Tree;      // pointer into tree
        internal int TreeIndex = 0;
        internal int Need;        // bits needed

        internal int lit;

        // if EXT or COPY, where and how much
        internal int BitsToGet;   // bits to get for extra
        internal int dist;        // distance back to copy from

        internal byte Lbits;      // ltree bits decoded per branch
        internal byte Dbits;      // dtree bits decoder per branch
        internal int[] Ltree;     // literal/length/eob tree
        internal int LtreeIndex; // literal/length/eob tree
        internal int[] Dtree;     // distance tree
        internal int DtreeIndex; // distance tree

        internal InflateCodes()
        {
        }

        internal void Init(int bl, int bd, int[] tl, int tlIndex, int[] td, int tdIndex)
        {
            Mode = Start;
            Lbits = (byte)bl;
            Dbits = (byte)bd;
            Ltree = tl;
            LtreeIndex = tlIndex;
            Dtree = td;
            DtreeIndex = tdIndex;
            Tree = null;
        }

        internal int Process(InflateBlocks blocks, int r)
        {
            int j;      // temporary storage
            int tindex; // temporary pointer
            int e;      // extra bits or operation
            int b = 0;  // bit buffer
            int k = 0;  // bits in bit buffer
            int p = 0;  // input data pointer
            int n;      // bytes available there
            int q;      // output window write pointer
            int m;      // bytes to end of window or read pointer
            int f;      // pointer to copy strings from

            ZlibCodec z = blocks.Codec;

            // copy input/output information to locals (UPDATE macro restores)
            p = z.NextIn;
            n = z.AvailableBytesIn;
            b = blocks.Bitb;
            k = blocks.Bitk;
            q = blocks.WriteAt; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;

            // process input and output based on current state
            while (true)
            {
                switch (Mode)
                {
                    // waiting for "i:"=input, "o:"=output, "x:"=nothing
                    case Start:  // x: set up for LEN
                        if (m >= 258 && n >= 10)
                        {
                            blocks.Bitb = b; blocks.Bitk = k;
                            z.AvailableBytesIn = n;
                            z.TotalBytesIn += p - z.NextIn;
                            z.NextIn = p;
                            blocks.WriteAt = q;
                            r = InflateFast(Lbits, Dbits, Ltree, LtreeIndex, Dtree, DtreeIndex, blocks, z);

                            p = z.NextIn;
                            n = z.AvailableBytesIn;
                            b = blocks.Bitb;
                            k = blocks.Bitk;
                            q = blocks.WriteAt; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;

                            if (r != ZlibConstants.ZOk)
                            {
                                Mode = (r == ZlibConstants.ZStreamEnd) ? Wash : Badcode;
                                break;
                            }
                        }
                        Need = Lbits;
                        Tree = Ltree;
                        TreeIndex = LtreeIndex;

                        Mode = Len;
                        goto case Len;

                    case Len:  // i: get length/literal/eob next
                        j = Need;

                        while (k < j)
                        {
                            if (n != 0)
                                r = ZlibConstants.ZOk;
                            else
                            {
                                blocks.Bitb = b; blocks.Bitk = k;
                                z.AvailableBytesIn = n;
                                z.TotalBytesIn += p - z.NextIn;
                                z.NextIn = p;
                                blocks.WriteAt = q;
                                return blocks.Flush(r);
                            }
                            n--;
                            b |= (z.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        tindex = (TreeIndex + (b & InternalInflateConstants.InflateMask[j])) * 3;

                        b >>= (Tree[tindex + 1]);
                        k -= (Tree[tindex + 1]);

                        e = Tree[tindex];

                        if (e == 0)
                        {
                            // literal
                            lit = Tree[tindex + 2];
                            Mode = Lit;
                            break;
                        }
                        if ((e & 16) != 0)
                        {
                            // length
                            BitsToGet = e & 15;
                            len = Tree[tindex + 2];
                            Mode = Lenext;
                            break;
                        }
                        if ((e & 64) == 0)
                        {
                            // next table
                            Need = e;
                            TreeIndex = tindex / 3 + Tree[tindex + 2];
                            break;
                        }
                        if ((e & 32) != 0)
                        {
                            // end of block
                            Mode = Wash;
                            break;
                        }
                        Mode = Badcode; // invalid code
                        z.Message = "invalid literal/length code";
                        r = ZlibConstants.ZDataError;

                        blocks.Bitb = b; blocks.Bitk = k;
                        z.AvailableBytesIn = n;
                        z.TotalBytesIn += p - z.NextIn;
                        z.NextIn = p;
                        blocks.WriteAt = q;
                        return blocks.Flush(r);


                    case Lenext:  // i: getting length extra (have base)
                        j = BitsToGet;

                        while (k < j)
                        {
                            if (n != 0)
                                r = ZlibConstants.ZOk;
                            else
                            {
                                blocks.Bitb = b; blocks.Bitk = k;
                                z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                                blocks.WriteAt = q;
                                return blocks.Flush(r);
                            }
                            n--; b |= (z.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        len += (b & InternalInflateConstants.InflateMask[j]);

                        b >>= j;
                        k -= j;

                        Need = Dbits;
                        Tree = Dtree;
                        TreeIndex = DtreeIndex;
                        Mode = Dist;
                        goto case Dist;

                    case Dist:  // i: get distance next
                        j = Need;

                        while (k < j)
                        {
                            if (n != 0)
                                r = ZlibConstants.ZOk;
                            else
                            {
                                blocks.Bitb = b; blocks.Bitk = k;
                                z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                                blocks.WriteAt = q;
                                return blocks.Flush(r);
                            }
                            n--; b |= (z.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        tindex = (TreeIndex + (b & InternalInflateConstants.InflateMask[j])) * 3;

                        b >>= Tree[tindex + 1];
                        k -= Tree[tindex + 1];

                        e = (Tree[tindex]);
                        if ((e & 0x10) != 0)
                        {
                            // distance
                            BitsToGet = e & 15;
                            dist = Tree[tindex + 2];
                            Mode = Distext;
                            break;
                        }
                        if ((e & 64) == 0)
                        {
                            // next table
                            Need = e;
                            TreeIndex = tindex / 3 + Tree[tindex + 2];
                            break;
                        }
                        Mode = Badcode; // invalid code
                        z.Message = "invalid distance code";
                        r = ZlibConstants.ZDataError;

                        blocks.Bitb = b; blocks.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        blocks.WriteAt = q;
                        return blocks.Flush(r);


                    case Distext:  // i: getting distance extra
                        j = BitsToGet;

                        while (k < j)
                        {
                            if (n != 0)
                                r = ZlibConstants.ZOk;
                            else
                            {
                                blocks.Bitb = b; blocks.Bitk = k;
                                z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                                blocks.WriteAt = q;
                                return blocks.Flush(r);
                            }
                            n--; b |= (z.InputBuffer[p++] & 0xff) << k;
                            k += 8;
                        }

                        dist += (b & InternalInflateConstants.InflateMask[j]);

                        b >>= j;
                        k -= j;

                        Mode = Copy;
                        goto case Copy;

                    case Copy:  // o: copying bytes in window, waiting for space
                        f = q - dist;
                        while (f < 0)
                        {
                            // modulo window size-"while" instead
                            f += blocks.End; // of "if" handles invalid distances
                        }
                        while (len != 0)
                        {
                            if (m == 0)
                            {
                                if (q == blocks.End && blocks.ReadAt != 0)
                                {
                                    q = 0; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;
                                }
                                if (m == 0)
                                {
                                    blocks.WriteAt = q; r = blocks.Flush(r);
                                    q = blocks.WriteAt; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;

                                    if (q == blocks.End && blocks.ReadAt != 0)
                                    {
                                        q = 0; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;
                                    }

                                    if (m == 0)
                                    {
                                        blocks.Bitb = b; blocks.Bitk = k;
                                        z.AvailableBytesIn = n;
                                        z.TotalBytesIn += p - z.NextIn;
                                        z.NextIn = p;
                                        blocks.WriteAt = q;
                                        return blocks.Flush(r);
                                    }
                                }
                            }

                            blocks.Window[q++] = blocks.Window[f++]; m--;

                            if (f == blocks.End)
                                f = 0;
                            len--;
                        }
                        Mode = Start;
                        break;

                    case Lit:  // o: got literal, waiting for output space
                        if (m == 0)
                        {
                            if (q == blocks.End && blocks.ReadAt != 0)
                            {
                                q = 0; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;
                            }
                            if (m == 0)
                            {
                                blocks.WriteAt = q; r = blocks.Flush(r);
                                q = blocks.WriteAt; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;

                                if (q == blocks.End && blocks.ReadAt != 0)
                                {
                                    q = 0; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;
                                }
                                if (m == 0)
                                {
                                    blocks.Bitb = b; blocks.Bitk = k;
                                    z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                                    blocks.WriteAt = q;
                                    return blocks.Flush(r);
                                }
                            }
                        }
                        r = ZlibConstants.ZOk;

                        blocks.Window[q++] = (byte)lit; m--;

                        Mode = Start;
                        break;

                    case Wash:  // o: got eob, possibly more output
                        if (k > 7)
                        {
                            // return unused byte, if any
                            k -= 8;
                            n++;
                            p--; // can always return one
                        }

                        blocks.WriteAt = q; r = blocks.Flush(r);
                        q = blocks.WriteAt; m = q < blocks.ReadAt ? blocks.ReadAt - q - 1 : blocks.End - q;

                        if (blocks.ReadAt != blocks.WriteAt)
                        {
                            blocks.Bitb = b; blocks.Bitk = k;
                            z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                            blocks.WriteAt = q;
                            return blocks.Flush(r);
                        }
                        Mode = End;
                        goto case End;

                    case End:
                        r = ZlibConstants.ZStreamEnd;
                        blocks.Bitb = b; blocks.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        blocks.WriteAt = q;
                        return blocks.Flush(r);

                    case Badcode:  // x: got error

                        r = ZlibConstants.ZDataError;

                        blocks.Bitb = b; blocks.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        blocks.WriteAt = q;
                        return blocks.Flush(r);

                    default:
                        r = ZlibConstants.ZStreamError;

                        blocks.Bitb = b; blocks.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        blocks.WriteAt = q;
                        return blocks.Flush(r);
                }
            }
        }


        // Called with number of bytes left to write in window at least 258
        // (the maximum string length) and number of input bytes available
        // at least ten.  The ten bytes are six bytes for the longest length/
        // distance pair plus four bytes for overloading the bit buffer.

        internal int InflateFast(int bl, int bd, int[] tl, int tlIndex, int[] td, int tdIndex, InflateBlocks s, ZlibCodec z)
        {
            int t;        // temporary pointer
            int[] tp;     // temporary pointer
            int tpIndex; // temporary pointer
            int e;        // extra bits or operation
            int b;        // bit buffer
            int k;        // bits in bit buffer
            int p;        // input data pointer
            int n;        // bytes available there
            int q;        // output window write pointer
            int m;        // bytes to end of window or read pointer
            int ml;       // mask for literal/length tree
            int md;       // mask for distance tree
            int c;        // bytes to copy
            int d;        // distance back to copy from
            int r;        // copy source pointer

            int tpIndexT3; // (tp_index+t)*3

            // load input, output, bit values
            p = z.NextIn; n = z.AvailableBytesIn; b = s.Bitb; k = s.Bitk;
            q = s.WriteAt; m = q < s.ReadAt ? s.ReadAt - q - 1 : s.End - q;

            // initialize masks
            ml = InternalInflateConstants.InflateMask[bl];
            md = InternalInflateConstants.InflateMask[bd];

            // do until not enough input or output space for fast loop
            do
            {
                // assume called with m >= 258 && n >= 10
                // get literal/length code
                while (k < (20))
                {
                    // max bits for literal/length code
                    n--;
                    b |= (z.InputBuffer[p++] & 0xff) << k; k += 8;
                }

                t = b & ml;
                tp = tl;
                tpIndex = tlIndex;
                tpIndexT3 = (tpIndex + t) * 3;
                if ((e = tp[tpIndexT3]) == 0)
                {
                    b >>= (tp[tpIndexT3 + 1]); k -= (tp[tpIndexT3 + 1]);

                    s.Window[q++] = (byte)tp[tpIndexT3 + 2];
                    m--;
                    continue;
                }
                do
                {

                    b >>= (tp[tpIndexT3 + 1]); k -= (tp[tpIndexT3 + 1]);

                    if ((e & 16) != 0)
                    {
                        e &= 15;
                        c = tp[tpIndexT3 + 2] + ((int)b & InternalInflateConstants.InflateMask[e]);

                        b >>= e; k -= e;

                        // decode distance base of block to copy
                        while (k < 15)
                        {
                            // max bits for distance code
                            n--;
                            b |= (z.InputBuffer[p++] & 0xff) << k; k += 8;
                        }

                        t = b & md;
                        tp = td;
                        tpIndex = tdIndex;
                        tpIndexT3 = (tpIndex + t) * 3;
                        e = tp[tpIndexT3];

                        do
                        {

                            b >>= (tp[tpIndexT3 + 1]); k -= (tp[tpIndexT3 + 1]);

                            if ((e & 16) != 0)
                            {
                                // get extra bits to add to distance base
                                e &= 15;
                                while (k < e)
                                {
                                    // get extra bits (up to 13)
                                    n--;
                                    b |= (z.InputBuffer[p++] & 0xff) << k; k += 8;
                                }

                                d = tp[tpIndexT3 + 2] + (b & InternalInflateConstants.InflateMask[e]);

                                b >>= e; k -= e;

                                // do the copy
                                m -= c;
                                if (q >= d)
                                {
                                    // offset before dest
                                    //  just copy
                                    r = q - d;
                                    if (q - r > 0 && 2 > (q - r))
                                    {
                                        s.Window[q++] = s.Window[r++]; // minimum count is three,
                                        s.Window[q++] = s.Window[r++]; // so unroll loop a little
                                        c -= 2;
                                    }
                                    else
                                    {
                                        Array.Copy(s.Window, r, s.Window, q, 2);
                                        q += 2; r += 2; c -= 2;
                                    }
                                }
                                else
                                {
                                    // else offset after destination
                                    r = q - d;
                                    do
                                    {
                                        r += s.End; // force pointer in window
                                    }
                                    while (r < 0); // covers invalid distances
                                    e = s.End - r;
                                    if (c > e)
                                    {
                                        // if source crosses,
                                        c -= e; // wrapped copy
                                        if (q - r > 0 && e > (q - r))
                                        {
                                            do
                                            {
                                                s.Window[q++] = s.Window[r++];
                                            }
                                            while (--e != 0);
                                        }
                                        else
                                        {
                                            Array.Copy(s.Window, r, s.Window, q, e);
                                            q += e; r += e; e = 0;
                                        }
                                        r = 0; // copy rest from start of window
                                    }
                                }

                                // copy all or what's left
                                if (q - r > 0 && c > (q - r))
                                {
                                    do
                                    {
                                        s.Window[q++] = s.Window[r++];
                                    }
                                    while (--c != 0);
                                }
                                else
                                {
                                    Array.Copy(s.Window, r, s.Window, q, c);
                                    q += c; r += c; c = 0;
                                }
                                break;
                            }
                            else if ((e & 64) == 0)
                            {
                                t += tp[tpIndexT3 + 2];
                                t += (b & InternalInflateConstants.InflateMask[e]);
                                tpIndexT3 = (tpIndex + t) * 3;
                                e = tp[tpIndexT3];
                            }
                            else
                            {
                                z.Message = "invalid distance code";

                                c = z.AvailableBytesIn - n; c = (k >> 3) < c ? k >> 3 : c; n += c; p -= c; k -= (c << 3);

                                s.Bitb = b; s.Bitk = k;
                                z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                                s.WriteAt = q;

                                return ZlibConstants.ZDataError;
                            }
                        }
                        while (true);
                        break;
                    }

                    if ((e & 64) == 0)
                    {
                        t += tp[tpIndexT3 + 2];
                        t += (b & InternalInflateConstants.InflateMask[e]);
                        tpIndexT3 = (tpIndex + t) * 3;
                        if ((e = tp[tpIndexT3]) == 0)
                        {
                            b >>= (tp[tpIndexT3 + 1]); k -= (tp[tpIndexT3 + 1]);
                            s.Window[q++] = (byte)tp[tpIndexT3 + 2];
                            m--;
                            break;
                        }
                    }
                    else if ((e & 32) != 0)
                    {
                        c = z.AvailableBytesIn - n; c = (k >> 3) < c ? k >> 3 : c; n += c; p -= c; k -= (c << 3);

                        s.Bitb = b; s.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        s.WriteAt = q;

                        return ZlibConstants.ZStreamEnd;
                    }
                    else
                    {
                        z.Message = "invalid literal/length code";

                        c = z.AvailableBytesIn - n; c = (k >> 3) < c ? k >> 3 : c; n += c; p -= c; k -= (c << 3);

                        s.Bitb = b; s.Bitk = k;
                        z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
                        s.WriteAt = q;

                        return ZlibConstants.ZDataError;
                    }
                }
                while (true);
            }
            while (m >= 258 && n >= 10);

            // not enough input or output--restore pointers and return
            c = z.AvailableBytesIn - n; c = (k >> 3) < c ? k >> 3 : c; n += c; p -= c; k -= (c << 3);

            s.Bitb = b; s.Bitk = k;
            z.AvailableBytesIn = n; z.TotalBytesIn += p - z.NextIn; z.NextIn = p;
            s.WriteAt = q;

            return ZlibConstants.ZOk;
        }
    }


    internal sealed class InflateManager
    {
        // preset dictionary flag in zlib header
        private const int PresetDict = 0x20;

        private const int ZDeflated = 8;

        private enum InflateManagerMode
        {
            Method = 0,  // waiting for method byte
            Flag   = 1,  // waiting for flag byte
            Dict4  = 2,  // four dictionary check bytes to go
            Dict3  = 3,  // three dictionary check bytes to go
            Dict2  = 4,  // two dictionary check bytes to go
            Dict1  = 5,  // one dictionary check byte to go
            Dict0  = 6,  // waiting for inflateSetDictionary
            Blocks = 7,  // decompressing blocks
            Check4 = 8,  // four check bytes to go
            Check3 = 9,  // three check bytes to go
            Check2 = 10, // two check bytes to go
            Check1 = 11, // one check byte to go
            Done   = 12, // finished check, done
            Bad    = 13, // got an error--stay here
        }

        private InflateManagerMode _mode; // current inflate mode
        internal ZlibCodec Codec; // pointer back to this zlib stream

        // mode dependent information
        internal int Method; // if FLAGS, method byte

        // if CHECK, check values to compare
        internal uint ComputedCheck; // computed check value
        internal uint ExpectedCheck; // stream check value

        // if BAD, inflateSync's marker bytes count
        internal int Marker;

        // mode independent information
        //internal int nowrap; // flag for no wrapper
        private bool _handleRfc1950HeaderBytes = true;
        internal bool HandleRfc1950HeaderBytes
        {
            get { return _handleRfc1950HeaderBytes; }
            set { _handleRfc1950HeaderBytes = value; }
        }
        internal int Wbits; // log2(window size)  (8..15, defaults to 15)

        internal InflateBlocks Blocks; // current inflate_blocks state

        public InflateManager() { }

        public InflateManager(bool expectRfc1950HeaderBytes)
        {
            _handleRfc1950HeaderBytes = expectRfc1950HeaderBytes;
        }

        internal int Reset()
        {
            Codec.TotalBytesIn = Codec.TotalBytesOut = 0;
            Codec.Message = null;
            _mode = HandleRfc1950HeaderBytes ? InflateManagerMode.Method : InflateManagerMode.Blocks;
            Blocks.Reset();
            return ZlibConstants.ZOk;
        }

        internal int End()
        {
            if (Blocks != null)
                Blocks.Free();
            Blocks = null;
            return ZlibConstants.ZOk;
        }

        internal int Initialize(ZlibCodec codec, int w)
        {
            Codec = codec;
            Codec.Message = null;
            Blocks = null;

            // handle undocumented nowrap option (no zlib header or check)
            //nowrap = 0;
            //if (w < 0)
            //{
            //    w = - w;
            //    nowrap = 1;
            //}

            // set window size
            if (w < 8 || w > 15)
            {
                End();
                throw new ZlibException("Bad window size.");

                //return ZlibConstants.Z_STREAM_ERROR;
            }
            Wbits = w;

            Blocks = new InflateBlocks(codec,
                HandleRfc1950HeaderBytes ? this : null,
                1 << w);

            // reset state
            Reset();
            return ZlibConstants.ZOk;
        }


        internal int Inflate(FlushType flush)
        {
            int b;

            if (Codec.InputBuffer == null)
                throw new ZlibException("InputBuffer is null. ");

//             int f = (flush == FlushType.Finish)
//                 ? ZlibConstants.Z_BUF_ERROR
//                 : ZlibConstants.Z_OK;

            // workitem 8870
            int f = ZlibConstants.ZOk;
            int r = ZlibConstants.ZBufError;

            while (true)
            {
                switch (_mode)
                {
                    case InflateManagerMode.Method:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        if (((Method = Codec.InputBuffer[Codec.NextIn++]) & 0xf) != ZDeflated)
                        {
                            _mode = InflateManagerMode.Bad;
                            Codec.Message = String.Format("unknown compression method (0x{0:X2})", Method);
                            Marker = 5; // can't try inflateSync
                            break;
                        }
                        if ((Method >> 4) + 8 > Wbits)
                        {
                            _mode = InflateManagerMode.Bad;
                            Codec.Message = String.Format("invalid window size ({0})", (Method >> 4) + 8);
                            Marker = 5; // can't try inflateSync
                            break;
                        }
                        _mode = InflateManagerMode.Flag;
                        break;


                    case InflateManagerMode.Flag:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        b = (Codec.InputBuffer[Codec.NextIn++]) & 0xff;

                        if ((((Method << 8) + b) % 31) != 0)
                        {
                            _mode = InflateManagerMode.Bad;
                            Codec.Message = "incorrect header check";
                            Marker = 5; // can't try inflateSync
                            break;
                        }

                        _mode = ((b & PresetDict) == 0)
                            ? InflateManagerMode.Blocks
                            : InflateManagerMode.Dict4;
                        break;

                    case InflateManagerMode.Dict4:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        ExpectedCheck = (uint)((Codec.InputBuffer[Codec.NextIn++] << 24) & 0xff000000);
                        _mode = InflateManagerMode.Dict3;
                        break;

                    case InflateManagerMode.Dict3:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)((Codec.InputBuffer[Codec.NextIn++] << 16) & 0x00ff0000);
                        _mode = InflateManagerMode.Dict2;
                        break;

                    case InflateManagerMode.Dict2:

                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)((Codec.InputBuffer[Codec.NextIn++] << 8) & 0x0000ff00);
                        _mode = InflateManagerMode.Dict1;
                        break;


                    case InflateManagerMode.Dict1:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--; Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)(Codec.InputBuffer[Codec.NextIn++] & 0x000000ff);
                        Codec._Adler32 = ExpectedCheck;
                        _mode = InflateManagerMode.Dict0;
                        return ZlibConstants.ZNeedDict;


                    case InflateManagerMode.Dict0:
                        _mode = InflateManagerMode.Bad;
                        Codec.Message = "need dictionary";
                        Marker = 0; // can try inflateSync
                        return ZlibConstants.ZStreamError;


                    case InflateManagerMode.Blocks:
                        r = Blocks.Process(r);
                        if (r == ZlibConstants.ZDataError)
                        {
                            _mode = InflateManagerMode.Bad;
                            Marker = 0; // can try inflateSync
                            break;
                        }

                        if (r == ZlibConstants.ZOk) r = f;

                        if (r != ZlibConstants.ZStreamEnd)
                            return r;

                        r = f;
                        ComputedCheck = Blocks.Reset();
                        if (!HandleRfc1950HeaderBytes)
                        {
                            _mode = InflateManagerMode.Done;
                            return ZlibConstants.ZStreamEnd;
                        }
                        _mode = InflateManagerMode.Check4;
                        break;

                    case InflateManagerMode.Check4:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        ExpectedCheck = (uint)((Codec.InputBuffer[Codec.NextIn++] << 24) & 0xff000000);
                        _mode = InflateManagerMode.Check3;
                        break;

                    case InflateManagerMode.Check3:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--; Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)((Codec.InputBuffer[Codec.NextIn++] << 16) & 0x00ff0000);
                        _mode = InflateManagerMode.Check2;
                        break;

                    case InflateManagerMode.Check2:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--;
                        Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)((Codec.InputBuffer[Codec.NextIn++] << 8) & 0x0000ff00);
                        _mode = InflateManagerMode.Check1;
                        break;

                    case InflateManagerMode.Check1:
                        if (Codec.AvailableBytesIn == 0) return r;
                        r = f;
                        Codec.AvailableBytesIn--; Codec.TotalBytesIn++;
                        ExpectedCheck += (uint)(Codec.InputBuffer[Codec.NextIn++] & 0x000000ff);
                        if (ComputedCheck != ExpectedCheck)
                        {
                            _mode = InflateManagerMode.Bad;
                            Codec.Message = "incorrect data check";
                            Marker = 5; // can't try inflateSync
                            break;
                        }
                        _mode = InflateManagerMode.Done;
                        return ZlibConstants.ZStreamEnd;

                    case InflateManagerMode.Done:
                        return ZlibConstants.ZStreamEnd;

                    case InflateManagerMode.Bad:
                        throw new ZlibException(String.Format("Bad state ({0})", Codec.Message));

                    default:
                        throw new ZlibException("Stream error.");

                }
            }
        }



        internal int SetDictionary(byte[] dictionary)
        {
            int index = 0;
            int length = dictionary.Length;
            if (_mode != InflateManagerMode.Dict0)
                throw new ZlibException("Stream error.");

            if (Adler.Adler32(1, dictionary, 0, dictionary.Length) != Codec._Adler32)
            {
                return ZlibConstants.ZDataError;
            }

            Codec._Adler32 = Adler.Adler32(0, null, 0, 0);

            if (length >= (1 << Wbits))
            {
                length = (1 << Wbits) - 1;
                index = dictionary.Length - length;
            }
            Blocks.SetDictionary(dictionary, index, length);
            _mode = InflateManagerMode.Blocks;
            return ZlibConstants.ZOk;
        }


        private static readonly byte[] Mark = new byte[] { 0, 0, 0xff, 0xff };

        internal int Sync()
        {
            int n; // number of bytes to look at
            int p; // pointer to bytes
            int m; // number of marker bytes found in a row
            long r, w; // temporaries to save total_in and total_out

            // set up
            if (_mode != InflateManagerMode.Bad)
            {
                _mode = InflateManagerMode.Bad;
                Marker = 0;
            }
            if ((n = Codec.AvailableBytesIn) == 0)
                return ZlibConstants.ZBufError;
            p = Codec.NextIn;
            m = Marker;

            // search
            while (n != 0 && m < 4)
            {
                if (Codec.InputBuffer[p] == Mark[m])
                {
                    m++;
                }
                else if (Codec.InputBuffer[p] != 0)
                {
                    m = 0;
                }
                else
                {
                    m = 4 - m;
                }
                p++; n--;
            }

            // restore
            Codec.TotalBytesIn += p - Codec.NextIn;
            Codec.NextIn = p;
            Codec.AvailableBytesIn = n;
            Marker = m;

            // return no joy or set up to restart on a new block
            if (m != 4)
            {
                return ZlibConstants.ZDataError;
            }
            r = Codec.TotalBytesIn;
            w = Codec.TotalBytesOut;
            Reset();
            Codec.TotalBytesIn = r;
            Codec.TotalBytesOut = w;
            _mode = InflateManagerMode.Blocks;
            return ZlibConstants.ZOk;
        }


        // Returns true if inflate is currently at the end of a block generated
        // by Z_SYNC_FLUSH or Z_FULL_FLUSH. This function is used by one PPP
        // implementation to provide an additional safety check. PPP uses Z_SYNC_FLUSH
        // but removes the length bytes of the resulting empty stored block. When
        // decompressing, PPP checks that at the end of input packet, inflate is
        // waiting for these length bytes.
        internal int SyncPoint(ZlibCodec z)
        {
            return Blocks.SyncPoint();
        }
    }
}