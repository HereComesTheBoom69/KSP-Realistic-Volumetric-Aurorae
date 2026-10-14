// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Watches, every frame, for the things that can make the aurora jump or
/// hitch, and logs each one — once, with a timestamp — to KSP.log, prefixed
/// [RVA:diag]. The tuning window shows the last few lines.
/// Nothing here changes what is rendered; the per-frame cost is a handful of
/// material reads.
///
/// What it looks for (v3.19b):
///   frame hitch     — a frame more than 3× the running average, or over 100 ms
///   GC              — a garbage collection ran this frame (KSP's collector stops the world)
///   AnimTime jump   — _AnimTime advanced by something other than deltaTime × _AnimSpeed
///                     (someone else wrote it, or it was reset)
///   material jump   — _Activity / _AltTop / _LatCenter / _BandSpacing / _LatWidth /
///                     _Arc2Fade / _Arc3Fade / _NoiseScale / _FoldStrength changed by
///                     more than 5 % in one frame (storm ramps move them by well under 1 %)
///   storm           — the scheduler's phase changed, or its intensity moved by > 0.05 in a frame
///   origin shift    — the planet or the camera moved by more than 2 km in one frame
///                     (KSP's floating origin / Krakensbane)
///   LUT stale       — the meander lookup texture was not uploaded for 5+ frames,
///                     or its bake took over 50 ms
///   ApplySettings   — the settings were re-pushed to the material (the window does this on any slider change)
/// </summary>
public class AuroraDiagnostics : MonoBehaviour
{
    public Material             auroraMaterial;
    public Transform            planet;
    public Transform            cam;          // set each frame by the owner (the flight camera)
    public AuroraStormScheduler storm;
    public AuroraMeanderTexture meander;
    public bool                 active = true;

    /// <summary>Most recent lines, newest first (for the window).</summary>
    public readonly List<string> Recent = new List<string>();
    public int EventCount { get; private set; }

    const int    MaxRecent   = 8;
    const int    MaxLogLines = 400;          // hard cap per session, so a stuck condition cannot flood KSP.log
    const float  PerKindGap  = 0.5f;         // seconds between two logs of the same kind

    static readonly string[] Watched = { "_Activity", "_AltTop", "_LatCenter", "_BandSpacing", "_LatWidth", "_Arc2Fade", "_Arc3Fade", "_NoiseScale", "_FoldStrength" };

    float   emaDt = 1f / 40f;
    int     lastGc = -1, logged;
    bool    primed;
    float   lastAnim, lastIntensity;
    int     lastPhase = -1;
    float[] lastWatched = new float[Watched.Length];
    Vector3 lastPlanet, lastCam;
    bool    haveCam;
    int     lastUploadSeen;
    int     applyCalls, applyLogged;
    readonly Dictionary<string, float> lastLogAt = new Dictionary<string, float>();

    /// <summary>Called by the owner whenever ApplySettings() runs.</summary>
    public void NoteApplySettings() { applyCalls++; }

    void LateUpdate()
    {
        if (!active || auroraMaterial == null) return;

        float now = Time.realtimeSinceStartup;
        float dt  = Time.unscaledDeltaTime;

        // ---- frame hitch -----------------------------------------------------
        if (primed && dt > Mathf.Max(0.1f, 3f * emaDt))
            Report("frame hitch", $"{dt * 1000f:F0} ms (average {emaDt * 1000f:F1} ms)");
        emaDt = Mathf.Lerp(emaDt, dt, 0.05f);

        // ---- garbage collection ------------------------------------------------
        int gc = GC.CollectionCount(0);
        if (lastGc >= 0 && gc != lastGc)
            Report("GC", $"collection #{gc} this frame ({dt * 1000f:F0} ms)");
        lastGc = gc;

        // ---- animation time: must advance by deltaTime × speed, nothing else --------
        float anim  = auroraMaterial.GetFloat("_AnimTime");
        float speed = auroraMaterial.GetFloat("_AnimSpeed");
        if (primed)
        {
            float expected = Time.deltaTime * speed;
            float got      = anim - lastAnim;
            if (Mathf.Abs(got - expected) > Mathf.Max(0.5f * Mathf.Abs(expected), 1e-4f))
                Report("AnimTime jump", $"advanced {got:F5}, expected {expected:F5} (deltaTime {Time.deltaTime:F4} × speed {speed:F3}); now {anim:F3}");
        }
        lastAnim = anim;

        // ---- material values the storm drives: smooth, or somebody poked them ----
        for (int i = 0; i < Watched.Length; i++)
        {
            float v = auroraMaterial.GetFloat(Watched[i]);
            if (primed)
            {
                float d = Mathf.Abs(v - lastWatched[i]);
                if (d > 1e-4f && d > 0.05f * Mathf.Max(Mathf.Abs(lastWatched[i]), 1e-3f))
                    Report("material jump " + Watched[i], $"{lastWatched[i]:F4} → {v:F4}");
            }
            lastWatched[i] = v;
        }

        // ---- storm scheduler --------------------------------------------------
        if (storm != null)
        {
            int phase = storm.PhaseIndex;
            if (primed && phase != lastPhase)
                Report("storm", $"phase {AuroraStormScheduler.PhaseNames[Mathf.Clamp(lastPhase, 0, 3)]} → {AuroraStormScheduler.PhaseNames[phase]}, intensity {storm.currentStormIntensity:F3}{(storm.manualOverride ? " (manual)" : "")}");
            if (primed && Mathf.Abs(storm.currentStormIntensity - lastIntensity) > 0.05f)
                Report("storm", $"intensity {lastIntensity:F3} → {storm.currentStormIntensity:F3} in one frame");
            lastPhase = phase; lastIntensity = storm.currentStormIntensity;
        }

        // ---- floating origin / camera -------------------------------------------
        if (planet != null)
        {
            Vector3 p = planet.position;
            if (primed && (p - lastPlanet).magnitude > 2000f)
                Report("origin shift", $"planet moved {(p - lastPlanet).magnitude / 1000f:F1} km in one frame");
            lastPlanet = p;
        }
        if (cam != null)
        {
            Vector3 c = cam.position;
            if (haveCam && (c - lastCam).magnitude > 2000f)
                Report("origin shift", $"camera moved {(c - lastCam).magnitude / 1000f:F1} km in one frame");
            lastCam = c; haveCam = true;
        }

        // ---- meander lookup texture -----------------------------------------------
        if (meander != null)
        {
            int up = meander.LastUploadFrame;
            if (primed && up == lastUploadSeen && Time.frameCount - up >= 5)
                Report("LUT stale", $"no upload for {Time.frameCount - up} frames (last bake {meander.LastBakeMs:F1} ms)");
            if (meander.LastBakeMs > 50f)
                Report("LUT slow", $"bake took {meander.LastBakeMs:F0} ms");
            lastUploadSeen = up;
        }

        // ---- settings pushes -------------------------------------------------------
        if (applyCalls != applyLogged)
        {
            Report("ApplySettings", $"called {applyCalls - applyLogged}× since the last note");
            applyLogged = applyCalls;
        }

        primed = true;
    }

    void Report(string kind, string detail)
    {
        float now = Time.realtimeSinceStartup;
        if (lastLogAt.TryGetValue(kind, out float at) && now - at < PerKindGap) return;
        lastLogAt[kind] = now;
        EventCount++;
        string line = $"t={now:F2}s f={Time.frameCount} {kind}: {detail}";
        Recent.Insert(0, line);
        if (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
        if (logged < MaxLogLines)
        {
            logged++;
            Debug.Log("[RVA:diag] " + line + (logged == MaxLogLines ? " (log cap reached — further events only in the window)" : ""));
        }
    }
}
