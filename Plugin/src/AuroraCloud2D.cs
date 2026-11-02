using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RVA
{

    public static class AuroraCloud2D
    {
        public struct Layer
        {
            public string   name;
            public Renderer renderer;
            public Material material;
            public bool     scaled;
        }

        static bool searched, available;
        static Type cloudsPQSType, clouds2DType;
        static FieldInfo fBody, fLayer2D, fMesh, fMaterial, fScaled, fEnabled;
        static readonly List<Layer> layers = new List<Layer>();
        static readonly List<string> notes = new List<string>();
        static string scannedBody = ""; static float scannedAt = -100f; static int scannedCount = -1;

        public static bool Available { get { Search(); return available; } }

        public static string Why { get; private set; } = "";

        static void Search()
        {
            if (searched) return;
            searched = true;
            try
            {
                Assembly atmo = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "Atmosphere") { atmo = a; break; }
                if (atmo == null) { Why = "EVE (Atmosphere.dll) not loaded"; return; }
                cloudsPQSType = atmo.GetType("Atmosphere.CloudsPQS");
                clouds2DType  = atmo.GetType("Atmosphere.Clouds2D");
                if (cloudsPQSType == null || clouds2DType == null) { Why = "EVE's CloudsPQS / Clouds2D not found"; return; }
                var fl = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                fBody     = cloudsPQSType.GetField("body", fl);
                fLayer2D  = cloudsPQSType.GetField("layer2D", fl);
                fMesh     = clouds2DType.GetField("CloudMesh", fl);
                fMaterial = clouds2DType.GetField("cloudMaterial", fl);
                fScaled   = clouds2DType.GetField("isScaled", fl);
                fEnabled  = clouds2DType.GetField("_enabled", fl);
                if (fBody == null || fLayer2D == null || fMesh == null || fMaterial == null || fScaled == null)
                { Why = "EVE's flat-layer fields have changed names"; return; }
                if (fBody.FieldType != typeof(string) || !typeof(GameObject).IsAssignableFrom(fMesh.FieldType)
                    || !typeof(Material).IsAssignableFrom(fMaterial.FieldType) || fScaled.FieldType != typeof(bool))
                { Why = "EVE's flat-layer fields have changed types"; return; }
                available = true;
                Debug.Log("[RVA] EVE flat cloud layers: readable (CloudsPQS.layer2D: CloudMesh, cloudMaterial, isScaled)");
            }
            catch (Exception e) { available = false; Why = "EVE's flat layers not readable: " + e.GetType().Name; }
        }

        public static List<Layer> Layers(string body)
        {
            Search();
            if (!available || string.IsNullOrEmpty(body)) { layers.Clear(); return layers; }
            if (body == scannedBody && Time.unscaledTime - scannedAt < 3f) return layers;
            scannedBody = body; scannedAt = Time.unscaledTime;
            layers.Clear(); notes.Clear();
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType(cloudsPQSType);
                foreach (var o in all)
                {
                    if (o == null) continue;
                    string b = fBody.GetValue(o) as string;
                    if (!string.Equals(b, body, StringComparison.OrdinalIgnoreCase)) continue;
                    object l2 = fLayer2D.GetValue(o);
                    if (l2 == null) continue;
                    if (fEnabled != null && fEnabled.FieldType == typeof(bool) && !(bool)fEnabled.GetValue(l2)) continue;
                    var go  = fMesh.GetValue(l2) as GameObject;
                    var mat = fMaterial.GetValue(l2) as Material;
                    if (go == null || mat == null) continue;
                    Renderer r = go.GetComponent<Renderer>();
                    if (r == null) r = go.GetComponentInChildren<Renderer>();
                    if (r == null) continue;
                    layers.Add(new Layer { name = o.name, renderer = r, material = mat, scaled = (bool)fScaled.GetValue(l2) });
                }
                if (layers.Count != scannedCount)
                {
                    scannedCount = layers.Count;
                    int sc = 0; foreach (var l in layers) if (l.scaled) sc++;
                    Debug.Log($"[RVA] EVE flat cloud layers on {body}: {layers.Count} ({sc} in scaled space)");
                }
            }
            catch (Exception e) { layers.Clear(); Why = "EVE's flat layers not readable: " + e.GetType().Name; }
            return layers;
        }

        public static void Rescan() { scannedBody = ""; scannedAt = -100f; }
    }
}
