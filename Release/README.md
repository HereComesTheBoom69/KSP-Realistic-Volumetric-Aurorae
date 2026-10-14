# Realistic Volumetric Aurorae (RVA) — v1.0.0

A volumetric polar aurora for Kerbin, for Kerbal Space Program 1.12.x.

A raymarched glow layer drawn around the planet every frame.
Thin, folding ribbons that curl back on themselves, vertical rays that taper
to tips, green at the base and red at the top, three arcs that form the
auroral oval and move with a storm cycle. From the
ground the curtains fill the sky; from orbit the oval sits over the night side
and follows the terminator. The aurora is drawn in flight, in the map view and
in the Tracking Station, is hidden where terrain or your vessel is in front of
it, and if you run EVE with Blackrack's volumetric clouds, is occluded per
pixel by the clouds' own transmittance.

## Install

1. Delete any previous `GameData/RealisticVolumetricAurorae` (or the old
   `GameData/SPVE_AuroraGlow`) folder.
2. Copy the `GameData/RealisticVolumetricAurorae` folder from the zip into
   your KSP `GameData`.

That is all. Nothing else is required.

Layout:
```
GameData/RealisticVolumetricAurorae/
├── Bundles/rva_shaders                       the shaders
├── Plugins/RealisticVolumetricAurorae.dll     the plugin
├── PluginData/settings.cfg                    your settings — created on first launch; never touched by updates
├── PluginData/settings-defaults.cfg           reference copy of the shipped values (with comments); not read by the mod
├── Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg      optional: needs ModuleManager + SPVE
├── Compatibility/RVA_SPVE_ambience_lightning_fix.cfg    optional: needs ModuleManager + SPVE
├── RealisticVolumetricAurorae.version         KSP-AVC version file
└── LICENSE.txt
```

## Requirements and compatibility

- **KSP 1.12.x** (built and tested on 1.12.5). Windows / DirectX 11.
- **No dependencies.** Scatterer, EVE and ModuleManager are not required.
- **Optional — EVE with volumetric clouds:** the aurora beyond the cloud tops
  is occluded by the clouds per pixel. Without it the aurora simply draws.
- **Optional — SPVE + ModuleManager:** the patch in `Compatibility/` removes
  SPVE's own Kerbin aurora layers so RVA replaces them. Without SPVE the patch
  does nothing. The second patch is an unrelated fix for SPVE's underwater
  ambience lightning; delete it if you want SPVE's behaviour.
- Works with Scatterer in both camera modes.
- Kerbin only in this release.

## The tuning window

Hold **Alt** and press **F8** in flight. Every slider applies instantly:
brightness and exposure, the three colours and where each lives in the band,
ray density and fineness, the band's altitude, latitude, ribbon width and
folds, animation speeds, storm frequency, the render scale, the map-view
toggle and the hotkey. Each row's **def** button restores the shipped value.
*Save to settings.cfg* keeps your changes; *All defaults* brings the shipped
look back. Hovering the window stops the camera from rotating. If you prefer a
text editor: `PluginData/settings.cfg` is written on the first launch (values
only); `settings-defaults.cfg` next to it has the same keys with comments.
Updates — by hand or through CKAN — never overwrite `settings.cfg`.

## Performance

The aurora renders into a buffer that is **Render scale** × the screen
(default 0.35) and is then composited; that buffer size is the cost. On a
large display, 0.25 is roughly half the price of 0.35 and 0.2 a quarter, at the
expense of fine detail in the curtains. Storms cost more than quiet time
(three arcs instead of one). The *Aurora render* toggle at the top of the
window is an instant A/B of the mod's share of your frame time.

## Known limits

- The map-view / Tracking Station instance has no cloud occlusion (scaled
  space has no cloud buffer).
- Settings are global, not per save.

## Licence

CC BY-NC-SA 4.0 — Creative Commons Attribution-NonCommercial-ShareAlike 4.0
International. Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric
Aurorae"). Full text in `LICENSE.txt`. Source code: see the forum thread.

KSP, EVE, Scatterer, SPVE and ModuleManager are not part of this work and
remain under their own licences.
