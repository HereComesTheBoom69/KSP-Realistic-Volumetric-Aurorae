# Changelog

## 1.2.2 (2026-11-06)

- The aurora shows on the main menu planet. A MainMenu addon finds the menu's own Kerbin
  object (its a separate mesh in the menu scene, not the real scaled body), measures its
  radius off the mesh, and runs a full instance on it (own material, meander LUT, storm
  scheduler) through the menu camera, depth occluded by the planet and the Mun, with the
  sun taken from the menus directional light and the pixel budget on. Global toggle
  `mainMenu` (on), in the window under the map view row. Uses the RVA preset of whatever
  body the menu object is named after, so a Kopernicus menu body swap still gets one if it
  has a preset. No cloud occlusion there, same as the map view.
- Shader: the march can rotate into a planet frame given by `_PlanetAxis` (w = 1 to use).
  The menu planet isnt guaranteed to have its pole on world Y like the real bodies, so the
  menu instance passes its axis; nothing else sets it and the default is the old
  behaviour. Groundwork for magnetic tilt later. Shader build 1132 (both halves rebuilt).

## 1.2.1 (2026-11-03)

Render scale by pixel budget for the body your at.

- The budget only ever applied to other bodies in flight and to the map view, so from a high
  orbit Kerbin's own aurora stayed at 0.25 of a screen it filled a tenth of, and the only
  way to a sharp band was pushing the slider to 1. Now the scale goes up as the shell shrinks
  on screen (0.3 at 300 km, 1.0 from about 3000 km up on a 5120x1440 screen) for the same
  cost as the configured scale filling the view, and nothing changes inside the band. The
  slider shows the scale in use. Scale steps have a bit of hysteresis so a camera sitting on
  a step doesnt flip the buffers back and forth.
- No shader change, build tag stays 1131. DLL only.

## 1.2.0 (2026-11-02)

New bodies
- Duna and Eve get aurorae (Config/Duna.cfg, Config/Eve.cfg). Duna is a pale oxygen green
  with the CO2 violet-blue from mid height up, no red top, roughly 20-40 km up around 53
  deg, dim when quiet, storms about an hour apart. Eve is a thin bright soft edged sheet
  60-85 km up above the clouds, green base, pale blue-white haze above it, soft blue top,
  storms half an hour apart and spreading the oval wide. Ike and Gilly terrain maps ship so
  they occlude them properly.
- Compatibility/RVA_SPVE_remove_duna_eve_aurora.cfg, same as the Kerbin and Jool/Laythe
  patches (needs MM).
- Laythe preset retuned, three arcs like Kerbin (they fade in with the storm) instead of
  one sheet folding over itself, and a bit more active. The presets carry every per body
  key now. Jool and Kerbin values unchanged.

Clouds
- Cloud top is read from EVE's configs now instead of the per body cloudTopKm number that
  had to match whatever cloud pack you had. After MM, so planet packs and your own edits
  count. Every OBJECT with a layerRaymarchedVolume (or V5) node is a volumetric layer, its
  top is the highest maxAltitude of its cloud types, highest top on the body + 0.5 km is
  the shell. Layers that are fx only (fxOnlyLayer, or all cloud types at density 0, SPVE
  has these for sounds/rain/lightning) are skipped. cloudTopAuto toggle (on) in the window
  goes back to the preset value, which is also what a body with only 2D layers keeps.
  KSP.log says what it picked per body.
- Fixed: a cloud mask sliding around on the far side of the oval with the camera zoomed
  out (Eve). EVE stops rendering the volumetrics above their fade out altitude and the
  buffer just keeps whatever it last had, fixed to the screen, and we were multiplying the
  far aurora by it. The buffer is only read while its actually live now. Thats read from
  EVE itself, its renderer raises a flag per frame when a volumetric layer passes culling
  and we check it right before the camera renders. Fallback is the highest
  scaledFadeEndAltitude from the configs. Status line says whats in use and why.
- EVE's flat 2D cloud layers occlude the aurora too now. Above the volumetric fade out they
  are the only clouds and the far side of the oval showed straight through them. The
  renderer draws the bodys 2D layers with EVE's own meshes and materials into two half
  size buffers, one cleared black one white, difference of the two is the opacity whatever
  the colour/lighting/blend mode, and the composite multiplies the far aurora by it. Layers
  EVE keeps in scaled space get drawn on the scaled space camera. So at every altitude only
  clouds and terrain occlude the aurora, nothing else. Reflection on CloudsPQS / Clouds2D,
  if your EVE doesnt have those members it stays off and the status line says so.
- Fixed: far side of the oval showing through the cloud band at the horizon. A ray grazing
  the planet goes through hundreds of km of cloud and EVE's march leaves it translucent,
  which a bright aurora on black shows. The composite raises the clouds optical depth by
  1 + cloudGrazeBoost * (path through the slab / slab thickness - 1), nothing looking down
  through the layer, a lot along it. A pixel EVE left clear stays clear so its still only
  cloud doing the occluding. cloudGrazeBoost is per body (default 1, 0 = off) with a slider
  under the cloud top row. Debug mode 10 shows the cloud buffer.

Limb / terrain
- Fixed: aurora showing through a thin sliver of the bodys own limb from orbit (Duna, tall
  mountains for the radius). The march used to cut rays at the datum sphere so a ray that
  grazed the datum by less than the mountain height came through under them. The limb is
  decided in the composite now, per screen pixel, from the cameras depth buffer. Wherever a
  pixel shows ground beyond the near range the far aurora is dropped. Silhouette is the
  drawn terrains at screen res whatever the render scale. Where the depth buffer has no
  terrain (above the PQS limit the scaled model takes over, and a camera far above the
  vessel sees past what the depth reaches) the bodys own model stands in, drawn at camera
  size into the occluder depth buffer (terrain map sphere if theres a map, else KSP's
  scaled mesh), and only where no terrain is drawn close by on screen since the model is
  coarser. Same rule now hides another bodys aurora behind the terrain of the body your at.
  Several dev builds tried cutting at the march instead, from the depth or the model, and at
  0.25 render scale the peaks came out as blocky flickering patches, worse under warp.
  KSP.log says when the model takes over.

Temporal
- Fixed: the "split" in the aurora when whipping the camera round the vessel from far out
  or in orbit under warp, a seam on a curve just outside the limb trailing the motion.
  Three things, all the camera translating a long way in one frame: the history now carries
  each rays total and this frames near/far ratio splits it (the near/far boundary used to
  sweep across the band and reconverge from nothing), the march writes the spread of the
  auroras distance along the ray and the resolve reprojects both ends and fades the history
  where they land more than a texel apart (parallax limit slider), and the history fades
  over the last texels before last frames view edge. Fast moves look grainier while they
  last and settle at once.

Map view
- Render scale by pixel budget in the map view and tracking station too. A body spanning a
  small part of the map renders at a higher scale, capped at 0.6 of the screen, for the cost
  the map scale would spend on a body filling the view.

Cleanup
- Dead code removed: the cloud light machinery (off since v3.23), the per texel limb cut
  terrain shells. Shader build tag 1131. 1119 to 1130 were dev builds.

## 1.1.1 (2026-10-28)

Another bodys aurora hides behind the terrain of the body your at now, at every altitude
and zoom (Kerbin's from the Mun, Jool's from Laythe etc).

- Fixed: another bodys aurora visible through the ground just below the horizon (Kerbin
  from Mun orbit). The depth test only counted ground within 2% of the datum and the Mun
  highlands on the limb are higher than that. Now anything in the bodys terrain shell
  counts, the plugin samples the terrain once per session to find the max height.
- Fixed: same thing with the camera zoomed far out or from high orbit, where KSP unloads
  the terrain and draws the smooth distant model with another camera so it never reaches
  the depth buffer. The plugin builds its own model from the terrain height function, a
  1024x512 map pushes out a dense sphere drawn into its own depth buffer. Maps for the stock
  bodies that need one ship in PluginData/terrain (1 MB each), other bodies build theirs
  first time your there with another aurora in view, ~20 s with an FPS dip, then kept.
  Shipped maps are checked against your terrain and rebuilt if a pack changed the body.
- Fixed: the occluder sphere and model were anchored on the bodys local transform, which
  KSP leaves behind on the orbit while the terrain is unloaded. They follow the orbital
  position now, same for the auroras own centre.
- Fixed: another bodys aurora fading/vanishing in parts of the screen around an airless
  body. EVE's cloud buffer keeps whatever it last held where EVE draws nothing, cloud
  occlusion is only used while the body your at has an atmosphere now.
- Shader build 1116.
- Dev build: occlusion probe, depth probe and model probe on the shader debug row, a depth
  readout that logs whats under the mouse once a second, status line names the ground test
  in use and terrain map progress.

## 1.1.0 (2026-10-22)

Jool and Laythe get aurorae and the plugin goes multi body.

New
- Every body with an RVA_AURORA { body = X } node gets an aurora with its own settings.
  Ships Config/Kerbin.cfg, Jool.cfg, Laythe.cfg. Jool: hydrogen pinks/violets/blues, ~100
  to 370 km above the cloud deck, tight around the poles, almost always on, best from Jool
  orbit and from Laythe's surface where it sits on Jool's limb. Laythe: oxygen aurora,
  greens blues and red tops, 20-55 km around 52 deg, rarely quiet.
- Storms that reach the equator (Laythe). At peak the oval spreads from its quiet latitude
  down to the equator and draws back as the storm fades. stormToEquator, stormReach,
  stormFoldGain per body.
- Eclipses. While the parent body hides the sun (Laythe in Jool's shadow, about an hour
  per orbit) the daylight fade is lifted and the aurora shows over the whole moon. Global
  toggle `eclipses`.
- The aurora lights your vessel again (vesselLight, on, four lights; vesselLightIntensity,
  vesselLightCount). The old v3 ring of 90 point lights lit everything and went away in
  v3.23, this is just the part the vessel saw, done as up to 4 lights on the vessel layers
  only. The meander LUT now also carries where each ribbon is per longitude column, the
  ribbons are traced from it each frame around the vessel and every bit above the horizon
  adds light by brightness and inverse square distance, sky split into four quarters, each
  quarter becomes one light from its mean direction. Fades with daylight, follows the
  storm with a quarter second lag. Per pixel by default (a pass per part and light), per
  vertex option is free but shares Unity's 4 vertex light slots with the crafts own lights
  and can pulse.
- Storms are rare events now. Average 45 min real time between them on every body
  (averageQuietDuration 2700 s, was 30 to 90 s, slider in minutes). Exponential waits with
  a floor of 40% of the mean. Real time less pauses so physics warp doesnt hurry them.
  Each bodys place in the sequence lasts the whole session, not one scene, so reverts /
  vessel switches / tracking station dont restart the wait (before every scene started a
  fresh wait from the same seed so a saves first storm was always at the same minute and a
  short flight never saw one). Changing the average mid wait keeps the fraction passed.
  Status line counts down, Storm now button.
- Local aurora follows the vessels main body (rebuilt on SOI change and vessel switch).
  The other bodies render in scaled space, in flight, map and tracking station, culled
  while the band spans less than half a degree. A distant body renders at the scale that
  keeps its pixel budget what its own scale would spend filling the screen, up to 1.0
  (distantAutoScale, on).
- Another bodys aurora seen from a vessel (Jool from Laythe) is drawn on the flight camera
  like the local one so terrain, vessel and clouds hide it properly, with an analytic
  occluder for the body your at. Scaled space copy is used in map/tracking station and is
  depth occluded by other scaled bodies.
- Body selector in the window. Each bodys sliders, def buttons and preset reset are its
  own incl render scale and sampling. settings.cfg keeps the 1.0.x layout for Kerbin and
  the globals with a BODY { name = Jool } node per other body, old files load unchanged.
- Adding a body: copy Config/Kerbin.cfg, change body, tune, paste "Copy + log" output in.
  Planet packs can ship nodes and MM can patch them. enabled = false turns one off.
- SPVE: new optional patch removes SPVE's aurora layers on Jool and Laythe like the Kerbin
  one.
- Dev build: "Write Config/<body>.cfg" button.

Performance (each per body, shader build 1110)
- Fine latitude gate, always on. Before paying for the fold noise a sample is tested
  against the meander shifted centre with only folds + width as margin instead of the
  whole meander range, about half the near miss samples stop there. (tried a coverage map
  baked per frame first, the ribbon distance isnt smooth enough across a texel for it to
  be conservative at any affordable size, dropped it)
- Temporal accumulation (temporalAccumulation, temporalWeight). Rays tilted by a sub pixel
  offset each frame, sample positions shift, frames averaged in two history buffers at 2x
  render res reprojected through the camera motion. History rejected where vessel/terrain
  newly cover or uncover the aurora and outside last frames view, clamped to the current
  neighbourhood so the animation cant ghost. Dev: temporalUpscale, temporalClamp.
- Per body step tuning, presets carry each bodys steps. New dev setting stepFilter.
- Render scale 0.25 everywhere (was 0.35), the accumulation rebuilds the detail.

Look
- Same look at every latitude. The noise used to be sampled at the projection onto the
  equatorial plane which squeezed things by sin(lat), 10-20% at the ovals and a tearing
  stretch near the equator once a storm got there. Sampled on the sphere now. Latitude is
  signed too, each hemisphere has its own three arcs instead of one set mirrored and
  meeting in a cusp. Patterns at the ovals are therefore new realisations of the same noise.
- Step size is a function of distance from the camera now (fine close, growing with range)
  instead of one step over the whole ray. Removes the seam along the bands lower horizon
  and the concentric rings inside Jool's shell. Dev settings stepGrowth, stepFarMul,
  maxSteps cap 192.
- Shipped look retuned on the new renderer. Kerbin band starts at 20 km not 30, brighter,
  quieter base and milder storms. Jool band 270 km tall instead of 490. Laythe quieter.

Fixed
- Horizontal stripes across far bands. Rays that skipped an empty stretch all landed on the
  same bound and marched in lockstep. They land their own fraction of a step past it now.
- Fine diagonal stripes on anything under sampled. The per pixel offset was interleaved
  gradient noise which is itself a diagonal grating. Blue noise tile embedded in the plugin
  now.
- Another bodys aurora through the map focus body (Jool through Laythe in the map). Scaled
  renderers carry the same analytic occluder as the flight ones.
- Bundle carries a build tag the plugin checks. Mismatched dll/bundle used to fail silently,
  now KSP.log warns and the window says so in caps.
- Window layout, labels used to run under the sliders. Name in the column, explanation on
  its own line under the row.

Still missing
- Magnetic axis tilt. Ovals on the rotation poles.

## 1.0.1 (2026-10-14)

Fixes the 1.0.0 download which didnt work for anyone, the zip had the bundle and dll one
folder too deep (Bundles/rva_shaders/rva_shaders, Plugins/RealisticVolumetricAurorae.dll/...).
KSP loaded the nested dll so the game looked fine but the plugin couldnt find its shaders
and disabled itself.

- Package rebuilt with the right layout, packaging script refuses a wrong layout now.
- Plugin also looks for rva_shaders anywhere under its own folder and warns instead of
  giving up.
- If the bundle really is missing a dialog at the main menu says which file and how to fix
  it instead of a silent black sky.
- SPVE patches gated with :NEEDS[SPVE] so MM stops logging "Deleting root node" without
  SPVE.
- Shipped dll built from the published source (the 1.0.0 one predated the first launch
  settings write).

Delete the old GameData/RealisticVolumetricAurorae before installing, or let CKAN / AVC do it.

## 1.0.0 (2026-10-13)

First release.
