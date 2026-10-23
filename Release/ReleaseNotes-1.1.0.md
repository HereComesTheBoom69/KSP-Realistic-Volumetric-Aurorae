# Realistic Volumetric Aurorae 1.1.0

**Jool and Laythe get aurorae, and the plugin becomes multi-body.** For KSP 1.12.x. Delete the
old `GameData/RealisticVolumetricAurorae` folder before installing, or let CKAN or KSP-AVC do
it. Your `PluginData/settings.cfg` is left alone, but it predates the new storm cadence. If
you have one, delete its `averageQuietDuration` lines (or press that row's *def* button per
body and *Save*) to get the 45-minute storms.

## New

- **Jool**: a gas giant's aurora in hydrogen pinks, violets and blues, about 100 to 370 km
  above the cloud deck, tight around the poles and almost always active. Best from Jool
  orbit, and from Laythe's surface, where it sits on Jool's limb.
- **Laythe**: an oxygen aurora in greens, blues and red tops, 20 to 55 km up around 52
  degrees latitude, rarely quiet inside Jool's magnetosphere. Its **storms reach the
  equator**: at the peak the arcs fan out from the quiet oval all the way down to 12
  degrees, and draw back as the storm fades.
- **Eclipses**: while Laythe is in Jool's shadow (about an hour per orbit) the daylight fade
  is lifted and the aurora shows over the whole moon.
- **Any body can be given an aurora** with an `RVA_AURORA { body = X ... }` node (copy
  `Config/Kerbin.cfg`). Planet packs can ship their own and ModuleManager can patch them.
  The tuning window has a body selector, and every body has its own settings, buffers and
  steps.
- **The aurora lights your vessel** (on by default): up to four lights from the directions
  the aurora's light actually comes from, in its colour, fading with daylight and rising with
  storms. The vessel only, not the ground or the clouds. Per body you can pick off, one, two
  or four lights, and per-pixel or per-vertex.
- **Storms are rare events**: 45 minutes of real time apart on average, never less than 18,
  counted in real time. Physics warp does not hurry them and a pause stops the clock. The
  sequence lasts the whole game session, so a revert or a trip to the tracking station does
  not restart the wait. The window counts down to the next one and has a *Storm now* button.
- **Performance**: the frames are now accumulated over time (each samples a different
  sub-pixel and different depths, and the history is reprojected as the camera moves), so the
  grain is gone and a **render scale of 0.25 looks like 0.35 used to**. The shipped default is
  0.25. A fine latitude gate skips about half the near-miss samples for free. Other bodies
  render at the scale their pixel budget allows, and are culled while they span less than half
  a degree of view.
- The aurora looks the same at every latitude (its noise is sampled on the sphere itself, and
  each hemisphere has its own arcs), and the look is retuned on the new renderer.

## Fixed

- Horizontal stripes across the far oval seen from an equatorial orbit or the map view.
- Fine diagonal stripes across under-sampled bands (the sample offset is now blue noise).
- Another body's aurora showing through the body the map is focused on.
- The seam along the band's lower horizon, and the concentric sample rings inside Jool's
  shell (the march step now grows with distance from the camera).
- The shader bundle carries a build tag the plugin checks. A DLL and a bundle from different
  versions used to fail silently. Now KSP.log and the window say so. Look for
  `Shader build 1110 matches the plugin`.
- Tuning window: explanations no longer run under the sliders and the def buttons.

## Still true

- The ovals sit on the rotation poles (no magnetic axis tilt yet).
- The map view and Tracking Station instance has no cloud occlusion.
- Settings are global, not per save.

Full details in `Release/CHANGELOG.md`. Licence CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
