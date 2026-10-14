// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RVA
{
    /// <summary>
    /// Every tunable in one place.
    ///   - Defaults are the values below — since v3.20 the same tuned values the
    ///     shipped settings.cfg carries, so a missing key or a "def" button lands
    ///     on the shipped look.
    ///   - GameData/RealisticVolumetricAurorae/PluginData/settings.cfg overrides them
    ///     (only the keys present in the file; missing keys keep defaults).
    ///   - The in-game window (Modifier + toggleKey, Alt+F8 by default) edits
    ///     them live and can save them back to that file.
    /// Unity-scale numbers (storm heights) are multiplied by worldScale =
    /// planetRadius / 10 by the bootstrap, exactly as before.
    /// </summary>
    /// <summary>A setting the release build keeps out of the user's hands: not shown in
    /// the window, not written to settings.cfg, ignored if the file has it. The dev
    /// build (AURORA_DEV defined — the "Dev" configuration) exposes everything.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DevOnlyAttribute : Attribute { }

    public class AuroraSettings
    {
        /// <summary>True in the dev build (Dev configuration, AURORA_DEV defined).</summary>
        public static bool DevBuild
        {
            get
            {
#if AURORA_DEV
                return true;
#else
                return false;
#endif
            }
        }

        static System.Collections.Generic.HashSet<string> devOnly;
        /// <summary>Whether a field is dev-only (see DevOnlyAttribute).</summary>
        public static bool IsDevOnly(string field)
        {
            if (devOnly == null)
            {
                devOnly = new System.Collections.Generic.HashSet<string>();
                foreach (var f in typeof(AuroraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (f.GetCustomAttribute<DevOnlyAttribute>() != null) devOnly.Add(f.Name);
            }
            return devOnly.Contains(field);
        }
        /// <summary>The fields this build reads, writes and shows: all of them in the dev build.</summary>
        public static bool Exposed(string field) => DevBuild || !IsDevOnly(field);

        // ---- geometry (fractions of planet radius; Kerbin = 600 km) ----------
        public float altBaseFraction  = 0.0497f;   // lift: bottom of the band above the surface (0.0497 = 30 km on Kerbin)
        public float altTopFraction   = 0.08f;   // band height (thickness): 0.08 = 48 km
        [DevOnly] public float stormHeightGain  = 1.2478f;   // top rises 25% at peak storm

        // ---- look -----------------------------------------------------------
        public float brightness     = 11.2596f;      // HDR multiplier before the tonemap
        public float exposure       = 0.6822f;      // tonemap exposure (same effect, finer control)
        // vertical colour layout (0..1 of band height): green | green→blue | blue | blue→pink | pink
        public float greenTop       = 0.504f;     // pure green up to here
        public float blueBottom     = 0.5f;     // fully blue from here
        public float blueTop        = 0.5f;     // pure blue until here
        public float pinkBottom     = 1f;     // fully pink from here
        public float gainLow        = 3f;      // green brightness
        public float gainMid        = 0f;      // blue brightness
        public float gainHigh       = 3f;      // pink brightness
        [DevOnly] public float highAltBoost   = 3f;     // extra density toward the top
        [DevOnly] public float compressFactor = 0.7875f;     // upper-band squeeze
        public float rayThreshold   = 0.4268f;     // higher = sparser rays
        [DevOnly] public float rayGain        = 4.3836f;      // higher = crisper rays
        [DevOnly] public float rayTaper       = 1f;      // columns widest at the base, narrowing to tips at the top (0 = uniform width; the threshold rises by 0.2 × this at the top)
        [DevOnly] public float rayZone        = 1f;        // where the rays live: +1 = rays above a smooth lower glow (original look), 0 = rays from the bottom edge up, -1 = rays at the bottom under a smooth diffuse top
        public float rayScale       = 6f;      // ray fineness: 1 = the original 60 km rays, 6 = very thin columns
        [DevOnly] public float edgeSharpness  = 0.0524f;      // ribbon edge: 0 = soft falloff, 1 = hard rim
        [DevOnly] public float topSpread      = 3f;      // ribbon widens toward its top (diffuse red cap)
        [DevOnly] public float bottomSoftness = 0.02f;     // bottom edge fade-in height (0..1 of band); small = crisp lower border
        [DevOnly] public float detailStrength = 1f;     // close-range fine structure
        public float latCenter      = 0.65f;
        public float latWidth       = 0.0061f;     // ribbon half-width (0.0061 = ±0.55° = ±5.7 km on Kerbin: a thin sheet; its apparent width comes from the folds)
        public float foldStrength   = 0.3f;     // ribbon folds/curls amplitude (latNorm units); 0 = plain meander
        public float foldScale      = 4f;      // fold size: higher = tighter curls
        [DevOnly] public float bandSpacing    = 1.1746f;      // latitude spacing of the three arcs: 0 = all on one line, 1 = ±4.5°, 2 = twice that
        [DevOnly] public float bandIndependence = 0f;    // 0 = the arcs fold identically (nested parallel contours), 1 = each folds on its own and they cross/merge
        [DevOnly] public float widthVariance  = 3f;        // band thickness varies along the band: width × (1 ± this), floored at 0.15×. Above 1 (v3.23b) the thick stretches grow to 1+V× and more of the band sits at the floor
        [DevOnly] public float heightVariance = 0.6042f;        // 1 = local top 30..100 % of height (original look), 0 = flat top
        [DevOnly] public float spikiness      = 0f;        // thin columns stretching the top upward (0 = none, 1 = up to 2.2× local top)
        [DevOnly] public float spikeThreshold = 0.6f;      // higher = fewer, sparser spikes
        [DevOnly] public float greenSpikes    = 1f;        // regions where rays stay green up into the blue/pink heights
        [DevOnly] public float noiseScale     = 2.5f;
        public Color colorLow  = new Color(0.4f, 1f, 0.192f, 1f);   // green
        public Color colorMid  = new Color(0.4f, 0.2f, 0.9f, 1f);   // blue
        public Color colorHigh = new Color(1f, 0.213f, 0.244f, 1f);   // pink

        // ---- storm cycle ----------------------------------------------------
        // The oval through a storm (storm intensity 0 = quiet .. 1 = peak, from the scheduler or the manual slider):
        [DevOnly] public float ovalQuietSpread      = 0.35f;  // arc spacing × this when quiet (1 = the configured spacing all the time; 0.35 = the arcs nearly on one line)
        [DevOnly] public float ovalStormSpread      = 1.6f;   // arc spacing × this at peak storm (the oval widens to lower AND higher latitudes)
        [DevOnly] public float ovalEquatorward      = 0.04f;  // the whole oval's centre moves toward the equator by this at peak (latitude units: 0.04 = 3.6°)
        [DevOnly] public float ovalStormWiden       = 0f;     // ribbon half-width × (1 + this × intensity): fatter sheets in a storm (0 = unchanged)
        [DevOnly] public float arc2Appears          = 0.35f;  // storm intensity at which the second arc is fully in (it fades in over the 0.15 below); 0 = always on
        [DevOnly] public float arc3Appears          = 0.6f;   // same for the third arc
        [DevOnly] public float baseActivity         = 0.2503f;
        [DevOnly] public float stormActivity        = 0.6985f;
        public float baseAnimSpeed        = 0.1549f;  // quiet aurora drifts slowly…
        public float stormAnimSpeed       = 0.2507f;  // …and livelier at peak storm
        public float averageQuietDuration = 90f;     // seconds between storms (mean)

        // ---- sampling / performance ----------------------------------------
        [DevOnly] public float stepLength        = 0.005f; // sample spacing as a fraction of planet radius (0.005 = 3 km on Kerbin)
        [DevOnly] public float nearStepScale     = 0.1243f;  // the step shrinks to this × stepLength as the band comes within half a planet radius of the camera (0.25 = 750 m from under the band); 1 = fixed steps
        [DevOnly] public float maxSteps          = 127.5148f;    // per-ray cap
        [DevOnly] public float jitter            = 1f;   // per-pixel sample offset (hides sample-plane layering)
        [DevOnly] public float upsampleSmoothing = 0.7037f;   // 0 = plain bilinear upsample, 1 = 5-tap smoothed (hides grain / blockiness)
        [DevOnly] public bool  edgeAwareUpsample = true;   // the upsample only blends buffer texels whose scene depth agrees with the screen pixel's: vessel, terrain and horizon silhouettes at screen resolution (needs sceneDepthOcclusion)
        public float daylightFade      = 0.901f;   // 1 = aurora hidden on the day side, 0 = always visible
        public float renderScale       = 0.35f; // aurora resolution vs screen
        // camera events (codes): 0 AfterForwardAlpha, 1 AfterImageEffects, 2 BeforeImageEffects, 3 BeforeForwardAlpha, 4 AfterEverything, 5 AfterForwardOpaque
        [DevOnly] public bool  cloudOcclusion      = true; // EVE's volumetric clouds occlude the aurora PER PIXEL: the aurora beyond the cloud-top shell is multiplied by their own transmittance (EVE's cloud buffer). Works at every altitude; no composite switch needed
        [DevOnly] public int   compositeEvent      = 0;    // where the aurora is composited (below cloudTopKm when autoAboveClouds): 3 = after EVE's volumetrics, before the transparent pass. With cloudOcclusion this cannot be earlier than 3 (EVE's buffer is written at AfterForwardOpaque); without it, 5 = right after the opaques is the only event that puts the aurora UNDER the volumetric clouds
        [DevOnly] public int   compositeEventAbove = 2;    // where it is composited above cloudTopKm when autoAboveClouds: 2 = after the transparent pass, before the image effects
        [DevOnly] public bool  autoAboveClouds     = false; // switch between the two by camera altitude — only needed without cloudOcclusion, or if things drawn in the transparent pass (2D cloud layers) cover the aurora from orbit
        [DevOnly] public float cloudTopKm          = 12f;  // cloud-top altitude (km): the shell the near / far split uses, and the switch altitude. Set it to the TOP of the highest volumetric layer
        [DevOnly] public bool  sceneDepthOcclusion = true; // terrain / vessel occlude the aurora (uses the camera depth buffer)
        [DevOnly] public bool  flipY             = false;
        [DevOnly] public int   meanderResolution = 256;    // LUT longitude samples
        [DevOnly] public bool  meanderThreaded   = true;   // bake the whole meander LUT every frame on a worker thread (false: one row per frame on the main thread)

        // ---- EVE cloud buffer (the per-pixel occlusion reads it) ---------------
        [DevOnly] public int   cloudBufferFormat   = 2;    // how EVE's cloud buffer stores its alpha: 2 = transmittance (verified for the current volumetrics), 0 = opacity
        [DevOnly] public bool  cloudBufferFlipY    = false; // flip EVE's cloud buffer vertically (only if the occlusion is upside-down)

        // ---- map view / tracking station --------------------------------------
        public bool  mapView           = true;   // the aurora around the scaled planet in the map view and the tracking station (a second renderer on the scaled-space camera; no cloud occlusion there)
        [DevOnly] public float mapRenderScale    = 0.3f;   // aurora buffer size vs screen for that renderer
        [DevOnly] public int   mapCompositeEvent = 0;      // camera event on the scaled-space camera: 0 = AfterForwardAlpha, after the scaled clouds and atmosphere

        // ---- misc ------------------------------------------------------------
        public string toggleKey = "F8";          // pressed together with KSP's modifier key (Alt)
        [DevOnly] public float  debugMode = 0f;            // 0 normal | 1 red mesh | 2 blue shell | 6 density probe
        [DevOnly] public bool   diagnostics = false;        // log frame hitches, GC runs, animation-time / material jumps, origin shifts and LUT stalls to KSP.log ([RVA:diag]); shown in the window too

        // =====================================================================
        const string NodeName       = "RVA_SETTINGS";
        const string LegacyNodeName = "AURORAGLOW_SETTINGS";   // files written before the rebrand (SPVE AuroraGlow 3.x)

        public static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/PluginData/settings.cfg");

        static FieldInfo[] Fields() =>
            typeof(AuroraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static AuroraSettings Load(out string report)
        {
            var s = new AuroraSettings();
            string path = FilePath;
            if (!File.Exists(path)) { report = $"no settings file at {path} — using defaults"; return s; }

            ConfigNode root = ConfigNode.Load(path);
            ConfigNode node = root != null ? (root.GetNode(NodeName) ?? root.GetNode(LegacyNodeName) ?? root) : null;
            if (node == null) { report = $"settings file unreadable: {path} — using defaults"; return s; }

            int applied = 0, bad = 0, devSkipped = 0;
            foreach (var f in Fields())
            {
                string v = node.GetValue(f.Name);
                if (v == null) continue;
                if (!Exposed(f.Name)) { devSkipped++; continue; }     // release build: dev-only keys stay at their defaults
                object parsed = Parse(f.FieldType, v.Trim());
                if (parsed == null) { bad++; continue; }
                f.SetValue(s, parsed);
                applied++;
            }
            report = $"loaded {applied} value(s) from {path}" + (bad > 0 ? $" ({bad} unparseable, skipped)" : "")
                   + (devSkipped > 0 ? $" ({devSkipped} dev-only key(s) ignored by this build)" : "");
            return s;
        }

        public void Save()
        {
            var root = new ConfigNode();
            var node = root.AddNode(NodeName);
            foreach (var f in Fields())
                if (Exposed(f.Name)) node.AddValue(f.Name, Format(f.GetValue(this)));
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            root.Save(path);
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(NodeName);
            sb.AppendLine("{");
            foreach (var f in Fields())
                if (Exposed(f.Name)) sb.AppendLine($"    {f.Name} = {Format(f.GetValue(this))}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public AuroraSettings Clone() => (AuroraSettings)MemberwiseClone();

        // ---- parsing ----------------------------------------------------------
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static object Parse(Type t, string v)
        {
            try
            {
                if (t == typeof(float))  return float.Parse(v, Inv);
                if (t == typeof(int))    return int.Parse(v, Inv);
                if (t == typeof(bool))   return bool.Parse(v);
                if (t == typeof(string)) return v;
                if (t == typeof(Color))
                {
                    var parts = v.Split(',');
                    if (parts.Length < 3) return null;
                    float r = float.Parse(parts[0], Inv), g = float.Parse(parts[1], Inv), b = float.Parse(parts[2], Inv);
                    float a = parts.Length > 3 ? float.Parse(parts[3], Inv) : 1f;
                    return new Color(r, g, b, a);
                }
            }
            catch { }
            return null;
        }

        static string Format(object o)
        {
            switch (o)
            {
                case float f:  return f.ToString("0.####", Inv);
                case int i:    return i.ToString(Inv);
                case bool b:   return b ? "true" : "false";
                case Color c:  return $"{c.r.ToString("0.###", Inv)},{c.g.ToString("0.###", Inv)},{c.b.ToString("0.###", Inv)},{c.a.ToString("0.###", Inv)}";
                default:       return o?.ToString() ?? "";
            }
        }
    }
}
