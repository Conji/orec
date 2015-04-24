// Deflate.cs
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
// Time-stamp: <2011-August-03 19:52:15>
//
// ------------------------------------------------------------------
//
// This module defines logic for handling the Deflate or compression.
//
// This code is based on multiple sources:
// - the original zlib v1.2.3 source, which is Copyright (C) 1995-2005 Jean-loup Gailly.
// - the original jzlib, which is Copyright (c) 2000-2003 ymnk, JCraft,Inc.
//
// However, this code is significantly different from both.
// The object model is not the same, and many of the behaviors are different.
//
// In keeping with the license for these other works, the copyrights for
// jzlib and zlib are here.
//
// -----------------------------------------------------------------------
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

    internal enum BlockState
    {
        NeedMore = 0,       // block not completed, need more input or more output
        BlockDone,          // block flush performed
        FinishStarted,              // finish started, need only more output at next deflate
        FinishDone          // finish done, accept no more input or output
    }

    internal enum DeflateFlavor
    {
        Store,
        Fast,
        Slow
    }

    internal sealed class DeflateManager
    {
        private static readonly int MemLevelMax = 9;
        private static readonly int MemLevelDefault = 8;

        internal delegate BlockState CompressFunc(FlushType flush);

        internal class Config
        {
            // Use a faster search when the previous match is longer than this
            internal int GoodLength; // reduce lazy search above this match length

            // Attempt to find a better match only when the current match is
            // strictly smaller than this value. This mechanism is used only for
            // compression levels >= 4.  For levels 1,2,3: MaxLazy is actually
            // MaxInsertLength. (See DeflateFast)

            internal int MaxLazy;    // do not perform lazy search above this match length

            internal int NiceLength; // quit search above this match length

            // To speed up deflation, hash chains are never searched beyond this
            // length.  A higher limit improves compression ratio but degrades the speed.

            internal int MaxChainLength;

            internal DeflateFlavor Flavor;

            private Config(int goodLength, int maxLazy, int niceLength, int maxChainLength, DeflateFlavor flavor)
            {
                this.GoodLength = goodLength;
                this.MaxLazy = maxLazy;
                this.NiceLength = niceLength;
                this.MaxChainLength = maxChainLength;
                this.Flavor = flavor;
            }

            public static Config Lookup(CompressionLevel level)
            {
                return Table[(int)level];
            }


            static Config()
            {
                Table = new Config[] {
                    new Config(0, 0, 0, 0, DeflateFlavor.Store),
                    new Config(4, 4, 8, 4, DeflateFlavor.Fast),
                    new Config(4, 5, 16, 8, DeflateFlavor.Fast),
                    new Config(4, 6, 32, 32, DeflateFlavor.Fast),

                    new Config(4, 4, 16, 16, DeflateFlavor.Slow),
                    new Config(8, 16, 32, 32, DeflateFlavor.Slow),
                    new Config(8, 16, 128, 128, DeflateFlavor.Slow),
                    new Config(8, 32, 128, 256, DeflateFlavor.Slow),
                    new Config(32, 128, 258, 1024, DeflateFlavor.Slow),
                    new Config(32, 258, 258, 4096, DeflateFlavor.Slow),
                };
            }

            private static readonly Config[] Table;
        }


        private CompressFunc _deflateFunction;

        private static readonly String[] ErrorMessage = new String[]
        {
            "need dictionary",
            "stream end",
            "",
            "file error",
            "stream error",
            "data error",
            "insufficient memory",
            "buffer error",
            "incompatible version",
            ""
        };

        // preset dictionary flag in zlib header
        private static readonly int PresetDict = 0x20;

        private static readonly int InitState = 42;
        private static readonly int BusyState = 113;
        private static readonly int FinishState = 666;

        // The deflate compression method
        private static readonly int ZDeflated = 8;

        private static readonly int StoredBlock = 0;
        private static readonly int StaticTrees = 1;
        private static readonly int DynTrees = 2;

        // The three kinds of block type
        private static readonly int ZBinary = 0;
        private static readonly int ZAscii = 1;
        private static readonly int ZUnknown = 2;

        private static readonly int BufSize = 8 * 2;

        private static readonly int MinMatch = 3;
        private static readonly int MaxMatch = 258;

        private static readonly int MinLookahead = (MaxMatch + MinMatch + 1);

        private static readonly int HeapSize = (2 * InternalConstants.LCodes + 1);

        private static readonly int EndBlock = 256;

        internal ZlibCodec Codec; // the zlib encoder/decoder
        internal int Status;       // as the name implies
        internal byte[] Pending;   // output still pending - waiting to be compressed
        internal int NextPending;  // index of next pending byte to output to the stream
        internal int PendingCount; // number of bytes in the pending buffer

        internal sbyte DataType;  // UNKNOWN, BINARY or ASCII
        internal int LastFlush;   // value of flush param for previous deflate call

        internal int WSize;       // LZ77 window size (32K by default)
        internal int WBits;       // log2(w_size)  (8..16)
        internal int WMask;       // w_size - 1

        //internal byte[] dictionary;
        internal byte[] Window;

        // Sliding window. Input bytes are read into the second half of the window,
        // and move to the first half later to keep a dictionary of at least wSize
        // bytes. With this organization, matches are limited to a distance of
        // wSize-MAX_MATCH bytes, but this ensures that IO is always
        // performed with a length multiple of the block size.
        //
        // To do: use the user input buffer as sliding window.

        internal int WindowSize;
        // Actual size of window: 2*wSize, except when the user input buffer
        // is directly used as sliding window.

        internal short[] Prev;
        // Link to older string with same hash index. To limit the size of this
        // array to 64K, this link is maintained only for the last 32K strings.
        // An index in this array is thus a window index modulo 32K.

        internal short[] Head;  // Heads of the hash chains or NIL.

        internal int InsH;     // hash index of string to be inserted
        internal int HashSize; // number of elements in hash table
        internal int HashBits; // log2(hash_size)
        internal int HashMask; // hash_size-1

        // Number of bits by which ins_h must be shifted at each input
        // step. It must be such that after MIN_MATCH steps, the oldest
        // byte no longer takes part in the hash key, that is:
        // hash_shift * MIN_MATCH >= hash_bits
        internal int HashShift;

        // Window position at the beginning of the current output block. Gets
        // negative when the window is moved backwards.

        internal int BlockStart;

        Config _config;
        internal int MatchLength;    // length of best match
        internal int PrevMatch;      // previous match
        internal int MatchAvailable; // set if previous match exists
        internal int Strstart;        // start of string to insert into.....????
        internal int MatchStart;     // start of matching string
        internal int Lookahead;       // number of valid bytes ahead in window

        // Length of the best match at previous step. Matches not greater than this
        // are discarded. This is used in the lazy match evaluation.
        internal int PrevLength;

        // Insert new strings in the hash table only if the match length is not
        // greater than this length. This saves time but degrades compression.
        // max_insert_length is used only for compression levels <= 3.

        internal CompressionLevel CompressionLevel; // compression level (1..9)
        internal CompressionStrategy CompressionStrategy; // favor or force Huffman coding


        internal short[] DynLtree;         // literal and length tree
        internal short[] DynDtree;         // distance tree
        internal short[] BlTree;           // Huffman tree for bit lengths

        internal Tree TreeLiterals = new Tree();  // desc for literal tree
        internal Tree TreeDistances = new Tree();  // desc for distance tree
        internal Tree TreeBitLengths = new Tree(); // desc for bit length tree

        // number of codes at each bit length for an optimal tree
        internal short[] BlCount = new short[InternalConstants.MaxBits + 1];

        // heap used to build the Huffman trees
        internal int[] Heap = new int[2 * InternalConstants.LCodes + 1];

        internal int HeapLen;              // number of elements in the heap
        internal int HeapMax;              // element of largest frequency

        // The sons of heap[n] are heap[2*n] and heap[2*n+1]. heap[0] is not used.
        // The same heap array is used to build all trees.

        // Depth of each subtree used as tie breaker for trees of equal frequency
        internal sbyte[] Depth = new sbyte[2 * InternalConstants.LCodes + 1];

        internal int LengthOffset;                 // index for literals or lengths


        // Size of match buffer for literals/lengths.  There are 4 reasons for
        // limiting lit_bufsize to 64K:
        //   - frequencies can be kept in 16 bit counters
        //   - if compression is not successful for the first block, all input
        //     data is still in the window so we can still emit a stored block even
        //     when input comes from standard input.  (This can also be done for
        //     all blocks if lit_bufsize is not greater than 32K.)
        //   - if compression is not successful for a file smaller than 64K, we can
        //     even emit a stored file instead of a stored block (saving 5 bytes).
        //     This is applicable only for zip (not gzip or zlib).
        //   - creating new Huffman trees less frequently may not provide fast
        //     adaptation to changes in the input data statistics. (Take for
        //     example a binary file with poorly compressible code followed by
        //     a highly compressible string table.) Smaller buffer sizes give
        //     fast adaptation but have of course the overhead of transmitting
        //     trees more frequently.

        internal int LitBufsize;

        internal int LastLit;     // running index in l_buf

        // Buffer for distances. To simplify the code, d_buf and l_buf have
        // the same number of elements. To use different lengths, an extra flag
        // array would be necessary.

        internal int DistanceOffset;        // index into pending; points to distance data??

        internal int OptLen;      // bit length of current block with optimal trees
        internal int StaticLen;   // bit length of current block with static trees
        internal int Matches;      // number of string matches in current block
        internal int LastEobLen; // bit length of EOB code for last block

        // Output buffer. bits are inserted starting at the bottom (least
        // significant bits).
        internal short BiBuf;

        // Number of valid bits in bi_buf.  All bits above the last valid bit
        // are always zero.
        internal int BiValid;


        internal DeflateManager()
        {
            DynLtree = new short[HeapSize * 2];
            DynDtree = new short[(2 * InternalConstants.DCodes + 1) * 2]; // distance tree
            BlTree = new short[(2 * InternalConstants.BlCodes + 1) * 2]; // Huffman tree for bit lengths
        }


        // lm_init
        private void _InitializeLazyMatch()
        {
            WindowSize = 2 * WSize;

            // clear the hash - workitem 9063
            Array.Clear(Head, 0, HashSize);
            //for (int i = 0; i < hash_size; i++) head[i] = 0;

            _config = Config.Lookup(CompressionLevel);
            SetDeflater();

            Strstart = 0;
            BlockStart = 0;
            Lookahead = 0;
            MatchLength = PrevLength = MinMatch - 1;
            MatchAvailable = 0;
            InsH = 0;
        }

        // Initialize the tree data structures for a new zlib stream.
        private void _InitializeTreeData()
        {
            TreeLiterals.DynTree = DynLtree;
            TreeLiterals.StaticTree = StaticTree.Literals;

            TreeDistances.DynTree = DynDtree;
            TreeDistances.StaticTree = StaticTree.Distances;

            TreeBitLengths.DynTree = BlTree;
            TreeBitLengths.StaticTree = StaticTree.BitLengths;

            BiBuf = 0;
            BiValid = 0;
            LastEobLen = 8; // enough lookahead for inflate

            // Initialize the first block of the first file:
            _InitializeBlocks();
        }

        internal void _InitializeBlocks()
        {
            // Initialize the trees.
            for (int i = 0; i < InternalConstants.LCodes; i++)
                DynLtree[i * 2] = 0;
            for (int i = 0; i < InternalConstants.DCodes; i++)
                DynDtree[i * 2] = 0;
            for (int i = 0; i < InternalConstants.BlCodes; i++)
                BlTree[i * 2] = 0;

            DynLtree[EndBlock * 2] = 1;
            OptLen = StaticLen = 0;
            LastLit = Matches = 0;
        }

        // Restore the heap property by moving down the tree starting at node k,
        // exchanging a node with the smallest of its two sons if necessary, stopping
        // when the heap property is re-established (each father smaller than its
        // two sons).
        internal void Pqdownheap(short[] tree, int k)
        {
            int v = Heap[k];
            int j = k << 1; // left son of k
            while (j <= HeapLen)
            {
                // Set j to the smallest of the two sons:
                if (j < HeapLen && _IsSmaller(tree, Heap[j + 1], Heap[j], Depth))
                {
                    j++;
                }
                // Exit if v is smaller than both sons
                if (_IsSmaller(tree, v, Heap[j], Depth))
                    break;

                // Exchange v with the smallest son
                Heap[k] = Heap[j]; k = j;
                // And continue down the tree, setting j to the left son of k
                j <<= 1;
            }
            Heap[k] = v;
        }

        internal static bool _IsSmaller(short[] tree, int n, int m, sbyte[] depth)
        {
            short tn2 = tree[n * 2];
            short tm2 = tree[m * 2];
            return (tn2 < tm2 || (tn2 == tm2 && depth[n] <= depth[m]));
        }


        // Scan a literal or distance tree to determine the frequencies of the codes
        // in the bit length tree.
        internal void scan_tree(short[] tree, int maxCode)
        {
            int n; // iterates over all tree elements
            int prevlen = -1; // last emitted length
            int curlen; // length of current code
            int nextlen = (int)tree[0 * 2 + 1]; // length of next code
            int count = 0; // repeat count of the current code
            int maxCount = 7; // max repeat count
            int minCount = 4; // min repeat count

            if (nextlen == 0)
            {
                maxCount = 138; minCount = 3;
            }
            tree[(maxCode + 1) * 2 + 1] = (short)0x7fff; // guard //??

            for (n = 0; n <= maxCode; n++)
            {
                curlen = nextlen; nextlen = (int)tree[(n + 1) * 2 + 1];
                if (++count < maxCount && curlen == nextlen)
                {
                    continue;
                }
                else if (count < minCount)
                {
                    BlTree[curlen * 2] = (short)(BlTree[curlen * 2] + count);
                }
                else if (curlen != 0)
                {
                    if (curlen != prevlen)
                        BlTree[curlen * 2]++;
                    BlTree[InternalConstants.Rep36 * 2]++;
                }
                else if (count <= 10)
                {
                    BlTree[InternalConstants.Repz310 * 2]++;
                }
                else
                {
                    BlTree[InternalConstants.Repz11138 * 2]++;
                }
                count = 0; prevlen = curlen;
                if (nextlen == 0)
                {
                    maxCount = 138; minCount = 3;
                }
                else if (curlen == nextlen)
                {
                    maxCount = 6; minCount = 3;
                }
                else
                {
                    maxCount = 7; minCount = 4;
                }
            }
        }

        // Construct the Huffman tree for the bit lengths and return the index in
        // bl_order of the last bit length code to send.
        internal int build_bl_tree()
        {
            int maxBlindex; // index of last bit length code of non zero freq

            // Determine the bit length frequencies for literal and distance trees
            scan_tree(DynLtree, TreeLiterals.MaxCode);
            scan_tree(DynDtree, TreeDistances.MaxCode);

            // Build the bit length tree:
            TreeBitLengths.build_tree(this);
            // opt_len now includes the length of the tree representations, except
            // the lengths of the bit lengths codes and the 5+5+4 bits for the counts.

            // Determine the number of bit length codes to send. The pkzip format
            // requires that at least 4 bit length codes be sent. (appnote.txt says
            // 3 but the actual value used is 4.)
            for (maxBlindex = InternalConstants.BlCodes - 1; maxBlindex >= 3; maxBlindex--)
            {
                if (BlTree[Tree.BlOrder[maxBlindex] * 2 + 1] != 0)
                    break;
            }
            // Update opt_len to include the bit length tree and counts
            OptLen += 3 * (maxBlindex + 1) + 5 + 5 + 4;

            return maxBlindex;
        }


        // Send the header for a block using dynamic Huffman trees: the counts, the
        // lengths of the bit length codes, the literal tree and the distance tree.
        // IN assertion: lcodes >= 257, dcodes >= 1, blcodes >= 4.
        internal void send_all_trees(int lcodes, int dcodes, int blcodes)
        {
            int rank; // index in bl_order

            send_bits(lcodes - 257, 5); // not +255 as stated in appnote.txt
            send_bits(dcodes - 1, 5);
            send_bits(blcodes - 4, 4); // not -3 as stated in appnote.txt
            for (rank = 0; rank < blcodes; rank++)
            {
                send_bits(BlTree[Tree.BlOrder[rank] * 2 + 1], 3);
            }
            send_tree(DynLtree, lcodes - 1); // literal tree
            send_tree(DynDtree, dcodes - 1); // distance tree
        }

        // Send a literal or distance tree in compressed form, using the codes in
        // bl_tree.
        internal void send_tree(short[] tree, int maxCode)
        {
            int n;                           // iterates over all tree elements
            int prevlen   = -1;              // last emitted length
            int curlen;                      // length of current code
            int nextlen   = tree[0 * 2 + 1]; // length of next code
            int count     = 0;               // repeat count of the current code
            int maxCount = 7;               // max repeat count
            int minCount = 4;               // min repeat count

            if (nextlen == 0)
            {
                maxCount = 138; minCount = 3;
            }

            for (n = 0; n <= maxCode; n++)
            {
                curlen = nextlen; nextlen = tree[(n + 1) * 2 + 1];
                if (++count < maxCount && curlen == nextlen)
                {
                    continue;
                }
                else if (count < minCount)
                {
                    do
                    {
                        send_code(curlen, BlTree);
                    }
                    while (--count != 0);
                }
                else if (curlen != 0)
                {
                    if (curlen != prevlen)
                    {
                        send_code(curlen, BlTree); count--;
                    }
                    send_code(InternalConstants.Rep36, BlTree);
                    send_bits(count - 3, 2);
                }
                else if (count <= 10)
                {
                    send_code(InternalConstants.Repz310, BlTree);
                    send_bits(count - 3, 3);
                }
                else
                {
                    send_code(InternalConstants.Repz11138, BlTree);
                    send_bits(count - 11, 7);
                }
                count = 0; prevlen = curlen;
                if (nextlen == 0)
                {
                    maxCount = 138; minCount = 3;
                }
                else if (curlen == nextlen)
                {
                    maxCount = 6; minCount = 3;
                }
                else
                {
                    maxCount = 7; minCount = 4;
                }
            }
        }

        // Output a block of bytes on the stream.
        // IN assertion: there is enough room in pending_buf.
        private void put_bytes(byte[] p, int start, int len)
        {
            Array.Copy(p, start, Pending, PendingCount, len);
            PendingCount += len;
        }

#if NOTNEEDED
        private void put_byte(byte c)
        {
            pending[pendingCount++] = c;
        }
        internal void put_short(int b)
        {
            unchecked
            {
                pending[pendingCount++] = (byte)b;
                pending[pendingCount++] = (byte)(b >> 8);
            }
        }
        internal void putShortMSB(int b)
        {
            unchecked
            {
                pending[pendingCount++] = (byte)(b >> 8);
                pending[pendingCount++] = (byte)b;
            }
        }
#endif

        internal void send_code(int c, short[] tree)
        {
            int c2 = c * 2;
            send_bits((tree[c2] & 0xffff), (tree[c2 + 1] & 0xffff));
        }

        internal void send_bits(int value, int length)
        {
            int len = length;
            unchecked
            {
                if (BiValid > (int)BufSize - len)
                {
                    //int val = value;
                    //      bi_buf |= (val << bi_valid);

                    BiBuf |= (short)((value << BiValid) & 0xffff);
                    //put_short(bi_buf);
                        Pending[PendingCount++] = (byte)BiBuf;
                        Pending[PendingCount++] = (byte)(BiBuf >> 8);


                    BiBuf = (short)((uint)value >> (BufSize - BiValid));
                    BiValid += len - BufSize;
                }
                else
                {
                    //      bi_buf |= (value) << bi_valid;
                    BiBuf |= (short)((value << BiValid) & 0xffff);
                    BiValid += len;
                }
            }
        }

        // Send one empty static block to give enough lookahead for inflate.
        // This takes 10 bits, of which 7 may remain in the bit buffer.
        // The current inflate code requires 9 bits of lookahead. If the
        // last two codes for the previous block (real code plus EOB) were coded
        // on 5 bits or less, inflate may have only 5+3 bits of lookahead to decode
        // the last real code. In this case we send two empty static blocks instead
        // of one. (There are no problems if the previous block is stored or fixed.)
        // To simplify the code, we assume the worst case of last real code encoded
        // on one bit only.
        internal void _tr_align()
        {
            send_bits(StaticTrees << 1, 3);
            send_code(EndBlock, StaticTree.LengthAndLiteralsTreeCodes);

            bi_flush();

            // Of the 10 bits for the empty block, we have already sent
            // (10 - bi_valid) bits. The lookahead for the last real code (before
            // the EOB of the previous block) was thus at least one plus the length
            // of the EOB plus what we have just sent of the empty static block.
            if (1 + LastEobLen + 10 - BiValid < 9)
            {
                send_bits(StaticTrees << 1, 3);
                send_code(EndBlock, StaticTree.LengthAndLiteralsTreeCodes);
                bi_flush();
            }
            LastEobLen = 7;
        }


        // Save the match info and tally the frequency counts. Return true if
        // the current block must be flushed.
        internal bool _tr_tally(int dist, int lc)
        {
            Pending[DistanceOffset + LastLit * 2] = unchecked((byte) ( (uint)dist >> 8 ) );
            Pending[DistanceOffset + LastLit * 2 + 1] = unchecked((byte)dist);
            Pending[LengthOffset + LastLit] = unchecked((byte)lc);
            LastLit++;

            if (dist == 0)
            {
                // lc is the unmatched char
                DynLtree[lc * 2]++;
            }
            else
            {
                Matches++;
                // Here, lc is the match length - MIN_MATCH
                dist--; // dist = match distance - 1
                DynLtree[(Tree.LengthCode[lc] + InternalConstants.Literals + 1) * 2]++;
                DynDtree[Tree.DistanceCode(dist) * 2]++;
            }

            if ((LastLit & 0x1fff) == 0 && (int)CompressionLevel > 2)
            {
                // Compute an upper bound for the compressed length
                int outLength = LastLit << 3;
                int inLength = Strstart - BlockStart;
                int dcode;
                for (dcode = 0; dcode < InternalConstants.DCodes; dcode++)
                {
                    outLength = (int)(outLength + (int)DynDtree[dcode * 2] * (5L + Tree.ExtraDistanceBits[dcode]));
                }
                outLength >>= 3;
                if ((Matches < (LastLit / 2)) && outLength < inLength / 2)
                    return true;
            }

            return (LastLit == LitBufsize - 1) || (LastLit == LitBufsize);
            // dinoch - wraparound?
            // We avoid equality with lit_bufsize because of wraparound at 64K
            // on 16 bit machines and because stored blocks are restricted to
            // 64K-1 bytes.
        }



        // Send the block data compressed using the given Huffman trees
        internal void send_compressed_block(short[] ltree, short[] dtree)
        {
            int distance; // distance of matched string
            int lc;       // match length or unmatched char (if dist == 0)
            int lx = 0;   // running index in l_buf
            int code;     // the code to send
            int extra;    // number of extra bits to send

            if (LastLit != 0)
            {
                do
                {
                    int ix = DistanceOffset + lx * 2;
                    distance = ((Pending[ix] << 8) & 0xff00) |
                        (Pending[ix + 1] & 0xff);
                    lc = (Pending[LengthOffset + lx]) & 0xff;
                    lx++;

                    if (distance == 0)
                    {
                        send_code(lc, ltree); // send a literal byte
                    }
                    else
                    {
                        // literal or match pair
                        // Here, lc is the match length - MIN_MATCH
                        code = Tree.LengthCode[lc];

                        // send the length code
                        send_code(code + InternalConstants.Literals + 1, ltree);
                        extra = Tree.ExtraLengthBits[code];
                        if (extra != 0)
                        {
                            // send the extra length bits
                            lc -= Tree.LengthBase[code];
                            send_bits(lc, extra);
                        }
                        distance--; // dist is now the match distance - 1
                        code = Tree.DistanceCode(distance);

                        // send the distance code
                        send_code(code, dtree);

                        extra = Tree.ExtraDistanceBits[code];
                        if (extra != 0)
                        {
                            // send the extra distance bits
                            distance -= Tree.DistanceBase[code];
                            send_bits(distance, extra);
                        }
                    }

                    // Check that the overlay between pending and d_buf+l_buf is ok:
                }
                while (lx < LastLit);
            }

            send_code(EndBlock, ltree);
            LastEobLen = ltree[EndBlock * 2 + 1];
        }



        // Set the data type to ASCII or BINARY, using a crude approximation:
        // binary if more than 20% of the bytes are <= 6 or >= 128, ascii otherwise.
        // IN assertion: the fields freq of dyn_ltree are set and the total of all
        // frequencies does not exceed 64K (to fit in an int on 16 bit machines).
        internal void set_data_type()
        {
            int n = 0;
            int asciiFreq = 0;
            int binFreq = 0;
            while (n < 7)
            {
                binFreq += DynLtree[n * 2]; n++;
            }
            while (n < 128)
            {
                asciiFreq += DynLtree[n * 2]; n++;
            }
            while (n < InternalConstants.Literals)
            {
                binFreq += DynLtree[n * 2]; n++;
            }
            DataType = (sbyte)(binFreq > (asciiFreq >> 2) ? ZBinary : ZAscii);
        }



        // Flush the bit buffer, keeping at most 7 bits in it.
        internal void bi_flush()
        {
            if (BiValid == 16)
            {
                Pending[PendingCount++] = (byte)BiBuf;
                Pending[PendingCount++] = (byte)(BiBuf >> 8);
                BiBuf = 0;
                BiValid = 0;
            }
            else if (BiValid >= 8)
            {
                //put_byte((byte)bi_buf);
                Pending[PendingCount++] = (byte)BiBuf;
                BiBuf >>= 8;
                BiValid -= 8;
            }
        }

        // Flush the bit buffer and align the output on a byte boundary
        internal void bi_windup()
        {
            if (BiValid > 8)
            {
                Pending[PendingCount++] = (byte)BiBuf;
                Pending[PendingCount++] = (byte)(BiBuf >> 8);
            }
            else if (BiValid > 0)
            {
                //put_byte((byte)bi_buf);
                Pending[PendingCount++] = (byte)BiBuf;
            }
            BiBuf = 0;
            BiValid = 0;
        }

        // Copy a stored block, storing first the length and its
        // one's complement if requested.
        internal void copy_block(int buf, int len, bool header)
        {
            bi_windup(); // align on byte boundary
            LastEobLen = 8; // enough lookahead for inflate

            if (header)
                unchecked
                {
                    //put_short((short)len);
                    Pending[PendingCount++] = (byte)len;
                    Pending[PendingCount++] = (byte)(len >> 8);
                    //put_short((short)~len);
                    Pending[PendingCount++] = (byte)~len;
                    Pending[PendingCount++] = (byte)(~len >> 8);
                }

            put_bytes(Window, buf, len);
        }

        internal void flush_block_only(bool eof)
        {
            _tr_flush_block(BlockStart >= 0 ? BlockStart : -1, Strstart - BlockStart, eof);
            BlockStart = Strstart;
            Codec.flush_pending();
        }

        // Copy without compression as much as possible from the input stream, return
        // the current block state.
        // This function does not insert new strings in the dictionary since
        // uncompressible data is probably not useful. This function is used
        // only for the level=0 compression option.
        // NOTE: this function should be optimized to avoid extra copying from
        // window to pending_buf.
        internal BlockState DeflateNone(FlushType flush)
        {
            // Stored blocks are limited to 0xffff bytes, pending is limited
            // to pending_buf_size, and each stored block has a 5 byte header:

            int maxBlockSize = 0xffff;
            int maxStart;

            if (maxBlockSize > Pending.Length - 5)
            {
                maxBlockSize = Pending.Length - 5;
            }

            // Copy as much as possible from input to output:
            while (true)
            {
                // Fill the window as much as possible:
                if (Lookahead <= 1)
                {
                    _fillWindow();
                    if (Lookahead == 0 && flush == FlushType.None)
                        return BlockState.NeedMore;
                    if (Lookahead == 0)
                        break; // flush the current block
                }

                Strstart += Lookahead;
                Lookahead = 0;

                // Emit a stored block if pending will be full:
                maxStart = BlockStart + maxBlockSize;
                if (Strstart == 0 || Strstart >= maxStart)
                {
                    // strstart == 0 is possible when wraparound on 16-bit machine
                    Lookahead = (int)(Strstart - maxStart);
                    Strstart = (int)maxStart;

                    flush_block_only(false);
                    if (Codec.AvailableBytesOut == 0)
                        return BlockState.NeedMore;
                }

                // Flush if we may have to slide, otherwise block_start may become
                // negative and the data will be gone:
                if (Strstart - BlockStart >= WSize - MinLookahead)
                {
                    flush_block_only(false);
                    if (Codec.AvailableBytesOut == 0)
                        return BlockState.NeedMore;
                }
            }

            flush_block_only(flush == FlushType.Finish);
            if (Codec.AvailableBytesOut == 0)
                return (flush == FlushType.Finish) ? BlockState.FinishStarted : BlockState.NeedMore;

            return flush == FlushType.Finish ? BlockState.FinishDone : BlockState.BlockDone;
        }


        // Send a stored block
        internal void _tr_stored_block(int buf, int storedLen, bool eof)
        {
            send_bits((StoredBlock << 1) + (eof ? 1 : 0), 3); // send block type
            copy_block(buf, storedLen, true); // with header
        }

        // Determine the best encoding for the current block: dynamic trees, static
        // trees or store, and output the encoded block to the zip file.
        internal void _tr_flush_block(int buf, int storedLen, bool eof)
        {
            int optLenb, staticLenb; // opt_len and static_len in bytes
            int maxBlindex = 0; // index of last bit length code of non zero freq

            // Build the Huffman trees unless a stored block is forced
            if (CompressionLevel > 0)
            {
                // Check if the file is ascii or binary
                if (DataType == ZUnknown)
                    set_data_type();

                // Construct the literal and distance trees
                TreeLiterals.build_tree(this);

                TreeDistances.build_tree(this);

                // At this point, opt_len and static_len are the total bit lengths of
                // the compressed block data, excluding the tree representations.

                // Build the bit length tree for the above two trees, and get the index
                // in bl_order of the last bit length code to send.
                maxBlindex = build_bl_tree();

                // Determine the best encoding. Compute first the block length in bytes
                optLenb = (OptLen + 3 + 7) >> 3;
                staticLenb = (StaticLen + 3 + 7) >> 3;

                if (staticLenb <= optLenb)
                    optLenb = staticLenb;
            }
            else
            {
                optLenb = staticLenb = storedLen + 5; // force a stored block
            }

            if (storedLen + 4 <= optLenb && buf != -1)
            {
                // 4: two words for the lengths
                // The test buf != NULL is only necessary if LIT_BUFSIZE > WSIZE.
                // Otherwise we can't have processed more than WSIZE input bytes since
                // the last block flush, because compression would have been
                // successful. If LIT_BUFSIZE <= WSIZE, it is never too late to
                // transform a block into a stored block.
                _tr_stored_block(buf, storedLen, eof);
            }
            else if (staticLenb == optLenb)
            {
                send_bits((StaticTrees << 1) + (eof ? 1 : 0), 3);
                send_compressed_block(StaticTree.LengthAndLiteralsTreeCodes, StaticTree.DistTreeCodes);
            }
            else
            {
                send_bits((DynTrees << 1) + (eof ? 1 : 0), 3);
                send_all_trees(TreeLiterals.MaxCode + 1, TreeDistances.MaxCode + 1, maxBlindex + 1);
                send_compressed_block(DynLtree, DynDtree);
            }

            // The above check is made mod 2^32, for files larger than 512 MB
            // and uLong implemented on 32 bits.

            _InitializeBlocks();

            if (eof)
            {
                bi_windup();
            }
        }

        // Fill the window when the lookahead becomes insufficient.
        // Updates strstart and lookahead.
        //
        // IN assertion: lookahead < MIN_LOOKAHEAD
        // OUT assertions: strstart <= window_size-MIN_LOOKAHEAD
        //    At least one byte has been read, or avail_in == 0; reads are
        //    performed for at least two bytes (required for the zip translate_eol
        //    option -- not supported here).
        private void _fillWindow()
        {
            int n, m;
            int p;
            int more; // Amount of free space at the end of the window.

            do
            {
                more = (WindowSize - Lookahead - Strstart);

                // Deal with !@#$% 64K limit:
                if (more == 0 && Strstart == 0 && Lookahead == 0)
                {
                    more = WSize;
                }
                else if (more == -1)
                {
                    // Very unlikely, but possible on 16 bit machine if strstart == 0
                    // and lookahead == 1 (input done one byte at time)
                    more--;

                    // If the window is almost full and there is insufficient lookahead,
                    // move the upper half to the lower one to make room in the upper half.
                }
                else if (Strstart >= WSize + WSize - MinLookahead)
                {
                    Array.Copy(Window, WSize, Window, 0, WSize);
                    MatchStart -= WSize;
                    Strstart -= WSize; // we now have strstart >= MAX_DIST
                    BlockStart -= WSize;

                    // Slide the hash table (could be avoided with 32 bit values
                    // at the expense of memory usage). We slide even when level == 0
                    // to keep the hash table consistent if we switch back to level > 0
                    // later. (Using level 0 permanently is not an optimal usage of
                    // zlib, so we don't care about this pathological case.)

                    n = HashSize;
                    p = n;
                    do
                    {
                        m = (Head[--p] & 0xffff);
                        Head[p] = (short)((m >= WSize) ? (m - WSize) : 0);
                    }
                    while (--n != 0);

                    n = WSize;
                    p = n;
                    do
                    {
                        m = (Prev[--p] & 0xffff);
                        Prev[p] = (short)((m >= WSize) ? (m - WSize) : 0);
                        // If n is not on any hash chain, prev[n] is garbage but
                        // its value will never be used.
                    }
                    while (--n != 0);
                    more += WSize;
                }

                if (Codec.AvailableBytesIn == 0)
                    return;

                // If there was no sliding:
                //    strstart <= WSIZE+MAX_DIST-1 && lookahead <= MIN_LOOKAHEAD - 1 &&
                //    more == window_size - lookahead - strstart
                // => more >= window_size - (MIN_LOOKAHEAD-1 + WSIZE + MAX_DIST-1)
                // => more >= window_size - 2*WSIZE + 2
                // In the BIG_MEM or MMAP case (not yet supported),
                //   window_size == input_size + MIN_LOOKAHEAD  &&
                //   strstart + s->lookahead <= input_size => more >= MIN_LOOKAHEAD.
                // Otherwise, window_size == 2*WSIZE so more >= 2.
                // If there was sliding, more >= WSIZE. So in all cases, more >= 2.

                n = Codec.read_buf(Window, Strstart + Lookahead, more);
                Lookahead += n;

                // Initialize the hash value now that we have some input:
                if (Lookahead >= MinMatch)
                {
                    InsH = Window[Strstart] & 0xff;
                    InsH = (((InsH) << HashShift) ^ (Window[Strstart + 1] & 0xff)) & HashMask;
                }
                // If the whole input has less than MIN_MATCH bytes, ins_h is garbage,
                // but this is not important since only literal bytes will be emitted.
            }
            while (Lookahead < MinLookahead && Codec.AvailableBytesIn != 0);
        }

        // Compress as much as possible from the input stream, return the current
        // block state.
        // This function does not perform lazy evaluation of matches and inserts
        // new strings in the dictionary only for unmatched strings or for short
        // matches. It is used only for the fast compression options.
        internal BlockState DeflateFast(FlushType flush)
        {
            //    short hash_head = 0; // head of the hash chain
            int hashHead = 0; // head of the hash chain
            bool bflush; // set if current block must be flushed

            while (true)
            {
                // Make sure that we always have enough lookahead, except
                // at the end of the input file. We need MAX_MATCH bytes
                // for the next match, plus MIN_MATCH bytes to insert the
                // string following the next match.
                if (Lookahead < MinLookahead)
                {
                    _fillWindow();
                    if (Lookahead < MinLookahead && flush == FlushType.None)
                    {
                        return BlockState.NeedMore;
                    }
                    if (Lookahead == 0)
                        break; // flush the current block
                }

                // Insert the string window[strstart .. strstart+2] in the
                // dictionary, and set hash_head to the head of the hash chain:
                if (Lookahead >= MinMatch)
                {
                    InsH = (((InsH) << HashShift) ^ (Window[(Strstart) + (MinMatch - 1)] & 0xff)) & HashMask;

                    //  prev[strstart&w_mask]=hash_head=head[ins_h];
                    hashHead = (Head[InsH] & 0xffff);
                    Prev[Strstart & WMask] = Head[InsH];
                    Head[InsH] = unchecked((short)Strstart);
                }

                // Find the longest match, discarding those <= prev_length.
                // At this point we have always match_length < MIN_MATCH

                if (hashHead != 0L && ((Strstart - hashHead) & 0xffff) <= WSize - MinLookahead)
                {
                    // To simplify the code, we prevent matches with the string
                    // of window index 0 (in particular we have to avoid a match
                    // of the string with itself at the start of the input file).
                    if (CompressionStrategy != CompressionStrategy.HuffmanOnly)
                    {
                        MatchLength = longest_match(hashHead);
                    }
                    // longest_match() sets match_start
                }
                if (MatchLength >= MinMatch)
                {
                    //        check_match(strstart, match_start, match_length);

                    bflush = _tr_tally(Strstart - MatchStart, MatchLength - MinMatch);

                    Lookahead -= MatchLength;

                    // Insert new strings in the hash table only if the match length
                    // is not too large. This saves time but degrades compression.
                    if (MatchLength <= _config.MaxLazy && Lookahead >= MinMatch)
                    {
                        MatchLength--; // string at strstart already in hash table
                        do
                        {
                            Strstart++;

                            InsH = ((InsH << HashShift) ^ (Window[(Strstart) + (MinMatch - 1)] & 0xff)) & HashMask;
                            //      prev[strstart&w_mask]=hash_head=head[ins_h];
                            hashHead = (Head[InsH] & 0xffff);
                            Prev[Strstart & WMask] = Head[InsH];
                            Head[InsH] = unchecked((short)Strstart);

                            // strstart never exceeds WSIZE-MAX_MATCH, so there are
                            // always MIN_MATCH bytes ahead.
                        }
                        while (--MatchLength != 0);
                        Strstart++;
                    }
                    else
                    {
                        Strstart += MatchLength;
                        MatchLength = 0;
                        InsH = Window[Strstart] & 0xff;

                        InsH = (((InsH) << HashShift) ^ (Window[Strstart + 1] & 0xff)) & HashMask;
                        // If lookahead < MIN_MATCH, ins_h is garbage, but it does not
                        // matter since it will be recomputed at next deflate call.
                    }
                }
                else
                {
                    // No match, output a literal byte

                    bflush = _tr_tally(0, Window[Strstart] & 0xff);
                    Lookahead--;
                    Strstart++;
                }
                if (bflush)
                {
                    flush_block_only(false);
                    if (Codec.AvailableBytesOut == 0)
                        return BlockState.NeedMore;
                }
            }

            flush_block_only(flush == FlushType.Finish);
            if (Codec.AvailableBytesOut == 0)
            {
                if (flush == FlushType.Finish)
                    return BlockState.FinishStarted;
                else
                    return BlockState.NeedMore;
            }
            return flush == FlushType.Finish ? BlockState.FinishDone : BlockState.BlockDone;
        }

        // Same as above, but achieves better compression. We use a lazy
        // evaluation for matches: a match is finally adopted only if there is
        // no better match at the next window position.
        internal BlockState DeflateSlow(FlushType flush)
        {
            //    short hash_head = 0;    // head of hash chain
            int hashHead = 0; // head of hash chain
            bool bflush; // set if current block must be flushed

            // Process the input block.
            while (true)
            {
                // Make sure that we always have enough lookahead, except
                // at the end of the input file. We need MAX_MATCH bytes
                // for the next match, plus MIN_MATCH bytes to insert the
                // string following the next match.

                if (Lookahead < MinLookahead)
                {
                    _fillWindow();
                    if (Lookahead < MinLookahead && flush == FlushType.None)
                        return BlockState.NeedMore;

                    if (Lookahead == 0)
                        break; // flush the current block
                }

                // Insert the string window[strstart .. strstart+2] in the
                // dictionary, and set hash_head to the head of the hash chain:

                if (Lookahead >= MinMatch)
                {
                    InsH = (((InsH) << HashShift) ^ (Window[(Strstart) + (MinMatch - 1)] & 0xff)) & HashMask;
                    //  prev[strstart&w_mask]=hash_head=head[ins_h];
                    hashHead = (Head[InsH] & 0xffff);
                    Prev[Strstart & WMask] = Head[InsH];
                    Head[InsH] = unchecked((short)Strstart);
                }

                // Find the longest match, discarding those <= prev_length.
                PrevLength = MatchLength;
                PrevMatch = MatchStart;
                MatchLength = MinMatch - 1;

                if (hashHead != 0 && PrevLength < _config.MaxLazy &&
                    ((Strstart - hashHead) & 0xffff) <= WSize - MinLookahead)
                {
                    // To simplify the code, we prevent matches with the string
                    // of window index 0 (in particular we have to avoid a match
                    // of the string with itself at the start of the input file).

                    if (CompressionStrategy != CompressionStrategy.HuffmanOnly)
                    {
                        MatchLength = longest_match(hashHead);
                    }
                    // longest_match() sets match_start

                    if (MatchLength <= 5 && (CompressionStrategy == CompressionStrategy.Filtered ||
                                              (MatchLength == MinMatch && Strstart - MatchStart > 4096)))
                    {

                        // If prev_match is also MIN_MATCH, match_start is garbage
                        // but we will ignore the current match anyway.
                        MatchLength = MinMatch - 1;
                    }
                }

                // If there was a match at the previous step and the current
                // match is not better, output the previous match:
                if (PrevLength >= MinMatch && MatchLength <= PrevLength)
                {
                    int maxInsert = Strstart + Lookahead - MinMatch;
                    // Do not insert strings in hash table beyond this.

                    //          check_match(strstart-1, prev_match, prev_length);

                    bflush = _tr_tally(Strstart - 1 - PrevMatch, PrevLength - MinMatch);

                    // Insert in hash table all strings up to the end of the match.
                    // strstart-1 and strstart are already inserted. If there is not
                    // enough lookahead, the last two strings are not inserted in
                    // the hash table.
                    Lookahead -= (PrevLength - 1);
                    PrevLength -= 2;
                    do
                    {
                        if (++Strstart <= maxInsert)
                        {
                            InsH = (((InsH) << HashShift) ^ (Window[(Strstart) + (MinMatch - 1)] & 0xff)) & HashMask;
                            //prev[strstart&w_mask]=hash_head=head[ins_h];
                            hashHead = (Head[InsH] & 0xffff);
                            Prev[Strstart & WMask] = Head[InsH];
                            Head[InsH] = unchecked((short)Strstart);
                        }
                    }
                    while (--PrevLength != 0);
                    MatchAvailable = 0;
                    MatchLength = MinMatch - 1;
                    Strstart++;

                    if (bflush)
                    {
                        flush_block_only(false);
                        if (Codec.AvailableBytesOut == 0)
                            return BlockState.NeedMore;
                    }
                }
                else if (MatchAvailable != 0)
                {

                    // If there was no match at the previous position, output a
                    // single literal. If there was a match but the current match
                    // is longer, truncate the previous match to a single literal.

                    bflush = _tr_tally(0, Window[Strstart - 1] & 0xff);

                    if (bflush)
                    {
                        flush_block_only(false);
                    }
                    Strstart++;
                    Lookahead--;
                    if (Codec.AvailableBytesOut == 0)
                        return BlockState.NeedMore;
                }
                else
                {
                    // There is no previous match to compare with, wait for
                    // the next step to decide.

                    MatchAvailable = 1;
                    Strstart++;
                    Lookahead--;
                }
            }

            if (MatchAvailable != 0)
            {
                bflush = _tr_tally(0, Window[Strstart - 1] & 0xff);
                MatchAvailable = 0;
            }
            flush_block_only(flush == FlushType.Finish);

            if (Codec.AvailableBytesOut == 0)
            {
                if (flush == FlushType.Finish)
                    return BlockState.FinishStarted;
                else
                    return BlockState.NeedMore;
            }

            return flush == FlushType.Finish ? BlockState.FinishDone : BlockState.BlockDone;
        }


        internal int longest_match(int curMatch)
        {
            int chainLength = _config.MaxChainLength; // max hash chain length
            int scan         = Strstart;              // current string
            int match;                                // matched string
            int len;                                  // length of current match
            int bestLen     = PrevLength;           // best match length so far
            int limit        = Strstart > (WSize - MinLookahead) ? Strstart - (WSize - MinLookahead) : 0;

            int niceLength = _config.NiceLength;

            // Stop when cur_match becomes <= limit. To simplify the code,
            // we prevent matches with the string of window index 0.

            int wmask = WMask;

            int strend = Strstart + MaxMatch;
            byte scanEnd1 = Window[scan + bestLen - 1];
            byte scanEnd = Window[scan + bestLen];

            // The code is optimized for HASH_BITS >= 8 and MAX_MATCH-2 multiple of 16.
            // It is easy to get rid of this optimization if necessary.

            // Do not waste too much time if we already have a good match:
            if (PrevLength >= _config.GoodLength)
            {
                chainLength >>= 2;
            }

            // Do not look for matches beyond the end of the input. This is necessary
            // to make deflate deterministic.
            if (niceLength > Lookahead)
                niceLength = Lookahead;

            do
            {
                match = curMatch;

                // Skip to next match if the match length cannot increase
                // or if the match length is less than 2:
                if (Window[match + bestLen] != scanEnd ||
                    Window[match + bestLen - 1] != scanEnd1 ||
                    Window[match] != Window[scan] ||
                    Window[++match] != Window[scan + 1])
                    continue;

                // The check at best_len-1 can be removed because it will be made
                // again later. (This heuristic is not always a win.)
                // It is not necessary to compare scan[2] and match[2] since they
                // are always equal when the other bytes match, given that
                // the hash keys are equal and that HASH_BITS >= 8.
                scan += 2; match++;

                // We check for insufficient lookahead only every 8th comparison;
                // the 256th check will be made at strstart+258.
                do
                {
                }
                while (Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] &&
                       Window[++scan] == Window[++match] && scan < strend);

                len = MaxMatch - (int)(strend - scan);
                scan = strend - MaxMatch;

                if (len > bestLen)
                {
                    MatchStart = curMatch;
                    bestLen = len;
                    if (len >= niceLength)
                        break;
                    scanEnd1 = Window[scan + bestLen - 1];
                    scanEnd = Window[scan + bestLen];
                }
            }
            while ((curMatch = (Prev[curMatch & wmask] & 0xffff)) > limit && --chainLength != 0);

            if (bestLen <= Lookahead)
                return bestLen;
            return Lookahead;
        }


        private bool _rfc1950BytesEmitted = false;
        private bool _wantRfc1950HeaderBytes = true;
        internal bool WantRfc1950HeaderBytes
        {
            get { return _wantRfc1950HeaderBytes; }
            set { _wantRfc1950HeaderBytes = value; }
        }


        internal int Initialize(ZlibCodec codec, CompressionLevel level)
        {
            return Initialize(codec, level, ZlibConstants.WindowBitsMax);
        }

        internal int Initialize(ZlibCodec codec, CompressionLevel level, int bits)
        {
            return Initialize(codec, level, bits, MemLevelDefault, CompressionStrategy.Default);
        }

        internal int Initialize(ZlibCodec codec, CompressionLevel level, int bits, CompressionStrategy compressionStrategy)
        {
            return Initialize(codec, level, bits, MemLevelDefault, compressionStrategy);
        }

        internal int Initialize(ZlibCodec codec, CompressionLevel level, int windowBits, int memLevel, CompressionStrategy strategy)
        {
            Codec = codec;
            Codec.Message = null;

            // validation
            if (windowBits < 9 || windowBits > 15)
                throw new ZlibException("windowBits must be in the range 9..15.");

            if (memLevel < 1 || memLevel > MemLevelMax)
                throw new ZlibException(String.Format("memLevel must be in the range 1.. {0}", MemLevelMax));

            Codec.Dstate = this;

            WBits = windowBits;
            WSize = 1 << WBits;
            WMask = WSize - 1;

            HashBits = memLevel + 7;
            HashSize = 1 << HashBits;
            HashMask = HashSize - 1;
            HashShift = ((HashBits + MinMatch - 1) / MinMatch);

            Window = new byte[WSize * 2];
            Prev = new short[WSize];
            Head = new short[HashSize];

            // for memLevel==8, this will be 16384, 16k
            LitBufsize = 1 << (memLevel + 6);

            // Use a single array as the buffer for data pending compression,
            // the output distance codes, and the output length codes (aka tree).
            // orig comment: This works just fine since the average
            // output size for (length,distance) codes is <= 24 bits.
            Pending = new byte[LitBufsize * 4];
            DistanceOffset = LitBufsize;
            LengthOffset = (1 + 2) * LitBufsize;

            // So, for memLevel 8, the length of the pending buffer is 65536. 64k.
            // The first 16k are pending bytes.
            // The middle slice, of 32k, is used for distance codes.
            // The final 16k are length codes.

            this.CompressionLevel = level;
            this.CompressionStrategy = strategy;

            Reset();
            return ZlibConstants.ZOk;
        }


        internal void Reset()
        {
            Codec.TotalBytesIn = Codec.TotalBytesOut = 0;
            Codec.Message = null;
            //strm.data_type = Z_UNKNOWN;

            PendingCount = 0;
            NextPending = 0;

            _rfc1950BytesEmitted = false;

            Status = (WantRfc1950HeaderBytes) ? InitState : BusyState;
            Codec._Adler32 = Adler.Adler32(0, null, 0, 0);

            LastFlush = (int)FlushType.None;

            _InitializeTreeData();
            _InitializeLazyMatch();
        }


        internal int End()
        {
            if (Status != InitState && Status != BusyState && Status != FinishState)
            {
                return ZlibConstants.ZStreamError;
            }
            // Deallocate in reverse order of allocations:
            Pending = null;
            Head = null;
            Prev = null;
            Window = null;
            // free
            // dstate=null;
            return Status == BusyState ? ZlibConstants.ZDataError : ZlibConstants.ZOk;
        }


        private void SetDeflater()
        {
            switch (_config.Flavor)
            {
                case DeflateFlavor.Store:
                    _deflateFunction = DeflateNone;
                    break;
                case DeflateFlavor.Fast:
                    _deflateFunction = DeflateFast;
                    break;
                case DeflateFlavor.Slow:
                    _deflateFunction = DeflateSlow;
                    break;
            }
        }


        internal int SetParams(CompressionLevel level, CompressionStrategy strategy)
        {
            int result = ZlibConstants.ZOk;

            if (CompressionLevel != level)
            {
                Config newConfig = Config.Lookup(level);

                // change in the deflate flavor (Fast vs slow vs none)?
                if (newConfig.Flavor != _config.Flavor && Codec.TotalBytesIn != 0)
                {
                    // Flush the last buffer:
                    result = Codec.Deflate(FlushType.Partial);
                }

                CompressionLevel = level;
                _config = newConfig;
                SetDeflater();
            }

            // no need to flush with change in strategy?  Really?
            CompressionStrategy = strategy;

            return result;
        }


        internal int SetDictionary(byte[] dictionary)
        {
            int length = dictionary.Length;
            int index = 0;

            if (dictionary == null || Status != InitState)
                throw new ZlibException("Stream error.");

            Codec._Adler32 = Adler.Adler32(Codec._Adler32, dictionary, 0, dictionary.Length);

            if (length < MinMatch)
                return ZlibConstants.ZOk;
            if (length > WSize - MinLookahead)
            {
                length = WSize - MinLookahead;
                index = dictionary.Length - length; // use the tail of the dictionary
            }
            Array.Copy(dictionary, index, Window, 0, length);
            Strstart = length;
            BlockStart = length;

            // Insert all strings in the hash table (except for the last two bytes).
            // s->lookahead stays null, so s->ins_h will be recomputed at the next
            // call of fill_window.

            InsH = Window[0] & 0xff;
            InsH = (((InsH) << HashShift) ^ (Window[1] & 0xff)) & HashMask;

            for (int n = 0; n <= length - MinMatch; n++)
            {
                InsH = (((InsH) << HashShift) ^ (Window[(n) + (MinMatch - 1)] & 0xff)) & HashMask;
                Prev[n & WMask] = Head[InsH];
                Head[InsH] = (short)n;
            }
            return ZlibConstants.ZOk;
        }



        internal int Deflate(FlushType flush)
        {
            int oldFlush;

            if (Codec.OutputBuffer == null ||
                (Codec.InputBuffer == null && Codec.AvailableBytesIn != 0) ||
                (Status == FinishState && flush != FlushType.Finish))
            {
                Codec.Message = ErrorMessage[ZlibConstants.ZNeedDict - (ZlibConstants.ZStreamError)];
                throw new ZlibException(String.Format("Something is fishy. [{0}]", Codec.Message));
            }
            if (Codec.AvailableBytesOut == 0)
            {
                Codec.Message = ErrorMessage[ZlibConstants.ZNeedDict - (ZlibConstants.ZBufError)];
                throw new ZlibException("OutputBuffer is full (AvailableBytesOut == 0)");
            }

            oldFlush = LastFlush;
            LastFlush = (int)flush;

            // Write the zlib (rfc1950) header bytes
            if (Status == InitState)
            {
                int header = (ZDeflated + ((WBits - 8) << 4)) << 8;
                int levelFlags = (((int)CompressionLevel - 1) & 0xff) >> 1;

                if (levelFlags > 3)
                    levelFlags = 3;
                header |= (levelFlags << 6);
                if (Strstart != 0)
                    header |= PresetDict;
                header += 31 - (header % 31);

                Status = BusyState;
                //putShortMSB(header);
                unchecked
                {
                    Pending[PendingCount++] = (byte)(header >> 8);
                    Pending[PendingCount++] = (byte)header;
                }
                // Save the adler32 of the preset dictionary:
                if (Strstart != 0)
                {
                    Pending[PendingCount++] = (byte)((Codec._Adler32 & 0xFF000000) >> 24);
                    Pending[PendingCount++] = (byte)((Codec._Adler32 & 0x00FF0000) >> 16);
                    Pending[PendingCount++] = (byte)((Codec._Adler32 & 0x0000FF00) >> 8);
                    Pending[PendingCount++] = (byte)(Codec._Adler32 & 0x000000FF);
                }
                Codec._Adler32 = Adler.Adler32(0, null, 0, 0);
            }

            // Flush as much pending output as possible
            if (PendingCount != 0)
            {
                Codec.flush_pending();
                if (Codec.AvailableBytesOut == 0)
                {
                    //System.out.println("  avail_out==0");
                    // Since avail_out is 0, deflate will be called again with
                    // more output space, but possibly with both pending and
                    // avail_in equal to zero. There won't be anything to do,
                    // but this is not an error situation so make sure we
                    // return OK instead of BUF_ERROR at next call of deflate:
                    LastFlush = -1;
                    return ZlibConstants.ZOk;
                }

                // Make sure there is something to do and avoid duplicate consecutive
                // flushes. For repeated and useless calls with Z_FINISH, we keep
                // returning Z_STREAM_END instead of Z_BUFF_ERROR.
            }
            else if (Codec.AvailableBytesIn == 0 &&
                     (int)flush <= oldFlush &&
                     flush != FlushType.Finish)
            {
                // workitem 8557
                //
                // Not sure why this needs to be an error.  pendingCount == 0, which
                // means there's nothing to deflate.  And the caller has not asked
                // for a FlushType.Finish, but...  that seems very non-fatal.  We
                // can just say "OK" and do nothing.

                // _codec.Message = z_errmsg[ZlibConstants.Z_NEED_DICT - (ZlibConstants.Z_BUF_ERROR)];
                // throw new ZlibException("AvailableBytesIn == 0 && flush<=old_flush && flush != FlushType.Finish");

                return ZlibConstants.ZOk;
            }

            // User must not provide more input after the first FINISH:
            if (Status == FinishState && Codec.AvailableBytesIn != 0)
            {
                Codec.Message = ErrorMessage[ZlibConstants.ZNeedDict - (ZlibConstants.ZBufError)];
                throw new ZlibException("status == FINISH_STATE && _codec.AvailableBytesIn != 0");
            }

            // Start a new block or continue the current one.
            if (Codec.AvailableBytesIn != 0 || Lookahead != 0 || (flush != FlushType.None && Status != FinishState))
            {
                BlockState bstate = _deflateFunction(flush);

                if (bstate == BlockState.FinishStarted || bstate == BlockState.FinishDone)
                {
                    Status = FinishState;
                }
                if (bstate == BlockState.NeedMore || bstate == BlockState.FinishStarted)
                {
                    if (Codec.AvailableBytesOut == 0)
                    {
                        LastFlush = -1; // avoid BUF_ERROR next call, see above
                    }
                    return ZlibConstants.ZOk;
                    // If flush != Z_NO_FLUSH && avail_out == 0, the next call
                    // of deflate should use the same flush parameter to make sure
                    // that the flush is complete. So we don't have to output an
                    // empty block here, this will be done at next call. This also
                    // ensures that for a very small output buffer, we emit at most
                    // one empty block.
                }

                if (bstate == BlockState.BlockDone)
                {
                    if (flush == FlushType.Partial)
                    {
                        _tr_align();
                    }
                    else
                    {
                        // FlushType.Full or FlushType.Sync
                        _tr_stored_block(0, 0, false);
                        // For a full flush, this empty block will be recognized
                        // as a special marker by inflate_sync().
                        if (flush == FlushType.Full)
                        {
                            // clear hash (forget the history)
                            for (int i = 0; i < HashSize; i++)
                                Head[i] = 0;
                        }
                    }
                    Codec.flush_pending();
                    if (Codec.AvailableBytesOut == 0)
                    {
                        LastFlush = -1; // avoid BUF_ERROR at next call, see above
                        return ZlibConstants.ZOk;
                    }
                }
            }

            if (flush != FlushType.Finish)
                return ZlibConstants.ZOk;

            if (!WantRfc1950HeaderBytes || _rfc1950BytesEmitted)
                return ZlibConstants.ZStreamEnd;

            // Write the zlib trailer (adler32)
            Pending[PendingCount++] = (byte)((Codec._Adler32 & 0xFF000000) >> 24);
            Pending[PendingCount++] = (byte)((Codec._Adler32 & 0x00FF0000) >> 16);
            Pending[PendingCount++] = (byte)((Codec._Adler32 & 0x0000FF00) >> 8);
            Pending[PendingCount++] = (byte)(Codec._Adler32 & 0x000000FF);
            //putShortMSB((int)(SharedUtils.URShift(_codec._Adler32, 16)));
            //putShortMSB((int)(_codec._Adler32 & 0xffff));

            Codec.flush_pending();

            // If avail_out is zero, the application will call deflate again
            // to flush the rest.

            _rfc1950BytesEmitted = true; // write the trailer only once!

            return PendingCount != 0 ? ZlibConstants.ZOk : ZlibConstants.ZStreamEnd;
        }

    }
}