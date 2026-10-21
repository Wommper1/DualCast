<#
Builds the portable DualCast package: tests, publish, docs, zip, SHA256SUMS.txt,
and a check that no build-machine user name or folder is inside the package.
Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
Output goes to artifacts\ (git-ignored). Needs the .NET 10 SDK.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$proj = [xml](Get-Content -Raw DualBluetooth.csproj)
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> in DualBluetooth.csproj.' }

$art = Join-Path $root 'artifacts'
$stage = Join-Path $art 'stage\DualCast'
$zipName = "DualCast-$version-win-x64.zip"
$zip = Join-Path $art $zipName
if (Test-Path (Join-Path $art 'stage')) { Remove-Item -Recurse -Force (Join-Path $art 'stage') }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host "1/5 Tests"
dotnet test tests/DualBluetooth.Tests/DualBluetooth.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

Write-Host "2/5 Publish"
dotnet publish DualBluetooth.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force

Write-Host "3/5 Docs"
foreach ($f in 'README.md', 'LICENSE', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'VERIFICATION.md') {
    Copy-Item (Join-Path $root $f) $stage
}

Write-Host "4/5 Personal-data check"
$needles = @($env:USERNAME, $env:USERPROFILE, $root) | Where-Object { $_ -and $_.Length -ge 4 } | Select-Object -Unique
$latin1 = [Text.Encoding]::GetEncoding(28591)
$bad = @()
foreach ($file in Get-ChildItem $stage -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $views = @($latin1.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))
    if ($bytes.Length -gt 1) { $views += [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1) }
    foreach ($view in $views) {
        foreach ($n in $needles) {
            if ($view.IndexOf($n, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $bad += "$($file.Name): contains '$n'" }
        }
        if ($view -match '[A-Za-z]:\\Users\\[A-Za-z0-9._ -]+\\') { $bad += "$($file.Name): contains a C:\Users\... path" }
    }
}
if ($bad.Count) { $bad | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }; throw 'Personal data found in the package. Do not publish it.' }
Write-Host "  clean"

Write-Host "5/5 Zip and checksum"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -Path (Join-Path $art 'SHA256SUMS.txt') -Value "$hash  $zipName" -Encoding ascii
Write-Host "$zipName"
Write-Host "SHA-256 $hash"
Write-Host "Next: extract the zip on a clean PC, run the checks in VERIFICATION.md, then attach the zip and SHA256SUMS.txt to a GitHub pre-release."
