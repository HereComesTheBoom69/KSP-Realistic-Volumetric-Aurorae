// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

namespace RVA
{

    public class AuroraVesselLight : MonoBehaviour
    {
        const string LogPrefix = "[RVA:VesselLight]";

        public Transform            planet;
        public Material             auroraMaterial;
        public AuroraMeanderTexture meander;
        public float                planetRadius = 600000f;

        public bool  lightOn = false;
        public float intensity = 0.3f;
        public bool  pixelLight = true;
        public int   lightCount = 2;
        public int   samples = 32;
        public float smoothing = 0.25f;

        public const int VesselLayers = (1 << 0) | (1 << 16) | (1 << 17) | (1 << 19);

        const float LightDistance     = 50000f;
        const float AttenCompensation = 1f + 25f / 16f;
        const int   Quadrants          = 4;

        const float FirstStepDeg = 0.15f;

        static readonly float[] BandLatOffsets = { 0.00f, -0.05f, +0.04f };

        GameObject lightsRoot;
        readonly Light[]   lights  = new Light[Quadrants];
        readonly Vector3[] curDir  = new Vector3[Quadrants];
        readonly float[]   curI    = new float[Quadrants];
        readonly bool[]    haveDir = new bool[Quadrants];
        readonly Vector3[] dirSum  = new Vector3[Quadrants];
        readonly float[]   wSum    = new float[Quadrants];
        float[]  stepLon;
        int      stepCount;
        int      arcsLit, lightsOn;
        readonly float[] targetI = new float[Quadrants];
        float    logUntil = -1f;
        int      logFrames;
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        public float LastUpdateMs { get; private set; }
        public float CurrentIntensity { get { float s = 0f; for (int q = 0; q < Quadrants; q++) s += curI[q]; return s; } }

        int Count => lightCount >= 4 ? 4 : lightCount >= 2 ? 2 : 1;

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

        void EnsureGrid()
        {
            int n = Mathf.Clamp(samples, 16, 128);
            if (stepLon != null && stepCount == n) return;
            stepLon = new float[n];
            stepCount = n;
            float first = FirstStepDeg * Mathf.Deg2Rad;

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
            Vector3 V      = vessel.transform.position - centre;
            float   R      = planetRadius;
            float   altBase = auroraMaterial.GetFloat("_AltBase");
            float   altTop  = auroraMaterial.GetFloat("_AltTop");
            float   hL      = altBase + 0.35f * Mathf.Max(altTop - altBase, 0f);
            float   rL      = R + hL;

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

            float   lonV  = Mathf.Atan2(V.z, V.x);
            Vector3 up    = V.normalized;
            Vector3 north = Vector3.up - up * Vector3.Dot(Vector3.up, up);
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

                    for (int side = -1; side <= 1; side += 2)
                    {
                        for (int i = 0; i < stepCount; i++)
                        {
                            float off  = stepLon[i];
                            float prev = i > 0 ? stepLon[i - 1] : -off;
                            float next = i + 1 < stepCount ? stepLon[i + 1] : Mathf.PI;
                            float dLon = 0.5f * (next - prev);
                            if (dLon <= 0f) continue;
                            float lon = lonV + side * off;
                            float shift = 0f, flare01 = 0.5f, width01 = 0.5f, lat = cLat, ribbonW = 1f;
                            if (meander != null && meander.Sample(b, hemi, lon, out shift, out flare01, out width01, out lat, out ribbonW)) { }
                            else lat = cLat + shift;
                            if (ribbonW <= 0.001f) continue;
                            lat = Mathf.Clamp(lat, -1f, 1f);
                            float latR = sign * lat * Mathf.PI * 0.5f;
                            float cl   = Mathf.Cos(latR);
                            Vector3 P  = new Vector3(cl * Mathf.Cos(lon), Mathf.Sin(latR), cl * Mathf.Sin(lon)) * rL;
                            Vector3 dv = P - V;
                            float   d2 = dv.sqrMagnitude;
                            if (d2 < 1f) continue;
                            float   d  = Mathf.Sqrt(d2);
                            if (!Visible(V, dv, d, R)) continue;
                            float width = Mathf.Max(Mathf.Lerp(1f - wv, 1f + wv, width01), 0.15f);
                            float e     = fade * Mathf.Lerp(0.7f, 1.6f, flare01) * width * Mathf.Min(ribbonW, 4f);
                            float night = 1f;
                            if (sunOn)
                            {
                                float cosSun = Vector3.Dot(P / rL, sunDir);
                                night = 1f - dayFade * sun.w * AuroraNoise.SmoothStep(-0.2f, 0.05f, cosSun);
                            }
                            float ds = rL * cl * dLon;
                            float w  = e * night * ds / (d2 + soft2);
                            if (w <= 0f) continue;

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

        Color LightColor()
        {
            Color c = auroraMaterial.GetColor("_ColorLow") * Mathf.Max(auroraMaterial.GetFloat("_GainLow"), 0f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m <= 1e-4f) return new Color(0.4f, 0.9f, 0.5f, 1f);
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        static bool Visible(Vector3 V, Vector3 dv, float d, float R)
        {

            float tt = Mathf.Clamp(-Vector3.Dot(V, dv) / (d * d), 0f, 1f);
            Vector3 q = V + dv * tt;
            float   r = R * 0.995f;
            return q.sqrMagnitude > r * r;
        }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
