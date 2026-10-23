// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Bakes the aurora's meander shift, flare factor and width noise into a small
/// lookup texture (longitude x [band, hemisphere]) and feeds it to the aurora
/// material as _MeanderTex.
///
/// Layout: width = resolution (longitude, -pi..pi wrapping), height = 6 rows:
///   row = band * 2 + hemisphere   (band 0..2, hemisphere 0 = north, 1 = south)
///   R = meander shift + 0.5       (shift is +/-0.225 in latNorm units)
///   G = flare 0..1                (shader: flare = lerp(0.7, 1.6, G))
///   B = width noise 0..1          (shader: width x lerp(1-var, 1+var, B))
///   A = where the folded ribbon is in this longitude column (latNorm toward the
///       set's pole; 1.1.0): the presence-weighted mean latitude over the fold's
///       reach (AuroraNoise.RibbonPresence), with the amount of ribbon the column
///       holds kept beside it in a CPU-only array (1 = one straight crossing).
///       Not read by the shader (its march finds the fold per sample); the vessel
///       light reads both to know where the ribbons are without evaluating the
///       fold itself.
///
/// Refresh: in play mode a worker thread bakes the WHOLE texture (all 6 rows
/// at one animation time) continuously, and the newest bake is uploaded every
/// frame, so the meander moves as smoothly as everything else in the shader.
/// A 256-texel bake is ~2-4 ms of CPU (about twice that with the ribbon latitudes of
/// 1.1.0), which the worker hides entirely.
/// Without the thread (editor, or threaded = false) rowsPerFrame rows are
/// recomputed per frame on the main thread, the way v3 did it.
///
/// Attach to the same object as the aurora material user (the KSP plugin
/// does this automatically; in the Unity test scene add it to the Aurora
/// object and assign the material). Without it the shader's default gray
/// texture gives zero meander and a neutral flare: it still renders, just
/// straighter bands.
/// </summary>
[ExecuteAlways]
public class AuroraMeanderTexture : MonoBehaviour
{
    public Material auroraMaterial;

    [Tooltip("Longitude samples around the planet. 256 ~ 1.4° per texel.")]
    [Range(32, 1024)] public int resolution = 256;

    [Tooltip("Play mode: bake the whole LUT on a worker thread and upload the newest bake every frame.")]
    public bool threaded = true;

    [Tooltip("Rows recomputed per frame when NOT threaded (6 rows total).")]
    [Range(1, 6)] public int rowsPerFrame = 1;

    // Must match the shader's auroraField()
    static readonly float[] BandSeedOffsets = { 0.00f, 73.41f, 218.93f };
    static readonly float[] BandLatOffsets  = { 0.00f, -0.05f, +0.04f };
    const int Rows = 6;

    /// <summary>Everything a bake needs besides the longitude: the animation time and the
    /// band's placement and folds as the material has them this frame (1.1.0: the A channel).</summary>
    struct BakeParams
    {
        public float t, noiseScale, latCenter, spacing, foldStrength, foldScale, independence;
    }

    Texture2D tex;
    float[]   front;        // the buffer that was uploaded last
    float[]   back;         // the buffer the worker (or the row loop) writes
    float[]   wFront, wBack; // the ribbon weight per texel (res x Rows), CPU only, swapped with front / back
    int       nextRow;

    // ---- worker thread ------------------------------------------------------
    Thread         worker;
    AutoResetEvent kick;
    volatile bool  running;
    volatile bool  ready;          // worker: "back holds a finished bake"
    BakeParams     reqParams;          // written by the main thread before each kick, read by the worker after it (the event is the fence)
    volatile float lastBakeMs;
    volatile string workerError;
    bool  workerFailed;
    int   uploadsThisSecond, uploadsPerSecond;
    float secondStart;

    public Texture2D Texture => tex;
    /// <summary>Time.frameCount of the last upload to the GPU, and the worker's last bake time (diagnostics).</summary>
    public int   LastUploadFrame { get; private set; }
    public float LastBakeMs      => lastBakeMs;
    public string Status
    {
        get
        {
            if (tex == null) return "no texture";
            bool th = threaded && Application.isPlaying && !workerFailed;
            return th ? $"{tex.width}x{Rows}, worker thread, bake {lastBakeMs:F1} ms, {uploadsPerSecond} uploads/s"
                      : $"{tex.width}x{Rows}, main thread, {Mathf.Clamp(rowsPerFrame, 1, Rows)} row(s)/frame" + (workerFailed ? " (worker failed: " + workerError + ")" : "");
        }
    }

    void OnEnable()  { Allocate(); }
    void OnDisable() { StopWorker(); Release(); }

    void Allocate()
    {
        StopWorker();
        Release();
        resolution = Mathf.Clamp(resolution, 32, 1024);
        tex = new Texture2D(resolution, Rows, TextureFormat.RGBAFloat, false, true);
        tex.name       = "RVA_MeanderLUT";
        tex.wrapModeU  = TextureWrapMode.Repeat;
        tex.wrapModeV  = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags  = HideFlags.HideAndDontSave;

        front  = new float[resolution * Rows * 4];
        back   = new float[resolution * Rows * 4];
        wFront = new float[resolution * Rows];
        wBack  = new float[resolution * Rows];
        for (int i = 0; i < front.Length; i++) { front[i] = 0.5f; back[i] = 0.5f; }   // neutral
        for (int i = 0; i < wFront.Length; i++) { wFront[i] = 1f; wBack[i] = 1f; }

        // First bake synchronously so the first frame is already right
        BakeAll(front, wFront, resolution, CurrentParams());
        nextRow = 0;
        Upload();
        Bind();
    }

    void Release()
    {
        if (tex == null) return;
        if (Application.isPlaying) Destroy(tex); else DestroyImmediate(tex);
        tex = null;
    }

    void Update()
    {
        if (auroraMaterial == null) return;
        if (tex == null || tex.width != Mathf.Clamp(resolution, 32, 1024)) { Allocate(); return; }

        BakeParams bp = CurrentParams();
        bool useThread = threaded && Application.isPlaying && !workerFailed;

        if (useThread)
        {
            if (worker == null) StartWorker(bp);
            else if (!worker.IsAlive && running)
            {
                // the worker died (exception): fall back to the main thread, say so once
                workerFailed = true;
                Log("Meander worker thread stopped" + (workerError != null ? ": " + workerError : "") + ", baking on the main thread instead");
                StopWorker();
            }

            if (ready)
            {
                // consume the finished bake, then hand the worker the other buffer
                var tmp = front; front = back; back = tmp;
                var tmpW = wFront; wFront = wBack; wBack = tmpW;
                ready = false;
                Upload();
                CountUpload();
                reqParams = bp;
                kick.Set();
            }
        }
        else
        {
            if (worker != null) StopWorker();
            int rows = Mathf.Clamp(rowsPerFrame, 1, Rows);
            for (int k = 0; k < rows; k++)
            {
                BakeRow(front, wFront, resolution, nextRow, bp);
                nextRow = (nextRow + 1) % Rows;
            }
            Upload();
            CountUpload();
        }
        Bind();
    }

    void CountUpload()
    {
        LastUploadFrame = Time.frameCount;
        uploadsThisSecond++;
        float now = Time.unscaledTime;
        if (now - secondStart >= 1f) { uploadsPerSecond = uploadsThisSecond; uploadsThisSecond = 0; secondStart = now; }
    }

    // ---- worker -------------------------------------------------------------
    void StartWorker(BakeParams bp)
    {
        StopWorker();
        kick    = new AutoResetEvent(false);
        running = true;
        ready   = false;
        reqParams = bp;
        worker  = new Thread(WorkerLoop) { IsBackground = true, Name = "RealisticVolumetricAurorae meander LUT", Priority = System.Threading.ThreadPriority.BelowNormal };
        worker.Start();
        kick.Set();                                   // first bake straight away
    }

    void StopWorker()
    {
        if (worker == null) return;
        running = false;
        try { kick.Set(); worker.Join(1000); } catch { }
        worker = null;
        try { kick.Dispose(); } catch { }
        kick  = null;
        ready = false;
    }

    void WorkerLoop()
    {
        var sw = new Stopwatch();
        try
        {
            while (running)
            {
                kick.WaitOne();
                if (!running) break;
                float[] dst = back, wdst = wBack;     // the main thread swapped before kicking
                int res = dst.Length / (Rows * 4);
                sw.Restart();
                BakeAll(dst, wdst, res, reqParams);
                sw.Stop();
                lastBakeMs = (float)sw.Elapsed.TotalMilliseconds;
                ready = true;                         // volatile: publishes dst's contents
            }
        }
        catch (Exception e)
        {
            workerError = e.GetType().Name + ": " + e.Message;
        }
    }

    // ---- baking (pure, thread-safe) -------------------------------------------
    static void BakeAll(float[] dst, float[] wdst, int res, BakeParams bp)
    {
        for (int r = 0; r < Rows; r++) BakeRow(dst, wdst, res, r, bp);
    }

    static void BakeRow(float[] dst, float[] wdst, int res, int row, BakeParams bp)
    {
        int band = row / 2;
        int hemi = row % 2;
        float seed        = BandSeedOffsets[band];
        float seedMeander = seed + (hemi == 1 ? 500f : 0f);   // south oval is independent
        float cLat        = bp.latCenter + BandLatOffsets[band] * bp.spacing;
        int   baseIdx     = row * res * 4;
        float invRes      = 1f / res;

        for (int i = 0; i < res; i++)
        {
            // texel centre -> longitude in -pi..pi (shader: atan2(z, x) / 2 pi + 0.5)
            float lon = ((i + 0.5f) * invRes - 0.5f) * Mathf.PI * 2f;
            int o = baseIdx + i * 4;
            float shift = AuroraNoise.BandShift(lon, bp.t, seedMeander, bp.noiseScale);
            dst[o]     = shift + 0.5f;
            dst[o + 1] = AuroraNoise.Flare01(lon, bp.t, seed, bp.noiseScale);
            dst[o + 2] = AuroraNoise.WidthNoise01(lon, bp.t, seedMeander, bp.noiseScale);
            // where the folded ribbon is in this column and how much of it (1.1.0, for the vessel
            // light): a presence-weighted scan over the fold's reach, not a fixed-point step (the
            // fold's slope in latitude is far above 1, so iterating lat <- mean - fold(lat) bounces
            // between unrelated points) and not the nearest crossing either (a crossing pair being
            // born or dying switched the column to another strand; either way the light flickered).
            // Every other texel (a 12-step scan); the ones between take the mean of their
            // neighbours below, a light does not need the fold to the texel
            if ((i & 1) == 0)
            {
                float latEff, w;
                AuroraNoise.RibbonPresence(lon, cLat + shift, bp.t, hemi, bp.foldStrength, bp.foldScale, seed, bp.independence, 12, out latEff, out w);
                dst[o + 3] = latEff;
                wdst[row * res + i] = w;
            }
        }
        for (int i = 1; i < res; i += 2)
        {
            int o = baseIdx + i * 4;
            int prev = baseIdx + (i - 1) * 4, next = baseIdx + ((i + 1) % res) * 4;
            dst[o + 3] = 0.5f * (dst[prev + 3] + dst[next + 3]);
            wdst[row * res + i] = 0.5f * (wdst[row * res + i - 1] + wdst[row * res + (i + 1) % res]);
        }
    }

    /// <summary>The LUT as last uploaded, read on the CPU (main thread): meander shift (latNorm),
    /// flare 0..1, width noise 0..1, the folded ribbon's latitude (latNorm toward the set's pole)
    /// and how much ribbon the column holds (1 = one straight crossing) for a band (0..2),
    /// hemisphere (0 north, 1 south) and longitude (radians), linearly interpolated between
    /// texels as the shader samples it. The vessel light reads the ribbons' positions from here (1.1.0).</summary>
    public bool Sample(int band, int hemi, float longitudeRad, out float shift, out float flare01, out float width01, out float ribbonLat, out float ribbonWeight)
    {
        shift = 0f; flare01 = 0.5f; width01 = 0.5f; ribbonLat = 0f; ribbonWeight = 1f;
        if (front == null || wFront == null || tex == null) return false;
        int res = tex.width;
        if (res < 2 || front.Length < res * Rows * 4) return false;
        int row = Mathf.Clamp(band, 0, 2) * 2 + Mathf.Clamp(hemi, 0, 1);
        // texel i's centre is longitude ((i + 0.5) / res - 0.5) x 2 pi
        float u  = (longitudeRad / (Mathf.PI * 2f) + 0.5f) * res - 0.5f;
        u -= Mathf.Floor(u / res) * res;                        // wrap
        int   i0 = Mathf.FloorToInt(u) % res, i1 = (i0 + 1) % res;
        float f  = u - Mathf.Floor(u);
        int   o0 = (row * res + i0) * 4, o1 = (row * res + i1) * 4;
        shift     = Mathf.Lerp(front[o0],     front[o1],     f) - 0.5f;
        flare01   = Mathf.Lerp(front[o0 + 1], front[o1 + 1], f);
        width01   = Mathf.Lerp(front[o0 + 2], front[o1 + 2], f);
        ribbonLat = Mathf.Lerp(front[o0 + 3], front[o1 + 3], f);
        ribbonWeight = Mathf.Lerp(wFront[row * res + i0], wFront[row * res + i1], f);
        return true;
    }

    // ---- main-thread plumbing ---------------------------------------------------
    void Bind()
    {
        if (auroraMaterial != null && tex != null && auroraMaterial.GetTexture("_MeanderTex") != tex)
            auroraMaterial.SetTexture("_MeanderTex", tex);
    }

    BakeParams CurrentParams()
    {
        var bp = new BakeParams { noiseScale = 2.5f, latCenter = 0.65f, spacing = 1f, foldStrength = 0f, foldScale = 1f };
        if (auroraMaterial == null) return bp;
        bp.t            = auroraMaterial.GetFloat("_AnimTime");
        bp.noiseScale   = auroraMaterial.GetFloat("_NoiseScale");
        bp.latCenter    = auroraMaterial.GetFloat("_LatCenter");
        bp.spacing      = auroraMaterial.GetFloat("_BandSpacing");
        bp.foldStrength = auroraMaterial.GetFloat("_FoldStrength");
        bp.foldScale    = auroraMaterial.GetFloat("_FoldScale");
        bp.independence = auroraMaterial.GetFloat("_BandIndependence");
        return bp;
    }

    void Upload()
    {
        if (tex == null) return;
        tex.SetPixelData(front, 0);
        tex.Apply(false, false);
    }

    static void Log(string m) => Debug.Log($"[RVA:MeanderLUT] {m}");
}
