# Realistic Volumetric Aurorae (RVA) — build kit, v1.1.0

Volumetric polar aurorae for Kerbin, Jool and Laythe in KSP 1.12.x: a raymarched glow layer
rendered at reduced resolution and composited onto the flight camera, with
meandering, folding ribbons, rays, a storm cycle that grows the oval from one
arc to three, per-pixel occlusion by EVE's volumetric clouds if installed, depth occlusion
by the vessel and nearby terrain, and a map-view / tracking-station instance.
Since 1.1.0 it lights the vessel (and only the vessel: up to four lights from where the aurora's light comes from); the ground and the clouds it leaves alone.

**Standalone** RVA depends on nothing but KSP. Two optional extras:

- **EVE with Blackrack's volumetric clouds** when its raymarched-clouds
  renderer is present, the aurora beyond the cloud tops is occluded per pixel
  by the clouds' own transmittance (EVE's global `colorBuffer`). Without it the
  occlusion switches itself off and the aurora simply draws.
- **ModuleManager + SPVE** `Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg`
  removes SPVE's own Kerbin aurora layers so RVA replaces them, and
  `RVA_SPVE_ambience_lightning_fix.cfg` is an unrelated optional fix for SPVE's
  underwater ambience lightning. Both are inert without SPVE.

Scatterer is neither required nor affected; the plugin follows whichever
camera renders the far scene (two-camera or unified mode).

**Licence:** CC BY-NC-SA 4.0 — see `LICENSE.txt`.

```
Unity/Assets/            ← drop into the Unity 2019.4.18f1 project
  Shaders/Aurora_Shader.shader, AuroraBlit.shader  (+ .meta files, both tagged for the bundle "rva_shaders")
  Materials/AuroraMat.mat      (pre-tuned baseline, test scene only)
  Scripts/AuroraStormScheduler.cs, AuroraNoise.cs, AuroraMeanderTexture.cs   (shared with the plugin; test scene)
  Editor/BuildRVABundle.cs     (menu: RVA > Build Shader Bundle)
Plugin/                  ← open RealisticVolumetricAurorae.csproj in Visual Studio (configurations Release and Dev)
  src/   8 files: RVABootstrap (bootstrap + flight / tracking setups + scaled driver), AuroraRenderer, AuroraSettings,
         AuroraTuningWindow, AuroraDiagnostics, AuroraNoise, AuroraMeanderTexture, AuroraStormScheduler
  refs/  copy 7 KSP DLLs here (see the txt inside)
GameData/RealisticVolumetricAurorae/   ← copy into KSP's GameData once bundle + DLL are built
  Bundles/rva_shaders                  (built in Unity, below)
  Plugins/RealisticVolumetricAurorae.dll   (built in Visual Studio, below)
  PluginData/settings.cfg              (the 27 user settings; the in-game window edits/saves it)
  Compatibility/RVA_SPVE_*.cfg         (optional ModuleManager patches, see above)
  RealisticVolumetricAurorae.version   (KSP-AVC / CKAN version file)
  LICENSE.txt                          (CC BY-NC-SA 4.0 notice + legal code; ships with the mod)
LICENSE.txt              ← the same, for the repository
Dev/settings.cfg         ← all 74 settings, for a Dev-build install (same PluginData path)
Release/                 ← packaging script, release guide, forum / SpaceDock text, CKAN netkan
```

## Building from source

**Shader bundle (Unity 2019.4.18f1 — the version KSP 1.12 is built on).**
Create an empty project, drop `Unity/Assets/` into it, check that both shaders
in `Assets/Shaders/` show **AssetBundle: rva_shaders** at the bottom of the
Inspector, then run the menu **RVA → Build Shader Bundle**. The bundle appears
as `<project>/AssetBundles/rva_shaders` (no extension); copy it to
`GameData/RealisticVolumetricAurorae/Bundles/`.

**Plugin (Visual Studio 2019 or later, .NET Framework 4.8 targeting pack).**
Copy the seven KSP assemblies named in `Plugin/refs/COPY_THESE_DLLS_HERE.txt`
from `KSP_x64_Data/Managed/` into `Plugin/refs/` (they are referenced with
Copy Local off and never shipped), open `Plugin/RealisticVolumetricAurorae.csproj`
and build the **Release** configuration. The result,
`Plugin/bin/Release/RealisticVolumetricAurorae.dll`, goes into
`GameData/RealisticVolumetricAurorae/Plugins/`. The **Dev** configuration
builds the same DLL with every setting exposed in the in-game window.

**Install:** copy `GameData/RealisticVolumetricAurorae/` into KSP's `GameData`.
The plugin writes `PluginData/settings.cfg` on first launch if it is missing.

## Licensing

Realistic Volumetric Aurorae (RVA) © 2026 HereComesTheBoom, released under
**CC BY-NC-SA 4.0** (Creative Commons Attribution-NonCommercial-ShareAlike
4.0 International). You may share and adapt it for non-commercial purposes
with credit, and derivative works must carry the same licence. The full legal
code is in `LICENSE.txt`. Kerbal Space Program and the assemblies in
`Plugin/refs/` belong to their respective owners and are not part of this
licence.
