using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Mutable game state over a Layout. Freeness is maintained with neighbour counters.</summary>
    public sealed class Board
    {
        public readonly Layout Layout;
        public readonly byte[] KindAt;
        public readonly bool[] Present;
        readonly short[] aboveCnt, leftCnt, rightCnt;
        public int Remaining { get; private set; }

        public Board(Layout layout, byte[] kinds)
        {
            Layout = layout;
            int n = layout.Count;
            KindAt = (byte[])kinds.Clone();
            Present = new bool[n];
            aboveCnt = new short[n]; leftCnt = new short[n]; rightCnt = new short[n];
            for (int i = 0; i < n; i++) Present[i] = true;
            Remaining = n;
            for (int i = 0; i < n; i++)
            {
                aboveCnt[i] = (short)layout.Above[i].Length;
                leftCnt[i] = (short)layout.Left[i].Length;
                rightCnt[i] = (short)layout.Right[i].Length;
            }
        }

        public Board Clone()
        {
            var b = new Board(Layout, KindAt);
            for (int i = 0; i < Present.Length; i++) b.Present[i] = Present[i];
            Array.Copy(aboveCnt, b.aboveCnt, aboveCnt.Length);
            Array.Copy(leftCnt, b.leftCnt, leftCnt.Length);
            Array.Copy(rightCnt, b.rightCnt, rightCnt.Length);
            b.Remaining = Remaining;
            return b;
        }

        public bool IsCovered(int p) { return aboveCnt[p] > 0; }
        public bool IsBlockedLeft(int p) { return leftCnt[p] > 0; }
        public bool IsBlockedRight(int p) { return rightCnt[p] > 0; }

        public bool IsFree(int p)
        {
            return Present[p] && aboveCnt[p] == 0 && (leftCnt[p] == 0 || rightCnt[p] == 0);
        }

        void Take(int p)
        {
            Present[p] = false;
            Remaining--;
            var lo = Layout.Below[p]; for (int i = 0; i < lo.Length; i++) aboveCnt[lo[i]]--;
            var l = Layout.Left[p]; for (int i = 0; i < l.Length; i++) rightCnt[l[i]]--;
            var r = Layout.Right[p]; for (int i = 0; i < r.Length; i++) leftCnt[r[i]]--;
        }

        void Put(int p)
        {
            Present[p] = true;
            Remaining++;
            var lo = Layout.Below[p]; for (int i = 0; i < lo.Length; i++) aboveCnt[lo[i]]++;
            var l = Layout.Left[p]; for (int i = 0; i < l.Length; i++) rightCnt[l[i]]++;
            var r = Layout.Right[p]; for (int i = 0; i < r.Length; i++) leftCnt[r[i]]++;
        }

        public void Remove(int a, int b) { Take(a); Take(b); }
        public void Restore(int a, int b) { Put(b); Put(a); }

        /// <summary>Re-deals kinds (used by shuffle and by undo of a shuffle).</summary>
        public void SetKinds(byte[] kinds)
        {
            for (int i = 0; i < KindAt.Length; i++) KindAt[i] = kinds[i];
        }

        public void FreeTiles(List<int> output)
        {
            output.Clear();
            for (int i = 0; i < Present.Length; i++) if (IsFree(i)) output.Add(i);
        }

        /// <summary>All matching free pairs, ordered by (a,b) ascending with a &lt; b, packed with Pack.</summary>
        public void GetFreePairs(List<int> freeScratch, List<long> output)
        {
            FreeTiles(freeScratch);
            output.Clear();
            for (int i = 0; i < freeScratch.Count; i++)
            {
                int a = freeScratch[i];
                int ga = Kinds.Group(KindAt[a]);
                for (int j = i + 1; j < freeScratch.Count; j++)
                {
                    int b = freeScratch[j];
                    if (Kinds.Group(KindAt[b]) == ga) output.Add(Pack(a, b));
                }
            }
        }

        public bool HasMove()
        {
            for (int i = 0; i < Present.Length; i++)
            {
                if (!IsFree(i)) continue;
                int g = Kinds.Group(KindAt[i]);
                for (int j = i + 1; j < Present.Length; j++)
                    if (IsFree(j) && Kinds.Group(KindAt[j]) == g) return true;
            }
            return false;
        }

        public static long Pack(int a, int b) { return ((long)a << 20) | (long)b; }
        public static int PairA(long p) { return (int)(p >> 20); }
        public static int PairB(long p) { return (int)(p & 0xFFFFF); }

        /// <summary>FNV-1a over layout id, kinds and presence.</summary>
        public ulong Hash()
        {
            ulong h = Mix.Fnv1a64(Layout.Id);
            for (int i = 0; i < KindAt.Length; i++)
            {
                ulong v = Present[i] ? (ulong)(KindAt[i] + 1) : 0UL;
                h = Mix.Fnv1aStep(h, v);
            }
            return h;
        }

        /// <summary>Hash of presence only (used by the solver where kinds never change).</summary>
        public ulong PresenceHash()
        {
            ulong h = 14695981039346656037UL;
            ulong word = 0; int bits = 0;
            for (int i = 0; i < Present.Length; i++)
            {
                word = (word << 1) | (Present[i] ? 1UL : 0UL);
                if (++bits == 64) { h = Mix.Fnv1aStep(h, word); word = 0; bits = 0; }
            }
            return Mix.Fnv1aStep(h, word);
        }
    }
}
