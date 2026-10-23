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
///   A = unused
///
/// Refresh: in play mode a worker thread bakes the WHOLE texture (all 6 rows
/// at one animation time) continuously, and the newest bake is uploaded every
/// frame, so the meander moves as smoothly as everything else in the shader.
/// A 256-texel bake is ~2-4 ms of CPU, which the worker hides entirely.
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
    const int Rows = 6;

    Texture2D tex;
    float[]   front;        // the buffer that was uploaded last
    float[]   back;         // the buffer the worker (or the row loop) writes
    int       nextRow;

    // ---- worker thread ------------------------------------------------------
    Thread         worker;
    AutoResetEvent kick;
    volatile bool  running;
    volatile bool  ready;          // worker: "back holds a finished bake"
    volatile float reqTime, reqNoiseScale;
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

        front = new float[resolution * Rows * 4];
        back  = new float[resolution * Rows * 4];
        for (int i = 0; i < front.Length; i++) { front[i] = 0.5f; back[i] = 0.5f; }   // neutral

        // First bake synchronously so the first frame is already right
        BakeAll(front, resolution, CurrentTime(), CurrentNoiseScale());
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

        float t = CurrentTime(), ns = CurrentNoiseScale();
        bool useThread = threaded && Application.isPlaying && !workerFailed;

        if (useThread)
        {
            if (worker == null) StartWorker(t, ns);
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
                ready = false;
                Upload();
                CountUpload();
                reqTime = t; reqNoiseScale = ns;
                kick.Set();
            }
        }
        else
        {
            if (worker != null) StopWorker();
            int rows = Mathf.Clamp(rowsPerFrame, 1, Rows);
            for (int k = 0; k < rows; k++)
            {
                BakeRow(front, resolution, nextRow, t, ns);
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
    void StartWorker(float t, float ns)
    {
        StopWorker();
        kick    = new AutoResetEvent(false);
        running = true;
        ready   = false;
        reqTime = t; reqNoiseScale = ns;
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
                float[] dst = back;                   // the main thread swapped before kicking
                int res = dst.Length / (Rows * 4);
                sw.Restart();
                BakeAll(dst, res, reqTime, reqNoiseScale);
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
    static void BakeAll(float[] dst, int res, float t, float noiseScale)
    {
        for (int r = 0; r < Rows; r++) BakeRow(dst, res, r, t, noiseScale);
    }

    static void BakeRow(float[] dst, int res, int row, float t, float noiseScale)
    {
        int band = row / 2;
        int hemi = row % 2;
        float seed        = BandSeedOffsets[band];
        float seedMeander = seed + (hemi == 1 ? 500f : 0f);   // south oval is independent
        int   baseIdx     = row * res * 4;
        float invRes      = 1f / res;

        for (int i = 0; i < res; i++)
        {
            // texel centre -> longitude in -pi..pi (shader: atan2(z, x) / 2 pi + 0.5)
            float lon = ((i + 0.5f) * invRes - 0.5f) * Mathf.PI * 2f;
            int o = baseIdx + i * 4;
            dst[o]     = AuroraNoise.BandShift(lon, t, seedMeander, noiseScale) + 0.5f;
            dst[o + 1] = AuroraNoise.Flare01(lon, t, seed, noiseScale);
            dst[o + 2] = AuroraNoise.WidthNoise01(lon, t, seedMeander, noiseScale);
            dst[o + 3] = 0f;
        }
    }

    // ---- main-thread plumbing ---------------------------------------------------
    void Bind()
    {
        if (auroraMaterial != null && tex != null && auroraMaterial.GetTexture("_MeanderTex") != tex)
            auroraMaterial.SetTexture("_MeanderTex", tex);
    }

    float CurrentTime()       => auroraMaterial != null ? auroraMaterial.GetFloat("_AnimTime")   : 0f;
    float CurrentNoiseScale() => auroraMaterial != null ? auroraMaterial.GetFloat("_NoiseScale") : 2.5f;

    void Upload()
    {
        if (tex == null) return;
        tex.SetPixelData(front, 0);
        tex.Apply(false, false);
    }

    static void Log(string m) => Debug.Log($"[RVA:MeanderLUT] {m}");
}
