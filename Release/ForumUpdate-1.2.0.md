<!-- update post for the thread. paste as a new reply, then edit the first post from
ForumPost.md and change the thread title to "[1.12.x] Realistic Volumetric Aurorae (RVA)
v1.2.0 - volumetric aurorae for Kerbin, Jool, Laythe, Duna and Eve". screenshots: Duna from
orbit, Eve from orbit, the oval behind the 2D clouds from high up -->

**1.2.0 is out. Duna and Eve have aurorae, and the clouds finally hide the aurora properly at every altitude.**

Duna's is a pale oxygen green with the CO2 violet-blue from mid height up, no red top, dim when its quiet and fully up in a storm. Eve's is a thin bright sheet way up above the clouds, green base, a blue-white haze over it and a soft blue top, and its storms spread the oval wide. Both tuned in orbit of each. Thats all five of the stock atmospheric bodies done.

The cloud stuff, for those running EVE volumetrics:
- **The flat 2D cloud layers hide the aurora now too.** Once your high enough that EVE swaps the volumetrics for the 2D textures those are the only clouds, and the far side of the oval used to show straight through them. Now only clouds (2D or volumetric) and terrain mask the aurora, nothing else, at any altitude.
- The cloud shaped mask that slid around over the far side of the oval when you zoomed out is gone (we were reading EVE's buffer after it went stale).
- The band at the horizon is closed. EVE's own march leaves a ray that grazes the planet translucent and a bright aurora on black showed it. Theres a per body *Horizon boost* slider for it, nothing changes looking down through the clouds.
- The cloud top is read from EVE's configs so any cloud pack works without touching a setting.

Also fixed: the aurora peeking through a thin sliver of the limb from orbit (Duna mostly), the "split" that trailed a fast camera move or time warp, and the map view renders small bodies at a higher scale for the same cost so you get a band instead of a blur. Laythe's preset is retuned to three arcs like Kerbin's.

Delete the old `GameData/RealisticVolumetricAurorae` before installing (CKAN and AVC do that for you). Your `PluginData/settings.cfg` is kept, the new keys just take there defaults. If you saved one before and want the new Laythe look, hit *preset values* for Laythe in the window and Save.

Downloads: SpaceDock https://spacedock.info/mod/4689/Realistic%20Volumetric%20Aurorae, GitHub https://github.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/releases/latest, CKAN once it has the new version. Full changelog on github.
