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
    /// "def" button (resets that one value to the built-in default), RGB
    /// sliders for the three colours, storm override, the render A/B switch,
    /// composite-order and debug views, Save / Copy buttons.
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

        // Built-in defaults, looked up by field name for the per-row def buttons
        static readonly AuroraSettings Defaults = new AuroraSettings();
        static readonly Dictionary<string, FieldInfo> Fields = BuildFieldMap();

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
            string key = owner != null && owner.Settings != null ? owner.Settings.toggleKey : "F8";
            if (!Enum.TryParse(key, true, out toggleKey)) toggleKey = KeyCode.F8;
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
            if (!visible || owner == null || owner.Settings == null) return;
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
            var   s       = owner.Settings;
            bool  changed = false;
            float rkm     = owner.Radius / 1000f;

            // ---- status --------------------------------------------------------
            if (Dev) GUILayout.Label($"FPS {fps:F0}   |   {(owner.Renderer != null ? owner.Renderer.Status : "renderer n/a")}");
            else     GUILayout.Label($"FPS {fps:F0}");
            string stormInfo = owner.Storm != null
                ? $"storm {owner.Storm.currentStormIntensity:F2}{(owner.Storm.manualOverride ? " (manual)" : "")}"
                : "storm n/a";
            GUILayout.Label($"{stormInfo}   |   {AuroraSettings.FilePath}");
            // ---- cost (v3.18): the GPU cost is the FPS delta of the A/B toggles; the CPU numbers are measured ----
            GUILayout.BeginHorizontal();
            if (owner.Renderer != null)
            {
                bool ar = GUILayout.Toggle(owner.Renderer.renderingEnabled, " Aurora render (A/B — not saved)");
                if (ar != owner.Renderer.renderingEnabled) owner.Renderer.renderingEnabled = ar;
            }
            if (Dev)
            {
                string cpu = $"CPU/frame: renderer {(owner.Renderer != null ? owner.Renderer.LastPreCullMs : 0f):F2} ms"
                           + (owner.Meander != null ? $"   LUT: {owner.Meander.Status}" : "");
                GUILayout.Label(cpu);
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(flash) && Time.unscaledTime < flashUntil) GUILayout.Label(flash);

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
            changed |= Slider("Noise scale",               ref s.noiseScale,     0.5f,  5f,   "F2", nameof(s.noiseScale));

            Header("Geometry");
            changed |= Slider($"Lift — band bottom ({s.altBaseFraction * rkm:F0} km)", ref s.altBaseFraction, 0f,     0.40f, "F3", nameof(s.altBaseFraction));
            changed |= Slider($"Height — band thickness ({s.altTopFraction * rkm:F0} km)", ref s.altTopFraction, 0.02f, 0.30f, "F3", nameof(s.altTopFraction));
            changed |= Slider("Storm height gain",                                ref s.stormHeightGain, 1f,     1.6f,  "F2", nameof(s.stormHeightGain));
            changed |= Slider($"Latitude center ({s.latCenter * 90f:F0}°)",       ref s.latCenter,       0.40f,  0.90f, "F3", nameof(s.latCenter));
            changed |= Slider($"Ribbon half-width (±{s.latWidth * 90f:F1}°)",     ref s.latWidth,        0.005f, 0.20f, "F3", nameof(s.latWidth));
            changed |= Slider($"Ribbon folds (±{s.foldStrength * 45f:F1}°)",      ref s.foldStrength,    0f,     0.30f, "F3", nameof(s.foldStrength));
            changed |= Slider("Fold scale (→ tighter curls)",                     ref s.foldScale,       0.2f,   4f,    "F2", nameof(s.foldScale));
            changed |= Slider("Arc spacing (3 arcs; 0 = on one line)",            ref s.bandSpacing,     0f,     2f,    "F2", nameof(s.bandSpacing));
            changed |= Slider("Arc independence (0 parallel → 1 free)",           ref s.bandIndependence, 0f,    1f,    "F2", nameof(s.bandIndependence));
            changed |= Slider("Width variance (1 = 0.15–2× the width; above 1 the thick stretches grow to 1+V×, the thin ones sit at the 0.15 floor)", ref s.widthVariance, 0f, 3f, "F2", nameof(s.widthVariance));
            changed |= Slider("Height variance",                                  ref s.heightVariance,  0f,     1f,    "F2", nameof(s.heightVariance));
            changed |= Slider("Spike height",                                     ref s.spikiness,       0f,     2f,    "F2", nameof(s.spikiness));
            changed |= Slider("Spike sparsity (→ fewer)",                         ref s.spikeThreshold,  0.3f,   0.9f,  "F2", nameof(s.spikeThreshold));
            changed |= Slider("Green rays into upper bands",                      ref s.greenSpikes,     0f,     1f,    "F2", nameof(s.greenSpikes));

            Header("Storm");
            if (owner.Storm != null)
            {
                bool manual = GUILayout.Toggle(owner.Storm.manualOverride, " Manual storm intensity (scheduler paused)");
                if (manual != owner.Storm.manualOverride) owner.Storm.manualOverride = manual;
                if (manual)
                {
                    float i  = owner.Storm.currentStormIntensity;
                    float ni = SliderValue("Storm intensity", i, 0f, 1f, "F2");
                    if (ni != i) owner.Storm.currentStormIntensity = ni;
                }
            }
            changed |= Slider("Base activity",      ref s.baseActivity,         0f,  1f,   "F2", nameof(s.baseActivity));
            changed |= Slider("Storm activity",     ref s.stormActivity,        0f,  1f,   "F2", nameof(s.stormActivity));
            changed |= Slider("Base anim speed",    ref s.baseAnimSpeed,        0f,  2f,   "F2", nameof(s.baseAnimSpeed));
            changed |= Slider("Storm anim speed",   ref s.stormAnimSpeed,       0f,  3f,   "F2", nameof(s.stormAnimSpeed));
            changed |= Slider("Avg quiet time (s)", ref s.averageQuietDuration, 10f, 600f, "F0", nameof(s.averageQuietDuration));
            if (Dev) GUILayout.Label("   The oval through a storm (preview with the manual intensity above):");
            changed |= Slider("Quiet arc spacing (× configured; 0.35 = arcs nearly on one line)", ref s.ovalQuietSpread, 0f, 1f, "F2", nameof(s.ovalQuietSpread));
            changed |= Slider("Peak-storm arc spacing (× configured; spreads to lower + higher latitudes)", ref s.ovalStormSpread, 1f, 3f, "F2", nameof(s.ovalStormSpread));
            changed |= Slider($"Equatorward shift at peak ({s.ovalEquatorward * 90f:F1}°)", ref s.ovalEquatorward, 0f, 0.15f, "F3", nameof(s.ovalEquatorward));
            changed |= Slider("Sheets widen at peak (half-width × (1 + this))", ref s.ovalStormWiden, 0f, 2f, "F2", nameof(s.ovalStormWiden));
            changed |= Slider("Second arc fully in at storm intensity (0 = always on)", ref s.arc2Appears, 0f, 1f, "F2", nameof(s.arc2Appears));
            changed |= Slider("Third arc fully in at storm intensity (0 = always on)",  ref s.arc3Appears, 0f, 1f, "F2", nameof(s.arc3Appears));

            Header("Performance / sampling");
            changed |= Slider("Render scale",                        ref s.renderScale,       0.1f,   1f,    "F2", nameof(s.renderScale));
            changed |= Slider($"Step length ({s.stepLength * rkm:F1} km far away)", ref s.stepLength,  0.001f, 0.03f, "F4", nameof(s.stepLength));
            changed |= Slider($"Step scale near the band ({s.stepLength * s.nearStepScale * rkm:F2} km up close; 1 = fixed steps)", ref s.nearStepScale, 0.1f, 1f, "F2", nameof(s.nearStepScale));
            changed |= Slider("Max steps per ray",                   ref s.maxSteps,          8f,     256f,  "F0", nameof(s.maxSteps));
            changed |= Slider("Sample jitter (hides layering)",      ref s.jitter,            0f,     1f,    "F2", nameof(s.jitter));
            changed |= Slider("Upsample smoothing (hides grain)",    ref s.upsampleSmoothing, 0f,     1f,    "F2", nameof(s.upsampleSmoothing));
            changed |= Toggle("Depth-aware upsample (vessel / terrain / horizon silhouettes at screen resolution; needs the depth occlusion below)", ref s.edgeAwareUpsample, nameof(s.edgeAwareUpsample));
            changed |= IntSlider("Meander LUT resolution",           ref s.meanderResolution, 32,     1024,        nameof(s.meanderResolution));
            changed |= Toggle("Meander LUT on a worker thread (whole LUT every frame)", ref s.meanderThreaded, nameof(s.meanderThreaded));
            if (Dev && owner.Meander != null)
                GUILayout.Label($"   LUT: {owner.Meander.Status}");

            if (Dev)
            {
            Header("Composite / debug");
            changed |= Toggle("EVE's volumetric clouds occlude the aurora per pixel (their own transmittance, from EVE's cloud buffer; works at every altitude)", ref s.cloudOcclusion, nameof(s.cloudOcclusion));
            if (!Hidden(nameof(s.cloudBufferFormat)))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("   EVE buffer alpha is:", GUILayout.Width(LabelW));
                changed |= Choice(ref s.cloudBufferFormat, 2, "transmittance (verified)");
                changed |= Choice(ref s.cloudBufferFormat, 0, "opacity");
                if (GUILayout.Button("def", GUILayout.Width(DefW))) { int d = DefI(nameof(s.cloudBufferFormat)); if (d != s.cloudBufferFormat) { s.cloudBufferFormat = d; changed = true; } }
                GUILayout.EndHorizontal();
            }
            changed |= Toggle("   flip EVE's buffer vertically (only if the occlusion is upside-down)", ref s.cloudBufferFlipY, nameof(s.cloudBufferFlipY));
            changed |= Slider("Cloud top altitude (km) — top of the highest volumetric layer", ref s.cloudTopKm, 2f, 40f, "F0", nameof(s.cloudTopKm));
            changed |= Toggle("Also switch the composite event by camera altitude (below / above the cloud tops)", ref s.autoAboveClouds, nameof(s.autoAboveClouds));
            changed |= EventRow(s.autoAboveClouds ? "Composite at (below):" : "Composite at:", ref s.compositeEvent, nameof(s.compositeEvent),
                                AuroraRenderer.EvAfterForwardOpaque, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardAlpha,
                                AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects, AuroraRenderer.EvAfterEverything);
            if (s.autoAboveClouds)
                changed |= EventRow("Composite at (above):", ref s.compositeEventAbove, nameof(s.compositeEventAbove),
                                    AuroraRenderer.EvAfterForwardOpaque, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardAlpha,
                                    AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects, AuroraRenderer.EvAfterEverything);
            changed |= Toggle("Vessel / nearby terrain occlude the aurora (depth buffer, < 30 km)", ref s.sceneDepthOcclusion, nameof(s.sceneDepthOcclusion));
            if (owner.Renderer != null && GUILayout.Button("Log cameras + command buffers to KSP.log (shows where EVE / Scatterer draw)"))
            {
                string dump = owner.Renderer.DumpCameraInfo();
                Debug.Log("[RVA:Renderer] " + dump);
                GUIUtility.systemCopyBuffer = dump;
                Flash("Camera / command-buffer dump written to KSP.log and copied to the clipboard");
            }
            }   // Dev
            Header("Map view / tracking station");
            changed |= Toggle("   Render the aurora in the map view and the tracking station", ref s.mapView, nameof(s.mapView));
            changed |= Slider("   Map render scale", ref s.mapRenderScale, 0.1f, 1f, "F2", nameof(s.mapRenderScale));
            changed |= EventRow("   Map composite at:", ref s.mapCompositeEvent, nameof(s.mapCompositeEvent),
                                AuroraRenderer.EvAfterForwardAlpha, AuroraRenderer.EvBeforeImageEffects, AuroraRenderer.EvAfterImageEffects,
                                AuroraRenderer.EvAfterEverything, AuroraRenderer.EvBeforeForwardAlpha, AuroraRenderer.EvAfterForwardOpaque);
            if (Dev && owner.Scaled != null) GUILayout.Label($"   map view: {owner.Scaled.Status}");
            if (Dev)
            {
            changed |= Toggle("Flip Y (only if the aurora is upside-down)", ref s.flipY, nameof(s.flipY));
            if (owner.Renderer != null)
            {
                bool ao = GUILayout.Toggle(owner.Renderer.auroraOnly, " Aurora only (hide the scene — shows the raw aurora buffer)");
                if (ao != owner.Renderer.auroraOnly) owner.Renderer.auroraOnly = ao;
            }
            Header("Diagnostics (KSP.log, prefix [RVA:diag])");
            changed |= Toggle("Log frame hitches, GC runs, animation / material jumps, origin shifts, LUT stalls", ref s.diagnostics, nameof(s.diagnostics));
            if (owner.Diag != null && s.diagnostics)
            {
                var recent = owner.Diag.Recent;
                if (recent.Count == 0) GUILayout.Label($"   nothing yet (frames average {1000f / Mathf.Max(fps, 1f):F1} ms)");
                for (int i = 0; i < recent.Count; i++) GUILayout.Label("   " + recent[i]);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader debug:", GUILayout.Width(220));
            changed |= ChoiceF(ref s.debugMode, 0f, "off");
            changed |= ChoiceF(ref s.debugMode, 1f, "red mesh");
            changed |= ChoiceF(ref s.debugMode, 2f, "blue shell");
            changed |= ChoiceF(ref s.debugMode, 6f, "density probe");
            GUILayout.EndHorizontal();
            }   // Dev

            GUILayout.EndScrollView();

            // ---- buttons -------------------------------------------------------
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to settings.cfg"))
            {
                try { s.Save(); Flash("Saved " + AuroraSettings.FilePath); }
                catch (Exception e) { Flash("Save failed: " + e.Message); }
            }
            if (GUILayout.Button("Copy + log values"))
            {
                string txt = s.ToText();
                GUIUtility.systemCopyBuffer = txt;
                Debug.Log("[RVA] Current settings:\n" + txt);
                Flash("Settings copied to the clipboard and written to KSP.log");
            }
            if (GUILayout.Button("Reload file"))
            {
                RVABootstrap.Settings = AuroraSettings.Load(out string rep);
                ParseToggleKey();
                Flash(rep);
                changed = true;
            }
            if (GUILayout.Button("All defaults"))
            {
                RVABootstrap.Settings = new AuroraSettings();
                ParseToggleKey();
                Flash("All defaults restored (not saved)");
                changed = true;
            }
            if (GUILayout.Button("Close")) visible = false;
            GUILayout.EndHorizontal();

            if (changed) owner.ApplySettings();
            GUI.DragWindow();
        }

        // ---- widgets -------------------------------------------------------------
        const float LabelW = 220f, SliderW = 200f, ValueW = 56f, DefW = 34f;

        static void Header(string t)
        {
            GUILayout.Space(8);
            GUILayout.Label($"— {t} —");
        }

        /// <summary>Slider row without a def button (for values that aren't settings).</summary>
        static float SliderValue(string label, float v, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW));
            GUILayout.Label(nv.ToString(fmt), GUILayout.Width(ValueW));
            GUILayout.Space(DefW);
            GUILayout.EndHorizontal();
            return nv;
        }

        /// <summary>Slider row with a def button that restores the built-in default of the named field.</summary>
        static bool Slider(string label, ref float v, float min, float max, string fmt, string field)
        {
            if (Hidden(field)) return false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW));
            GUILayout.Label(nv.ToString(fmt), GUILayout.Width(ValueW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefF(field);
            GUILayout.EndHorizontal();
            if (nv == v) return false;
            v = nv;
            return true;
        }

        static bool IntSlider(string label, ref int v, int min, int max, string field)
        {
            if (Hidden(field)) return false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(SliderW)));
            GUILayout.Label(nv.ToString(), GUILayout.Width(ValueW));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefI(field);
            GUILayout.EndHorizontal();
            if (nv == v) return false;
            v = nv;
            return true;
        }

        static bool Toggle(string label, ref bool v, string field)
        {
            if (Hidden(field)) return false;
            GUILayout.BeginHorizontal();
            bool nv = GUILayout.Toggle(v, " " + label, GUILayout.Width(LabelW + SliderW + ValueW + 8f));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) nv = DefB(field);
            GUILayout.EndHorizontal();
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
            GUILayout.Label(label, GUILayout.Width(LabelW));
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
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelW));
            foreach (int code in codes)
                changed |= Choice(ref field, code, ShortEventName(code));
            if (GUILayout.Button("def", GUILayout.Width(DefW))) { int d = DefI(settingName); if (d != field) { field = d; changed = true; } }
            GUILayout.EndHorizontal();
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
