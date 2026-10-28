// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
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
    ///   - scaled: a ScaledAuroraDriver on "Camera ScaledSpace", the map view and the
    ///     tracking station, where the planets themselves are scaled-space objects;
    ///   - local (flight only): an AuroraRenderer on the flight camera, synced from the
    ///     master material x ScaleFactor into metres and parented to the body's
    ///     local-space transform. It gets what the local aurora gets: the terrain /
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
        /// <summary>Owner switch (map view off ...). Default on.</summary>
        public bool                 wanted = true;
        /// <summary>The body the vessel is at: hides this aurora (flight only). Null = none.</summary>
        public CelestialBody        occluder;

        public string Status => material == null ? "not built"
                              : !wanted ? "off"
                              : !Visible ? $"culled ({lastAngleDeg:F2}° < {minAngleDeg:F2}°)"
                              : localRenderer != null && !MapView.MapIsEnabled
                                  ? $"{lastAngleDeg:F1}°, flight camera{(autoScaleOn && autoScale > 0f ? $", scale {autoScale:F2} by pixel budget (own {configuredScale:F2})" : "")}{(occluder != null && occluder != body ? $", hidden by {occluder.bodyName} (sphere datum + {occluderTerrain:F0} m, ground test up to datum + {localRenderer.occluderShellOuter - occluder.Radius:F0} m{(localRenderer.OccluderDepthActive ? $" on its model ({modelWhy})" : " on the depth buffer")}, camera {occluderCamDist / 1000.0:F1} km from its centre, {(occluderCamDist - occluder.Radius) / 1000.0:F1} km up)" : "")}: {localRenderer.Status}"
                                  : $"{lastAngleDeg:F1}°, scaled space: {(scaled != null ? scaled.Status : "n/a")}";

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
                localRenderer.cameraName       = "";                       // the flight camera
                localRenderer.syncFrom         = material;
                localRenderer.syncScale        = ScaledSpace.ScaleFactor;  // scaled units -> metres
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
                // EVE's cloud buffer is only fresh while EVE renders volumetric clouds, which it does
                // on bodies with an atmosphere. Around an airless body (the Mun) the buffer keeps
                // whatever it last held, Kerbin's clouds as of the last frame they were drawn, and
                // multiplying another body's aurora by that stale transmittance hid Kerbin's aurora
                // from Mun orbit wherever those old clouds sat on the screen.
                localRenderer.cloudOcclusion      = s.cloudOcclusion && occluder != null && occluder.atmosphere;
                localRenderer.cloudBufferFormat   = s.cloudBufferFormat;
                localRenderer.bufferFlipY         = s.cloudBufferFlipY;
                localRenderer.sceneDepthOcclusion = s.sceneDepthOcclusion;
                localRenderer.smoothing           = Mathf.Clamp01(s.upsampleSmoothing);
                localRenderer.edgeAware           = s.edgeAwareUpsample;
                RVAFlightSetup.PushRendererPerf(localRenderer, s);
            }
        }

        /// <summary>The occluder sphere's radius for a body, in metres: the datum plus the body's
        /// highest terrain (see TerrainMax; the datum alone for a body without terrain), capped
        /// just under the camera's distance from the centre so that the camera is always outside
        /// the sphere. camDistance in metres, or negative when unknown.</summary>
        static float OccluderRadius(CelestialBody b, double camDistance, double depthReach, out double terrainUsed)
        {
            // The sphere's terrain height is for the limb the depth buffer cannot see. Where the
            // limb is within the camera's far plane the depth samples cut the aurora at the real
            // terrain, and a sphere at the highest terrain would cut it a few degrees above that
            // (3 degrees from a 30 km Mun orbit), so the height is blended in only as the limb
            // moves out past the depth buffer's reach: none while the limb is within 70 percent of
            // the far plane, all of it beyond the far plane.
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

        // The highest terrain of each body above its datum, found by asking its PQS for the
        // surface height in 4096 evenly spread directions, 48 per frame, once per session. The
        // occluder sphere is the datum plus this: the mountains on the limb stand above the datum,
        // and a datum sphere let another body's aurora through them (Kerbin's from Mun orbit).
        class TerrainScan { public int next; public double maxHeight; public bool done; }
        static readonly System.Collections.Generic.Dictionary<string, TerrainScan> scans = new System.Collections.Generic.Dictionary<string, TerrainScan>();
        /// <summary>The body's terrain shell for the shader's ground test, in the given units: from
        /// 15 percent under the datum to the highest terrain plus a fifth of it and 1 percent of the
        /// radius (the scan can miss a peak, and the rendered terrain's detail level differs a little
        /// from the heights the PQS reports).</summary>
        static void TerrainShell(CelestialBody b, double unitsPerMetre, out float inner, out float outer)
        {
            double hMax = System.Math.Min(TerrainMax(b) * 1.2 + b.Radius * 0.01, b.Radius * 0.12);
            inner = (float)(b.Radius * 0.85 * unitsPerMetre);
            outer = (float)((b.Radius + hMax) * unitsPerMetre);
        }

        static bool TerrainScanDone(CelestialBody b) => b != null && (b.pqsController == null || (scans.TryGetValue(b.bodyName, out TerrainScan sc) && sc.done));
        double occluderTerrain, occluderCamDist;      // what the sphere is using right now, for the status line
        bool   occLogged, occLoggedScanned;
        bool   modelDepthOn, modelMissingLogged;      // the occluder's model is the depth source; its absence was logged
        bool   mapInUse;                              // the model is the terrain map's sphere (else KSP's smooth one)
        float  transformGapLogged = -10f;             // when the transform-versus-position gap was last checked
        string modelWhy = "";                         // why the model is in use, for the status line

        /// <summary>The body's scaled-space model (the mesh KSP shows for it from far away).</summary>
        static Mesh ScaledMesh(CelestialBody b)
        {
            if (b == null || b.scaledBody == null) return null;
            MeshFilter mf = b.scaledBody.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>How far the model's surface runs under and over the body's datum, in metres,
        /// measured once per body from the mesh (when it is readable) through the same matrix
        /// the renderer draws it with. For the log: KSP's models do not follow the terrain's
        /// heights exactly, and this says by how much.</summary>
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

        /// <summary>That model's rotation and scale in metres, without its position: the
        /// renderer adds the body's local position every frame, after the floating origin
        /// has moved. The scaled transform's own scale and ScaledSpace's 1 : 6000 both apply.</summary>
        static Matrix4x4 ScaledMeshRS(CelestialBody b)
        {
            Matrix4x4 m = b.scaledBody.transform.localToWorldMatrix;
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return Matrix4x4.Scale(Vector3.one * ScaledSpace.ScaleFactor) * m;
        }

        static double TerrainMax(CelestialBody b)
        {
            if (b == null || b.pqsController == null) return 0.0;
            if (!scans.TryGetValue(b.bodyName, out TerrainScan sc)) { sc = new TerrainScan(); scans[b.bodyName] = sc; }
            if (sc.done) return sc.maxHeight;
            const int total = 4096, perFrame = 48;
            try
            {
                for (int k = 0; k < perFrame && sc.next < total; k++, sc.next++)
                {
                    // a Fibonacci sphere: direction number n of 4096, evenly spread
                    double z   = 1.0 - 2.0 * (sc.next + 0.5) / total;
                    double rxy = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - z * z));
                    double phi = sc.next * 2.399963229728653;          // the golden angle
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

            // sun direction (daylight fade), in scaled space, the same direction in local space;
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
                    Log($"{body.bodyName}: {(dark ? $"in {body.referenceBody.bodyName}'s shadow, daylight fade lifted" : "back in sunlight, daylight fade on")}");
                }
            }
            else material.SetVector("_SunDir", Vector4.zero);

            // the occluder: the body the vessel is at (flight only). The ground near the limb is
            // cut by a depth test against the body's terrain shell (the shader does that for an
            // aurora that has an occluder); the sphere, at the datum, covers the body itself, and
            // rises to the terrain's highest point only as the limb nears the camera's far plane,
            // beyond which the depth buffer cannot see the terrain. Never larger than the camera's
            // own distance from the centre less a margin, so a camera down among the terrain is
            // always outside it.
            if (localRenderer != null)
            {
                bool occ = occluder != null && occluder != body && occluder.transform != null;
                Camera rc = localRenderer.TargetCamera;
                Transform fc = rc != null ? rc.transform : (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform : null);
                // distances from the body's orbital position, not its transform (see the renderer)
                double camDist = occ && fc != null ? Vector3.Distance(fc.position, (Vector3)occluder.position) : -1.0;
                // KSP builds the terrain around the vessel, out to its horizon, and above the body's
                // PQS limit (120 km on the Mun) drops it for the body's scaled-space model, which
                // another camera draws. A camera zoomed far out above the vessel sees past the
                // terrain that exists, so the flight camera's depth buffer has no ground at the limb
                // and Kerbin's aurora showed through it again. The renderer then draws a model
                // itself, as a depth source of its own: whenever the terrain is off, and whenever the
                // camera is well above the vessel and clear of the terrain (not from the ground or
                // a low zoom, where a model is too coarse to stand in for the terrain it matches
                // from afar). The model is a sphere pushed out by a height map of the body's terrain
                // (AuroraTerrainMap, built from the height function that never switches off); until
                // that map is ready, KSP's own scaled-space model, which is a smooth sphere.
                double camAlt    = occ ? camDist - occluder.Radius : 0.0;
                double vesselAlt = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.altitude : 0.0;
                bool terrainOff  = occ && (occluder.pqsController == null || !occluder.pqsController.isActive);
                bool highCamera  = occ && camAlt > vesselAlt + 2000.0 && camAlt > 2.5 * TerrainMax(occluder) && camAlt > 5000.0;
                // the map starts building on arrival, but only where this aurora is in view at all:
                // from Eve nothing of Kerbin's shows, so Eve gets no map
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
                        ? $"{body.bodyName}: {occluder.bodyName}'s model is the ground test now ({modelWhy}; camera {camAlt:F0} m up, vessel {vesselAlt:F0} m; model {model.vertexCount} vertices, {(useMap ? $"surface from datum {map.MinHeight:+0;-0} m to datum {map.MaxHeight:+0;-0} m" : ModelRange(occluder, model))}, terrain shell to datum +{localRenderer.occluderShellOuter - occluder.Radius:F0} m)"
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
                // the model is drawn without the far plane's clip, so with it in use the sphere stays
                // at the datum however far the limb is; otherwise the sphere rises as the limb nears
                // the far plane, beyond which the depth buffer cannot see the terrain
                double reach   = model != null ? 1e12 : (rc != null ? rc.farClipPlane : 0.0);
                localRenderer.occluder       = occ ? occluder.transform : null;
                localRenderer.occluderBody   = occ ? occluder : null;
                localRenderer.planetBody     = body;
                // once a second while the model is in use: how far the occluder's local transform sits
                // from its orbital position. Anything beyond a few metres is the drift that showed the
                // aurora under the horizon again while the terrain was off.
                if (occ && model != null && Time.unscaledTime - transformGapLogged > 1f)
                {
                    transformGapLogged = Time.unscaledTime;
                    float gap = Vector3.Distance(occluder.transform.position, (Vector3)occluder.position);
                    if (gap > 2f) Log($"{body.bodyName}: {occluder.bodyName}'s transform is {gap:F0} m from its orbital position ({modelWhy})");
                }
                localRenderer.occluderRadius = occ ? OccluderRadius(occluder, camDist, reach, out occluderTerrain) : 0f;
                if (occ) TerrainShell(occluder, 1.0, out localRenderer.occluderShellInner, out localRenderer.occluderShellOuter);
                occluderCamDist = camDist;
                // one line of numbers to KSP.log when the picture changes (the first time, and when the terrain scan ends)
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
            // ... and in the map view / tracking station the body the map camera is looking at
            // (or the vessel's), in scaled space: the scaled camera has no usable depth, so
            // without this Jool's aurora shows through Laythe on the map (1.1.0)
            var sr = scaled != null ? scaled.Renderer : null;
            if (sr != null)
            {
                CelestialBody focus = MapFocusBody();
                if (focus == null || focus == body) focus = occluder;
                bool occ = focus != null && focus != body && focus.scaledBody != null;
                double camDist = occ && scaledCam != null ? Vector3.Distance(scaledCam.transform.position, focus.scaledBody.transform.position) * ScaledSpace.ScaleFactor : -1.0;
                sr.occluder       = occ ? focus.scaledBody.transform : null;
                sr.occluderRadius = occ ? OccluderRadius(focus, camDist, 0.0, out double unused) / ScaledSpace.ScaleFactor : 0f;   // no depth in scaled space: the full terrain height
                if (occ) TerrainShell(focus, 1.0 / ScaledSpace.ScaleFactor, out sr.occluderShellInner, out sr.occluderShellOuter);
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
                // as the body shrinks in the view: Jool from Laythe orbit renders sharp
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

            // flight view: the flight-camera renderer; map view or no local renderer: the scaled one
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
