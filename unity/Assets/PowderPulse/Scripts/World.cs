using System.Collections.Generic;
using UnityEngine;

namespace PowderPulse
{
    public class Kicker { public float z, x, len, h, w; public bool mega; }
    public class Drop { public float z0, z1, d; public int shape; }
    public struct Tree { public float x, z, s; public bool deco; }

    /// <summary>
    /// The endless Free Ride mountain. Features (jump lines, mega kickers, cliffs) are generated
    /// lazily ahead of the rider, the same recipe as the web version, and Ground(x, z) gives the
    /// snow height anywhere. +z is downhill, +x is the rider's right.
    /// </summary>
    public class World
    {
        public const float W = 90f;        // rideable width
        public const float G = 24f;        // gravity (game units)
        const float Bucket = 16f;
        const float Diff = 2f;             // Free Ride difficulty, like the web game

        public readonly float grade = 0.26f + Diff * 0.02f;
        public readonly List<Kicker> kickers = new List<Kicker>();
        public readonly List<Drop> cliffs = new List<Drop>();
        public readonly List<Tree> trees = new List<Tree>();      // sorted by z

        readonly List<Drop> drops = new List<Drop>();             // sorted by z0
        readonly List<float> dropPre = new List<float> { 0f };    // prefix sums of drop depths
        float dropMax;
        readonly Dictionary<int, List<Kicker>> kb = new Dictionary<int, List<Kicker>>();
        readonly System.Random rng;
        readonly float ox1, oz1, ox2, oz2;
        float featureZ = 90f, treeZ = 0f;

        public World(int seed)
        {
            rng = new System.Random(seed);
            ox1 = R() * 500f; oz1 = R() * 500f; ox2 = R() * 500f + 600f; oz2 = R() * 500f + 600f;
        }

        float R() => (float)rng.NextDouble();

        /// <summary>Make sure every feature and tree up to z exists.</summary>
        public void GenerateTo(float z)
        {
            while (featureZ < z + 200f) GenFeature();
            while (treeZ + 64f < featureZ - 160f) { GenTrees(treeZ, treeZ + 64f); treeZ += 64f; }
        }

        void AddKicker(Kicker k)
        {
            kickers.Add(k);
            for (int i = Mathf.FloorToInt(k.z / Bucket); i <= Mathf.FloorToInt((k.z + k.len) / Bucket); i++)
            {
                if (!kb.TryGetValue(i, out var list)) kb[i] = list = new List<Kicker>();
                list.Add(k);
            }
        }

        void AddDrop(Drop d)
        {
            drops.Add(d);
            dropPre.Add(dropPre[dropPre.Count - 1] + d.d);
            dropMax = Mathf.Max(dropMax, d.z1 - d.z0);
        }

        void GenFeature()
        {
            float z = featureZ, r = R();
            if (r < 0.30f)
            {   // jump line: 2-3 kickers in a row
                float x = (R() - .5f) * 40f, size = 2f + R() * 1.5f + Diff * 0.2f;
                int n = 2 + (int)(R() * 2f);
                for (int i = 0; i < n; i++)
                    AddKicker(new Kicker { z = z + i * 46f, x = x + (R() - .5f) * 6f, len = 8f + size * 1.3f, h = size + R() * 0.8f, w = 5f + R() * 2f });
                featureZ += n * 46f + 35f;
            }
            else if (r < 0.52f)
            {   // mega kicker that launches you over a steep landing pitch
                float h = 6.5f + R() * 3f + Diff * 0.35f, len = 19f + R() * 6f, x = (R() - .5f) * 30f;
                AddKicker(new Kicker { z = z, x = x, len = len, h = h, w = 9f + R() * 3f, mega = true });
                float pz = z + len + 8f, plen = 42f + R() * 16f;
                AddDrop(new Drop { z0 = pz, z1 = pz + plen, d = 15f + R() * 8f + Diff * 1.2f, shape = 1 });
                featureZ += len + 130f;
            }
            else if (r < 0.74f)
            {   // big cliff with a steep runout
                float d = 8f + R() * 9f + Diff * 1.6f;
                var cliff = new Drop { z0 = z, z1 = z + 1.5f, d = d, shape = 0 };
                cliffs.Add(cliff);
                AddDrop(cliff);
                AddDrop(new Drop { z0 = z + 4f, z1 = z + 38f, d = d * 0.45f, shape = 1 });
                featureZ += 120f;
            }
            else if (r < 0.88f)
            {   // single medium kicker
                AddKicker(new Kicker { z = z, x = (R() - .5f) * 48f, len = 9f + R() * 5f, h = 2.6f + R() * 2.4f + Diff * 0.25f, w = 5f + R() * 3f });
                featureZ += 70f + R() * 30f;
            }
            else featureZ += 55f;   // open snow
        }

        void GenTrees(float z0, float z1)
        {
            var block = new List<Tree>();
            int count = Mathf.RoundToInt((z1 - z0) * 0.16f * (1f + Diff * 0.25f));
            for (int i = 0; i < count; i++)
            {
                float x = (R() * 2f - 1f) * (W / 2f - 4f), tz = z0 + R() * (z1 - z0);
                if (tz < 140f && Mathf.Abs(x) < 6f) continue;
                if (tz < 40f) continue;
                if (!ClearOfFeatures(x, tz)) continue;
                block.Add(new Tree { x = x, z = tz, s = 0.8f + R() * 0.7f });
            }
            // decoration on the side walls, outside the rideable lane
            for (int i = 0; i < (z1 - z0) * 0.14f; i++)
            {
                float side = R() < .5f ? -1f : 1f;
                block.Add(new Tree { x = side * (W / 2f + 2f + R() * 24f), z = z0 + R() * (z1 - z0), s = 0.9f + R() * 0.9f, deco = true });
            }
            block.Sort((a, b) => a.z.CompareTo(b.z));
            trees.AddRange(block);
        }

        bool ClearOfFeatures(float x, float tz)
        {
            for (int i = kickers.Count - 1; i >= 0; i--)
            {
                var k = kickers[i];
                if (k.z < tz - 200f) break;
                float before = k.mega ? 45f : 28f, after = k.mega ? 75f : 28f, pad = k.mega ? 12f : 5f;
                if (tz > k.z - before && tz < k.z + k.len + after && Mathf.Abs(x - k.x) < k.w + pad) return false;
            }
            for (int i = cliffs.Count - 1; i >= 0; i--)
            {
                var c = cliffs[i];
                if (c.z0 < tz - 60f) break;
                if (tz > c.z0 - 14f && tz < c.z0 + 22f) return false;
            }
            return true;
        }

        static float DropShape(Drop d, float t)
        {
            if (t >= 1f) return 1f;
            if (d.shape == 0) return t;
            return t * t * (3f - 2f * t);
        }

        float DropAt(float z)
        {
            int lo = 0, hi = drops.Count;
            while (lo < hi) { int m = (lo + hi) >> 1; if (drops[m].z0 < z) lo = m + 1; else hi = m; }
            float total = dropPre[lo];                      // every drop that has started
            for (int j = lo - 1; j >= 0 && z - drops[j].z0 < dropMax; j--)
            {
                var d = drops[j];
                if (z < d.z1) total -= d.d * (1f - DropShape(d, (z - d.z0) / (d.z1 - d.z0)));
            }
            return total;
        }

        float Noise1(float x, float z) => (Mathf.PerlinNoise(x + ox1, z + oz1) - 0.5f) * 2f;
        float Noise2(float x, float z) => (Mathf.PerlinNoise(x + ox2, z + oz2) - 0.5f) * 2f;

        /// <summary>Snow height at (x, z).</summary>
        public float Ground(float x, float z)
        {
            float h = -z * grade + Noise1(x * 0.025f, z * 0.02f) * 5f + Noise2(x * 0.08f, z * 0.08f) * 1.2f;
            float ax = Mathf.Abs(x), e = W / 2f - 12f;
            if (ax > e) { float d = ax - e; h += d * d * 0.09f; }    // steep side walls
            if (kb.TryGetValue(Mathf.FloorToInt(z / Bucket), out var b))
            {
                for (int i = 0; i < b.Count; i++)
                {
                    var k = b[i];
                    float t = (z - k.z) / k.len;
                    if (t <= 0f || t >= 1f) continue;
                    float dx = Mathf.Abs(x - k.x);
                    if (dx < k.w) { float f = dx < k.w - 2f ? 1f : (k.w - dx) / 2f; h += k.h * t * t * f; }
                }
            }
            return h - DropAt(z);
        }

        /// <summary>True if taking off here should get the big-air slow motion.</summary>
        public bool IsBigAirLaunch(float x, float z)
        {
            for (int i = kickers.Count - 1; i >= 0; i--)
            {
                var k = kickers[i];
                if (k.z < z - 80f) break;
                if (k.mega && Mathf.Abs(x - k.x) < k.w + 1f && z > k.z + k.len * 0.6f && z < k.z + k.len + 2f) return true;
            }
            for (int i = cliffs.Count - 1; i >= 0; i--)
            {
                var c = cliffs[i];
                if (c.z0 < z - 20f) break;
                if (c.d > 9f && z > c.z0 - 6f && z < c.z0 + 2f) return true;
            }
            return false;
        }

        /// <summary>Index of the first tree at or after z.</summary>
        public int FirstTree(float z)
        {
            int lo = 0, hi = trees.Count;
            while (lo < hi) { int m = (lo + hi) >> 1; if (trees[m].z < z) lo = m + 1; else hi = m; }
            return lo;
        }
    }
}
