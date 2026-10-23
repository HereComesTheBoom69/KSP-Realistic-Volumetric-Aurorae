# Releasing Realistic Volumetric Aurorae: GitHub, SpaceDock, CKAN, forum

The order matters a little. GitHub first, because it gives you the source link and the
version file URL the others want. Then SpaceDock, which gives you the download link and
files the CKAN request for you. Then the forum thread, which links to both. CKAN happens
on its own after SpaceDock.

## 0. Build and check the release

1. Build the shader bundle: `Aurora_Shader.shader` and `AuroraBlit.shader` in Unity,
   menu **RVA > Build Shader Bundle**, output `AssetBundles/rva_shaders`.
2. Build the plugin: Visual Studio, configuration **Release**, output
   `Plugin/bin/Release/RealisticVolumetricAurorae.dll`. Not the Dev build. The release
   DLL hides the dev-only settings.
3. Install both into `GameData/RealisticVolumetricAurorae/` in KSP, start a flight and
   check KSP.log for `[RVA] Realistic Volumetric Aurorae (RVA) v<version> (release build)`
   and the renderer line. Open Alt+F8 and confirm the title shows the version without
   "(dev build)".
4. Package. In PowerShell, from the kit folder:

       .\Release\Package-Release.ps1 -FromGameData "C:\...\Kerbal Space Program\GameData"

   This writes `RealisticVolumetricAurorae-<version>.zip`. It lists what it packed and,
   since 1.0.1, **fails loudly if the layout is wrong**. The bundle must be the file
   `GameData/RealisticVolumetricAurorae/Bundles/rva_shaders` and the DLL the file
   `.../Plugins/RealisticVolumetricAurorae.dll`, directly inside those folders and never
   inside a folder of the same name. The 1.0.0 zip was built by hand with
   `Bundles/rva_shaders/rva_shaders` and
   `Plugins/RealisticVolumetricAurorae.dll/RealisticVolumetricAurorae.dll`. KSP still
   loaded the nested DLL, so the game looked fine while the plugin could not find its
   bundle, and nobody who downloaded it saw an aurora. Use the script. If you have to zip
   by hand, mirror this exactly:

       GameData/RealisticVolumetricAurorae/Bundles/rva_shaders            (a file, no extension)
       GameData/RealisticVolumetricAurorae/Plugins/RealisticVolumetricAurorae.dll
       GameData/RealisticVolumetricAurorae/Config/*.cfg                  (the per-body presets, 1.1.0)

4b. **1.1.0 checks.** Jool orbit at 300 to 900 km (inside the band: no rings, no seam at
   the band's lower horizon). The view of Jool's limb from Laythe's surface and from
   Laythe orbit (its aurora hidden by Laythe's terrain and clouds, visible above the
   horizon). Laythe's own aurora from its surface at about 52 degrees latitude and from
   low orbit (green base, red top, nothing showing through the sea or the terrain, no
   horizontal stripes across the far oval from an equatorial orbit). A Laythe storm from
   the ground near the equator (window, Storm section, manual intensity 1: the arcs fan
   out from the oval down to overhead, and draw back as you lower it). An eclipse: time
   warp on Laythe's day side until Jool covers the sun. The log says "Laythe: in Jool's
   shadow, daylight fade lifted" and the aurora comes up over the lit side, then goes
   again when the sun returns. An SOI crossing from Laythe to Jool (the local aurora
   rebuilds and the log says "Main body changed"). The map view zoomed to Jool and to
   Kerbin, and the tracking station. With SPVE installed: no second, flat aurora on Jool
   or Laythe (the removal patch found SPVE's layers; if one is still there, see the
   patch's header).

   **Temporal accumulation** (window, Performance section, on by default): whip the
   camera round the vessel and stop. There should be no aurora trailing behind the
   motion and no smear at the vessel's edges. Let it settle: no grain, and the ribbons
   sharper than with the toggle off at the same render scale. Time warp through a storm:
   the arcs move without ghosting. Toggle it off and on: the picture rebuilds within a
   second. Then the measurement: FPS with the toggle on and off at your render scale,
   and with it on at half that scale.

   **Storms** (45 min of real time apart): the window's status line reads "quiet, next
   storm in N min". N is up to about 45 the first time, never counts down during a pause,
   and does not run faster at 4x physics warp. Revert the flight or visit the tracking
   station and come back, and N carries on from where it was. The *Storm now* button
   brings one on at once and the line then says "storm rising to ...", "at its peak ..."
   and "fading ...". Drag the minutes slider down to 1 and the next storm is due within
   the minute.

4c. **DLL and bundle from the same source.** After rebuilding either, start KSP and find
   `[RVA] Shader build 1110 matches the plugin` in KSP.log (the window's status line
   shows it too). A `SHADER BUILD ... DOES NOT MATCH PLUGIN` warning means one of the
   two is stale. Rebuild it from the current kit before testing anything else.
5. **Test the zip, not your working folder.** Move your own
   `GameData/RealisticVolumetricAurorae` aside, extract the release zip into GameData,
   start KSP, and check the log for `Bundle loaded` and `Shaders ready`. Put your folder
   back afterwards. This is the step that would have caught 1.0.0.

   Why `settings-defaults.cfg` and not `settings.cfg`: CKAN overwrites every file it
   installed when it upgrades a mod, and `settings.cfg` is the user's. The release ships
   no settings.cfg. The plugin uses its built-in defaults (the same values; the log says
   `no settings file ... using defaults`) and Alt+F8 then *Save* creates the file, which
   upgrades then leave alone.

## 1. GitHub (source and releases), about 15 min

The forum rules require plugin source to be available, KSP-AVC wants a URL for the
`.version` file, and CKAN reviewers like a repository link.

1. Create a public repository, for example `HereComesTheBoom/RealisticVolumetricAurorae`.
2. Copy the kit into it. The `.gitignore` in the kit root already excludes build
   outputs and KSP's reference DLLs (never commit those). `Dev/settings.cfg` is fine to
   publish.
3. Commit, push, then **Releases > Draft a new release**: tag `v1.0.0`, title
   `Realistic Volumetric Aurorae 1.0.0`, attach `RealisticVolumetricAurorae-1.0.0.zip`,
   paste the changelog (the *1.0.0* section of the README).
4. Fill in the `.version` file's two URLs and commit again:
   - `URL`: the raw version file, for example
     `https://raw.githubusercontent.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/main/GameData/RealisticVolumetricAurorae/RealisticVolumetricAurorae.version`
     (KSP-AVC and MiniAVC compare the installed file against this one).
   - `DOWNLOAD`: the SpaceDock page once it exists (step 2), or the GitHub release page.
   Run the packager again after that so the zip carries the filled-in file. The zip you
   upload to SpaceDock and GitHub should be the same bytes.

## 2. SpaceDock, about 10 min

1. Account on https://spacedock.info. Use `HereComesTheBoom` so the author matches the
   licence and the forum.
2. **Add Mod**: game *Kerbal Space Program*; name `Realistic Volumetric Aurorae`; short
   description (one line, see the forum post's first paragraph); **licence
   `CC BY-NC-SA 4.0`** from the dropdown (it must match the LICENSE.txt in the zip, since
   CKAN takes the licence from here); KSP version `1.12.5`; upload
   `RealisticVolumetricAurorae-1.0.0.zip`; version `1.0.0`; changelog.
3. Mod page, *Edit*: source code link = the GitHub repo; a background image and a few
   screenshots (the night-side orbit shots and one from the ground).
4. Mod page, *Edit*, tick **"Add to CKAN"** (SpaceDock calls it enabling CKAN).
   SpaceDock's bot then opens the NetKAN pull request by itself, usually within a day.

For each later version: *Upload a new version* on SpaceDock with the new zip and
changelog. CKAN indexes it automatically a few hours later, and the forum thread gets a
post.

## 3. CKAN, mostly waiting

- After the SpaceDock checkbox, watch https://github.com/KSP-CKAN/NetKAN/pulls for a PR
  named `RealisticVolumetricAurorae`. A volunteer reviews it (they check the zip layout,
  the licence and the KSP versions) and may ask questions in the PR. Answer there. Once
  it is merged the mod shows up in CKAN clients within a few hours.
- If you would rather not wait for the bot, or want the relationships set from the
  start: fork `KSP-CKAN/NetKAN`, add `NetKAN/RealisticVolumetricAurorae.netkan` (the
  draft is next to this file; fill in the SpaceDock mod id, which is the number in the
  SpaceDock URL, the forum URL and the repository URL) and open the PR. The `supports`
  entries tell CKAN the mod works with EVE and ModuleManager without requiring them. The
  `install` stanza is the default CKAN would infer anyway.
- CKAN takes the version from the `.version` file in the zip (`$vref`), so the `.version`
  file and the csproj `<Version>` must be bumped together on every release. The packager
  warns when they disagree.

## 4. Forum thread, about 20 min

Post in **Add-on Releases**. The add-on posting rules, applied to RVA:

- The title carries the KSP version and the mod version:
  `[1.12.x] Realistic Volumetric Aurorae (RVA) v1.1.0 - volumetric aurorae for Kerbin, Jool and Laythe`
- The licence is named in the post **and** included in the download (it is, as
  `LICENSE.txt` in the zip).
- A link to the source (GitHub). Plugins must have their source available.
- Download links: SpaceDock (primary), GitHub release (mirror), CKAN once it is indexed.
- Screenshots: at least one in the post body. The forum shows the first one in listings.

`ForumPost.md` next to this file is the first post, ready to paste. The forum takes
Markdown-ish BBCode; headings become bold lines. Paste, then tidy in the editor. Fill in
the two links and attach the images.

## 5. Every later release, the short list

1. Bump three things together: the `Version` const in `RVABootstrap.cs`, `<Version>` in
   the csproj, and `VERSION` in the `.version` file.
2. Rebuild what changed (shader to bundle, C# to DLL) **from the repository's source**,
   run the packager, then test the zip itself (step 0.5).
3. GitHub release (tag `v<version>`, the zip, body from `Release/ReleaseNotes-<version>.md`),
   SpaceDock new version (same zip, the same notes as its changelog), a reply in the
   forum thread (`Release/ForumUpdate-<version>.md`), and the first post refreshed from
   `ForumPost.md` with the thread title's version bumped. CKAN follows SpaceDock on its
   own.
