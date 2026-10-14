# SpaceDock listing text

## Short description (one line, shown in listings)
A volumetric polar aurora for Kerbin: folding ribbons, rays and a storm cycle, raymarched every frame, occluded by EVE's volumetric clouds, with a live tuning window. No dependencies.

## Long description (SpaceDock takes Markdown)

**Realistic Volumetric Aurorae (RVA)** draws a volumetric aurora around Kerbin: not a texture but a raymarched glow layer — thin, folding ribbons that curl back on themselves, vertical rays tapering to tips, green at the base and red at the top, three arcs forming the auroral oval. Quiet most of the time; then a storm comes, the oval widens, slides toward the equator and the outer arcs fade in, and it settles down again. From the ground the curtains fill the sky; from orbit the oval sits on the night side and follows the terminator.

**Features**

- Volumetric ribbons with folds and rays, animated; a configurable storm cycle
- Fades out past the terminator
- Hidden where your vessel or nearby terrain is in front of it, with crisp silhouettes at any render scale
- With EVE's volumetric clouds: occluded per pixel by the clouds' own transmittance
- Drawn in flight, in the map view and in the Tracking Station
- Live tuning window (Alt+F8) — brightness, colours, rays, altitude, latitude, ribbon width and folds, animation speed, storm frequency, render scale — with per-row reset and *Save*
- No dependencies; Scatterer-compatible

**Install:** copy the `GameData/RealisticVolumetricAurorae` folder into your GameData (delete any older copy first). Nothing else is needed.

**Optional:** EVE with volumetric clouds (cloud occlusion); SPVE + ModuleManager (the included compatibility patch removes SPVE's own Kerbin aurora so RVA replaces it).

**Performance:** the render scale (default 0.35) is the cost; 0.25 is about half the price, 0.2 a quarter.

**Known limits:** the aurora lights nothing; Kerbin only in this release; the map-view instance has no cloud occlusion.

Licence: CC BY-NC-SA 4.0, © 2026 HereComesTheBoom. Source: https://github.com/SkinnyThePimp/KSP-Realistic-Volumetric-Aurorae. Forum thread: <FORUM THREAD LINK>.

## Fields
- Game: Kerbal Space Program · KSP version: 1.12.5
- Licence dropdown: **CC BY-NC-SA 4.0** (CKAN reads the licence from here — it must match LICENSE.txt)
- Source code: the GitHub repository
- Version: 1.0.0 · Changelog: "First public release."
- Tick **Add to CKAN** (mod page → Edit) after the first upload: SpaceDock's bot opens the NetKAN pull request for you.
