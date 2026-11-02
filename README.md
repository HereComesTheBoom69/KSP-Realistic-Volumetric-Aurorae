# Realistic Volumetric Aurorae (RVA) - source / build kit

Volumetric aurorae for KSP 1.12.x. Raymarched glow layer around the planet, rendered at
a reduced res and composited onto the flight camera. Ribbons, rays, folds, a storm cycle,
per-pixel occlusion by EVE's clouds (volumetric and the flat 2D layers), depth occlusion by
the vessel and terrain, map view + tracking station instances, and it lights the vessel
(just the vessel, not the ground or clouds).

Ships presets for Kerbin, Jool, Laythe, Duna and Eve. Any body can get one with a cfg.

No hard dependancies, only KSP. Optional:

- EVE with Blackracks volumetric clouds. If its there the clouds hide the aurora per
  pixel using EVE's own cloud buffer (and the 2D layers are drawn into an opacity buffer
  of our own). If its not there the occlusion just turns itself off.
- ModuleManager + SPVE. The patches in `Compatibility/` remove SPVE's own aurora layers so
  the two dont overlap, plus one unrelated fix for SPVE's underwater ambience lightning.
  They do nothing without SPVE.

Scatterer isnt needed and isnt affected, the plugin just follows whatever camera renders
the far scene (two camera or unified mode, both work).

License: CC BY-NC-SA 4.0, see `LICENSE.txt`.

## Whats in here

```
Unity/Assets/           drop into an empty Unity 2019.4.18f1 project
  Shaders/              Aurora_Shader.shader, AuroraBlit.shader (+ .meta, both tagged for the bundle "rva_shaders")
  Materials/            AuroraMat.mat, test scene only
  Scripts/              old copies of the scheduler/noise/meander scripts for the test scene. the plugin's are the current ones
  Editor/               BuildRVABundle.cs -> menu RVA > Build Shader Bundle
Plugin/                 RealisticVolumetricAurorae.csproj, configs Release and Dev
  src/                  16 .cs files. RVABootstrap is the entry point (flight + tracking station setup, scaled driver),
                        AuroraRenderer does the actual rendering, AuroraSettings/AuroraConfigs/AuroraTuningWindow are
                        the settings side, AuroraCloudTops reads EVE's cloud configs, AuroraCloud2D grabs EVE's flat
                        layers, AuroraTerrainMap builds the height maps, ScaledAuroraInstance is the map view / other
                        bodies instance, the rest is noise, meander LUT, storms, vessel light, eclipses, blue noise, diag
  refs/                 put 7 KSP dlls in here, see the txt
GameData/RealisticVolumetricAurorae/    goes into KSP's GameData once the bundle and the dll are built
  Bundles/rva_shaders                   from Unity
  Plugins/RealisticVolumetricAurorae.dll   from VS
  PluginData/settings.cfg               user settings, the ingame window saves here
  PluginData/terrain/                   shipped terrain maps (.rvaterrain)
  Config/*.cfg                          one RVA_AURORA node per body
  Compatibility/RVA_SPVE_*.cfg          optional MM patches
  RealisticVolumetricAurorae.version    AVC / CKAN
  LICENSE.txt
Dev/settings.cfg        every setting incl the dev only ones, for a Dev build install
Release/                packaging script, release checklist, forum/spacedock text, changelog, netkan
```

## Building

**Shaders.** Unity 2019.4.18f1 (what KSP 1.12 is built on). Empty project, drop `Unity/Assets/`
in, check both shaders in Assets/Shaders say `AssetBundle: rva_shaders` at the bottom of the
inspector, then `RVA > Build Shader Bundle`. The bundle lands in `<project>/AssetBundles/rva_shaders`
(no extension). Copy it to `GameData/RealisticVolumetricAurorae/Bundles/`.

**Plugin.** VS 2019 or newer with the .NET Framework 4.8 targeting pack. Copy the seven dlls
listed in `Plugin/refs/COPY_THESE_DLLS_HERE.txt` from `KSP_x64_Data/Managed/` into `Plugin/refs/`
(Copy Local is off, they never ship). Open the csproj, build **Release**. Output is
`Plugin/bin/Release/RealisticVolumetricAurorae.dll`, put it in `GameData/RealisticVolumetricAurorae/Plugins/`.
The **Dev** config is the same dll with every setting exposed in the window.

The bundle carries a build tag and the plugin checks it on load. If you change a uniform
bump `_RVAShaderBuild` in the shader and `ShaderBuild` in RVABootstrap.cs together and
rebuild both, otherwise you get the mismatch warning and whatever you added is inert.

**Install:** copy `GameData/RealisticVolumetricAurorae/` into GameData. settings.cfg gets
written on first launch if its missing.

## License

(c) 2026 HereComesTheBoom, CC BY-NC-SA 4.0. Share and adapt for non commercial use with
credit, derivatives under the same license. Full text in LICENSE.txt. KSP and the dlls in
Plugin/refs belong to their owners and arent part of this.
