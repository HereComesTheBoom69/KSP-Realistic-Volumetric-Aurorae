# Releasing Realistic Volumetric Aurorae — GitHub, SpaceDock, CKAN, forum

Order matters a little: GitHub first (it gives you the source link and the
version-file URL the others want), then SpaceDock (it gives you the download
link and files the CKAN request for you), then the forum thread (it links to
both), and CKAN happens on its own after SpaceDock.

## 0. Build and check the release

1. `Aurora_Shader.shader` + `AuroraBlit.shader` → Unity → **RVA → Build Shader
   Bundle** → `AssetBundles/rva_shaders`.
2. Visual Studio, configuration **Release** → `Plugin/bin/Release/RealisticVolumetricAurorae.dll`.
   (Not the Dev build — the release DLL hides the dev-only settings.)
3. Install both into `GameData/RealisticVolumetricAurorae/` in KSP, start a
   flight, check KSP.log for
   `[RVA] Realistic Volumetric Aurorae (RVA) v<version> (release build)` and the
   renderer line; open Alt+F8 and confirm the title shows the version *without* "(dev build)".
4. Package: in PowerShell, from the kit folder

       .\Release\Package-Release.ps1 -FromGameData "C:\...\Kerbal Space Program\GameData"

   → `RealisticVolumetricAurorae-<version>.zip`. It lists what it packed and
   (since 1.0.1) **fails loudly if the layout is wrong**: the bundle must be the
   file `GameData/RealisticVolumetricAurorae/Bundles/rva_shaders` and the DLL
   the file `.../Plugins/RealisticVolumetricAurorae.dll` — directly inside
   those folders, never inside a folder of the same name. The 1.0.0 zip was
   hand-built with `Bundles/rva_shaders/rva_shaders` and
   `Plugins/RealisticVolumetricAurorae.dll/RealisticVolumetricAurorae.dll`;
   KSP still loaded the nested DLL, so the game looked fine while the plugin
   could not find its bundle and nobody who downloaded it saw an aurora.
   Use the script; if you must zip by hand, mirror this exactly:

       GameData/RealisticVolumetricAurorae/Bundles/rva_shaders            (a file, no extension)
       GameData/RealisticVolumetricAurorae/Plugins/RealisticVolumetricAurorae.dll

5. **Test the zip, not your working folder.** Move your own
   `GameData/RealisticVolumetricAurorae` aside, extract the release zip into
   GameData, start KSP, and check the log for `Bundle loaded` and `Shaders
   ready`. Put your folder back afterwards. This is the step that would have
   caught 1.0.0.

   Why `settings-defaults.cfg` and not `settings.cfg`: CKAN overwrites every
   file it installed when it upgrades a mod, and `settings.cfg` is the user's.
   The release ships none; the plugin uses its built-in defaults (the same
   values — the log says `no settings file ... using defaults`) and Alt+F8 →
   *Save* creates the file, which upgrades then leave alone.

## 1. GitHub (source + releases)  ~15 min

The forum rules require plugin source to be available, KSP-AVC wants a URL
for the `.version` file, and CKAN reviewers like a repository link.

1. Create a public repository, e.g. `HereComesTheBoom/RealisticVolumetricAurorae`.
2. Copy the kit into it. The `.gitignore` in the kit root already excludes
   build outputs, KSP's reference DLLs (never commit those) and
   `Dev/settings.cfg` is fine to publish.
4. Commit, push, then **Releases → Draft a new release**: tag `v1.0.0`, title
   `Realistic Volumetric Aurorae 1.0.0`, attach `RealisticVolumetricAurorae-1.0.0.zip`,
   paste the changelog (the *1.0.0* section of the README).
5. Fill the `.version` file's two URLs and commit again:
   - `URL`: the raw version file, e.g.
     `https://raw.githubusercontent.com/HereComesTheBoom69/KSP-Realistic-Volumetric-Aurorae/main/GameData/RealisticVolumetricAurorae/RealisticVolumetricAurorae.version`
     (KSP-AVC / MiniAVC compare the installed file against this one).
   - `DOWNLOAD`: the SpaceDock page once it exists (step 2), or the GitHub
     release page.
   Re-run the packager after that so the zip carries the filled-in file; the
   zip you upload to SpaceDock and GitHub should be the same bytes.

## 2. SpaceDock  ~10 min

1. Account on https://spacedock.info (use `HereComesTheBoom` so the author
   matches the licence and the forum).
2. **Add Mod**: game *Kerbal Space Program*; name `Realistic Volumetric Aurorae`;
   short description (one line — see the forum post's first paragraph);
   **licence: `CC BY-NC-SA 4.0`** from the dropdown (must match the LICENSE.txt
   in the zip — CKAN takes the licence from here); KSP version `1.12.5`;
   upload `RealisticVolumetricAurorae-1.0.0.zip`; version `1.0.0`; changelog.
3. Mod page → *Edit*: source code link = the GitHub repo; a background image
   and a few screenshots (the night-side orbit shots and one from the ground).
4. Mod page → *Edit* → tick **"Add to CKAN"** (SpaceDock calls it enabling
   CKAN). SpaceDock's bot then opens the NetKAN pull request by itself,
   usually within a day.

Each later version: *Upload a new version* on SpaceDock with the new zip and
changelog. CKAN indexes it automatically a few hours later; the forum thread
gets a post.

## 3. CKAN  — mostly waiting

- After the SpaceDock checkbox, watch https://github.com/KSP-CKAN/NetKAN/pulls
  for a PR named `RealisticVolumetricAurorae`. A volunteer reviews it (they
  check the zip layout, licence, KSP versions) and may ask questions in the PR
  — answer there. Merged → in CKAN clients within a few hours.
- If you'd rather not wait for the bot, or want the relationships set from
  the start: fork `KSP-CKAN/NetKAN`, add
  `NetKAN/RealisticVolumetricAurorae.netkan` (the draft is next to this file;
  fill in the SpaceDock mod id — the number in the SpaceDock URL — the forum
  URL and the repository URL), open the PR. The `supports` entries tell CKAN
  the mod works with EVE and ModuleManager without requiring them; the
  `install` stanza is the default CKAN would infer anyway.
- CKAN takes the version from the `.version` file in the zip (`$vref`), so
  the `.version` and the csproj `<Version>` must be bumped together on every
  release (the packager warns when they disagree).

## 4. Forum thread  ~20 min

Post in **Add-on Releases**. The add-on posting rules, applied to RVA:

- Title carries the KSP version and the mod version:
  `[1.12.x] Realistic Volumetric Aurorae (RVA) v1.0.0 — a volumetric aurora for Kerbin`
- The licence is named in the post **and** included in the download (it is:
  `LICENSE.txt` in the zip).
- A link to the source (GitHub) — plugins must have source available.
- Download links: SpaceDock (primary), GitHub release (mirror), CKAN once it
  is indexed.
- Screenshots: at least one in the post body; the forum shows the first one
  in listings.

`ForumPost.md` next to this file is the first post, ready to paste (the forum
takes Markdown-ish BBCode; headings become bold lines — paste, then tidy in
the editor). Fill in the two links and attach the images.

## 5. Every later release — the short list

1. Bump three things together: `Version` const in `RVABootstrap.cs`,
   `<Version>` in the csproj, `VERSION` in the `.version` file.
2. Rebuild what changed (shader → bundle; C# → DLL) **from the repository's
   source**, run the packager, then test the zip itself (step 0.5).
3. GitHub release (tag + zip), SpaceDock new version (same zip), forum post.
   CKAN follows SpaceDock on its own.
