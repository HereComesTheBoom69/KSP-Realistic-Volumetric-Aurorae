// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

/// <summary>
/// Drives the aurora's material and light-ring parameters between two
/// configurable states (baseline and max storm) using a single storm
/// intensity scalar in [0,1].
///
/// In Play mode the intensity is driven by a random storm scheduler: long
/// quiet periods at 0, occasional storms that ramp up to a random peak,
/// hold, then ramp back down (asymmetric: recovery is slower than onset).
/// 1.1.0: the clock is real time and each body's sequence lives for the whole
/// game session (see Seed), so a storm is a rare event (45 minutes apart on
/// average) that a scene change does not postpone.
///
/// In Edit mode the scheduler is paused; drag "Current Storm Intensity"
/// (or tick Manual Override in Play mode) to preview any storm level.
///
/// WORLD SCALE: every tuned number below is in Unity-test-scene units
/// (planet radius 10). In KSP the plugin sets worldScale = planetRadius / 10
/// (60000 for Kerbin) and the altitude / light-range values are multiplied
/// by it before being applied. Leave worldScale = 1 in the Unity test scene.
/// </summary>
[ExecuteAlways]
public class AuroraStormScheduler : MonoBehaviour
{
    [Header("References")]
    public Material auroraMaterial;

    [Header("World scale")]
    [Tooltip("Multiplier applied to altitude values. 1 in the Unity test scene; planetRadius/10 in KSP (Kerbin = 60000).")]
    public float worldScale = 1f;

    [Header("Current State")]
    [Tooltip("0 = quiet baseline, 1 = peak solar storm. In Play mode this is driven automatically.")]
    [Range(0f, 1f)] public float currentStormIntensity = 0f;
    [Tooltip("If checked, the scheduler is bypassed and the slider above is used directly.")]
    public bool manualOverride = false;

    [Header("Baseline material (storm = 0)")]
    public float baseMatActivity   = 0.30f;
    public float baseMatAnimSpeed  = 0.35f;
    public float baseMatAltTop     = 2.00f;

    [Header("Max storm material (storm = 1)")]
    public float stormMatActivity  = 1.00f;
    public float stormMatAnimSpeed = 0.75f;
    public float stormMatAltTop    = 2.30f;

    [Header("Oval through the storm (driveOval: set by the KSP plugin; off = the material's own values)")]
    public bool  driveOval        = false;
    public float baseLatCenter    = 0.65f, stormLatCenter    = 0.65f;   // centre moves equatorward in a storm
    public float baseBandSpacing  = 1.0f,  stormBandSpacing  = 1.0f;    // arcs spread apart in a storm
    public float baseLatWidth     = 0.03f, stormLatWidth     = 0.03f;   // sheets widen in a storm
    public float baseFoldStrength = 0.12f, stormFoldStrength = 0.12f;   // 1.1.0: _FoldStrength at quiet / at peak (ConfigureStorm)
    public float arc2Threshold    = 0f,    arc3Threshold     = 0f;      // intensity at which the 2nd / 3rd arc is fully in (fades over 0.15 below); 0 = always on

    [Header("Storm timing (seconds, real time, unaffected by physics warp)")]
    [Tooltip("Average seconds between storms. Exponentially distributed with a floor of 40 % of the mean, so two storms never come back to back. 1.1.0: 45 minutes.")]
    public float averageQuietDuration = 2700f;
    [Tooltip("How strong a storm peaks (random per event). 0.4 = mild, 1.0 = severe.")]
    [Range(0f, 1f)] public float minStormPeak = 0.4f;
    [Range(0f, 1f)] public float maxStormPeak = 1.0f;
    public float minRampUpDuration   = 8f;
    public float maxRampUpDuration   = 25f;
    public float minPeakDuration     = 4f;
    public float maxPeakDuration     = 15f;
    [Tooltip("Recovery is typically longer than onset.")]
    public float minRampDownDuration = 15f;
    public float maxRampDownDuration = 40f;

    enum StormPhase { Quiet, RampUp, Peak, RampDown }
    public static readonly string[] PhaseNames = { "Quiet", "RampUp", "Peak", "RampDown" };
    /// <summary>Current phase as an index into PhaseNames (diagnostics; no allocation).</summary>
    public int PhaseIndex => (int)seq.phase;
    float animTimeAccumulator = 0f;

    // ---- the storm sequence (1.1.0: outlives the scene) ------------------------------------
    /// <summary>One body's place in its storm sequence, with its own RNG (so storm timing never
    /// touches UnityEngine.Random, which other mods share). Kept in a static table by seed (the
    /// save's seed ^ the body's name), so a revert, a vessel switch, the tracking station, the VAB
    /// all resume the same sequence and the next storm is as far away as it was; switching saves
    /// gives each save its own.</summary>
    class Sequence
    {
        public StormPhase phase = StormPhase.Quiet;
        public float phaseStart, phaseDuration, peak;
        public float mean;                       // the averageQuietDuration the quiet wait was sampled with
        public System.Random rng = new System.Random();
    }
    static readonly System.Collections.Generic.Dictionary<int, Sequence> sequences
        = new System.Collections.Generic.Dictionary<int, Sequence>();
    Sequence seq = new Sequence();               // a private one until Seed() attaches the body's
    bool attached;

    // The storm clock: real seconds since the game started (Time.unscaledTime, so physics warp does
    // not hurry a storm, on-rails warp never did) less the time the game spent paused while an
    // aurora was alive to see it, so a storm never jumps on unpause.
    static float clockPaused;
    static int   clockFrame = -1;
    static void ClockTick()
    {
        if (Time.frameCount == clockFrame) return;
        clockFrame = Time.frameCount;
        if (Time.timeScale <= 0f) clockPaused += Time.unscaledDeltaTime;
    }

    void OnEnable()
    {
        if (!attached) ResetSchedule();
    }

    /// <summary>Attach this scheduler to the body's session-long sequence (the seed is the save's
    /// seed ^ the body's name); a sequence seen for the first time this session starts with the
    /// wait an observer arriving at a random moment would see (the stationary residual: on average
    /// 0.68 x the mean, sometimes minutes, never more than one full interval).</summary>
    public void Seed(int seed)
    {
        if (!sequences.TryGetValue(seed, out seq))
        {
            // the clock in the RNG seed keeps a save's first storm from falling at the same minute
            // of every session (the wait would otherwise be the same number every time)
            seq = new Sequence { rng = new System.Random(seed ^ System.Environment.TickCount) };
            sequences[seed] = seq;
            ResetSchedule();
            seq.phaseDuration = SampleFirstWait(averageQuietDuration);
        }
        attached = true;
        currentStormIntensity = 0f;              // recomputed from the phase on the next Update
    }

    void ResetSchedule()
    {
        seq.phase = StormPhase.Quiet;
        currentStormIntensity = 0f;
        seq.phaseStart = TimeNow();
        seq.mean = averageQuietDuration;
        seq.phaseDuration = SampleQuietInterval(averageQuietDuration);
    }

    /// <summary>The window's "storm now": the quiet wait ends this frame and the storm runs its course.</summary>
    public void TriggerStorm()
    {
        if (seq.phase != StormPhase.Quiet) return;
        seq.phaseStart = TimeNow() - seq.phaseDuration;
        manualOverride = false;
    }

    /// <summary>Where the sequence is, for the window's status line.</summary>
    public string Status
    {
        get
        {
            float left = Mathf.Max(0f, seq.phaseDuration - (TimeNow() - seq.phaseStart));
            switch (seq.phase)
            {
                case StormPhase.Quiet:  return left >= 120f ? $"quiet, next storm in {left / 60f:F0} min" : $"quiet, next storm in {left:F0} s";
                case StormPhase.RampUp: return $"storm rising to {seq.peak:F2}, at peak in {left:F0} s";
                case StormPhase.Peak:   return $"storm at its peak {seq.peak:F2} for {left:F0} s more";
                default:                return $"storm fading, over in {left:F0} s";
            }
        }
    }

    void Update()
    {
        // Planet center -> shader, so its ray-sphere math survives KSP's
        // floating origin. In the Unity test scene the Aurora object has no
        // parent and the planet sits at the origin, so this is skipped.
        if (auroraMaterial != null && transform.parent != null)
        {
            Vector3 c = transform.parent.position;
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(c.x, c.y, c.z, 0f));
        }

        // Integrate animation time (phase += speed * dt) so speed changes
        // during storm ramps don't make the noise jump.
        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        float currentSpeed = Mathf.Lerp(baseMatAnimSpeed, stormMatAnimSpeed, currentStormIntensity);
        animTimeAccumulator += dt * currentSpeed;
        if (auroraMaterial != null)
            auroraMaterial.SetFloat("_AnimTime", animTimeAccumulator);

        if (Application.isPlaying)
        {
            ClockTick();
            if (!manualOverride) UpdateStormState();
        }

        ApplyParameters(currentStormIntensity);
    }

    void UpdateStormState()
    {
        float now = TimeNow();
        float elapsed = now - seq.phaseStart;

        switch (seq.phase)
        {
            case StormPhase.Quiet:
                currentStormIntensity = 0f;
                if (averageQuietDuration != seq.mean && averageQuietDuration > 0f && seq.mean > 0f)
                {
                    // the mean changed under a running wait (the window's slider, or the body's own
                    // value applied after the scheduler was built): keep the fraction of the wait
                    // that has passed, so a shorter mean brings the next storm forward at once
                    float frac = seq.phaseDuration > 0f ? Mathf.Clamp01(elapsed / seq.phaseDuration) : 1f;
                    seq.phaseDuration *= averageQuietDuration / seq.mean;
                    seq.phaseStart     = now - frac * seq.phaseDuration;
                    seq.mean           = averageQuietDuration;
                    elapsed            = now - seq.phaseStart;
                }
                if (elapsed >= seq.phaseDuration)
                {
                    seq.phase         = StormPhase.RampUp;
                    seq.phaseStart    = now;
                    seq.phaseDuration = RandRange(minRampUpDuration, maxRampUpDuration);
                    seq.peak          = RandRange(minStormPeak, maxStormPeak);
                }
                break;

            case StormPhase.RampUp:
            {
                float frac = Mathf.Clamp01(elapsed / seq.phaseDuration);
                currentStormIntensity = Mathf.SmoothStep(0f, seq.peak, frac);
                if (frac >= 1f)
                {
                    seq.phase         = StormPhase.Peak;
                    seq.phaseStart    = now;
                    seq.phaseDuration = RandRange(minPeakDuration, maxPeakDuration);
                }
                break;
            }

            case StormPhase.Peak:
                currentStormIntensity = seq.peak;
                if (elapsed >= seq.phaseDuration)
                {
                    seq.phase         = StormPhase.RampDown;
                    seq.phaseStart    = now;
                    seq.phaseDuration = RandRange(minRampDownDuration, maxRampDownDuration);
                }
                break;

            case StormPhase.RampDown:
            {
                float frac = Mathf.Clamp01(elapsed / seq.phaseDuration);
                currentStormIntensity = Mathf.SmoothStep(seq.peak, 0f, frac);
                if (frac >= 1f)
                {
                    seq.phase         = StormPhase.Quiet;
                    seq.phaseStart    = now;
                    seq.mean          = averageQuietDuration;
                    seq.phaseDuration = SampleQuietInterval(averageQuietDuration);
                }
                break;
            }
        }
    }

    void ApplyParameters(float i)
    {
        if (auroraMaterial != null)
        {
            auroraMaterial.SetFloat("_Activity",  Mathf.Lerp(baseMatActivity,  stormMatActivity,  i));
            auroraMaterial.SetFloat("_AnimSpeed", Mathf.Lerp(baseMatAnimSpeed, stormMatAnimSpeed, i));
            auroraMaterial.SetFloat("_AltTop",    Mathf.Lerp(baseMatAltTop,    stormMatAltTop,    i) * worldScale);
            if (driveOval)
            {
                // quiet: a narrow oval, poleward, one arc; storm: wide, shifted toward
                // the equator, all three arcs. The light ring and the emission map read
                // these same material values, so everything moves together.
                auroraMaterial.SetFloat("_LatCenter",   Mathf.Lerp(baseLatCenter,   stormLatCenter,   i));
                auroraMaterial.SetFloat("_BandSpacing", Mathf.Lerp(baseBandSpacing, stormBandSpacing, i));
                auroraMaterial.SetFloat("_LatWidth",    Mathf.Lerp(baseLatWidth,    stormLatWidth,    i));
                auroraMaterial.SetFloat("_FoldStrength", Mathf.Lerp(baseFoldStrength, stormFoldStrength, i));   // 1.1.0: storms that reach the equator fold more
                auroraMaterial.SetFloat("_Arc2Fade",    ArcFade(arc2Threshold, i));
                auroraMaterial.SetFloat("_Arc3Fade",    ArcFade(arc3Threshold, i));
            }
        }
    }

    // ---------- helpers --------------------------------------------------------

    /// <summary>1 at and above the threshold, fading to 0 over the 0.15 below it; always 1 for threshold 0.</summary>
    public static float ArcFade(float threshold, float intensity)
    {
        if (threshold <= 0.0005f) return 1f;
        float u = Mathf.Clamp01((intensity - (threshold - 0.15f)) / 0.15f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>The storm clock (see ClockTick): real time less pauses. The cadence is a player's
    /// experience: 45 minutes between storms means 45 minutes at the keyboard.</summary>
    static float TimeNow()
    {
        return Application.isPlaying
            ? Time.unscaledTime - clockPaused
#if UNITY_EDITOR
            : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            : 0f;
#endif
    }

    float RandRange(float a, float b) => a + (float)seq.rng.NextDouble() * (b - a);

    /// <summary>The wait between storms: exponential (memoryless, as real storm arrivals are) but with a
    /// floor of 40 % of the mean, so a 45-minute average never produces two storms five minutes apart.
    /// The mean is preserved: mean x (0.4 + 0.6 x exponential with mean 1).</summary>
    float SampleQuietInterval(float mean)
    {
        float u = Mathf.Max(0.001f, (float)seq.rng.NextDouble());
        return mean * (0.4f + 0.6f * -Mathf.Log(u));
    }

    /// <summary>The wait seen by an observer who arrives at a random moment of a sequence that has
    /// been running forever (the stationary residual of SampleQuietInterval): with probability 0.4
    /// somewhere inside the floor, uniform on [0, 0.4 x mean], otherwise the floor plus the
    /// exponential tail. Mean 0.68 x the mean wait.</summary>
    float SampleFirstWait(float mean)
    {
        if (seq.rng.NextDouble() < 0.4) return mean * 0.4f * (float)seq.rng.NextDouble();
        float u = Mathf.Max(0.001f, (float)seq.rng.NextDouble());
        return mean * (0.4f + 0.6f * -Mathf.Log(u));
    }

    public void SetStormIntensity(float i) { currentStormIntensity = Mathf.Clamp01(i); manualOverride = true; }
    public void ResumeAutomaticScheduler() { manualOverride = false; }
}
