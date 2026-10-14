// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

/// <summary>
/// CPU port of the aurora shader's hash / noise3 / fbm plus the two
/// longitude-only quantities the shader used to compute per sample:
///   - the band centreline meander shift (5-tap smoothed fbm)
///   - the regional flare factor
/// Both depend only on longitude, band seed, hemisphere and animation time,
/// so AuroraMeanderTexture bakes them into a small lookup texture every frame
/// and the shader reads them with one texture fetch per sample.
/// </summary>
public static class AuroraNoise
{
    /// <summary>Meander shift of a band centreline (in latNorm units, ±0.225 max).
    /// seedOffset must already include the +500 southern-hemisphere offset.</summary>
    public static float BandShift(float longitudeRad, float t, float seedOffset, float noiseScale)
    {
        Vector3 horizDir = new Vector3(Mathf.Cos(longitudeRad), 0f, Mathf.Sin(longitudeRad));
        Vector3 meanderInput = horizDir * 1.4f
            + new Vector3(t * 0.025f + seedOffset,
                          0f,
                          t * 0.018f + seedOffset);
        const float eps = 0.35f;
        float n0 = Fbm(meanderInput * noiseScale);
        float n1 = Fbm((meanderInput + new Vector3( eps, 0f,    0f)) * noiseScale);
        float n2 = Fbm((meanderInput + new Vector3(-eps, 0f,    0f)) * noiseScale);
        float n3 = Fbm((meanderInput + new Vector3(   0f, 0f,  eps)) * noiseScale);
        float n4 = Fbm((meanderInput + new Vector3(   0f, 0f, -eps)) * noiseScale);
        float meanderSmooth = (n0 * 0.4f + (n1 + n2 + n3 + n4) * 0.15f) - 0.5f;
        return meanderSmooth * 0.45f;
    }

    /// <summary>Regional flare, 0..1. The shader maps it to lerp(0.7, 1.6, x).
    /// Uses the plain band seed (no hemisphere offset), as the shader always did.</summary>
    public static float Flare01(float longitudeRad, float t, float seedOffset, float noiseScale)
    {
        Vector3 horizDir = new Vector3(Mathf.Cos(longitudeRad), 0f, Mathf.Sin(longitudeRad));
        Vector3 flareInput = horizDir * 1.8f
            + new Vector3(t * 0.18f + seedOffset,
                          0f,
                          t * 0.22f + seedOffset);
        float flareNoise = Fbm(flareInput * noiseScale);
        return SmoothStep(0.35f, 0.75f, flareNoise);
    }

    /// <summary>Ribbon fold displacement (latNorm units) at a surface point — the
    /// CPU twin of the shader's foldAt() (value only). hemi: 0 north, 1 south.
    /// One fold field for all bands; each band samples it through its own
    /// rotated + shifted domain, scaled by independence (shader: _BandIndependence).</summary>
    public static float Fold(float longitudeRad, float latNorm, float t, int hemi,
                             float foldStrength, float foldScale, float seedOffset, float independence)
    {
        float cosLat = Mathf.Cos(latNorm * Mathf.PI * 0.5f);
        float hx = Mathf.Cos(longitudeRad) * 10f * cosLat;
        float hz = Mathf.Sin(longitudeRad) * 10f * cosLat;
        float k   = independence;
        float ang = k * seedOffset * 0.0173f;
        float sa = Mathf.Sin(ang), ca = Mathf.Cos(ang);
        Vector3 dom = new Vector3(hx * ca - hz * sa, 0f, hx * sa + hz * ca)
                    + new Vector3(seedOffset * 0.37f, 0f, seedOffset * 0.11f) * k;
        Vector3 fs  = dom * (0.6f * foldScale)
                    + new Vector3(t * 0.03f + 3.1f + hemi * 500f, 0f, t * 0.02f + 7.7f);
        return (Fbm(fs) - 0.5f) * foldStrength;
    }

    /// <summary>Latitude (latNorm) of the folded ribbon nearest the mean path at this
    /// longitude: the zero of F(l) = l − latMean + fold(l) closest to latMean.
    /// Found by scanning the fold's reach (strong folds make F non-monotonic, so
    /// a fixed-point iteration would bounce) and interpolating the crossing.</summary>
    public static float RibbonLatitude(float longitudeRad, float latMean, float t, int hemi,
                                       float foldStrength, float foldScale, float seedOffset, float independence)
    {
        if (foldStrength <= 0.0005f) return latMean;
        const int   N      = 16;
        float       margin = 0.65f * foldStrength + 0.02f;
        float       bestL  = latMean, bestD = float.MaxValue, bestAbsF = float.MaxValue, bestAbsL = latMean;
        float       prevL  = 0f, prevF = 0f;
        for (int i = 0; i <= N; i++)
        {
            float l = latMean - margin + 2f * margin * i / N;
            float F = l - latMean + Fold(longitudeRad, l, t, hemi, foldStrength, foldScale, seedOffset, independence);
            float aF = Mathf.Abs(F);
            if (aF < bestAbsF) { bestAbsF = aF; bestAbsL = l; }
            if (i > 0 && (F <= 0f) != (prevF <= 0f))
            {
                float z = prevL + (l - prevL) * (prevF / (prevF - F));   // linear interpolation of the crossing
                float d = Mathf.Abs(z - latMean);
                if (d < bestD) { bestD = d; bestL = z; }
            }
            prevL = l; prevF = F;
        }
        if (bestD == float.MaxValue) bestL = bestAbsL;   // no crossing in reach: closest approach
        return Mathf.Clamp01(bestL);
    }

    /// <summary>Slow noise along the band that drives the local band thickness, 0..1.</summary>
    public static float WidthNoise01(float longitudeRad, float t, float seedOffset, float noiseScale)
    {
        Vector3 horizDir = new Vector3(Mathf.Cos(longitudeRad), 0f, Mathf.Sin(longitudeRad));
        Vector3 input = horizDir * 1.1f
            + new Vector3(t * 0.02f + seedOffset * 1.3f + 77f,
                          0f,
                          t * 0.015f + seedOffset);
        return Mathf.Clamp01(Fbm(input * noiseScale));
    }

    /// <summary>HLSL smoothstep (Mathf.SmoothStep has different semantics).</summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float u = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return u * u * (3f - 2f * u);
    }

    public static float Fbm(Vector3 p)
    {
        float v = 0f, amp = 0.5f;
        for (int i = 0; i < 3; i++)
        {
            v += amp * Noise3(p);
            p *= 2f;
            amp *= 0.5f;
        }
        return v * 1.143f;
    }

    public static float Noise3(Vector3 p)
    {
        float fx = Mathf.Floor(p.x), fy = Mathf.Floor(p.y), fz = Mathf.Floor(p.z);
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        Vector3 f = new Vector3(p.x - fx, p.y - fy, p.z - fz);
        f = new Vector3(
            f.x * f.x * (3f - 2f * f.x),
            f.y * f.y * (3f - 2f * f.y),
            f.z * f.z * (3f - 2f * f.z)
        );
        float n000 = Hash(ix,     iy,     iz);
        float n100 = Hash(ix + 1, iy,     iz);
        float n010 = Hash(ix,     iy + 1, iz);
        float n110 = Hash(ix + 1, iy + 1, iz);
        float n001 = Hash(ix,     iy,     iz + 1);
        float n101 = Hash(ix + 1, iy,     iz + 1);
        float n011 = Hash(ix,     iy + 1, iz + 1);
        float n111 = Hash(ix + 1, iy + 1, iz + 1);
        float nx00 = Mathf.Lerp(n000, n100, f.x);
        float nx10 = Mathf.Lerp(n010, n110, f.x);
        float nx01 = Mathf.Lerp(n001, n101, f.x);
        float nx11 = Mathf.Lerp(n011, n111, f.x);
        float nxy0 = Mathf.Lerp(nx00, nx10, f.y);
        float nxy1 = Mathf.Lerp(nx01, nx11, f.y);
        return Mathf.Lerp(nxy0, nxy1, f.z);
    }

    /// <summary>Lattice hash, the shader's hashLattice() exactly (v3.20): integer
    /// arithmetic on the lattice point, so CPU and GPU agree bit for bit and a
    /// corner has one value whichever cell asks for it. 24-bit, uniform in [0, 1).</summary>
    public static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 0x9E3779B1u;
            h = (h ^ (uint)y) * 0x85EBCA77u;
            h = (h ^ (uint)z) * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x27D4EB2Fu;
            h ^= h >> 13;
            return (h >> 8) * (1f / 16777216f);
        }
    }

}
