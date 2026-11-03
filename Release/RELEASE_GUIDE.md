# Release checklist (github, spacedock, ckan, forum)

Order: GitHub first (gives the source link and the .version url the others want), then
SpaceDock (download link, and it files the CKAN request itself), then the forum post that
links both. CKAN happens on its own after SpaceDock.

## 0. Build + check

1. Shader bundle: Unity, RVA > Build Shader Bundle, output AssetBundles/rva_shaders.
2. Plugin: VS, **Release** config, Plugin/bin/Release/RealisticVolumetricAurorae.dll. NOT
   the dev build, the release dll hides the dev only settings.
3. Install both into GameData/RealisticVolumetricAurorae, start a flight, check KSP.log for
   `[RVA] Realistic Volumetric Aurorae (RVA) v<version> (release build)` and the renderer
   line. Alt+F8, the title shouldnt say (dev build).
4. Package. PowerShell from the kit folder:

       .\Release\Package-Release.ps1 -FromGameData "C:\...\Kerbal Space Program\GameData"

   Writes RealisticVolumetricAurorae-<version>.zip, lists what it packed and fails loudly
   if the layout is wrong. The bundle must be the FILE
   GameData/RealisticVolumetricAurorae/Bundles/rva_shaders and the dll the file
   .../Plugins/RealisticVolumetricAurorae.dll, directly in those folders, never inside a
   folder of the same name. 1.0.0 was zipped by hand with Bundles/rva_shaders/rva_shaders
   and nobody who downloaded it saw an aurora. Use the script. If you really have to zip
   by hand:

       GameData/RealisticVolumetricAurorae/Bundles/rva_shaders            (file, no extension)
       GameData/RealisticVolumetricAurorae/Plugins/RealisticVolumetricAurorae.dll
       GameData/RealisticVolumetricAurorae/Config/*.cfg

5. DLL and bundle from the same source: after rebuilding either, start KSP and look for
   `[RVA] Shader build 1131 matches the plugin` in KSP.log (status line in the window
   shows it too). `SHADER BUILD ... DOES NOT MATCH PLUGIN` means one of them is stale,
   rebuild it from the current kit before testing anything else.
6. **Test the zip, not your working folder.** Move your own
   GameData/RealisticVolumetricAurorae aside, extract the release zip into GameData, start
   KSP, check the log for `Bundle loaded` and `Shaders ready`. Put your folder back after.
   This is the step that wouldve caught 1.0.0.

   Why settings-defaults.cfg and not settings.cfg: CKAN overwrites every file it installed
   on upgrade and settings.cfg is the users. The release ships no settings.cfg, the plugin
   runs on built in defaults (same values, log says `no settings file ... using defaults`)
   and Alt+F8 Save creates the file, which upgrades then leave alone.

### Things to eyeball before a release

Clouds (1.2.0): Kerbin orbit, zoom the camera out past the volumetric fade out (status line
says the volumetrics buffer isnt read and the flat layers are occluding). No hard edged
patches sliding around the far side of the oval, no spherical mask between the atmosphere
and the limb, stars AND aurora visible through the gaps in the 2D clouds. Then zoom back in,
volumetrics occlude per pixel again, and the far side of the oval doesnt show through the
cloud band at the horizon (horizon boost slider, debug mode 10 shows the buffer). Eve same
thing from orbit. Duna: zoom out past the texture switch, aurora stays cut at the limb and
there is no fuzzy flickering patch where the far aurora meets the terrain, also under warp.

Terrain (1.1.1): the Mun at 15 km with Kerbin on the horizon, aurora cut at the ridges on
the limb with the camera close and zoomed all the way out, stays cut as the orbit runs.
KSP.log on start: `[RVA:Terrain] Mun: terrain map 1024x512 ready (from PluginData/terrain)`
not "building", for all the shipped bodies. No fade of Kerbins aurora with the camera angle.

1.1.0 stuff: Jool orbit 300-900 km (inside the band, no rings, no seam at the lower
horizon). Jool's limb from Laythe surface and orbit. Laythes own aurora from the surface at
~52 deg and low orbit (nothing through the sea/terrain, no horizontal stripes across the
far oval from an equatorial orbit). Laythe storm from the ground near the equator (manual
intensity 1, arcs fan out down to overhead and back). Eclipse: warp on Laythes day side
until Jool covers the sun, log says "in Jool's shadow, daylight fade lifted". SOI crossing
Laythe -> Jool, log says "Main body changed". Map view on Jool and Kerbin, tracking station.
With SPVE: no second flat aurora on any body.

Temporal: whip the camera round the vessel and stop, no trailing aurora, no smear at the
vessel edges, settles with no grain and sharper than with the toggle off. Warp through a
storm, no ghosting. FPS with it on/off at your render scale, and on at half the scale.

Storms: status line "quiet, next storm in N min", N up to ~45 first time, doesnt count down
while paused, doesnt run faster at 4x phys warp. Revert / tracking station and back, N
carries on. Storm now brings one on, line goes "storm rising", "at its peak", "fading".
Minutes slider down to 1, next storm within the minute.

Terrain maps: built once per body from the PQS height function (~20 s at 2.5 ms a frame)
and kept in PluginData/terrain/<body>.rvaterrain, 1 MB each. Only built where another bodys
aurora is in view and the body has terrain, so stock thats Mun, Minmus (Kerbin), Laythe,
Vall, Tylo, Bop, Pol (Jool/Laythe), Ike (Duna), Gilly (Eve). Jool has no PQS so nothing to
map. Build them all before packaging so users never see the smooth stand in: Alt+F12 set
orbit around each one, wait for `terrain map 1024x512 ready` in the log, copy the
.rvaterrain files from your install into the kits PluginData/terrain and commit them. The
dev window also has a "Build the terrain map for <body> now" button in the diagnostics
section that builds one regardless. The packager ships whatever that folder holds and warns
if its missing. Your mod list doesnt matter, the map samples the PQS height which Kopernicus
leaves at stock without a pack and Parallax doesnt touch, and each file carries the height
at 48 check points so a user with different terrain there gets a fresh map built.

## 1. GitHub

Forum rules want plugin source available, AVC wants a url for the .version file, CKAN
reviewers like a repo link.

1. Public repo, HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae. The .gitignore excludes
   build output, the KSP reference dlls and Dev/EVE_NOTES.md, never commit those.
   Dev/settings.cfg is fine.
2. Commit, push, Releases > Draft a new release: tag v<version>, title
   `Realistic Volumetric Aurorae <version>`, attach the zip, paste the release notes.
3. The .version files two urls:
   - URL: the raw version file on main
     (https://raw.githubusercontent.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/main/GameData/RealisticVolumetricAurorae/RealisticVolumetricAurorae.version)
   - DOWNLOAD: spacedock page or the github releases page.
   Run the packager again if you touched it so the zip has the right file. Same bytes to
   SpaceDock and GitHub.

## 2. SpaceDock

Account HereComesTheBoom so the author matches the license and forum. Add Mod: KSP, name
Realistic Volumetric Aurorae, one line description (first para of the forum post),
**license CC BY-NC-SA 4.0 from the dropdown** (CKAN takes the license from here so it has to
match LICENSE.txt), KSP 1.12.5, upload the zip, version, changelog. Edit: source = github
repo, background + a few screenshots. Tick "Add to CKAN", the bot opens the NetKAN PR
itself (done, PR 11697).

Later versions: Upload a new version with the zip + changelog. CKAN picks it up a few hours
later.

## 3. CKAN

- netkan-bot opened https://github.com/KSP-CKAN/NetKAN/pull/11697 on 2026-10-14. Lives on
  its own branch until merged so it wont show in the NetKAN folder, look at the PRs.
- Bot requests get merged in sweeps every week or two, the sweeps take the ones with no
  open question. Ours has one (MM and EVE as supports not depends, + the graphics tag) so
  the 10-17 and 10-28 sweeps skipped it. Queue for hand edited ones was ~140 PRs end of Oct.
- Way out: open our own PR with the finished file thats next to this guide,
  RealisticVolumetricAurorae.netkan. Fork KSP-CKAN/NetKAN, NetKAN folder, Add file,
  paste, commit, open PR titled `Add RealisticVolumetricAurorae`, body says its the authors
  version of #11697 with the changes asked for there. One comment on #11697 pointing at it.
  The file keeps the shape the bot proposed (github release first, spacedock fallback) and
  adds license, tags, supports, links and the install stanza.
- What the file relies on: every github release has the one zip and a v<version> tag, the
  spacedock version is the same number, the .version in the zip gives CKAN the KSP versions
  ($vref), so .version and the csproj <Version> get bumped together (packager warns if they
  dont match). License is written in because github doesnt detect the NC creative commons
  ones.
- Once merged every later release is picked up on its own.

## 4. Forum

Add-on Releases. Rules as they apply here:
- Title carries the KSP and mod version:
  `[1.12.x] Realistic Volumetric Aurorae (RVA) v1.2.1 - volumetric aurorae for Kerbin, Jool, Laythe, Duna and Eve`
- License named in the post AND in the download (LICENSE.txt in the zip).
- Link to source. Plugins must have source available.
- Download links: SpaceDock, GitHub release, CKAN once indexed.
- At least one screenshot in the body, the forum shows the first one in listings.

ForumPost.md is the first post ready to paste. Forum takes markdownish bbcode, headings
become bold lines. Paste, tidy, fill links, attach images.

## 5. Every release, short version

1. Bump three things together: Version const in RVABootstrap.cs, <Version> in the csproj,
   VERSION in the .version file.
2. Rebuild what changed from the repo source, run the packager, test the zip itself.
3. GitHub release (tag v<version>, zip, body from Release/ReleaseNotes-<version>.md),
   SpaceDock new version (same zip, same notes), reply in the thread
   (Release/ForumUpdate-<version>.md), refresh the first post from ForumPost.md and bump
   the thread title. CKAN follows on its own.
