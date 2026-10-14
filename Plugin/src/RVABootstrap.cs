// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Collections;
using System.IO;
using UnityEngine;

[assembly: KSPAssembly("RealisticVolumetricAurorae", 1, 0)]

namespace RVA
{
    /// <summary>
    /// Runs once at the main menu: loads settings + the shader bundle and
    /// keeps the shaders in static fields for the flight-scene setup to use.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RVABootstrap : MonoBehaviour
    {
        public static Shader AuroraShader { get; private set; }
        public static Shader BlitShader   { get; private set; }
        public static bool   ShaderLoaded { get; private set; }
        public static AuroraSettings Settings = new AuroraSettings();

        /// <summary>Kit version — logged at startup and shown in the window title,
        /// so it is always obvious which DLL KSP actually loaded.</summary>
        public const string Version = "1.0.0";

        const string LogPrefix = "[RVA]";
        const string BundleRelativePath = "GameData/RealisticVolumetricAurorae/Bundles/rva_shaders";

        void Awake()
        {
            Log($"Realistic Volumetric Aurorae (RVA) v{Version}{(AuroraSettings.DevBuild ? " (dev build: every setting exposed)" : " (release build)")} — bootstrap awake, loading settings and asset bundle");
            DontDestroyOnLoad(gameObject);

            bool hadFile = File.Exists(AuroraSettings.FilePath);
            Settings = AuroraSettings.Load(out string report);
            Log("Settings: " + report);
            if (!hadFile)
            {
                // First launch (or a CKAN install, which ships no settings.cfg so that
                // upgrades never overwrite the user's): write the defaults so there is a
                // file to edit. Updates leave it alone from here on.
                try { Settings.Save(); Log($"Settings: wrote the default settings file to {AuroraSettings.FilePath}"); }
                catch (System.Exception e) { LogWarn($"Settings: could not write {AuroraSettings.FilePath} ({e.GetType().Name}: {e.Message}) — running on built-in defaults"); }
            }

            StartCoroutine(LoadShaderBundle());
        }

        IEnumerator LoadShaderBundle()
        {
            string bundlePath = Path.Combine(KSPUtil.ApplicationRootPath, BundleRelativePath);
            Log($"Bundle path: {bundlePath}");

            if (!File.Exists(bundlePath))
            {
                LogError($"Bundle not found at {bundlePath} — plugin disabled");
                yield break;
            }

            var bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return bundleRequest;
            var bundle = bundleRequest.assetBundle;
            if (bundle == null)
            {
                LogError("Bundle file present but failed to load (wrong Unity version?)");
                yield break;
            }

            Log("Bundle loaded. Assets:");
            foreach (var name in bundle.GetAllAssetNames()) Log($"  - {name}");

            foreach (var assetName in bundle.GetAllAssetNames())
            {
                if (!assetName.EndsWith(".shader")) continue;
                var req = bundle.LoadAssetAsync<Shader>(assetName);
                yield return req;
                var sh = req.asset as Shader;
                if (sh == null) continue;
                Log($"Loaded '{sh.name}' from {assetName} (supported={sh.isSupported})");
                if      (sh.name == "RVA/Aurora")           AuroraShader = sh;
                else if (sh.name == "RVA/AuroraBlit")       BlitShader   = sh;
                // anything else in the bundle (old grab shaders from a project that still has them) is ignored
            }

            if (AuroraShader == null)
            {
                LogError("Bundle contains no 'RVA/Aurora' shader — plugin disabled (an old bundle still names it Custom/Aurora: rebuild it)");
                bundle.Unload(false);
                yield break;
            }
            if (BlitShader == null)
                LogWarn("No 'RVA/AuroraBlit' shader in bundle — the aurora will render at FULL resolution without tonemapping. " +
                        "Make sure AuroraBlit.shader AND its .meta are in the Unity project and rebuild the bundle.");

            ShaderLoaded = true;
            Log("Shaders ready");
            // Bundle is intentionally never unloaded — the shaders must stay alive.
        }

        static void Log(string m)      => Debug.Log($"{LogPrefix} {m}");
        static void LogWarn(string m)  => Debug.LogWarning($"{LogPrefix} {m}");
        static void LogError(string m) => Debug.LogError($"{LogPrefix} {m}");
    }


    /// <summary>
    /// Runs every time the Flight scene loads: builds the aurora for the
    /// target body, applies the settings, opens the live-tuning window on
    /// Modifier + toggleKey, and tears everything down when the scene ends.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RVAFlightSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Flight]";

        const string TargetBodyName = "Kerbin";   // per-planet configs come later
        const float  UnityTestRadius = 10f;       // the Unity test planet's radius; worldScale = radius / this

        public AuroraSettings       Settings   => RVABootstrap.Settings;
        public AuroraRenderer       Renderer   => renderer;
        public AuroraStormScheduler Storm      => storm;
        public AuroraMeanderTexture Meander    => meander;
        public ScaledAuroraDriver   Scaled     => scaled;
        public AuroraDiagnostics    Diag       => diag;
        public float                Radius     => radius;
        public float                WorldScale => worldScale;
        public bool                 Built      => auroraGO != null;

        GameObject           auroraGO;
        Material             material;
        CelestialBody        body;
        AuroraRenderer       renderer;
        AuroraStormScheduler storm;
        AuroraMeanderTexture meander;
        AuroraTuningWindow   window;
        ScaledAuroraDriver   scaled;
        AuroraDiagnostics    diag;
        float radius, worldScale;

        IEnumerator Start()
        {
            Log("Flight scene Start — waiting for shaders and bodies");

            int waitFrames = 0;
            while (!RVABootstrap.ShaderLoaded)
            {
                if (++waitFrames > 600) { LogError("Shaders never loaded — aborting"); yield break; }
                yield return null;
            }
            while (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                yield return null;

            CelestialBody body = null;
            foreach (var b in FlightGlobals.Bodies)
                if (b != null && b.bodyName == TargetBodyName) { body = b; break; }
            if (body == null) { LogError($"Body '{TargetBodyName}' not found"); yield break; }

            Log($"Found {body.bodyName}: radius={body.Radius}m, transform={body.transform.name}");
            this.body = body;
            BuildAurora(body);
            Log("Flight setup complete");
        }

        void BuildAurora(CelestialBody body)
        {
            var s = Settings;
            radius     = (float)body.Radius;
            worldScale = radius / UnityTestRadius;             // 60000 for Kerbin

            var stale = body.transform.Find($"RVA_{body.bodyName}");
            if (stale != null) { Log("  Removing stale aurora object"); Destroy(stale.gameObject); }

            // Root object under the LOCAL-SPACE body (real meters). Its children
            // give the storm scheduler the planet center each frame.
            auroraGO = new GameObject($"RVA_{body.bodyName}");
            auroraGO.transform.SetParent(body.transform, false);
            auroraGO.transform.localPosition = Vector3.zero;
            auroraGO.transform.localRotation = Quaternion.identity;
            auroraGO.transform.localScale    = Vector3.one;

            // Material — fixed per-planet values here, everything tunable in ApplySettings()
            material = new Material(RVABootstrap.AuroraShader);
            material.SetFloat("_SphereRadius", radius);
            material.SetFloat("_AltBase",      radius * s.altBaseFraction);
            material.SetFloat("_AltTop",       radius * (s.altBaseFraction + s.altTopFraction));   // scheduler keeps this updated
            material.SetFloat("_Activity",     s.baseActivity);
            material.SetFloat("_AnimSpeed",    s.baseAnimSpeed);
            material.SetFloat("_AnimTime",     0f);
            material.SetVector("_PlanetCenter", body.transform.position);
            Log("  Material created");

            // Meander / flare lookup texture (CPU-baked, feeds _MeanderTex)
            meander = auroraGO.AddComponent<AuroraMeanderTexture>();
            meander.auroraMaterial = material;
            meander.resolution     = s.meanderResolution;
            Log($"  AuroraMeanderTexture attached ({s.meanderResolution} longitudes × 6 rows)");

            // Renderer: low-res command-buffer render + blit composite
            renderer = auroraGO.AddComponent<AuroraRenderer>();
            renderer.auroraMaterial   = material;
            renderer.blitShader       = RVABootstrap.BlitShader;
            Log("  AuroraRenderer attached");

            // Storm scheduler — Unity-scale numbers × worldScale, seeded per save
            storm = auroraGO.AddComponent<AuroraStormScheduler>();
            storm.auroraMaterial = material;
            storm.worldScale     = worldScale;
            int seed = (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Seed : 0)
                     ^ body.bodyName.GetHashCode();
            storm.Seed(seed);
            Log($"  AuroraStormScheduler attached (worldScale={worldScale}, seed={seed})");

            // Map view: a second renderer around the SCALED-SPACE body on "Camera
            // ScaledSpace", following this material frame by frame (v3.17)
            scaled = auroraGO.AddComponent<ScaledAuroraDriver>();
            scaled.Build(body, material, null, s);
            Log("  Scaled-space (map view) aurora attached");

            // Diagnostics: logs hitches, GC runs, animation / material jumps, origin
            // shifts and LUT stalls to KSP.log, once each, with timestamps (v3.19b)
            diag = auroraGO.AddComponent<AuroraDiagnostics>();
            diag.auroraMaterial = material;
            diag.planet         = body.transform;
            diag.storm          = storm;
            diag.meander        = meander;
            Log("  Diagnostics attached");

            ApplySettings();

            // Live tuning window (Modifier + toggleKey)
            window = auroraGO.AddComponent<AuroraTuningWindow>();
            window.owner = this;
            Log($"  Tuning window ready — press {s.toggleKey} with the modifier key (Alt) held");
        }

        /// <summary>Every per-look value in Settings → a material. radiusUnits = the planet radius in
        /// the material's units (metres for the local aurora, scaled-space units for the map view's),
        /// unitsPerMetre converts the km settings (cloud top).</summary>
        public static void PushMaterial(AuroraSettings s, Material material, float radiusUnits, float unitsPerMetre)
        {
            material.SetFloat("_AltBase",        radiusUnits * s.altBaseFraction);
            material.SetFloat("_NoiseScale",     s.noiseScale);
            material.SetFloat("_LatCenter",      s.latCenter);
            material.SetFloat("_LatWidth",       s.latWidth);
            material.SetFloat("_WidthVariance",  s.widthVariance);
            material.SetFloat("_HeightVariance", s.heightVariance);
            material.SetFloat("_Spikiness",      s.spikiness);
            material.SetFloat("_SpikeThreshold", s.spikeThreshold);
            material.SetFloat("_GreenSpikes",    s.greenSpikes);
            material.SetFloat("_CloudLightBlur", 0f);             // v3.23: the shader's cloud-light / irradiance terms stay off
            material.SetFloat("_CloudTopAlt",    s.cloudTopKm * 1000f * unitsPerMetre);
            material.SetFloat("_StepLength",     s.stepLength);
            material.SetFloat("_StepNear",       Mathf.Clamp(s.nearStepScale, 0.1f, 1f));
            material.SetFloat("_MaxSteps",       s.maxSteps);
            material.SetFloat("_Jitter",         s.jitter);
            material.SetFloat("_DaylightFade",   s.daylightFade);
            material.SetFloat("_Brightness",     s.brightness);
            material.SetFloat("_GreenTop",       s.greenTop);
            material.SetFloat("_BlueBottom",     s.blueBottom);
            material.SetFloat("_BlueTop",        s.blueTop);
            material.SetFloat("_PinkBottom",     s.pinkBottom);
            material.SetFloat("_GainLow",        s.gainLow);
            material.SetFloat("_GainMid",        s.gainMid);
            material.SetFloat("_GainHigh",       s.gainHigh);
            material.SetFloat("_CloudLight",     0f);             // v3.23: no cloud light — the aurora lights nothing
            material.SetFloat("_HighAltBoost",   s.highAltBoost);
            material.SetFloat("_CompressFactor", s.compressFactor);
            material.SetFloat("_RayThreshold",   s.rayThreshold);
            material.SetFloat("_RayGain",        s.rayGain);
            material.SetFloat("_RayTaper",       Mathf.Clamp01(s.rayTaper));
            material.SetFloat("_RayZone",        Mathf.Clamp(s.rayZone, -1f, 1f));
            material.SetFloat("_RayScale",       s.rayScale);
            material.SetFloat("_EdgeSharpness",  s.edgeSharpness);
            material.SetFloat("_TopSpread",      s.topSpread);
            material.SetFloat("_BottomSoftness", s.bottomSoftness);
            material.SetFloat("_FoldStrength",   s.foldStrength);
            material.SetFloat("_FoldScale",      s.foldScale);
            material.SetFloat("_BandSpacing",    s.bandSpacing);
            material.SetFloat("_BandIndependence", s.bandIndependence);
            material.SetFloat("_DetailStrength", s.detailStrength);
            material.SetFloat("_DebugMode",      s.debugMode);
            material.SetColor("_ColorLow",  s.colorLow);
            material.SetColor("_ColorMid",  s.colorMid);
            material.SetColor("_ColorHigh", s.colorHigh);
        }

        /// <summary>The storm scheduler's base / peak values from Settings (unit-free: it multiplies by its worldScale).</summary>
        public static void ConfigureStorm(AuroraStormScheduler storm, AuroraSettings s)
        {
            storm.baseMatAltTop        = (s.altBaseFraction + s.altTopFraction) * UnityTestRadius;                       // top = lift + height
            storm.stormMatAltTop       = (s.altBaseFraction + s.altTopFraction * s.stormHeightGain) * UnityTestRadius;   // storms grow the height
            storm.baseMatActivity      = s.baseActivity;
            storm.stormMatActivity     = s.stormActivity;
            storm.baseMatAnimSpeed     = s.baseAnimSpeed;
            storm.stormMatAnimSpeed    = s.stormAnimSpeed;
            storm.averageQuietDuration = s.averageQuietDuration;
            // the oval through the storm (v3.15): the scheduler drives the material's
            // centre / spacing / width and the outer arcs' visibility every frame
            storm.driveOval          = true;
            storm.baseLatCenter      = s.latCenter;
            storm.stormLatCenter     = Mathf.Clamp(s.latCenter - s.ovalEquatorward, 0.05f, 0.98f);
            storm.baseBandSpacing    = s.bandSpacing * Mathf.Max(s.ovalQuietSpread, 0f);
            storm.stormBandSpacing   = s.bandSpacing * Mathf.Max(s.ovalStormSpread, 0f);
            storm.baseLatWidth       = s.latWidth;
            storm.stormLatWidth      = s.latWidth * (1f + Mathf.Max(s.ovalStormWiden, 0f));
            storm.arc2Threshold      = Mathf.Clamp01(s.arc2Appears);
            storm.arc3Threshold      = Mathf.Clamp01(s.arc3Appears);
        }

        /// <summary>Push every value in Settings to the material / components. Cheap; call on any change.</summary>
        public void ApplySettings()
        {
            var s = Settings;
            if (material == null) return;

            if (diag != null) { diag.active = s.diagnostics; diag.NoteApplySettings(); }
            PushMaterial(s, material, radius, 1f);

            if (renderer != null)
            {
                renderer.renderScale    = Mathf.Clamp(s.renderScale, 0.1f, 1f);
                renderer.exposure       = s.exposure;
                renderer.flipY          = s.flipY;
                renderer.compositeEvent = s.compositeEvent;
                renderer.cloudOcclusion = s.cloudOcclusion;
                renderer.cloudBufferFormat = s.cloudBufferFormat;
                renderer.bufferFlipY     = s.cloudBufferFlipY;
                renderer.sceneDepthOcclusion = s.sceneDepthOcclusion;
                renderer.smoothing      = Mathf.Clamp01(s.upsampleSmoothing);
                renderer.edgeAware      = s.edgeAwareUpsample;
            }

            if (meander != null)
            {
                meander.resolution = Mathf.Clamp(s.meanderResolution, 32, 1024);
                meander.threaded   = s.meanderThreaded;
            }

            if (storm != null) ConfigureStorm(storm, s);

            if (scaled != null) scaled.Apply(s);
        }

        void LateUpdate()
        {
            if (material == null || body == null) return;
            var s = Settings;

            Transform camT = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null
                           ? FlightCamera.fetch.mainCamera.transform : null;
            if (diag != null) diag.cam = camT;
            Vector3 camRel = camT != null ? camT.position - body.transform.position : Vector3.up * (radius + 1f);
            float   camAlt = camRel.magnitude - radius;

            // Composite order vs. the clouds: above the cloud tops the aurora
            // should draw over them; below, the clouds should hide it. Which
            // camera events do that depends on where EVE composites, so both
            // are settings (only needed without the per-pixel occlusion).
            if (renderer != null)
            {
                int wanted = s.compositeEvent;
                if (s.autoAboveClouds && camT != null && camAlt > s.cloudTopKm * 1000f)
                    wanted = s.compositeEventAbove;
                if (renderer.compositeEvent != wanted) renderer.compositeEvent = wanted;
            }

            // Map view: the local cameras are off, the scaled-space camera draws the
            // planet — hand the aurora to the scaled instance for as long as that lasts
            if (scaled != null) scaled.SetActive(s.mapView && MapView.MapIsEnabled);

            // Sun direction for the daylight fade and the night-only irradiance
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 sunDir = Vector3.zero;
            bool haveSun = sun != null && sun != body;
            if (haveSun)
            {
                sunDir = (sun.transform.position - body.transform.position).normalized;
                material.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 1f));
            }
            else material.SetVector("_SunDir", Vector4.zero);
        }

        void OnDestroy()
        {
            if (scaled != null) scaled.Shutdown();
            if (auroraGO != null) Destroy(auroraGO);
            if (material != null) Destroy(material);
        }

        static void Log(string m)      => Debug.Log($"{LogPrefix} {m}");
        static void LogError(string m) => Debug.LogError($"{LogPrefix} {m}");
    }

    /// <summary>
    /// The aurora around the SCALED-SPACE body — what the map view and the
    /// tracking station look at — on "Camera ScaledSpace". A second
    /// AuroraRenderer with a second material: in flight it copies the local
    /// material every frame (same storm, animation time, sun and sliders) and
    /// rescales the world-unit values by 1 / ScaledSpace.ScaleFactor; in the
    /// tracking station the material is driven directly. No EVE occlusion (there
    /// is no volumetric cloud buffer in scaled space) and no depth occlusion (the
    /// analytic planet hides the far side).
    /// </summary>
    public class ScaledAuroraDriver : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Scaled]";
        public const string ScaledCameraName = "Camera ScaledSpace";

        public AuroraRenderer Renderer       => scaledRenderer;
        public Material       ScaledMaterial => scaledMaterial;
        public string Status => scaledRenderer == null ? "not built"
                              : !scaledRenderer.renderingEnabled ? "idle (not in the map view)"
                              : scaledRenderer.Status;

        GameObject     go;
        Material       scaledMaterial;
        AuroraRenderer scaledRenderer;
        bool           ownsMaterial;

        /// <param name="source">the local-space material to follow each frame (flight), or null when
        /// <paramref name="own"/> is driven directly (tracking station)</param>
        public void Build(CelestialBody body, Material source, Material own, AuroraSettings s)
        {
            Transform parent = body.scaledBody != null ? body.scaledBody.transform : null;
            if (parent == null) { Log($"{body.bodyName} has no scaled body — map view aurora disabled"); return; }
            if (RVABootstrap.AuroraShader == null) return;

            var stale = parent.Find($"RVA_scaled_{body.bodyName}");
            if (stale != null) Destroy(stale.gameObject);

            go = new GameObject($"RVA_scaled_{body.bodyName}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            ownsMaterial   = own == null;
            scaledMaterial = own ?? new Material(RVABootstrap.AuroraShader);

            scaledRenderer = go.AddComponent<AuroraRenderer>();
            scaledRenderer.auroraMaterial      = scaledMaterial;
            scaledRenderer.blitShader          = RVABootstrap.BlitShader;
            scaledRenderer.cameraName          = ScaledCameraName;
            scaledRenderer.syncFrom            = source;
            scaledRenderer.syncScale           = 1f / ScaledSpace.ScaleFactor;
            scaledRenderer.cloudOcclusion      = false;
            scaledRenderer.sceneDepthOcclusion = false;
            scaledRenderer.edgeAware           = false;
            scaledRenderer.renderingEnabled    = false;
            Apply(s);
            Log($"built around {body.bodyName}'s scaled body (scale 1/{ScaledSpace.ScaleFactor:F0}, camera '{ScaledCameraName}')");
        }

        public void Apply(AuroraSettings s)
        {
            if (scaledRenderer == null) return;
            scaledRenderer.renderScale    = Mathf.Clamp(s.mapRenderScale, 0.1f, 1f);
            scaledRenderer.exposure       = s.exposure;
            scaledRenderer.flipY          = s.flipY;
            scaledRenderer.compositeEvent = s.mapCompositeEvent;
            scaledRenderer.smoothing      = Mathf.Clamp01(s.upsampleSmoothing);
        }

        public void SetActive(bool on)
        {
            if (scaledRenderer != null && scaledRenderer.renderingEnabled != on) scaledRenderer.renderingEnabled = on;
        }

        /// <summary>Detach from the camera and drop the scaled object now (before whoever owns the material destroys it).</summary>
        public void Shutdown()
        {
            if (scaledRenderer != null) { scaledRenderer.renderingEnabled = false; scaledRenderer.syncFrom = null; }
            if (go != null) { Destroy(go); go = null; }
            scaledRenderer = null;
            if (ownsMaterial && scaledMaterial != null) { Destroy(scaledMaterial); scaledMaterial = null; }
        }

        void OnDestroy() { Shutdown(); }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }


    /// <summary>
    /// Tracking station: no local aurora to follow, so this builds a scaled-space
    /// material of its own (meander texture, storm scheduler, sun direction) and
    /// hands it to a ScaledAuroraDriver. Same settings file, no tuning window.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class RVATrackingSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Tracking]";
        const string TargetBodyName = "Kerbin";

        CelestialBody        body;
        Material             material;
        AuroraMeanderTexture meander;
        AuroraStormScheduler storm;
        ScaledAuroraDriver   scaled;
        float                radiusUnits;

        IEnumerator Start()
        {
            var s = RVABootstrap.Settings;
            if (!s.mapView) yield break;

            int waitFrames = 0;
            while (!RVABootstrap.ShaderLoaded)
            {
                if (++waitFrames > 600) { Log("Shaders never loaded — aborting"); yield break; }
                yield return null;
            }
            while (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                yield return null;

            foreach (var b in FlightGlobals.Bodies)
                if (b != null && b.bodyName == TargetBodyName) { body = b; break; }
            if (body == null || body.scaledBody == null) { Log($"Body '{TargetBodyName}' (scaled) not found"); yield break; }

            float unitsPerMetre = 1f / ScaledSpace.ScaleFactor;
            radiusUnits = (float)body.Radius * unitsPerMetre;

            material = new Material(RVABootstrap.AuroraShader);
            material.SetFloat("_SphereRadius", radiusUnits);
            material.SetFloat("_AltBase",      radiusUnits * s.altBaseFraction);
            material.SetFloat("_AltTop",       radiusUnits * (s.altBaseFraction + s.altTopFraction));
            material.SetFloat("_Activity",     s.baseActivity);
            material.SetFloat("_AnimSpeed",    s.baseAnimSpeed);
            material.SetFloat("_AnimTime",     0f);
            material.SetVector("_PlanetCenter", body.scaledBody.transform.position);
            RVAFlightSetup.PushMaterial(s, material, radiusUnits, unitsPerMetre);

            meander = gameObject.AddComponent<AuroraMeanderTexture>();
            meander.auroraMaterial = material;
            meander.resolution     = Mathf.Clamp(s.meanderResolution, 32, 1024);
            meander.threaded       = s.meanderThreaded;

            storm = gameObject.AddComponent<AuroraStormScheduler>();
            storm.auroraMaterial = material;
            storm.worldScale     = radiusUnits / 10f;
            int seed = (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Seed : 0) ^ body.bodyName.GetHashCode();
            storm.Seed(seed);
            RVAFlightSetup.ConfigureStorm(storm, s);

            scaled = gameObject.AddComponent<ScaledAuroraDriver>();
            scaled.Build(body, null, material, s);
            scaled.SetActive(true);
            Log($"Tracking station aurora ready ({body.bodyName}, radius {radiusUnits:F1} scaled units)");
        }

        void LateUpdate()
        {
            if (material == null || body == null) return;
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun != null && sun != body && sun.scaledBody != null)
            {
                Vector3 d = (sun.scaledBody.transform.position - body.scaledBody.transform.position).normalized;
                material.SetVector("_SunDir", new Vector4(d.x, d.y, d.z, 1f));
            }
            else material.SetVector("_SunDir", Vector4.zero);
        }

        void OnDestroy()
        {
            // the renderer first (it references the material in its command buffers), then the material
            if (scaled != null) scaled.Shutdown();
            if (material != null) Destroy(material);
        }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
