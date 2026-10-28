# Realistic Volumetric Aurorae (RVA) v1.1.1

Volumetric polar aurorae for Kerbin, Jool and Laythe, for Kerbal Space Program 1.12.x.

The aurora is a raymarched glow layer drawn around the planet every frame. You get
thin, folding ribbons that curl back on themselves, vertical rays that taper to a
tip, green at the base and red at the top, and three arcs that form the auroral oval
and move with a storm cycle. From the ground the curtains fill the sky. From orbit the
oval sits over the night side and follows the terminator. The aurora is drawn in
flight, in the map view and in the Tracking Station. Terrain and your vessel hide it
where they are in front of it, and if you run EVE with Blackrack's volumetric clouds,
the clouds hide it per pixel using their own transmittance.

## Install

1. Delete any previous `GameData/RealisticVolumetricAurorae` folder (or the old
   `GameData/SPVE_AuroraGlow` folder).
2. Copy the `GameData/RealisticVolumetricAurorae` folder from the zip into your KSP
   `GameData`.

That is all. If you ever see no aurora, look in `KSP.log` for lines starting with
`[RVA]`. A missing-file message there (and, since 1.0.1, a dialog at the main menu)
means the folder was not copied whole. `Bundles/rva_shaders` has to be a file sitting
directly in `Bundles/`.

Layout:
```
GameData/RealisticVolumetricAurorae/
  Bundles/rva_shaders                                   the shaders
  Plugins/RealisticVolumetricAurorae.dll                the plugin
  PluginData/settings.cfg                               your settings. Created on first launch, never touched by updates
  PluginData/settings-defaults.cfg                      reference copy of the shipped values (with comments), not read by the mod
  PluginData/terrain/                                   terrain height maps of the stock bodies (1 MB each); others build on first visit
  Config/Kerbin.cfg, Jool.cfg, Laythe.cfg               which bodies get an aurora and each one's preset (RVA_AURORA nodes)
  Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg       optional, needs ModuleManager and SPVE
  Compatibility/RVA_SPVE_remove_jool_laythe_aurora.cfg  optional, needs ModuleManager and SPVE
  Compatibility/RVA_SPVE_ambience_lightning_fix.cfg     optional, needs ModuleManager and SPVE
  RealisticVolumetricAurorae.version                    KSP-AVC version file
  LICENSE.txt
```

## Bodies

1.1.0 ships aurorae for three bodies. **Kerbin** has the familiar green and red oval.
**Jool** has a gas giant's aurora in hydrogen pinks and violets, tight around the
poles and almost always on. **Laythe** has an oxygen aurora in greens and reds in a
20 to 55 km band. Jool's magnetosphere drives it, so it is rarely quiet, its storms
spread the oval all the way down to the equator, and while Laythe is in Jool's shadow
the aurora shows over the whole moon.

Each body comes from an `RVA_AURORA` node in `Config/`. The *Body* row in the window
picks which one the sliders edit, and that includes the render scale and the sampling
settings, because each body has its own buffers and materials. Only the hotkey, the
map view switch and the occlusion plumbing are shared. To add a body, copy
`Config/Kerbin.cfg`, change `body = ...`, tune it in the window and paste the output
of "Copy + log <body>'s values" into the file. `enabled = false` in a node switches
that body off.

Other bodies' aurorae are drawn too: in the map view, in the tracking station, and
from a vessel when the body is close enough to matter (Jool from Laythe, Laythe from
Jool orbit). A body that fills only part of the view renders at a higher scale, up to
1.0, for the same pixel budget. The body you are at hides them, mountains included, at
any altitude: where KSP has its terrain loaded, by that terrain; where it has not (the
camera zoomed far out above the vessel, or high enough that KSP shows the body's smooth
distant model), by a model of the body the plugin builds from the terrain's own height
function. The maps for the stock bodies ship in `PluginData/terrain/`; a body without one
(a planet pack's) is sampled once over about twenty seconds the first time you are there
with another body's aurora in view (KSP.log says `[RVA:Terrain]`), and KSP's smooth model
stands in until then. A shipped map is checked against the terrain in your game and
rebuilt if a planet pack has changed that body. Deleting a map rebuilds it too.

With SPVE and ModuleManager installed, the two `RVA_SPVE_remove_*` patches take
SPVE's own aurora layers off Kerbin, Jool and Laythe so the two mods do not overlap.
Delete a patch if you want to keep SPVE's layer on that body.

## Storms

A storm is a rare event. On average there is one every **45 minutes of real time** per
body (`averageQuietDuration`, 2700 s; the slider in the window is in minutes). The
waits are random, like real storm arrivals, but never shorter than 18 minutes, so two
never come in quick succession. They are counted in real time: physics warp does not
hurry a storm along, and pausing the game stops the clock. Each body keeps its place in
the sequence for the whole game session, so a revert, a vessel switch or a trip to the
tracking station or the VAB does not push the next storm back. The first storm after
starting the game can come at any time, as if the sequence had been running all along.
The window's status line counts down to the next one, *Storm now* ends the wait, and
the manual intensity slider previews any storm level.

## Light on the vessel

On by default. The aurora lights your vessel with four lights, on the vessel's layers
only, from the directions the aurora's light actually comes from: from either side
overhead when you are under the band, from the horizon when the oval sits there, from
below when you are above it. The light has the aurora's own base colour, fades with
daylight and brightens with storms, like the aurora does. Terrain, water and clouds
are not lit. *Light the vessel from the aurora* in the window (per body) switches it
off. The intensity slider is the sum of the lights directly under a quiet ribbon. Each
light is drawn per pixel, which costs a pass per part and light, so on a craft of
hundreds of parts drop to two lights or one, or use the per-vertex option. Per-vertex
is free, but it can pulse on a craft whose own lights compete for Unity's vertex light
slots.

## If something looks wrong

The window's status line (Alt+F8) shows the shader build. It has to read
`shader build 1116`. A line in capitals about a mismatch means the plugin DLL and the
shader bundle in your install come from different versions. Reinstall the release zip
so both are replaced. KSP.log carries the same message under `[RVA]`.

## Requirements and compatibility

- **KSP 1.12.x** (built and tested on 1.12.5). Windows / DirectX 11.
- **No dependencies.** Scatterer, EVE and ModuleManager are not required.
- **Optional: EVE with volumetric clouds.** The aurora beyond the cloud tops is hidden
  by the clouds per pixel. Without EVE the aurora simply draws.
- **Optional: SPVE and ModuleManager.** The two `RVA_SPVE_remove_*` patches in
  `Compatibility/` remove SPVE's own aurora layers on Kerbin, Jool and Laythe so RVA
  replaces them. Without SPVE they do nothing. The third patch is an unrelated fix for
  SPVE's underwater ambience lightning. Delete it if you want SPVE's behaviour.
- Works with Scatterer in both camera modes.
- Kerbin, Jool and Laythe in this release. Every oval is centred on the rotation poles
  (no magnetic tilt yet).

## The tuning window

Hold **Alt** and press **F8** in flight. Every slider applies instantly: brightness and
exposure, the three colours and where each one sits in the band, ray density and
fineness, the band's altitude, latitude, ribbon width and folds, animation speeds, the
time between storms, the render scale, the map view toggle and the hotkey. The **def**
button on each row restores the shipped value. *Save to settings.cfg* keeps your
changes and *All defaults* brings the shipped look back. Hovering over the window stops
the camera from rotating. If you prefer a text editor, `PluginData/settings.cfg` is
written on the first launch (values only) and `settings-defaults.cfg` next to it has
the same keys with comments. Updates, by hand or through CKAN, never overwrite
`settings.cfg`.

## Performance

The aurora renders into a buffer that is **Render scale** times the screen size and is
then composited onto the frame. That buffer size is the cost. The default is 0.25 since
1.1.0, down from 0.35, because the temporal accumulation described below rebuilds the
detail. 0.2 is a third cheaper again and 0.35 is twice the price, at the expense of
fine detail in the curtains. Storms cost more than quiet time (three arcs instead of
one). The *Aurora render* toggle at the top of the window is an instant A/B test of
the mod's share of your frame time.

Since 1.1.0 the march skips its most expensive noise for about half the samples that
turn out to miss the ribbon (no setting, and the picture is unchanged). **Temporal
accumulation** (per body, on by default) averages successive frames into a history
buffer at twice the render resolution. Each frame samples a different sub-pixel and
different depths, and the history is reprojected as the camera moves, so the grain
goes away, the detail comes back, and a lower render scale converges on the same
picture. Turn it off if you ever see the aurora trail behind a fast camera whip, or
lower its weight to make it follow the animation more closely.

## Known limits

- The aurora lights the vessel only. The ground and the clouds are not lit.
- The ovals sit on the rotation poles (no magnetic axis tilt yet).
- The map view and Tracking Station instance has no cloud occlusion (scaled space has
  no cloud buffer).
- Settings are global, not per save.

## Licence

CC BY-NC-SA 4.0 (Creative Commons Attribution-NonCommercial-ShareAlike 4.0
International). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae").
Full text in `LICENSE.txt`. Source code: see the forum thread.

KSP, EVE, Scatterer, SPVE and ModuleManager are not part of this work and remain under
their own licences.
