# Builds SoundMaster.exe and packages it for testers: dist\SoundMaster-<version>.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

& (Join-Path $PSScriptRoot 'build.bat')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$exe = Join-Path $root 'SoundMaster.exe'
$version = (Get-Item $exe).VersionInfo.ProductVersion
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "SoundMaster-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item $exe $stage
Copy-Item (Join-Path $PSScriptRoot 'TESTERS.txt') (Join-Path $stage 'README.txt')

$zip = "$stage.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entries written by hand: PowerShell 5.1 / .NET Framework zip helpers store backslash paths
# that non-Windows unzippers turn into odd file names.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($f in Get-ChildItem $stage -File) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName,
            "SoundMaster-$version/$($f.Name)", 'Optimal') | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Packaged $zip"
Write-Host "SHA256 $hash"
