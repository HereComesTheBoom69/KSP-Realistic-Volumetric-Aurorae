# Realistic Volumetric Aurorae 1.2.0

Duna and Eve get aurorae, and the clouds hide the aurora properly at every altitude. KSP
1.12.x. Delete the old `GameData/RealisticVolumetricAurorae` folder before installing or let
CKAN / AVC do it. Your `PluginData/settings.cfg` is left alone (new per body keys just take
their defaults). If you saved one before and want the new Laythe tuning, hit *preset values*
for Laythe in the window and Save.

## New

- **Duna**: pale oxygen green with the CO2 violet-blue from mid height up, no red top, about
  20 to 40 km up around 53 deg, dim when quiet and fully up in a storm, storms about an hour
  apart.
- **Eve**: a thin bright sheet 60 to 85 km up above the clouds, oxygen green at the base, a
  pale blue-white haze above it and a soft blue top, storms half an hour apart and spreading
  the oval wide.
- Ike and Gilly terrain maps ship so they hide the Duna and Eve aurorae at any zoom, and a
  new optional patch removes SPVE's own aurora layers on Duna and Eve (needs ModuleManager).
- **EVE's flat 2D cloud layers hide the aurora now**, as drawn. Above the altitude where the
  volumetrics switch off they are the only clouds, and the far side of the oval used to show
  straight through them.
- **Cloud top read from EVE's configs** instead of a per body number, so any cloud pack
  works without a setting (toggle in the window if you want your own value).
- **Horizon boost** (per body slider): closes the cloud band at the horizon, where EVE's own
  march leaves a grazing ray translucent and a bright aurora showed through. Nothing changes
  looking down through the clouds and a pixel EVE left clear stays clear.
- Laythe preset retuned to three arcs like Kerbin's, fading in with the storm.
- Map view and tracking station: a body that spans a small part of the view renders at a
  higher scale for the same pixel budget, so a small planet shows a band instead of a blur.

## Fixed

- A cloud shaped mask sliding over the far side of the oval with the camera zoomed out
  (Eve). EVE's cloud buffer goes stale above the volumetrics fade-out and we kept reading
  it. Its only read while EVE is actually rendering the volumetrics now, so above that only
  the 2D clouds and the terrain hide the aurora, nothing else.
- The far side of the oval showing through the cloud band at the horizon (the horizon boost
  above).
- The aurora showing through a thin sliver of the limb from orbit (Duna). The limb is now
  decided per screen pixel from the depth buffer, so the silhouette is the drawn terrains at
  any render scale, and where KSP has no terrain loaded the plugins own model of the body
  stands in.
- The "split" in the aurora, a seam trailing a fast camera move or time warp. Fast moves
  look grainier for a moment and settle at once.
- Shader build 1131. KSP.log says `Shader build 1131 matches the plugin` when the dll and
  the bundle are from the same build.

## Still true

- Ovals on the rotation poles, no magnetic tilt yet.
- Map view / tracking station instance has no cloud occlusion.
- Settings are global, not per save.

Full details in `Release/CHANGELOG.md`. License CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
