// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// In-flight tuning window (dev build: every setting and debug control;
    /// release build: the player-facing ones — see DevOnlyAttribute): a live slider with its own
    /// "def" button (resets that one value to the body's preset), RGB
    /// sliders for the three colours, storm override, the render A/B switch,
    /// composite-order and debug views, Save / Copy buttons.
    /// 1.1.0: a body selector at the top — each configured body has its own values
    /// (the local aurora's, or another body's scaled-space instance); the global
    /// settings (hotkey, render scale, map view …) are shared.
    /// Toggle with KSP's modifier key (Alt) + toggleKey (F8 by default).
    ///
    /// Adding a slider: call Slider/IntSlider/Toggle/ColorRows with the
    /// field's nameof(...) — the def button and the default lookup come for free.
    /// </summary>
    public class AuroraTuningWindow : MonoBehaviour
    {
        public RVAFlightSetup owner;

        const int    WindowId = 0x41475731;
        const string LockId   = "RealisticVolumetricAuroraeTuning";

        Rect    windowRect = new Rect(80, 80, 600, 100);
        Vector2 scroll;
        bool    visible;
        KeyCode toggleKey = KeyCode.F8;
        float   fps;
        bool    cameraLocked;
        string  flash = "";
        float   flashUntil;

        // Dev build: every setting and every debug control. Release: the handful a
        // player realistically wants (the rest keep their shipped values).
        static bool Dev => AuroraSettings.DevBuild;
        static bool Hidden(string field) => !AuroraSettings.Exposed(field);

        // The edited body's preset (defaults + RVA_AURORA), looked up by field name for the per-row def buttons
        static AuroraSettings Defaults = new AuroraSettings();
        static readonly Dictionary<string, FieldInfo> Fields = BuildFieldMap();

        // Which body the sliders edit: the local aurora's by default
        string editBody;
        AuroraSettings EditSettings => AuroraConfigs.Get(editBody) ?? owner.Settings;
        bool EditingLocal => owner != null && owner.BodyName != null && string.Equals(editBody, owner.BodyName, StringComparison.OrdinalIgnoreCase);
        ScaledAuroraInstance EditInstance => EditingLocal ? null : owner.DistantFor(editBody);

        static Dictionary<string, FieldInfo> BuildFieldMap()
        {
            var d = new Dictionary<string, FieldInfo>();
            foreach (var f in typeof(AuroraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)) d[f.Name] = f;
            return d;
        }
        static float DefF(string n) => (float)Fields[n].GetValue(Defaults);
        static int   DefI(string n) => (int)Fields[n].GetValue(Defaults);
        static bool  DefB(string n) => (bool)Fields[n].GetValue(Defaults);
        static Color DefC(string n) => (Color)Fields[n].GetValue(Defaults);

        void Start() { ParseToggleKey(); }

        void ParseToggleKey()
        {
            string key = AuroraConfigs.Global.toggleKey ?? "F8";
            if (!Enum.TryParse(key, true, out toggleKey)) toggleKey = KeyCode.F8;
        }

        void PickEditBody()
        {
            var bodies = AuroraConfigs.Bodies;
            if (editBody != null && AuroraConfigs.Has(editBody)) return;
            if (owner != null && owner.BodyName != null && AuroraConfigs.Has(owner.BodyName)) { editBody = owner.BodyName; return; }
            editBody = bodies.Count > 0 ? bodies[0] : AuroraConfigs.DefaultBody;
        }

        void Update()
        {
            if (GameSettings.MODIFIER_KEY.GetKey() && Input.GetKeyDown(toggleKey)) visible = !visible;

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) fps = Mathf.Lerp(fps, 1f / dt, 0.05f);

            if (!visible && cameraLocked) Unlock();
        }

        void Unlock()
        {
            InputLockManager.RemoveControlLock(LockId);
            cameraLocked = false;
        }

        void OnDisable() { if (cameraLocked) Unlock(); }
        void OnDestroy() { if (cameraLocked) Unlock(); }

        void OnGUI()
        {
            if (!visible || owner == null) return;
            PickEditBody();
            Defaults = AuroraConfigs.Preset(editBody);
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(WindowId, windowRect, Draw, $"Realistic Volumetric Aurorae v{RVABootstrap.Version} — live tuning{(Dev ? " (dev build)" : "")}", GUILayout.Width(620));

            // Stop the flight camera from spinning while sliders are dragged
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool over = windowRect.Contains(mouse);
            if (over && !cameraLocked)
            {
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, LockId);
                cameraLocked = true;
            }
            else if (!over && cameraLocked) Unlock();
        }

        void Draw(int id)
        {
            var   s       = EditSettings;
            bool  changed = false;
            var   inst    = EditInstance;                        // null = editing the local aurora
            float rkm     = EditingLocal ? owner.Radius / 1000f
                          : inst != null && inst.Body != null ? (float)inst.Body.Radius / 1000f : 600f;
            AuroraRenderer       rend = EditingLocal ? owner.Renderer : (inst != null ? inst.Renderer : null);
            AuroraStormScheduler st   = EditingLocal ? owner.Storm    : (inst != null ? inst.Storm    : null);
            AuroraMeanderTexture me   = EditingLocal ? owner.Meander  : (inst != null ? inst.Meander  : null);

            // ---- body selector (1.1.0) ------------------------------------------
            GUILayout.BeginHorizontal();
            GUILayout.Label("Body:", GUILayout.Width(60));
            foreach (var b in AuroraConfigs.Bodies)
            {
                bool here = owner.BodyName != null && string.Equals(b, owner.BodyName, StringComparison.OrdinalIgnoreCase);
                bool on   = string.Equals(b, editBody, StringComparison.OrdinalIgnoreCase);
                bool now  = GUILayout.Toggle(on, here ? b + " (here)" : b, HighLogic.Skin.button);
                if (now && !on) { editBody = b; Defaults = AuroraConfigs.Preset(b); GUILayout.EndHorizontal(); GUI.DragWindow(); return; }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!EditingLocal)
                GUILayout.Label(inst != null ? $"   {editBody}: scaled-space instance — {inst.Status}"
                                             : $"   {editBody}: not in this scene (edits apply when it is)", Wrap);

            // ---- status (wrapped: these lines can be long) -------------------------
            string build = RVABootstrap.ShaderBuildMatches ? $"shader build {RVABootstrap.ShaderBuild}"
                         : $"SHADER BUILD {RVABootstrap.ShaderBuildFound} ≠ PLUGIN {RVABootstrap.ShaderBuild} — rebuild the bundle and the DLL from the same source";
            if (Dev) GUILayout.Label($"FPS {fps:F0}   |   {build}   |   {(rend != null ? rend.Status : "renderer n/a")}", Wrap);
            else     GUILayout.Label($"FPS {fps:F0}   |   {build}", Wrap);
            string stormInfo = st != null
                ? $"storm {st.currentStormIntensity:F2} ({(st.manualOverride ? "manual" : st.Status)})"
                : "storm n/a";
            GUILayout.Label($"{stormInfo}   |   {AuroraSettings.FilePath}", Wrap);
            // ---- cost (v3.18): the GPU cost is the FPS delta of the A/B toggle; the CPU numbers are measured.
            // Each on its own line, the toggle with an explicit width: the skin's toggle is wider than its
            // layout says, and the CPU readout used to be drawn over it.
            if (rend != null)
            {
                bool ar = GUILayout.Toggle(rend.renderingEnabled, " Aurora render (A/B — not saved)", GUILayout.Width(ToggleW));
                if (ar != rend.renderingEnabled) rend.renderingEnabled = ar;
            }
            if (Dev)
                GUILayout.Label($"CPU/frame: renderer {(rend != null ? rend.LastPreCullMs : 0f):F2} ms"
                              + (me != null ? $"   |   LUT: {me.Status}" : ""), Wrap);
            if (!string.IsNullOrEmpty(flash) && Time.unscaledTime < flashUntil) GUILayout.Label(flash, Wrap);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Clamp(Screen.height - 260, 300, 820)));

            Header("Look");
            changed |= Slider("Brightness",             ref s.brightness,   0f,   40f, "F1", nameof(s.brightness));
            changed |= Slider("Exposure",               ref s.exposure,     0.1f, 4f,  "F2", nameof(s.exposure));
            changed |= Slider("High-alt density boost", ref s.highAltBoost, 0f,   3f,  "F2", nameof(s.highAltBoost));

            Header("Colours (RGB)");
            changed |= ColorRows("Bottom (green)", ref s.colorLow,  nameof(s.colorLow));
            changed |= ColorRows("Middle (blue)",  ref s.colorMid,  nameof(s.colorMid));
            changed |= ColorRows("Top (pink)",     ref s.colorHigh, nameof(s.colorHigh));
            changed |= Slider("Bottom brightness", ref s.gainLow,  0f, 3f, "F2", nameof(s.gainLow));
            changed |= Slider("Middle brightness", ref s.gainMid,  0f, 3f, "F2", nameof(s.gainMid));
            changed |= Slider("Top brightness",    ref s.gainHigh, 0f, 3f, "F2", nameof(s.gainHigh));

            Header("Colour layout (0 = band bottom, 1 = top)");
            changed |= Slider("Bottom colour until", ref s.greenTop,   0f, 1f, "F2", nameof(s.greenTop));
            changed |= Slider("Middle colour from",  ref s.blueBottom, 0f, 1f, "F2", nameof(s.blueBottom));
            changed |= Slider("Middle colour until", ref s.blueTop,    0f, 1f, "F2", nameof(s.blueTop));
            changed |= Slider("Top colour from",     ref s.pinkBottom, 0f, 1f, "F2", nameof(s.pinkBottom));

            Header("Structure");
            changed |= Slider("Upper band compression",    ref s.compressFactor, 0.2f,  1f,   "F2", nameof(s.compressFactor));
            changed |= Slider("Ray threshold (→ sparser)", ref s.rayThreshold,   0f,    0.6f, "F2", nameof(s.rayThreshold));
            changed |= Slider("Ray contrast (→ crisper)",  ref s.rayGain,        0.5f,  5f,   "F2", nameof(s.rayGain));
            changed |= Slider("Ray taper (columns widest at the base, tips at the top)", ref s.rayTaper, 0f, 1f, "F2", nameof(s.rayTaper));
            changed |= Slider("Ray zone (+1 rays above a smooth glow | 0 rays to the bottom edge | -1 rays under a diffuse top)", ref s.rayZone, -1f, 1f, "F2", nameof(s.rayZone));
            changed |= Slider("Ray fineness (→ thinner)",  ref s.rayScale,       0.5f,  6f,   "F2", nameof(s.rayScale));
            changed |= Slider("Ribbon edge sharpness",     ref s.edgeSharpness,  0f,    1f,   "F2", nameof(s.edgeSharpness));
            changed |= Slider("Top spread (diffuse cap)",  ref s.topSpread,      0f,    3f,   "F2", nameof(s.topSpread));
            changed |= Slider("Bottom edge softness",      ref s.bottomSoftness, 0.02f, 0.3f, "F3", nameof(s.bottomSoftness));
            changed |= Slider("Close-range detail",        ref s.detailStrength, 0f,    1f,   "F2", nameof(s.detailStrength));
            changed |= Slider("Hide in daylight",          ref s.daylightFade,   0f,    1f,   "F2", nameof(s.daylightFade));
            changed |= Toggle("   … except in an eclipse: while the body's parent hides the sun (Laythe in Jool's shadow, an hour per orbit) the aurora shows over the whole body — all bodies", ref s.eclipses, nameof(s.eclipses));
            changed |= Slider("Noise scale",               ref s.noiseScale,     0.5f,  5f,   "F2", nameof(s.noiseScale));

            Header("Geometry");
            changed |= Slider($"Lift ({s.altBaseFraction * rkm:F0} km: band bottom)", ref s.altBaseFraction, 0f,     0.40f, "F3", nameof(s.altBaseFraction));
            changed |= Slider($"Height ({s.altTopFraction * rkm:F0} km: band thickness)", ref s.altTopFraction, 0.02f, 0.30f, "F3", nameof(s.altTopFraction));
            changed |= Slider("Storm height gain",                                ref s.stormHeightGain, 1f,     1.6f,  "F2", nameof(s.stormHeightGain));
            changed |= Slider($"Latitude center ({s.latCenter * 90f:F0}°)",       ref s.latCenter,       0.40f,  0.90f, "F3", nameof(s.latCenter));
            changed |= Slider($"Ribbon half-width (±{s.latWidth * 90f:F1}°)",     ref s.latWidth,        0.005f, 0.20f, "F3", nameof(s.latWidth));
            changed |= Slider($"Ribbon folds (±{s.foldStrength * 45f:F1}°)",      ref s.foldStrength,    0f,     0.30f, "F3", nameof(s.foldStrength));
            changed |= Slider("Fold scale (→ tighter curls)",                     ref s.foldScale,       0.2f,   4f,    "F2", nameof(s.foldScale));
            changed |= Slider("Arc spacing (the three arcs; 0 = on one line)",    ref s.bandSpacing,     0f,     2f,    "F2", nameof(s.bandSpacing));
            changed |= Slider("Arc independence (0 parallel → 1 free)",           ref s.bandIndependence, 0f,    1f,    "F2", nameof(s.bandIndependence));
            changed |= Slider("Width variance (1 = 0.15–2× the width; above 1 the thick stretches grow to 1+V×, the thin ones sit at the 0.15 floor)", ref s.widthVariance, 0f, 3f, "F2", nameof(s.widthVariance));
            changed |= Slider("Height variance",                                  ref s.heightVariance,  0f,     1f,    "F2", nameof(s.heightVariance));
            changed |= Slider("Spike height",                                     ref s.spikiness,       0f,     2f,    "F2", nameof(s.spikiness));
            changed |= Slider("Spike sparsity (→ fewer)",                         ref s.spikeThreshold,  0.3f,   0.9f,  "F2", nameof(s.spikeThreshold));
            changed |= Slider("Green rays into upper bands",                      ref s.greenSpikes,     0f,     1f,    "F2", nameof(s.greenSpikes));

            Header("Storm");
            if (st != null)
            {
                bool manual = GUILayout.Toggle(st.manualOverride, " Manual storm intensity (scheduler paused)", GUILayout.Width(ToggleW));
                if (manual != st.manualOverride) st.manualOverride = manual;
                if (manual)
                {
                    float i  = st.currentStormIntensity;
                    float ni = SliderValue("Storm intensity", i, 0f, 1f, "F2");
                    if (ni != i) st.currentStormIntensity = ni;
                }
                else if (GUILayout.Button("Storm now (the wait ends and the storm runs its course)")) st.TriggerStorm();
            }
            changed |= Slider("Base activity",      ref s.baseActivity,         0f,  1f,   "F2", nameof(s.baseActivity));
            changed |= Slider("Storm activity",     ref s.stormActivity,        0f,  1f,   "F2", nameof(s.stormActivity));
            changed |= Slider("Base anim speed",    ref s.baseAnimSpeed,        0f,  2f,   "F2", nameof(s.baseAnimSpeed));
            changed |= Slider("Storm anim speed",   ref s.stormAnimSpeed,       0f,  3f,   "F2", nameof(s.stormAnimSpeed));
            if (!Hidden(nameof(s.averageQuietDuration)))
            {
                // the setting is seconds; the slider works in minutes of real time (1.1.0: 45 min)
                GUILayout.BeginHorizontal();
                SplitLabel("Avg time between storms (minutes of real time)", LabelW, out string qn, out string qh);
                NameLabel(qn);
                float mins = s.averageQuietDuration / 60f;
                float nm   = GUILayout.HorizontalSlider(mins, 0.5f, 120f, GUILayout.Width(SliderW));
                GUILayout.Label(nm.ToString("F1"), GUILayout.Width(ValueW));
                if (GUILayout.Button("def", GUILayout.Width(DefW))) nm = DefF(nameof(s.averageQuietDuration)) / 60f;
                GUILayout.EndHorizontal();
                HelpLine(qh);
                if (nm != mins) { s.averageQuietDuration = Mathf.Round(nm * 60f); changed = true; }
            }
            if (Dev) GUILayout.Label("   The oval through a storm (preview with the manual intensity above):", Wrap);
            changed |= Slider("Quiet arc spacing (× configured; 0.35 = arcs nearly on one line)", ref s.ovalQuietSpread, 0f, 1f, "F2", nameof(s.ovalQuietSpread));
            changed |= Toggle("Storms reach the equator: at peak the arcs run from the quiet oval's poleward arc down to the latitude below (replaces the two sliders after it)", ref s.stormToEquator, nameof(s.stormToEquator));
            if (s.stormToEquator)
            {
                changed |= Slider($"   … down to {s.stormReach * 90f:F1}° at peak",            ref s.stormReach,    0f,    0.6f,  "F3", nameof(s.stormReach));
                changed |= Slider("   fold amplitude × this at peak (spread arcs fill the gaps)", ref s.stormFoldGain, 1f,    4f,    "F2", nameof(s.stormFoldGain));
            }
            else
            {
                changed |= Slider("Peak-storm arc spacing (× configured; spreads to lower + higher latitudes)", ref s.ovalStormSpread, 1f, 3f, "F2", nameof(s.ovalStormSpread));
                changed |= Slider($"Equatorward shift at peak ({s.ovalEquatorward * 90f:F1}°)", ref s.ovalEquatorward, 0f, 0.15f, "F3", nameof(s.ovalEquatorward));
            }
            changed |= Slider("Sheets widen at peak (half-width × (1 + this))", ref s.ovalStormWiden, 0f, 2f, "F2", nameof(s.ovalStormWiden));
            changed |= Slider("Second arc fully in at (storm intensity; it fades in over the 0.15 below; 0 = always on)", ref s.arc2Appears, 0f, 1f, "F2", nameof(s.arc2Appears));
            changed |= Slider("Third arc fully in at (storm intensity; 0 = always on)",  ref s.arc3Appears, 0f, 1f, "F2", nameof(s.arc3Appears));

            Header("Light on the vessel (the aurora's glow on your craft — not on terrain, water or clouds)");
            changed |= Toggle("Light the vessel from the aurora", ref s.vesselLight, nameof(s.vesselLight));
            if (s.vesselLight)
            {
                changed |= Slider("   intensity (the lights' sum directly under a quiet ribbon; dimmer from farther off, gone in daylight)", ref s.vesselLightIntensity, 0f, 2f, "F2", nameof(s.vesselLightIntensity));
                if (!Hidden(nameof(s.vesselLightCount)))
                {
                    GUILayout.BeginHorizontal();
                    SplitLabel("   lights (each is a pass per part when per-pixel)", LabelW, out string ln, out string lh);
                    NameLabel(ln);
                    changed |= Choice(ref s.vesselLightCount, 1, "1 — mean direction");
                    changed |= Choice(ref s.vesselLightCount, 2, "2 — east / west");
                    changed |= Choice(ref s.vesselLightCount, 4, "4 — quarters");
                    if (GUILayout.Button("def", GUILayout.Width(DefW))) { int d = DefI(nameof(s.vesselLightCount)); if (d != s.vesselLightCount) { s.vesselLightCount = d; changed = true; } }
                    GUILayout.EndHorizontal();
                    HelpLine(lh);
                }
                changed |= Toggle("   per-pixel (a pass per part and light; off = per-vertex: free, but Unity hands out only four vertex-light slots per object and a light that loses its slot is drawn as ambient — the hull pulses if the craft's own lights compete)", ref s.vesselLightPixel, nameof(s.vesselLightPixel));
                if (Dev)
                {
                    changed |= IntSlider("   trace steps (longitude steps traced along each arc on each side of the vessel)", ref s.vesselLightSamples, 16, 128, nameof(s.vesselLightSamples));
                    changed |= Slider("   smoothing (s)", ref s.vesselLightSmoothing, 0f, 2f, "F2", nameof(s.vesselLightSmoothing));
                }
                if (EditingLocal && owner.VesselLight != null)
                {
                    GUILayout.Label($"   {owner.VesselLight.Status}", Wrap);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("   ", GUILayout.Width(20));
                    if (GUILayout.Button(owner.VesselLight.Logging ? "Logging the light to KSP.log…" : "Log the light to KSP.log for 10 s (every frame: inputs, each light's intensity and direction)", Wrap))
                    {
                        owner.VesselLight.LogFrames(10f);
                        Flash("Logging the vessel light every frame for 10 s — look for [RVA:VesselLight] in KSP.log");
                    }
                    GUILayout.EndHorizontal();
                }
            }

            Header($"Performance / sampling — {editBody}'s own (each body has its own buffers and steps)");
            changed |= Slider("Render scale",                        ref s.renderScale,       0.1f,   1f,    "F2", nameof(s.renderScale));
            changed |= Slider($"Step length ({s.stepLength * rkm:F1} km far away)", ref s.stepLength,  0.001f, 0.03f, "F4", nameof(s.stepLength));
            changed |= Slider($"Near step ({s.stepLength * s.nearStepScale * rkm:F2} km, up close)", ref s.nearStepScale, 0.05f, 1f, "F3", nameof(s.nearStepScale));
            changed |= Slider($"Step growth ({s.stepGrowth * 100f:F1} % of the distance from the camera)", ref s.stepGrowth, 0f, 0.1f, "F3", nameof(s.stepGrowth));
            changed |= Slider($"Far step (× step; {s.stepLength * s.stepFarMul * rkm:F1} km)", ref s.stepFarMul, 1f, 8f, "F1", nameof(s.stepFarMul));
            changed |= Slider("Max steps per ray (safety cap)",      ref s.maxSteps,          8f,     512f,  "F0", nameof(s.maxSteps));
            changed |= Slider("Sample jitter (hides layering)",      ref s.jitter,            0f,     1f,    "F2", nameof(s.jitter));
            changed |= Slider("Noise filter width (× step; lower keeps finer rays at coarser steps — with accumulation on, it averages the aliasing)", ref s.stepFilter, 0.2f, 1.5f, "F2", nameof(s.stepFilter));
            changed |= Slider("Upsample smoothing (hides grain)",    ref s.upsampleSmoothing, 0f,     1f,    "F2", nameof(s.upsampleSmoothing));
            changed |= Toggle("Depth-aware upsample (vessel / terrain / horizon silhouettes at screen resolution; needs the depth occlusion below)", ref s.edgeAwareUpsample, nameof(s.edgeAwareUpsample));
            // 1.1.0 performance
            changed |= Toggle("Temporal accumulation: each frame samples a different sub-pixel and different depths, and the frames are averaged in a history buffer (reprojected as the camera moves) at up to 2× the render resolution", ref s.temporalAccumulation, nameof(s.temporalAccumulation));
            if (s.temporalAccumulation)
            {
                changed |= Slider("   history weight (higher = smoother, slower to follow change)", ref s.temporalWeight, 0.5f, 0.98f, "F2", nameof(s.temporalWeight));
                changed |= Slider("   history resolution (× render scale, capped at the screen)", ref s.temporalUpscale, 1f, 2f, "F2", nameof(s.temporalUpscale));
                changed |= Slider("   history clamp (σ of the current neighbourhood; lower = less ghosting, more flicker)", ref s.temporalClamp, 0.5f, 4f, "F1", nameof(s.temporalClamp));
            }
            if (Dev && rend != null && !string.IsNullOrEmpty(rend.PerfStatus)) GUILayout.Label($"   {rend.PerfStatus}", Wrap);
            changed |= IntSlider("Meander LUT resolution",           ref s.meanderResolution, 32,     1024,        nameof(s.meanderResolution));
            changed |= Toggle("Meander LUT on a worker thread (whole LUT every frame)", ref s.meanderThreaded, nameof(s.meanderThreaded));
            if (Dev && me != null)
                GUILayout.Label($"   LUT: {me.Status}", Wrap);

            if (Dev)
            {
            Header("Composite / debug");
            changed |= Toggle("EVE's volumetric clouds occlude the aurora per pixel (their own transmittance, from EVE's cloud buffer; works at every altitude)", ref s.cloudOcclusion, nameof(s.cloudOcclusion));
            if (!Hidden(nameof(s.cloudBufferFormat)))
            {
                GUILayout.BeginHorizontal();
                NameLabel("   EVE buffer alpha is:");
                changed |= Choice(ref s.cloudBufferFormat, 2, "transmittance (verified)");
                changed |= Choice(ref s.cloudBufferFormat, 0, "opacity");
                if (GUILayout.Button("def", GUILayout.Width(DefW))) { int d = DefI(nameof(s.cloudBufferFormat)); if (d != s.cloudBufferFormat) { s.cloudBufferFormat = d; changed = true; } }
                GUILayout.EndHorizontal();
            }
            changed |= Toggle("   flip EVE's buffer vertically (only if the occlusion is upside-down)", ref s.cloudBufferFlipY, nameof(s.cloudBufferFlipY));
            changed |= Slider("Cloud top altitude (km) — top of the highest volumetric layer", ref s.cloudTopKm, 2f, 120f, "F0", nameof(s.cloudTopKm));
            changed |= Toggle("Also switch the composite event by camera altitude (below / above the cloud tops)", ref s.autoAboveClouds, nameof(s.autoAboveClouds));
            changed |= EventRow(s.autoAboveClouds ? "Composite at (below):" : "Composite at:", ref s.compositeEvent, nameof(s.compositeEvent),
                                AuroraRenderer.EvAfterForwardOpaque, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardAlpha,
                                AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects, AuroraRenderer.EvAfterEverything);
            if (s.autoAboveClouds)
                changed |= EventRow("Composite at (above):", ref s.compositeEventAbove, nameof(s.compositeEventAbove),
                                    AuroraRenderer.EvAfterForwardOpaque, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardAlpha,
                                    AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects, AuroraRenderer.EvAfterEverything);
            changed |= Toggle("Vessel / nearby terrain occlude the aurora (depth buffer, < 30 km)", ref s.sceneDepthOcclusion, nameof(s.sceneDepthOcclusion));
            if (rend != null && GUILayout.Button("Log cameras + command buffers to KSP.log (shows where EVE / Scatterer draw)"))
            {
                string dump = rend.DumpCameraInfo();
                Debug.Log("[RVA:Renderer] " + dump);
                GUIUtility.systemCopyBuffer = dump;
                Flash("Camera / command-buffer dump written to KSP.log and copied to the clipboard");
            }
            }   // Dev
            Header("Map view / tracking station / other bodies");
            changed |= Toggle("   Map view, tracking station, other bodies: render the aurorae there too (around the scaled planets)", ref s.mapView, nameof(s.mapView));
            changed |= Slider("   Map render scale", ref s.mapRenderScale, 0.1f, 1f, "F2", nameof(s.mapRenderScale));
            changed |= Slider($"   Other bodies drawn from (degrees of view their band spans; Jool from Laythe yes, Jool from Kerbin no)", ref s.distantMinAngleDeg, 0f, 5f, "F2", nameof(s.distantMinAngleDeg));
            changed |= Toggle("   Other bodies at the scale their pixel budget allows (a body that spans a small part of the view renders at up to 1.0 for the cost its own render scale would spend filling the screen)", ref s.distantAutoScale, nameof(s.distantAutoScale));
            changed |= EventRow("   Map composite at:", ref s.mapCompositeEvent, nameof(s.mapCompositeEvent),
                                AuroraRenderer.EvAfterForwardAlpha, AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects,
                                AuroraRenderer.EvAfterEverything, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardOpaque);
            if (Dev && EditingLocal && owner.Scaled != null) GUILayout.Label($"   map view: {owner.Scaled.Status}", Wrap);
            if (Dev)
                foreach (var d in owner.Distant)
                    if (d != null) GUILayout.Label($"   {d.BodyName}: {d.Status}", Wrap);
            if (Dev)
            {
            changed |= Toggle("Flip Y (only if the aurora is upside-down)", ref s.flipY, nameof(s.flipY));
            if (rend != null)
            {
                bool ao = GUILayout.Toggle(rend.auroraOnly, " Aurora only (hide the scene — shows the raw aurora buffer)", GUILayout.Width(ToggleW));
                if (ao != rend.auroraOnly) rend.auroraOnly = ao;
            }
            Header("Diagnostics (KSP.log, prefix [RVA:diag])");
            changed |= Toggle("Log to KSP.log: frame hitches, GC runs, animation / material jumps, origin shifts, LUT stalls", ref s.diagnostics, nameof(s.diagnostics));
            if (owner.Diag != null && s.diagnostics)
            {
                var recent = owner.Diag.Recent;
                if (recent.Count == 0) GUILayout.Label($"   nothing yet (frames average {1000f / Mathf.Max(fps, 1f):F1} ms)");
                for (int i = 0; i < recent.Count; i++) GUILayout.Label("   " + recent[i], Wrap);
            }
            GUILayout.BeginHorizontal();
            NameLabel("Shader debug:");
            changed |= ChoiceF(ref s.debugMode, 0f, "off");
            changed |= ChoiceF(ref s.debugMode, 1f, "red mesh");
            changed |= ChoiceF(ref s.debugMode, 2f, "blue shell");
            changed |= ChoiceF(ref s.debugMode, 6f, "density probe");
            GUILayout.EndHorizontal();
            }   // Dev

            GUILayout.EndScrollView();

            // ---- buttons -------------------------------------------------------
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to settings.cfg (all bodies)"))
            {
                try { AuroraConfigs.SyncGlobals(s); AuroraConfigs.Save(); Flash("Saved " + AuroraSettings.FilePath); }
                catch (Exception e) { Flash("Save failed: " + e.Message); }
            }
            if (GUILayout.Button($"Copy + log {editBody}'s values"))
            {
                string txt = s.ToText(editBody);
                GUIUtility.systemCopyBuffer = txt;
                Debug.Log($"[RVA] Current settings for {editBody}:\n" + txt);
                Flash("Values copied to the clipboard and written to KSP.log");
            }
            if (GUILayout.Button("Reload file"))
            {
                AuroraConfigs.Load(out string rep);
                ParseToggleKey();
                Flash(rep);
                changed = true;
            }
            if (GUILayout.Button($"{editBody}: preset values"))
            {
                AuroraConfigs.ResetToPreset(editBody);
                Flash($"{editBody} back to its preset (RVA_AURORA + built-in defaults; not saved)");
                changed = true;
            }
            if (Dev && GUILayout.Button($"Write Config/{editBody}.cfg"))
            {
                // the shipped preset, for the author: every per-body key as it is right now
                try { string path = AuroraConfigs.WritePresetFile(editBody); Flash($"Preset written: {path} — copy it into the kit's Config folder for the release"); }
                catch (Exception e) { Flash("Preset write failed: " + e.Message); }
            }
            if (GUILayout.Button("Close")) visible = false;
            GUILayout.EndHorizontal();

            if (changed) { AuroraConfigs.SyncGlobals(EditSettings); owner.ApplySettings(); }
            GUI.DragWindow();
        }

        // ---- widgets -------------------------------------------------------------
        // A row is  [name LabelW] [slider SliderW] [value ValueW] [def DefW] ; a toggle row  [box + name ToggleW] [def].
        // The name is the part of a row's label that fits its column; the rest of the label — the
        // explanation — goes on its own wrapped line under the row (HelpLine). Before 1.1.0 the whole
        // label sat in the column and ran on under the slider and the def button, which hid the end
        // of every explanation.
        const float LabelW = 220f, SliderW = 200f, ValueW = 56f, DefW = 34f;
        const float ToggleW = LabelW + SliderW + ValueW + 8f;          // a toggle's box + text, next to its def button
        const float ToggleBox = 40f;                                    // what the skin's toggle spends on its box and padding before the text (generous)
        const float HelpIndent = 16f, HelpW = ToggleW + DefW + 4f - HelpIndent;

        static GUIStyle wrap, name, help;
        /// <summary>The skin's label, word-wrapped (the skin's own runs off the window).</summary>
        static GUIStyle Wrap => wrap ?? (wrap = new GUIStyle(HighLogic.Skin.label) { wordWrap = true });
        /// <summary>A row's name in its column: wrapped, so a long one takes two lines instead of running under the slider.</summary>
        static GUIStyle Name => name ?? (name = new GUIStyle(HighLogic.Skin.label) { wordWrap = true });
        /// <summary>The explanation under a row: wrapped, a little smaller and dimmer than the name.</summary>
        static GUIStyle Help
        {
            get
            {
                if (help == null)
                {
                    help = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
                    if (help.fontSize > 0) help.fontSize = Mathf.Max(help.fontSize - 1, 9);
                    Color c = help.normal.textColor; help.normal.textColor = new Color(c.r, c.g, c.b, 0.75f);
                    help.margin = new RectOffset(help.margin.left, help.margin.right, 0, help.margin.bottom);
                }
                return help;
            }
        }

        // Text is measured with a plain, unwrapped copy of the skin's label: a wrapped style, and the
        // skin's toggle, do not report a single line's width from CalcSize (the toggle rows went unsplit).
        static GUIStyle measureStyle;
        static GUIStyle MeasureStyle => measureStyle ?? (measureStyle = new GUIStyle(HighLogic.Skin.label) { wordWrap = false, fixedWidth = 0f, fixedHeight = 0f });
        static readonly GUIContent measure = new GUIContent();
        static bool Fits(string text, float width)
        {
            measure.text = text;
            return MeasureStyle.CalcSize(measure).x <= width - 2f;
        }

        /// <summary>The text cut at a word so that it fits the width, with an ellipsis.</summary>
        static string Shorten(string text, float width)
        {
            string t = text.TrimEnd();
            while (t.Length > 8 && !Fits(t + "…", width))
            {
                int sp = t.LastIndexOf(' ');
                t = sp > 3 ? t.Substring(0, sp).TrimEnd() : t.Substring(0, t.Length - 4);
            }
            return t + "…";
        }

        /// <summary>Split a row's label into the name that fits the column (width) and the explanation
        /// for the line under the row — null when the whole label fits. The cut is the first ": " or
        /// " — ", or the " (" that opens a bracket closing the label, whichever comes first; a label
        /// with none of them is left whole (a slider's name wraps in its column; a toggle shortens it).</summary>
        static void SplitLabel(string label, float width, out string nm, out string hp)
        {
            nm = label; hp = null;
            if (Fits(label, width)) return;
            int ic = label.IndexOf(": ", StringComparison.Ordinal);
            int id = label.IndexOf(" — ", StringComparison.Ordinal);
            int ip = label.EndsWith(")", StringComparison.Ordinal) ? label.IndexOf(" (", StringComparison.Ordinal) : -1;
            int cut = -1, skip = 0; bool paren = false;
            if (ic > 0)                          { cut = ic; skip = 2; }
            if (id > 0 && (cut < 0 || id < cut)) { cut = id; skip = 3; paren = false; }
            if (ip > 0 && (cut < 0 || ip < cut)) { cut = ip; skip = 2; paren = true; }
            if (cut <= 0) return;
            nm = label.Substring(0, cut);
            hp = label.Substring(cut + skip);
            if (paren) hp = hp.Substring(0, hp.Length - 1);
            hp = hp.Trim();
            if (hp.Length > 0 && hp[0] < 128 && char.IsLower(hp[0])) hp = char.ToUpperInvariant(hp[0]) + hp.Substring(1);   // (not σ → Σ)
        }

        static void NameLabel(string nm) => GUILayout.Label(nm, Name, GUILayout.Width(LabelW));

        /// <summary>The explanation line under a row (nothing when there is none).</summary>
        static void HelpLine(string hp)
        {
            if (string.IsNullOrEmpty(hp)) return;
            GUILayout.BeginHorizontal();
            GUILayout.Space(HelpIndent);
            GUILayout.Label(hp, Help, GUILayout.Width(HelpW));
            GUILayout.EndHorizontal();
        }

        static void Header(string t)
        {
            GUILayout.Space(8);
            GUILayout.Label($"— {t} —", Wrap);
        }

        /// <summary>Slider row without a def button (for values that aren't settings).</summary>
        static float SliderValue(string label, float v, float min, float max, string fmt)
        {
            SplitLabel(label, LabelW, out string nm, out string hp);
            GUILayout.BeginHorizontal();
            NameLabel(nm);
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW));
            GUILayout.Label(nv.ToString(fmt), GUILayout.Width(ValueW));
            GUILayout.Space(DefW);
            GUILayout.EndHorizontal();
            HelpLine(hp);
            return nv;
        }

        /// <summary>Slider row with a def button that restores the built-in default of the named field.</summary>
        static bool Slider(string label, ref float v, float min, float max, string fmt, string field)
        {
            if (Hidden(field)) return false;
            SplitLabel(label, LabelW, out string nm, out string hp);
            GUILayout.BeginHorizontal();
            NameLabel(nm);
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW));
            GUILayout.Label(nv.ToString(fmt), GUILayout.Width(ValueW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefF(field);
            GUILayout.EndHorizontal();
            HelpLine(hp);
            if (nv == v) return false;
            v = nv;
            return true;
        }

        static bool IntSlider(string label, ref int v, int min, int max, string field)
        {
            if (Hidden(field)) return false;
            SplitLabel(label, LabelW, out string nm, out string hp);
            GUILayout.BeginHorizontal();
            NameLabel(nm);
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW)));
            GUILayout.Label(nv.ToString(), GUILayout.Width(ValueW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefI(field);
            GUILayout.EndHorizontal();
            HelpLine(hp);
            if (nv == v) return false;
            v = nv;
            return true;
        }

        static bool Toggle(string label, ref bool v, string field)
        {
            if (Hidden(field)) return false;
            float textW = ToggleW - ToggleBox;
            SplitLabel(label, textW, out string nm, out string hp);
            if (!Fits(nm, textW))
            {
                // no cut, or a name still too long for the toggle (whose text neither wraps nor stops
                // at its width): the whole label goes under the row and the toggle shows what fits
                hp = hp == null ? nm.Trim() : nm.Trim() + " — " + hp;
                nm = Shorten(nm, textW);
            }
            GUILayout.BeginHorizontal();
            bool nv = GUILayout.Toggle(v, " " + nm, GUILayout.Width(ToggleW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefB(field);
            GUILayout.EndHorizontal();
            HelpLine(hp);
            if (nv == v) return false;
            v = nv;
            return true;
        }

        /// <summary>Three slider rows (R, G, B) for a colour, each with its own def button.</summary>
        static bool ColorRows(string label, ref Color c, string field)
        {
            if (Hidden(field)) return false;
            Color d = DefC(field);
            float r = c.r, g = c.g, b = c.b;
            bool changed = false;
            changed |= Channel(label + "  R", ref r, d.r);
            changed |= Channel(label + "  G", ref g, d.g);
            changed |= Channel(label + "  B", ref b, d.b);
            if (changed) c = new Color(r, g, b, 1f);
            return changed;
        }

        static bool Channel(string label, ref float v, float def)
        {
            GUILayout.BeginHorizontal();
            NameLabel(label);
            float nv = GUILayout.HorizontalSlider(v, 0f, 1f, GUILayout.Width(SliderW));
            GUILayout.Label(nv.ToString("F2"), GUILayout.Width(ValueW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = def;
            GUILayout.EndHorizontal();
            if (nv == v) return false;
            v = nv;
            return true;
        }

        /// <summary>A row of camera-event buttons (short names) with a def button.</summary>
        static bool EventRow(string label, ref int field, string settingName, params int[] codes)
        {
            if (Hidden(settingName)) return false;
            bool changed = false;
            SplitLabel(label, LabelW, out string nm, out string hp);
            GUILayout.BeginHorizontal();
            NameLabel(nm);
            foreach (int code in codes)
                changed |= Choice(ref field, code, ShortEventName(code));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) { int d = DefI(settingName); if (d != field) { field = d; changed = true; } }
            GUILayout.EndHorizontal();
            HelpLine(hp);
            return changed;
        }

        static string ShortEventName(int code)
        {
            switch (code)
            {
                case AuroraRenderer.EvAfterForwardOpaque:  return "AftOpaque";
                case AuroraRenderer.EvBeforeForwardAlpha:  return "BefAlpha";
                case AuroraRenderer.EvAfterForwardAlpha:   return "AftAlpha";
                case AuroraRenderer.EvBeforeImageEffects:  return "BefImgFx";
                case AuroraRenderer.EvAfterImageEffects:   return "AftImgFx";
                case AuroraRenderer.EvAfterEverything:     return "AftAll";
                default: return code.ToString();
            }
        }

        static bool Choice(ref int field, int value, string label)
        {
            bool on  = field == value;
            bool now = GUILayout.Toggle(on, label, HighLogic.Skin.button);
            if (now && !on) { field = value; return true; }
            return false;
        }

        static bool ChoiceF(ref float field, float value, string label)
        {
            bool on  = Mathf.Abs(field - value) < 0.01f;
            bool now = GUILayout.Toggle(on, label, HighLogic.Skin.button);
            if (now && !on) { field = value; return true; }
            return false;
        }

        void Flash(string m)
        {
            flash = m;
            flashUntil = Time.unscaledTime + 8f;
        }
    }
}
