// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[assembly: KSPAssembly("RealisticVolumetricAurorae", 1, 1)]

namespace RVA
{

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RVABootstrap : MonoBehaviour
    {
        public static Shader AuroraShader { get; private set; }

        public const int ShaderBuild = 1132;

        public static int ShaderBuildFound { get; private set; }
        public static bool ShaderBuildMatches => ShaderBuildFound == ShaderBuild;
        public static Shader BlitShader   { get; private set; }
        public static bool   ShaderLoaded { get; private set; }

        public static AuroraSettings Settings => AuroraConfigs.Global;

        public const string Version = "1.2.2";

        const string LogPrefix = "[RVA]";
        const string ModRelativePath    = "GameData/RealisticVolumetricAurorae";
        const string BundleFileName     = "rva_shaders";
        const string BundleRelativePath = ModRelativePath + "/Bundles/" + BundleFileName;

        void Awake()
        {
            Log($"Realistic Volumetric Aurorae (RVA) v{Version}{(AuroraSettings.DevBuild ? " (dev build: every setting exposed)" : " (release build)")}: bootstrap awake, loading settings and asset bundle");
            DontDestroyOnLoad(gameObject);

            bool hadFile = File.Exists(AuroraSettings.FilePath);
            AuroraConfigs.Load(out string report);
            Log("Settings: " + report);
            Log("Bodies:\n" + AuroraConfigs.Describe());
            if (!hadFile)
            {

                try { AuroraConfigs.Save(); Log($"Settings: wrote the default settings file to {AuroraSettings.FilePath}"); }
                catch (System.Exception e) { LogWarn($"Settings: could not write {AuroraSettings.FilePath} ({e.GetType().Name}: {e.Message}), running on built-in defaults"); }
            }

            StartCoroutine(LoadShaderBundle());
        }

        IEnumerator LoadShaderBundle()
        {
            string bundlePath = Path.Combine(KSPUtil.ApplicationRootPath, BundleRelativePath);
            Log($"Bundle path: {bundlePath}");

            if (!File.Exists(bundlePath))
            {

                string found = FindBundleFallback();
                if (found == null)
                {
                    LogError($"Bundle not found at {bundlePath}, plugin disabled");
                    yield return null;
                    ShowMissingBundleDialog(bundlePath);
                    yield break;
                }
                LogWarn($"Bundle not at its expected path, using {found} (it should sit directly in Bundles/)");
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

            }

            if (AuroraShader == null)
            {
                LogError("Bundle has no 'RVA/Aurora' shader, plugin disabled (old bundle? rebuild it)");
                bundle.Unload(false);
                yield break;
            }
            if (BlitShader == null)
                LogWarn("No 'RVA/AuroraBlit' shader in the bundle, rendering at full res without tonemapping. Rebuild the bundle with AuroraBlit.shader and its .meta in the project.");

            ShaderBuildFound = ReadShaderBuild(AuroraShader);
            if (ShaderBuildMatches) Log($"Shader build {ShaderBuildFound} matches the plugin");
            else LogWarn($"SHADER / PLUGIN MISMATCH: bundle is shader build {ShaderBuildFound}, this DLL expects {ShaderBuild}. Rebuild the bundle and the DLL from the same kit.");

            ShaderLoaded = true;
            Log("Shaders ready");

        }

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

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RVAFlightSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Flight]";
        const float  UnityTestRadius = 10f;

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
            Log("Flight scene Start, waiting for shaders and bodies");

            int waitFrames = 0;
            while (!RVABootstrap.ShaderLoaded)
            {
                if (++waitFrames > 600) { LogError("Shaders never loaded, aborting"); yield break; }
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

            window = gameObject.AddComponent<AuroraTuningWindow>();
            window.owner = this;
            Log($"Flight setup done, Alt+{AuroraConfigs.Global.toggleKey} opens the tuning window");
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

        void Rebuild(CelestialBody newBody)
        {
            TearDownLocal();
            TearDownDistant();
            body = newBody;
            if (body == null) { Log("No main body yet, nothing built"); return; }

            if (AuroraConfigs.Has(body.bodyName))
            {
                Log($"Main body {body.bodyName}: radius={body.Radius}m, building the local aurora");
                BuildAurora(body);
            }
            else Log($"Main body {body.bodyName}: no RVA_AURORA config, no local aurora");

            BuildDistant();
        }

        void BuildDistant()
        {
            if (distant.Count > 0) return;
            if (!AuroraConfigs.Global.mapView) return;
            foreach (var name in AuroraConfigs.Bodies)
            {
                if (body != null && string.Equals(name, body.bodyName, System.StringComparison.OrdinalIgnoreCase)) continue;
                var cb = FindBody(name);
                if (cb == null) { Log($"  {name}: configured but not in this solar system, skipped"); continue; }
                if (cb.scaledBody == null) continue;
                var go = new GameObject($"RVA_distant_{name}");
                var inst = go.AddComponent<ScaledAuroraInstance>();
                inst.occluder = body;
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
            worldScale = radius / UnityTestRadius;

            var stale = body.transform.Find($"RVA_{body.bodyName}");
            if (stale != null) { Log("  Removing stale aurora object"); Destroy(stale.gameObject); }

            auroraGO = new GameObject($"RVA_{body.bodyName}");
            auroraGO.transform.SetParent(body.transform, false);
            auroraGO.transform.localPosition = Vector3.zero;
            auroraGO.transform.localRotation = Quaternion.identity;
            auroraGO.transform.localScale    = Vector3.one;

            material = new Material(RVABootstrap.AuroraShader);
            material.SetFloat("_SphereRadius", radius);
            material.SetFloat("_AltBase",      radius * s.altBaseFraction);
            material.SetFloat("_AltTop",       radius * (s.altBaseFraction + s.altTopFraction));
            material.SetFloat("_Activity",     s.baseActivity);
            material.SetFloat("_AnimSpeed",    s.baseAnimSpeed);
            material.SetFloat("_AnimTime",     0f);
            material.SetVector("_PlanetCenter", body.transform.position);
            Log("  Material created");

            meander = auroraGO.AddComponent<AuroraMeanderTexture>();
            meander.auroraMaterial = material;
            meander.resolution     = s.meanderResolution;
            Log($"  AuroraMeanderTexture attached ({s.meanderResolution} longitudes x 6 rows)");

            renderer = auroraGO.AddComponent<AuroraRenderer>();
            renderer.auroraMaterial   = material;
            renderer.planetBody       = body;
            renderer.blitShader       = RVABootstrap.BlitShader;
            Log("  AuroraRenderer attached");

            storm = auroraGO.AddComponent<AuroraStormScheduler>();
            storm.auroraMaterial = material;
            storm.worldScale     = worldScale;
            int seed = (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Seed : 0)
                     ^ body.bodyName.GetHashCode();
            storm.Seed(seed);
            Log($"  AuroraStormScheduler attached (worldScale={worldScale}, seed={seed})");

            scaled = auroraGO.AddComponent<ScaledAuroraDriver>();
            scaled.Build(body, material, null, s);
            Log("  Scaled-space (map view) aurora attached");

            vesselLight = auroraGO.AddComponent<AuroraVesselLight>();
            vesselLight.planet         = body.transform;
            vesselLight.auroraMaterial = material;
            vesselLight.meander        = meander;
            vesselLight.planetRadius   = radius;
            Log("  AuroraVesselLight attached");

            diag = auroraGO.AddComponent<AuroraDiagnostics>();
            diag.auroraMaterial = material;
            diag.planet         = body.transform;
            diag.storm          = storm;
            diag.meander        = meander;
            Log("  Diagnostics attached");
            Log("  " + AuroraCloudTops.Describe(s, body.bodyName));

            ApplySettings();
        }

        public static void PushMaterial(AuroraSettings s, Material material, float radiusUnits, float unitsPerMetre, string bodyName = null)
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
            var bn = AuroraBlueNoise.Texture;
            if (bn != null) material.SetTexture("_BlueNoise", bn);
            material.SetFloat("_BlueNoiseOn",    bn != null ? 1f : 0f);
            material.SetFloat("_CloudTopAlt",    AuroraCloudTops.EffectiveKm(s, bodyName) * 1000f * unitsPerMetre);
            material.SetFloat("_CloudBaseAlt",   AuroraCloudTops.EffectiveBaseKm(s, bodyName) * 1000f * unitsPerMetre);
            material.SetFloat("_StepLength",     s.stepLength);
            material.SetFloat("_StepNear",       Mathf.Clamp(s.nearStepScale, 0.05f, 1f));
            material.SetFloat("_StepGrowth",     Mathf.Clamp(s.stepGrowth, 0f, 0.2f));
            material.SetFloat("_StepFarMul",     Mathf.Clamp(s.stepFarMul, 1f, 16f));
            material.SetFloat("_MaxSteps",       Mathf.Clamp(s.maxSteps, 8f, 512f));
            material.SetFloat("_Jitter",         s.jitter);
            material.SetFloat("_StepFilter",     Mathf.Clamp(s.stepFilter, 0.2f, 1.5f));
            material.SetFloat("_DaylightFade",   s.daylightFade);
            material.SetFloat("_Brightness",     s.brightness);
            material.SetFloat("_GreenTop",       s.greenTop);
            material.SetFloat("_BlueBottom",     s.blueBottom);
            material.SetFloat("_BlueTop",        s.blueTop);
            material.SetFloat("_PinkBottom",     s.pinkBottom);
            material.SetFloat("_GainLow",        s.gainLow);
            material.SetFloat("_GainMid",        s.gainMid);
            material.SetFloat("_GainHigh",       s.gainHigh);
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

        public static void PushRendererPerf(AuroraRenderer r, AuroraSettings s)
        {
            if (r == null) return;
            r.temporal        = s.temporalAccumulation;
            r.temporalWeight  = Mathf.Clamp(s.temporalWeight, 0f, 0.99f);
            r.temporalUpscale = Mathf.Clamp(s.temporalUpscale, 1f, 4f);
            r.temporalClamp   = Mathf.Clamp(s.temporalClamp, 0.1f, 8f);
            r.temporalParallax = Mathf.Clamp(s.temporalParallax, 0.5f, 32f);
            r.cloudGrazeBoost  = Mathf.Clamp(s.cloudGrazeBoost, 0f, 8f);
            r.blitDebug        = s.debugMode >= 9.5f ? Mathf.RoundToInt(s.debugMode) - 9 : 0;
        }

        public static void ConfigureStorm(AuroraStormScheduler storm, AuroraSettings s)
        {
            storm.baseMatAltTop        = (s.altBaseFraction + s.altTopFraction) * UnityTestRadius;
            storm.stormMatAltTop       = (s.altBaseFraction + s.altTopFraction * s.stormHeightGain) * UnityTestRadius;
            storm.baseMatActivity      = s.baseActivity;
            storm.stormMatActivity     = s.stormActivity;
            storm.baseMatAnimSpeed     = s.baseAnimSpeed;
            storm.stormMatAnimSpeed    = s.stormAnimSpeed;
            storm.averageQuietDuration = s.averageQuietDuration;

            storm.driveOval          = true;
            storm.baseLatCenter      = s.latCenter;
            storm.baseBandSpacing    = s.bandSpacing * Mathf.Max(s.ovalQuietSpread, 0f);
            if (s.stormToEquator)
            {

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

        public void ApplySettings()
        {
            var g = AuroraConfigs.Global;

            if (ready)
            {
                if (g.mapView && distant.Count == 0) BuildDistant();
                else if (!g.mapView && distant.Count > 0) TearDownDistant();
            }
            foreach (var kv in distant) if (kv.Value != null) kv.Value.Apply(AuroraConfigs.Get(kv.Key));

            if (material == null) return;
            var s = Settings;

            if (diag != null) { diag.active = s.diagnostics; diag.NoteApplySettings(); }
            PushMaterial(s, material, radius, 1f, body != null ? body.bodyName : null);

            if (renderer != null)
            {
                renderer.renderScale    = LocalRenderScale(s, FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null);
                renderer.exposure       = s.exposure;
                renderer.flipY          = s.flipY;
                renderer.compositeEvent = s.compositeEvent;
                renderer.cloudOcclusion = s.cloudOcclusion && body != null && body.atmosphere;
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

            if (pendingBody == null && ++bodyPoll >= 60)
            {
                bodyPoll = 0;
                var now = CurrentMainBody();
                if (now != null && now != body) pendingBody = now;
            }
            if (pendingBody != null)
            {
                var nb = pendingBody; pendingBody = null;
                Log($"Main body changed: {(body != null ? body.bodyName : "(none)")} -> {nb.bodyName}");
                Rebuild(nb);
            }

            if (material == null || body == null) return;
            var s = Settings;

            Transform camT = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null
                           ? FlightCamera.fetch.mainCamera.transform : null;
            if (diag != null) diag.cam = camT;
            Vector3 camRel = camT != null ? camT.position - body.transform.position : Vector3.up * (radius + 1f);
            float   camAlt = camRel.magnitude - radius;

            if (renderer != null)
            {
                float scale = LocalRenderScale(s, camT != null ? FlightCamera.fetch.mainCamera : null);
                if (Mathf.Abs(scale - renderer.renderScale) > 0.001f) renderer.renderScale = scale;

                int wanted = s.compositeEvent;
                if (s.autoAboveClouds && camT != null && camAlt > AuroraCloudTops.EffectiveKm(s, body.bodyName) * 1000f)
                    wanted = s.compositeEventAbove;
                if (renderer.compositeEvent != wanted) renderer.compositeEvent = wanted;

                renderer.cameraAltitudeM    = camAlt;
                renderer.cloudBody          = body.bodyName;
                renderer.volumetricsCeilingM = AuroraCloudTops.TryGet(body.bodyName, out var cloudInfo) ? cloudInfo.VolumetricsCeilingM : -1f;

                bool   terrainOff = body.pqsController == null || !body.pqsController.isActive;
                double vesselAlt  = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.altitude : 0.0;
                bool   highCamera = camAlt > 2.5 * ScaledAuroraInstance.TerrainMax(body) && camAlt > 5000f;
                bool   wantModel  = body.pqsController != null && (terrainOff || highCamera);
                string modelNote  = ScaledAuroraInstance.HandPlanetModel(renderer, body, wantModel);
                if (renderer.planetModelOn != planetModelLogged || (renderer.planetModelOn && modelNote != planetModelNote))
                {
                    planetModelLogged = renderer.planetModelOn; planetModelNote = modelNote;
                    Log(renderer.planetModelOn
                        ? $"{body.bodyName}: its own model is the limb's ground test now ({(terrainOff ? "terrain off" : "camera clear of the terrain")}; {modelNote}; camera {camAlt:F0} m up, vessel {vesselAlt:F0} m)"
                        : $"{body.bodyName}: the depth buffer is the limb's ground test again (camera {camAlt:F0} m up, vessel {vesselAlt:F0} m)");
                }
            }

            if (scaled != null) scaled.SetActive(s.mapView && MapView.MapIsEnabled);

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
                    Log($"{body.bodyName}: {(dark ? $"in {body.referenceBody.bodyName}'s shadow, daylight fade lifted" : "back in sunlight, daylight fade on")}");
                }
            }
            else material.SetVector("_SunDir", Vector4.zero);
        }
        bool eclipsed;
        bool   planetModelLogged;
        string planetModelNote = "";

        public float LocalSpanDeg { get; private set; }

        float LocalRenderScale(AuroraSettings s, Camera cam)
        {
            float cfg = Mathf.Clamp(s.renderScale, 0.1f, 1f);
            LocalSpanDeg = 0f;
            if (!s.distantAutoScale || cam == null || body == null || renderer == null) return cfg;
            float top  = radius * (1f + s.altBaseFraction + s.altTopFraction * Mathf.Max(s.stormHeightGain, 1f));
            float dist = (cam.transform.position - body.transform.position).magnitude;
            float half = dist <= top ? 90f : Mathf.Asin(Mathf.Clamp01(top / dist)) * Mathf.Rad2Deg;
            LocalSpanDeg = 2f * half;
            return ScaledAuroraInstance.BudgetScale(cfg, 1f, renderer.renderScale, cam, LocalSpanDeg);
        }

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

    public class ScaledAuroraDriver : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Scaled]";
        public const string ScaledCameraName = "Camera ScaledSpace";

        public AuroraRenderer Renderer       => scaledRenderer;
        public Material       ScaledMaterial => scaledMaterial;
        public string Status => scaledRenderer == null ? "not built"
                              : !scaledRenderer.renderingEnabled ? "idle (not in the map view)"
                              : $"{(autoScaleOn && lastSpanDeg > 0f ? $"{lastSpanDeg:F1} deg across, scale {scaledRenderer.renderScale:F2} by pixel budget (map scale {mapScale:F2}); " : "")}{scaledRenderer.Status}";

        GameObject     go;
        Material       scaledMaterial;
        AuroraRenderer scaledRenderer;
        bool           ownsMaterial;
        CelestialBody  body;
        Camera         scaledCam;
        int            camSearch;

        bool  autoScaleOn;
        float mapScale = 0.3f, mapScaleMax = 0.6f, bandTopFraction = 0.1f, lastSpanDeg;

        public void Build(CelestialBody body, Material source, Material own, AuroraSettings s)
        {
            Transform parent = body.scaledBody != null ? body.scaledBody.transform : null;
            if (parent == null) { Log($"{body.bodyName} has no scaled body, map view aurora disabled"); return; }
            if (RVABootstrap.AuroraShader == null) return;

            var stale = parent.Find($"RVA_scaled_{body.bodyName}");
            if (stale != null) Destroy(stale.gameObject);

            this.body = body;
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
            scaledRenderer.sceneDepthOcclusion = true;
            scaledRenderer.edgeAware           = false;
            scaledRenderer.renderingEnabled    = false;
            Apply(s);
            Log($"built around {body.bodyName}'s scaled body (scale 1/{ScaledSpace.ScaleFactor:F0}, camera '{ScaledCameraName}')");
        }

        public void Apply(AuroraSettings s)
        {
            if (scaledRenderer == null) return;
            mapScale        = Mathf.Clamp(s.mapRenderScale, 0.1f, 1f);
            mapScaleMax     = Mathf.Clamp(s.mapAutoScaleMax, mapScale, 1f);
            autoScaleOn     = s.distantAutoScale;
            bandTopFraction = s.altBaseFraction + s.altTopFraction * Mathf.Max(s.stormHeightGain, 1f);
            scaledRenderer.renderScale    = mapScale;
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

        void LateUpdate()
        {
            if (scaledRenderer == null || !scaledRenderer.renderingEnabled || body == null || body.scaledBody == null) return;
            float target = mapScale;
            if (autoScaleOn)
            {
                if (scaledCam == null && ++camSearch % 30 == 1)
                    foreach (var c in Camera.allCameras)
                        if (c != null && c.name == ScaledCameraName) { scaledCam = c; break; }
                if (scaledCam != null)
                {
                    float r    = (float)(body.Radius * (1.0 + bandTopFraction) / ScaledSpace.ScaleFactor);
                    float dist = Vector3.Distance(scaledCam.transform.position, body.scaledBody.transform.position);
                    float half = dist <= r ? 90f : Mathf.Asin(Mathf.Clamp01(r / dist)) * Mathf.Rad2Deg;
                    lastSpanDeg = 2f * half;
                    target = ScaledAuroraInstance.BudgetScale(mapScale, mapScaleMax, scaledRenderer.renderScale, scaledCam, lastSpanDeg);
                }
            }
            else lastSpanDeg = 0f;
            if (Mathf.Abs(target - scaledRenderer.renderScale) > 0.001f) scaledRenderer.renderScale = target;
        }

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
                if (++waitFrames > 600) { Log("Shaders never loaded, aborting"); yield break; }
                yield return null;
            }
            while (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                yield return null;

            foreach (var name in AuroraConfigs.Bodies)
            {
                CelestialBody body = null;
                foreach (var b in FlightGlobals.Bodies)
                    if (b != null && string.Equals(b.bodyName, name, System.StringComparison.OrdinalIgnoreCase)) { body = b; break; }
                if (body == null || body.scaledBody == null) { Log($"Body '{name}' (scaled) not found, skipped"); continue; }
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
