// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RVA
{

    public static class AuroraConfigs
    {
        public const string PresetNodeName = "RVA_AURORA";
        public const string FileNodeName   = "RVA_SETTINGS";
        public const string LegacyNodeName = "AURORAGLOW_SETTINGS";
        public const string BodyNodeName   = "BODY";
        public const string DefaultBody    = "Kerbin";

        class Entry
        {
            public string         body;
            public bool           enabled = true;
            public AuroraSettings preset;
            public AuroraSettings current;
            public string         source;
        }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> order = new List<string>();
        static readonly List<ConfigNode> unknownBodyNodes = new List<ConfigNode>();
        static bool loaded;

        public static string FilePath => AuroraSettings.FilePath;
        public static bool   Loaded   => loaded;

        public static IList<string> Bodies
        {
            get
            {
                EnsureLoaded();
                var list = new List<string>();
                foreach (var b in order) if (entries[b].enabled) list.Add(b);
                return list;
            }
        }

        public static bool Has(string body)
        {
            EnsureLoaded();
            return body != null && entries.TryGetValue(body, out var e) && e.enabled;
        }

        public static AuroraSettings Get(string body)
        {
            EnsureLoaded();
            return body != null && entries.TryGetValue(body, out var e) && e.enabled ? e.current : null;
        }

        public static AuroraSettings Preset(string body)
        {
            EnsureLoaded();
            return body != null && entries.TryGetValue(body, out var e) ? e.preset : new AuroraSettings();
        }

        public static AuroraSettings Global
        {
            get
            {
                EnsureLoaded();
                if (entries.TryGetValue(DefaultBody, out var k)) return k.current;
                foreach (var b in order) return entries[b].current;
                return fallback ?? (fallback = new AuroraSettings());
            }
        }
        static AuroraSettings fallback;

        static bool EnsureLoaded()
        {
            if (!loaded) Load(out string rep);
            return true;
        }

        public static void Load(out string report)
        {
            entries.Clear(); order.Clear(); unknownBodyNodes.Clear();
            AuroraCloudTops.Rescan();
            var sb = new StringBuilder();

            ConfigNode[] presets = null;
            try { if (GameDatabase.Instance != null) presets = GameDatabase.Instance.GetConfigNodes(PresetNodeName); }
            catch (Exception e) { sb.Append($"(GameDatabase unavailable: {e.GetType().Name}) "); }

            if (presets != null)
            {
                foreach (var n in presets)
                {
                    if (n == null) continue;
                    string body = n.GetValue("body") ?? n.GetValue("name");
                    if (string.IsNullOrEmpty(body)) continue;
                    body = body.Trim();
                    var e = GetOrAdd(body);
                    string en = n.GetValue("enabled");
                    if (en != null && bool.TryParse(en.Trim(), out bool enabled)) e.enabled = enabled;
                    e.preset.ApplyNode(n, globals: false, perBody: true, allowDevOnly: true, out int bad, out int dev);
                    e.source = (e.source == null ? "" : e.source + " + ") + $"{PresetNodeName}" + (bad > 0 ? $" ({bad} unparsable)" : "");
                }
            }
            if (entries.Count == 0)
            {
                GetOrAdd(DefaultBody).source = "built-in defaults (no RVA_AURORA node found)";
            }
            SortOrder();

            foreach (var e in entries.Values) e.current = e.preset.Clone();

            string path = FilePath;
            if (!File.Exists(path))
            {
                sb.Append($"no settings file at {path}, presets only");
            }
            else
            {
                ConfigNode root = null;
                try { root = ConfigNode.Load(path); } catch { }
                ConfigNode node = root != null ? (root.GetNode(FileNodeName) ?? root.GetNode(LegacyNodeName) ?? root) : null;
                if (node == null) sb.Append($"settings file unreadable: {path}, presets only");
                else
                {
                    int applied = 0, bad = 0, dev = 0;

                    foreach (var e in entries.Values) { applied += e.current.ApplyNode(node, true, false, false, out int b1, out int d1); bad += b1; dev += d1; }

                    if (entries.TryGetValue(DefaultBody, out var kerbin))
                    { applied += kerbin.current.ApplyNode(node, false, true, false, out int b2, out int d2); bad += b2; dev += d2; }

                    var bodyNodes = node.GetNodes(BodyNodeName);
                    if (bodyNodes != null)
                        foreach (var bn in bodyNodes)
                        {
                            string name = bn.GetValue("name") ?? bn.GetValue("body");
                            if (string.IsNullOrEmpty(name)) continue;
                            if (entries.TryGetValue(name.Trim(), out var e))
                            { applied += e.current.ApplyNode(bn, false, true, false, out int b3, out int d3); bad += b3; dev += d3; }
                            else unknownBodyNodes.Add(bn.CreateCopy());
                        }
                    sb.Append($"loaded {applied} value(s) from {path}")
                      .Append(bad > 0 ? $" ({bad} unparsable, skipped)" : "")
                      .Append(dev > 0 ? $" ({dev} dev-only key(s) ignored by this build)" : "")
                      .Append(unknownBodyNodes.Count > 0 ? $" ({unknownBodyNodes.Count} BODY node(s) for bodies without a preset, kept)" : "");
                }
            }

            loaded = true;
            sb.Append("; bodies: ");
            bool first = true;
            foreach (var b in order)
            {
                var e = entries[b];
                sb.Append(first ? "" : ", ").Append(b).Append(e.enabled ? "" : " (disabled)");
                first = false;
            }
            report = sb.ToString();
        }

        static Entry GetOrAdd(string body)
        {
            if (!entries.TryGetValue(body, out var e))
            {
                e = new Entry { body = body, preset = new AuroraSettings() };
                entries[body] = e;
                order.Add(body);
            }
            return e;
        }

        static void SortOrder()
        {

            int i = order.FindIndex(b => string.Equals(b, DefaultBody, StringComparison.OrdinalIgnoreCase));
            if (i > 0) { string k = order[i]; order.RemoveAt(i); order.Insert(0, k); }
        }

        public static void Save()
        {
            EnsureLoaded();
            var root = new ConfigNode();
            var node = root.AddNode(FileNodeName);
            bool hasKerbin = entries.TryGetValue(DefaultBody, out var kerbin);
            if (hasKerbin) kerbin.current.WriteNode(node, globals: true, perBody: true);
            else           Global.WriteNode(node, globals: true, perBody: false);
            foreach (var b in order)
            {
                if (hasKerbin && string.Equals(b, DefaultBody, StringComparison.OrdinalIgnoreCase)) continue;
                var bn = node.AddNode(BodyNodeName);
                bn.AddValue("name", b);
                entries[b].current.WriteNode(bn, false, true);
            }
            foreach (var un in unknownBodyNodes) node.AddNode(un.CreateCopy());
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            root.Save(path);
        }

        public static string WritePresetFile(string body)
        {
            EnsureLoaded();
            if (body == null || !entries.TryGetValue(body, out var e)) throw new ArgumentException("no such body: " + body);
            var root = new ConfigNode();
            var node = root.AddNode(PresetNodeName);
            node.AddValue("body", body);
            node.AddValue("enabled", e.enabled ? "true" : "false");
            e.current.WriteNode(node, globals: false, perBody: true);
            string dir  = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/Config");
            string path = Path.Combine(dir, body + ".cfg");
            Directory.CreateDirectory(dir);
            root.Save(path, "Realistic Volumetric Aurorae (RVA) - Copyright (c) 2026 HereComesTheBoom (\"Realistic Volumetric Aurorae\")\n"
                          + "SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)\n"
                          + body + " preset, written by the dev window");

            e.preset.CopyPerBodyFrom(e.current);
            e.source = "written by the window";
            return path;
        }

        public static void ResetToPreset(string body)
        {
            EnsureLoaded();
            if (body != null && entries.TryGetValue(body, out var e)) e.current.CopyPerBodyFrom(e.preset);
        }

        public static void SyncGlobals(AuroraSettings from)
        {
            EnsureLoaded();
            foreach (var e in entries.Values) if (!ReferenceEquals(e.current, from)) e.current.CopyGlobalsFrom(from);
        }

        public static string Describe()
        {
            EnsureLoaded();
            var sb = new StringBuilder();
            foreach (var b in order)
            {
                var e = entries[b];
                sb.Append("  ").Append(b).Append(e.enabled ? "" : " (disabled)").Append(": ").Append(e.source ?? "defaults").Append('\n');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
