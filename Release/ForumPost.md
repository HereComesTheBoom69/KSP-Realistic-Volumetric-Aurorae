<!-- Thread title (Add-on Releases):
[1.12.x] Realistic Volumetric Aurorae (RVA) v1.1.1 - volumetric aurorae for Kerbin, Jool and Laythe
Paste the body below into the forum editor. Headings become bold lines, keep the lists.
Fill in any link still marked <...> and attach 3 or 4 screenshots (night-side orbit, ground
view under the curtains, map view). -->

**Realistic Volumetric Aurorae (RVA)** v1.1.1 for KSP 1.12.x

Volumetric polar aurorae for Kerbin, Jool and Laythe. Not a texture: a raymarched glow layer drawn around the planet every frame. Thin, folding ribbons that curl back on themselves, vertical rays tapering to tips, green at the base and red at the top, three arcs forming the auroral oval. It is quiet most of the time. Then a storm comes, the oval widens, slides toward the equator and the outer arcs fade in, and it settles down again. From the ground the curtains fill the sky. From orbit the oval sits on the night side and follows the terminator.

**1.1.0:** Jool and Laythe have aurorae too. Jool's is a gas giant's, in hydrogen pinks and violets tight around its poles, visible from Jool orbit and from Laythe's surface. Laythe's is an oxygen aurora in greens and reds that never rests for long inside Jool's magnetosphere. Its storms spread the oval all the way down to the equator, and it lights up the whole moon while Laythe passes through Jool's shadow. Any body can be given an aurora with a config file (see the README).

[screenshots]

**What it does**
- Volumetric ribbons with folds and rays, animated, with a storm cycle (quiet, ramp up, peak, ramp down). Storms come on average every 45 minutes of real time and never less than 18 minutes apart. All of it is configurable.
- Day and night: it fades out past the terminator.
- Hidden where your vessel or nearby terrain is in front of it, with crisp silhouettes at any render scale.
- With EVE's volumetric clouds installed, the clouds hide it per pixel using their own transmittance. You see it through thin cloud and not through thick cloud, with no altitude switch.
- Drawn in flight, in the map view and in the Tracking Station.
- 1.1.0: the aurora lights your vessel. Up to four lights come from the directions the aurora's light actually comes from, in its own colour, fading with daylight and rising with storms. They light the vessel only, not the ground or the clouds, and can be switched off per body.
- A live tuning window (Alt+F8): brightness, colours and where they sit in the band, ray density and fineness, altitude, latitude, ribbon width and folds, animation speed, time between storms, render scale. Every row has a reset-to-default button and *Save* keeps your look.
- No dependencies. Scatterer-compatible (both camera modes).

**Download**
- SpaceDock: https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae
- GitHub release: https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest
- CKAN: `Realistic Volumetric Aurorae` (once indexed)

**Install:** delete any older `GameData/RealisticVolumetricAurorae` (or the pre-release `GameData/SPVE_AuroraGlow`), then copy the `GameData/RealisticVolumetricAurorae` folder from the zip into your GameData. Nothing else.

**Requirements and compatibility**
- KSP 1.12.x (built on 1.12.5), Windows / DirectX 11
- Optional: EVE with volumetric clouds (per-pixel cloud occlusion). Without it the aurora simply draws.
- Optional: SPVE and ModuleManager. The included `Compatibility/RVA_SPVE_remove_*.cfg` patches remove SPVE's own aurora layers on Kerbin, Jool and Laythe so RVA replaces them (they do nothing without SPVE). A third optional patch fixes SPVE's underwater ambience lightning. Delete it if you want SPVE's behaviour.
- Kerbin, Jool and Laythe in this release. The ovals sit on the rotation poles (no magnetic tilt yet).

**Performance:** the aurora renders into a buffer that is **Render scale** times the screen (default 0.25 since 1.1.0, down from 0.35) and is composited onto the frame. That buffer size is the cost. 0.2 is a third cheaper again, 0.35 twice the price, at the expense of fine detail. Storms cost more than quiet time. Since 1.1.0 the frames are accumulated over time (each samples a different sub-pixel, and the history is reprojected as the camera moves), so the grain is gone and a lower render scale converges on the same picture. Switch *Temporal accumulation* off per body if you prefer every frame on its own. The *Aurora render* toggle at the top of the window is an instant A/B test of the mod's share of your frame time.

**Known limits:** the aurora lights your vessel but not the ground or the clouds; the ovals sit on the rotation poles (no magnetic tilt yet); the map view instance has no cloud occlusion; settings are global, not per save.

**Licence:** CC BY-NC-SA 4.0 (Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International), (c) 2026 HereComesTheBoom. The licence text ships in the download (`LICENSE.txt`).
**Source:** https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae (plugin C# and shader source, build instructions).

**Changelog**
- 1.1.0: Jool and Laythe (per-body presets, any body can be given an aurora); storms that reach the equator (Laythe); eclipses; the aurora lights the vessel; temporal accumulation, so a render scale of 0.25 looks like 0.35 used to; storms 45 minutes of real time apart; the aurora looks the same at every latitude (noise sampled on the sphere); fixes for stripes seen from far away and for another body's aurora showing through the map's body; a shader and plugin build check. Full list in CHANGELOG.md.
- 1.0.1: fixes the 1.0.0 download (bundle and DLL one folder too deep); missing-bundle dialog; SPVE patches gated with :NEEDS.
- 1.0.0: first public release.
