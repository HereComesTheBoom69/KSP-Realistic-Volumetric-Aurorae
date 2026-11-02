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

    [AttributeUsage(AttributeTargets.Field)]
    public class DevOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class GlobalAttribute : Attribute { }

    public class AuroraSettings
    {

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

        public static bool Exposed(string field) => DevBuild || !IsDevOnly(field);

        public float altBaseFraction  = 0.0334f;
        public float altTopFraction   = 0.08f;
        [DevOnly] public float stormHeightGain  = 1.2478f;

        public float brightness     = 14.9848f;
        public float exposure       = 0.6822f;

        public float greenTop       = 0.504f;
        public float blueBottom     = 0.5f;
        public float blueTop        = 0.5f;
        public float pinkBottom     = 1f;
        public float gainLow        = 3f;
        public float gainMid        = 0f;
        public float gainHigh       = 3f;
        [DevOnly] public float highAltBoost   = 3f;
        [DevOnly] public float compressFactor = 0.9132f;
        public float rayThreshold   = 0.4268f;
        [DevOnly] public float rayGain        = 4.3836f;
        [DevOnly] public float rayTaper       = 1f;
        [DevOnly] public float rayZone        = 1f;
        public float rayScale       = 6f;
        [DevOnly] public float edgeSharpness  = 0.0524f;
        [DevOnly] public float topSpread      = 3f;
        [DevOnly] public float bottomSoftness = 0.02f;
        [DevOnly] public float detailStrength = 1f;
        public float latCenter      = 0.65f;
        public float latWidth       = 0.0061f;
        public float foldStrength   = 0.3f;
        public float foldScale      = 4f;
        [DevOnly] public float bandSpacing    = 1.1746f;
        [DevOnly] public float bandIndependence = 0f;
        [DevOnly] public float widthVariance  = 3f;
        [DevOnly] public float heightVariance = 0.6042f;
        [DevOnly] public float spikiness      = 0f;
        [DevOnly] public float spikeThreshold = 0.6f;
        [DevOnly] public float greenSpikes    = 1f;
        [DevOnly] public float noiseScale     = 2.5f;
        public Color colorLow  = new Color(0.4f, 1f, 0.192f, 1f);
        public Color colorMid  = new Color(0.4f, 0.2f, 0.9f, 1f);
        public Color colorHigh = new Color(1f, 0.213f, 0.244f, 1f);

        [DevOnly] public float ovalQuietSpread      = 0.35f;
        [DevOnly] public float ovalStormSpread      = 1.6f;
        [DevOnly] public float ovalEquatorward      = 0.04f;
        [DevOnly] public float ovalStormWiden       = 0f;

        public bool  stormToEquator  = false;
        public float stormReach      = 0.02f;
        public float stormFoldGain   = 1.5f;
        [DevOnly] public float arc2Appears          = 0.35f;
        [DevOnly] public float arc3Appears          = 0.6f;
        [DevOnly] public float baseActivity         = 0.1513f;
        [DevOnly] public float stormActivity        = 0.2503f;
        public float baseAnimSpeed        = 0.1549f;
        public float stormAnimSpeed       = 0.2507f;
        public float averageQuietDuration = 2700f;

        public bool  vesselLight          = true;
        public float vesselLightIntensity = 0.3f;
        public int   vesselLightCount     = 4;
        public bool  vesselLightPixel     = true;
        [DevOnly] public int   vesselLightSamples   = 32;
        [DevOnly] public float vesselLightSmoothing = 0.25f;

        [DevOnly] public float stepLength        = 0.0042f;
        [DevOnly] public float nearStepScale     = 0.05f;

        [DevOnly] public float stepGrowth        = 0.1f;
        [DevOnly] public float stepFarMul        = 1f;
        [DevOnly] public float maxSteps          = 192f;
        [DevOnly] public float jitter            = 1f;
        [DevOnly] public float upsampleSmoothing = 0.7037f;
        [DevOnly] public bool edgeAwareUpsample = true;

        public bool  temporalAccumulation = true;
        public float temporalWeight = 0.9f;
        [DevOnly] public float temporalUpscale  = 2f;
        [DevOnly] public float temporalClamp    = 2f;
        [DevOnly] public float temporalParallax = 3f;
        [DevOnly] public float stepFilter       = 1.5f;
        public float daylightFade      = 0.901f;
        [Global] public bool eclipses  = true;
        public float renderScale       = 0.2452f;

        [Global] [DevOnly] public bool cloudOcclusion      = true;
        [Global] [DevOnly] public int compositeEvent      = 0;
        [Global] [DevOnly] public int compositeEventAbove = 2;
        [Global] [DevOnly] public bool autoAboveClouds     = false;
        public float cloudGrazeBoost = 1f;
        public bool cloudTopAuto = true;
        [DevOnly] public float cloudTopKm          = 12f;
        [Global] [DevOnly] public bool sceneDepthOcclusion = true;
        [Global] [DevOnly] public bool flipY             = false;
        [DevOnly] public int meanderResolution = 256;
        [DevOnly] public bool meanderThreaded   = true;

        [Global] [DevOnly] public int cloudBufferFormat   = 2;
        [Global] [DevOnly] public bool cloudBufferFlipY    = false;

        [Global] public bool mapView           = true;
        [Global] [DevOnly] public float mapRenderScale    = 0.3f;
        [Global] [DevOnly] public float mapAutoScaleMax   = 0.6f;
        [Global] [DevOnly] public int mapCompositeEvent = 0;
        [Global] public bool distantAutoScale = true;
        [Global] [DevOnly] public float distantMinAngleDeg = 0.5f;

        [Global] public string toggleKey = "F8";
        [Global] [DevOnly] public float debugMode = 0f;
        [Global] [DevOnly] public bool diagnostics = false;

        public static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/PluginData/settings.cfg");

        static FieldInfo[] fields;
        static FieldInfo[] Fields() =>
            fields ?? (fields = typeof(AuroraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance));

        static System.Collections.Generic.HashSet<string> globalSet;

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

        public string ToText(string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// " + body + " per-body values");
            sb.AppendLine(AuroraConfigs.BodyNodeName);
            sb.AppendLine("{");
            sb.AppendLine("    name = " + body);
            foreach (var f in Fields())
                if (!IsGlobal(f.Name) && Exposed(f.Name)) sb.AppendLine($"    {f.Name} = {Format(f.GetValue(this))}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public AuroraSettings Clone() => (AuroraSettings)MemberwiseClone();

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
