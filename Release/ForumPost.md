<!-- Thread title (Add-on Releases):
[1.12.x] Realistic Volumetric Aurorae (RVA) v1.0.0 — a volumetric aurora for Kerbin
Paste the body below into the forum editor; headings become bold lines, keep the
lists. Fill in the two links still marked <...> (SpaceDock, and the thread's own URL if you quote it) and attach 3–4 screenshots (night-side
orbit, ground view under the curtains, map view). -->

**Realistic Volumetric Aurorae (RVA)** — v1.0.0 for KSP 1.12.x

A volumetric polar aurora for Kerbin. Not a texture: a raymarched glow layer drawn around the planet every frame — thin, folding ribbons that curl back on themselves, vertical rays tapering to tips, green at the base and red at the top, three arcs forming the auroral oval. Quiet most of the time; then a storm comes, the oval widens, slides toward the equator and the outer arcs fade in, and it settles down again. From the ground the curtains fill the sky, from orbit the oval sits on the night side and follows the terminator.

[screenshots]

**What it does**
- Volumetric ribbons with folds and rays, animated; a storm cycle (quiet → ramp up → peak → ramp down) with an average of 90 s between storms, all configurable
- Day/night: fades out past the terminator
- Hidden where your vessel or nearby terrain is in front of it, with crisp silhouettes at any render scale
- With EVE's volumetric clouds installed: occluded per pixel by the clouds' own transmittance — seen through thin cloud, hidden behind thick cloud, no altitude switch
- Drawn in flight, in the map view and in the Tracking Station
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
- Optional: SPVE + ModuleManager — the included `Compatibility/RVA_SPVE_remove_kerbin_aurora.cfg` removes SPVE's own Kerbin aurora so RVA replaces it (does nothing without SPVE). A second optional patch fixes SPVE's underwater-ambience lightning; delete it if you want SPVE's behaviour.
- Kerbin only in this release

**Performance:** the aurora renders into a buffer **Render scale** × the screen (default 0.35) and is composited onto the frame; that buffer size is the cost. 0.25 is about half the price of 0.35, 0.2 a quarter, at the expense of fine detail. Storms cost more than quiet time. The *Aurora render* toggle at the top of the window is an instant A/B of the mod's share of your frame time.

**Known limits:** the aurora lights nothing (no ground, cloud or vessel light); the map-view instance has no cloud occlusion; settings are global, not per save.

**Licence:** CC BY-NC-SA 4.0 (Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International), © 2026 HereComesTheBoom. The licence text ships in the download (`LICENSE.txt`).
**Source:** https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae (plugin C# and shader source, build instructions).

**Changelog**
- 1.0.0 — first public release.
