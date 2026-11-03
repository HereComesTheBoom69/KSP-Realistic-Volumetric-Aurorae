# Realistic Volumetric Aurorae 1.2.1

Small follow up to 1.2.0. KSP 1.12.x. Install over 1.2.0 (delete the old
`GameData/RealisticVolumetricAurorae` folder first, or let CKAN / AVC do it). Your
`PluginData/settings.cfg` is left alone. The shader bundle is unchanged from 1.2.0.

## Fixed

- Render scale by pixel budget now covers the body your orbiting. It only ever applied to
  other bodies in flight and to the map view, so from a high orbit your own planets aurora
  stayed at the configured scale of a screen it filled a tenth of, and the only way to a
  sharp band was pushing the render scale slider up by hand. Now the scale rises as the
  planet shrinks on screen, up to 1.0, for the same cost as the configured scale filling the
  view, and nothing changes inside the band or from low orbit. The Render scale row in the
  window shows the scale in use when it differs from the slider.

If your coming from 1.1.x, 1.2.0 added Duna and Eve, occlusion by EVE's 2D cloud layers, the
cloud top read from EVE's configs, the horizon boost and a per pixel limb. Notes in
`Release/CHANGELOG.md`.

License CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom.
