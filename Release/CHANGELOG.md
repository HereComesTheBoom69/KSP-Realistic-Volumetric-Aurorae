# Changelog

## 1.1.1 (2026-10-28)

**Another body's aurora now hides behind the terrain of the body you are at, at every
altitude and zoom.** Kerbin's from the Mun, Jool's from Laythe and the other moons.

### Fixed

- Another body's aurora seen through the ground of the body you are at, just below its
  horizon (Kerbin's from Mun orbit). Three causes, all fixed:
  - The depth test counted a sample as that body's ground only within 2 % of the datum
    sphere, and the Mun's highlands on the limb stand higher than that. A sample now counts
    as ground when it lands anywhere in the body's terrain shell, from a little under the
    datum to above its highest point, which the plugin finds by sampling the terrain once
    per session.
  - With the camera zoomed far out, or from a high orbit, KSP unloads the body's terrain
    and shows its smooth distant model instead, which is drawn by another camera and never
    reaches the depth buffer, so there was nothing left to test against but the datum. The
    plugin now builds its own model of the body from the terrain's height function, which
    never switches off: a 1024 x 512 height map of the body pushes out a dense sphere that
    is drawn into a depth buffer of its own, and the cut follows the real highlands from
    any distance. The maps of the stock bodies that need one ship in `PluginData/terrain/`
    (1 MB each); any other body's is built the first time you are there with another
    body's aurora in view, about twenty seconds with an FPS dip, and kept. A shipped map
    is checked against the terrain in your game and rebuilt if a planet pack has changed
    that body.
  - The sphere and the model were anchored on the body's local transform, which KSP
    leaves behind on the orbit while the terrain is unloaded. They follow the body's
    orbital position now, and so does the aurora's own centre.
- Another body's aurora fading out or vanishing in parts of the screen around an airless
  body (Kerbin's from Mun orbit, with nothing in front of it). The per-pixel cloud
  occlusion multiplies the aurora by EVE's cloud buffer, and around a body where EVE draws
  no volumetric clouds that buffer keeps whatever it last held. The cloud occlusion is now
  used only while the body you are at has an atmosphere.
- Shader build tag 1116. A DLL and a bundle from different builds say so in KSP.log and
  in the window, as before.

### Dev build

- Three more views on the shader debug row: an occlusion probe (what each pixel's depth
  sample was taken for), a depth probe (how far the depth sample says the scene is) and a
  model probe (the body model as the plugin draws it). A depth readout that logs what the
  buffers hold under the mouse once a second. The body's status line names the ground test
  in use and how far a terrain map has got.

## 1.1.0 (2026-10-22)

**Jool and Laythe get aurorae, and the plugin becomes multi-body.**

### New

- Every body with an `RVA_AURORA { body = X ... }` node gets an aurora with its own
  settings. The mod ships `Config/Kerbin.cfg`, `Config/Jool.cfg` and `Config/Laythe.cfg`.
  Jool's is a gas giant aurora in hydrogen pinks, violets and blues, about 100 to 370 km
  above the cloud deck, tight around the poles and almost always active. It is best seen
  from Jool orbit and from Laythe's surface, where it sits on Jool's limb. Laythe's is an
  oxygen aurora in greens, blues and red tops, 20 to 55 km up around 52 degrees latitude.
  Jool's magnetosphere drives it, so it is rarely quiet.
- Storms that reach the equator (Laythe). At the peak of a storm the oval spreads from its
  quiet latitude all the way down to the equator: the poleward arc stays, the main arc
  moves to the middle, the equatorward arc goes to the equator itself and the folds grow
  to fill the gaps. It all draws back as the storm fades. Per-body settings
  `stormToEquator`, `stormReach` and `stormFoldGain`. Kerbin and Jool keep their ordinary
  storms.
- Eclipses. While a body's parent hides the sun (Laythe is in Jool's shadow for about an
  hour every orbit) the daylight fade is lifted and the aurora shows over the whole moon,
  with a short penumbral ramp. Global toggle `eclipses` in the window.
- The aurora lights your vessel again (`vesselLight`, on by default with four lights;
  also `vesselLightIntensity` and `vesselLightCount`). The v3.x ring of ninety point
  lights lit the terrain, the water and the vessel, and went away in v3.23. This is the
  part the vessel saw, done as up to four lights that reach the vessel's layers only, not
  the terrain, the water or the clouds. The meander lookup the shader reads now also
  carries where each ribbon is in every longitude column (a presence-weighted mean of its
  folded latitude, and how much ribbon the column holds, baked on its worker thread). Each
  frame the ribbons are traced from it on a grid anchored at the vessel, and every piece
  of ribbon above the vessel's horizon adds light by its brightness and the inverse square
  of its distance. The sky is split softly into four quarters around the vessel and each
  quarter's light becomes one light from its mean direction. A band overhead lights the
  hull from two or four directions at roughly 45 degrees, the way a band across the sky
  does; a band on the horizon lights it from there; a band below you lights it from
  below. The light fades with daylight and follows the storm's arcs like the aurora does,
  with a quarter-second lag. It is per pixel (a pass per part and light, so normal maps
  and speculars respond). The window's per-vertex option is free but shares Unity's four
  vertex light slots per object with the craft's own lights, and can pulse.
- Storms are rare events. The average time between them is 45 minutes of real time on
  every body (`averageQuietDuration` 2700 s, up from 30 to 90 s; the window's slider is
  in minutes). The waits are still exponential but with a floor of 40 percent of the
  mean, so two never come in quick succession. The clock is real time less pauses, so
  physics warp no longer hurries a storm along and a storm cannot jump while the game is
  paused. Each body's place in its sequence now lasts the whole game session instead of
  one scene, so a revert, a vessel switch, the tracking station or the VAB no longer
  restart the wait. (Before, every scene began a fresh wait from the same per-save seed,
  so a save's first storm always fell at the same minute and a short flight never saw
  one.) The first storm after starting the game falls as it would in a sequence that had
  been running all along: sometimes minutes away, 31 minutes on average. Changing the
  average while a wait runs keeps the fraction of it that has passed, so a shorter
  setting takes effect at once. The window's status line counts down to the next storm,
  and a *Storm now* button ends the wait.
- In flight the local aurora follows the vessel's main body (it is rebuilt on an SOI
  change and on a vessel switch). The other configured bodies render in scaled space, in
  flight, in the map view and in the tracking station, and are culled while their band
  spans less than half a degree of view, so an aurora around a distant dot costs nothing.
  A distant body renders at the scale that keeps its pixel budget what its own render
  scale would spend filling the screen, up to 1.0. Jool from Laythe orbit is sharp for the
  price of a 0.25 Kerbin, and the same setting still means 0.25 once you are in Jool
  orbit (`distantAutoScale`, on by default).
- Another body's aurora seen from a vessel (Jool from Laythe) is drawn on the flight
  camera like the local one, so terrain, the vessel and EVE's clouds hide it properly,
  with an exact analytic occluder for the body you are at. The scaled-space copy is used
  in the map view and the tracking station, and is now depth-occluded by other scaled
  bodies.
- The tuning window has a body selector. Each body's sliders, "def" buttons and preset
  reset are its own, including the render scale and the sampling settings, since each
  body has its own buffers and materials. Global settings (hotkey, map view, occlusion
  plumbing) are shared. `settings.cfg` keeps its 1.0.x layout for Kerbin and the globals,
  with a `BODY { name = Jool ... }` node per other body. Old files load unchanged.
- Adding a body: copy `Config/Kerbin.cfg`, change `body`, tune it in the window and paste
  the output of "Copy + log <body>'s values" into the file. Planet packs can ship their
  own nodes and ModuleManager can patch them. `enabled = false` switches a body off.
- With SPVE installed, a new optional patch removes SPVE's own aurora layers on Jool and
  Laythe (every EVE cloud object on those bodies with "aurora" in its name), as the
  Kerbin patch already did. Delete `Compatibility/RVA_SPVE_remove_jool_laythe_aurora.cfg`
  to keep them.
- Dev build: a "Write Config/<body>.cfg" button writes the edited body's values as its
  shipped preset. The user-facing Save still goes to settings.cfg.

### Performance

Three changes, each per body. The window's "Aurora render" A/B toggle and FPS readout
measure them. Shader build tag 1110.

- Fine latitude gate (always on, and the picture is unchanged). Before a sample pays for
  the fold noise, the most expensive thing a near-miss sample evaluates, the march used
  to test it only against each arc's nominal latitude plus or minus the meander's whole
  range (0.225), the folds and the width, which comes to about 30 degrees either side at
  the shipped folds. The meander shift for the sample's longitude is in a lookup the band
  reads anyway, so the test is now made against the shifted centre with only the folds
  and this longitude's width as margin. About half the near-miss samples stop there. A
  coverage map baked per frame over longitude and latitude was built and measured first.
  The ribbon's distance measure is not smooth enough across a texel for the map to be
  conservative at any affordable size, and its bake would have cost most of what it
  saved, so it was dropped in favour of the gate.
- Temporal accumulation (on by default; `temporalAccumulation`, `temporalWeight`). Each
  frame the rays are tilted by a sub-pixel offset and the sample positions along them
  shift, and the frames are averaged in a pair of history buffers at twice the render
  resolution, reprojected through the camera's motion relative to the planet. The grain
  of the sample jitter averages away, the picture is reconstructed at the history's
  resolution rather than the render buffers', and the same converged picture now comes
  from a lower render scale or coarser steps. History is rejected where the vessel or
  terrain newly covers or uncovers the aurora and outside last frame's view, and it is
  clamped to the current frame's neighbourhood so the animation cannot ghost. Dev
  settings: `temporalUpscale` (history size, 2x by default) and `temporalClamp` (the
  standard deviations of that clamp).
- Per-body step tuning. The sampling settings became per body earlier in this release.
  The presets carry each body's own steps (Jool `stepLength` 0.001, Laythe 0.0041 with
  `stepFarMul` 1.08), and a new dev setting `stepFilter` sets the width the noise is
  filtered to as a fraction of the step (3/4 used to be a constant). With accumulation
  on, a smaller value keeps finer rays at coarser steps, because the accumulation
  averages out the single-frame aliasing that filter used to prevent.
- Render scale 0.25 on every body (down from 0.35). The temporal accumulation rebuilds
  the detail.

### Look

- The aurora now looks the same at every latitude. Its noise used to be sampled at the
  projection of the surface point onto the equatorial plane, which squeezed the poleward
  direction by sin(latitude): a 10 to 20 percent stretch at the ovals that nobody
  noticed, and a tearing stretch near the equator once a storm got there. Folds, rays,
  spikes and green-ray regions are now sampled on the sphere itself. Latitude is signed
  too: each hemisphere has its own three arcs, each a smooth curve that may cross the
  equator, instead of one set mirrored into both hemispheres and meeting itself in a
  cusp. The patterns at Kerbin's and Jool's ovals are therefore new realisations of the
  same noise. Their scale, contrast and motion are unchanged.
- Ray march: the step size is now a function of distance from the camera (fine up close,
  growing with range) instead of one step spread over each ray's whole path through the
  shell. That removes the seam along the band's lower horizon, where neighbouring rays
  sampled the same aurora at very different densities, and the concentric sample rings
  visible from inside Jool's 600 km shell. New dev settings `stepGrowth` and
  `stepFarMul`; `maxSteps` is a safety cap, default 192.
- The shipped look is retuned on the 1.1.0 renderer. The window's save of 2026-10-22 is
  the new baseline, for the built-in defaults and the presets alike. Kerbin's band starts
  at 20 km instead of 30 and is brighter, with a quieter base and milder storms
  (`baseActivity` 0.15, `stormActivity` 0.25), and a near-constant 2.5 km march step
  (`stepLength` 0.0042, `stepGrowth` 0.1, `stepFarMul` 1, `nearStepScale` 0.05) with the
  noise filter at 1.5 times the step. Jool's band is 270 km tall instead of 490 and
  quieter, with one storm spread and fixed 6 km steps growing to 18 km, with the filter at
  0.2 times the step. Laythe is quieter.

### Fixed

- Horizontal stripes across bands seen from far away (an equatorial orbit's view of the
  far oval, or the map view). Rays that skipped an empty stretch all landed exactly on the
  same bound and marched on in lockstep, so the sample shells the per-pixel jitter is
  meant to break up came back. They now land their own fraction of a step past it.
- Fine diagonal stripes across anything the march under-samples, such as a far band's
  thin ribbons. The per-pixel sample offset was interleaved gradient noise, which is
  itself a diagonal grating, and where each pixel's result depends on its offset that
  grating showed. The offset now comes from a blue-noise tile embedded in the plugin (no
  new asset), which shows as grain that the upsample smooths away.
- Another body's aurora showing through the body the map is focused on (Jool's through
  Laythe in the map view, worse the farther out you zoom, since the depth test only
  reaches 5 percent of a radius). The scaled-space renderers now carry the same analytic
  occluder the flight-camera ones do, for the body the map camera looks at.
- The shader bundle now carries a build tag that the plugin checks when it loads it. A
  DLL and a bundle built from different sources used to fail silently (settings the
  shader did not know, uniforms the plugin did not set). Now KSP.log gets a warning and
  the window's status line says so in capitals. "Shader build 1110 matches the plugin" is
  the line to look for.
- Tuning window layout. A row's label used to run on under its slider and its def button
  when it was longer than the label column, which hid the end of most explanations. A row
  now shows the setting's name in the column and the explanation on its own line under
  the row. The status lines wrap, and the "Aurora render" A/B toggle and the dev build's
  CPU readout, which were drawn over each other, have a line each.

### Still missing

- No magnetic axis tilt yet. Every oval is centred on the body's rotation poles.


## 1.0.1 (2026-10-14)

**Fixes the 1.0.0 download, which did not work for anyone.** The 1.0.0 zip had the
shader bundle and the plugin DLL one folder too deep (`Bundles/rva_shaders/rva_shaders`
and `Plugins/RealisticVolumetricAurorae.dll/...`). KSP loaded the nested DLL, so the game
looked fine, but the plugin could not find its shaders and disabled itself.

- Release package rebuilt with the correct layout. The packaging script now refuses to
  produce a zip whose layout is wrong.
- The plugin now also looks for `rva_shaders` anywhere under its own folder if it is not
  at the expected path, and logs a warning instead of giving up.
- If the bundle is truly missing, a dialog at the main menu says which file is missing
  and how to fix it, instead of a silent black sky.
- The two optional SPVE compatibility patches are now gated with `:NEEDS[SPVE]`, so
  ModuleManager no longer logs "Deleting root node" about them when SPVE is not
  installed.
- The shipped DLL is built from the published source (the 1.0.0 one predated the
  first-launch settings write).

Delete the old `GameData/RealisticVolumetricAurorae` folder before installing, or let
CKAN or KSP-AVC do it.

## 1.0.0 (2026-10-13)

First release.
