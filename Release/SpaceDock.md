# SpaceDock listing text

## Short description (one line)
Volumetric aurorae for Kerbin, Jool, Laythe, Duna and Eve. Folding ribbons, rays, a storm cycle, raymarched every frame, hidden by EVE's clouds, live tuning window. No dependencies.

## Long description (spacedock takes markdown)

**Realistic Volumetric Aurorae (RVA)** draws volumetric aurorae around Kerbin, Jool, Laythe, Duna and Eve. Not a texture, a raymarched glow layer: thin folding ribbons that curl back on themselves, rays tapering off at the top, three arcs forming the oval, each body in its own colours. Quiet most of the time, then a storm comes through, the oval widens and slides toward the equator, the outer arcs fade in, and it settles back down. From the ground the curtains fill the sky. From orbit the oval sits on the night side and follows the terminator.

**Features**

- Volumetric ribbons with folds and rays, animated, configurable storm cycle
- Fades out past the terminator
- Hidden where your vessel or nearby terrain is in front of it, crisp silhouettes at any render scale
- With EVE: hidden by the clouds, volumetrics per pixel by there own transmittance and the 2D layers as drawn. Only clouds and terrain ever mask it, at any altitude
- Drawn in flight, map view, the tracking station and on the main menu planet
- Five bodies with there own looks: Kerbin's green and red oval, Jool's gas giant aurora in hydrogen pinks and violets tight around the poles, Laythe's oxygen aurora driven by Jool's magnetosphere (storms down to the equator, the whole moon lit while its in Jool's shadow), Duna's pale green with the CO2 violet-blue, Eve's thin bright sheet above the clouds. Any body can get one with a config file
- The aurora lights your vessel (not the ground or clouds) from the directions the light comes from. Per body switch
- Live tuning window (Alt+F8): brightness, colours, rays, altitude, latitude, ribbon width and folds, animation speed, time between storms, render scale, per row reset and Save
- No dependencies. Scatterer compatible

**Install:** copy the `GameData/RealisticVolumetricAurorae` folder into your GameData (delete any older copy first). Nothing else needed.

**Optional:** EVE with volumetric clouds (cloud occlusion). SPVE + ModuleManager (the included patches remove SPVE's own aurora layers on Kerbin, Jool, Laythe, Duna and Eve so RVA replaces them).

**Performance:** render scale (default 0.25) is the cost. 0.2 is a third cheaper, 0.35 twice the price. Frames are accumulated over time (reprojected history, no grain) so a lower scale converges on the same picture.

**Known limits:** lights the vessel only, not the ground or clouds. Ovals on the rotation poles (no magnetic tilt yet). Map view instance has no cloud occlusion.

License: CC BY-NC-SA 4.0, (c) 2026 HereComesTheBoom. Source: https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae. Forum: https://forum.kerbalspaceprogram.com/topic/231944-realistic-volumetric-aurorae-rva-visual-mod/

## Fields
- Game: KSP. KSP version 1.12.5
- License dropdown: **CC BY-NC-SA 4.0** (CKAN reads it from here, has to match LICENSE.txt)
- Source code: the github repo
- Version: 1.2.2, changelog: paste Release/ReleaseNotes-1.2.2.md (same text as the github release)
- Tick **Add to CKAN** (mod page, Edit) after the first upload, spacedocks bot opens the NetKAN PR for you
