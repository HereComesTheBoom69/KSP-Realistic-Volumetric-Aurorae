<#
  Realistic Volumetric Aurorae (RVA) — release packager.
  Builds RealisticVolumetricAurorae-<version>.zip for SpaceDock / CKAN / GitHub releases
  from your Release-configuration DLL and the built shader bundle.

  Usage (PowerShell), either:
    .\Release\Package-Release.ps1 -Bundle "C:\...\AssetBundles\rva_shaders" -Dll "C:\...\Plugin\bin\Release\RealisticVolumetricAurorae.dll"
  or take both from your KSP install:
    .\Release\Package-Release.ps1 -FromGameData "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\GameData"
  Optional: -OutDir <folder>   (default: the kit root)

  The zip contains exactly what CKAN should install:
    GameData/RealisticVolumetricAurorae/
      Bundles/rva_shaders
      Plugins/RealisticVolumetricAurorae.dll
      PluginData/settings-defaults.cfg     <- REFERENCE copy. settings.cfg itself is NOT shipped:
                                              CKAN overwrites every installed file on upgrade and
                                              settings.cfg belongs to the user. The plugin's built-in
                                              defaults are the same values; Alt+F8 -> Save creates it.
      Compatibility/RVA_SPVE_*.cfg
      RealisticVolumetricAurorae.version
      LICENSE.txt
      README.md                            <- the user-facing readme (Release/README.md)
#>
param(
    [string]$Bundle,
    [string]$Dll,
    [string]$FromGameData,
    [string]$OutDir
)
$ErrorActionPreference = "Stop"
$kit = Split-Path -Parent $PSScriptRoot
$src = Join-Path $kit "GameData\RealisticVolumetricAurorae"
if (-not $OutDir) { $OutDir = $kit }

if ($FromGameData) {
    $Bundle = Join-Path $FromGameData "RealisticVolumetricAurorae\Bundles\rva_shaders"
    $Dll    = Join-Path $FromGameData "RealisticVolumetricAurorae\Plugins\RealisticVolumetricAurorae.dll"
}
if (-not $Bundle -or -not (Test-Path $Bundle)) { throw "Shader bundle not found: '$Bundle' (build it in Unity: RVA > Build Shader Bundle)" }
if (-not $Dll    -or -not (Test-Path $Dll))    { throw "DLL not found: '$Dll' (build the Release configuration in Visual Studio)" }
if ($Dll -match '\\Dev\\') { Write-Warning "The DLL path contains '\Dev\' — make sure you are packaging the RELEASE build, not the dev build." }

$verJson = Get-Content (Join-Path $src "RealisticVolumetricAurorae.version") -Raw | ConvertFrom-Json
$version = "{0}.{1}.{2}" -f $verJson.VERSION.MAJOR, $verJson.VERSION.MINOR, $verJson.VERSION.PATCH
$fileVer = (Get-Item $Dll).VersionInfo.FileVersion
Write-Host "Packaging RVA $version   (DLL file version $fileVer, bundle $((Get-Item $Bundle).Length) bytes)"
if ($fileVer -and -not $fileVer.StartsWith($version)) { Write-Warning "DLL file version $fileVer does not match the .version file ($version) — rebuild the DLL after bumping <Version> in the csproj." }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("rva_release_" + [guid]::NewGuid().ToString("N"))
$dst   = Join-Path $stage "GameData\RealisticVolumetricAurorae"
foreach ($d in "Bundles", "Plugins", "PluginData", "Compatibility") { New-Item -ItemType Directory -Path (Join-Path $dst $d) -Force | Out-Null }

Copy-Item $Bundle (Join-Path $dst "Bundles\rva_shaders")
Copy-Item $Dll    (Join-Path $dst "Plugins\RealisticVolumetricAurorae.dll")
Copy-Item (Join-Path $src "Compatibility\*.cfg") (Join-Path $dst "Compatibility\")
Copy-Item (Join-Path $src "RealisticVolumetricAurorae.version") $dst
Copy-Item (Join-Path $src "LICENSE.txt") $dst
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $dst "README.md")     # the user-facing readme

$ref = Get-Content (Join-Path $src "PluginData\settings.cfg") -Raw
$ref = "// REFERENCE COPY - not read by the mod. The built-in defaults are these same values.`r`n" +
       "// To customise: use the in-game window (Alt+F8, 'Save to settings.cfg'), which creates settings.cfg`r`n" +
       "// next to this file, or copy this file to settings.cfg and edit it. settings.cfg is never`r`n" +
       "// overwritten by an update; this reference copy is.`r`n" + $ref
Set-Content -Path (Join-Path $dst "PluginData\settings-defaults.cfg") -Value $ref -NoNewline -Encoding UTF8

$zip = Join-Path $OutDir "RealisticVolumetricAurorae-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$tar = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($tar) {
    # Windows 10+ bsdtar writes standard zips with forward-slash entry names
    & $tar.Source -a -c -f $zip -C $stage GameData
} else {
    Compress-Archive -Path (Join-Path $stage "GameData") -DestinationPath $zip
}
Remove-Item $stage -Recurse -Force

Write-Host "`nRelease zip: $zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
try   { $z.Entries | Where-Object { $_.Length -gt 0 } | ForEach-Object { Write-Host ("  {0,10}  {1}" -f $_.Length, $_.FullName) } }
finally { $z.Dispose() }
Write-Host "`nNext: Release\RELEASE_GUIDE.md"
