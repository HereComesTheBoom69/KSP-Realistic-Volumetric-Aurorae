# Realistic Volumetric Aurorae (RVA) — v1.1.0

Volumetric polar aurorae for Kerbin, Jool and Laythe, for Kerbal Space Program 1.12.x.

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

That is all. Nothing else is required. If you ever see no aurora, check
`KSP.log` for lines starting with `[RVA]`: a missing-file message there (and,
since 1.0.1, a dialog at the main menu) means the folder was not copied whole —
`Bundles/rva_shaders` must be a file sitting directly in `Bundles/`.

Layout:
```
GameData/RealisticVolumetricAurorae/
├── Bundles/rva_shaders                       the shaders
├── Plugins/RealisticVolumetricAurorae.dll     the plugin
├── PluginData/settings.cfg                    your settings — created on first launch; never touched by updates
├── PluginData/settings-defaults.cfg           reference copy of the shipped values (with comments); not read by the mod
├── Config/Kerbin.cfg, Jool.cfg, Laythe.cfg       which bodies get an aurora, and each one's preset (RVA_AURORA nodes)
├── Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg      optional: needs ModuleManager + SPVE
├── Compatibility/RVA_SPVE_remove_jool_laythe_aurora.cfg optional: needs ModuleManager + SPVE
├── Compatibility/RVA_SPVE_ambience_lightning_fix.cfg    optional: needs ModuleManager + SPVE
├── RealisticVolumetricAurorae.version         KSP-AVC version file
└── LICENSE.txt
```

## Bodies

1.1.0 ships aurorae for **Kerbin**, **Jool** (a gas giant's: hydrogen pinks and
violets, tight around the poles, almost always on) and **Laythe** (oxygen greens and
reds in a 30–70 km band, driven by Jool's magnetosphere so rarely quiet, and whose
storms spread the oval all the way down to the equator; while Laythe is in Jool's
shadow the aurora shows over the whole moon). Each comes from an
`RVA_AURORA` node in `Config/`; the window's *Body* row switches which one the
sliders edit — look, storm, and the render scale and sampling too, since each body has
its own buffers and materials (only the hotkey, the map-view and occlusion plumbing
are shared). To add a body, copy `Config/Kerbin.cfg`, change `body = …`, tune it in
the window and paste the "Copy + log <body>'s values" output into the file.
`enabled = false` in a node switches that body off. Other bodies' aurorae are drawn
too — in the map view, the tracking station, and from a vessel when the body is
close enough to matter (Jool from Laythe, Laythe from Jool orbit); a body that fills
only part of the view renders at a higher scale, up to 1.0, for the same pixel budget.

With SPVE installed and ModuleManager present, the two `RVA_SPVE_remove_*` patches
take SPVE's own aurora layers off Kerbin, Jool and Laythe so the two do not overlap;
delete a patch to keep SPVE's.

## Storms

A storm is a rare event: on average one every **45 minutes of real time** per body
(`averageQuietDuration`, 2700 s; the slider in the window is in minutes). The waits are
random — exponential, as real storm arrivals are — but never shorter than 18 minutes,
so two never come in quick succession, and they are counted in real time: physics warp
does not hurry one along, pausing the game stops the clock. Each body's place in its
sequence lasts the whole game session — a revert, a vessel switch, a trip to the tracking
station or the VAB does not push the next storm back — and the first storm after
starting the game can come at any time, as if the sequence had been running all along.
The window's status line counts down to the next one; *Storm now* ends the wait, and
the manual intensity slider previews any storm level.

## Light on the vessel

On by default: the aurora lights your vessel with four lights, on the vessel's layers
only, from the directions the aurora's light actually comes from — from either side
overhead when you are under the band, from the horizon when the oval sits there, from
below when you are above it — in the aurora's own base colour, fading with daylight and
brightening with storms as the aurora does. Terrain, water and clouds are not lit.
*Light the vessel from the aurora* in the window (per body) switches it off; the
intensity slider is the lights' sum directly under a quiet ribbon. Each light is drawn
per pixel, which is a pass per part and light: on a craft of hundreds of parts, drop to
two lights or one, or use the per-vertex option (free, but it can pulse on a craft whose
own lights compete for Unity's vertex-light slots).

## If something looks wrong

The window's status line (Alt+F8) shows the shader build. It must read
`shader build 1110`; a line in capitals about a mismatch means the plugin DLL and
the shader bundle in your install come from different versions — reinstall the
release zip so both are replaced. KSP.log carries the same message under `[RVA]`.

## Requirements and compatibility

- **KSP 1.12.x** (built and tested on 1.12.5). Windows / DirectX 11.
- **No dependencies.** Scatterer, EVE and ModuleManager are not required.
- **Optional — EVE with volumetric clouds:** the aurora beyond the cloud tops
  is occluded by the clouds per pixel. Without it the aurora simply draws.
- **Optional — SPVE + ModuleManager:** the two `RVA_SPVE_remove_*` patches in
  `Compatibility/` remove SPVE's own aurora layers on Kerbin, Jool and Laythe so
  RVA replaces them. Without SPVE they do nothing. The second patch is an unrelated fix for SPVE's underwater
  ambience lightning; delete it if you want SPVE's behaviour.
- Works with Scatterer in both camera modes.
- Kerbin, Jool and Laythe in this release; every oval is centred on the rotation poles (no magnetic tilt yet).

## The tuning window

Hold **Alt** and press **F8** in flight. Every slider applies instantly:
brightness and exposure, the three colours and where each lives in the band,
ray density and fineness, the band's altitude, latitude, ribbon width and
folds, animation speeds, the time between storms, the render scale, the
map-view toggle and the hotkey. Each row's **def** button restores the shipped value.
*Save to settings.cfg* keeps your changes; *All defaults* brings the shipped
look back. Hovering the window stops the camera from rotating. If you prefer a
text editor: `PluginData/settings.cfg` is written on the first launch (values
only); `settings-defaults.cfg` next to it has the same keys with comments.
Updates — by hand or through CKAN — never overwrite `settings.cfg`.

## Performance

The aurora renders into a buffer that is **Render scale** × the screen
(default 0.25 since 1.1.0, down from 0.35: the temporal accumulation below
rebuilds the detail) and is then composited; that buffer size is the cost.
0.2 is a third cheaper again, 0.35 twice the price, at the expense of fine
detail in the curtains. Storms cost more than quiet time (three arcs instead
of one). The *Aurora render* toggle at the top of the
window is an instant A/B of the mod's share of your frame time.

Since 1.1.0 the march skips its dearest noise for about half the samples that
turn out to miss the ribbon (no setting; the picture is unchanged), and
**temporal accumulation** (per body, on by default) averages successive frames —
each sampling a different sub-pixel and different depths — into a history
buffer at twice the render resolution, reprojected as the camera moves: the
grain goes, the detail comes back, and the same picture converges from a lower
render scale. Turn it off if you ever see the aurora trail behind a fast camera
whip; lower its weight to make it follow the animation more closely.

## Known limits

- The aurora lights the vessel only; the ground and the clouds are not lit.
- The ovals sit on the rotation poles (no magnetic-axis tilt yet).
- The map-view / Tracking Station instance has no cloud occlusion (scaled
  space has no cloud buffer).
- Settings are global, not per save.

## Licence

CC BY-NC-SA 4.0 — Creative Commons Attribution-NonCommercial-ShareAlike 4.0
International. Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric
Aurorae"). Full text in `LICENSE.txt`. Source code: see the forum thread.

KSP, EVE, Scatterer, SPVE and ModuleManager are not part of this work and
remain under their own licences.
