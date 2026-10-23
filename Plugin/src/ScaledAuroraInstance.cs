// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// The aurora of a body that is NOT the one the vessel is at (1.1.0): Jool's seen
    /// from Laythe, any body the map view is looking at, every body in the tracking
    /// station. It owns one master material in scaled-space units, driven on its own
    /// (meander LUT, storm scheduler, sun direction), and up to two renderers that
    /// follow it:
    ///   - scaled: a ScaledAuroraDriver on "Camera ScaledSpace" — the map view and the
    ///     tracking station, where the planets themselves are scaled-space objects;
    ///   - local (flight only): an AuroraRenderer on the flight camera, synced from the
    ///     master material × ScaleFactor into metres and parented to the body's
    ///     local-space transform. It gets what the local aurora gets — the terrain /
    ///     vessel depth test, EVE's per-pixel cloud occlusion (every sample counts as
    ///     beyond this world's clouds), and an exact analytic occluder for the body the
    ///     vessel is at, so Laythe hides Jool's aurora at any altitude.
    /// In flight view the local renderer draws and the scaled one rests; in the map
    /// view the reverse. Both are culled while the band spans less than
    /// distantMinAngleDeg of view, so an aurora around a dot costs nothing.
    /// </summary>
    public class ScaledAuroraInstance : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Distant]";

        public CelestialBody        Body     => body;
        public string               BodyName => body != null ? body.bodyName : null;
        public Material             Material => material;
        public AuroraStormScheduler Storm    => storm;
        public AuroraMeanderTexture Meander  => meander;
        public ScaledAuroraDriver   Driver   => scaled;
        /// <summary>The renderer that draws right now (flight-camera one in flight view, scaled one in the map / tracking station).</summary>
        public AuroraRenderer       Renderer => (localRenderer != null && !MapView.MapIsEnabled) ? localRenderer : (scaled != null ? scaled.Renderer : null);
        public AuroraRenderer       LocalRenderer  => localRenderer;
        public float                RadiusUnits => radiusUnits;
        public bool                 Built    => material != null;
        /// <summary>False while culled by angular size or switched off by the owner.</summary>
        public bool                 Visible  { get; private set; }
        /// <summary>Owner switch (map view off …). Default on.</summary>
        public bool                 wanted = true;
        /// <summary>The body the vessel is at: hides this aurora (flight only). Null = none.</summary>
        public CelestialBody        occluder;

        public string Status => material == null ? "not built"
                              : !wanted ? "off"
                              : !Visible ? $"culled ({lastAngleDeg:F2}° < {minAngleDeg:F2}°)"
                              : localRenderer != null && !MapView.MapIsEnabled
                                  ? $"{lastAngleDeg:F1}°, flight camera{(autoScaleOn && autoScale > 0f ? $", scale {autoScale:F2} by pixel budget (own {configuredScale:F2})" : "")} — {localRenderer.Status}"
                                  : $"{lastAngleDeg:F1}°, scaled space — {(scaled != null ? scaled.Status : "n/a")}";

        CelestialBody        body;
        Material             material;              // master, scaled-space units
        AuroraMeanderTexture meander;
        AuroraStormScheduler storm;
        ScaledAuroraDriver   scaled;
        GameObject           localGO;               // under the body's local transform
        Material             localMaterial;
        AuroraRenderer       localRenderer;
        float                radiusUnits, altTopUnits, minAngleDeg = 0.5f, lastAngleDeg;
        float                configuredScale = 0.35f, autoScale = -1f;   // the body's own render scale, and the one in force on the flight camera (1.1.0)
        bool                 autoScaleOn = true;
        bool                 eclipses = true, eclipsed;
        Camera               scaledCam;
        int                  camSearch;

        /// <param name="withLocal">also render on the flight camera (flight scene; false in the tracking station)</param>
        public void Build(CelestialBody b, AuroraSettings s, bool withLocal)
        {
            if (b == null || s == null || RVABootstrap.AuroraShader == null) return;
            if (b.scaledBody == null) { Log($"{b.bodyName} has no scaled body — skipped"); return; }
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
                localRenderer.cameraName       = "";                       // the flight camera
                localRenderer.syncFrom         = material;
                localRenderer.syncScale        = ScaledSpace.ScaleFactor;  // scaled units → metres
                localRenderer.forceFar         = true;                     // behind this world's clouds everywhere
                localRenderer.renderingEnabled = false;
            }

            Apply(s);
            Log($"{b.bodyName}: aurora built (scaled radius {radiusUnits:F1} units, seed {seed}{(localRenderer != null ? ", flight-camera renderer too" : "")})");
        }

        /// <summary>Push the body's settings to the material, LUT, storm and renderers.</summary>
        public void Apply(AuroraSettings s)
        {
            if (material == null || s == null) return;
            float unitsPerMetre = 1f / ScaledSpace.ScaleFactor;
            RVAFlightSetup.PushMaterial(s, material, radiusUnits, unitsPerMetre);
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
                localRenderer.cloudOcclusion      = s.cloudOcclusion;
                localRenderer.cloudBufferFormat   = s.cloudBufferFormat;
                localRenderer.bufferFlipY         = s.cloudBufferFlipY;
                localRenderer.sceneDepthOcclusion = s.sceneDepthOcclusion;
                localRenderer.smoothing           = Mathf.Clamp01(s.upsampleSmoothing);
                localRenderer.edgeAware           = s.edgeAwareUpsample;
                RVAFlightSetup.PushRendererPerf(localRenderer, s);
            }
        }

        void LateUpdate()
        {
            if (material == null || body == null || body.scaledBody == null) return;

            // sun direction (daylight fade), in scaled space — the same direction in local space;
            // w = the fade's weight, lifted while this body's parent eclipses the sun (Laythe behind Jool)
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
                    Log($"{body.bodyName}: {(dark ? $"in {body.referenceBody.bodyName}'s shadow — daylight fade lifted" : "back in sunlight — daylight fade on")}");
                }
            }
            else material.SetVector("_SunDir", Vector4.zero);

            // the occluder: the body the vessel is at (flight only), slightly shrunk so a
            // camera on its surface is always outside the sphere
            if (localRenderer != null)
            {
                bool occ = occluder != null && occluder != body && occluder.transform != null;
                localRenderer.occluder       = occ ? occluder.transform : null;
                localRenderer.occluderRadius = occ ? (float)occluder.Radius * 0.995f : 0f;
            }
            // … and in the map view / tracking station the body the map camera is looking at
            // (or the vessel's), in scaled space: the scaled camera has no usable depth, so
            // without this Jool's aurora shows through Laythe on the map (1.1.0)
            var sr = scaled != null ? scaled.Renderer : null;
            if (sr != null)
            {
                CelestialBody focus = MapFocusBody();
                if (focus == null || focus == body) focus = occluder;
                bool occ = focus != null && focus != body && focus.scaledBody != null;
                sr.occluder       = occ ? focus.scaledBody.transform : null;
                sr.occluderRadius = occ ? (float)(focus.Radius / ScaledSpace.ScaleFactor) * 0.995f : 0f;
            }

            // angular cull: how much of the view the band's outer shell spans
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

                // Render scale by pixel budget (1.1.0): a ray that misses this body's shell
                // costs almost nothing, so what a distant aurora costs is its buffer pixels
                // INSIDE the body's outline. Keep that number at what the body's own render
                // scale would spend filling the screen, and the scale can rise toward 1.0
                // as the body shrinks in the view — Jool from Laythe orbit renders sharp
                // for the price of a 0.35 Kerbin, and the same setting still means 0.35
                // when you are in Jool orbit and it fills the sky.
                if (!inMap && localRenderer != null && camT != null && visible)
                {
                    float target = configuredScale;
                    if (autoScaleOn)
                    {
                        Camera cam = FlightCamera.fetch.mainCamera;
                        float vFov = Mathf.Max(cam.fieldOfView, 1f);
                        float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(cam.aspect, 0.1f)) * Mathf.Rad2Deg;
                        float span = 2f * lastAngleDeg;                                   // the band's outline, degrees across
                        float area = Mathf.Min(1f, span / hFov) * Mathf.Min(1f, span / vFov);  // share of the screen its bounding box covers
                        target = Mathf.Clamp(configuredScale / Mathf.Sqrt(Mathf.Max(area, 1e-4f)), configuredScale, 1f);
                        target = Mathf.Round(target * 20f) / 20f;                         // 0.05 steps: a new scale re-allocates the buffers
                    }
                    if (Mathf.Abs(target - localRenderer.renderScale) > 0.001f) { localRenderer.renderScale = target; autoScale = target; }
                }
            }
            Visible = visible;

            // flight view → the flight-camera renderer; map view / no local renderer → the scaled one
            bool useLocal = localRenderer != null && !inMap;
            if (localRenderer != null) localRenderer.renderingEnabled = visible && useLocal;
            if (scaled != null) scaled.SetActive(visible && !useLocal);
        }

        /// <summary>The body the map camera is on: its target when that is a body, the target vessel's main body otherwise; null when there is no map camera.</summary>
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
