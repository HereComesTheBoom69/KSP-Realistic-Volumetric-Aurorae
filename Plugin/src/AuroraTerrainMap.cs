// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// A height map of a body's terrain, and the sphere the occluder model is drawn with
    /// once the map is ready (1.1.0).
    ///
    /// KSP unloads a body's terrain mesh when the camera climbs past the body's PQS limit
    /// (120 km on the Mun) and shows its scaled-space model instead, which is a smooth
    /// sphere with the relief painted on. Seen from up there, another body's aurora was
    /// cut at the datum, a degree or so under the ridges on the limb. The terrain's height
    /// function (PQS.GetSurfaceHeight, which CelestialBody.TerrainAltitude wraps) never
    /// switches off, so this class samples it into a 1024 x 512 map of the body, in
    /// budgeted slices of a few milliseconds a frame, and keeps the result in
    /// PluginData/terrain/ for later sessions. The renderer then draws a dense sphere
    /// whose vertices the map pushes out, so the model's silhouette carries the real
    /// highlands at any altitude. Until a map is ready the scaled-space model stands in.
    /// The release ships the maps of the stock bodies that need one (built this way and
    /// copied into the kit), so a map is built in play only for a body the release does
    /// not cover, such as a planet pack's. Each file carries the terrain's height at 48
    /// fixed points; a map whose points no longer match the terrain in the game (a pack
    /// that rewrote a stock body, a mod that changed its heights) is rebuilt, not used.
    ///
    /// Frames: the map is indexed by KSP's own latitude and longitude, and the sphere's
    /// vertices sit at those same latitudes and longitudes in a fixed frame; the rotation
    /// that takes that frame into the world is built from three surface normals KSP gives
    /// for (0, 0), (90, 0) and (0, 90), so the map and the mesh can never disagree on
    /// where a mountain is.
    /// </summary>
    public class AuroraTerrainMap
    {
        public const int   Width  = 1024, Height = 512;
        public const int   SphereCols = 512, SphereRows = 256;
        /// <summary>Main-thread milliseconds a frame spent sampling the terrain while a map builds.</summary>
        public static float BudgetMs = 2.5f;
        const string LogPrefix = "[RVA:Terrain]";
        const int    FileVersion = 3;              // 2: int16 heights with a per-file scale (1 MB a body); 3: + a fingerprint of the terrain
        const int    FingerprintSamples = 48;      // fixed points re-sampled on load; a terrain that differs there is a different terrain

        public readonly CelestialBody body;
        public readonly float[] heights = new float[Width * Height];   // metres above the datum; row 0 = latitude -90, column 0 = longitude -180
        public Texture2D Texture { get; private set; }
        public bool  Ready  { get; private set; }
        public bool  Failed { get; private set; }
        public float MinHeight { get; private set; }
        public float MaxHeight { get; private set; }
        public float Progress => (float)next / heights.Length;
        public string Source { get; private set; } = "building";

        int   next;
        float startedAt = -1f;

        static readonly Dictionary<string, AuroraTerrainMap> maps = new Dictionary<string, AuroraTerrainMap>();
        static Mesh sphere;
        static int  workFrame = -1;
        static readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        AuroraTerrainMap(CelestialBody b) { body = b; }

        /// <summary>The map for a body: loaded from PluginData/terrain/ when a matching file is
        /// there, otherwise started (Work() carries it on). Null for a body without terrain.</summary>
        /// <summary>True once a map exists for the body (ready, building or failed).</summary>
        public static bool Has(CelestialBody b) => b != null && maps.ContainsKey(b.bodyName);

        /// <summary>The body's map if one exists (ready, building or failed), without starting one.</summary>
        public static AuroraTerrainMap Peek(CelestialBody b) => b != null && maps.TryGetValue(b.bodyName, out AuroraTerrainMap m) ? m : null;

        static readonly HashSet<string> noTerrainLogged = new HashSet<string>();

        public static AuroraTerrainMap Get(CelestialBody b)
        {
            if (b == null) return null;
            if (b.pqsController == null)
            {
                // a gas giant: no terrain to map, and KSP's scaled-space model of it is exact as it is
                if (noTerrainLogged.Add(b.bodyName)) Log($"{b.bodyName}: no terrain (no PQS), nothing to map; its scaled-space model is the ground test as it is");
                return null;
            }
            if (maps.TryGetValue(b.bodyName, out AuroraTerrainMap m)) return m;
            m = new AuroraTerrainMap(b);
            maps[b.bodyName] = m;
            if (m.TryLoad()) m.Finish("from " + m.Source);
            else Log($"{b.bodyName}: building a {Width}x{Height} terrain map ({BudgetMs:F1} ms a frame)");
            return m;
        }

        /// <summary>Called every frame by whoever holds a map; samples one unfinished map for BudgetMs.</summary>
        public static void Work()
        {
            if (Time.frameCount == workFrame) return;
            workFrame = Time.frameCount;
            foreach (var kv in maps)
            {
                AuroraTerrainMap m = kv.Value;
                if (m.Ready || m.Failed) continue;
                m.Step();
                return;                                 // one map at a time
            }
        }

        void Step()
        {
            if (startedAt < 0f) startedAt = Time.realtimeSinceStartup;
            watch.Restart();
            try
            {
                while (next < heights.Length)
                {
                    int u = next % Width, v = next / Width;
                    double lat = (v + 0.5) / Height * 180.0 - 90.0;
                    double lon = (u + 0.5) / Width * 360.0 - 180.0;
                    // the sea surface counts as ground (no negative altitudes), like the terrain scan;
                    // clamped to six tenths of the radius either way in case a body's height function
                    // misbehaves (Bop's mountains are a third of its radius, so a tenth was too tight)
                    double h = body.TerrainAltitude(lat, lon, false);
                    double cap = body.Radius * 0.6;
                    if (double.IsNaN(h) || double.IsInfinity(h)) h = 0.0;
                    heights[next] = (float)(h > cap ? cap : h < -cap ? -cap : h);
                    next++;
                    if ((next & 63) == 0 && watch.Elapsed.TotalMilliseconds >= BudgetMs) break;
                }
            }
            catch (System.Exception e)
            {
                Failed = true;
                Debug.LogWarning($"{LogPrefix} {body.bodyName}: terrain map stopped at {Progress * 100f:F0} % ({e.GetType().Name}: {e.Message}); the scaled-space model stays in use");
                return;
            }
            if (next >= heights.Length)
            {
                Source = $"built in {Time.realtimeSinceStartup - startedAt:F0} s";
                Save();
                Finish(Source);
            }
        }

        void Finish(string how)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < heights.Length; i++) { float h = heights[i]; if (h < lo) lo = h; if (h > hi) hi = h; }
            MinHeight = lo; MaxHeight = hi;
            Texture = new Texture2D(Width, Height, TextureFormat.RFloat, false, true)
            {
                name = "RVA_terrain_" + body.bodyName,
                wrapModeU  = TextureWrapMode.Repeat,     // longitude wraps
                wrapModeV  = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            byte[] raw = new byte[heights.Length * 4];
            System.Buffer.BlockCopy(heights, 0, raw, 0, raw.Length);
            Texture.LoadRawTextureData(raw);
            Texture.Apply(false, true);                  // no mips, and the CPU copy can go
            Ready = true;
            Log($"{body.bodyName}: terrain map {Width}x{Height} ready ({how}), surface from datum {lo:+0;-0} m to datum {hi:+0;-0} m");
        }

        // ---- the cache on disk: PluginData/terrain/<body>.rvaterrain --------------------------
        static string CacheDir => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/PluginData/terrain");
        string CachePath => Path.Combine(CacheDir, body.bodyName + ".rvaterrain");

        /// <summary>The terrain at 48 fixed points spread over the body: a map built for a
        /// different terrain (a planet pack rewriting a stock body, a mod changing its
        /// heights) differs here and is rebuilt instead of trusted.</summary>
        float[] Fingerprint()
        {
            var f = new float[FingerprintSamples];
            for (int i = 0; i < FingerprintSamples; i++)
            {
                double z   = 1.0 - 2.0 * (i + 0.5) / FingerprintSamples;          // a Fibonacci sphere
                double phi = i * 2.399963229728653;
                double lat = System.Math.Asin(z) * 180.0 / System.Math.PI;
                double lon = ((phi % (2.0 * System.Math.PI)) / System.Math.PI) * 180.0 - 180.0;
                f[i] = (float)body.TerrainAltitude(lat, lon, false);
            }
            return f;
        }

        bool TryLoad()
        {
            try
            {
                string p = CachePath;
                if (!File.Exists(p)) return false;
                using (var br = new BinaryReader(File.OpenRead(p)))
                {
                    if (br.ReadInt32() != 0x54415652) return false;              // "RVAT"
                    if (br.ReadInt32() != FileVersion) return false;
                    int w = br.ReadInt32(), h = br.ReadInt32();
                    double radius = br.ReadDouble();
                    if (w != Width || h != Height || System.Math.Abs(radius - body.Radius) > 0.5) return false;
                    float[] now = Fingerprint();
                    for (int i = 0; i < FingerprintSamples; i++)
                    {
                        float stored = br.ReadSingle();
                        if (System.Math.Abs(stored - now[i]) > 2f)
                        {
                            Log($"{body.bodyName}: the terrain map on disk is for a different terrain ({stored:F0} m against {now[i]:F0} m at check point {i}); building a new one");
                            return false;
                        }
                    }
                    float scale = br.ReadSingle();                                 // metres per unit
                    byte[] raw = br.ReadBytes(heights.Length * 2);
                    if (raw.Length != heights.Length * 2) return false;
                    short[] q = new short[heights.Length];
                    System.Buffer.BlockCopy(raw, 0, q, 0, raw.Length);
                    float top = float.MinValue;
                    for (int i = 0; i < heights.Length; i++) { heights[i] = q[i] * scale; if (heights[i] > top) top = heights[i]; }
                    // the map must hold its own check points: a map built with a tighter cap on the
                    // heights than the terrain needed (Bop, Pol on the first build) tops out under them
                    for (int i = 0; i < FingerprintSamples; i++)
                        if (now[i] > top + 1f)
                        {
                            Log($"{body.bodyName}: the terrain map on disk tops out at datum +{top:F0} m, under its own check point {i} ({now[i]:F0} m); building a new one");
                            return false;
                        }
                }
                next = heights.Length;
                Source = "PluginData/terrain";
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LogPrefix} {body.bodyName}: terrain map file not used ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                float peak = 1f;
                for (int i = 0; i < heights.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(heights[i]));
                float scale = peak / 32000f;                                       // int16 range, a quarter metre on the Mun
                short[] q = new short[heights.Length];
                for (int i = 0; i < heights.Length; i++) q[i] = (short)Mathf.Clamp(Mathf.RoundToInt(heights[i] / scale), -32000, 32000);
                using (var bw = new BinaryWriter(File.Create(CachePath)))
                {
                    bw.Write(0x54415652); bw.Write(FileVersion);
                    bw.Write(Width); bw.Write(Height);
                    bw.Write(body.Radius);
                    float[] fp = Fingerprint();
                    for (int i = 0; i < FingerprintSamples; i++) bw.Write(fp[i]);
                    bw.Write(scale);
                    byte[] raw = new byte[heights.Length * 2];
                    System.Buffer.BlockCopy(q, 0, raw, 0, raw.Length);
                    bw.Write(raw);
                }
                Log($"{body.bodyName}: terrain map saved to PluginData/terrain/{body.bodyName}.rvaterrain ({heights.Length * 2 / 1024} KB)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LogPrefix} {body.bodyName}: terrain map not saved ({e.GetType().Name}: {e.Message}); it will be built again next session");
            }
        }

        // ---- the sphere the model is drawn with -----------------------------------------------
        /// <summary>A unit sphere, SphereCols x SphereRows, whose vertex (lat, lon) sits at
        /// (cos lat cos lon, sin lat, cos lat sin lon) with uv = ((lon + 180) / 360, (lat + 90) / 180):
        /// the map's own coordinates. The pass pushes each vertex out by the map's height.</summary>
        public static Mesh Sphere
        {
            get
            {
                if (sphere != null) return sphere;
                int cols = SphereCols, rows = SphereRows;
                var verts = new Vector3[(cols + 1) * (rows + 1)];
                var uvs   = new Vector2[verts.Length];
                for (int j = 0; j <= rows; j++)
                {
                    double lat = (double)j / rows * System.Math.PI - System.Math.PI * 0.5;
                    double cl = System.Math.Cos(lat), sl = System.Math.Sin(lat);
                    for (int i = 0; i <= cols; i++)
                    {
                        double lon = (double)i / cols * 2.0 * System.Math.PI - System.Math.PI;
                        int k = j * (cols + 1) + i;
                        verts[k] = new Vector3((float)(cl * System.Math.Cos(lon)), (float)sl, (float)(cl * System.Math.Sin(lon)));
                        uvs[k]   = new Vector2((float)i / cols, (float)j / rows);
                    }
                }
                var tris = new int[cols * rows * 6];
                int t = 0;
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols; i++)
                    {
                        int a = j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                sphere = new Mesh { name = "RVA_terrain_sphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                sphere.vertices  = verts;
                sphere.uv        = uvs;
                sphere.triangles = tris;
                sphere.bounds    = new Bounds(Vector3.zero, Vector3.one * 4f);
                sphere.UploadMeshData(true);
                return sphere;
            }
        }

        /// <summary>The rotation that takes the sphere's frame into the world right now: its
        /// x, y and z axes are KSP's surface normals at (0, 0), the north pole and (0, 90).
        /// Rotation only, no scale; the pass applies the datum radius and the map's height.</summary>
        public static Matrix4x4 Rotation(CelestialBody b)
        {
            Vector3 x = ((Vector3)b.GetSurfaceNVector(0.0, 0.0)).normalized;
            Vector3 y = ((Vector3)b.GetSurfaceNVector(90.0, 0.0)).normalized;
            Vector3 z = ((Vector3)b.GetSurfaceNVector(0.0, 90.0)).normalized;
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            return m;
        }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
