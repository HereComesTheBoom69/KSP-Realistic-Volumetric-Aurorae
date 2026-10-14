# Changelog

## 1.0.1 — 2026-10-14

**Fixes the 1.0.0 download, which did not work for anyone.** The 1.0.0 zip had
the shader bundle and the plugin DLL one folder too deep (`Bundles/rva_shaders/rva_shaders`,
`Plugins/RealisticVolumetricAurorae.dll/...`). KSP loaded the nested DLL, so the
game looked fine, but the plugin could not find its shaders and disabled itself.

- Release package rebuilt with the correct layout; the packaging script now
  refuses to produce a zip whose layout is wrong.
- The plugin now also looks for `rva_shaders` anywhere under its own folder if it
  is not at the expected path, and logs a warning instead of giving up.
- If the bundle is truly missing, a dialog at the main menu says which file is
  missing and how to fix it, instead of a silent black sky.
- The two optional SPVE compatibility patches are now gated with `:NEEDS[SPVE]`,
  so ModuleManager no longer logs "Deleting root node" about them when SPVE is
  not installed.
- The shipped DLL is built from the published source (1.0.0's predated the
  first-launch settings write).

Delete the old `GameData/RealisticVolumetricAurorae` folder before installing,
or let CKAN / KSP-AVC do it.

## 1.0.0 — 2026-10-13

First release.
