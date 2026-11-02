# Realistic Volumetric Aurorae 1.1.1

Fix release. KSP 1.12.x. Install over 1.1.0 (delete the old `GameData/RealisticVolumetricAurorae`
folder first, or let CKAN / AVC do it). Your `PluginData/settings.cfg` is left alone.

## Fixed

- Another bodys aurora showing through the ground of the body your at just below the horizon
  (Kerbin's from Mun orbit), including with the camera zoomed far out and from high orbit
  where KSP has unloaded the terrain. The plugin now carries its own model of the body built
  from the terrain height function and hides the aurora behind the real highlands at any
  distance. Maps for the stock bodies that need one ship with the mod (`PluginData/terrain/`,
  1 MB each), any other body builds its own the first time your there with another bodys
  aurora in view, about 20 seconds with an FPS dip, then its kept.
- Another bodys aurora fading or vanishing with the camera angle around an airless body
  (Kerbin's from Mun orbit). Cloud occlusion is only used where the body your at has an
  atmosphere now.
- Shader build 1116. KSP.log says `Shader build 1116 matches the plugin` when the dll and
  bundle are from the same build.

Coming from 1.0.x: 1.1.0 added Jool and Laythe, per body settings, the vessel light,
temporal accumulation and the 45 min storm cadence. Notes in `Release/CHANGELOG.md`.

License CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
