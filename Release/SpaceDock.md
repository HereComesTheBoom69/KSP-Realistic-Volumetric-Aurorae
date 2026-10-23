# SpaceDock listing text

## Short description (one line, shown in listings)
Volumetric aurorae for Kerbin, Jool and Laythe: folding ribbons, rays and a storm cycle, raymarched every frame, occluded by EVE's volumetric clouds, with a live tuning window. No dependencies.

## Long description (SpaceDock takes Markdown)

**Realistic Volumetric Aurorae (RVA)** draws volumetric aurorae around Kerbin, Jool and Laythe: not a texture but a raymarched glow layer — thin, folding ribbons that curl back on themselves, vertical rays tapering to tips, green at the base and red at the top, three arcs forming the auroral oval. Quiet most of the time; then a storm comes, the oval widens, slides toward the equator and the outer arcs fade in, and it settles down again. From the ground the curtains fill the sky; from orbit the oval sits on the night side and follows the terminator.

**Features**

- Volumetric ribbons with folds and rays, animated; a configurable storm cycle
- Fades out past the terminator
- Hidden where your vessel or nearby terrain is in front of it, with crisp silhouettes at any render scale
- With EVE's volumetric clouds: occluded per pixel by the clouds' own transmittance
- Drawn in flight, in the map view and in the Tracking Station
- 1.1.0: Jool's aurora — a gas giant's, hydrogen pinks and violets tight around the poles, almost always on, visible from Jool orbit and on Jool's limb from Laythe — and Laythe's, oxygen greens and reds driven by Jool's magnetosphere, lighting the whole moon while Laythe is in Jool's shadow. Any body can be given one with a config file
- 1.1.0: the aurora lights your vessel (not the ground or the clouds) from the directions its light comes from — switchable per body
- Live tuning window (Alt+F8) — brightness, colours, rays, altitude, latitude, ribbon width and folds, animation speed, storm frequency, render scale — with per-row reset and *Save*
- No dependencies; Scatterer-compatible

**Install:** copy the `GameData/RealisticVolumetricAurorae` folder into your GameData (delete any older copy first). Nothing else is needed.

**Optional:** EVE with volumetric clouds (cloud occlusion); SPVE + ModuleManager (the included compatibility patches remove SPVE's own aurora layers on Kerbin, Jool and Laythe so RVA replaces them).

**Performance:** the render scale (default 0.25 since 1.1.0, down from 0.35) is the cost; 0.2 is a third cheaper, 0.35 twice the price. Frames are accumulated over time since 1.1.0 (reprojected history, no grain), so a lower scale converges on the same picture.

**Known limits:** the aurora lights the vessel only, not the ground or the clouds; ovals sit on the rotation poles (no magnetic tilt yet); the map-view instance has no cloud occlusion.

Licence: CC BY-NC-SA 4.0, © 2026 HereComesTheBoom. Source: https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae. Forum thread: https://forum.kerbalspaceprogram.com/topic/231944-realistic-volumetric-aurorae-rva-visual-mod/.

## Fields
- Game: Kerbal Space Program · KSP version: 1.12.5
- Licence dropdown: **CC BY-NC-SA 4.0** (CKAN reads the licence from here — it must match LICENSE.txt)
- Source code: the GitHub repository
- Version: 1.1.0 · Changelog: paste `Release/ReleaseNotes-1.1.0.md` (the same text as the GitHub release)
- Tick **Add to CKAN** (mod page → Edit) after the first upload: SpaceDock's bot opens the NetKAN pull request for you.
