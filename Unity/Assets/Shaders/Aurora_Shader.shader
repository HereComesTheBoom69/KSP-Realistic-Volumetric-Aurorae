// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
Shader "RVA/Aurora"
{
    Properties
    {
        _Activity    ("Activity", Range(0, 1)) = 0.3

        _ColorLow    ("Color (low, green)",  Color) = (0.4, 0.9, 0.5, 1)
        _ColorMid    ("Color (mid, blue)",   Color) = (0.4, 0.2, 0.9, 1)
        _ColorHigh   ("Color (high, pink)",  Color) = (0.9, 0.3, 0.5, 1)

        _GreenTop    ("Green until",  Range(0, 1)) = 0.05
        _BlueBottom  ("Blue from",    Range(0, 1)) = 0.5
        _BlueTop     ("Blue until",   Range(0, 1)) = 0.5
        _PinkBottom  ("Pink from",    Range(0, 1)) = 1.0
        _GainLow     ("Green brightness", Range(0, 3)) = 1.0
        _GainMid     ("Blue brightness",  Range(0, 3)) = 1.0
        _GainHigh    ("Pink brightness",  Range(0, 3)) = 1.0

        _HighAltBoost   ("High-altitude density boost", Range(0, 3)) = 0.6

        _CompressFactor ("Upper band compression", Range(0.2, 1)) = 0.55

        _RayThreshold   ("Ray threshold (higher = sparser)", Range(0, 0.6)) = 0.3
        _RayGain        ("Ray contrast (higher = crisper)", Range(0.5, 5)) = 1.4

        _RayTaper       ("Ray taper (columns narrow toward the top)", Range(0, 1)) = 0.4

        _RayZone        ("Ray zone", Range(-1, 1)) = 1.0

        _DetailStrength ("Close-range detail", Range(0, 1)) = 0.5

        _NoiseScale  ("Noise scale", Range(0.1, 5.0)) = 2.5

        _AnimSpeed   ("Animation speed", Range(0, 5)) = 0.35
        _AnimTime    ("Anim time (driven by C#)", Float) = 0.0

        _AltBase     ("Altitude base",  Float) = 0.0
        _AltTop      ("Altitude top",   Float) = 2.0

        _LatCenter   ("Latitude center", Range(0, 1)) = 0.65
        _LatWidth    ("Ribbon half-width", Range(0, 0.6)) = 0.03

        _FoldStrength   ("Ribbon folds (amplitude)", Range(0, 0.3)) = 0.12
        _FoldScale      ("Ribbon fold scale", Range(0.2, 4)) = 1.0

        _BandSpacing    ("Band spacing", Range(0, 2)) = 1.0
        _BandIndependence ("Band fold independence", Range(0, 1)) = 0.7

        _Arc2Fade       ("Second arc visibility", Range(0, 1)) = 1.0
        _Arc3Fade       ("Third arc visibility", Range(0, 1)) = 1.0

        _EdgeSharpness  ("Ribbon edge sharpness", Range(0, 1)) = 0.6

        _TopSpread      ("Top spread (diffuse cap)", Range(0, 3)) = 0.8

        _RayScale       ("Ray fineness", Range(0.5, 6)) = 2.5

        _BottomSoftness ("Bottom edge softness", Range(0.02, 0.3)) = 0.07

        _WidthVariance  ("Band width variance", Range(0, 3)) = 0.0

        _HeightVariance ("Band height variance", Range(0, 1)) = 1.0

        _Spikiness      ("Spike height", Range(0, 2)) = 0.0
        _SpikeThreshold ("Spike sparsity (higher = fewer)", Range(0.3, 0.9)) = 0.6

        _GreenSpikes    ("Green rays into upper bands", Range(0, 1)) = 0.0

        _SphereRadius ("Sphere radius", Float) = 10.0

        _Occluder     ("Occluder sphere (set by C#)", Vector) = (0, 0, 0, 0)

        _OccluderDepth ("Occluder body model, 1 / eye depth (set by C#)", 2D) = "black" {}
        _OccluderDepthParams ("Occluder model depth params (set by C#)", Vector) = (0, 0, 0, 0)

        _ForceFar     ("All samples to the far target", Float) = 0

        _StepLength  ("Step length (fraction of planet radius)", Range(0.0005, 0.05)) = 0.005

        _StepNear    ("Step scale near the band", Range(0.1, 1)) = 0.25

        _StepGrowth  ("Step growth per unit distance", Range(0, 0.1)) = 0.03
        _StepFarMul  ("Far step (x step length)", Range(1, 8)) = 3
        _MaxSteps    ("Max steps per ray", Range(8, 512)) = 192
        _Jitter      ("Sample jitter", Range(0, 1)) = 0.75

        _BlueNoise   ("Blue-noise tile (set by C#)", 2D) = "gray" {}
        _BlueNoiseOn ("1 = use the tile", Float) = 0.0

        _StepFilter  ("Noise filter width (x step)", Range(0.2, 1.5)) = 0.75

        _Brightness  ("Brightness", Range(0, 40)) = 8.0

        _MeanderTex  ("Meander/flare LUT (set by C#)", 2D) = "gray" {}

        _UseSceneDepth ("Occlude by scene depth (0 or 1)", Float) = 0.0

        _CloudTopAlt   ("Cloud top altitude (set by C#)", Float) = 0.0
        _CloudBaseAlt ("Cloud base altitude (carried on the material for the composite's horizon boost; the march does not read it)", Float) = 0.0

        _FarSplit      ("Split at the cloud shell (0 or 1)", Float) = 0.0

        _SunDir        ("Sun direction (set by C#)", Vector) = (0, 0, 0, 0)
        _DaylightFade  ("Hide in daylight", Range(0, 1)) = 1.0

        _PixelAngle  ("Pixel angle (set by C#)", Float) = 0.0

        _DebugMode   ("Debug mode (0 = off)", Float) = 0.0

        _RVAShaderBuild ("Shader build (read by C#)", Float) = 1131
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        CGINCLUDE
            #include "UnityCG.cginc"

            float  _DebugMode;
            float  _Activity;
            float4 _ColorLow, _ColorMid, _ColorHigh;
            float  _GreenTop, _BlueBottom, _BlueTop, _PinkBottom;
            float  _GainLow, _GainMid, _GainHigh;
            float  _HighAltBoost, _CompressFactor;
            float  _RayThreshold, _RayGain, _RayTaper, _RayZone, _DetailStrength;
            float  _NoiseScale, _AnimSpeed, _AnimTime;
            float  _AltBase, _AltTop;
            float4 _PlanetCenter;
            float  _LatCenter, _LatWidth;
            float  _FoldStrength, _FoldScale, _EdgeSharpness, _TopSpread, _RayScale, _BottomSoftness;
            float  _BandSpacing, _BandIndependence, _Arc2Fade, _Arc3Fade;
            float  _WidthVariance, _HeightVariance, _Spikiness, _SpikeThreshold, _GreenSpikes;
            float  _CloudTopAlt, _FarSplit;
            float  _CloudBaseAlt;
            float  _SphereRadius;
            float4 _Occluder;
            sampler2D _OccluderDepth;
            float4 _OccluderDepthParams;
            float  _ForceFar;
            float  _StepLength, _StepNear, _MaxSteps, _Jitter, _StepGrowth, _StepFarMul, _StepFilter;
            sampler2D _BlueNoise;
            float  _BlueNoiseOn;

            float4    _RayJitter;
            float     _TemporalOffset;
            float4 _SunDir;
            float  _DaylightFade;
            float  _Brightness;
            float  _PixelAngle;
            sampler2D _MeanderTex;
            float  _UseSceneDepth;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos       : SV_POSITION;
                float3 worldPos  : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz - _PlanetCenter.xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            uint hashLattice(int3 i)
            {
                uint h = (uint)i.x * 0x9E3779B1u;
                h = (h ^ (uint)i.y) * 0x85EBCA77u;
                h = (h ^ (uint)i.z) * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x27D4EB2Fu;
                h ^= h >> 13;
                return h >> 8;
            }
            float hash(int3 i) { return (float)hashLattice(i) * (1.0 / 16777216.0); }

            float noise3(float3 p)
            {
                float3 fl = floor(p);
                int3   i  = (int3)fl;
                float3 f  = p - fl;
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash(i + int3(0,0,0));
                float n100 = hash(i + int3(1,0,0));
                float n010 = hash(i + int3(0,1,0));
                float n110 = hash(i + int3(1,1,0));
                float n001 = hash(i + int3(0,0,1));
                float n101 = hash(i + int3(1,0,1));
                float n011 = hash(i + int3(0,1,1));
                float n111 = hash(i + int3(1,1,1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0, amp = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += amp * noise3(p);
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v * 1.143;
            }

            float3 rotXZ(float3 p, float c, float s)
            {
                return float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
            }

            float fbmAA(float3 p, float cellSize, float px)
            {
                float v = 0.0, amp = 0.5, size = cellSize;
                px = max(px, 1e-6);
                for (int i = 0; i < 3; i++)
                {
                    float fade = saturate((size / px - 1.0) * 0.5);

                    if (fade > 0.0) v += amp * lerp(0.5, noise3(p), fade);
                    else            v += amp * 0.5;
                    p = rotXZ(p, 0.7986355, 0.6018150) * 2.0 + float3(3.7, 0.0, 1.9);
                    amp *= 0.5;
                    size *= 0.5;
                }
                return v * 1.143;
            }

            float4 noise3d(float3 p)
            {
                float3 fl = floor(p);
                int3   i  = (int3)fl;
                float3 f  = p - fl;
                float3 u  = f * f * (3.0 - 2.0 * f);
                float3 du = 6.0 * f * (1.0 - f);
                float a = hash(i + int3(0,0,0));
                float b = hash(i + int3(1,0,0));
                float c = hash(i + int3(0,1,0));
                float d = hash(i + int3(1,1,0));
                float e = hash(i + int3(0,0,1));
                float f1= hash(i + int3(1,0,1));
                float g = hash(i + int3(0,1,1));
                float h = hash(i + int3(1,1,1));
                float k0 = a, k1 = b - a, k2 = c - a, k3 = e - a;
                float k4 = a - b - c + d, k5 = a - c - e + g, k6 = a - b - e + f1;
                float k7 = -a + b + c - d + e - f1 - g + h;
                float v = k0 + k1*u.x + k2*u.y + k3*u.z
                        + k4*u.x*u.y + k5*u.y*u.z + k6*u.z*u.x + k7*u.x*u.y*u.z;
                float3 grad = du * float3(k1 + k4*u.y + k6*u.z + k7*u.y*u.z,
                                          k2 + k4*u.x + k5*u.z + k7*u.z*u.x,
                                          k3 + k5*u.y + k6*u.x + k7*u.x*u.y);
                return float4(v, grad);
            }

            float4 fbmd(float3 p)
            {
                float4 r = 0.0;
                float amp = 0.5, freq = 1.0;
                for (int i = 0; i < 3; i++)
                {
                    float4 n = noise3d(p);
                    r.x   += amp * n.x;
                    r.yzw += amp * freq * n.yzw;
                    p *= 2.0;
                    amp *= 0.5;
                    freq *= 2.0;
                }
                return r * 1.143;
            }

            float4 foldAt(float3 sP, float t, float hemi, float seedOffset, float foldStrength)
            {
                if (foldStrength <= 0.0005) return float4(0, 0, 0, 0);
                float k = _BandIndependence;
                float sa, ca;
                sincos(k * seedOffset * 0.0173, sa, ca);
                float3 dom = float3(sP.x * ca - sP.z * sa, sP.y, sP.x * sa + sP.z * ca)
                           + float3(seedOffset * 0.37, 0.0, seedOffset * 0.11) * k;
                float  sc = 0.6 * _FoldScale;
                float3 fs = dom * sc + float3(t * 0.03 + 3.1 + hemi * 500.0, 0.0, t * 0.02 + 7.7);
                float4 n  = fbmd(fs);

                float gx = (n.y * ca + n.w * sa) * sc;
                float gy = n.z * sc;
                float gz = (-n.y * sa + n.w * ca) * sc;
                return float4(n.x - 0.5, gx, gy, gz) * foldStrength;
            }

            struct SampleGeo
            {
                float3 p;
                float  r;
                float  altitude;
                float  latNorm;
                float  lon01;
                float  hemi;
                float3 hp;
                float3 hpN;
                float  internalAltTop;
                float3 east3, north3;
            };

            float ribbonDistance(float F, float4 fold, SampleGeo geo)
            {

                float  gE = dot(fold.yzw, geo.east3);
                float  gN = dot(fold.yzw, geo.north3);
                float2 gradArc = float2(gE, 0.063662 + gN);
                return abs(F) / max(length(gradArc), 0.01);
            }

            struct BandTweak
            {
                float latWidth;
                float foldStrength;
                float foldZoom;
                float shiftScale;
                float widthVar;
                float topSpread;
            };
            BandTweak mainTweak()
            {
                BandTweak tw;
                tw.latWidth = _LatWidth; tw.foldStrength = _FoldStrength; tw.foldZoom = 1.0;
                tw.shiftScale = 1.0; tw.widthVar = _WidthVariance; tw.topSpread = _TopSpread;
                return tw;
            }

            float2 auroraBand(SampleGeo geo, float t, int bandIndex, float bandLatCenter,
                              float seedOffset, float detail, float pxI, float envPx,
                              inout float4 foldCache, inout float foldCached, BandTweak tw)
            {

                const float3 drift = float3(0.57735, 0.57735, 0.57735);
                float altitude       = geo.altitude;
                float internalAltTop = geo.internalAltTop;
                float latNorm        = geo.latNorm;
                float hemi           = geo.hemi;
                float3 hp            = geo.hp;
                float3 hpN           = geo.hpN;
                float3 p             = geo.p;

                float rowV  = ((float)bandIndex * 2.0 + hemi + 0.5) / 6.0;
                float4 lut  = tex2Dlod(_MeanderTex, float4(geo.lon01, rowV, 0, 0));
                float bandShift = (lut.r - 0.5) * tw.shiftScale;
                float flare     = lerp(0.7, 1.6, lut.g);

                float widthScale = lerp(1.0 - tw.widthVar, 1.0 + tw.widthVar, lut.b);
                float localWidth = tw.latWidth * max(widthScale, 0.15);

                {
                    float wLat = max(localWidth * (1.0 + max(tw.topSpread, 0.0)), pxI * 1.5 * 0.063662);
                    float fineGate = wLat + 0.65 * tw.foldStrength + 0.01;
                    if (abs(latNorm - (bandLatCenter + bandShift)) > fineGate) return float2(0, 0);
                }

                float4 fold;
                if (_BandIndependence <= 0.0)
                {
                    if (foldCached < 0.5)
                    {
                        foldCache = foldAt(hpN * tw.foldZoom, t, hemi, seedOffset, tw.foldStrength);
                        foldCache.yzw *= tw.foldZoom;
                        foldCached = 1.0;
                    }
                    fold = foldCache;
                }
                else
                {
                    fold = foldAt(hpN * tw.foldZoom, t, hemi, seedOffset, tw.foldStrength);
                    fold.yzw *= tw.foldZoom;
                }
                float  F     = latNorm - (bandLatCenter + bandShift) + fold.x;
                float  dist  = ribbonDistance(F, fold, geo);

                float wMin    = pxI * 1.5;
                float ceilMin = min(internalAltTop * (1.0 - 0.7 * _HeightVariance), internalAltTop);
                float anMax   = (ceilMin > 1e-6) ? saturate(altitude / ceilMin) : 1.0;
                float wMaxHere = localWidth * (1.0 + max(tw.topSpread, 0.0) * anMax * anMax) * 15.70796;
                if (dist >= max(wMaxHere, wMin)) return float2(0, 0);

                float c1 = fbm(hp * 0.137 * _NoiseScale
                            + float3( 13.41 + seedOffset,  0,  7.93 + seedOffset)
                            + float3(t * 0.04, 0, t * 0.06));
                float c2 = fbm(hp * 0.318 * _NoiseScale
                            + float3(-29.17 + seedOffset, 0, 41.23 + seedOffset)
                            + float3(t * 0.07, 0, t * 0.03));
                float ceilingNoise = saturate(c1 * 0.65 + c2 * 0.55 - 0.1);
                float localCeiling = lerp(internalAltTop * (1.0 - 0.7 * _HeightVariance), internalAltTop, ceilingNoise);

                float altNormRaw = saturate(altitude / localCeiling);

                float wEff   = localWidth * (1.0 + tw.topSpread * altNormRaw * altNormRaw) * 15.70796;

                float widthComp = 1.0;
                if (wEff < wMin) { widthComp = wEff / wMin; wEff = wMin; }
                float latGate = smoothstep(wEff, wEff * 0.9 * _EdgeSharpness, dist);
                if (latGate <= 0.001) return float2(0, 0);

                float activityMul = 0.3 + 0.7 * _Activity;

                float g = 0.0;
                bool  needG = _GreenSpikes > 0.001;
                if (needG && _Spikiness > 0.001)
                {
                    float3 gs = hpN * (0.25 * _NoiseScale) + float3(seedOffset * 2.3 + 41.0, 0, 0) + drift * (t * 0.12);
                    g = smoothstep(0.55, 0.80, fbm(gs)) * _GreenSpikes;
                    needG = false;
                }

                float spikeDensity = 0.0, spikeColorAlt = 1.0;
                if (_Spikiness > 0.001)
                {
                    float3 hs = hpN * (0.9 * _NoiseScale) + float3(seedOffset * 1.7 + 5.0, 0, 0) + drift * (t * 0.25);
                    float m = saturate((fbmAA(hs, 1.0 / (0.9 * _NoiseScale), pxI) - _SpikeThreshold) / max(1.0 - _SpikeThreshold, 0.05));
                    m = m * m * m;
                    if (m > 0.002)
                    {
                        float rootAlt = localCeiling * 0.5;
                        float topAlt  = localCeiling * (1.0 + 1.6 * _Spikiness * m);
                        float v = smoothstep(rootAlt, rootAlt + 0.25 * localCeiling, altitude)
                                * (1.0 - smoothstep(topAlt * 0.8, topAlt, altitude));
                        spikeDensity  = m * v * 0.7 * latGate * flare * activityMul * (1.0 + 0.5 * g);
                        spikeColorAlt = saturate(altitude / localCeiling) * (1.0 - 0.85 * g);
                    }
                }

                float altNorm = saturate(altitude / localCeiling);

                {
                    float extra = (altNorm - 0.45) * (1.0 / _CompressFactor - 1.0);
                    altNorm = saturate(altNorm + extra * smoothstep(0.35, 0.55, altNorm));
                }

                if (altNorm <= 0.001 || altNorm >= 0.999) return float2(spikeDensity * widthComp, spikeColorAlt);

                float thrH = _RayThreshold + _RayTaper * 0.2 * altNorm;

                bool  boundsOk = (_RayGain >= 0.0) && (abs(_RayZone) <= 1.0);
                float patMax   = max(0.6501, 1.0002 + 0.225 * abs(detail));
                float density  = 0.0;
                if (!(boundsOk && patMax * latGate * 1.0001 <= thrH))
                {

                    float fpN = envPx / max(localCeiling, 1e-4);
                    float bs  = clamp(max(_BottomSoftness, fpN), 0.02, 0.5);
                    float ts  = clamp(1.5 * fpN, 0.15, 1.0);
                    float vEnvelope = smoothstep(0.0, bs, altNorm)
                                    * (1.0 - 0.38 * smoothstep(bs, 0.85, altNorm))
                                    * (1.0 - smoothstep(1.0 - ts, 1.0, altNorm));

                    float warpStrength = 0.5 + 1.5 * _Activity;
                    float3 warpInput = hpN * 0.05 * _NoiseScale
                                     + float3(t * 0.1 + seedOffset, 0, 0);
                    float3 warp = float3(
                        fbm(warpInput),
                        fbm(warpInput + float3(17.3, 0, 0)),
                        fbm(warpInput + float3(0, 31.7, 0))
                    ) - 0.5;
                    float3 pWarped = hpN + warp * warpStrength * 2.0;

                    float3 baseInput = pWarped * 0.25 * _NoiseScale
                                     + float3(seedOffset, 0, 0) + drift * (t * 0.15);
                    float basePattern = fbm(baseInput);
                    basePattern = lerp(0.45, 0.65, basePattern);

                    float  rayCell  = 1.0 / (0.4 * _NoiseScale * _RayScale);
                    float3 raySpace = pWarped * 0.4 * _NoiseScale * _RayScale + float3(seedOffset, 0, 0);
                    float3 rayInput = raySpace + drift * (t * 0.3);
                    float upperPattern = fbmAA(rayInput, rayCell, pxI);

                    if (detail > 0.001)
                    {
                        float px = max(pxI, 1e-6);
                        float f7  = saturate((rayCell / 7.0  / px - 1.0) * 0.5);
                        float f16 = saturate((rayCell / 16.1 / px - 1.0) * 0.5);
                        if (f7 > 0.0)
                        {

                            float3 r7 = rotXZ(raySpace, 0.4848096, 0.8746197);
                            float3 dp = r7 * 7.0 + drift * (t * 0.6) + 11.7;
                            float  d7 = lerp(0.5, noise3(dp), f7) * 0.6;
                            float  d16 = 0.5 * 0.4;
                            if (f16 > 0.0)
                            {
                                float3 dp2 = rotXZ(dp, 0.7986355, 0.6018150) * 2.3 + float3(5.1, 0.0, 2.3);
                                d16 = lerp(0.5, noise3(dp2), f16) * 0.4;
                            }
                            float d = d7 + d16;
                            upperPattern += (d - 0.5) * detail * 0.45;
                        }

                    }

                    float zoneS    = smoothstep(0.30, 0.60, altNorm);
                    float zoneMix  = (_RayZone >= 0.0) ? zoneS : (1.0 - zoneS);
                    float layerMix = 1.0 - abs(_RayZone) * (1.0 - zoneMix);
                    float rayPattern = lerp(basePattern, upperPattern, layerMix);

                    if (!(boundsOk && rayPattern * latGate * 1.0001 <= thrH))
                    {

                        if (needG)
                        {
                            float3 gs = hpN * (0.25 * _NoiseScale) + float3(seedOffset * 2.3 + 41.0, 0, 0) + drift * (t * 0.12);
                            g = smoothstep(0.55, 0.80, fbm(gs)) * _GreenSpikes;
                        }

                        float flicker = lerp(0.85, 1.0,
                            fbm(p * 1.2 + float3(seedOffset, t * 2, 0)));

                        density = rayPattern * latGate * flicker;
                        density = saturate((density - thrH) * _RayGain);
                        density *= vEnvelope * (1.0 + _HighAltBoost * altNorm);
                        density *= flare * activityMul;
                        density *= 1.0 + 0.5 * g;
                    }
                }

                float colorAlt = altNorm * (1.0 - 0.85 * g);

                float total = (density + spikeDensity) * widthComp;
                if (total <= 1e-5) return float2(0, colorAlt);
                return float2(total, (density * colorAlt + spikeDensity * spikeColorAlt) / total);
            }

            float3 auroraField(float3 pWorld, float t, float detail, float pxI, float envPx)
            {
                SampleGeo g;
                float internalScale = 10.0 / _SphereRadius;
                g.p = pWorld * internalScale;
                float internalBase   = _AltBase * internalScale;
                g.internalAltTop     = max(_AltTop - _AltBase, 1e-5) * internalScale;
                g.r        = length(g.p);
                g.altitude = g.r - 10.0 - internalBase;
                if (g.altitude <= 0.0) return float3(0, 0.5, 0);

                float latS = asin(clamp(g.p.y / g.r, -1.0, 1.0)) / 1.5707963;

                float preGate = _LatWidth * (1.0 + _WidthVariance) * (1.0 + _TopSpread) + 0.23 + 0.65 * _FoldStrength + 0.01;
                float c1Lat = _LatCenter;
                float c2Lat = _LatCenter - 0.05 * _BandSpacing;
                float c3Lat = _LatCenter + 0.04 * _BandSpacing;
                bool  a2 = _Arc2Fade > 0.001, a3 = _Arc3Fade > 0.001;
                float n1 = abs(latS - c1Lat) - preGate;
                float n2 = a2 ? abs(latS - c2Lat) - preGate : 1e30;
                float n3 = a3 ? abs(latS - c3Lat) - preGate : 1e30;
                float s1 = abs(-latS - c1Lat) - preGate;
                float s2 = a2 ? abs(-latS - c2Lat) - preGate : 1e30;
                float s3 = a3 ? abs(-latS - c3Lat) - preGate : 1e30;
                float mN = min(n1, min(n2, n3)), mS = min(s1, min(s2, s3));
                float mMin = min(mN, mS);
                if (mMin > 0.0)
                {

                    return float3(0, 0.5, mMin * 1.5707963 * (_SphereRadius + _AltBase) * 0.999);
                }

                g.lon01 = atan2(g.p.z, g.p.x) * 0.15915494 + 0.5;

                float3 sP = g.p * (10.0 / g.r);
                g.hp  = sP;
                g.hpN = sP;
                float3 up   = sP * 0.1;
                float  rho  = length(up.xz);
                float3 east = rho > 1e-4 ? float3(-up.z, 0.0, up.x) / rho : float3(1, 0, 0);
                float3 north = cross(east, up);
                g.east3 = east;

                BandTweak mt = mainTweak();
                float dSum = 0.0, aSum = 0.0;

                [loop]
                for (int h = 0; h < 2; h++)
                {
                    bool  south = h > 0;
                    if ((south ? mS : mN) > 0.0) continue;
                    g.latNorm = south ? -latS : latS;
                    g.hemi    = south ? 1.0 : 0.0;
                    g.north3  = south ? -north : north;
                    float4 fc = float4(0, 0, 0, 0); float fcd = 0.0;
                    [loop]
                    for (int b = 0; b < 3; b++)
                    {

                        float gate = (b == 0) ? (south ? s1 : n1) : (b == 1) ? (south ? s2 : n2) : (south ? s3 : n3);
                        if (gate > 0.0) continue;
                        float cLat = (b == 0) ? c1Lat : (b == 1) ? c2Lat : c3Lat;
                        float seed = (b == 0) ? 0.0 : (b == 1) ? 73.41 : 218.93;
                        float fade = (b == 0) ? 1.0 : (b == 1) ? _Arc2Fade : _Arc3Fade;
                        float2 bd = auroraBand(g, t, b, cLat, seed, detail, pxI, envPx, fc, fcd, mt);
                        bd.x *= fade;
                        dSum += bd.x;
                        aSum += bd.x * bd.y;
                    }
                }

                if (dSum <= 0.001) return float3(0, 0.5, 0);
                return float3(saturate(dSum * 0.6), aSum / dSum, 0);
            }

            float3 auroraColor(float a)
            {
                float gTop = _GreenTop;
                float bBot = max(_BlueBottom, gTop);
                float bTop = max(_BlueTop,    bBot);
                float pBot = max(_PinkBottom, bTop);
                float c1 = 0.5 * (gTop + bBot), h1 = max(0.5 * (bBot - gTop), 0.06);
                float c2 = 0.5 * (bTop + pBot), h2 = max(0.5 * (pBot - bTop), 0.06);
                float3 low  = _ColorLow.rgb  * _GainLow;
                float3 mid  = _ColorMid.rgb  * _GainMid;
                float3 high = _ColorHigh.rgb * _GainHigh;
                float3 c = lerp(low, mid, smoothstep(c1 - h1, c1 + h1, a));
                return     lerp(c, high,  smoothstep(c2 - h2, c2 + h2, a));
            }

            float2 raySphereShell(float3 ro, float3 rd, float rOut)
            {
                float b = dot(ro, rd);
                float c = dot(ro, ro) - rOut * rOut;
                float disc = b*b - c;
                if (disc < 0) return float2(-1, -1);
                float sd = sqrt(disc);
                return float2(max(-b - sd, 0), -b + sd);
            }

            struct FragOut
            {
                float4 near : SV_Target0;
                float4 far  : SV_Target1;
                float4 aux  : SV_Target2;

            };

            FragOut outNF(float3 near, float3 far, float chord, float depthKm, float tMean, float tSpread)
            {
                FragOut o;
                o.near = float4(near, chord);
                o.far  = float4(far, depthKm);
                o.aux  = float4(tMean, tSpread, 0, 0);
                return o;
            }
            FragOut outNF(float3 near, float3 far, float chord, float depthKm, float tMean)
            {
                return outNF(near, far, chord, depthKm, tMean, 0.0);
            }

            FragOut frag (v2f i)
            {
                float3 ro = _WorldSpaceCameraPos - _PlanetCenter.xyz;
                float3 rd = normalize(i.worldPos - ro);

                if (abs(_RayJitter.x) + abs(_RayJitter.y) > 1e-12)
                {
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, fwd = -UNITY_MATRIX_V[2].xyz;
                    float  k = max(dot(rd, fwd), 1e-4);
                    rd = normalize(rd / k + right * _RayJitter.x + up * (_RayJitter.y * -_ProjectionParams.x));
                }

                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return outNF(float3(2.0, 0.0, 0.0), float3(0, 0, 0), 1.0, 0.0, 0.0);

                float thick  = max(_AltTop - _AltBase, 1e-5);
                float rOuter = _SphereRadius + _AltTop + thick * 1.6 * _Spikiness;
                float2 tNearFar = raySphereShell(ro, rd, rOuter);
                if (tNearFar.y <= tNearFar.x) discard;

                float t    = tNearFar.x;
                float tEnd = tNearFar.y;

                float gapStart = 1e30, gapLen = 0.0;
                if (_AltBase > 0.0)
                {
                    float rIn = _SphereRadius + _AltBase;
                    float b = dot(ro, rd);
                    float c = dot(ro, ro) - rIn * rIn;
                    float disc = b * b - c;
                    if (disc > 0)
                    {
                        float sd = sqrt(disc);
                        float i0 = -b - sd, i1 = -b + sd;
                        if (i1 > t)
                        {
                            if (i0 > t) { gapStart = i0; gapLen = max(i1 - i0, 0.0); }
                            else        { t = max(t, i1); }
                        }
                    }
                }

                float tPlanet = -1.0;
                {
                    float b = dot(ro, rd);
                    float c = dot(ro, ro) - _SphereRadius * _SphereRadius;
                    float disc = b * b - c;
                    if (disc > 0)
                    {
                        float tPlanetNear = -b - sqrt(disc);
                        if (tPlanetNear > 0)
                        {
                            tPlanet = tPlanetNear;
                            if (tPlanetNear < tEnd) tEnd = tPlanetNear;
                            if (gapLen > 0.0 && gapStart < tEnd) { tEnd = gapStart; gapLen = 0.0; }
                        }
                    }
                }

                bool sphereCut = false;
                if (_Occluder.w > 0.0)
                {
                    float3 roO = ro - _Occluder.xyz;
                    float  bO  = dot(roO, rd);
                    float  cO  = dot(roO, roO) - _Occluder.w * _Occluder.w;
                    if (cO > 0.0)
                    {
                        float dO = bO * bO - cO;
                        if (dO > 0.0)
                        {
                            float tO = -bO - sqrt(dO);
                            if (tO > 0.0 && tO < tEnd)
                            {
                                tEnd = tO; sphereCut = true;
                                if (gapLen > 0.0 && gapStart + gapLen > tEnd) { if (gapStart < tEnd) tEnd = gapStart; gapLen = 0.0; }
                            }
                        }
                    }
                }

                float tScene  = 1e30;
                float depthKm = 0.0;
                bool  nearHit = false;
                if (_UseSceneDepth > 0.5)
                {
                    float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                    float sceneZ   = LinearEyeDepth(rawDepth);
                    float3 camFwd  = -UNITY_MATRIX_V[2].xyz;
                    tScene  = sceneZ / max(dot(rd, camFwd), 0.05);
                    nearHit = sceneZ < _SphereRadius * 0.05;
                    if (nearHit && tScene < tEnd)
                    {
                        tEnd = tScene;
                        if (gapLen > 0.0 && gapStart + gapLen > tEnd) { if (gapStart < tEnd) tEnd = gapStart; gapLen = 0.0; }
                    }

                    depthKm = nearHit ? min(sceneZ, 1e7) * 0.001 : 0.0;
                }

                bool bodyCut = tPlanet > 0.0 || sphereCut;
                if (!nearHit && bodyCut) depthKm = -1.0;

                if (_DebugMode > 6.5 && _DebugMode < 7.5)
                {
                    float3 c = nearHit ? float3(1, 0, 0) : (bodyCut ? float3(0, 1, 0) : float3(0, 0, 1));
                    return outNF(c * 2.0, float3(0, 0, 0), 1.0, depthKm, 0.0);
                }

                if (_DebugMode > 7.5 && _DebugMode < 8.5)
                {
                    float3 c = float3(0, 0, 0);
                    if (_UseSceneDepth > 0.5)
                    {
                        float z = tScene * max(dot(rd, -UNITY_MATRIX_V[2].xyz), 0.05);
                        float l = log10(max(z, 1.0));
                        if      (z >= _ProjectionParams.z * 0.98) c = float3(1, 1, 1);
                        else if (l >= 6.0) c = float3(1, 0, 0);
                        else if (l >= 5.0) c = float3(1, 0.5, 0);
                        else if (l >= 4.0) c = float3(1, 1, 0);
                        else if (l >= 3.0) c = float3(0, 1, 0);
                        else if (l >= 2.0) c = float3(0, 1, 1);
                        else               c = float3(0, 0, 1);
                    }
                    return outNF(c * 2.0, float3(0, 0, 0), 1.0, depthKm, 0.0);
                }

                if (_DebugMode > 8.5 && _DebugMode < 9.5)
                {
                    float3 c = float3(0, 0, 0);
                    if (_OccluderDepthParams.z > 0.5)
                    {
                        float iM = tex2Dlod(_OccluderDepth, float4(i.pos.xy * _OccluderDepthParams.xy, 0.0, 0.0)).r;
                        if (iM <= 0.0) c = float3(0, 0, 0.3);
                        else
                        {
                            float l = log10(max(1.0 / iM, 1.0));
                            if      (l >= 6.0) c = float3(1, 0, 0);
                            else if (l >= 5.0) c = float3(1, 0.5, 0);
                            else if (l >= 4.0) c = float3(1, 1, 0);
                            else if (l >= 3.0) c = float3(0, 1, 0);
                            else if (l >= 2.0) c = float3(0, 1, 1);
                            else               c = float3(0, 0, 1);
                        }
                    }
                    return outNF(c * 2.0, float3(0, 0, 0), 1.0, depthKm, 0.0);
                }

                float tCloud = 1e30;
                {
                    float rCloud = _SphereRadius + max(_CloudTopAlt, 0.0);
                    float bC = dot(ro, rd);
                    float cC = dot(ro, ro) - rCloud * rCloud;
                    float dC = bC * bC - cC;
                    if (dC > 0.0)
                    {
                        float sdC = sqrt(dC);
                        if (dot(ro, ro) > rCloud * rCloud)
                        {
                            float tn = -bC - sdC;
                            if (tn > 0.0) tCloud = tn;
                        }
                        else
                        {
                            float tf = -bC + sdC;
                            tCloud = tf;
                        }
                    }
                    if (_ForceFar > 0.5) tCloud = 0.0;
                }

                float slabChord = 0.0;
                {
                    float rTop  = _SphereRadius + max(_CloudTopAlt, 0.0);
                    float rBase = _SphereRadius + clamp(_CloudBaseAlt, 0.0, max(_CloudTopAlt, 0.0) * 0.9);
                    float thick = max(rTop - rBase, 1e-4 * _SphereRadius);
                    float bS = dot(ro, rd), rrS = dot(ro, ro);
                    float dT = bS * bS - (rrS - rTop * rTop);
                    if (dT > 0.0)
                    {
                        float sT = sqrt(dT);
                        float t0 = max(-bS - sT, 0.0), t1 = -bS + sT;
                        if (t1 > t0)
                        {
                            float Lc = t1 - t0;
                            float dB = bS * bS - (rrS - rBase * rBase);
                            if (dB > 0.0)
                            {
                                float sB = sqrt(dB);
                                float lo = max(max(-bS - sB, 0.0), t0), hi = min(-bS + sB, t1);
                                if (hi > lo) Lc -= hi - lo;
                            }
                            slabChord = Lc / thick;
                        }
                    }
                }

                if (tEnd <= t) discard;

                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return outNF(float3(0.0, 0.0, 2.0), float3(0, 0, 0), 1.0, depthKm, 0.0);

                float shell = max(_AltTop - _AltBase, 1e-5);
                float dtMin = max(_StepLength, 0.0001) * _SphereRadius * clamp(_StepNear, 0.05, 1.0);
                float dtMax = max(_StepLength, 0.0001) * _SphereRadius * max(_StepFarMul, 1.0);
                float grow  = max(_StepGrowth, 0.0);
                if (gapLen > 0.0 && gapStart + gapLen >= tEnd) { gapLen = 0.0; if (gapStart < tEnd) tEnd = gapStart; }
                float L = (tEnd - t) - gapLen;
                if (L <= 0.0) discard;

                float segEnd = (gapLen > 0.0) ? gapStart : tEnd;
                float tFar   = gapStart + gapLen;
                bool  farSeg = false;

                float jitter = (_BlueNoiseOn > 0.5)
                             ? tex2Dlod(_BlueNoise, float4(i.pos.xy * (1.0 / 64.0), 0, 0)).r
                             : frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));

                jitter = frac(jitter + _TemporalOffset);
                float offset = 0.5 + (jitter - 0.5) * _Jitter;
                float tc = t + offset * clamp(grow * t, dtMin, dtMax);

                float kShell = 0.6 / shell;
                float internalScale = 10.0 / _SphereRadius;
                int   maxN = (int)max(_MaxSteps, 8.0);
                float filt = clamp(_StepFilter, 0.2, 1.5);

                float3 colorNear = float3(0, 0, 0), colorFar = float3(0, 0, 0);

                float  alpha = 0.0, dSum = 0.0, d2Sum = 0.0, tRef = tc;
                float  time  = _AnimTime;

                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                {
                    float maxDens = 0.0, sumAlt = 0.0;
                    int hitCount = 0, nTaken = 0;
                    float tcd = tc, segEndD = segEnd; bool farD = false;
                    [loop]
                    for (int sd = 0; sd < maxN; sd++)
                    {
                        if (tcd >= segEndD)
                        {
                            if (farD || gapLen <= 0.0) break;
                            farD = true; tcd = tFar + (tcd - segEndD); segEndD = tEnd;
                            if (tcd >= segEndD) break;
                        }
                        float dtd = clamp(grow * tcd, dtMin, dtMax);
                        float pxP = max(max(tcd, 1.0) * _PixelAngle, dtd * filt) * internalScale;
                        float3 fld = auroraField(ro + rd * tcd, time, 0.0, pxP, dtd * filt * internalScale);
                        if (fld.x > 0.0001) { maxDens = max(maxDens, fld.x); sumAlt += fld.y; hitCount++; }
                        nTaken++;
                        tcd += dtd;
                    }
                    float avgAlt  = (hitCount > 0) ? (sumAlt / (float)hitCount) : 0.0;
                    float hitFrac = (float)hitCount / (float)max(nTaken, 1);
                    return outNF(float3(saturate(maxDens * 4.0), saturate(avgAlt), saturate(hitFrac * 4.0)), float3(0, 0, 0), 1.0, depthKm, 0.0);
                }

                [loop]
                for (int s = 0; s < maxN; s++)
                {
                    if (tc >= segEnd)
                    {
                        if (farSeg || gapLen <= 0.0) break;
                        farSeg = true; tc = tFar + (tc - segEnd); segEnd = tEnd;
                        if (tc >= segEnd) break;
                    }
                    float  dtc = clamp(grow * tc, dtMin, dtMax);
                    float3 p   = ro + rd * tc;

                    float detail = _DetailStrength * (1.0 - smoothstep(3.0, 12.0, tc * internalScale));

                    float stepI = dtc * filt * internalScale;
                    float pxI   = max(max(tc, 1.0) * _PixelAngle * internalScale, stepI);
                    float3 fld  = auroraField(p, time, detail, pxI, stepI);
                    if (fld.x > 0.001)
                    {

                        float dayAtten = 1.0;
                        if (_SunDir.w > 0.001 && _DaylightFade > 0.001)
                            dayAtten = 1.0 - _DaylightFade * _SunDir.w * smoothstep(-0.2, 0.05, dot(normalize(p), _SunDir.xyz));

                        float knee = saturate((1.6 - alpha) * 1.6667);
                        float contribution = fld.x * dtc * kShell * dayAtten * knee;
                        float3 add = auroraColor(fld.y) * contribution;
                        if (tc < tCloud || _FarSplit < 0.5) colorNear += add; else colorFar += add;
                        alpha += contribution;
                        float dRef = tc - tRef;
                        dSum  += contribution * dRef;
                        d2Sum += contribution * dRef * dRef;
                        if (alpha >= 1.6) break;
                    }
                    else if (fld.z > 0.0)
                    {

                        float room = farSeg ? 1e30 : (segEnd - tc);
                        float skip = min(fld.z, room);
                        if (skip > dtc) tc += skip - dtc + offset * dtc;
                    }
                    tc += dtc;
                }

                float dMean   = (alpha > 1e-6) ? dSum / alpha : 0.0;
                float tMean   = (alpha > 1e-6) ? tRef + dMean : 0.0;
                float tSpread = (alpha > 1e-6) ? sqrt(max(d2Sum / alpha - dMean * dMean, 0.0)) : 0.0;
                return outNF(colorNear * _Brightness, colorFar * _Brightness, slabChord, depthKm, tMean, tSpread);
            }
        ENDCG

        Pass
        {
            ZTest Always
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.0
            ENDCG
        }
    }
}
