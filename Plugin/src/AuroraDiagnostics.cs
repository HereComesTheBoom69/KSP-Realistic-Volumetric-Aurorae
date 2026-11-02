// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using UnityEngine;

public class AuroraDiagnostics : MonoBehaviour
{
    public Material             auroraMaterial;
    public Transform            planet;
    public Transform            cam;
    public AuroraStormScheduler storm;
    public AuroraMeanderTexture meander;
    public bool                 active = true;

    public readonly List<string> Recent = new List<string>();
    public int EventCount { get; private set; }

    const int    MaxRecent   = 8;
    const int    MaxLogLines = 400;
    const float  PerKindGap  = 0.5f;

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

    public void NoteApplySettings() { applyCalls++; }

    void LateUpdate()
    {
        if (!active || auroraMaterial == null) return;

        float now = Time.realtimeSinceStartup;
        float dt  = Time.unscaledDeltaTime;

        if (primed && dt > Mathf.Max(0.1f, 3f * emaDt))
            Report("frame hitch", $"{dt * 1000f:F0} ms (average {emaDt * 1000f:F1} ms)");
        emaDt = Mathf.Lerp(emaDt, dt, 0.05f);

        int gc = GC.CollectionCount(0);
        if (lastGc >= 0 && gc != lastGc)
            Report("GC", $"collection #{gc} this frame ({dt * 1000f:F0} ms)");
        lastGc = gc;

        float anim  = auroraMaterial.GetFloat("_AnimTime");
        float speed = auroraMaterial.GetFloat("_AnimSpeed");
        if (primed)
        {
            float expected = Time.deltaTime * speed;
            float got      = anim - lastAnim;
            if (Mathf.Abs(got - expected) > Mathf.Max(0.5f * Mathf.Abs(expected), 1e-4f))
                Report("AnimTime jump", $"advanced {got:F5}, expected {expected:F5} (deltaTime {Time.deltaTime:F4} x speed {speed:F3}); now {anim:F3}");
        }
        lastAnim = anim;

        for (int i = 0; i < Watched.Length; i++)
        {
            float v = auroraMaterial.GetFloat(Watched[i]);
            if (primed)
            {
                float d = Mathf.Abs(v - lastWatched[i]);
                if (d > 1e-4f && d > 0.05f * Mathf.Max(Mathf.Abs(lastWatched[i]), 1e-3f))
                    Report("material jump " + Watched[i], $"{lastWatched[i]:F4} -> {v:F4}");
            }
            lastWatched[i] = v;
        }

        if (storm != null)
        {
            int phase = storm.PhaseIndex;
            if (primed && phase != lastPhase)
                Report("storm", $"phase {AuroraStormScheduler.PhaseNames[Mathf.Clamp(lastPhase, 0, 3)]} -> {AuroraStormScheduler.PhaseNames[phase]}, intensity {storm.currentStormIntensity:F3}{(storm.manualOverride ? " (manual)" : "")}");
            if (primed && Mathf.Abs(storm.currentStormIntensity - lastIntensity) > 0.05f)
                Report("storm", $"intensity {lastIntensity:F3} -> {storm.currentStormIntensity:F3} in one frame");
            lastPhase = phase; lastIntensity = storm.currentStormIntensity;
        }

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

        if (meander != null)
        {
            int up = meander.LastUploadFrame;
            if (primed && up == lastUploadSeen && Time.frameCount - up >= 5)
                Report("LUT stale", $"no upload for {Time.frameCount - up} frames (last bake {meander.LastBakeMs:F1} ms)");
            if (meander.LastBakeMs > 50f)
                Report("LUT slow", $"bake took {meander.LastBakeMs:F0} ms");
            lastUploadSeen = up;
        }

        if (applyCalls != applyLogged)
        {
            Report("ApplySettings", $"called {applyCalls - applyLogged}x since the last note");
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
            Debug.Log("[RVA:diag] " + line + (logged == MaxLogLines ? " (log cap reached, further events only in the window)" : ""));
        }
    }
}
