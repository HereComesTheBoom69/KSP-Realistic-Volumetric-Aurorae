
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
if (-not $Bundle -or -not (Test-Path $Bundle -PathType Leaf)) { throw "Shader bundle not found or not a file: '$Bundle' (build it in Unity: RVA > Build Shader Bundle; pass the FILE AssetBundles\rva_shaders, not a folder)" }
if (-not $Dll    -or -not (Test-Path $Dll    -PathType Leaf)) { throw "DLL not found or not a file: '$Dll' (build the Release configuration in Visual Studio; pass the FILE bin\Release\RealisticVolumetricAurorae.dll, not a folder)" }
if ((Get-Item $Bundle).Length -lt 10KB) { throw "'$Bundle' is only $((Get-Item $Bundle).Length) bytes - that is not the shader bundle (did you pass the .manifest?)" }
if ($Dll -match '\\Dev\\') { Write-Warning "The DLL path contains '\Dev\' - make sure you are packaging the RELEASE build, not the dev build." }

$verJson = Get-Content (Join-Path $src "RealisticVolumetricAurorae.version") -Raw | ConvertFrom-Json
$version = "{0}.{1}.{2}" -f $verJson.VERSION.MAJOR, $verJson.VERSION.MINOR, $verJson.VERSION.PATCH
$fileVer = (Get-Item $Dll).VersionInfo.FileVersion
Write-Host "Packaging RVA $version   (DLL file version $fileVer, bundle $((Get-Item $Bundle).Length) bytes)"
if ($fileVer -and -not $fileVer.StartsWith($version)) { Write-Warning "DLL file version $fileVer does not match the .version file ($version) - rebuild the DLL after bumping <Version> in the csproj." }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("rva_release_" + [guid]::NewGuid().ToString("N"))
$dst   = Join-Path $stage "GameData\RealisticVolumetricAurorae"
foreach ($d in "Bundles", "Plugins", "PluginData", "Compatibility", "Config") { New-Item -ItemType Directory -Path (Join-Path $dst $d) -Force | Out-Null }

Copy-Item $Bundle (Join-Path $dst "Bundles\rva_shaders")
Copy-Item $Dll    (Join-Path $dst "Plugins\RealisticVolumetricAurorae.dll")
Copy-Item (Join-Path $src "Compatibility\*.cfg") (Join-Path $dst "Compatibility\")
Copy-Item (Join-Path $src "Config\*.cfg")        (Join-Path $dst "Config\")
Copy-Item (Join-Path $src "RealisticVolumetricAurorae.version") $dst

$terrain = Join-Path $src "PluginData\terrain"
if (Test-Path $terrain) {
    $maps = Get-ChildItem $terrain -Filter "*.rvaterrain"
    if ($maps.Count -gt 0) {
        New-Item -ItemType Directory -Path (Join-Path $dst "PluginData\terrain") -Force | Out-Null
        Copy-Item (Join-Path $terrain "*.rvaterrain") (Join-Path $dst "PluginData\terrain\")
        Write-Host ("Terrain maps: " + (($maps | ForEach-Object { $_.BaseName }) -join ", "))
    }
} else { Write-Warning "No PluginData\terrain folder in the kit, shipping no terrain maps (see RELEASE_GUIDE)" }

$lic = Join-Path $src "LICENSE.txt"
if (-not (Test-Path $lic)) { $lic = Join-Path $kit "LICENSE.txt" }
if (-not (Test-Path $lic)) { throw "LICENSE.txt not found in '$src' or '$kit' - it must ship in the zip" }
Copy-Item $lic (Join-Path $dst "LICENSE.txt")
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $dst "README.md")

$ref = Get-Content (Join-Path $src "PluginData\settings.cfg") -Raw
$ref = "// reference copy of the shipped values, not read by the mod. edit settings.cfg instead`r`n" +
       "// (Alt+F8 > Save creates it, updates never touch it. this file gets overwritten)`r`n" + $ref
Set-Content -Path (Join-Path $dst "PluginData\settings-defaults.cfg") -Value $ref -NoNewline -Encoding UTF8

$zip = Join-Path $OutDir "RealisticVolumetricAurorae-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$tar = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($tar) {

    & $tar.Source -a -c -f $zip -C $stage GameData
} else {
    Compress-Archive -Path (Join-Path $stage "GameData") -DestinationPath $zip
}
Remove-Item $stage -Recurse -Force

Write-Host "`nRelease zip: $zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($z.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $z.Entries | Where-Object { $_.Length -gt 0 } | ForEach-Object { Write-Host ("  {0,10}  {1}" -f $_.Length, $_.FullName) }

    $must = @("GameData/RealisticVolumetricAurorae/Bundles/rva_shaders",
              "GameData/RealisticVolumetricAurorae/Plugins/RealisticVolumetricAurorae.dll",
              "GameData/RealisticVolumetricAurorae/RealisticVolumetricAurorae.version",
              "GameData/RealisticVolumetricAurorae/LICENSE.txt",
              "GameData/RealisticVolumetricAurorae/Config/Kerbin.cfg",
              "GameData/RealisticVolumetricAurorae/Config/Jool.cfg",
              "GameData/RealisticVolumetricAurorae/Config/Laythe.cfg",
              "GameData/RealisticVolumetricAurorae/Config/Duna.cfg",
              "GameData/RealisticVolumetricAurorae/Config/Eve.cfg")
    foreach ($m in $must) {
        $e = $z.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $m }
        if (-not $e)            { throw "ZIP LAYOUT ERROR: missing entry '$m'" }
        if ($e.Length -le 0)    { throw "ZIP LAYOUT ERROR: '$m' is empty or is a folder, not a file" }
    }
    $nested = $entries | Where-Object { $_ -like "GameData/RealisticVolumetricAurorae/Bundles/rva_shaders/*" -or $_ -like "GameData/RealisticVolumetricAurorae/Plugins/RealisticVolumetricAurorae.dll/*" }
    if ($nested) { throw "ZIP LAYOUT ERROR: the bundle or the DLL is nested inside a folder of the same name: $($nested -join ', ')" }
    if ($entries -contains "GameData/RealisticVolumetricAurorae/PluginData/settings.cfg") { throw "ZIP LAYOUT ERROR: settings.cfg must not ship (CKAN would overwrite users' settings on upgrade)" }
    Write-Host "`nLayout check passed: bundle, DLL, .version and LICENSE are directly where the plugin expects them."
}
catch   { $z.Dispose(); Remove-Item $zip -Force -ErrorAction SilentlyContinue; throw }
finally { $z.Dispose() }
Write-Host "`nNext: Release\RELEASE_GUIDE.md"
