# Realistic Volumetric Aurorae (RVA)

Volumetric aurorae for Kerbal Space Program 1.12.x. Kerbin, Jool, Laythe, Duna and Eve
out of the box.

Its not a texture, its a raymarched glow layer drawn around the planet every frame. Thin
folding ribbons that curl back on themselves, rays that taper off at the top, green at the
bottom and red up top (on Kerbin anyway, the other bodies have their own colours), three
arcs that make up the oval. Most of the time its quiet. Every now and then a storm comes
through, the oval widens and slides toward the equator, the outer arcs fade in, and then it
calms back down. From the ground the curtains fill the sky, from orbit the oval sits on the
night side and follows the terminator around. Works in flight, map view and the tracking
station, and on the title screen. Terrain and your vessel hide it where there in front of it, and if you have EVE
with volumetric clouds the clouds hide it too, per pixel, using there own transmittance.

## Install

1. Delete any old `GameData/RealisticVolumetricAurorae` folder (or the really old
   `GameData/SPVE_AuroraGlow` one).
2. Copy `GameData/RealisticVolumetricAurorae` from the zip into your GameData.

Thats it. If you see no aurora at all, check KSP.log for lines starting with `[RVA]`. A
missing file message there (and a popup at the main menu) means the folder didnt get copied
whole. `Bundles/rva_shaders` has to be a file sitting directly in `Bundles/`, not in a
subfolder.

```
GameData/RealisticVolumetricAurorae/
  Bundles/rva_shaders                       shaders
  Plugins/RealisticVolumetricAurorae.dll    the plugin
  PluginData/settings.cfg                   your settings. made on first launch, updates never touch it
  PluginData/settings-defaults.cfg          reference copy of the shipped values, not read by the mod
  PluginData/terrain/                       terrain height maps for the stock bodies (1 MB each). other bodies build theirs on first visit
  Config/*.cfg                              which bodies get an aurora + each ones preset (RVA_AURORA nodes)
  Compatibility/RVA_SPVE_*.cfg              optional, need ModuleManager and SPVE
  RealisticVolumetricAurorae.version        KSP-AVC
  LICENSE.txt
```

## Bodies

**Kerbin** has the usual green and red oval. **Jool** gets a gas giant aurora, hydrogen pinks
and violets tight around the poles and pretty much always on. **Laythe** has an oxygen
aurora in greens and reds in a 20 to 55 km band, Jool's magnetosphere drives it so its
rarely quiet, its storms spread the oval all the way down to the equator, and while Laythe
is in Jool's shadow the aurora shows over the whole moon. **Duna** is a pale green with the
CO2 violet-blue from mid height up, dim when quiet. **Eve** is a thin bright sheet 60-85 km
up above the clouds, green base, blue-white haze, soft blue top.

Each body comes from an `RVA_AURORA` node in `Config/`. The *Body* row at the top of the
window picks which one the sliders edit, and that includes render scale and the sampling
settings since every body has its own buffers and materials. Only the hotkey, the map view
switch and the occlusion plumbing are shared. To add a body copy `Config/Kerbin.cfg`, change
`body = ...`, tune it in the window and paste the output of "Copy + log <body>'s values"
into the file. `enabled = false` in a node turns that body off.

Other bodies aurorae are drawn too, in the map view, the tracking station, and from a
vessel when the body is close enough to matter (Jool from Laythe, Laythe from Jool orbit).
A body that only fills part of the view renders at a higher scale for the same pixel budget
(up to 1.0 in flight, 0.6 in the map), and that goes for the body your at as well once your
high enough that its aurora doesnt fill the screen. The body your at hides them, mountains
included, at any altitude. Where KSP has the terrain loaded its the terrain, where it hasnt (camera
zoomed way out, or high enough that KSP shows the smooth distant model) the plugin uses a
model of the body it builds from the terrain height function. Maps for the stock bodies
ship in `PluginData/terrain/`. A planet pack body without one gets sampled once over about
20 seconds the first time your there with another bodys aurora in view (KSP.log says
`[RVA:Terrain]`), KSP's smooth model stands in untill then. Shipped maps get checked against
the terrain in your game and rebuilt if a pack changed that body. Deleting a map rebuilds it.

With SPVE and MM installed the `RVA_SPVE_remove_*` patches take SPVE's own aurora layers off
Kerbin, Jool, Laythe, Duna and Eve so the two mods dont double up. Delete a patch if you
want to keep SPVE's layer on that body.

## Storms

Storms are rare. On average one every **45 minutes of real time** per body
(`averageQuietDuration`, 2700 s, the slider is in minutes). The waits are random but never
shorter than 18 min so you dont get two back to back. Counted in real time, physics warp
doesnt speed them up and pausing stops the clock. Each body keeps its place in the sequence
for the whole game session so a revert, vessel switch or trip to the tracking station
doesnt push the next storm back. The first storm after starting the game can come at any
time. The status line in the window counts down to the next one, *Storm now* skips the wait,
and the manual intensity slider previews any storm level.

## Light on the vessel

On by default. The aurora lights your vessel with up to four lights, vessel layers only,
from the directions the light actually comes from: both sides overhead when your under the
band, from the horizon when the oval sits there, from below when your above it. Same base
colour as the aurora, fades in daylight, brightens with storms. Terrain, water and clouds
are not lit. *Light the vessel from the aurora* in the window (per body) turns it off. The
intensity slider is the sum of the lights directly under a quiet ribbon. Each light is per
pixel, which is a pass per part and light, so on a craft with hundreds of parts drop to two
lights or one, or use per-vertex. Per-vertex is free but can pulse on a craft whos own
lights compete for Unity's vertex light slots.

## If something looks wrong

The status line in the window (Alt+F8) shows the shader build. It should say
`shader build 1132`. A line in caps about a mismatch means the dll and the shader bundle
are from different versions, reinstall the zip so both get replaced. KSP.log has the same
message under `[RVA]`.

## Requirements / compatability

- KSP 1.12.x (built and tested on 1.12.5), Windows / DX11.
- No dependencies. Scatterer, EVE and ModuleManager are all optional.
- EVE with volumetric clouds: the aurora beyond the cloud tops is hidden by the clouds per
  pixel while EVE is rendering them (below their fade-out altitude). Above that EVE's flat
  2D layers take over and those occlude it too, as drawn. The cloud top per body is read
  from EVE's own configs so any cloud pack works without a setting. Without EVE the aurora
  just draws.
- SPVE + MM: the `RVA_SPVE_remove_*` patches remove SPVE's aurora layers on Kerbin, Jool,
  Laythe, Duna and Eve. The fourth patch is an unrelated fix for SPVE's underwater ambience
  lightning, delete it if you want SPVE's behaviour.
- Scatterer works in both camera modes.
- All the ovals sit on the rotation poles, no magnetic tilt yet.

## The tuning window

Alt+F8 in flight. Every slider applies instantly: brightness and exposure, the three colours
and where they sit in the band, ray density and fineness, altitude, latitude, ribbon width
and folds, animation speeds, time between storms, render scale, map view and main menu toggles, hotkey.
The **def** button on each row restores the shipped value. *Save to settings.cfg* keeps your
changes, *preset values* brings the shipped look back for that body. Hovering over the
window stops the camera from rotating. If you'd rather use a text editor,
`PluginData/settings.cfg` is written on first launch and `settings-defaults.cfg` next to it
has the shipped values. Updates (by hand or CKAN) never overwrite settings.cfg.

## Performance

The aurora renders into a buffer thats **Render scale** times the screen size and gets
composited onto the frame. That buffer size is the cost. Default is 0.25, the temporal
accumulation (below) rebuilds the detail. 0.2 is a third cheaper again, 0.35 is twice the
price. Storms cost more than quiet time (three arcs instead of one). The *Aurora render*
toggle at the top of the window is a quick A/B test of what the mod costs you.

**Temporal accumulation** (per body, on by default) averages frames into a history buffer
at twice the render resolution. Each frame samples a different sub pixel and different
depths and the history is reprojected as the camera moves, so the grain goes away and a
lower render scale converges on the same picture. When the camera moves a long way in one
frame (whipped round the vessel from far out, or the vessel racing along under warp) the
history gives way to the raw frame, so a fast move looks grainier for a moment instead of
smeared and settles as soon as the camera does. The *parallax limit* slider sets how soon it
gives way. Lower the history weight if you want the aurora to follow its own animation
more closely.

## Known limits

- Lights the vessel only, not the ground or the clouds.
- Ovals on the rotation poles, no magnetic tilt.
- The map view / tracking station / main menu instances have no cloud occlusion.
- Settings are global, not per save.

## License

CC BY-NC-SA 4.0. Copyright (c) 2026 HereComesTheBoom. Full text in LICENSE.txt, source on
GitHub (link in the forum thread).

KSP, EVE, Scatterer, SPVE and ModuleManager are not part of this and have there own licenses.
