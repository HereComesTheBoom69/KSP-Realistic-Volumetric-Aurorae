// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

[ExecuteAlways]
public class AuroraMeanderTexture : MonoBehaviour
{
    public Material auroraMaterial;

    [Range(32, 1024)] public int resolution = 256;

    public bool threaded = true;

    [Range(1, 6)] public int rowsPerFrame = 1;

    static readonly float[] BandSeedOffsets = { 0.00f, 73.41f, 218.93f };
    static readonly float[] BandLatOffsets  = { 0.00f, -0.05f, +0.04f };
    const int Rows = 6;

    struct BakeParams
    {
        public float t, noiseScale, latCenter, spacing, foldStrength, foldScale, independence;
    }

    Texture2D tex;
    float[]   front;
    float[]   back;
    float[]   wFront, wBack;
    int       nextRow;

    Thread         worker;
    AutoResetEvent kick;
    volatile bool  running;
    volatile bool  ready;
    BakeParams     reqParams;
    volatile float lastBakeMs;
    volatile string workerError;
    bool  workerFailed;
    int   uploadsThisSecond, uploadsPerSecond;
    float secondStart;

    public Texture2D Texture => tex;

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
        for (int i = 0; i < front.Length; i++) { front[i] = 0.5f; back[i] = 0.5f; }
        for (int i = 0; i < wFront.Length; i++) { wFront[i] = 1f; wBack[i] = 1f; }

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

                workerFailed = true;
                Log("Meander worker thread stopped" + (workerError != null ? ": " + workerError : "") + ", baking on the main thread instead");
                StopWorker();
            }

            if (ready)
            {

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

    void StartWorker(BakeParams bp)
    {
        StopWorker();
        kick    = new AutoResetEvent(false);
        running = true;
        ready   = false;
        reqParams = bp;
        worker  = new Thread(WorkerLoop) { IsBackground = true, Name = "RealisticVolumetricAurorae meander LUT", Priority = System.Threading.ThreadPriority.BelowNormal };
        worker.Start();
        kick.Set();
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
                float[] dst = back, wdst = wBack;
                int res = dst.Length / (Rows * 4);
                sw.Restart();
                BakeAll(dst, wdst, res, reqParams);
                sw.Stop();
                lastBakeMs = (float)sw.Elapsed.TotalMilliseconds;
                ready = true;
            }
        }
        catch (Exception e)
        {
            workerError = e.GetType().Name + ": " + e.Message;
        }
    }

    static void BakeAll(float[] dst, float[] wdst, int res, BakeParams bp)
    {
        for (int r = 0; r < Rows; r++) BakeRow(dst, wdst, res, r, bp);
    }

    static void BakeRow(float[] dst, float[] wdst, int res, int row, BakeParams bp)
    {
        int band = row / 2;
        int hemi = row % 2;
        float seed        = BandSeedOffsets[band];
        float seedMeander = seed + (hemi == 1 ? 500f : 0f);
        float cLat        = bp.latCenter + BandLatOffsets[band] * bp.spacing;
        int   baseIdx     = row * res * 4;
        float invRes      = 1f / res;

        for (int i = 0; i < res; i++)
        {

            float lon = ((i + 0.5f) * invRes - 0.5f) * Mathf.PI * 2f;
            int o = baseIdx + i * 4;
            float shift = AuroraNoise.BandShift(lon, bp.t, seedMeander, bp.noiseScale);
            dst[o]     = shift + 0.5f;
            dst[o + 1] = AuroraNoise.Flare01(lon, bp.t, seed, bp.noiseScale);
            dst[o + 2] = AuroraNoise.WidthNoise01(lon, bp.t, seedMeander, bp.noiseScale);

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

    public bool Sample(int band, int hemi, float longitudeRad, out float shift, out float flare01, out float width01, out float ribbonLat, out float ribbonWeight)
    {
        shift = 0f; flare01 = 0.5f; width01 = 0.5f; ribbonLat = 0f; ribbonWeight = 1f;
        if (front == null || wFront == null || tex == null) return false;
        int res = tex.width;
        if (res < 2 || front.Length < res * Rows * 4) return false;
        int row = Mathf.Clamp(band, 0, 2) * 2 + Mathf.Clamp(hemi, 0, 1);

        float u  = (longitudeRad / (Mathf.PI * 2f) + 0.5f) * res - 0.5f;
        u -= Mathf.Floor(u / res) * res;
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
