<!-- Thread title (Add-on Releases):
[1.12.x] Realistic Volumetric Aurorae (RVA) v1.1.0 — volumetric aurorae for Kerbin, Jool and Laythe
Paste the body below into the forum editor; headings become bold lines, keep the
lists. Fill in the two links still marked <...> (SpaceDock, and the thread's own URL if you quote it) and attach 3–4 screenshots (night-side
orbit, ground view under the curtains, map view). -->

**Realistic Volumetric Aurorae (RVA)** — v1.1.0 for KSP 1.12.x

Volumetric polar aurorae for Kerbin, Jool and Laythe. Not a texture: a raymarched glow layer drawn around the planet every frame — thin, folding ribbons that curl back on themselves, vertical rays tapering to tips, green at the base and red at the top, three arcs forming the auroral oval. Quiet most of the time; then a storm comes, the oval widens, slides toward the equator and the outer arcs fade in, and it settles down again. From the ground the curtains fill the sky, from orbit the oval sits on the night side and follows the terminator.

**1.1.0:** Jool and Laythe have aurorae too — Jool's a gas giant's, in hydrogen pinks and violets tight around its poles, visible from Jool orbit and from Laythe's surface; Laythe's an oxygen aurora in greens and reds that never rests for long inside Jool's magnetosphere, whose storms spread the oval all the way down to the equator, and which lights up the whole moon while Laythe passes through Jool's shadow. Any body can be given one with a config file (see the README).

[screenshots]

**What it does**
- Volumetric ribbons with folds and rays, animated; a storm cycle (quiet → ramp up → peak → ramp down) with an average of 45 minutes of real time between storms (never less than 18), all configurable
- Day/night: fades out past the terminator
- Hidden where your vessel or nearby terrain is in front of it, with crisp silhouettes at any render scale
- With EVE's volumetric clouds installed: occluded per pixel by the clouds' own transmittance — seen through thin cloud, hidden behind thick cloud, no altitude switch
- Drawn in flight, in the map view and in the Tracking Station
- 1.1.0: the aurora lights your vessel — up to four lights from the directions its light actually comes from, in its own colour, fading with daylight and rising with storms; the vessel only, not the ground or the clouds. Switchable per body
- A live tuning window (Alt+F8): brightness, colours and their layout in the band, ray density and fineness, altitude, latitude, ribbon width and folds, animation speed, storm frequency, render scale; every row has a reset-to-default button; *Save* keeps your look
- No dependencies. Scatterer-compatible (both camera modes)

**Download**
- SpaceDock: https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae
- GitHub release: https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest
- CKAN: `Realistic Volumetric Aurorae` (once indexed)

**Install:** delete any older `GameData/RealisticVolumetricAurorae` (or the pre-release `GameData/SPVE_AuroraGlow`), then copy the `GameData/RealisticVolumetricAurorae` folder from the zip into your GameData. Nothing else.

**Requirements / compatibility**
- KSP 1.12.x (built on 1.12.5), Windows / DirectX 11
- Optional: EVE with volumetric clouds (per-pixel cloud occlusion). Without it the aurora simply draws.
- Optional: SPVE + ModuleManager — the included `Compatibility/RVA_SPVE_remove_*.cfg` patches remove SPVE's own aurora layers on Kerbin, Jool and Laythe so RVA replaces them (they do nothing without SPVE). A third optional patch fixes SPVE's underwater-ambience lightning; delete it if you want SPVE's behaviour.
- Kerbin, Jool and Laythe in this release; ovals sit on the rotation poles (no magnetic tilt yet)

**Performance:** the aurora renders into a buffer **Render scale** × the screen (default 0.25 since 1.1.0, down from 0.35) and is composited onto the frame; that buffer size is the cost. 0.2 is a third cheaper again, 0.35 twice the price, at the expense of fine detail. Storms cost more than quiet time. Since 1.1.0 the frames are accumulated over time (each samples a different sub-pixel; the history is reprojected as the camera moves), so the grain is gone and a lower render scale converges on the same picture — switch *Temporal accumulation* off per body if you prefer every frame on its own. The *Aurora render* toggle at the top of the window is an instant A/B of the mod's share of your frame time.

**Known limits:** the aurora lights your vessel but not the ground or the clouds; ovals sit on the rotation poles (no magnetic tilt yet); the map-view instance has no cloud occlusion; settings are global, not per save.

**Licence:** CC BY-NC-SA 4.0 (Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International), © 2026 HereComesTheBoom. The licence text ships in the download (`LICENSE.txt`).
**Source:** https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae (plugin C# and shader source, build instructions).

**Changelog**
- 1.1.0 — Jool and Laythe (per-body presets, any body can be given an aurora); storms that reach the equator (Laythe); eclipses; the aurora lights the vessel; temporal accumulation, so a render scale of 0.25 looks like 0.35 used to; storms 45 minutes of real time apart; the aurora looks the same at every latitude (noise on the sphere); fixes for stripes seen from far away and another body's aurora showing through the map's body; shader/plugin build check. Full list in CHANGELOG.md.
- 1.0.1 — fixes the 1.0.0 download (bundle and DLL one folder too deep); missing-bundle dialog; SPVE patches gated with :NEEDS.
- 1.0.0 — first public release.
