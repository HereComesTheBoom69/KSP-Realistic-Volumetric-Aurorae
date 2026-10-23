<!-- The update post for the existing thread
(https://forum.kerbalspaceprogram.com/topic/231944-realistic-volumetric-aurorae-rva-visual-mod/).
Paste it into a new reply. Then edit the first post from ForumPost.md and change the thread
title to "[1.12.x] Realistic Volumetric Aurorae (RVA) v1.1.0 - volumetric aurorae for Kerbin,
Jool and Laythe". Attach two or three screenshots: Jool's aurora from orbit, Laythe's from the
ground, a lit vessel. -->

**1.1.0 is out. Jool and Laythe have aurorae.**

Jool's is a gas giant's: hydrogen pinks and violets, a couple of hundred kilometres tall, tight around the poles and almost always on. Best from Jool orbit, and from Laythe's surface, where it sits on Jool's limb. Laythe's is an oxygen aurora, greens with red tops, that rarely rests inside Jool's magnetosphere, and its storms spread the oval all the way down to the equator. While Laythe passes through Jool's shadow the whole moon lights up.

Also new:
- **The aurora lights your vessel** (on by default; off, one, two or four lights per body in the window). Not the ground and not the clouds, just the craft, from where the light actually comes from.
- **Storms are rare now**: 45 minutes of real time apart on average, never less than 18, and the countdown survives reverts and scene changes. The window shows it and has a *Storm now* button.
- **Cheaper**: frames are accumulated over time, so the grain is gone and a render scale of 0.25 (the new default) looks like 0.35 used to. If you kept a settings.cfg from 1.0.x, try lowering yours.
- **Any body** can be given an aurora with one config node. Planet pack authors, see the README.
- Fixes: stripes across the far oval from orbit and in the map view, another body's aurora showing through the map's body, and the seam along the band's horizon. The plugin now checks that the shader bundle matches it and says so if not.

Delete the old `GameData/RealisticVolumetricAurorae` before installing (CKAN and KSP-AVC do that for you). Your own `PluginData/settings.cfg` is kept, but it still has the old storm cadence. Delete its `averageQuietDuration` lines, or press *def* on that row per body and *Save*.

Downloads: SpaceDock https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae, GitHub https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest, and CKAN once it has indexed the new version. Full changelog on GitHub.
