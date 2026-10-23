// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
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
    ///   - Defaults are the values below, since v3.20 the same tuned values the
    ///     shipped settings.cfg carries, so a missing key or a "def" button lands
    ///     on the shipped look.
    ///   - Since 1.1.0 there is one AuroraSettings per body (see AuroraConfigs):
    ///     RVA_AURORA { body = X ... } nodes anywhere in GameData set each body's
    ///     preset on top of these defaults, and
    ///     GameData/RealisticVolumetricAurorae/PluginData/settings.cfg holds the
    ///     user's overrides (root = global settings + Kerbin, BODY { name = X } nodes
    ///     for the others; a 1.0.x file reads unchanged as Kerbin).
    ///   - The in-game window (Modifier + toggleKey, Alt+F8 by default) edits
    ///     them live, per body, and saves them back to that file.
    /// Unity-scale numbers (storm heights) are multiplied by worldScale =
    /// planetRadius / 10 by the bootstrap, exactly as before.
    /// </summary>
    /// <summary>A setting the release build keeps out of the user's hands: not shown in
    /// the window, not written to settings.cfg, ignored if the file has it. The dev
    /// build (AURORA_DEV defined, the "Dev" configuration) exposes everything.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DevOnlyAttribute : Attribute { }

    /// <summary>A setting that is the same for every body (hotkey, render scale, map
    /// view, occlusion plumbing ...). Since 1.1.0 every body has its own AuroraSettings;
    /// global fields are read from / written to the file's root only and kept in step
    /// across bodies (AuroraConfigs.SyncGlobals). Everything else is per body.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class GlobalAttribute : Attribute { }

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
        public float altBaseFraction  = 0.0334f;   // lift: bottom of the band above the surface (0.0334 = 20 km on Kerbin)
        public float altTopFraction   = 0.08f;   // band height (thickness): 0.08 = 48 km
        [DevOnly] public float stormHeightGain  = 1.2478f;   // top rises 25% at peak storm

        // ---- look -----------------------------------------------------------
        public float brightness     = 14.9848f;      // HDR multiplier before the tonemap
        public float exposure       = 0.6822f;      // tonemap exposure (same effect, finer control)
        // vertical colour layout (0..1 of band height): green | green to blue | blue | blue to pink | pink
        public float greenTop       = 0.504f;     // pure green up to here
        public float blueBottom     = 0.5f;     // fully blue from here
        public float blueTop        = 0.5f;     // pure blue until here
        public float pinkBottom     = 1f;     // fully pink from here
        public float gainLow        = 3f;      // green brightness
        public float gainMid        = 0f;      // blue brightness
        public float gainHigh       = 3f;      // pink brightness
        [DevOnly] public float highAltBoost   = 3f;     // extra density toward the top
        [DevOnly] public float compressFactor = 0.9132f;     // upper-band squeeze
        public float rayThreshold   = 0.4268f;     // higher = sparser rays
        [DevOnly] public float rayGain        = 4.3836f;      // higher = crisper rays
        [DevOnly] public float rayTaper       = 1f;      // columns widest at the base, narrowing to tips at the top (0 = uniform width; the threshold rises by 0.2 x this at the top)
        [DevOnly] public float rayZone        = 1f;        // where the rays live: +1 = rays above a smooth lower glow (original look), 0 = rays from the bottom edge up, -1 = rays at the bottom under a smooth diffuse top
        public float rayScale       = 6f;      // ray fineness: 1 = the original 60 km rays, 6 = very thin columns
        [DevOnly] public float edgeSharpness  = 0.0524f;      // ribbon edge: 0 = soft falloff, 1 = hard rim
        [DevOnly] public float topSpread      = 3f;      // ribbon widens toward its top (diffuse red cap)
        [DevOnly] public float bottomSoftness = 0.02f;     // bottom edge fade-in height (0..1 of band); small = crisp lower border
        [DevOnly] public float detailStrength = 1f;     // close-range fine structure
        public float latCenter      = 0.65f;
        public float latWidth       = 0.0061f;     // ribbon half-width (0.0061 = +/-0.55° = +/-5.7 km on Kerbin: a thin sheet; its apparent width comes from the folds)
        public float foldStrength   = 0.3f;     // ribbon folds/curls amplitude (latNorm units); 0 = plain meander
        public float foldScale      = 4f;      // fold size: higher = tighter curls
        [DevOnly] public float bandSpacing    = 1.1746f;      // latitude spacing of the three arcs: 0 = all on one line, 1 = +/-4.5°, 2 = twice that
        [DevOnly] public float bandIndependence = 0f;    // 0 = the arcs fold identically (nested parallel contours), 1 = each folds on its own and they cross/merge
        [DevOnly] public float widthVariance  = 3f;        // band thickness varies along the band: width x (1 +/- this), floored at 0.15x. Above 1 (v3.23b) the thick stretches grow to 1+Vx and more of the band sits at the floor
        [DevOnly] public float heightVariance = 0.6042f;        // 1 = local top 30..100 % of height (original look), 0 = flat top
        [DevOnly] public float spikiness      = 0f;        // thin columns stretching the top upward (0 = none, 1 = up to 2.2x local top)
        [DevOnly] public float spikeThreshold = 0.6f;      // higher = fewer, sparser spikes
        [DevOnly] public float greenSpikes    = 1f;        // regions where rays stay green up into the blue/pink heights
        [DevOnly] public float noiseScale     = 2.5f;
        public Color colorLow  = new Color(0.4f, 1f, 0.192f, 1f);   // green
        public Color colorMid  = new Color(0.4f, 0.2f, 0.9f, 1f);   // blue
        public Color colorHigh = new Color(1f, 0.213f, 0.244f, 1f);   // pink

        // ---- storm cycle ----------------------------------------------------
        // The oval through a storm (storm intensity 0 = quiet .. 1 = peak, from the scheduler or the manual slider):
        [DevOnly] public float ovalQuietSpread      = 0.35f;  // arc spacing x this when quiet (1 = the configured spacing all the time; 0.35 = the arcs nearly on one line)
        [DevOnly] public float ovalStormSpread      = 1.6f;   // arc spacing x this at peak storm (the oval widens to lower AND higher latitudes)
        [DevOnly] public float ovalEquatorward      = 0.04f;  // the whole oval's centre moves toward the equator by this at peak (latitude units: 0.04 = 3.6°)
        [DevOnly] public float ovalStormWiden       = 0f;     // ribbon half-width x (1 + this x intensity): fatter sheets in a storm (0 = unchanged)
        // 1.1.0: a storm that reaches the equator (Laythe). At peak the three arcs are laid out from the quiet oval's
        // poleward arc down to stormReach: the poleward arc stays, the main arc moves to the middle, the equatorward
        // arc to stormReach itself; in between they lerp with the storm's intensity, so the oval visibly spreads down
        // the sky as the storm builds. The folds grow by stormFoldGain at peak so the spread arcs still fill the gaps.
        // Replaces ovalEquatorward / ovalStormSpread while on.
        public bool  stormToEquator  = false;
        public float stormReach      = 0.02f;   // latitude the equatorward arc reaches at peak, latNorm (0.02 = 1.8°)
        public float stormFoldGain   = 1.5f;    // fold amplitude x lerp(1, this, intensity)
        [DevOnly] public float arc2Appears          = 0.35f;  // storm intensity at which the second arc is fully in (it fades in over the 0.15 below); 0 = always on
        [DevOnly] public float arc3Appears          = 0.6f;   // same for the third arc
        [DevOnly] public float baseActivity         = 0.1513f;
        [DevOnly] public float stormActivity        = 0.2503f;
        public float baseAnimSpeed        = 0.1549f;  // quiet aurora drifts slowly...
        public float stormAnimSpeed       = 0.2507f;  // ...and livelier at peak storm
        public float averageQuietDuration = 2700f;   // seconds between storms (mean, real time): 45 minutes since 1.1.0 (exponential with a floor of 40 % of the mean)

        // ---- the aurora's light on the vessel (1.1.0) --------------------------
        public bool  vesselLight          = true;  // the aurora lights your vessel: one, two or four lights from the directions the ribbons' light comes from, on the vessel's layers only (not terrain, water or clouds)
        public float vesselLightIntensity = 0.3f;   // their sum directly under a quiet, straight ribbon (dimmer from farther away; off in daylight with the aurora)
        public int   vesselLightCount     = 4;      // 1 = the mean direction, 2 = east / west halves of the sky (a band overhead lights the hull from both sides), 4 = quarters (north / south too); each is a pass per part when per-pixel
        public bool  vesselLightPixel     = true;   // per-pixel (a ForwardAdd pass per part and light; normal maps and speculars respond) instead of per-vertex (free, but one of Unity's four vertex-light slots per object, shared with the craft's own lights; a light that loses its slot is drawn as ambient instead, and the hull pulses between the two)
        [DevOnly] public int   vesselLightSamples   = 32;     // longitude steps traced along each arc on each side of the vessel (0.15° near it, growing outward)
        [DevOnly] public float vesselLightSmoothing = 0.25f;  // seconds over which the lights follow the ribbons' motion

        // ---- sampling / performance ----------------------------------------
        [DevOnly] public float stepLength        = 0.0042f; // sample spacing as a fraction of planet radius (0.0042 = 2.5 km on Kerbin)
        [DevOnly] public float nearStepScale     = 0.05f;  // the step shrinks to this x stepLength as the band comes within half a planet radius of the camera (0.05 = 130 m from under the band); 1 = fixed steps
        // Sampling and resolution are PER BODY (1.1.0): a gas giant's 600 km shell and a moon's 40 km one want
        // different steps and buffers, and each body's renderers and materials are its own.
        [DevOnly] public float stepGrowth        = 0.1f;   // 1.1.0: the step grows by this per unit of distance from the camera (dt = clamp(growth x distance, stepLength x nearStepScale x R, stepLength x stepFarMul x R)); it no longer depends on the ray's total length, so neighbouring rays agree
        [DevOnly] public float stepFarMul        = 1f;      // the far step, x stepLength x R (reached at distance stepLength x stepFarMul x R / stepGrowth, which is 0.042 R = 25 km on Kerbin at the defaults: Kerbin's 1.1.0 look is tuned on a near-constant 2.5 km step; Jool's grows to 3x)
        [DevOnly] public float maxSteps          = 192f;    // per-ray safety cap; with the distance-grown step only the longest grazing rays approach it
        [DevOnly] public float jitter            = 1f;   // per-pixel sample offset (hides sample-plane layering)
        [DevOnly] public float upsampleSmoothing = 0.7037f;   // 0 = plain bilinear upsample, 1 = 5-tap smoothed (hides grain / blockiness)
        [DevOnly] public bool edgeAwareUpsample = true;   // the upsample only blends buffer texels whose scene depth agrees with the screen pixel's: vessel, terrain and horizon silhouettes at screen resolution (needs sceneDepthOcclusion)
        // 1.1.0 performance (per body):
        public bool  temporalAccumulation = true;         // average the frames into a history buffer reprojected through the camera's motion; each frame samples a different sub-pixel and different depths, so a lower render scale / coarser step converges on the same picture
        public float temporalWeight = 0.9f;               // the history's weight per frame (0.9 = each new frame is a tenth): higher is smoother and slower to follow the animation
        [DevOnly] public float temporalUpscale  = 2f;     // the history buffer's size as a multiple of the render buffers' (capped at the screen): the frames' sub-pixel offsets reconstruct the picture at this resolution
        [DevOnly] public float temporalClamp    = 2f;     // the history is clamped to the current frame's 3x3 neighbourhood mean +/- this many sigma: lower = less ghosting, more flicker
        [DevOnly] public float stepFilter       = 1.5f;  // the noise is filtered to this fraction of the march step (3/4 was a constant): with accumulation on, lower keeps finer rays at coarser steps (Jool 0.2), higher is smoother (Kerbin 1.5)
        public float daylightFade      = 0.901f;   // 1 = aurora hidden on the day side, 0 = always visible
        [Global] public bool eclipses  = true;     // 1.1.0: lift the daylight fade while the body's parent eclipses the sun (Laythe in Jool's shadow ~1 h per orbit, and the aurora then shows over the whole moon)
        public float renderScale       = 0.2452f; // aurora resolution vs screen (1.1.0: a quarter, the temporal accumulation rebuilding the detail)
        // camera events (codes): 0 AfterForwardAlpha, 1 AfterImageEffects, 2 BeforeImageEffects, 3 BeforeForwardAlpha, 4 AfterEverything, 5 AfterForwardOpaque
        [Global] [DevOnly] public bool cloudOcclusion      = true; // EVE's volumetric clouds occlude the aurora PER PIXEL: the aurora beyond the cloud-top shell is multiplied by their own transmittance (EVE's cloud buffer). Works at every altitude; no composite switch needed
        [Global] [DevOnly] public int compositeEvent      = 0;    // where the aurora is composited (below cloudTopKm when autoAboveClouds): 3 = after EVE's volumetrics, before the transparent pass. With cloudOcclusion this cannot be earlier than 3 (EVE's buffer is written at AfterForwardOpaque); without it, 5 = right after the opaques is the only event that puts the aurora UNDER the volumetric clouds
        [Global] [DevOnly] public int compositeEventAbove = 2;    // where it is composited above cloudTopKm when autoAboveClouds: 2 = after the transparent pass, before the image effects
        [Global] [DevOnly] public bool autoAboveClouds     = false; // switch between the two by camera altitude, only needed without cloudOcclusion, or if things drawn in the transparent pass (2D cloud layers) cover the aurora from orbit
        [DevOnly] public float cloudTopKm          = 12f;  // cloud-top altitude (km): the shell the near / far split uses, and the switch altitude. Set it to the TOP of the highest volumetric layer
        [Global] [DevOnly] public bool sceneDepthOcclusion = true; // terrain / vessel occlude the aurora (uses the camera depth buffer)
        [Global] [DevOnly] public bool flipY             = false;
        [DevOnly] public int meanderResolution = 256;    // LUT longitude samples
        [DevOnly] public bool meanderThreaded   = true;   // bake the whole meander LUT every frame on a worker thread (false: one row per frame on the main thread)

        // ---- EVE cloud buffer (the per-pixel occlusion reads it) ---------------
        [Global] [DevOnly] public int cloudBufferFormat   = 2;    // how EVE's cloud buffer stores its alpha: 2 = transmittance (verified for the current volumetrics), 0 = opacity
        [Global] [DevOnly] public bool cloudBufferFlipY    = false; // flip EVE's cloud buffer vertically (only if the occlusion is upside-down)

        // ---- map view / tracking station --------------------------------------
        [Global] public bool mapView           = true;   // the aurora around the scaled planet in the map view and the tracking station (a second renderer on the scaled-space camera; no cloud occlusion there)
        [Global] [DevOnly] public float mapRenderScale    = 0.3f;   // aurora buffer size vs screen for that renderer
        [Global] [DevOnly] public int mapCompositeEvent = 0;      // camera event on the scaled-space camera: 0 = AfterForwardAlpha, after the scaled clouds and atmosphere
        [Global] public bool distantAutoScale = true;              // 1.1.0: another body's aurora (Jool from Laythe) renders at the scale that keeps its pixel budget what its own render scale would spend filling the screen, up to 1.0 when it spans a small part of the view. Off = its render scale as set.
        [Global] [DevOnly] public float distantMinAngleDeg = 0.5f;  // 1.1.0: another body's aurora is drawn (scaled space, in flight and the map) only while that body's band spans at least this many degrees of view: Jool from Laythe yes, Jool from Kerbin no

        // ---- misc ------------------------------------------------------------
        [Global] public string toggleKey = "F8";          // pressed together with KSP's modifier key (Alt)
        [Global] [DevOnly] public float debugMode = 0f;            // 0 normal | 1 red mesh | 2 blue shell | 6 density probe
        [Global] [DevOnly] public bool diagnostics = false;        // log frame hitches, GC runs, animation-time / material jumps, origin shifts and LUT stalls to KSP.log ([RVA:diag]); shown in the window too

        // =====================================================================
        public static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/PluginData/settings.cfg");

        static FieldInfo[] fields;
        static FieldInfo[] Fields() =>
            fields ?? (fields = typeof(AuroraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance));

        static System.Collections.Generic.HashSet<string> globalSet;
        /// <summary>Whether a field is global (see GlobalAttribute) rather than per body.</summary>
        public static bool IsGlobal(string field)
        {
            if (globalSet == null)
            {
                globalSet = new System.Collections.Generic.HashSet<string>();
                foreach (var f in Fields())
                    if (f.GetCustomAttribute<GlobalAttribute>() != null) globalSet.Add(f.Name);
            }
            return globalSet.Contains(field);
        }

        /// <summary>Apply the values a ConfigNode carries: global fields, per-body fields, or
        /// both. Missing keys keep their current value. Dev-only keys are ignored by the
        /// release build unless allowDevOnly (an RVA_AURORA preset may set anything: it is
        /// the mod's or a planet pack's, not the user's). Returns how many values were applied.</summary>
        public int ApplyNode(ConfigNode node, bool globals, bool perBody, bool allowDevOnly, out int bad, out int devSkipped)
        {
            int applied = 0; bad = 0; devSkipped = 0;
            if (node == null) return 0;
            foreach (var f in Fields())
            {
                bool g = IsGlobal(f.Name);
                if (g && !globals) continue;
                if (!g && !perBody) continue;
                string v = node.GetValue(f.Name);
                if (v == null) continue;
                if (!allowDevOnly && !Exposed(f.Name)) { devSkipped++; continue; }
                object parsed = Parse(f.FieldType, v.Trim());
                if (parsed == null) { bad++; continue; }
                f.SetValue(this, parsed);
                applied++;
            }
            return applied;
        }

        /// <summary>Write the exposed fields into a ConfigNode (global and/or per-body ones).</summary>
        public void WriteNode(ConfigNode node, bool globals, bool perBody)
        {
            foreach (var f in Fields())
            {
                bool g = IsGlobal(f.Name);
                if (g && !globals) continue;
                if (!g && !perBody) continue;
                if (Exposed(f.Name)) node.AddValue(f.Name, Format(f.GetValue(this)));
            }
        }

        public void CopyGlobalsFrom(AuroraSettings other)
        {
            if (other == null || other == this) return;
            foreach (var f in Fields()) if (IsGlobal(f.Name)) f.SetValue(this, f.GetValue(other));
        }

        public void CopyPerBodyFrom(AuroraSettings other)
        {
            if (other == null || other == this) return;
            foreach (var f in Fields()) if (!IsGlobal(f.Name)) f.SetValue(this, f.GetValue(other));
        }

        /// <summary>The per-body values as a BODY node, for the clipboard / log. Paste into
        /// settings.cfg, or turn into an RVA_AURORA node for a planet pack.</summary>
        public string ToText(string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// per-body values for " + body + " (the global settings are not listed)");
            sb.AppendLine(AuroraConfigs.BodyNodeName);
            sb.AppendLine("{");
            sb.AppendLine("    name = " + body);
            foreach (var f in Fields())
                if (!IsGlobal(f.Name) && Exposed(f.Name)) sb.AppendLine($"    {f.Name} = {Format(f.GetValue(this))}");
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
