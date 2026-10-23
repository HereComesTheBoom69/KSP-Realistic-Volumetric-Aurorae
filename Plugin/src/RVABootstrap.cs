// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[assembly: KSPAssembly("RealisticVolumetricAurorae", 1, 1)]

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
        /// <summary>The shader build this plugin was written against: _RVAShaderBuild in Aurora_Shader.shader.
        /// Bumped together with it whenever a uniform is added or renamed (1.1.0).</summary>
        public const int ShaderBuild = 1110;
        /// <summary>The build tag the loaded bundle reports (0 = a bundle from before the tag, -1 = unreadable).</summary>
        public static int ShaderBuildFound { get; private set; }
        public static bool ShaderBuildMatches => ShaderBuildFound == ShaderBuild;
        public static Shader BlitShader   { get; private set; }
        public static bool   ShaderLoaded { get; private set; }
        /// <summary>The GLOBAL settings (hotkey, render scale, map view …): identical on every
        /// body's AuroraSettings; per-body values come from AuroraConfigs.Get(body).</summary>
        public static AuroraSettings Settings => AuroraConfigs.Global;

        /// <summary>Kit version — logged at startup and shown in the window title,
        /// so it is always obvious which DLL KSP actually loaded.</summary>
        public const string Version = "1.1.0";

        const string LogPrefix = "[RVA]";
        const string ModRelativePath    = "GameData/RealisticVolumetricAurorae";
        const string BundleFileName     = "rva_shaders";
        const string BundleRelativePath = ModRelativePath + "/Bundles/" + BundleFileName;

        void Awake()
        {
            Log($"Realistic Volumetric Aurorae (RVA) v{Version}{(AuroraSettings.DevBuild ? " (dev build: every setting exposed)" : " (release build)")} — bootstrap awake, loading settings and asset bundle");
            DontDestroyOnLoad(gameObject);

            bool hadFile = File.Exists(AuroraSettings.FilePath);
            AuroraConfigs.Load(out string report);
            Log("Settings: " + report);
            Log("Bodies:\n" + AuroraConfigs.Describe());
            if (!hadFile)
            {
                // First launch (or a CKAN install, which ships no settings.cfg so that
                // upgrades never overwrite the user's): write the defaults so there is a
                // file to edit. Updates leave it alone from here on.
                try { AuroraConfigs.Save(); Log($"Settings: wrote the default settings file to {AuroraSettings.FilePath}"); }
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
                // 1.0.1: the 1.0.0 zip shipped the bundle one folder too deep
                // (Bundles/rva_shaders/rva_shaders). Tolerate any layout that keeps the
                // file name, so a packaging slip or a hand-made install still works.
                string found = FindBundleFallback();
                if (found == null)
                {
                    LogError($"Bundle not found at {bundlePath} — plugin disabled");
                    yield return null;                       // let the main-menu UI finish coming up
                    ShowMissingBundleDialog(bundlePath);
                    yield break;
                }
                LogWarn($"Bundle not at its expected path; using {found} instead (the install layout is off — the file should sit directly in Bundles/)");
                bundlePath = found;
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

            ShaderBuildFound = ReadShaderBuild(AuroraShader);
            if (ShaderBuildMatches) Log($"Shader build {ShaderBuildFound} — matches the plugin");
            else LogWarn($"SHADER / PLUGIN MISMATCH: the bundle reports shader build {ShaderBuildFound}, this DLL expects {ShaderBuild}. " +
                         "They were built from different sources — rebuild the bundle (Unity) and the DLL from the same kit. " +
                         "Until then features added on either side are inert (settings the shader does not know, uniforms the plugin does not set).");

            ShaderLoaded = true;
            Log("Shaders ready");
            // Bundle is intentionally never unloaded — the shaders must stay alive.
        }

        /// <summary>Looks for the bundle anywhere under the mod folder when it is not at
        /// the expected path. Returns the full path, or null.</summary>
        static string FindBundleFallback()
        {
            try
            {
                string modDir = Path.Combine(KSPUtil.ApplicationRootPath, ModRelativePath);
                if (!Directory.Exists(modDir)) return null;
                string[] hits = Directory.GetFiles(modDir, BundleFileName, SearchOption.AllDirectories);
                foreach (string h in hits)
                    if (string.Equals(Path.GetFileName(h), BundleFileName, System.StringComparison.OrdinalIgnoreCase) && new FileInfo(h).Length > 0)
                        return h;
            }
            catch (System.Exception e) { LogWarn($"Bundle search failed ({e.GetType().Name}: {e.Message})"); }
            return null;
        }

        /// <summary>Tells the player, in game, why there is no aurora — a missing file is
        /// an install problem they can fix themselves, if they know about it.</summary>
        static void ShowMissingBundleDialog(string expectedPath)
        {
            try
            {
                string msg = "Realistic Volumetric Aurorae could not find its shader file and has disabled itself.\n\n" +
                             "Missing: GameData/RealisticVolumetricAurorae/Bundles/rva_shaders\n\n" +
                             "Re-extract the download and copy the whole RealisticVolumetricAurorae folder into GameData. " +
                             "The file rva_shaders (no extension) must sit directly inside the Bundles folder.";
                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    "RVA_MissingBundle", "Realistic Volumetric Aurorae", msg, "OK", true, HighLogic.UISkin);
            }
            catch (System.Exception e) { LogWarn($"Could not show the missing-bundle dialog ({e.GetType().Name}: {e.Message})"); }
        }

        static int ReadShaderBuild(Shader sh)
        {
            if (sh == null) return -1;
            try
            {
                var m = new Material(sh);
                int v = m.HasProperty("_RVAShaderBuild") ? Mathf.RoundToInt(m.GetFloat("_RVAShaderBuild")) : 0;
                UnityEngine.Object.Destroy(m);
                return v;
            }
            catch { return -1; }
        }

        static void Log(string m)      => Debug.Log($"{LogPrefix} {m}");
        static void LogWarn(string m)  => Debug.LogWarning($"{LogPrefix} {m}");
        static void LogError(string m) => Debug.LogError($"{LogPrefix} {m}");
    }


    /// <summary>
    /// Runs every time the Flight scene loads: builds the local-space aurora of the
    /// body the vessel is at (rebuilt on SOI change / vessel switch), a self-driven
    /// scaled-space aurora for every other configured body (1.1.0: Jool's aurora from
    /// Laythe, the map view zoomed to another planet), applies the settings, opens the
    /// live-tuning window on Modifier + toggleKey, and tears everything down when the
    /// scene ends.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RVAFlightSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Flight]";
        const float  UnityTestRadius = 10f;       // the Unity test planet's radius; worldScale = radius / this

        /// <summary>Settings of the body the LOCAL aurora is built around (the global settings when there is none).</summary>
        public AuroraSettings       Settings   => body != null && AuroraConfigs.Has(body.bodyName) ? AuroraConfigs.Get(body.bodyName) : AuroraConfigs.Global;
        public string               BodyName   => body != null ? body.bodyName : null;
        public CelestialBody        Body       => body;
        public AuroraRenderer       Renderer   => renderer;
        public AuroraStormScheduler Storm      => storm;
        public AuroraMeanderTexture Meander    => meander;
        public ScaledAuroraDriver   Scaled     => scaled;
        public AuroraDiagnostics    Diag       => diag;
        public AuroraVesselLight    VesselLight => vesselLight;
        public float                Radius     => radius;
        public float                WorldScale => worldScale;
        public bool                 Built      => auroraGO != null;
        /// <summary>The self-driven scaled-space aurorae of the other bodies, by body name.</summary>
        public IEnumerable<ScaledAuroraInstance> Distant => distant.Values;
        public ScaledAuroraInstance DistantFor(string b) => b != null && distant.TryGetValue(b, out var d) ? d : null;

        GameObject           auroraGO;
        Material             material;
        CelestialBody        body;
        AuroraRenderer       renderer;
        AuroraStormScheduler storm;
        AuroraMeanderTexture meander;
        AuroraTuningWindow   window;
        ScaledAuroraDriver   scaled;
        AuroraDiagnostics    diag;
        AuroraVesselLight    vesselLight;
        float radius, worldScale;

        readonly Dictionary<string, ScaledAuroraInstance> distant = new Dictionary<string, ScaledAuroraInstance>(System.StringComparer.OrdinalIgnoreCase);
        readonly List<GameObject> distantObjects = new List<GameObject>();
        bool          ready, subscribed;
        CelestialBody pendingBody;
        int           bodyPoll;

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
            waitFrames = 0;
            while (CurrentMainBody() == null && ++waitFrames < 600)
                yield return null;

            ready = true;
            Rebuild(CurrentMainBody());

            GameEvents.onVesselSOIChanged.Add(OnSOIChanged);
            GameEvents.onVesselChange.Add(OnVesselChange);
            subscribed = true;

            // Live tuning window (Modifier + toggleKey) — on this object, so it outlives body changes
            window = gameObject.AddComponent<AuroraTuningWindow>();
            window.owner = this;
            Log($"Flight setup complete — tuning window ready, press {AuroraConfigs.Global.toggleKey} with the modifier key (Alt) held");
        }

        static CelestialBody CurrentMainBody()
        {
            var v = FlightGlobals.ActiveVessel;
            if (v != null && v.mainBody != null) return v.mainBody;
            return FlightGlobals.currentMainBody;
        }

        static CelestialBody FindBody(string name)
        {
            if (FlightGlobals.Bodies == null) return null;
            foreach (var b in FlightGlobals.Bodies)
                if (b != null && string.Equals(b.bodyName, name, System.StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        void OnSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> a)
        {
            if (a.host != null && a.host == FlightGlobals.ActiveVessel) RequestRebuild(a.to);
        }
        void OnVesselChange(Vessel v) { if (v != null) RequestRebuild(v.mainBody); }
        void RequestRebuild(CelestialBody b) { if (b != null && b != body) pendingBody = b; }

        /// <summary>Tear everything down and build for a new main body: the local aurora for it (if
        /// configured) and a scaled-space instance for every other configured body.</summary>
        void Rebuild(CelestialBody newBody)
        {
            TearDownLocal();
            TearDownDistant();
            body = newBody;
            if (body == null) { Log("No main body yet — nothing built"); return; }

            if (AuroraConfigs.Has(body.bodyName))
            {
                Log($"Main body {body.bodyName}: radius={body.Radius}m — building the local aurora");
                BuildAurora(body);
            }
            else Log($"Main body {body.bodyName}: no RVA_AURORA config — no local aurora");

            BuildDistant();
        }

        void BuildDistant()
        {
            if (distant.Count > 0) return;                   // already built (ApplySettings builds them too)
            if (!AuroraConfigs.Global.mapView) return;      // the scaled-space renderers are one switch (map view / tracking station / other bodies)
            foreach (var name in AuroraConfigs.Bodies)
            {
                if (body != null && string.Equals(name, body.bodyName, System.StringComparison.OrdinalIgnoreCase)) continue;
                var cb = FindBody(name);
                if (cb == null) { Log($"  {name}: configured but not in this solar system — skipped"); continue; }
                if (cb.scaledBody == null) continue;
                var go = new GameObject($"RVA_distant_{name}");     // root-level: its own storm / LUT; destroyed with the scene or on rebuild
                var inst = go.AddComponent<ScaledAuroraInstance>();
                inst.occluder = body;                                  // the body we are at hides it
                inst.Build(cb, AuroraConfigs.Get(name), withLocal: true);
                distant[name] = inst;
                distantObjects.Add(go);
            }
        }

        void TearDownDistant()
        {
            foreach (var inst in distant.Values) if (inst != null) inst.Shutdown();
            foreach (var go in distantObjects) if (go != null) Destroy(go);
            distant.Clear();
            distantObjects.Clear();
        }

        void TearDownLocal()
        {
            if (scaled != null) { scaled.Shutdown(); scaled = null; }
            if (auroraGO != null) { Destroy(auroraGO); auroraGO = null; }
            if (material != null) { Destroy(material); material = null; }
            renderer = null; storm = null; meander = null; diag = null; vesselLight = null;
        }

        void BuildAurora(CelestialBody body)
        {
            var s = AuroraConfigs.Get(body.bodyName);
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

            // Storm scheduler — Unity-scale numbers × worldScale, seeded per save and body
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

            // The aurora's light on the vessel (1.1.0): up to four lights on the vessel's layers,
            // from the directions the ribbons' light comes from. Off until the settings switch it on.
            vesselLight = auroraGO.AddComponent<AuroraVesselLight>();
            vesselLight.planet         = body.transform;
            vesselLight.auroraMaterial = material;
            vesselLight.meander        = meander;
            vesselLight.planetRadius   = radius;
            Log("  AuroraVesselLight attached");

            // Diagnostics: logs hitches, GC runs, animation / material jumps, origin
            // shifts and LUT stalls to KSP.log, once each, with timestamps (v3.19b)
            diag = auroraGO.AddComponent<AuroraDiagnostics>();
            diag.auroraMaterial = material;
            diag.planet         = body.transform;
            diag.storm          = storm;
            diag.meander        = meander;
            Log("  Diagnostics attached");

            ApplySettings();
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
            var bn = AuroraBlueNoise.Texture;                                                 // 1.1.0: blue-noise sample offsets
            if (bn != null) material.SetTexture("_BlueNoise", bn);
            material.SetFloat("_BlueNoiseOn",    bn != null ? 1f : 0f);
            material.SetFloat("_CloudLightBlur", 0f);             // v3.23: the shader's cloud-light / irradiance terms stay off
            material.SetFloat("_CloudTopAlt",    s.cloudTopKm * 1000f * unitsPerMetre);
            material.SetFloat("_StepLength",     s.stepLength);
            material.SetFloat("_StepNear",       Mathf.Clamp(s.nearStepScale, 0.05f, 1f));
            material.SetFloat("_StepGrowth",     Mathf.Clamp(s.stepGrowth, 0f, 0.2f));
            material.SetFloat("_StepFarMul",     Mathf.Clamp(s.stepFarMul, 1f, 16f));
            material.SetFloat("_MaxSteps",       Mathf.Clamp(s.maxSteps, 8f, 512f));
            material.SetFloat("_Jitter",         s.jitter);
            material.SetFloat("_StepFilter",     Mathf.Clamp(s.stepFilter, 0.2f, 1.5f));   // 1.1.0
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

        /// <summary>1.1.0: the per-body performance options → a renderer (any of a body's three).</summary>
        public static void PushRendererPerf(AuroraRenderer r, AuroraSettings s)
        {
            if (r == null) return;
            r.temporal        = s.temporalAccumulation;
            r.temporalWeight  = Mathf.Clamp(s.temporalWeight, 0f, 0.99f);
            r.temporalUpscale = Mathf.Clamp(s.temporalUpscale, 1f, 4f);
            r.temporalClamp   = Mathf.Clamp(s.temporalClamp, 0.1f, 8f);
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
            storm.baseBandSpacing    = s.bandSpacing * Mathf.Max(s.ovalQuietSpread, 0f);
            if (s.stormToEquator)
            {
                // 1.1.0: at peak the arcs run from the quiet oval's poleward arc (which stays put) down to
                // stormReach. The shader places them at centre + 0.04 × spacing, centre and centre − 0.05 ×
                // spacing, so a span D needs spacing D / 0.09 with the centre 0.05 / 0.09 of the way up from
                // the bottom.
                float top   = Mathf.Clamp01(s.latCenter + 0.04f * storm.baseBandSpacing);
                float reach = Mathf.Clamp(s.stormReach, 0f, top);
                float span  = Mathf.Max(top - reach, 0.001f);
                storm.stormLatCenter   = reach + span * (0.05f / 0.09f);
                storm.stormBandSpacing = span / 0.09f;
            }
            else
            {
                storm.stormLatCenter   = Mathf.Clamp(s.latCenter - s.ovalEquatorward, 0.05f, 0.98f);
                storm.stormBandSpacing = s.bandSpacing * Mathf.Max(s.ovalStormSpread, 0f);
            }
            storm.baseFoldStrength   = s.foldStrength;
            storm.stormFoldStrength  = s.foldStrength * (s.stormToEquator ? Mathf.Max(s.stormFoldGain, 0f) : 1f);
            storm.baseLatWidth       = s.latWidth;
            storm.stormLatWidth      = s.latWidth * (1f + Mathf.Max(s.ovalStormWiden, 0f));
            storm.arc2Threshold      = Mathf.Clamp01(s.arc2Appears);
            storm.arc3Threshold      = Mathf.Clamp01(s.arc3Appears);
        }

        /// <summary>Push every value in Settings to the local aurora's material / components, and each
        /// other body's settings to its scaled instance. Cheap; call on any change.</summary>
        public void ApplySettings()
        {
            var g = AuroraConfigs.Global;

            // the scaled-space renderers follow the one switch at runtime
            if (ready)
            {
                if (g.mapView && distant.Count == 0) BuildDistant();
                else if (!g.mapView && distant.Count > 0) TearDownDistant();
            }
            foreach (var kv in distant) if (kv.Value != null) kv.Value.Apply(AuroraConfigs.Get(kv.Key));

            if (material == null) return;
            var s = Settings;

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
                PushRendererPerf(renderer, s);
            }

            if (meander != null)
            {
                meander.resolution = Mathf.Clamp(s.meanderResolution, 32, 1024);
                meander.threaded   = s.meanderThreaded;
            }

            if (storm != null) ConfigureStorm(storm, s);

            if (vesselLight != null)
            {
                vesselLight.lightOn    = s.vesselLight;
                vesselLight.intensity  = Mathf.Max(s.vesselLightIntensity, 0f);
                vesselLight.pixelLight = s.vesselLightPixel;
                vesselLight.lightCount = s.vesselLightCount >= 4 ? 4 : s.vesselLightCount >= 2 ? 2 : 1;
                vesselLight.samples    = Mathf.Clamp(s.vesselLightSamples, 16, 128);
                vesselLight.smoothing  = Mathf.Clamp(s.vesselLightSmoothing, 0f, 5f);
            }

            if (scaled != null) scaled.Apply(s);
        }

        void LateUpdate()
        {
            if (!ready) return;

            // body change requested by an event, or noticed by polling (belt and braces)
            if (pendingBody == null && ++bodyPoll >= 60)
            {
                bodyPoll = 0;
                var now = CurrentMainBody();
                if (now != null && now != body) pendingBody = now;
            }
            if (pendingBody != null)
            {
                var nb = pendingBody; pendingBody = null;
                Log($"Main body changed: {(body != null ? body.bodyName : "(none)")} → {nb.bodyName}");
                Rebuild(nb);
            }

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

            // Sun direction for the daylight fade and the night-only irradiance. w is the
            // fade's weight: 1 in sunlight, down to 0 while the body's parent eclipses the
            // sun (Laythe behind Jool, ~1 h per orbit) — then the aurora shows all over.
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            Vector3 sunDir = Vector3.zero;
            bool haveSun = sun != null && sun != body;
            if (haveSun)
            {
                sunDir = (sun.transform.position - body.transform.position).normalized;
                float cover = s.eclipses ? AuroraEclipse.Coverage(body, sun) : 0f;
                material.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 1f - cover));
                bool dark = cover > 0.5f;
                if (dark != eclipsed)
                {
                    eclipsed = dark;
                    Log($"{body.bodyName}: {(dark ? $"in {body.referenceBody.bodyName}'s shadow — daylight fade lifted" : "back in sunlight — daylight fade on")}");
                }
            }
            else material.SetVector("_SunDir", Vector4.zero);
        }
        bool eclipsed;

        void OnDestroy()
        {
            if (subscribed)
            {
                GameEvents.onVesselSOIChanged.Remove(OnSOIChanged);
                GameEvents.onVesselChange.Remove(OnVesselChange);
                subscribed = false;
            }
            TearDownDistant();
            TearDownLocal();
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
    /// is no volumetric cloud buffer in scaled space); depth occlusion against the
    /// scaled camera's depth, so another body in front hides the aurora (1.1.0).
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
            scaledRenderer.sceneDepthOcclusion = true;    // 1.1.0: another scaled body in front (Laythe before Jool) cuts the march
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
            RVAFlightSetup.PushRendererPerf(scaledRenderer, s);
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
    /// Tracking station: no local aurora anywhere, so every configured body gets a
    /// self-driven scaled-space aurora (ScaledAuroraInstance). Same settings, no window.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class RVATrackingSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Tracking]";

        readonly List<ScaledAuroraInstance> instances = new List<ScaledAuroraInstance>();
        readonly List<GameObject> objects = new List<GameObject>();

        IEnumerator Start()
        {
            if (!AuroraConfigs.Global.mapView) yield break;

            int waitFrames = 0;
            while (!RVABootstrap.ShaderLoaded)
            {
                if (++waitFrames > 600) { Log("Shaders never loaded — aborting"); yield break; }
                yield return null;
            }
            while (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                yield return null;

            foreach (var name in AuroraConfigs.Bodies)
            {
                CelestialBody body = null;
                foreach (var b in FlightGlobals.Bodies)
                    if (b != null && string.Equals(b.bodyName, name, System.StringComparison.OrdinalIgnoreCase)) { body = b; break; }
                if (body == null || body.scaledBody == null) { Log($"Body '{name}' (scaled) not found — skipped"); continue; }
                var go = new GameObject($"RVA_tracking_{name}");
                var inst = go.AddComponent<ScaledAuroraInstance>();
                inst.Build(body, AuroraConfigs.Get(name), withLocal: false);
                instances.Add(inst);
                objects.Add(go);
            }
            Log($"Tracking station: {instances.Count} aurora(e) ready");
        }

        void OnDestroy()
        {
            foreach (var i in instances) if (i != null) i.Shutdown();
            foreach (var go in objects) if (go != null) Destroy(go);
            instances.Clear(); objects.Clear();
        }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
