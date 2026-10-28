# Realistic Volumetric Aurorae 1.1.1

**Fix release.** For KSP 1.12.x. Install over 1.1.0 (delete the old
`GameData/RealisticVolumetricAurorae` folder first, or let CKAN or KSP-AVC do it). Your
`PluginData/settings.cfg` is left alone.

## Fixed

- Another body's aurora showing through the ground of the body you are at just below its
  horizon (Kerbin's from Mun orbit), including with the camera zoomed far out and from high
  orbit, where KSP has unloaded the body's terrain. The plugin now carries its own model of
  the body, built from the terrain's height function, and hides the aurora behind the real
  highlands at any distance. The maps of the stock bodies that need one ship with the mod
  (`PluginData/terrain/`, 1 MB each); any other body's is built the first time you are there
  with another body's aurora in view, about twenty seconds with an FPS dip, and kept.
- Another body's aurora fading out or vanishing with the camera angle around an airless
  body (Kerbin's from Mun orbit). The cloud occlusion is now used only where the body you
  are at has an atmosphere.
- Shader build 1116. KSP.log says `Shader build 1116 matches the plugin` when the DLL and
  the bundle are from the same build.

If you are coming from 1.0.x: 1.1.0 added Jool's and Laythe's aurorae, per-body settings,
the light on the vessel, the temporal accumulation and the 45-minute storm cadence. Its
notes are in `Release/CHANGELOG.md`.

Licence CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
