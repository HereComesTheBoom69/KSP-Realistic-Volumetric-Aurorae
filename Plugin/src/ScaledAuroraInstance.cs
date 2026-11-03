// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

namespace RVA
{

    public class ScaledAuroraInstance : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Distant]";

        public CelestialBody        Body     => body;
        public string               BodyName => body != null ? body.bodyName : null;
        public Material             Material => material;
        public AuroraStormScheduler Storm    => storm;
        public AuroraMeanderTexture Meander  => meander;
        public ScaledAuroraDriver   Driver   => scaled;

        public AuroraRenderer       Renderer => (localRenderer != null && !MapView.MapIsEnabled) ? localRenderer : (scaled != null ? scaled.Renderer : null);
        public AuroraRenderer       LocalRenderer  => localRenderer;
        public float                RadiusUnits => radiusUnits;
        public bool                 Built    => material != null;

        public bool                 Visible  { get; private set; }

        public bool                 wanted = true;

        public CelestialBody        occluder;

        public string Status => material == null ? "not built"
                              : !wanted ? "off"
                              : !Visible ? $"culled ({lastAngleDeg:F2}° < {minAngleDeg:F2}°)"
                              : localRenderer != null && !MapView.MapIsEnabled
                                  ? $"{lastAngleDeg:F1}°, flight camera{(autoScaleOn && autoScale > 0f ? $", scale {autoScale:F2} by pixel budget (own {configuredScale:F2})" : "")}{(occluder != null && occluder != body ? $", hidden by {occluder.bodyName} (sphere datum + {occluderTerrain:F0} m, ground test {(localRenderer.OccluderDepthActive ? $"on its model ({modelWhy})" : "on the depth buffer")}, camera {occluderCamDist / 1000.0:F1} km from its centre, {(occluderCamDist - occluder.Radius) / 1000.0:F1} km up)" : "")}: {localRenderer.Status}"
                                  : $"{lastAngleDeg:F1}°, scaled space: {(scaled != null ? scaled.Status : "n/a")}";

        CelestialBody        body;
        Material             material;
        AuroraMeanderTexture meander;
        AuroraStormScheduler storm;
        ScaledAuroraDriver   scaled;
        GameObject           localGO;
        Material             localMaterial;
        AuroraRenderer       localRenderer;
        float                radiusUnits, altTopUnits, minAngleDeg = 0.5f, lastAngleDeg;
        float                configuredScale = 0.35f, autoScale = -1f;
        bool                 autoScaleOn = true;
        bool                 eclipses = true, eclipsed;
        Camera               scaledCam;
        int                  camSearch;

        public void Build(CelestialBody b, AuroraSettings s, bool withLocal)
        {
            if (b == null || s == null || RVABootstrap.AuroraShader == null) return;
            if (b.scaledBody == null) { Log($"{b.bodyName} has no scaled body, skipped"); return; }
            body = b;

            float unitsPerMetre = 1f / ScaledSpace.ScaleFactor;
            radiusUnits = (float)b.Radius * unitsPerMetre;

            material = new Material(RVABootstrap.AuroraShader);
            material.SetFloat("_SphereRadius", radiusUnits);
            material.SetFloat("_AltBase",      radiusUnits * s.altBaseFraction);
            material.SetFloat("_AltTop",       radiusUnits * (s.altBaseFraction + s.altTopFraction));
            material.SetFloat("_Activity",     s.baseActivity);
            material.SetFloat("_AnimSpeed",    s.baseAnimSpeed);
            material.SetFloat("_AnimTime",     0f);
            material.SetVector("_PlanetCenter", b.scaledBody.transform.position);

            meander = gameObject.AddComponent<AuroraMeanderTexture>();
            meander.auroraMaterial = material;

            storm = gameObject.AddComponent<AuroraStormScheduler>();
            storm.auroraMaterial = material;
            storm.worldScale     = radiusUnits / 10f;
            int seed = (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Seed : 0) ^ b.bodyName.GetHashCode();
            storm.Seed(seed);

            scaled = gameObject.AddComponent<ScaledAuroraDriver>();
            scaled.Build(b, null, material, s);

            if (withLocal && b.transform != null)
            {
                var stale = b.transform.Find($"RVA_distantLocal_{b.bodyName}");
                if (stale != null) Destroy(stale.gameObject);
                localGO = new GameObject($"RVA_distantLocal_{b.bodyName}");
                localGO.transform.SetParent(b.transform, false);
                localGO.transform.localPosition = Vector3.zero;
                localGO.transform.localRotation = Quaternion.identity;
                localGO.transform.localScale    = Vector3.one;
                localMaterial = new Material(RVABootstrap.AuroraShader);
                localMaterial.CopyPropertiesFromMaterial(material);
                localRenderer = localGO.AddComponent<AuroraRenderer>();
                localRenderer.auroraMaterial   = localMaterial;
                localRenderer.blitShader       = RVABootstrap.BlitShader;
                localRenderer.cameraName       = "";
                localRenderer.syncFrom         = material;
                localRenderer.syncScale        = ScaledSpace.ScaleFactor;
                localRenderer.forceFar         = true;
                localRenderer.renderingEnabled = false;
            }

            Apply(s);
            Log($"{b.bodyName}: aurora built (scaled radius {radiusUnits:F1} units, seed {seed}{(localRenderer != null ? ", flight-camera renderer too" : "")})");
        }

        public void Apply(AuroraSettings s)
        {
            if (material == null || s == null) return;
            float unitsPerMetre = 1f / ScaledSpace.ScaleFactor;
            RVAFlightSetup.PushMaterial(s, material, radiusUnits, unitsPerMetre, body != null ? body.bodyName : null);
            altTopUnits = radiusUnits * (s.altBaseFraction + s.altTopFraction * Mathf.Max(s.stormHeightGain, 1f));
            minAngleDeg = Mathf.Max(s.distantMinAngleDeg, 0f);
            eclipses    = s.eclipses;
            if (meander != null)
            {
                meander.resolution = Mathf.Clamp(s.meanderResolution, 32, 1024);
                meander.threaded   = s.meanderThreaded;
            }
            if (storm != null) RVAFlightSetup.ConfigureStorm(storm, s);
            if (scaled != null) scaled.Apply(s);
            configuredScale = Mathf.Clamp(s.renderScale, 0.1f, 1f);
            autoScaleOn     = s.distantAutoScale;
            if (localRenderer != null)
            {
                if (!autoScaleOn || autoScale < 0f) { localRenderer.renderScale = configuredScale; autoScale = -1f; }
                localRenderer.exposure            = s.exposure;
                localRenderer.flipY               = s.flipY;
                localRenderer.compositeEvent      = s.compositeEvent;

                localRenderer.cloudOcclusion      = s.cloudOcclusion && occluder != null && occluder.atmosphere;
                localRenderer.cloudBufferFormat   = s.cloudBufferFormat;
                localRenderer.bufferFlipY         = s.cloudBufferFlipY;
                localRenderer.sceneDepthOcclusion = s.sceneDepthOcclusion;
                localRenderer.smoothing           = Mathf.Clamp01(s.upsampleSmoothing);
                localRenderer.edgeAware           = s.edgeAwareUpsample;
                RVAFlightSetup.PushRendererPerf(localRenderer, s);
            }
        }

        static float OccluderRadius(CelestialBody b, double camDistance, double depthReach, out double terrainUsed)
        {

            double hMax = System.Math.Min(TerrainMax(b) * 1.15 + 100.0, b.Radius * 0.1);
            double blend = 1.0;
            if (camDistance > b.Radius && depthReach > 0.0)
            {
                double limb = System.Math.Sqrt(camDistance * camDistance - b.Radius * b.Radius);
                blend = System.Math.Max(0.0, System.Math.Min(1.0, (limb - 0.7 * depthReach) / (0.3 * depthReach)));
            }
            terrainUsed = hMax * blend;
            double r = b.Radius + terrainUsed;
            if (camDistance > 0.0) r = System.Math.Min(r, camDistance * 0.999);
            return (float)r;
        }

        class TerrainScan { public int next; public double maxHeight; public bool done; }
        static readonly System.Collections.Generic.Dictionary<string, TerrainScan> scans = new System.Collections.Generic.Dictionary<string, TerrainScan>();
        public static bool TerrainScanDone(CelestialBody b) => b != null && (b.pqsController == null || (scans.TryGetValue(b.bodyName, out TerrainScan sc) && sc.done));
        double occluderTerrain, occluderCamDist;
        bool   occLogged, occLoggedScanned;
        bool   modelDepthOn, modelMissingLogged;
        bool   mapInUse;
        float  transformGapLogged = -10f;
        string modelWhy = "";

        public static string HandPlanetModel(AuroraRenderer r, CelestialBody b, bool want)
        {
            if (r == null) return "";
            AuroraTerrainMap map = want && b != null ? AuroraTerrainMap.Get(b) : null;
            if (want) AuroraTerrainMap.Work();
            bool useMap = want && map != null && map.Ready && map.Texture != null;
            Mesh model  = !want ? null : useMap ? AuroraTerrainMap.Sphere : ScaledMesh(b);
            r.planetModelOn     = model != null;
            r.occluderMeshOn    = model != null;
            r.occluderMesh      = model;
            r.occluderHeightMap = useMap ? map.Texture : null;
            r.occluderDatum     = b != null ? (float)b.Radius : 0f;
            if (model != null) r.occluderMeshRS = useMap ? AuroraTerrainMap.Rotation(b) : ScaledMeshRS(b);
            if (model == null) return want ? "no model (no mesh on the scaled body)" : "";
            return useMap ? $"terrain map (surface from datum {map.MinHeight:+0;-0} m to datum {map.MaxHeight:+0;-0} m)"
                 : map != null && !map.Ready && !map.Failed ? $"smooth model, map {map.Progress * 100f:F0} %" : "smooth model";
        }

        public static Mesh ScaledMesh(CelestialBody b)
        {
            if (b == null || b.scaledBody == null) return null;
            MeshFilter mf = b.scaledBody.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        static readonly System.Collections.Generic.Dictionary<string, string> modelRanges = new System.Collections.Generic.Dictionary<string, string>();
        static string ModelRange(CelestialBody b, Mesh m)
        {
            if (modelRanges.TryGetValue(b.bodyName, out string cached)) return cached;
            string r = "mesh not readable";
            try
            {
                if (m.isReadable)
                {
                    Vector3[] v = m.vertices;
                    if (v != null && v.Length > 0)
                    {
                        Matrix4x4 rs = ScaledMeshRS(b);
                        float lo = float.MaxValue, hi = float.MinValue;
                        for (int i = 0; i < v.Length; i++)
                        {
                            float d = rs.MultiplyPoint3x4(v[i]).magnitude - (float)b.Radius;
                            if (d < lo) lo = d;
                            if (d > hi) hi = d;
                        }
                        r = $"surface from datum {lo:+0;-0} m to datum {hi:+0;-0} m";
                    }
                }
            }
            catch (System.Exception e) { r = "mesh not measured: " + e.GetType().Name; }
            modelRanges[b.bodyName] = r;
            return r;
        }

        public static Matrix4x4 ScaledMeshRS(CelestialBody b)
        {
            Matrix4x4 m = b.scaledBody.transform.localToWorldMatrix;
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return Matrix4x4.Scale(Vector3.one * ScaledSpace.ScaleFactor) * m;
        }

        public static double TerrainMax(CelestialBody b)
        {
            if (b == null || b.pqsController == null) return 0.0;
            if (!scans.TryGetValue(b.bodyName, out TerrainScan sc)) { sc = new TerrainScan(); scans[b.bodyName] = sc; }
            if (sc.done) return sc.maxHeight;
            const int total = 4096, perFrame = 48;
            try
            {
                for (int k = 0; k < perFrame && sc.next < total; k++, sc.next++)
                {

                    double z   = 1.0 - 2.0 * (sc.next + 0.5) / total;
                    double rxy = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - z * z));
                    double phi = sc.next * 2.399963229728653;
                    Vector3d dir = new Vector3d(rxy * System.Math.Cos(phi), z, rxy * System.Math.Sin(phi));
                    double h = b.pqsController.GetSurfaceHeight(dir) - b.Radius;
                    if (h > sc.maxHeight && h < b.Radius) sc.maxHeight = h;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RVA] {b.bodyName}: terrain scan stopped ({e.GetType().Name}: {e.Message}); the occluder sphere keeps {sc.maxHeight:F0} m of terrain");
                sc.next = total;
            }
            if (sc.next >= total)
            {
                sc.done = true;
                Debug.Log($"[RVA] {b.bodyName}: highest terrain {sc.maxHeight:F0} m above the datum (occluder sphere)");
            }
            return sc.maxHeight;
        }

        void LateUpdate()
        {
            if (material == null || body == null || body.scaledBody == null) return;

            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun != null && sun != body && sun.scaledBody != null)
            {
                Vector3 d = (sun.scaledBody.transform.position - body.scaledBody.transform.position).normalized;
                float cover = eclipses ? AuroraEclipse.Coverage(body, sun) : 0f;
                material.SetVector("_SunDir", new Vector4(d.x, d.y, d.z, 1f - cover));
                bool dark = cover > 0.5f;
                if (dark != eclipsed)
                {
                    eclipsed = dark;
                    Log($"{body.bodyName}: {(dark ? $"in {body.referenceBody.bodyName}'s shadow, daylight fade lifted" : "back in sunlight, daylight fade on")}");
                }
            }
            else material.SetVector("_SunDir", Vector4.zero);

            if (localRenderer != null)
            {
                bool occ = occluder != null && occluder != body && occluder.transform != null;
                Camera rc = localRenderer.TargetCamera;
                Transform fc = rc != null ? rc.transform : (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform : null);

                double camDist = occ && fc != null ? Vector3.Distance(fc.position, (Vector3)occluder.position) : -1.0;

                double camAlt    = occ ? camDist - occluder.Radius : 0.0;
                double vesselAlt = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.altitude : 0.0;
                bool terrainOff  = occ && (occluder.pqsController == null || !occluder.pqsController.isActive);

                localRenderer.cameraAltitudeM    = occ ? (float)camAlt : -1f;
                localRenderer.cloudBody          = occ ? occluder.bodyName : null;
                localRenderer.volumetricsCeilingM = occ && AuroraCloudTops.TryGet(occluder.bodyName, out var occClouds) ? occClouds.VolumetricsCeilingM : -1f;
                bool highCamera  = occ && camAlt > vesselAlt + 2000.0 && camAlt > 2.5 * TerrainMax(occluder) && camAlt > 5000.0;

                AuroraTerrainMap map = occ && (Visible || AuroraTerrainMap.Has(occluder)) ? AuroraTerrainMap.Get(occluder) : null;
                AuroraTerrainMap.Work();
                bool wantModel = occ && (terrainOff || highCamera);
                bool useMap    = wantModel && map != null && map.Ready && map.Texture != null;
                Mesh model = !wantModel ? null : useMap ? AuroraTerrainMap.Sphere : ScaledMesh(occluder);
                localRenderer.occluderMeshOn     = model != null;
                localRenderer.occluderMesh       = model;
                localRenderer.occluderHeightMap  = useMap ? map.Texture : null;
                localRenderer.occluderDatum      = occ ? (float)occluder.Radius : 0f;
                if (model != null) localRenderer.occluderMeshRS = useMap ? AuroraTerrainMap.Rotation(occluder) : ScaledMeshRS(occluder);
                string mapNote = useMap ? "terrain map" : map != null && !map.Ready && !map.Failed ? $"smooth model, map {map.Progress * 100f:F0} %" : "smooth model";
                modelWhy = model == null ? "" : (terrainOff ? "terrain off" : $"camera {(camAlt - vesselAlt) / 1000.0:F0} km above the vessel") + ", " + mapNote;
                if (occ && (model != null) != modelDepthOn)
                {
                    modelDepthOn = model != null;
                    Log(modelDepthOn
                        ? $"{body.bodyName}: {occluder.bodyName}'s model is the ground test now ({modelWhy}; camera {camAlt:F0} m up, vessel {vesselAlt:F0} m; model {model.vertexCount} vertices, {(useMap ? $"surface from datum {map.MinHeight:+0;-0} m to datum {map.MaxHeight:+0;-0} m" : ModelRange(occluder, model))})"
                        : $"{body.bodyName}: the depth buffer is the ground test again (camera {camAlt:F0} m up, vessel {vesselAlt:F0} m)");
                }
                if (occ && useMap != mapInUse)
                {
                    mapInUse = useMap;
                    if (useMap) Log($"{body.bodyName}: {occluder.bodyName}'s terrain map is the model now (surface from datum {map.MinHeight:+0;-0} m to datum {map.MaxHeight:+0;-0} m)");
                }
                else if (occ && (terrainOff || highCamera) && model == null && !modelMissingLogged)
                {
                    modelMissingLogged = true;
                    Log($"{body.bodyName}: no mesh on {occluder.bodyName}'s scaled body, the depth buffer stays the ground test");
                }

                double reach   = model != null ? 1e12 : (rc != null ? rc.farClipPlane : 0.0);
                localRenderer.occluder       = occ ? occluder.transform : null;
                localRenderer.occluderBody   = occ ? occluder : null;
                localRenderer.planetBody     = body;

                if (occ && model != null && Time.unscaledTime - transformGapLogged > 1f)
                {
                    transformGapLogged = Time.unscaledTime;
                    float gap = Vector3.Distance(occluder.transform.position, (Vector3)occluder.position);
                    if (gap > 2f) Log($"{body.bodyName}: {occluder.bodyName}'s transform is {gap:F0} m from its orbital position ({modelWhy})");
                }
                localRenderer.occluderRadius = occ ? OccluderRadius(occluder, camDist, reach, out occluderTerrain) : 0f;
                occluderCamDist = camDist;

                if (occ && rc != null)
                {
                    bool scanned = TerrainScanDone(occluder);
                    if (!occLogged || (scanned && !occLoggedScanned))
                    {
                        occLogged = true; occLoggedScanned = scanned;
                        Log($"{body.bodyName}: occluder {occluder.bodyName} datum {occluder.Radius:F0} m, highest terrain {TerrainMax(occluder):F0} m{(scanned ? "" : " (scan running)")}, "
                          + $"sphere datum + {occluderTerrain:F0} m, camera {camDist:F0} m from its centre; camera '{rc.name}' near {rc.nearClipPlane:F2} far {rc.farClipPlane:F0}");
                    }
                }
            }

            var sr = scaled != null ? scaled.Renderer : null;
            if (sr != null)
            {
                CelestialBody focus = MapFocusBody();
                if (focus == null || focus == body) focus = occluder;
                bool occ = focus != null && focus != body && focus.scaledBody != null;
                double camDist = occ && scaledCam != null ? Vector3.Distance(scaledCam.transform.position, focus.scaledBody.transform.position) * ScaledSpace.ScaleFactor : -1.0;
                sr.occluder       = occ ? focus.scaledBody.transform : null;
                sr.occluderRadius = occ ? OccluderRadius(focus, camDist, 0.0, out double unused) / ScaledSpace.ScaleFactor : 0f;
            }

            bool inMap = MapView.MapIsEnabled;
            bool visible = wanted;
            if (visible)
            {
                Transform camT = null;
                float r = radiusUnits + altTopUnits, dist = -1f;
                if (!inMap && localRenderer != null)
                {
                    camT = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform : null;
                    if (camT != null) { dist = Vector3.Distance(camT.position, body.transform.position); r *= ScaledSpace.ScaleFactor; }
                }
                else
                {
                    if (scaledCam == null && ++camSearch % 30 == 1)
                        foreach (var c in Camera.allCameras)
                            if (c != null && c.name == ScaledAuroraDriver.ScaledCameraName) { scaledCam = c; break; }
                    if (scaledCam != null) dist = Vector3.Distance(scaledCam.transform.position, body.scaledBody.transform.position);
                }
                if (dist >= 0f)
                {
                    lastAngleDeg = dist <= r ? 180f : Mathf.Asin(Mathf.Clamp01(r / dist)) * Mathf.Rad2Deg;
                    visible = lastAngleDeg >= minAngleDeg;
                }

                if (!inMap && localRenderer != null && camT != null && visible)
                {
                    float target = autoScaleOn ? BudgetScale(configuredScale, 1f, localRenderer.renderScale, FlightCamera.fetch.mainCamera, 2f * lastAngleDeg) : configuredScale;
                    if (Mathf.Abs(target - localRenderer.renderScale) > 0.001f) { localRenderer.renderScale = target; autoScale = target; }
                }
            }
            Visible = visible;

            bool useLocal = localRenderer != null && !inMap;
            if (localRenderer != null) localRenderer.renderingEnabled = visible && useLocal;
            if (scaled != null) scaled.SetActive(visible && !useLocal);
        }

        public static float BudgetScale(float configured, float cap, float current, Camera cam, float spanDeg)
        {
            if (cam == null) return configured;
            cap = Mathf.Max(cap, configured);
            float vFov = Mathf.Max(cam.fieldOfView, 1f);
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(cam.aspect, 0.1f)) * Mathf.Rad2Deg;
            float area = Mathf.Min(1f, spanDeg / hFov) * Mathf.Min(1f, spanDeg / vFov);
            float t    = Mathf.Clamp(configured / Mathf.Sqrt(Mathf.Max(area, 1e-4f)), configured, cap);
            if (t <= configured + 0.001f) return configured;
            if (current > configured + 0.001f && current <= cap + 0.001f && Mathf.Abs(t - current) < 0.04f) return current;
            return Mathf.Clamp(Mathf.Round(t * 20f) / 20f, configured, cap);
        }

        public static CelestialBody MapFocusBody()
        {
            try
            {
                var pc = PlanetariumCamera.fetch;
                var t  = pc != null ? pc.target : null;
                if (t != null)
                {
                    if (t.celestialBody != null) return t.celestialBody;
                    if (t.vessel != null) return t.vessel.mainBody;
                }
            }
            catch { }
            return null;
        }

        public void Shutdown()
        {
            if (scaled != null) { scaled.Shutdown(); scaled = null; }
            if (localRenderer != null) { localRenderer.renderingEnabled = false; localRenderer.syncFrom = null; localRenderer = null; }
            if (localGO != null) { Destroy(localGO); localGO = null; }
            if (localMaterial != null) { Destroy(localMaterial); localMaterial = null; }
            if (material != null) { Destroy(material); material = null; }
        }

        void OnDestroy() { Shutdown(); }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
