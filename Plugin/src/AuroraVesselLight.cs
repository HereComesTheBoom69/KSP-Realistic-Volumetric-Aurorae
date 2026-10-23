// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// The aurora's light on the vessel (1.1.0; an option, off by default). The point
    /// lights of v3.x lit the terrain, the water and the vessel from a ring of 90 lights
    /// along the ribbons and went in v3.23; this brings back the part the vessel saw, as
    /// a handful of Unity lights that reach the vessel's layers only — not the terrain,
    /// not the water, not the clouds.
    ///
    /// Where the ribbons are comes from the meander lookup the shader reads: besides each
    /// arc's shift, flare and width per longitude it carries the folded ribbon's latitude
    /// (AuroraMeanderTexture, channel A — the scan in AuroraNoise.RibbonLatitude, baked on
    /// its worker thread), so tracing a ribbon here is a lookup, not noise. Every piece of
    /// ribbon above the vessel's horizon contributes light in proportion to its brightness
    /// (arc fade, flare, width, daylight fade) and inversely to its distance squared — an
    /// extended source summed element by element, on a longitude grid anchored at the
    /// vessel and fine where the ribbon is near (0.15° steps growing geometrically to the
    /// far side), so the sum is the integral to a few percent and moves smoothly with the
    /// vessel instead of stepping from sample to sample.
    ///
    /// The sky is then split into four quarters around the vessel (north-east, north-west,
    /// south-east, south-west) and each quarter's light becomes one Unity light: its
    /// direction the quarter's weighted mean, its strength the quarter's share. The split is
    /// soft — a piece of ribbon is shared between the quarters by its direction cosines, so
    /// one straight overhead gives every light a quarter of it and one due east gives the two
    /// eastern lights half each — because a hard split flipped whole stretches of ribbon from
    /// one light to another as the folds drifted across the boundaries. A ribbon straight
    /// overhead thus lights the hull from two or four directions at ±45° or so, the way a band
    /// across the sky does, instead of from the zenith alone, which lit nothing but the
    /// up-facing rims; a band on the horizon puts all four in that quarter. Directly under a
    /// quiet, straight ribbon the four add up to the configured intensity. Everything is
    /// smoothed over a quarter second so the ribbons' motion does not flicker on the hull.
    ///
    /// Each light is a point light placed 50 km from the vessel along its direction with a
    /// 200 km range: over the vessel its direction and attenuation are uniform, so it behaves
    /// as a directional light, and the known attenuation at a quarter of the range
    /// (1 / (1 + 25/16)) is divided out. The distance is a constant, not a multiple of the
    /// planet's radius: it only has to be large next to the craft, and a light 12,000 km
    /// out with a 48,000 km range (two Jool radii) is a size Unity's light pipeline was
    /// never meant to see. PER PIXEL by default (a ForwardAdd pass per part
    /// and light, so the count is a setting: 1, 2 or 4): per-vertex was the first choice —
    /// no extra pass at all, and Unity's vertex lights are point lights, four per object —
    /// but a vertex light is one of four slots Unity hands out per object by intensity,
    /// shared with every other non-pixel light on the craft, and a light that loses its slot
    /// is rendered as spherical harmonics instead; with these lights' intensities moving
    /// against each other the slots reshuffled, and the hull pulsed between the two
    /// renderings. Per-vertex remains an option for craft with no lights of their own.
    /// </summary>
    public class AuroraVesselLight : MonoBehaviour
    {
        const string LogPrefix = "[RVA:VesselLight]";

        [Header("Inputs (set by the bootstrap)")]
        public Transform            planet;
        public Material             auroraMaterial;
        public AuroraMeanderTexture meander;
        public float                planetRadius = 600000f;

        [Header("Settings (live)")]
        [Tooltip("Master switch.")]
        public bool  lightOn = false;
        [Tooltip("Light intensity directly under a quiet, straight ribbon (the four quadrant lights together).")]
        public float intensity = 0.3f;
        [Tooltip("Per-pixel lights (a ForwardAdd pass per part and light) instead of per-vertex (free, but one of four slots per object shared with the craft's other lights).")]
        public bool  pixelLight = true;
        [Tooltip("How many lights the sky is split into: 1 (the mean direction), 2 (east / west halves) or 4 (quarters).")]
        public int   lightCount = 2;
        [Tooltip("Longitude steps traced along each arc on each side of the vessel (0.15° near it, growing to the far side).")]
        public int   samples = 32;
        [Tooltip("Seconds over which the lights follow the ribbons' motion.")]
        public float smoothing = 0.25f;

        // KSP layers the light reaches: 0 Default (parts), 16 kerbals, 17 EVA, 19 PhysicalObjects
        public const int VesselLayers = (1 << 0) | (1 << 16) | (1 << 17) | (1 << 19);
        // a light sits this far from the vessel (metres), with four times that as its range: Unity's
        // point-light falloff 1 / (1 + 25 (d / r)²) is then 1 / (1 + 25/16) over the whole vessel
        const float LightDistance     = 50000f;
        const float AttenCompensation = 1f + 25f / 16f;
        const int   Quadrants          = 4;
        // the trace grid: steps grow geometrically from the first (0.15°) outward on each side, the
        // growth chosen so the last step reaches the far side of the planet whatever the count
        const float FirstStepDeg = 0.15f;

        // the three arcs, as the shader places and seeds them
        static readonly float[] BandLatOffsets = { 0.00f, -0.05f, +0.04f };

        GameObject lightsRoot;
        readonly Light[]   lights  = new Light[Quadrants];
        readonly Vector3[] curDir  = new Vector3[Quadrants];      // from the vessel toward each light
        readonly float[]   curI    = new float[Quadrants];
        readonly bool[]    haveDir = new bool[Quadrants];
        readonly Vector3[] dirSum  = new Vector3[Quadrants];
        readonly float[]   wSum    = new float[Quadrants];
        float[]  stepLon;                                          // the grid's longitude offsets (radians, one side)
        int      stepCount;
        int      arcsLit, lightsOn;
        readonly float[] targetI = new float[Quadrants];           // this frame's unsmoothed intensities (diagnostics)
        float    logUntil = -1f;                                   // Time.unscaledTime until which every frame is logged
        int      logFrames;
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        /// <summary>Main-thread milliseconds of the last trace.</summary>
        public float LastUpdateMs { get; private set; }
        public float CurrentIntensity { get { float s = 0f; for (int q = 0; q < Quadrants; q++) s += curI[q]; return s; } }
        /// <summary>1, 2 or 4.</summary>
        int Count => lightCount >= 4 ? 4 : lightCount >= 2 ? 2 : 1;

        /// <summary>Diagnostics: write one line per frame to KSP.log for this many seconds — every input of the
        /// trace, the unsmoothed and smoothed intensity and direction of each light — so a pulse on the hull can
        /// be matched against the numbers that drove it.</summary>
        public void LogFrames(float seconds)
        {
            logUntil  = Time.unscaledTime + Mathf.Clamp(seconds, 1f, 60f);
            logFrames = 0;
            Log($"logging every frame for {seconds:F0} s (body radius {planetRadius:F0} m, {Count} light(s), {(pixelLight ? "per pixel" : "per vertex")}, {Mathf.Clamp(samples, 16, 128)} steps per side, smoothing {smoothing:F2} s)");
        }
        public bool Logging => Time.unscaledTime < logUntil;

        public string Status
        {
            get
            {
                if (!lightOn) return "off";
                if (lightsOn == 0) return "on, dark (no ribbon above the horizon, or daylight)";
                var v = FlightGlobals.ActiveVessel;
                string dirs = "";
                if (v != null && planet != null)
                {
                    Vector3 up = (v.transform.position - planet.position).normalized;
                    for (int q = 0; q < Quadrants; q++)
                    {
                        if (lights[q] == null || !lights[q].enabled) continue;
                        float elev = Mathf.Asin(Mathf.Clamp(Vector3.Dot(curDir[q], up), -1f, 1f)) * Mathf.Rad2Deg;
                        dirs += $"{(dirs.Length > 0 ? ", " : "")}{curI[q]:F3} from {elev:F0}°";
                    }
                }
                return $"{lightsOn} light(s): {dirs}; {arcsLit} arc(s), {(pixelLight ? "per pixel" : "per vertex")}, {LastUpdateMs:F2} ms";
            }
        }

        void OnDisable() { SetAllOff(); }
        void OnDestroy() { if (lightsRoot != null) { Destroy(lightsRoot); lightsRoot = null; for (int q = 0; q < Quadrants; q++) lights[q] = null; } }

        void SetAllOff()
        {
            for (int q = 0; q < Quadrants; q++) { if (lights[q] != null && lights[q].enabled) lights[q].enabled = false; curI[q] = 0f; }
            lightsOn = 0;
        }

        void EnsureLights()
        {
            if (lightsRoot != null) return;
            // a scene-root object, not a child of the planet: a child's world position goes through the
            // planet's transform, thousands of km away, and loses metres to float precision; these are
            // placed in world space every frame and live exactly where they are put
            lightsRoot = new GameObject("RVA_VesselLights");
            for (int q = 0; q < Quadrants; q++)
            {
                var go = new GameObject("RVA_VesselLight_" + q);
                go.transform.SetParent(lightsRoot.transform, false);
                var l = go.AddComponent<Light>();
                l.type        = LightType.Point;
                l.shadows     = LightShadows.None;
                l.cullingMask = VesselLayers;
                l.intensity   = 0f;
                l.enabled     = false;
                lights[q] = l;
            }
            Log("lights created (vessel layers only)");
        }

        /// <summary>The longitude offsets of the trace grid on one side of the vessel: FirstStepDeg
        /// growing geometrically so that the n-th step lands on the far side (π).</summary>
        void EnsureGrid()
        {
            int n = Mathf.Clamp(samples, 16, 128);
            if (stepLon != null && stepCount == n) return;
            stepLon = new float[n];
            stepCount = n;
            float first = FirstStepDeg * Mathf.Deg2Rad;
            // growth g with first × (g^n − 1) / (g − 1) = π, by bisection
            float lo = 1.0001f, hi = 2f;
            for (int it = 0; it < 40; it++)
            {
                float g = 0.5f * (lo + hi);
                float span = first * (Mathf.Pow(g, n) - 1f) / (g - 1f);
                if (span < Mathf.PI) lo = g; else hi = g;
            }
            float growth = 0.5f * (lo + hi), step = first, lon = 0f;
            for (int i = 0; i < n; i++)
            {
                lon += step;
                stepLon[i] = Mathf.Min(lon, Mathf.PI);
                step *= growth;
            }
            stepLon[n - 1] = Mathf.PI;
        }

        void LateUpdate()
        {
            if (!lightOn || planet == null || auroraMaterial == null)
            {
                SetAllOff();
                return;
            }
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) { SetAllOff(); return; }
            EnsureLights();
            EnsureGrid();

            watch.Restart();

            Vector3 centre = planet.position;
            Vector3 V      = vessel.transform.position - centre;           // the vessel, planet-relative
            float   R      = planetRadius;
            float   altBase = auroraMaterial.GetFloat("_AltBase");
            float   altTop  = auroraMaterial.GetFloat("_AltTop");
            float   hL      = altBase + 0.35f * Mathf.Max(altTop - altBase, 0f);   // the green base, where most of the light is
            float   rL      = R + hL;
            // the ribbon is not a line: a vessel inside or beside the curtain is lit from all of its
            // thickness, not from one point at distance zero — soften the inverse square by the
            // band's half-thickness or the ribbon's half-width, whichever is larger
            float   latW    = auroraMaterial.GetFloat("_LatWidth");
            float   soft    = Mathf.Max(0.5f * Mathf.Max(altTop - altBase, 0f), latW * 1.5707963f * R);
            float   soft2   = soft * soft;
            float   c0      = auroraMaterial.GetFloat("_LatCenter");
            float   spacing = auroraMaterial.GetFloat("_BandSpacing");
            float   a2      = auroraMaterial.GetFloat("_Arc2Fade");
            float   a3      = auroraMaterial.GetFloat("_Arc3Fade");
            float   wv      = auroraMaterial.GetFloat("_WidthVariance");
            float   act     = 0.3f + 0.7f * auroraMaterial.GetFloat("_Activity");
            float   dayFade = auroraMaterial.GetFloat("_DaylightFade");
            Vector4 sun     = auroraMaterial.GetVector("_SunDir");
            Vector3 sunDir  = new Vector3(sun.x, sun.y, sun.z);
            bool    sunOn   = sun.w > 0.001f && dayFade > 0.001f;

            // the vessel's longitude (the shader's convention: atan2(z, x)) and local frame for the quadrants
            float   lonV  = Mathf.Atan2(V.z, V.x);
            Vector3 up    = V.normalized;
            Vector3 north = Vector3.up - up * Vector3.Dot(Vector3.up, up);           // the pole, in the tangent plane
            if (north.sqrMagnitude < 1e-6f) north = Vector3.forward - up * Vector3.Dot(Vector3.forward, up);
            north.Normalize();
            Vector3 east  = Vector3.Cross(north, up);

            for (int q = 0; q < Quadrants; q++) { dirSum[q] = Vector3.zero; wSum[q] = 0f; }
            int count = Count;
            arcsLit = 0;
            for (int hemi = 0; hemi < 2; hemi++)
            {
                float sign = hemi == 0 ? 1f : -1f;
                for (int b = 0; b < 3; b++)
                {
                    float fade = b == 0 ? 1f : b == 1 ? a2 : a3;
                    if (fade < 0.01f) continue;
                    float cLat = c0 + BandLatOffsets[b] * spacing;
                    float arcW = 0f;
                    // both sides of the vessel's longitude, fine near it and coarse far away; each sample
                    // stands for the stretch halfway to its neighbours
                    for (int side = -1; side <= 1; side += 2)
                    {
                        for (int i = 0; i < stepCount; i++)
                        {
                            float off  = stepLon[i];
                            float prev = i > 0 ? stepLon[i - 1] : -off;              // the first cell runs from the vessel's longitude itself
                            float next = i + 1 < stepCount ? stepLon[i + 1] : Mathf.PI;
                            float dLon = 0.5f * (next - prev);
                            if (dLon <= 0f) continue;
                            float lon = lonV + side * off;
                            float shift = 0f, flare01 = 0.5f, width01 = 0.5f, lat = cLat, ribbonW = 1f;
                            if (meander != null && meander.Sample(b, hemi, lon, out shift, out flare01, out width01, out lat, out ribbonW)) { }
                            else lat = cLat + shift;
                            if (ribbonW <= 0.001f) continue;                      // no ribbon in this column
                            lat = Mathf.Clamp(lat, -1f, 1f);                      // toward this set's pole
                            float latR = sign * lat * Mathf.PI * 0.5f;
                            float cl   = Mathf.Cos(latR);
                            Vector3 P  = new Vector3(cl * Mathf.Cos(lon), Mathf.Sin(latR), cl * Mathf.Sin(lon)) * rL;
                            Vector3 dv = P - V;
                            float   d2 = dv.sqrMagnitude;
                            if (d2 < 1f) continue;
                            float   d  = Mathf.Sqrt(d2);
                            if (!Visible(V, dv, d, R)) continue;                   // below the vessel's horizon
                            float width = Mathf.Max(Mathf.Lerp(1f - wv, 1f + wv, width01), 0.15f);
                            float e     = fade * Mathf.Lerp(0.7f, 1.6f, flare01) * width * Mathf.Min(ribbonW, 4f);
                            float night = 1f;
                            if (sunOn)
                            {
                                float cosSun = Vector3.Dot(P / rL, sunDir);
                                night = 1f - dayFade * sun.w * AuroraNoise.SmoothStep(-0.2f, 0.05f, cosSun);
                            }
                            float ds = rL * cl * dLon;                              // this piece of ribbon, metres (its folds make it longer; ignored)
                            float w  = e * night * ds / (d2 + soft2);
                            if (w <= 0f) continue;
                            // soft split between the lights by the direction cosines (see the header):
                            // 4 = quarters (east / west × north / south), 2 = east / west halves, 1 = all in one
                            Vector3 du = dv * (w / d);
                            if (count == 4)
                            {
                                float sE = Vector3.Dot(dv, east) / d, sN = Vector3.Dot(dv, north) / d;
                                float mE = 0.5f + 0.5f * sE, mN = 0.5f + 0.5f * sN;
                                float m0 = (1f - mE) * (1f - mN), m1 = mE * (1f - mN), m2 = (1f - mE) * mN, m3 = mE * mN;
                                wSum[0] += w * m0; dirSum[0] += du * m0;
                                wSum[1] += w * m1; dirSum[1] += du * m1;
                                wSum[2] += w * m2; dirSum[2] += du * m2;
                                wSum[3] += w * m3; dirSum[3] += du * m3;
                            }
                            else if (count == 2)
                            {
                                float mE = 0.5f + 0.5f * Vector3.Dot(dv, east) / d;
                                wSum[0] += w * (1f - mE); dirSum[0] += du * (1f - mE);
                                wSum[1] += w * mE;        dirSum[1] += du * mE;
                            }
                            else { wSum[0] += w; dirSum[0] += du; }
                            arcW    += w;
                        }
                    }
                    if (arcW > 0f) arcsLit++;
                }
            }

            // normalisation: an infinite straight ribbon straight overhead at height h, softened by soft, gives
            // ∫ ds / (h² + soft² + s²) = π / √(h² + soft²), so directly under a quiet ribbon on the ground the
            // quadrants' targets add up to the configured intensity
            float norm = intensity * act * Mathf.Sqrt(hL * hL + soft2) / Mathf.PI;
            float dt   = Mathf.Max(Time.deltaTime, 1e-4f);
            float k    = smoothing > 1e-3f ? 1f - Mathf.Exp(-dt / smoothing) : 1f;
            float L    = LightDistance;
            Color col  = LightColor();
            lightsOn   = 0;
            for (int q = 0; q < Quadrants; q++)
            {
                if (q >= count) { if (lights[q].enabled) lights[q].enabled = false; curI[q] = 0f; targetI[q] = 0f; continue; }
                float target = norm * wSum[q];
                targetI[q] = target;
                curI[q] = Mathf.Lerp(curI[q], target, k);
                if (wSum[q] > 0f)
                {
                    // normalised BY HAND: the sums are of order 1e-5 on a Jool-sized body (ribbon length over
                    // distance squared), and Vector3.normalized returns ZERO for anything under 1e-5 — which put
                    // the light at the vessel itself, and had it jumping between there and the sky as the sum
                    // crossed that threshold: the flicker, and the "only the seams lit" state, seen at Jool and
                    // never at Kerbin or Laythe, where the sums are ten times larger
                    double sx = dirSum[q].x, sy = dirSum[q].y, sz = dirSum[q].z;
                    double sm = System.Math.Sqrt(sx * sx + sy * sy + sz * sz);
                    if (sm > 1e-30)
                    {
                        Vector3 dn = new Vector3((float)(sx / sm), (float)(sy / sm), (float)(sz / sm));
                        curDir[q]  = haveDir[q] ? Vector3.Slerp(curDir[q], dn, k) : dn;
                        haveDir[q] = true;
                    }
                }
                var l = lights[q];
                bool on = curI[q] > 0.002f && haveDir[q];
                if (l.enabled != on) l.enabled = on;
                if (!on) continue;
                lightsOn++;
                l.transform.position = vessel.transform.position + curDir[q] * L;
                l.range      = 4f * L;
                l.intensity  = curI[q] * AttenCompensation;
                l.color      = col;
                l.renderMode = pixelLight ? LightRenderMode.ForcePixel : LightRenderMode.ForceVertex;
                if (l.cullingMask != VesselLayers) l.cullingMask = VesselLayers;
            }

            watch.Stop();
            LastUpdateMs = (float)watch.Elapsed.TotalMilliseconds;

            if (Time.unscaledTime < logUntil)
            {
                logFrames++;
                var sb = new System.Text.StringBuilder(512);
                float vm = V.magnitude;
                sb.Append($"f{Time.frameCount} dt {dt * 1000f:F1}ms vessel alt {(vm - R) * 0.001f:F1}km lat {Mathf.Asin(Mathf.Clamp(V.y / vm, -1f, 1f)) * Mathf.Rad2Deg:F2} lon {lonV * Mathf.Rad2Deg:F2} |pos| {vessel.transform.position.magnitude:F0}m");
                sb.Append($" | band hL {hL * 0.001f:F0}km soft {soft * 0.001f:F0}km c0 {c0:F4} sp {spacing:F3} a2 {a2:F2} a3 {a3:F2} act {act:F3} anim {auroraMaterial.GetFloat("_AnimTime"):F3} sun.w {sun.w:F2}");
                if (meander != null) sb.Append($" | lut up f{meander.LastUploadFrame} bake {meander.LastBakeMs:F1}ms");
                sb.Append($" | arcs {arcsLit}");
                for (int q = 0; q < count; q++)
                {
                    float elev = haveDir[q] ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(curDir[q], up), -1f, 1f)) * Mathf.Rad2Deg : 0f;
                    float az   = haveDir[q] ? Mathf.Atan2(Vector3.Dot(curDir[q], east), Vector3.Dot(curDir[q], north)) * Mathf.Rad2Deg : 0f;
                    sb.Append($" | L{q} w {wSum[q]:E2} tgt {targetI[q]:F4} cur {curI[q]:F4} el {elev:F1} az {az:F1} {(lights[q].enabled ? "on" : "off")}");
                }
                Log(sb.ToString());
                if (Time.unscaledTime + dt >= logUntil) Log($"logged {logFrames} frames");
            }
        }

        /// <summary>The aurora's base colour (what the vessel sees from below), normalised to a peak of 1.</summary>
        Color LightColor()
        {
            Color c = auroraMaterial.GetColor("_ColorLow") * Mathf.Max(auroraMaterial.GetFloat("_GainLow"), 0f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m <= 1e-4f) return new Color(0.4f, 0.9f, 0.5f, 1f);
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        /// <summary>Does the segment from the vessel (V, planet-relative) along dv (length d) clear the planet?
        /// The planet is taken half a percent under its sea level, so a vessel in a valley below it still sees the sky.</summary>
        static bool Visible(Vector3 V, Vector3 dv, float d, float R)
        {
            // nearest approach of the segment to the planet's centre
            float tt = Mathf.Clamp(-Vector3.Dot(V, dv) / (d * d), 0f, 1f);
            Vector3 q = V + dv * tt;
            float   r = R * 0.995f;
            return q.sqrMagnitude > r * r;
        }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
