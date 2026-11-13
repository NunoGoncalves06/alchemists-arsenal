# Builds the Windows x64 player, checks that it starts, zips it and publishes a GitHub release.
#
#   pwsh tools/release.ps1                 # the version in Project Settings (Player > Version)
#   pwsh tools/release.ps1 -Version 0.2.0
#
# Before running: close the Unity Editor (one Unity per project), set the version in
# Project Settings, and write the release notes in docs/releases/v<version>.md.
# Needs the GitHub CLI (gh), signed in.
param(
    [string]$Version,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe",
    [string]$Project = (Resolve-Path "$PSScriptRoot\..").Path
)
$ErrorActionPreference = "Stop"

if (-not $Version) {
    $Version = (Select-String -Path "$Project\ProjectSettings\ProjectSettings.asset" -Pattern "^\s*bundleVersion:\s*(.+)$").Matches[0].Groups[1].Value.Trim()
}
$tag = "v$Version"
$notes = "$Project\docs\releases\$tag.md"
if (-not (Test-Path $notes)) { throw "Write the release notes first: $notes" }
gh release view $tag 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists. Bump the version." }

$out = "$Project\build"
$stage = "$Project\build-release\AlchemistsArsenal"
$zip = "$Project\build-release\AlchemistsArsenal-Windows.zip"
Remove-Item -Recurse -Force $out, "$Project\build-release" -ErrorAction SilentlyContinue

Write-Host "Building $tag..."
$p = Start-Process $Unity -PassThru -ArgumentList @("-batchmode", "-quit", "-projectPath", "`"$Project`"",
    "-executeMethod", "AlchemistsArsenal.EditorTools.BuildWindows.Build",
    "-buildPath", "`"$out\AlchemistsArsenal.exe`"", "-logFile", "`"$Project\build.log`"")
if (-not $p.WaitForExit(1200000)) { $p.Kill(); throw "The build took more than 20 minutes (see build.log)." }
if ($p.ExitCode -ne 0) { throw "The build failed (see build.log)." }

Write-Host "Checking that it starts..."
$smoke = Start-Process "$out\AlchemistsArsenal.exe" -PassThru -ArgumentList @("-smoketest", "-screen-fullscreen", "0", "-logFile", "`"$out\smoke.log`"")
if (-not $smoke.WaitForExit(120000)) { $smoke.Kill(); throw "The game did not reach the main menu in 2 minutes." }
if (-not (Select-String -Path "$out\smoke.log" -Pattern "SMOKE: main menu reached" -Quiet)) { throw "The game did not reach the main menu (see build\smoke.log)." }

Write-Host "Zipping..."
New-Item -ItemType Directory -Force $stage | Out-Null
Get-ChildItem $out | Where-Object { $_.Name -notlike "*DontShip*" -and $_.Name -ne "smoke.log" } | Copy-Item -Destination $stage -Recurse
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
"{0}: {1:N1} MB" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)

Write-Host "Publishing $tag..."
gh release create $tag $zip --title "Alchemist's Arsenal $Version" --notes-file $notes --target main
