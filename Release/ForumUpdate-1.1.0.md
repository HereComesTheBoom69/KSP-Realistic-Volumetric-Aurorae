<!-- update post for the existing thread
(https://forum.kerbalspaceprogram.com/topic/231944-realistic-volumetric-aurorae-rva-visual-mod/)
paste as a new reply, then edit the first post from ForumPost.md and change the thread title
to "[1.12.x] Realistic Volumetric Aurorae (RVA) v1.1.0 - volumetric aurorae for Kerbin, Jool
and Laythe". 2 or 3 screenshots: Jool's aurora from orbit, Laythes from the ground, a lit vessel -->

**1.1.0 is out. Jool and Laythe have aurorae now.**

Jool's is a gas giant one, hydrogen pinks and violets, a couple hundred km tall, tight around the poles and almost always on. Best from Jool orbit and from Laythe's surface where it sits on Jools limb. Laythe's is an oxygen aurora, greens with red tops, rarely quiet inside Jool's magnetosphere, and its storms spread the oval all the way down to the equator. While Laythe goes through Jool's shadow the whole moon lights up.

Also new:
- **The aurora lights your vessel** (on by default, off / one / two / four lights per body in the window). Not the ground and not the clouds, just the craft, from where the light actually comes from.
- **Storms are rare now**, 45 minutes of real time apart on average, never less than 18, and the countdown survives reverts and scene changes. The window shows it and has a Storm now button.
- **Cheaper**: frames get accumulated over time so the grain is gone and 0.25 render scale (the new default) looks like 0.35 used to. If you kept a settings.cfg from 1.0.x try lowering yours.
- **Any body** can get an aurora with one config node. Planet pack authors see the README.
- Fixes: stripes across the far oval from orbit and in the map view, another bodys aurora showing through the maps body, the seam along the bands horizon. The plugin checks the shader bundle matches it now and tells you if not.

Delete the old `GameData/RealisticVolumetricAurorae` before installing (CKAN and AVC do that for you). Your `PluginData/settings.cfg` is kept but it still has the old storm cadence, delete its `averageQuietDuration` lines or hit def on that row per body and Save.

Downloads: SpaceDock https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae, GitHub https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest, CKAN once its indexed. Full changelog on github.
