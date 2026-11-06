using System.Collections.Generic;
using UnityEngine;

namespace PowderPulse
{
    /// <summary>Builds and recycles the mountain around the rider in 64 m chunks: snow mesh, trees, kicker lips.</summary>
    public class TerrainStreamer
    {
        const float ChunkLen = 64f, HalfW = 72f, Step = 1f;
        const int NX = (int)(HalfW * 2f / Step) + 1, NZ = (int)(ChunkLen / Step) + 1;

        readonly World w;
        readonly Materials m;
        readonly Mesh pine;
        readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();
        readonly Transform parent;
        readonly List<int> drop = new List<int>();

        public TerrainStreamer(World world, Materials mats)
        {
            w = world; m = mats;
            pine = MeshKit.Pine();
            parent = new GameObject("Mountain").transform;
        }

        public void Update(float riderZ)
        {
            int c0 = Mathf.FloorToInt((riderZ - 70f) / ChunkLen), c1 = Mathf.FloorToInt((riderZ + 460f) / ChunkLen);
            drop.Clear();
            foreach (var kv in chunks) if (kv.Key < c0 || kv.Key > c1) drop.Add(kv.Key);
            foreach (int k in drop) { DestroyChunk(chunks[k]); chunks.Remove(k); }
            // build at most one chunk per frame so a new mountain never hitches
            for (int c = c0; c <= c1; c++) if (!chunks.ContainsKey(c)) { chunks[c] = Build(c); break; }
        }

        public void BuildAllNow(float riderZ)
        {
            for (int i = 0; i < 12; i++) Update(riderZ);
        }

        static void DestroyChunk(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf) Object.Destroy(mf.sharedMesh);
            Object.Destroy(go);
        }

        GameObject Build(int ci)
        {
            float z0 = ci * ChunkLen;
            var go = new GameObject("Chunk " + ci);
            go.transform.SetParent(parent, false);

            var verts = new Vector3[NX * NZ];
            for (int iz = 0; iz < NZ; iz++)
            {
                float z = z0 + iz * Step;
                for (int ix = 0; ix < NX; ix++)
                {
                    float x = -HalfW + ix * Step;
                    verts[iz * NX + ix] = new Vector3(x, w.Ground(x, z), z);
                }
            }
            var tris = new int[(NX - 1) * (NZ - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < NZ - 1; iz++)
                for (int ix = 0; ix < NX - 1; ix++)
                {
                    int a = iz * NX + ix, b = a + 1, c = a + NX, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "Snow " + ci };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m.snow;

            // trees
            var trees = w.trees;
            for (int i = w.FirstTree(z0); i < trees.Count && trees[i].z < z0 + ChunkLen; i++)
            {
                var tr = trees[i];
                if (Mathf.Abs(tr.x) > HalfW + 30f) continue;
                var tg = new GameObject("Tree");
                tg.transform.SetParent(go.transform, false);
                tg.transform.localPosition = new Vector3(tr.x, w.Ground(tr.x, tr.z) - 0.2f, tr.z);
                tg.transform.localRotation = Quaternion.Euler(0f, (tr.x * 37.1f + tr.z * 13.7f) % 360f, 0f);
                tg.transform.localScale = Vector3.one * tr.s;
                tg.AddComponent<MeshFilter>().sharedMesh = pine;
                tg.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.trunk, m.pine, m.pineSnow };
            }

            // kicker lips: a dark stripe so you can read the takeoff from far away
            foreach (var k in w.kickers)
            {
                float ze = k.z + k.len;
                if (ze < z0 || ze >= z0 + ChunkLen) continue;
                var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(lip.GetComponent<Collider>());
                lip.transform.SetParent(go.transform, false);
                lip.transform.localPosition = new Vector3(k.x, w.Ground(k.x, ze - 0.05f) - 0.1f, ze - 0.15f);
                lip.transform.localScale = new Vector3(k.w * 2f - 1.5f, 0.24f, 0.3f);
                lip.GetComponent<MeshRenderer>().sharedMaterial = k.mega ? m.lipMega : m.lip;
            }
            return go;
        }
    }

    public static class MeshKit
    {
        /// <summary>A low-poly pine: trunk + three stacked cones with snowy tips (3 submeshes).</summary>
        public static Mesh Pine()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>();
            var trunk = new List<int>(); var needles = new List<int>(); var snow = new List<int>();
            Cone(v, n, trunk, 0f, 1.4f, 0.28f, 0.2f, 7);
            Cone(v, n, needles, 0.9f, 3.9f, 2.2f, 0f, 9);
            Cone(v, n, needles, 2.5f, 5.4f, 1.7f, 0f, 9);
            Cone(v, n, snow, 4.0f, 7.0f, 1.15f, 0f, 9);
            var mesh = new Mesh { name = "Pine" };
            mesh.SetVertices(v); mesh.SetNormals(n);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(trunk, 0); mesh.SetTriangles(needles, 1); mesh.SetTriangles(snow, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Cone(List<Vector3> v, List<Vector3> n, List<int> tri, float y0, float y1, float r0, float r1, int seg)
        {
            int start = v.Count;
            float h = y1 - y0, slant = (r0 - r1) / h;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var norm = (dir + Vector3.up * slant).normalized;
                v.Add(dir * r0 + Vector3.up * y0); n.Add(norm);
                v.Add(dir * r1 + Vector3.up * y1); n.Add(r1 > 0f ? norm : Vector3.up);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 2, d = a + 3;
                tri.Add(a); tri.Add(b); tri.Add(c);
                tri.Add(c); tri.Add(b); tri.Add(d);
            }
            // underside cap so the outline reads from below
            int center = v.Count; v.Add(Vector3.up * y0); n.Add(Vector3.down);
            for (int i = 0; i < seg; i++) { tri.Add(center); tri.Add(start + i * 2); tri.Add(start + i * 2 + 2); }
        }
    }
}
