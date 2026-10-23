# Realistic Volumetric Aurorae (RVA), build kit, v1.1.0

Volumetric polar aurorae for Kerbin, Jool and Laythe in KSP 1.12.x. A raymarched glow
layer rendered at reduced resolution and composited onto the flight camera, with
meandering, folding ribbons, rays, a storm cycle that grows the oval from one arc to
three, per-pixel occlusion by EVE's volumetric clouds if they are installed, depth
occlusion by the vessel and nearby terrain, and a map view and tracking station
instance. Since 1.1.0 it lights the vessel, and only the vessel: up to four lights from
where the aurora's light comes from. The ground and the clouds are left alone.

**Standalone.** RVA depends on nothing but KSP. Two optional extras:

- **EVE with Blackrack's volumetric clouds.** When its raymarched clouds renderer is
  present, the aurora beyond the cloud tops is hidden per pixel by the clouds' own
  transmittance (EVE's global `colorBuffer`). Without it the occlusion switches itself
  off and the aurora simply draws.
- **ModuleManager and SPVE.** `Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg` and
  `RVA_SPVE_remove_jool_laythe_aurora.cfg` remove SPVE's own aurora layers so RVA
  replaces them, and `RVA_SPVE_ambience_lightning_fix.cfg` is an unrelated optional fix
  for SPVE's underwater ambience lightning. All three do nothing without SPVE.

Scatterer is neither required nor affected. The plugin follows whichever camera renders
the far scene (two-camera or unified mode).

**Licence:** CC BY-NC-SA 4.0, see `LICENSE.txt`.

```
Unity/Assets/            drop into the Unity 2019.4.18f1 project
  Shaders/Aurora_Shader.shader, AuroraBlit.shader  (plus .meta files, both tagged for the bundle "rva_shaders")
  Materials/AuroraMat.mat      (pre-tuned baseline, test scene only)
  Scripts/AuroraStormScheduler.cs, AuroraNoise.cs, AuroraMeanderTexture.cs   (older copies for the test scene; the plugin's are current)
  Editor/BuildRVABundle.cs     (menu: RVA > Build Shader Bundle)
Plugin/                  open RealisticVolumetricAurorae.csproj in Visual Studio (configurations Release and Dev)
  src/   13 files: RVABootstrap (bootstrap, flight and tracking station setups, scaled driver), AuroraRenderer,
         AuroraSettings, AuroraConfigs, AuroraTuningWindow, AuroraDiagnostics, AuroraNoise, AuroraMeanderTexture,
         AuroraStormScheduler, AuroraVesselLight, AuroraEclipse, AuroraBlueNoise, ScaledAuroraInstance
  refs/  copy 7 KSP DLLs here (see the txt inside)
GameData/RealisticVolumetricAurorae/   copy into KSP's GameData once the bundle and the DLL are built
  Bundles/rva_shaders                  (built in Unity, see below)
  Plugins/RealisticVolumetricAurorae.dll   (built in Visual Studio, see below)
  PluginData/settings.cfg              (the user settings with comments; the in-game window edits and saves it)
  Config/Kerbin.cfg, Jool.cfg, Laythe.cfg  (which bodies get an aurora, and each one's preset)
  Compatibility/RVA_SPVE_*.cfg         (optional ModuleManager patches, see above)
  RealisticVolumetricAurorae.version   (KSP-AVC / CKAN version file)
  LICENSE.txt                          (CC BY-NC-SA 4.0 notice and legal code; ships with the mod)
LICENSE.txt              the same, for the repository
Dev/settings.cfg         every setting, for a Dev build install (same PluginData path)
Release/                 packaging script, release guide, forum and SpaceDock text, release notes, CKAN netkan
```

## Building from source

**Shader bundle (Unity 2019.4.18f1, the version KSP 1.12 is built on).** Create an empty
project, drop `Unity/Assets/` into it, check that both shaders in `Assets/Shaders/` show
**AssetBundle: rva_shaders** at the bottom of the Inspector, then run the menu
**RVA > Build Shader Bundle**. The bundle appears as `<project>/AssetBundles/rva_shaders`
(no extension). Copy it to `GameData/RealisticVolumetricAurorae/Bundles/`.

**Plugin (Visual Studio 2019 or later, .NET Framework 4.8 targeting pack).** Copy the
seven KSP assemblies named in `Plugin/refs/COPY_THESE_DLLS_HERE.txt` from
`KSP_x64_Data/Managed/` into `Plugin/refs/` (they are referenced with Copy Local off and
never shipped), open `Plugin/RealisticVolumetricAurorae.csproj` and build the **Release**
configuration. The result, `Plugin/bin/Release/RealisticVolumetricAurorae.dll`, goes into
`GameData/RealisticVolumetricAurorae/Plugins/`. The **Dev** configuration builds the same
DLL with every setting exposed in the in-game window.

The shader bundle carries a build tag that the plugin checks when it loads it. When a
uniform changes, bump `_RVAShaderBuild` in the shader and `ShaderBuild` in
`RVABootstrap.cs` together, and rebuild both.

**Install:** copy `GameData/RealisticVolumetricAurorae/` into KSP's `GameData`. The plugin
writes `PluginData/settings.cfg` on the first launch if it is missing.

## Licensing

Realistic Volumetric Aurorae (RVA) (c) 2026 HereComesTheBoom, released under
**CC BY-NC-SA 4.0** (Creative Commons Attribution-NonCommercial-ShareAlike 4.0
International). You may share and adapt it for non-commercial purposes with credit, and
derivative works must carry the same licence. The full legal code is in `LICENSE.txt`.
Kerbal Space Program and the assemblies in `Plugin/refs/` belong to their respective
owners and are not part of this licence.
