// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System.Collections.Generic;
using UnityEngine;

namespace RVA
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class RVAMainMenuSetup : MonoBehaviour
    {
        const string LogPrefix = "[RVA:Menu]";

        GameObject           go;
        Material             material;
        AuroraMeanderTexture meander;
        AuroraStormScheduler storm;
        AuroraRenderer       renderer;
        Transform            planet;
        Camera               cam;
        Light                sun;
        AuroraSettings       settings;
        string               bodyName = "";
        float                radiusUnits, unitsPerMetre, spanDeg;
        int                  frames, sunSearch;
        bool                 built, gaveUp;

        void LateUpdate()
        {
            if (gaveUp) return;
            if (!built)
            {
                frames++;
                if (!RVABootstrap.ShaderLoaded) { if (frames > 900) { gaveUp = true; Log("shaders never loaded, no menu aurora"); } return; }
                if (frames < 10 || frames % 10 != 0) return;
                if (!AuroraConfigs.Global.mainMenu) { gaveUp = true; return; }
                if (TryBuild()) built = true;
                else if (frames > 600)
                {
                    gaveUp = true;
                    Log("no menu planet found, no menu aurora. Active mesh objects in the scene:");
                    DumpScene();
                }
                return;
            }
            Drive();
        }

        bool TryBuild()
        {
            planet = FindPlanet(out bodyName);
            if (planet == null) return false;
            cam = FindCamera(planet);
            if (cam == null) { Log($"'{planet.name}' found but no camera renders its layer {planet.gameObject.layer} yet"); return false; }

            MeshFilter mf = planet.GetComponent<MeshFilter>();
            Bounds b = mf.sharedMesh.bounds;
            Vector3 ls = planet.lossyScale;
            radiusUnits = Mathf.Max(b.extents.x * Mathf.Abs(ls.x), b.extents.y * Mathf.Abs(ls.y), b.extents.z * Mathf.Abs(ls.z));
            if (radiusUnits <= 0f) { Log($"'{planet.name}' has no usable mesh bounds"); return false; }

            double metres = 600000.0;
            if (FlightGlobals.Bodies != null)
                foreach (var cb in FlightGlobals.Bodies)
                    if (cb != null && string.Equals(cb.bodyName, bodyName, System.StringComparison.OrdinalIgnoreCase)) { metres = cb.Radius; break; }
            unitsPerMetre = radiusUnits / (float)metres;

            settings = AuroraConfigs.Get(bodyName);
            if (settings == null) { Log($"no RVA preset for {bodyName}"); return false; }

            go = new GameObject($"RVA_menu_{bodyName}");
            go.transform.SetParent(planet, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            material = new Material(RVABootstrap.AuroraShader);
            material.SetFloat("_SphereRadius", radiusUnits);
            material.SetFloat("_AltBase",      radiusUnits * settings.altBaseFraction);
            material.SetFloat("_AltTop",       radiusUnits * (settings.altBaseFraction + settings.altTopFraction));
            material.SetFloat("_Activity",     settings.baseActivity);
            material.SetFloat("_AnimSpeed",    settings.baseAnimSpeed);
            material.SetFloat("_AnimTime",     0f);
            material.SetVector("_PlanetCenter", planet.position);
            RVAFlightSetup.PushMaterial(settings, material, radiusUnits, unitsPerMetre, bodyName);

            meander = go.AddComponent<AuroraMeanderTexture>();
            meander.auroraMaterial = material;
            meander.resolution = Mathf.Clamp(settings.meanderResolution, 32, 1024);
            meander.threaded   = settings.meanderThreaded;

            storm = go.AddComponent<AuroraStormScheduler>();
            storm.auroraMaterial = material;
            storm.worldScale     = radiusUnits / 10f;
            storm.Seed(bodyName.GetHashCode() ^ 0x4d454e55);
            RVAFlightSetup.ConfigureStorm(storm, settings);

            renderer = go.AddComponent<AuroraRenderer>();
            renderer.auroraMaterial      = material;
            renderer.blitShader          = RVABootstrap.BlitShader;
            renderer.cameraOverride      = cam;
            renderer.cameraName          = cam.name;
            renderer.cloudOcclusion      = false;
            renderer.sceneDepthOcclusion = true;
            renderer.edgeAware           = false;
            renderer.renderScale         = Mathf.Clamp(settings.renderScale, 0.1f, 1f);
            renderer.exposure            = settings.exposure;
            renderer.flipY               = settings.flipY;
            renderer.compositeEvent      = AuroraConfigs.Global.mapCompositeEvent;
            renderer.smoothing           = Mathf.Clamp01(settings.upsampleSmoothing);
            RVAFlightSetup.PushRendererPerf(renderer, settings);
            renderer.renderingEnabled    = true;

            sun = FindSun();
            Log($"{bodyName}: menu aurora built on '{planet.name}' (radius {radiusUnits:F1} units = {metres / 1000.0:F0} km, axis {planet.up}, layer {planet.gameObject.layer}), camera '{cam.name}' (fov {cam.fieldOfView:F0}, near {cam.nearClipPlane:F2}, far {cam.farClipPlane:F0}), sun {(sun != null ? $"light '{sun.name}'" : "none, no daylight fade")}");
            return true;
        }

        void Drive()
        {
            if (planet == null || material == null || renderer == null || cam == null) { Shutdown(); gaveUp = true; return; }

            Vector3 axis = planet.up.normalized;
            material.SetVector("_PlanetAxis", new Vector4(axis.x, axis.y, axis.z, 1f));

            if (sun == null && ++sunSearch % 120 == 0) sun = FindSun();
            if (sun != null)
            {
                Vector3 d = -sun.transform.forward;
                material.SetVector("_SunDir", new Vector4(d.x, d.y, d.z, 1f));
            }
            else material.SetVector("_SunDir", Vector4.zero);

            float top  = radiusUnits * (1f + settings.altBaseFraction + settings.altTopFraction * Mathf.Max(settings.stormHeightGain, 1f));
            float dist = Vector3.Distance(cam.transform.position, planet.position);
            float half = dist <= top ? 90f : Mathf.Asin(Mathf.Clamp01(top / dist)) * Mathf.Rad2Deg;
            spanDeg = 2f * half;
            float cfg = Mathf.Clamp(settings.renderScale, 0.1f, 1f);
            float target = AuroraConfigs.Global.distantAutoScale ? ScaledAuroraInstance.BudgetScale(cfg, 1f, renderer.renderScale, cam, spanDeg) : cfg;
            if (Mathf.Abs(target - renderer.renderScale) > 0.001f) renderer.renderScale = target;
        }

        static Transform FindPlanet(out string name)
        {
            name = "";
            var names = new List<string>(AuroraConfigs.Bodies);
            if (!names.Contains("Kerbin")) names.Add("Kerbin");
            var scaled = new HashSet<GameObject>();
            if (FlightGlobals.Bodies != null)
                foreach (var cb in FlightGlobals.Bodies)
                    if (cb != null && cb.scaledBody != null) scaled.Add(cb.scaledBody);

            Transform best = null; float bestR = 0f;
            foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                if (scaled.Contains(mr.gameObject)) continue;
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string match = null;
                foreach (var n in names)
                    if (string.Equals(mr.name, n, System.StringComparison.OrdinalIgnoreCase)) { match = n; break; }
                if (match == null) continue;
                float r = mf.sharedMesh.bounds.extents.magnitude * mr.transform.lossyScale.x;
                if (best == null || r > bestR) { best = mr.transform; bestR = r; name = match; }
            }
            return best;
        }

        static Camera FindCamera(Transform planet)
        {
            int mask = 1 << planet.gameObject.layer;
            Camera best = null; float bestArea = 0f;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.enabled || (c.cullingMask & mask) == 0) continue;
                if (c.targetTexture != null) continue;
                float area = c.pixelWidth * c.pixelHeight;
                if (c == Camera.main) area *= 4f;
                if (best == null || area > bestArea) { best = c; bestArea = area; }
            }
            return best;
        }

        static Light FindSun()
        {
            Light best = null;
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l == null || !l.enabled || l.type != LightType.Directional || !l.gameObject.activeInHierarchy) continue;
                if (best == null || l.intensity > best.intensity) best = l;
            }
            return best;
        }

        static void DumpScene()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mr == null || !mr.gameObject.activeInHierarchy) continue;
                var t = mr.transform;
                sb.AppendLine($"  {t.name} (parent {(t.parent != null ? t.parent.name : "-")}, layer {t.gameObject.layer}, scale {t.lossyScale.x:F2})");
            }
            foreach (var c in Camera.allCameras)
                if (c != null) sb.AppendLine($"  camera {c.name} (enabled {c.enabled}, mask {c.cullingMask}, far {c.farClipPlane:F0})");
            Debug.Log(sb.ToString());
        }

        public string Status => gaveUp ? "off" : !built ? "waiting" : $"{bodyName} on '{planet.name}', {spanDeg:F1} deg across, {renderer.Status}";

        void Shutdown()
        {
            if (renderer != null) renderer.renderingEnabled = false;
            if (go != null) { Destroy(go); go = null; }
            if (material != null) { Destroy(material); material = null; }
            renderer = null; storm = null; meander = null;
        }

        void OnDestroy() { Shutdown(); }

        static void Log(string m) => Debug.Log($"{LogPrefix} {m}");
    }
}
