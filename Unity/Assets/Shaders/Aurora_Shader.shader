// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
// =============================================================================
// Aurora_Shader.shader — volumetric polar aurora  (v3)
// =============================================================================
// Per-pixel raymarch of an auroral shell around the planet. The mesh this is
// drawn on is irrelevant to the math: every fragment just supplies a ray
// direction from the camera. In KSP the mesh is a small sphere pinned to the
// camera (so it is never clipped by the far plane); in the Unity test scene
// it is a sphere around the planet. Both work.
//
//   - Three auroral bands at slightly offset latitudes, own seeds
//   - Variable ray height from two-frequency ceiling noise
//   - Smoothed (5-tap) meandering centerline — snakes without kinks
//   - Two-zone pattern: near-uniform green base + dynamic upper layer
//   - Regional flares, flicker, domain warp
//   - Green → blue → pink altitude gradient (upper bands compressed)
//   - Independent north / south ovals (hemispheric seed offset)
//
// 1.1.0 performance (the picture is unchanged unless the temporal accumulation is on)
//   - FINE LATITUDE GATE (auroraBand): the cheap pre-gate in auroraField has to
//     allow for the meander — the LUT shifts each arc's centre by up to ±0.225
//     latNorm along its length — so it lets every sample within ±(0.23 + 0.65 ×
//     fold + width) of an arc's nominal latitude through to the fold noise, the
//     dearest thing a near-miss sample evaluates. auroraBand now reads the LUT
//     (which it did anyway) and tests the sample against the SHIFTED centre with
//     only the fold and the width as margin, before the fold is evaluated: at the
//     shipped folds about half the near-miss samples stop there. Exact — the same
//     geometric bound the pre-gate uses, with the meander known instead of
//     bounded. (A longitude × latitude coverage map baked per frame was tried
//     first; the ribbon's distance measure |F| / |∇F| is not smooth enough across
//     a texel for a conservative map at any affordable size — see the 1.1.0 notes.)
//   - TEMPORAL ACCUMULATION (AuroraBlit pass 4): the renderer rotates the sample
//     offsets each frame (_TemporalOffset: a golden-ratio shift of the blue-noise
//     value, which keeps its spatial distribution) and tilts every ray by a
//     sub-pixel amount (_RayJitter), so successive frames sample different
//     depths and different sub-pixels; the composite shader averages them into
//     a history buffer reprojected through the camera's motion. The march writes
//     a third target: the density-weighted mean distance of what it saw, which
//     the reprojection uses as the pixel's depth.
//   - _StepFilter: the fraction of the step the noise is filtered to (it was a
//     constant ¾). With accumulation on, a smaller value keeps finer rays with
//     coarser steps — the aliasing a single frame shows is what the accumulation
//     averages away.
//   The temporal globals (_RayJitter, _TemporalOffset) are set per draw by the
//   renderer's command buffer, not on the material: the scaled-space instances copy
//   the local material every frame, and a global survives that. Build tag 1110.
//
// v3 changes
//   - Meander + flare come from a small lookup texture (_MeanderTex,
//     longitude × band/hemisphere) that C# computes on the CPU each frame
//     (AuroraMeanderTexture.cs). Same math, but 6 of the 15 fbm calls per
//     sample are gone, and the latitude gate is exact from the first test.
//   - Step length is a function of distance from the camera (1.1.0: from
//     _StepLength × _StepNear × R up close, growing by _StepGrowth per unit of
//     distance, to _StepLength × _StepFarMul × R; _MaxSteps is a safety cap;
//     per-pixel jittered) instead of "N steps
//     per chord", so a thin shell on a big planet is sampled as finely as
//     the Unity test scene and raising the height never thickens the layers.
//   - Integration is normalised to the shell thickness: changing the aurora
//     height no longer changes its brightness.
//   - _RayThreshold / _RayGain expose the ray contrast; _DetailStrength adds
//     two extra noise octaves to the rays when the camera is close.
//   - _UseSceneDepth = 1 (set by the KSP plugin) stops the march at the
//     camera depth buffer, so terrain and the vessel occlude the aurora.
//
// v3.1 changes
//   - Ribbon width is a true distance to the fold contour (|F|/|∇F|, with the
//     fold gradient from analytic noise derivatives): uniform thickness round
//     every curl instead of thin runs + fat hairpins.
//   - _BandSpacing / _BandIndependence: the three arcs no longer have to be
//     nested parallel contours of one field.
//   - Colour stops always blend over ≥ ±6 % of the height; the vertical
//     envelope and the upper-band compression are smooth (no horizontal
//     seams at fixed fractions of the height).
//   - Output: rgb = aurora, a = aurora irradiance under the pixel, which the
//     composite multiplies with the clouds' own rendered colour to light the
//     cloud tops (the flat "ground glow" overlay is gone).
//
// v3.18 changes (performance; the picture is unchanged)
//   - Per-sample geometry (scaling, radius, latitude, longitude, the
//     surface-projected position and its local frame) is computed once in
//     auroraField and shared by the three bands.
//   - A sample farther from the fold contour than the band can ever be wide
//     returns before the ceiling noise; the green-ray noise is evaluated only
//     once the sample is known to be inside the band body; noise octaves
//     faded to nothing are not evaluated; with band independence 0 the fold
//     field is evaluated once and shared.
//   - auroraField returns a skip distance when every arc rejects the sample
//     at the latitude pre-gate, and the march jumps over the samples that
//     are provably zero.
//   - The emission bake returns early for texels outside every arc's
//     latitude range.
//
// v3.16 changes
//   - far.a = the scene depth the texel's march was cut against (km), for
//     the composite's depth-aware upsample (crisp silhouettes).
//
// v3.15 changes
//   - Storm-driven oval: the KSP storm scheduler now drives _LatCenter,
//     _BandSpacing and _LatWidth per frame (quiet: arcs close together and
//     poleward; storm: spread apart and shifted toward the equator) and
//     fades the second and third arcs in through _Arc2Fade / _Arc3Fade. An
//     arc at 0 is not evaluated at all.
//
// v3.14 changes
//   - _RayTaper: the ray threshold rises with height, so columns are widest
//     at their base and narrow to tips at the top (they used to taper the
//     other way). _RayZone chooses where the rays live relative to the
//     smooth base glow (+1 original, 0 rays to the bottom edge, -1 rays
//     under a diffuse top).
//
// v3.13 changes
//   - The ray noise's octaves are rotated 37° against each other (and the
//     two close-range octaves 61° further): thresholded value noise on one
//     set of axes made square pillars with straight sides, plain to see
//     from under the band. Ray / spike pattern only — the fold field and
//     its CPU twin are untouched, so the ribbon's path does not change.
//
// v3.12 changes
//   - Two colour targets: the aurora met before the ray's first crossing of
//     the cloud-top shell and the aurora met after it. The composite applies
//     EVE's per-pixel cloud transmittance to the second only, so the clouds
//     occlude exactly the aurora behind them at every altitude — no
//     composite-order switch.
//
// v3.11 changes
//   - The emission bake takes four sub-samples per texel with the noise
//     filtered to ~3 km (as on screen) instead of to the texel; the minimum-
//     width dimming is applied after the ray threshold; cloud light gain ×4.
//
// v3.10 changes
//   - The noise is filtered to the SAMPLE SPACING, not just the pixel: the
//     footprint handed to fbmAA is max(pixel, ¾ step), and the band's bottom
//     and top edges are never sharper than it. Content finer than the step
//     cannot be integrated along the ray; sampled anyway it aliased into
//     hard-edged blocks that jumped with the animation (seen from under the
//     band, where 3 km steps met 1–2 km rays).
//   - The step shrinks near the band (_StepNear × _StepLength as the march
//     start comes within half a planet radius), so the detail that filtering
//     removes far away is really sampled where it is large on screen.
//
// Coordinates: all positions are planet-relative (C# pushes _PlanetCenter per
// frame for KSP's floating origin); inside auroraBand() positions are
// normalized to an internal planet radius of 10 so the noise behaves the same
// at Unity test scale (_SphereRadius = 10) and KSP scale (600000).
//
// Animation is driven by _AnimTime (accumulated by C#), not _Time.y.
//
// Output is linear HDR, additive. In KSP a blit pass tonemaps it.
//
// _DebugMode: 0 normal | 1 solid red (mesh test) | 2 solid blue (shell hit)
//             6 probe: R = max density, G = avg altNorm, B = hit fraction
// =============================================================================

Shader "RVA/Aurora"
{
    Properties
    {
        _Activity    ("Activity", Range(0, 1)) = 0.3

        _ColorLow    ("Color (low — green)",  Color) = (0.4, 0.9, 0.5, 1)
        _ColorMid    ("Color (mid — blue)",   Color) = (0.4, 0.2, 0.9, 1)
        _ColorHigh   ("Color (high — pink)",  Color) = (0.9, 0.3, 0.5, 1)

        // Vertical colour layout, in 0..1 of the band height:
        //   pure green up to _GreenTop, fades to blue by _BlueBottom, pure blue
        //   until _BlueTop, fades to pink by _PinkBottom, pure pink above.
        _GreenTop    ("Green until",  Range(0, 1)) = 0.05
        _BlueBottom  ("Blue from",    Range(0, 1)) = 0.5
        _BlueTop     ("Blue until",   Range(0, 1)) = 0.5
        _PinkBottom  ("Pink from",    Range(0, 1)) = 1.0
        _GainLow     ("Green brightness", Range(0, 3)) = 1.0
        _GainMid     ("Blue brightness",  Range(0, 3)) = 1.0
        _GainHigh    ("Pink brightness",  Range(0, 3)) = 1.0
        // Extra density toward the top of the band (makes pink read from above)
        _HighAltBoost   ("High-altitude density boost", Range(0, 3)) = 0.6
        // How much the upper (blue/pink) part is squeezed into less height
        _CompressFactor ("Upper band compression", Range(0.2, 1)) = 0.55

        // Ray contrast: density = saturate((pattern - threshold) * gain)
        _RayThreshold   ("Ray threshold (higher = sparser)", Range(0, 0.6)) = 0.3
        _RayGain        ("Ray contrast (higher = crisper)", Range(0.5, 5)) = 1.4
        // The threshold rises with height (by 0.2 × taper at the top): each
        // column is widest at its base and narrows to a tip at the top, the
        // way real rays are crisp and broad at the lower border and fade to
        // thin tips above. Brightness profile and colours are untouched.
        _RayTaper       ("Ray taper (columns narrow toward the top)", Range(0, 1)) = 0.4
        // Where the ray pattern lives vs the smooth base glow: +1 = rays above
        // a smooth lower glow (original), 0 = rays from the bottom edge up,
        // -1 = rays at the bottom under a smooth, diffuse top
        _RayZone        ("Ray zone", Range(-1, 1)) = 1.0
        // Extra fine structure in the rays, fades in as the camera gets close
        _DetailStrength ("Close-range detail", Range(0, 1)) = 0.5

        _NoiseScale  ("Noise scale", Range(0.1, 5.0)) = 2.5

        _AnimSpeed   ("Animation speed", Range(0, 5)) = 0.35
        _AnimTime    ("Anim time (driven by C#)", Float) = 0.0

        // Bottom and top of the band above the surface (world units). The band's
        // internal structure spans base..top, so raising _AltBase lifts the whole
        // thing; the storm scheduler drives _AltTop.
        _AltBase     ("Altitude base",  Float) = 0.0
        _AltTop      ("Altitude top",   Float) = 2.0

        _LatCenter   ("Latitude center", Range(0, 1)) = 0.65
        _LatWidth    ("Ribbon half-width", Range(0, 0.6)) = 0.03
        // The ribbon is the zero contour of  lat − centre + fold(x,z): contours
        // of a noise field curl, fold and wrap back on themselves. Its width is
        // measured as a true distance to that contour (|F| / |∇F|), so the
        // ribbon is equally thick in its straight runs and in its hairpins.
        _FoldStrength   ("Ribbon folds (amplitude)", Range(0, 0.3)) = 0.12
        _FoldScale      ("Ribbon fold scale", Range(0.2, 4)) = 1.0
        // The three bands: spacing scales their latitude offsets (0 = all on one
        // line, 1 = original ±4.5°); independence rotates each band's fold field
        // away from the shared one, so the bands cross and merge instead of
        // running as nested contour lines (0 = parallel copies, 1 = each alone)
        _BandSpacing    ("Band spacing", Range(0, 2)) = 1.0
        _BandIndependence ("Band fold independence", Range(0, 1)) = 0.7
        // Visibility of the second and third arcs (0 = absent, 1 = full). The
        // KSP storm scheduler fades them in as a storm builds; an absent arc
        // costs nothing. _LatCenter / _BandSpacing / _LatWidth are likewise
        // driven per frame by the scheduler (quiet oval narrow and poleward,
        // storm oval wide and shifted toward the equator).
        _Arc2Fade       ("Second arc visibility", Range(0, 1)) = 1.0
        _Arc3Fade       ("Third arc visibility", Range(0, 1)) = 1.0
        // 0 = soft Gaussian-like edge, 1 = hard edge with a thin anti-aliased rim
        _EdgeSharpness  ("Ribbon edge sharpness", Range(0, 1)) = 0.6
        // The ribbon widens toward its top: the diffuse red/pink cap above the crisp green
        _TopSpread      ("Top spread (diffuse cap)", Range(0, 3)) = 0.8
        // Ray fineness: 1 = the original 60 km rays, 2.5 = thin 25 km columns
        _RayScale       ("Ray fineness", Range(0.5, 6)) = 2.5
        // Height (0..1 of band) over which the bottom edge fades in: small = crisp lower border
        _BottomSoftness ("Bottom edge softness", Range(0.02, 0.3)) = 0.07
        // Band thickness varies along the band: width × (1 ± variance)
        _WidthVariance  ("Band width variance", Range(0, 3)) = 0.0   // inspector range only (v3.23b); the plugin sets it unclamped — no rebuild needed for the wider slider
        // How much the local top height varies along the band (1 = 30..100 % of
        // AltTop, the original look; 0 = flat top)
        _HeightVariance ("Band height variance", Range(0, 1)) = 1.0
        // Sharp thin columns that stretch the local top upward (SPVE-style spikes)
        _Spikiness      ("Spike height", Range(0, 2)) = 0.0
        _SpikeThreshold ("Spike sparsity (higher = fewer)", Range(0.3, 0.9)) = 0.6
        // Regions where the rays stay green all the way up into the blue/pink heights
        _GreenSpikes    ("Green rays into upper bands", Range(0, 1)) = 0.0

        _SphereRadius ("Sphere radius", Float) = 10.0
        // 1.1.0, another body's aurora drawn from here: a sphere that hides it (the body the
        // vessel is at — xyz relative to this aurora's planet centre, w = radius; w = 0 none)
        _Occluder     ("Occluder sphere (set by C#)", Vector) = (0, 0, 0, 0)
        // 1.1.0: 1 = every sample counts as beyond the cloud tops (a distant body's aurora is
        // behind this world's clouds everywhere), so the composite occludes all of it per pixel
        _ForceFar     ("All samples to the far target", Float) = 0

        // Sampling: step length is a fraction of the planet radius (0.005 = 3 km
        // on Kerbin, 0.05 units in the Unity test scene), at most MaxSteps per
        // ray. Jitter offsets each pixel's samples by up to one step so the
        // sample planes don't show as layers.
        _StepLength  ("Step length (fraction of planet radius)", Range(0.0005, 0.05)) = 0.005
        // The step shrinks to this fraction of _StepLength as the march start
        // comes within half a planet radius of the camera (0.25 = 750 m steps
        // from under the band on Kerbin); 1 = fixed steps everywhere. The
        // noise is always filtered to the step, so coarse steps mean smooth
        // rays, never blocks.
        _StepNear    ("Step scale near the band", Range(0.1, 1)) = 0.25
        // 1.1.0: the step grows with distance from the camera — dt = clamp(_StepGrowth × t,
        // _StepLength × _StepNear × R, _StepLength × _StepFarMul × R) — and no longer depends on
        // the ray's total length through the shell (which made neighbouring rays sample the
        // same aurora at very different densities: the seam along the band's lower horizon).
        _StepGrowth  ("Step growth per unit distance", Range(0, 0.1)) = 0.03
        _StepFarMul  ("Far step (× step length)", Range(1, 8)) = 3
        _MaxSteps    ("Max steps per ray", Range(8, 512)) = 192
        _Jitter      ("Sample jitter", Range(0, 1)) = 0.75
        // 1.1.0: the per-pixel offset comes from this blue-noise tile when the plugin
        // supplies one (AuroraBlueNoise.cs); gradient noise otherwise. Under-sampled
        // features show the offset pattern through — gradient noise as stripes, blue
        // noise as grain the upsample smooths away.
        _BlueNoise   ("Blue-noise tile (set by C#)", 2D) = "gray" {}
        _BlueNoiseOn ("1 = use the tile", Float) = 0.0
        // 1.1.0: the noise is filtered to this fraction of the march step (it was a constant ¾):
        // content finer than the sample spacing cannot be integrated and aliases into blocks.
        // With the temporal accumulation on, lower values keep finer rays at coarser steps.
        _StepFilter  ("Noise filter width (× step)", Range(0.2, 1.5)) = 0.75

        _Brightness  ("Brightness", Range(0, 40)) = 8.0


        // Longitude × (band, hemisphere) lookup: R = meander shift + 0.5,
        // G = flare 0..1. Generated by AuroraMeanderTexture.cs. Gray = neutral.
        _MeanderTex  ("Meander/flare LUT (set by C#)", 2D) = "gray" {}

        // 1 = clip the march at _CameraDepthTexture (KSP). Leave 0 in the Unity
        // test scene unless its camera has depthTextureMode = Depth.
        _UseSceneDepth ("Occlude by scene depth (0 or 1)", Float) = 0.0
        // Aurora irradiance on the cloud deck under each pixel, written to the
        // output ALPHA (AuroraBlit pass 2 multiplies it onto EVE's clouds).
        // It is read from the EMISSION MAP: pass 1 of this shader bakes the
        // aurora's own visible density into a small longitude × latitude
        // texture every frame, and the irradiance is that map blurred by
        // _CloudLightBlur (its mip chain does the blurring). So the glow sits
        // exactly where the curtains are, bright under bright curtains, gone
        // where there is no aurora, and without the curtains' fine structure.
        // Night side only when _SunDir is set. w = the daylight weight (1 sunlit, 0 off / eclipsed by the parent body).
        _CloudLight    ("Cloud light (irradiance gain)", Range(0, 10)) = 0.0
        // Blur radius of that light on the clouds (world units)
        _CloudLightBlur ("Cloud light blur", Float) = 60000.0
        _EmissionMap   ("Aurora emission map (set by C#)", 2D) = "black" {}
        _EmissionMapSize ("Emission map size (set by C#)", Vector) = (512, 128, 0, 0)
        // Altitude of the cloud tops (world units): the irradiance is evaluated
        // where the view ray crosses that shell, so clouds at the horizon and
        // clouds seen from below get the right receiving point.
        _CloudTopAlt   ("Cloud top altitude (set by C#)", Float) = 0.0
        // 1 = the aurora beyond the first crossing of the cloud-top shell goes
        // to the second colour target (the composite occludes it with EVE's
        // cloud transmittance) — set by the KSP renderer, which binds two
        // targets; 0 = everything to the first (Unity test scene, direct mode)
        _FarSplit      ("Split at the cloud shell (0 or 1)", Float) = 0.0
        // Sun direction (world space, unit length, w = 1 when valid). Set by
        // the KSP plugin; left at 0 the aurora is never faded by daylight.
        _SunDir        ("Sun direction (set by C#)", Vector) = (0, 0, 0, 0)
        _DaylightFade  ("Hide in daylight", Range(0, 1)) = 1.0

        // Radians per pixel of the aurora buffer (set by C#). Lets the noise
        // fade octaves finer than a pixel (procedural mip-mapping) and keeps the
        // ribbon at least ~1.5 px wide, so thin bands and fine rays don't alias.
        // 0 = no filtering (Unity test scene).
        _PixelAngle  ("Pixel angle (set by C#)", Float) = 0.0

        _DebugMode   ("Debug mode (0 = off)", Float) = 0.0

        // 1.1.0: build tag. The plugin reads this default when it loads the bundle and
        // compares it with RVABootstrap.ShaderBuild; a mismatch means the DLL and the
        // bundle were built from different sources (new features silently inert). Bump
        // both together whenever a uniform is added or renamed.
        _RVAShaderBuild ("Shader build (read by C#)", Float) = 1110
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
            float  _CloudLight;
            float  _RayThreshold, _RayGain, _RayTaper, _RayZone, _DetailStrength;
            float  _NoiseScale, _AnimSpeed, _AnimTime;
            float  _AltBase, _AltTop;
            float4 _PlanetCenter;
            float  _LatCenter, _LatWidth;
            float  _FoldStrength, _FoldScale, _EdgeSharpness, _TopSpread, _RayScale, _BottomSoftness;
            float  _BandSpacing, _BandIndependence, _Arc2Fade, _Arc3Fade;
            float  _WidthVariance, _HeightVariance, _Spikiness, _SpikeThreshold, _GreenSpikes;
            float  _CloudTopAlt, _CloudLightBlur, _FarSplit;
            sampler2D _EmissionMap;
            float4 _EmissionMapSize;
            float  _SphereRadius;
            float4 _Occluder;
            float  _ForceFar;
            float  _StepLength, _StepNear, _MaxSteps, _Jitter, _StepGrowth, _StepFarMul, _StepFilter;
            sampler2D _BlueNoise;
            float  _BlueNoiseOn;
            // 1.1.0 temporal accumulation — GLOBALS, set per draw by AuroraRenderer's command buffer (see the header)
            float4    _RayJitter;        // sub-pixel ray tilt: xy = (right, up) per unit of forward; 0 = none
            float     _TemporalOffset;   // per-frame shift of the sample offsets (0 = the static pattern)
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

            // ---------- noise (AuroraNoise.cs / AuroraLightRing.cs: line-for-line ports)
            // Lattice hash (v3.20): integer arithmetic on the lattice point, so a
            // corner hashes to the same value whichever cell asks for it — exact on
            // every GPU, and on the CPU twins. The float hash it replaces,
            // frac(p × K) feeding a product, turned a one-ulp difference in how a
            // corner coordinate was formed into an unrelated value, and a driver is
            // free to form the "i + 1" corners of one cell differently from the
            // "i" corners of the next: the field was discontinuous across lattice
            // lines. Along the time axis, which every pixel shares, that was the
            // whole pattern switching once per lattice period (6 s at the shipped
            // speed). Values are 24-bit and uniform in [0, 1).
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

            // fbm whose octaves fade to their mean once their cells are smaller
            // than ~a pixel: the noise equivalent of mip-mapping. cellSize is the
            // first octave's cell size and px the pixel footprint, both in the
            // same units. Keeps the mean (so thresholds stay put), removes the
            // variance that would alias.
            // Rotation in the horizontal (x, z) plane; y (time) untouched
            float3 rotXZ(float3 p, float c, float s)
            {
                return float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
            }

            // Each octave is turned 37° against the previous one (v3.13). Value
            // noise lives on a square lattice and a hard threshold cuts along
            // it; with every octave on the same axes the rays came out as
            // square pillars with straight sides, obvious from underneath.
            // Turned, no two octaves share an edge direction and the cells
            // stop reading as boxes. The first octave is left as it was, so
            // the overall pattern keeps its phase.
            float fbmAA(float3 p, float cellSize, float px)
            {
                float v = 0.0, amp = 0.5, size = cellSize;
                px = max(px, 1e-6);
                for (int i = 0; i < 3; i++)
                {
                    float fade = saturate((size / px - 1.0) * 0.5);   // full at ≥ 3 px, gone at ≤ 1 px
                    // an octave faded all the way out contributes exactly amp × 0.5:
                    // don't evaluate the noise for it (v3.18, identical result)
                    if (fade > 0.0) v += amp * lerp(0.5, noise3(p), fade);
                    else            v += amp * 0.5;
                    p = rotXZ(p, 0.7986355, 0.6018150) * 2.0 + float3(3.7, 0.0, 1.9);   // 37°, and off the lattice corners
                    amp *= 0.5;
                    size *= 0.5;
                }
                return v * 1.143;
            }

            // Value noise with its analytic gradient: .x = the same value as
            // noise3(), .yzw = d/dp. Costs a handful of extra multiplies, so the
            // fold field's slope comes for free instead of from 2 extra fbm calls.
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

            // fbm() with the gradient of the sum (same value as fbm())
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

            // Ribbon fold displacement (latNorm units) at a surface-projected
            // horizontal position, plus its gradient w.r.t. that position
            // (.yzw = d/dx, d/dy, d/dz). One fold field for all bands; each band
            // samples it through its own rotated + shifted domain, scaled by
            // _BandIndependence (0 = the same field for all three → nested
            // parallel contours, 1 = three uncorrelated fields that cross and
            // merge). A rotation never collapses or stretches the field, so
            // every independence value gives folds of the same size.
            // AuroraNoise.Fold is the CPU twin (value only).
            // 1.1.0: the field is sampled at the point on the planet's surface (radius
            // 10) under the sample, in 3D, not at its projection onto the equatorial
            // plane as before — that projection squeezed the poleward direction by
            // sin(latitude), stretching every curl and ray more and more toward the
            // equator, which a storm that reaches the equator could not hide.
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
                // gradient back through the rotation (R^T, about the polar axis) and the scale
                float gx = (n.y * ca + n.w * sa) * sc;
                float gy = n.z * sc;
                float gz = (-n.y * sa + n.w * ca) * sc;
                return float4(n.x - 0.5, gx, gy, gz) * foldStrength;
            }

            // Arc-length distance (internal units, 1 = R/10) from a point at
            // normalised latitude `lat`, projected horizontal position hpN, to
            // the ribbon's centre line F = lat − centre + fold = 0, using the
            // gradient of F: |F| / |∇F|. Without this the ribbon is only as
            // wide as its *latitude* gate, i.e. thin where the fold is steep and
            // a fat blob at every hairpin.
            // Everything about a sample point that does not depend on which band
            // is being evaluated — computed once per sample in auroraField and
            // handed to the three bands (v3.18; it used to be recomputed, trig
            // included, in each of them).
            struct SampleGeo
            {
                float3 p;               // internal units (planet radius = 10)
                float  r;
                float  altitude;        // above the band's bottom
                float  latNorm;
                float  lon01;
                float  hemi;
                float3 hp;              // the point on the planet's surface under the sample (radius 10), 1.1.0
                float3 hpN;             // the same (kept for the noise domains that read it)
                float  internalAltTop;  // band thickness, internal units
                float3 east3, north3;   // tangent frame at hpN for ribbonDistance; north3 points toward THIS set's pole
            };

            float ribbonDistance(float F, float4 fold, SampleGeo geo)
            {
                // (east, poleward) components of ∇F on the surface, latNorm per unit of
                // arc length: latitude rises 1/(10·π/2) per unit poleward, plus the fold's
                // own slope along each direction
                float  gE = dot(fold.yzw, geo.east3);
                float  gN = dot(fold.yzw, geo.north3);
                float2 gradArc = float2(gE, 0.063662 + gN);
                return abs(F) / max(length(gradArc), 0.01);
            }

            // ---------- one auroral band ---------------------------------------
            // bandIndex selects the LUT row (0..2); detail = 0..1 extra octaves;
            // pxI = sample footprint for the noise, internal units (0 = unfiltered);
            // envPx = footprint the vertical envelope's edges are softened to
            // (the march step; 0 = the band's own edge softness, emission map).
            // The caller has already applied the cheap latitude pre-gate.
            // foldCache / foldCached: with _BandIndependence = 0 the three bands
            // sample one and the same fold field, so the first band to need it
            // computes it and the others reuse it (bit-identical: the rotation
            // is by 0 and the offset is × 0).
            // What a ribbon is made of, apart from where it is (1.1.0): the three
            // polar arcs pass the uniforms unchanged (mainTweak); anything that wants
            // a thinner ribbon, a fold of its own size or no meander shift passes its own.
            struct BandTweak
            {
                float latWidth;      // half-width, latNorm units
                float foldStrength;  // fold amplitude, latNorm units
                float foldZoom;      // the fold field is sampled at hpN × this (curls this many times smaller)
                float shiftScale;    // × the LUT's meander shift (0 = none)
                float widthVar;      // width × (1 ± this) along the ribbon
                float topSpread;     // widening toward the top
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
                // time moves every 3D noise domain along this direction (the old
                // domains were 2D slices with time in the unused axis; the surface
                // point now fills all three)
                const float3 drift = float3(0.57735, 0.57735, 0.57735);
                float altitude       = geo.altitude;
                float internalAltTop = geo.internalAltTop;
                float latNorm        = geo.latNorm;
                float hemi           = geo.hemi;
                float3 hp            = geo.hp;
                float3 hpN           = geo.hpN;
                float3 p             = geo.p;

                // Meander shift, flare and width noise for this longitude, band,
                // hemisphere (CPU-generated LUT)
                float rowV  = ((float)bandIndex * 2.0 + hemi + 0.5) / 6.0;
                float4 lut  = tex2Dlod(_MeanderTex, float4(geo.lon01, rowV, 0, 0));
                float bandShift = (lut.r - 0.5) * tw.shiftScale;
                float flare     = lerp(0.7, 1.6, lut.g);
                // local thickness: width × (1 ± variance), driven by slow noise along the band
                float widthScale = lerp(1.0 - tw.widthVar, 1.0 + tw.widthVar, lut.b);
                float localWidth = tw.latWidth * max(widthScale, 0.15);

                // FINE LATITUDE GATE (1.1.0): the same geometric bound auroraField's pre-gate
                // applies — the ribbon lies within its half-width of a centre line that the
                // fold moves by at most ±0.65 × its amplitude — but around the centre as the
                // meander actually shifts it here (the LUT above), not the nominal centre ± the
                // meander's whole ±0.225 range, and with this longitude's own width rather than
                // the widest. Also the pixel minimum the ribbon is widened to when it is far
                // away (never thinner than 1.5 px — wMin below). At the shipped folds about
                // half the near-miss samples end here, before the fold noise. Conservative in
                // the same sense as the pre-gate: the ribbon's own measure |F| / |∇F| could
                // reach past this bound only where the fold sits near an extreme and is steep
                // at the same time, which smooth noise does not do.
                {
                    float wLat = max(localWidth * (1.0 + max(tw.topSpread, 0.0)), pxI * 1.5 * 0.063662);   // latNorm (1 arc unit = 0.0637 latNorm)
                    float fineGate = wLat + 0.65 * tw.foldStrength + 0.01;
                    if (abs(latNorm - (bandLatCenter + bandShift)) > fineGate) return float2(0, 0);
                }

                // Ribbon folds. The ribbon is the zero contour of
                //     F = latNorm − (centre + meander) + fold(x, z)
                // A contour of a smooth noise field curls, folds and wraps back
                // on itself — the ISS-photo look a single-valued centre line
                // can never give. dist is the true arc distance to that contour
                // (internal units), so the ribbon keeps one thickness all the
                // way round its curls. AuroraLightRing/AuroraNoise.Fold is the CPU twin.
                float4 fold;
                if (_BandIndependence <= 0.0)
                {
                    if (foldCached < 0.5)
                    {
                        foldCache = foldAt(hpN * tw.foldZoom, t, hemi, seedOffset, tw.foldStrength);
                        foldCache.yzw *= tw.foldZoom;                      // chain rule for the zoomed domain
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

                // EXACT EARLY-OUT (v3.18, tightened v3.19): the cross-section
                // gate below is zero whenever dist ≥ wEff. wEff grows with
                // altNormRaw = altitude / localCeiling, and the ceiling noise can
                // lower the ceiling to no less than internalAltTop × (1 − 0.7 ×
                // _HeightVariance) — so at this altitude altNormRaw is at most
                // altitude / that floor, and wEff at most the width for that
                // value (or the pixel minimum). A sample farther from the
                // contour than that is empty before the ceiling noise is even
                // looked at. v3.18 used the width at the very top for every
                // altitude; low in the band, where the ribbon is thin, this is
                // up to four times tighter. Most samples inside the latitude
                // pre-gate end here.
                float wMin    = pxI * 1.5;
                float ceilMin = min(internalAltTop * (1.0 - 0.7 * _HeightVariance), internalAltTop);
                float anMax   = (ceilMin > 1e-6) ? saturate(altitude / ceilMin) : 1.0;
                float wMaxHere = localWidth * (1.0 + max(tw.topSpread, 0.0) * anMax * anMax) * 15.70796;
                if (dist >= max(wMaxHere, wMin)) return float2(0, 0);

                // Variable ceiling (band body)
                float c1 = fbm(hp * 0.137 * _NoiseScale
                            + float3( 13.41 + seedOffset,  0,  7.93 + seedOffset)
                            + float3(t * 0.04, 0, t * 0.06));
                float c2 = fbm(hp * 0.318 * _NoiseScale
                            + float3(-29.17 + seedOffset, 0, 41.23 + seedOffset)
                            + float3(t * 0.07, 0, t * 0.03));
                float ceilingNoise = saturate(c1 * 0.65 + c2 * 0.55 - 0.1);
                float localCeiling = lerp(internalAltTop * (1.0 - 0.7 * _HeightVariance), internalAltTop, ceilingNoise);

                // Ribbon cross-section: crisp core that widens toward the top
                // (the diffuse cap). Edge sharpness 0 = the old soft falloff,
                // 1 = plateau with a thin anti-aliased rim.
                float altNormRaw = saturate(altitude / localCeiling);
                // half-width in internal arc units (1 latNorm = 10·π/2 units)
                float wEff   = localWidth * (1.0 + tw.topSpread * altNormRaw * altNormRaw) * 15.70796;
                // Never thinner than ~1.5 pixels: widen and dim in proportion so a
                // distant ribbon stays a smooth line instead of a broken jaggy one.
                // The dimming (widthComp) is applied to the final density, AFTER
                // the ray threshold: applied to the pattern it pushed a thin
                // ribbon under the threshold, so it vanished instead of dimming
                // (v3.11).
                float widthComp = 1.0;
                if (wEff < wMin) { widthComp = wEff / wMin; wEff = wMin; }
                float latGate = smoothstep(wEff, wEff * 0.9 * _EdgeSharpness, dist);
                if (latGate <= 0.001) return float2(0, 0);

                float activityMul = 0.3 + 0.7 * _Activity;

                // Green-ray regions: slow, low-frequency; where g > 0 the colour
                // altitude is squeezed toward the bottom so rays stay green all
                // the way up (band body and spikes alike). With no spikes it is
                // only needed inside the band body, so it is evaluated after the
                // body test below (v3.18, same value).
                float g = 0.0;
                bool  needG = _GreenSpikes > 0.001;
                if (needG && _Spikiness > 0.001)
                {
                    float3 gs = hpN * (0.25 * _NoiseScale) + float3(seedOffset * 2.3 + 41.0, 0, 0) + drift * (t * 0.12);
                    g = smoothstep(0.55, 0.80, fbm(gs)) * _GreenSpikes;
                    needG = false;
                }

                // Spikes: isolated thin columns rooted inside the band, with
                // their own density — the band's own top is NOT stretched, so
                // the body never curves up into them. Column mask = cubed
                // excess over the threshold (sharp, sparse); the taller the
                // mask, the higher the column, so each spike comes to a point.
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

                // Compress the upper (blue/pink) bands. The extra stretch above
                // 0.45 is blended in over 0.35..0.55 instead of switched on at
                // 0.45: a kink in the altitude mapping shows as a horizontal
                // seam in the colour gradient.
                {
                    float extra = (altNorm - 0.45) * (1.0 / _CompressFactor - 1.0);
                    altNorm = saturate(altNorm + extra * smoothstep(0.35, 0.55, altNorm));
                }

                // Outside the band body (below it or above its local top): spike only
                if (altNorm <= 0.001 || altNorm >= 0.999) return float2(spikeDensity * widthComp, spikeColorAlt);

                // Ray taper (v3.14): the threshold rises with height, so a column's
                // footprint is widest at the band bottom and narrows to a tip at
                // the top. Before, the pattern crossfaded into the smooth glow
                // below 40 % and the density grew with height (_HighAltBoost), so
                // columns read as icicles hanging from the top.
                float thrH = _RayThreshold + _RayTaper * 0.2 * altNorm;

                // EXACT EARLY-OUTS (v3.19). The body density is
                //     saturate((rayPattern × latGate × flicker − thrH) × _RayGain)
                // and every factor is bounded: fbm and fbmAA stay below 1.000125
                // (three octaves of value noise in [0, 1), × 1.143 × 0.875), so
                // basePattern < 0.6501, upperPattern < 1.0002 + 0.225 × |detail|
                // (the detail term is (d − 0.5) × detail × 0.45 with d in [0, 1)),
                // rayPattern — their lerp, with a mix in [0, 1] for |_RayZone| ≤ 1
                // — no more than the larger of the two, and flicker < 1.0001.
                // Where that ceiling cannot clear the threshold the body is
                // exactly zero (for a non-negative gain) and none of its noise
                // needs evaluating: first judged from latGate alone (the outer
                // part of the ribbon's cross-section), then again once the
                // pattern itself is known (below the threshold — the flicker and
                // green-region noise are skipped). The tail below then runs the
                // same arithmetic with density 0, so the returned values are
                // what the full evaluation would have returned.
                bool  boundsOk = (_RayGain >= 0.0) && (abs(_RayZone) <= 1.0);
                float patMax   = max(0.6501, 1.0002 + 0.225 * abs(detail));
                float density  = 0.0;
                if (!(boundsOk && patMax * latGate * 1.0001 <= thrH))
                {
                    // Vertical density envelope: crisp rise over _BottomSoftness,
                    // a slow decline to 62 % at 0.85, fade-out to the top. All
                    // smooth — piecewise-linear segments put horizontal "strata"
                    // across the band at fixed fractions of its height. Neither
                    // edge is ever sharper than the sample footprint (one march
                    // step, see frag): an edge the march cannot resolve shows as
                    // a hard cut that moves with the sample planes (v3.10).
                    float fpN = envPx / max(localCeiling, 1e-4);               // step footprint as a fraction of the local height
                    float bs  = clamp(max(_BottomSoftness, fpN), 0.02, 0.5);
                    float ts  = clamp(1.5 * fpN, 0.15, 1.0);
                    float vEnvelope = smoothstep(0.0, bs, altNorm)
                                    * (1.0 - 0.38 * smoothstep(bs, 0.85, altNorm))
                                    * (1.0 - smoothstep(1.0 - ts, 1.0, altNorm));

                    // Domain warp (on the surface-projected position so rays stay vertical)
                    float warpStrength = 0.5 + 1.5 * _Activity;
                    float3 warpInput = hpN * 0.05 * _NoiseScale
                                     + float3(t * 0.1 + seedOffset, 0, 0);
                    float3 warp = float3(
                        fbm(warpInput),
                        fbm(warpInput + float3(17.3, 0, 0)),
                        fbm(warpInput + float3(0, 31.7, 0))
                    ) - 0.5;
                    float3 pWarped = hpN + warp * warpStrength * 2.0;

                    // Two-zone pattern
                    float3 baseInput = pWarped * 0.25 * _NoiseScale
                                     + float3(seedOffset, 0, 0) + drift * (t * 0.15);
                    float basePattern = fbm(baseInput);
                    basePattern = lerp(0.45, 0.65, basePattern);

                    float  rayCell  = 1.0 / (0.4 * _NoiseScale * _RayScale);   // first-octave cell, internal units
                    float3 raySpace = pWarped * 0.4 * _NoiseScale * _RayScale + float3(seedOffset, 0, 0);
                    float3 rayInput = raySpace + drift * (t * 0.3);
                    float upperPattern = fbmAA(rayInput, rayCell, pxI);

                    // Close-range detail: two more octaves on the rays (horizontal
                    // frequency ×7 and ×16, time only ×2 so it doesn't strobe),
                    // each fading out once it is finer than the pixel footprint.
                    // An octave faded all the way out is not evaluated (v3.18).
                    if (detail > 0.001)
                    {
                        float px = max(pxI, 1e-6);
                        float f7  = saturate((rayCell / 7.0  / px - 1.0) * 0.5);
                        float f16 = saturate((rayCell / 16.1 / px - 1.0) * 0.5);
                        if (f7 > 0.0)
                        {
                            // the two detail octaves are turned too (61° and 61° + 37°), see fbmAA
                            float3 r7 = rotXZ(raySpace, 0.4848096, 0.8746197);
                            float3 dp = r7 * 7.0 + drift * (t * 0.6) + 11.7;     // space × 7, time × 2
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
                        // f7 == 0 implies f16 == 0: d == 0.5 and the term vanishes exactly
                    }

                    // _RayZone: +1 = rays above the smooth base glow (original),
                    // 0 = rays all the way down, -1 = rays below a smooth top
                    float zoneS    = smoothstep(0.30, 0.60, altNorm);
                    float zoneMix  = (_RayZone >= 0.0) ? zoneS : (1.0 - zoneS);
                    float layerMix = 1.0 - abs(_RayZone) * (1.0 - zoneMix);
                    float rayPattern = lerp(basePattern, upperPattern, layerMix);

                    // the pattern is known now: below the threshold even at the
                    // flicker's maximum, the body is zero and nothing else is needed
                    if (!(boundsOk && rayPattern * latGate * 1.0001 <= thrH))
                    {
                        // Green-ray regions (see above): with no spikes they are only
                        // needed here, for a body that is actually lit (v3.18/v3.19)
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
                // (where the body is zero, g may still be 0 here: colorAlt is then
                // only ever multiplied by that zero density, or returned beside it)
                float colorAlt = altNorm * (1.0 - 0.85 * g);

                // Body + spike, colour altitude weighted by density
                float total = (density + spikeDensity) * widthComp;
                if (total <= 1e-5) return float2(0, colorAlt);
                return float2(total, (density * colorAlt + spikeDensity * spikeColorAlt) / total);
            }

            // ---------- three bands aggregated (offsets/seeds match C#) --------
            // Returns (density, colour altitude, skip): skip > 0 means every
            // active arc rejected this sample at the latitude pre-gate, and it
            // is the distance along any ray (world units) within which they
            // must keep rejecting it — latitude changes by at most 1 radian per
            // radius of travel — so the march can jump that far over samples
            // that are exactly zero (v3.18).
            float3 auroraField(float3 pWorld, float t, float detail, float pxI, float envPx)
            {
                SampleGeo g;
                float internalScale = 10.0 / _SphereRadius;
                g.p = pWorld * internalScale;
                float internalBase   = _AltBase * internalScale;
                g.internalAltTop     = max(_AltTop - _AltBase, 1e-5) * internalScale;   // band thickness
                g.r        = length(g.p);
                g.altitude = g.r - 10.0 - internalBase;        // height above the band's bottom
                if (g.altitude <= 0.0) return float3(0, 0.5, 0);

                // Signed latitude (1.1.0): + north. Each hemisphere has its own set of
                // three arcs, at +c and −c, each a smooth curve in its own right that
                // may cross the equator when the storm takes it there. Until now the
                // latitude was |lat|, which mirrored one set into both hemispheres and
                // bent every arc into a cusp where it met its reflection at the equator.
                float latS = asin(clamp(g.p.y / g.r, -1.0, 1.0)) / 1.5707963;

                // CHEAP PRE-GATE: meander ±0.225, folds ±0.65·FoldStrength, width
                // incl. variance and top spread — anything farther from the
                // nominal centre can never be in-band. Costs no noise at all.
                float preGate = _LatWidth * (1.0 + _WidthVariance) * (1.0 + _TopSpread) + 0.23 + 0.65 * _FoldStrength + 0.01;
                float c1Lat = _LatCenter;
                float c2Lat = _LatCenter - 0.05 * _BandSpacing;
                float c3Lat = _LatCenter + 0.04 * _BandSpacing;
                bool  a2 = _Arc2Fade > 0.001, a3 = _Arc3Fade > 0.001;
                float n1 = abs(latS - c1Lat) - preGate;                       // the northern set …
                float n2 = a2 ? abs(latS - c2Lat) - preGate : 1e30;
                float n3 = a3 ? abs(latS - c3Lat) - preGate : 1e30;
                float s1 = abs(-latS - c1Lat) - preGate;                      // … and the southern
                float s2 = a2 ? abs(-latS - c2Lat) - preGate : 1e30;
                float s3 = a3 ? abs(-latS - c3Lat) - preGate : 1e30;
                float mN = min(n1, min(n2, n3)), mS = min(s1, min(s2, s3));
                float mMin = min(mN, mS);
                if (mMin > 0.0)
                {
                    // every active arc is out of range by mMin latNorm = mMin·π/2 rad;
                    // the ray stays inside the band shell (r ≥ R + base) so that
                    // margin cannot close faster than 1 rad per (R + base) of travel
                    return float3(0, 0.5, mMin * 1.5707963 * (_SphereRadius + _AltBase) * 0.999);
                }

                g.lon01 = atan2(g.p.z, g.p.x) * 0.15915494 + 0.5;
                // The point on the planet's surface under the sample (radius 10): every
                // noise domain reads it, so anything built from it — folds, rays, spike
                // columns, green-ray regions — stands exactly upright and looks the same
                // at every latitude (1.1.0; it used to be the projection onto the
                // equatorial plane, which stretched everything toward the equator).
                float3 sP = g.p * (10.0 / g.r);
                g.hp  = sP;
                g.hpN = sP;
                float3 up   = sP * 0.1;
                float  rho  = length(up.xz);
                float3 east = rho > 1e-4 ? float3(-up.z, 0.0, up.x) / rho : float3(1, 0, 0);
                float3 north = cross(east, up);                             // toward the +y pole
                g.east3 = east;

                BandTweak mt = mainTweak();
                float dSum = 0.0, aSum = 0.0;
                // the two sets in a real loop: one copy of the three arc evaluations in
                // the compiled code, not two (the compiler would otherwise inline six)
                // … and the three arcs in a real loop inside it, so the compiled code
                // holds ONE copy of the arc evaluation instead of three (or six)
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
                        // the outer arcs fade in with the storm (v3.15); an absent or out-of-gate arc is skipped
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

            // ---------- color from altitude ------------------------------------
            // green | green→blue | blue | blue→pink | pink, stops are user-set.
            // Each transition is a smoothstep centred between its two stops and
            // never narrower than ±0.06 of the band height: when two stops sit
            // on top of each other the colour used to switch hard at that
            // height, which reads as a horizontal layer inside the band.
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

            // ---------- ray vs. outer shell sphere -----------------------------
            float2 raySphereShell(float3 ro, float3 rd, float rOut)
            {
                float b = dot(ro, rd);
                float c = dot(ro, ro) - rOut * rOut;
                float disc = b*b - c;
                if (disc < 0) return float2(-1, -1);
                float sd = sqrt(disc);
                return float2(max(-b - sd, 0), -b + sd);
            }

            // Emission map coordinates: u = longitude, v = latNorm 0.1..1 of the
            // north (top half) or south (bottom half) polar cap
            float2 emissionMapUV(float lon01, float latNorm, float hemi)
            {
                return float2(lon01, hemi * 0.5 + 0.5 * saturate((latNorm - 0.1) / 0.9));
            }

            // ---------- fragment ---------------------------------------------
            // Two colour targets (v3.12): the aurora this ray meets BEFORE it
            // first crosses the cloud-top shell (R + _CloudTopAlt) and the
            // aurora it meets AFTER. The composite multiplies only the second
            // by EVE's per-pixel cloud transmittance, so the clouds occlude the
            // aurora behind them and not the aurora in front of them — from
            // every altitude, with no composite-order switch. near.a carries
            // the cloud irradiance.
            struct FragOut
            {
                float4 near : SV_Target0;   // rgb aurora before the cloud shell, a irradiance
                float4 far  : SV_Target1;   // rgb aurora beyond the cloud shell, a scene depth this texel used (km; 0 = none)
                float4 aux  : SV_Target2;   // r = density-weighted mean distance of the aurora along this ray (world units; 0 = none) — 1.1.0, for the temporal reprojection
            };

            // far.a carries the scene depth this texel's march was cut against
            // (km, 0 = depth not used). The composite upsamples the low-res
            // buffers with it: a screen pixel only takes texels whose depth
            // agrees with its own, so vessel and terrain silhouettes come out
            // at screen resolution instead of blurred buffer texels (v3.16).
            FragOut outNF(float3 near, float3 far, float irr, float depthKm, float tMean)
            {
                FragOut o;
                o.near = float4(near, irr);
                o.far  = float4(far, depthKm);
                o.aux  = float4(tMean, 0, 0, 0);
                return o;
            }

            FragOut frag (v2f i)
            {
                float3 ro = _WorldSpaceCameraPos - _PlanetCenter.xyz;
                float3 rd = normalize(i.worldPos - ro);
                // Temporal accumulation (1.1.0): tilt the ray by this frame's sub-pixel offset.
                // _RayJitter.xy is the offset in units of the view direction (pixels × 2 tan(fov/2)
                // / buffer size, computed by the renderer); y is in buffer rows, which run up the
                // image when the projection is flipped (render texture) and down it otherwise.
                if (abs(_RayJitter.x) + abs(_RayJitter.y) > 1e-12)
                {
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, fwd = -UNITY_MATRIX_V[2].xyz;
                    float  k = max(dot(rd, fwd), 1e-4);
                    rd = normalize(rd / k + right * _RayJitter.x + up * (_RayJitter.y * -_ProjectionParams.x));
                }

                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return outNF(float3(2.0, 0.0, 0.0), float3(0, 0, 0), 1.0, 0.0, 0.0);

                float thick  = max(_AltTop - _AltBase, 1e-5);
                float rOuter = _SphereRadius + _AltTop + thick * 1.6 * _Spikiness;   // room for spikes
                float2 tNearFar = raySphereShell(ro, rd, rOuter);
                if (tNearFar.y <= tNearFar.x) discard;

                float t    = tNearFar.x;
                float tEnd = tNearFar.y;

                // Hollow below the band's bottom (R + AltBase): a ray may cross
                // the shell, dive under the band and come back out on the far
                // side. The march skips the hollow — gapStart/gapLen — so steps
                // are never wasted there.
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
                            else        { t = max(t, i1); }      // started inside the hollow
                        }
                    }
                }

                // Stop at the planet's near surface (analytic sphere — exact and
                // precise at any distance)
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
                            if (gapLen > 0.0 && gapStart < tEnd) { tEnd = gapStart; gapLen = 0.0; }   // ends at the band's bottom
                        }
                    }
                }

                // Stop at the occluder sphere (1.1.0): another body's aurora seen
                // from the surface or orbit of this one — Jool's from Laythe. Exact,
                // at any distance, where the depth buffer is far too coarse.
                if (_Occluder.w > 0.0)
                {
                    float3 roO = ro - _Occluder.xyz;
                    float  bO  = dot(roO, rd);
                    float  cO  = dot(roO, roO) - _Occluder.w * _Occluder.w;
                    if (cO > 0.0)                                  // camera outside the occluder
                    {
                        float dO = bO * bO - cO;
                        if (dO > 0.0)
                        {
                            float tO = -bO - sqrt(dO);
                            if (tO > 0.0 && tO < tEnd)
                            {
                                tEnd = tO;
                                if (gapLen > 0.0 && gapStart + gapLen > tEnd) { if (gapStart < tEnd) tEnd = gapStart; gapLen = 0.0; }
                            }
                        }
                    }
                }

                // Stop at NEAR things the camera already drew in front (the
                // vessel, terrain within ~5% of the planet radius = 30 km on
                // Kerbin). Farther out the depth buffer is too coarse — it
                // terraces into visible layers — and the analytic planet above
                // already occludes correctly.
                float tScene  = 1e30;
                float depthKm = 0.0;      // → far.a for the depth-aware upsample
                bool  nearHit = false;
                if (_UseSceneDepth > 0.5)
                {
                    float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                    float sceneZ   = LinearEyeDepth(rawDepth);
                    float3 camFwd  = -UNITY_MATRIX_V[2].xyz;
                    tScene  = sceneZ / max(dot(rd, camFwd), 0.05);
                    nearHit = sceneZ < _SphereRadius * 0.05;
                    // v3.24: the depth goes to the upsample only where the march can
                    // have been cut by it (the near 5 % of the radius above). Farther
                    // depth — terrain seen from orbit, the sky — is 0 = "agrees with
                    // anything": the aurora was not clipped there, so there is no
                    // silhouette to keep, and the ground's coarse, fast-changing depth
                    // used to break the smoothing into per-texel grain on one side of
                    // the horizon line.
                    depthKm = nearHit ? min(sceneZ, 1e7) * 0.001 : 0.0;
                    if (nearHit && tScene < tEnd)
                    {
                        tEnd = tScene;
                        if (gapLen > 0.0 && gapStart + gapLen > tEnd) { if (gapStart < tEnd) tEnd = gapStart; gapLen = 0.0; }
                    }
                }

                // Irradiance under this pixel (→ output alpha): the emission map
                // (pass 1: the aurora's own visible density over longitude ×
                // latitude) sampled at the receiving point and blurred by
                // _CloudLightBlur through its mip chain. The composite
                // multiplies it onto the clouds the frame contains, so the
                // receiving point is where the view ray meets the cloud-top
                // shell (R + _CloudTopAlt): from above, its near side; from
                // below, its far side or the ground, whichever comes first.
                // The vessel itself (depth hit within 3 km) gets nothing.
                // The cloud-top shell along this ray. tCloud = its first crossing
                // (1e30 = never): aurora before it goes to the near target, aurora
                // beyond it to the far target (occluded by the clouds' transmittance
                // in the composite). From above the tops that is the shell's near
                // side — a ray grazing the limb meets the clouds first and the band
                // beyond the horizon after; from below, its exit on the way up.
                float irr    = 0.0;    // irradiance under this pixel → near.a
                float tRecv  = -1.0;   // where that irradiance is evaluated
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
                            float tn = -bC - sdC;                       // camera above the cloud tops: near side of the shell
                            if (tn > 0.0) { tCloud = tn; tRecv = tn; }
                        }
                        else
                        {
                            float tf = -bC + sdC;                       // camera below: looking up exits the shell, looking down hits the ground
                            tCloud = tf;
                            tRecv  = (tPlanet > 0.0) ? min(tPlanet, tf) : tf;
                        }
                    }
                    else if (tPlanet > 0.0) tRecv = tPlanet;
                    if (!(_CloudLight > 0.001 && !(nearHit && tScene < 3000.0))) tRecv = -1.0;
                    if (_ForceFar > 0.5) tCloud = 0.0;             // 1.1.0: a distant body's aurora is beyond this world's clouds everywhere
                }
                if (tRecv > 0.0)
                {
                    float3 P   = ro + rd * tRecv;
                    float3 up  = normalize(P);
                    float night = 1.0;
                    if (_SunDir.w > 0.001)                      // w = daylight weight: 1 in sunlight, 0 while eclipsed by the parent body (1.1.0)
                        night = 1.0 - _SunDir.w * smoothstep(-0.15, 0.1, dot(up, _SunDir.xyz));
                    if (night > 0.001)
                    {
                        float  internalScale = 10.0 / _SphereRadius;
                        float3 pI    = P * internalScale;
                        float  rI    = length(pI);
                        float  lat   = asin(saturate(abs(pI.y) / rI)) / 1.5707963;
                        float  lon01 = atan2(pI.z, pI.x) * 0.15915494 + 0.5;
                        float  hemi  = (pI.y >= 0.0) ? 0.0 : 1.0;
                        // Receivers at or above the band's glow height get nothing
                        // — they are lit by the aurora itself.
                        float  srcAlt = _AltBase + 0.45 * (_AltTop - _AltBase);
                        float  recAlt = max(rI - 10.0, 0.0) / internalScale;             // world units
                        float  heightFade = saturate((srcAlt - recAlt) / max(srcAlt * 0.3, 1e-5));
                        if (heightFade > 0.001)
                        {
                            // blur radius → mip level: one map texel spans 0.9·(π/2)·R / (H/2) in latitude
                            float texelWorld = 1.5707963 * 0.9 * _SphereRadius / max(_EmissionMapSize.y * 0.5, 1.0);
                            float lod  = clamp(log2(max(_CloudLightBlur, 1.0) / texelWorld), 0.0, 5.0);
                            float2 uvM = emissionMapUV(lon01, lat, hemi);
                            // four rotated taps half a texel apart at that level take the box out of the mip filter
                            float2 tx  = exp2(lod) * 0.5 / _EmissionMapSize.xy;
                            float  e   = tex2Dlod(_EmissionMap, float4(uvM + float2( tx.x,  tx.y * 0.3), 0, lod)).r
                                       + tex2Dlod(_EmissionMap, float4(uvM + float2(-tx.x * 0.3, tx.y), 0, lod)).r
                                       + tex2Dlod(_EmissionMap, float4(uvM + float2(-tx.x, -tx.y * 0.3), 0, lod)).r
                                       + tex2Dlod(_EmissionMap, float4(uvM + float2( tx.x * 0.3, -tx.y), 0, lod)).r;
                            // e is the mean visible density over the blur footprint:
                            // for a sparse, thresholded aurora ~0.02 under the ribbon.
                            // A ribbon 30 km wide 40 km above the deck fills ~1.5 sr of
                            // the cloud's sky, so the deck directly under it receives
                            // about half the ribbon's radiance and a white cloud glows
                            // at ~1/7 of it — ×4 puts _CloudLight = 1 there for the
                            // shipped settings; 10 saturates.
                            irr = e * 0.25 * 4.0 * _CloudLight * night * heightFade;
                        }
                    }
                }

                // Output: near/far rgb = aurora (HDR), near.a = irradiance under
                // the pixel (HDR). The composite multiplies a with the clouds' own
                // amount (pass 2).
                if (tEnd <= t)
                {
                    if (irr <= 0.0) discard;
                    return outNF(float3(0, 0, 0), float3(0, 0, 0), irr * _Brightness, depthKm, 0.0);
                }

                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return outNF(float3(0.0, 0.0, 2.0), float3(0, 0, 0), 1.0, depthKm, 0.0);

                // Step size (1.1.0): a function of distance from the camera only —
                //   dt(t) = clamp(_StepGrowth × t, dtMin, dtMax)
                // dtMin = _StepLength × _StepNear × R up close (curtains overhead, the
                // camera inside the shell), growing with range like the pixel
                // footprint does, dtMax = _StepLength × _StepFarMul × R far out. Until
                // v1.0.1 one step size was spread over the whole ray and capped at
                // _MaxSteps, so a ray running the full chord of the shell sampled
                // the same aurora 10× coarser than its neighbour that dipped under
                // the band — a seam along the band's lower horizon, and inside a
                // 600 km shell (Jool) coarse enough for the sample shells to show
                // as rings. Now neighbouring rays agree, and the budget (_MaxSteps)
                // is a safety cap that only the longest grazing rays approach.
                float shell = max(_AltTop - _AltBase, 1e-5);
                float dtMin = max(_StepLength, 0.0001) * _SphereRadius * clamp(_StepNear, 0.05, 1.0);
                float dtMax = max(_StepLength, 0.0001) * _SphereRadius * max(_StepFarMul, 1.0);
                float grow  = max(_StepGrowth, 0.0);
                if (gapLen > 0.0 && gapStart + gapLen >= tEnd) { gapLen = 0.0; if (gapStart < tEnd) tEnd = gapStart; }
                float L = (tEnd - t) - gapLen;
                if (L <= 0.0) { if (irr <= 0.0) discard; return outNF(float3(0, 0, 0), float3(0, 0, 0), irr * _Brightness, depthKm, 0.0); }
                // Two segments when the ray dives under the band and comes back out
                // beyond the horizon: the near one (t .. gapStart) and the far one
                // (gapStart + gapLen .. tEnd). The march hops over the hollow keeping
                // its phase, so the far segment is sampled exactly like the near one.
                float segEnd = (gapLen > 0.0) ? gapStart : tEnd;
                float tFar   = gapStart + gapLen;
                bool  farSeg = false;

                // Per-pixel sample offset: turns the remaining sample-plane
                // banding into fine grain that the low-res upsample smooths away.
                // Blue noise when the plugin supplies the tile (1.1.0) — the
                // interleaved gradient noise used before is a fine diagonal grating,
                // and wherever a feature is under-sampled (a far band's thin
                // ribbons) that grating showed through as stripes.
                float jitter = (_BlueNoiseOn > 0.5)
                             ? tex2Dlod(_BlueNoise, float4(i.pos.xy * (1.0 / 64.0), 0, 0)).r
                             : frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                // Temporal accumulation (1.1.0): the renderer shifts every pixel's offset by the
                // same amount each frame (a golden-ratio sequence) — a Cranley–Patterson rotation,
                // which keeps the blue noise blue — so successive frames sample different depths
                // and the history converges on the integral. 0 when accumulation is off.
                jitter = frac(jitter + _TemporalOffset);
                float offset = 0.5 + (jitter - 0.5) * _Jitter;
                float tc = t + offset * clamp(grow * t, dtMin, dtMax);

                // Scale-free integration: a full traverse of the shell at
                // density 1 adds 0.6 — identical to the Unity-tuned baseline
                // (shell 2.0 × old factor 0.3) at any planet size or height.
                float kShell = 0.6 / shell;
                float internalScale = 10.0 / _SphereRadius;
                int   maxN = (int)max(_MaxSteps, 8.0);
                float filt = clamp(_StepFilter, 0.2, 1.5);                 // 1.1.0: the noise filter's width as a fraction of the step (was ¾)

                float3 colorNear = float3(0, 0, 0), colorFar = float3(0, 0, 0);
                float  alpha = 0.0, tSum = 0.0;                            // tSum: Σ distance × contribution → the aux target's mean distance
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
                        farSeg = true; tc = tFar + (tc - segEnd); segEnd = tEnd;   // hop over the hollow, keep the phase
                        if (tc >= segEnd) break;
                    }
                    float  dtc = clamp(grow * tc, dtMin, dtMax);
                    float3 p   = ro + rd * tc;
                    // detail fades in inside ~3 planet-tenths (180 km on Kerbin),
                    // is gone beyond 12 (720 km) — the Unity test camera never sees it
                    float detail = _DetailStrength * (1.0 - smoothstep(3.0, 12.0, tc * internalScale));
                    // Sample footprint, internal units: the pixel at this
                    // distance, but never smaller than ¾ of the step. Noise
                    // finer than the sample spacing cannot be resolved along
                    // the ray — it aliases, and because neighbouring pixels
                    // sample at correlated depths the aliasing is coherent:
                    // hard-edged blocks that jump as the noise animates. Fading
                    // those octaves out (fbmAA) leaves what the march can
                    // actually integrate; finer steps near the band bring the
                    // detail back where it is large on screen (v3.10).
                    float stepI = dtc * filt * internalScale;                       // the filter's share of the step (¾ by default), internal units
                    float pxI   = max(max(tc, 1.0) * _PixelAngle * internalScale, stepI);
                    float3 fld  = auroraField(p, time, detail, pxI, stepI);
                    if (fld.x > 0.001)
                    {
                        // Daylight: real aurorae vanish against a sunlit sky.
                        // Full on the night side, gone a little past the terminator.
                        float dayAtten = 1.0;
                        if (_SunDir.w > 0.001 && _DaylightFade > 0.001)
                            dayAtten = 1.0 - _DaylightFade * _SunDir.w * smoothstep(-0.2, 0.05, dot(normalize(p), _SunDir.xyz));

                        // Soft knee instead of a hard early exit: dense overlaps
                        // saturate smoothly, so neighbouring pixels (whose jittered
                        // samples cross the limit at different depths) stay
                        // consistent instead of breaking into blocks.
                        float knee = saturate((1.6 - alpha) * 1.6667);
                        float contribution = fld.x * dtc * kShell * dayAtten * knee;
                        float3 add = auroraColor(fld.y) * contribution;
                        if (tc < tCloud || _FarSplit < 0.5) colorNear += add; else colorFar += add;
                        alpha += contribution;
                        tSum  += contribution * tc;
                        if (alpha >= 1.6) break;
                    }
                    else if (fld.z > 0.0)
                    {
                        // Every arc rejected this sample at the latitude pre-gate
                        // with fld.z of room along the ray: that stretch is exactly
                        // zero, so jump to its end. Never across the hop over the
                        // hollow — beyond it the latitude is anything (v3.18).
                        // Land this pixel's own fraction of a step PAST the end,
                        // not exactly on it (1.1.0): rays that all land on the same
                        // bound (a latitude cone) lose their jitter and march on
                        // in lockstep from there, and the sample shells the jitter
                        // breaks up come back as stripes across whatever they meet
                        // next (the far bands from an equatorial orbit, the map view).
                        float room = farSeg ? 1e30 : (segEnd - tc);
                        float skip = min(fld.z, room);
                        if (skip > dtc) tc += skip - dtc + offset * dtc;
                    }
                    tc += dtc;
                }

                // aux: where along the ray the aurora this pixel shows sits, on average (0 = nothing seen)
                float tMean = (alpha > 1e-6) ? tSum / alpha : 0.0;
                return outNF(colorNear * _Brightness, colorFar * _Brightness, irr * _Brightness, depthKm, tMean);
            }
            // ---------- emission map (pass 1) ------------------------------
            // Bakes the aurora's visible density (auroraField, three altitudes)
            // into a longitude × latitude texture: u = longitude, v = latNorm
            // 0.1..1 of the north (top half) or south (bottom half) polar cap.
            // The map's mip chain is the blur the cloud light samples.
            // Position goes out as a separate SV_POSITION so VPOS can be read in the fragment
            struct v2fMap { float2 dummy : TEXCOORD0; };

            v2fMap vertEmission (appdata v, out float4 outpos : SV_POSITION)
            {
                v2fMap o;
                o.dummy = float2(0, 0);
                outpos  = float4(v.vertex.xy, 0.5, 1.0);   // a ±1 quad covers the whole target
                return o;
            }

            float4 fragEmission (v2fMap i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                // Four sub-samples per texel (rotated grid) × three altitudes,
                // with the noise filtered to ~3 km as it is on screen — NOT to
                // the 13 km texel. Filtered to the texel, the ray pattern sat
                // at its mean and the ray threshold removed the rays' share
                // (the upper band came out ~4× too dark; the base pattern
                // below it was unaffected). The texel's value is now the mean
                // of the visible density across it (v3.11).
                float  thick = max(_AltTop - _AltBase, 1e-5);
                float  hemi  = (screenPos.y / _EmissionMapSize.y >= 0.5) ? 1.0 : 0.0;
                float  dens  = 0.0;
                // Texels whose whole footprint lies outside every arc's latitude
                // pre-gate hold exactly zero: skip the twelve evaluations (v3.18).
                // (A gate around the loop rather than an early return: the loop
                // below must stay a real loop, and fxc is happiest when nothing
                // unusual sits around it.)
                float vC   = screenPos.y / _EmissionMapSize.y;
                float latC = 0.1 + 0.9 * saturate((vC - hemi * 0.5) * 2.0);
                float texLat = 0.9 * 2.0 / _EmissionMapSize.y;                // one texel in latNorm
                float preGate = _LatWidth * (1.0 + _WidthVariance) * (1.0 + _TopSpread) + 0.23 + 0.65 * _FoldStrength + 0.01 + texLat;
                float d1 = abs(latC - _LatCenter);
                float d2 = (_Arc2Fade > 0.001) ? abs(latC - (_LatCenter - 0.05 * _BandSpacing)) : 1e30;
                float d3 = (_Arc3Fade > 0.001) ? abs(latC - (_LatCenter + 0.04 * _BandSpacing)) : 1e30;
                if (min(d1, min(d2, d3)) <= preGate)
                {
                    // rotated-grid offsets (texels): each one is the previous turned 90°
                    float2 offs  = float2(-0.375, -0.125);
                    // Both real loops. fxc refuses to unroll a body this size
                    // ("unrolled loop is too large"), and when an [unroll] fails
                    // inside a [loop] it reports the failure on the OUTER loop as
                    // "can't unroll loops marked with loop attribute" (v3.18d).
                    // One auroraField body in the code, run 4 × 3 times: the same
                    // arithmetic in the same order as unrolled, pixel for pixel.
                    [loop]
                    for (int j = 0; j < 4; j++)
                    {
                        float2 uv   = (screenPos.xy + offs) / _EmissionMapSize.xy;
                        offs = float2(-offs.y, offs.x);
                        float  lon  = (uv.x - 0.5) * 6.2831853;
                        float  latN = 0.1 + 0.9 * saturate((uv.y - hemi * 0.5) * 2.0);
                        float  latR = latN * 1.5707963;
                        float3 dir  = float3(cos(latR) * cos(lon), (hemi > 0.5 ? -1.0 : 1.0) * sin(latR), cos(latR) * sin(lon));
                        [loop]
                        for (int k = 0; k < 3; k++)
                        {
                            float alt = _AltBase + thick * (0.15 + 0.25 * (float)k);
                            dens += auroraField(dir * (_SphereRadius + alt), _AnimTime, 0.0, 0.05, 0.0).x;
                        }
                    }
                }
                return float4(dens / 12.0, 0, 0, 1);
            }
        ENDCG

        // 0: the aurora — near / far of the cloud-top shell into two targets
        //    (and, in near.a, the cloud irradiance)
        Pass
        {
            ZTest Always
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.0
            ENDCG
        }

        // 1: the emission map (rendered by AuroraRenderer into a small texture)
        Pass
        {
            Blend Off
            ZTest Always
            CGPROGRAM
            #pragma vertex   vertEmission
            #pragma fragment fragEmission
            #pragma target   4.0
            ENDCG
        }
    }
}
