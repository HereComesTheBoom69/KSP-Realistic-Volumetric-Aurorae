// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

[ExecuteAlways]
public class AuroraStormScheduler : MonoBehaviour
{
    public Material auroraMaterial;

    public float worldScale = 1f;

    [Range(0f, 1f)] public float currentStormIntensity = 0f;
    public bool manualOverride = false;

    public float baseMatActivity   = 0.30f;
    public float baseMatAnimSpeed  = 0.35f;
    public float baseMatAltTop     = 2.00f;

    public float stormMatActivity  = 1.00f;
    public float stormMatAnimSpeed = 0.75f;
    public float stormMatAltTop    = 2.30f;

    public bool  driveOval        = false;
    public float baseLatCenter    = 0.65f, stormLatCenter    = 0.65f;
    public float baseBandSpacing  = 1.0f,  stormBandSpacing  = 1.0f;
    public float baseLatWidth     = 0.03f, stormLatWidth     = 0.03f;
    public float arc2Threshold    = 0f,    arc3Threshold     = 0f;

    public float averageQuietDuration = 90f;
    [Range(0f, 1f)] public float minStormPeak = 0.4f;
    [Range(0f, 1f)] public float maxStormPeak = 1.0f;
    public float minRampUpDuration   = 8f;
    public float maxRampUpDuration   = 25f;
    public float minPeakDuration     = 4f;
    public float maxPeakDuration     = 15f;
    public float minRampDownDuration = 15f;
    public float maxRampDownDuration = 40f;

    enum StormPhase { Quiet, RampUp, Peak, RampDown }
    StormPhase phase = StormPhase.Quiet;
    public static readonly string[] PhaseNames = { "Quiet", "RampUp", "Peak", "RampDown" };

    public int PhaseIndex => (int)phase;
    float phaseStartTime;
    float phaseDuration;
    float currentPeakIntensity;
    float animTimeAccumulator = 0f;

    System.Random rng = new System.Random();

    void OnEnable()
    {
        ResetSchedule();
    }

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

        if (auroraMaterial != null && transform.parent != null)
        {
            Vector3 c = transform.parent.position;
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(c.x, c.y, c.z, 0f));
        }

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

                auroraMaterial.SetFloat("_LatCenter",   Mathf.Lerp(baseLatCenter,   stormLatCenter,   i));
                auroraMaterial.SetFloat("_BandSpacing", Mathf.Lerp(baseBandSpacing, stormBandSpacing, i));
                auroraMaterial.SetFloat("_LatWidth",    Mathf.Lerp(baseLatWidth,    stormLatWidth,    i));
                auroraMaterial.SetFloat("_Arc2Fade",    ArcFade(arc2Threshold, i));
                auroraMaterial.SetFloat("_Arc3Fade",    ArcFade(arc3Threshold, i));
            }
        }
    }

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

    float SamplePoissonInterval(float mean)
    {
        float u = Mathf.Max(0.001f, (float)rng.NextDouble());
        return -Mathf.Log(u) * mean;
    }

    public void SetStormIntensity(float i) { currentStormIntensity = Mathf.Clamp01(i); manualOverride = true; }
    public void ResumeAutomaticScheduler() { manualOverride = false; }
}
