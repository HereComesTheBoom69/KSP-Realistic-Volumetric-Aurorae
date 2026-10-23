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
    public float arc2Threshold    = 0f,    arc3Threshold     = 0f;      // intensity at which the 2nd / 3rd arc is fully in (fades over 0.15 below); 0 = always on

    [Header("Storm timing (seconds)")]
    [Tooltip("Average seconds between storms (exponential / Poisson). Real storms are days apart; compressed for game pacing.")]
    public float averageQuietDuration = 90f;
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
    StormPhase phase = StormPhase.Quiet;
    public static readonly string[] PhaseNames = { "Quiet", "RampUp", "Peak", "RampDown" };
    /// <summary>Current phase as an index into PhaseNames (diagnostics; no allocation).</summary>
    public int PhaseIndex => (int)phase;
    float phaseStartTime;
    float phaseDuration;
    float currentPeakIntensity;
    float animTimeAccumulator = 0f;

    // Own RNG so storm timing can be seeded per save without touching
    // UnityEngine.Random (which other mods share).
    System.Random rng = new System.Random();

    void OnEnable()
    {
        ResetSchedule();
    }

    /// <summary>Seed the storm sequence (e.g. from the save game) and restart it.</summary>
    public void Seed(int seed)
    {
        rng = new System.Random(seed);
        ResetSchedule();
    }

    void ResetSchedule()
    {
        phase = StormPhase.Quiet;
        currentStormIntensity = 0f;
        phaseStartTime = TimeNow();
        phaseDuration = SamplePoissonInterval(averageQuietDuration);
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

        if (Application.isPlaying && !manualOverride)
            UpdateStormState();

        ApplyParameters(currentStormIntensity);
    }

    void UpdateStormState()
    {
        float now = TimeNow();
        float elapsed = now - phaseStartTime;

        switch (phase)
        {
            case StormPhase.Quiet:
                currentStormIntensity = 0f;
                if (elapsed >= phaseDuration)
                {
                    phase = StormPhase.RampUp;
                    phaseStartTime = now;
                    phaseDuration = RandRange(minRampUpDuration, maxRampUpDuration);
                    currentPeakIntensity = RandRange(minStormPeak, maxStormPeak);
                }
                break;

            case StormPhase.RampUp:
            {
                float frac = Mathf.Clamp01(elapsed / phaseDuration);
                currentStormIntensity = Mathf.SmoothStep(0f, currentPeakIntensity, frac);
                if (frac >= 1f)
                {
                    phase = StormPhase.Peak;
                    phaseStartTime = now;
                    phaseDuration = RandRange(minPeakDuration, maxPeakDuration);
                }
                break;
            }

            case StormPhase.Peak:
                currentStormIntensity = currentPeakIntensity;
                if (elapsed >= phaseDuration)
                {
                    phase = StormPhase.RampDown;
                    phaseStartTime = now;
                    phaseDuration = RandRange(minRampDownDuration, maxRampDownDuration);
                }
                break;

            case StormPhase.RampDown:
            {
                float frac = Mathf.Clamp01(elapsed / phaseDuration);
                currentStormIntensity = Mathf.SmoothStep(currentPeakIntensity, 0f, frac);
                if (frac >= 1f)
                {
                    phase = StormPhase.Quiet;
                    phaseStartTime = now;
                    phaseDuration = SamplePoissonInterval(averageQuietDuration);
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

    static float TimeNow()
    {
        return Application.isPlaying
            ? Time.time
#if UNITY_EDITOR
            : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            : 0f;
#endif
    }

    float RandRange(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Exponentially-distributed interval with the given mean.</summary>
    float SamplePoissonInterval(float mean)
    {
        float u = Mathf.Max(0.001f, (float)rng.NextDouble());
        return -Mathf.Log(u) * mean;
    }

    public void SetStormIntensity(float i) { currentStormIntensity = Mathf.Clamp01(i); manualOverride = true; }
    public void ResumeAutomaticScheduler() { manualOverride = false; }
}
