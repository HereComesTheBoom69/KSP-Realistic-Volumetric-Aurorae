// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RVA
{

    public static class AuroraCloudTops
    {
        public class Info
        {
            public string body;
            public int    layers;
            public float  topMetres;
            public float  baseMetres = -1f;
            public string topLayer;
            public bool   fromCloudTypes;
            public int    fxLayers;
            public float  fadeStartMetres = -1f, fadeEndMetres = -1f;
            public string fadeLayer;
            public float  TopKm => topMetres / 1000f;

            public float  VolumetricsCeilingM => fadeEndMetres > 0f ? fadeEndMetres : -1f;
        }

        public const float DefaultFadeStartM = 30000f, DefaultFadeEndM = 55000f;

        public const float MarginKm = 0.5f;

        static readonly Dictionary<string, Info> byBody = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
        static bool   scanned;
        static string report = "";
        static int    objects;

        public static void Rescan() { scanned = false; byBody.Clear(); report = ""; objects = 0; }

        public static bool TryGet(string body, out Info info)
        {
            Scan();
            info = null;
            return body != null && byBody.TryGetValue(body, out info);
        }

        public static float EffectiveKm(AuroraSettings s, string body)
        {
            if (s == null) return 12f;
            if (s.cloudTopAuto && TryGet(body, out var info) && info.topMetres > 0f) return info.TopKm + MarginKm;
            return s.cloudTopKm;
        }

        public static float EffectiveBaseKm(AuroraSettings s, string body)
        {
            float top = EffectiveKm(s, body);
            if (s != null && s.cloudTopAuto && TryGet(body, out var info) && info.topMetres > 0f && info.baseMetres >= 0f)
                return Math.Min(info.baseMetres / 1000f, top * 0.9f);
            return top * 0.1f;
        }

        public static bool IsAuto(AuroraSettings s, string body) => s != null && s.cloudTopAuto && TryGet(body, out var i) && i.topMetres > 0f;

        public static string Describe(AuroraSettings s, string body)
        {
            if (TryGet(body, out var info))
            {
                string slab = info.baseMetres >= 0f ? $", base {info.baseMetres / 1000f:F1} km" : "";
                string fade = info.fadeEndMetres > 0f ? $"; volumetrics off above {info.fadeEndMetres / 1000f:F0} km ({info.fadeLayer}{(info.fadeStartMetres > 0f ? $", fade from {info.fadeStartMetres / 1000f:F0} km" : "")})" : "";
                string fx   = info.fxLayers > 0 ? $" ({info.fxLayers} fx only)" : "";
                if (info.topMetres > 0f)
                {
                    string how = info.fromCloudTypes ? "cloudTypes" : "layer altitude only";
                    string use = s != null && s.cloudTopAuto ? $"cloud top {info.TopKm + MarginKm:F1} km" : $"cloud top from the setting, {(s != null ? s.cloudTopKm : 0f):F1} km";
                    return $"EVE volumetrics on {body}: {info.layers} layer{(info.layers == 1 ? "" : "s")}{fx}, top {info.TopKm:F1} km ({info.topLayer}, {how}){slab}; {use}{fade}";
                }
                return $"EVE volumetrics on {body}: {info.layers}{fx}, none occluding; cloud top from the setting, {(s != null ? s.cloudTopKm : 0f):F1} km{fade}";
            }
            Scan();
            return objects > 0
                ? $"no EVE volumetrics on {body}; cloud top from the setting, {(s != null ? s.cloudTopKm : 0f):F1} km"
                : $"no EVE cloud configs found; cloud top from the setting, {(s != null ? s.cloudTopKm : 0f):F1} km";
        }

        public static string Report { get { Scan(); return report; } }

        static void Scan()
        {
            if (scanned) return;
            scanned = true;
            byBody.Clear(); objects = 0;
            var sb = new StringBuilder();
            try
            {
                ConfigNode[] roots = GameDatabase.Instance != null ? GameDatabase.Instance.GetConfigNodes("EVE_CLOUDS") : null;
                if (roots != null)
                {
                    foreach (var root in roots)
                    {
                        if (root == null) continue;
                        foreach (ConfigNode obj in root.nodes)
                        {
                            if (obj == null || !string.Equals(obj.name, "OBJECT", StringComparison.OrdinalIgnoreCase)) continue;
                            objects++;
                            string body = obj.GetValue("body");
                            if (string.IsNullOrEmpty(body)) continue;

                            ConfigNode rm = null;
                            foreach (ConfigNode n in obj.nodes)
                                if (n != null && n.name != null && n.name.StartsWith("layerRaymarchedVolume", StringComparison.OrdinalIgnoreCase)) { rm = n; break; }
                            if (rm == null) continue;
                            string layerName = obj.GetValue("name") ?? "(unnamed)";

                            bool fxOnly = false, anyDensity = false, anyType = false;
                            foreach (ConfigNode n in rm.nodes)
                            {
                                if (n == null) continue;
                                if (string.Equals(n.name, "raymarchingSettings", StringComparison.OrdinalIgnoreCase))
                                {
                                    string fx = n.GetValue("fxOnlyLayer");
                                    if (fx != null && fx.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) fxOnly = true;
                                }
                            }

                            float top = float.NaN; bool fromTypes = false;
                            float layerBase = float.NaN;
                            foreach (ConfigNode n in rm.nodes)
                            {
                                if (n == null || !string.Equals(n.name, "cloudTypes", StringComparison.OrdinalIgnoreCase)) continue;
                                foreach (ConfigNode type in n.nodes)
                                {
                                    if (type == null) continue;
                                    anyType = true;
                                    bool densityKey = false, densityZero = false;
                                    foreach (ConfigNode.Value v in type.values)
                                    {
                                        if (v == null) continue;
                                        if (string.Equals(v.name, "maxAltitude", StringComparison.OrdinalIgnoreCase)
                                            && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float m))
                                        { top = float.IsNaN(top) ? m : Math.Max(top, m); fromTypes = true; }
                                        if (string.Equals(v.name, "minAltitude", StringComparison.OrdinalIgnoreCase)
                                            && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float mn))
                                        { layerBase = float.IsNaN(layerBase) ? mn : Math.Min(layerBase, mn); }
                                        if (string.Equals(v.name, "density", StringComparison.OrdinalIgnoreCase)
                                            && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                                        { densityKey = true; densityZero = d <= 0f; }
                                    }
                                    if (!densityKey || !densityZero) anyDensity = true;
                                }
                            }
                            if (float.IsNaN(top))
                            {
                                string alt = obj.GetValue("altitude");
                                if (alt != null && float.TryParse(alt, NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) top = a;
                            }
                            bool occludes = !fxOnly && (anyDensity || !anyType);

                            float fadeStart = -1f, fadeEnd = -1f;
                            foreach (ConfigNode holder in new[] { rm, obj })
                            {
                                foreach (ConfigNode.Value v in holder.values)
                                {
                                    if (v == null) continue;
                                    if (string.Equals(v.name, "scaledFadeStartAltitude", StringComparison.OrdinalIgnoreCase) && fadeStart < 0f
                                        && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fs)) fadeStart = fs;
                                    if (string.Equals(v.name, "scaledFadeEndAltitude", StringComparison.OrdinalIgnoreCase) && fadeEnd < 0f
                                        && float.TryParse(v.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fe)) fadeEnd = fe;
                                }
                            }

                            if (fadeStart < 0f) fadeStart = DefaultFadeStartM;
                            if (fadeEnd   < 0f) fadeEnd   = DefaultFadeEndM;

                            if (!byBody.TryGetValue(body, out var info)) { info = new Info { body = body, topMetres = -1f }; byBody[body] = info; }
                            info.layers++;
                            if (!occludes) info.fxLayers++;
                            if (occludes && !float.IsNaN(top) && top > 0f && top > info.topMetres) { info.topMetres = top; info.topLayer = layerName; info.fromCloudTypes = fromTypes; }
                            if (occludes && !float.IsNaN(layerBase) && (info.baseMetres < 0f || layerBase < info.baseMetres)) info.baseMetres = Math.Max(layerBase, 0f);
                            if (fadeEnd > info.fadeEndMetres) { info.fadeEndMetres = fadeEnd; info.fadeStartMetres = fadeStart; info.fadeLayer = layerName; }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("EVE cloud config scan failed: ").Append(e.Message);
            }
            foreach (var kv in byBody)
            {
                var i = kv.Value;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append($"{i.body}: {i.layers} volumetric layer{(i.layers == 1 ? "" : "s")}{(i.fxLayers > 0 ? $" ({i.fxLayers} effects-only)" : "")}, {(i.topMetres > 0f ? $"top {i.TopKm:F1} km ({i.topLayer}{(i.fromCloudTypes ? "" : ", layer altitude only")})" : "no occluding layer")}{(i.fadeEndMetres > 0f ? $", rendered below {i.fadeEndMetres / 1000f:F0} km" : "")}");
            }
            if (byBody.Count == 0 && sb.Length == 0)
                sb.Append(objects > 0 ? $"{objects} EVE cloud objects, none volumetric" : "no EVE cloud configs");
            report = sb.ToString();
        }
    }
}
