<!-- thread title (Add-on Releases):
[1.12.x] Realistic Volumetric Aurorae (RVA) v1.2.1 - volumetric aurorae for Kerbin, Jool, Laythe, Duna and Eve
paste the body below into the forum editor, headings turn into bold lines. fill in anything
still marked <...> and attach 3 or 4 screenshots (night side orbit, ground view under the
curtains, map view) -->

**Realistic Volumetric Aurorae (RVA)** v1.2.1 for KSP 1.12.x

Volumetric aurorae for Kerbin, Jool, Laythe, Duna and Eve. This isnt a texture, its a raymarched glow layer drawn around the planet every frame. Thin folding ribbons, rays tapering off at the top, green at the base and red up top, three arcs forming the oval. Its quiet most of the time, then a storm rolls in, the oval widens and slides toward the equator, the outer arcs fade in, and it settles back down. From the ground the curtains fill the sky. From orbit the oval sits on the night side and follows the terminator around.

Every body has its own look. Jool's is a gas giant one, hydrogen pinks and violets tight around the poles, you can see it from Jool orbit and from Laythe's surface. Laythe's is an oxygen aurora in greens and reds that never rests for long inside Jools magnetosphere, its storms spread the oval all the way to the equator and it lights up the whole moon while Laythe is in Jool's shadow. Duna's is a pale green with the CO2 violet-blue up top, Eve's a thin bright sheet above the clouds with a blue-white haze. Any body can get an aurora with a config file (see the README).

[screenshots]

**What it does**
- Volumetric ribbons with folds and rays, animated, storm cycle (quiet, ramp up, peak, ramp down). Storms come every 45 min of real time on average, never less than 18 apart. All configurable.
- Fades out past the terminator.
- Hidden where your vessel or nearby terrain is in front of it, crisp silhouettes at any render scale.
- With EVE the clouds hide it, the volumetrics per pixel using there own transmittance and the flat 2D layers as drawn, so only clouds and terrain ever mask the aurora at any altitude. You see it through thin cloud and not through thick cloud.
- Drawn in flight, map view and the tracking station.
- The aurora lights your vessel. Up to four lights from the directions the light actually comes from, in its own colour, fading with daylight and rising with storms. Vessel only, not the ground or clouds, can be turned off per body.
- Live tuning window (Alt+F8): brightness, colours and where they sit in the band, ray density and fineness, altitude, latitude, ribbon width and folds, animation speed, time between storms, render scale. Every row has a reset to default button and Save keeps your look.
- No dependencies. Works with Scatterer (both camera modes).

**Download**
- SpaceDock: https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae
- GitHub: https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest
- CKAN: `Realistic Volumetric Aurorae` (once its indexed)

**Install:** delete any older `GameData/RealisticVolumetricAurorae` (or the pre release `GameData/SPVE_AuroraGlow`), then copy the `GameData/RealisticVolumetricAurorae` folder from the zip into your GameData. Thats all.

**Requirements / compatibility**
- KSP 1.12.x (built on 1.12.5), Windows / DX11
- Optional: EVE with volumetric clouds (cloud occlusion). Without it the aurora just draws.
- Optional: SPVE + ModuleManager. The included `Compatibility/RVA_SPVE_remove_*.cfg` patches remove SPVE's own aurora layers on Kerbin, Jool, Laythe, Duna and Eve so RVA replaces them (they do nothing without SPVE). Theres also an optional patch that fixes SPVE's underwater ambience lightning, delete it if you want SPVE's behaviour.
- Ovals sit on the rotation poles, no magnetic tilt yet.

**Performance:** the aurora renders into a buffer thats **Render scale** times the screen (default 0.25) and gets composited onto the frame. That buffer size is the cost. 0.2 is a third cheaper again, 0.35 twice the price, you lose some fine detail going down. Storms cost more than quiet time. Frames are accumulated over time (each one samples a different sub pixel and the history is reprojected as the camera moves) so the grain is gone and a lower render scale converges on the same picture. Turn *Temporal accumulation* off per body if you'd rather have every frame on its own. The *Aurora render* toggle at the top of the window is a quick A/B test of what the mod costs you.

**Known limits:** lights the vessel but not the ground or clouds, ovals on the rotation poles (no magnetic tilt yet), the map view instance has no cloud occlusion, settings are global not per save.

**License:** CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom. License text ships in the download (`LICENSE.txt`).
**Source:** https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae (plugin C# + shader source, build instructions)

**Changelog**
- 1.2.1: render scale by pixel budget now covers the body your orbiting, so from a high orbit the band sharpens up as the planet shrinks on screen (up to render scale 1) for the same cost instead of staying a blur.
- 1.2.0: Duna and Eve. EVE's 2D cloud layers hide the aurora too, the stale cloud mask above the volumetrics fade-out is gone, the cloud band at the horizon is closed, cloud top read from EVE's configs. Limb decided per screen pixel (no more aurora through a sliver of the limb), the temporal "split" on fast camera moves fixed, map view renders small bodies sharper. Laythe preset retuned.
- 1.1.1: another bodys aurora hides behind the terrain of the body your at at every altitude and zoom (Kerbin's from the Mun), the plugin builds its own model of the body from the terrain height function. Fix for the aurora fading with camera angle around airless bodies.
- 1.1.0: Jool and Laythe (per body presets, any body can get one), storms that reach the equator (Laythe), eclipses, the aurora lights the vessel, temporal accumulation so 0.25 render scale looks like 0.35 used to, storms 45 min apart, same look at every latitude (noise sampled on the sphere), fixes for stripes seen from far away and for another bodys aurora showing through the maps body, shader/plugin build check. Full list in CHANGELOG.md.
- 1.0.1: fixes the 1.0.0 download (bundle and dll one folder too deep), missing bundle dialog, SPVE patches gated with :NEEDS.
- 1.0.0: first release.
