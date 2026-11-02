# Realistic Volumetric Aurorae 1.1.0

Jool and Laythe get aurorae and the plugin goes multi body. KSP 1.12.x. Delete the old
`GameData/RealisticVolumetricAurorae` folder before installing or let CKAN / AVC do it. Your
`PluginData/settings.cfg` is left alone but it predates the new storm cadence, if you have one
delete its `averageQuietDuration` lines (or hit def on that row per body and Save) to get the
45 minute storms.

## New

- **Jool**: gas giant aurora in hydrogen pinks, violets and blues, about 100 to 370 km above
  the cloud deck, tight around the poles, almost always on. Best from Jool orbit and from
  Laythe's surface where it sits on Jools limb.
- **Laythe**: oxygen aurora in greens, blues and red tops, 20 to 55 km up around 52 deg,
  rarely quiet inside Jool's magnetosphere. Its **storms reach the equator**, at peak the arcs
  fan out from the quiet oval all the way down to 12 deg and draw back as the storm fades.
- **Eclipses**: while Laythe is in Jool's shadow (about an hour per orbit) the daylight fade
  is lifted and the aurora shows over the whole moon.
- **Any body can get an aurora** with an `RVA_AURORA { body = X ... }` node (copy
  `Config/Kerbin.cfg`). Planet packs can ship their own and MM can patch them. Body selector
  in the window, every body has its own settings, buffers and steps.
- **The aurora lights your vessel** (on by default), up to four lights from the directions
  the light actually comes from, in its colour, fading with daylight and rising with storms.
  Vessel only, not the ground or clouds. Per body you can pick off / one / two / four lights
  and per pixel or per vertex.
- **Storms are rare**, 45 min of real time apart on average, never less than 18, counted in
  real time so physics warp doesnt hurry them and pause stops the clock. The sequence lasts
  the whole session so a revert or the tracking station doesnt restart the wait. Window
  counts down to the next one, Storm now button.
- **Performance**: frames are accumulated over time now (each samples a different sub pixel
  and different depths, history reprojected as the camera moves) so the grain is gone and
  **0.25 render scale looks like 0.35 used to**. Default is 0.25. A fine latitude gate skips
  about half the near miss samples for free. Other bodies render at the scale their pixel
  budget allows and are culled while they span under half a degree.
- Same look at every latitude (noise sampled on the sphere itself, each hemisphere has its
  own arcs) and the look is retuned on the new renderer.

## Fixed

- Horizontal stripes across the far oval from an equatorial orbit or the map view.
- Fine diagonal stripes on under sampled bands (sample offset is blue noise now).
- Another bodys aurora showing through the body the map is focused on.
- The seam along the bands lower horizon and the concentric rings inside Jool's shell (step
  grows with distance from the camera now).
- The bundle carries a build tag the plugin checks. Mismatched dll and bundle used to fail
  silently, now KSP.log and the window say so. Look for `Shader build 1110 matches the plugin`.
- Tuning window, explanations dont run under the sliders anymore.

## Still true

- Ovals on the rotation poles, no magnetic tilt yet.
- Map view / tracking station instance has no cloud occlusion.
- Settings are global, not per save.

Full details in `Release/CHANGELOG.md`. License CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
