# Builds the Windows and Linux x64 players, checks that each starts, packs them and
# publishes a GitHub release with both.
#
#   pwsh tools/release.ps1                 # the version in Project Settings (Player > Version)
#   pwsh tools/release.ps1 -Version 0.2.0
#
# Before running: close the Unity Editor (one Unity per project), set the version in
# Project Settings, and write the release notes in docs/releases/v<version>.md.
# Needs the GitHub CLI (gh), signed in, and the Unity modules "Windows Build Support"
# and "Linux Build Support (Mono)". The Linux build is checked in WSL (Ubuntu); without
# WSL that check is skipped with a warning.
param(
    [string]$Version,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe",
    [string]$Project = (Resolve-Path "$PSScriptRoot\..").Path,
    [string]$WslDistro = "Ubuntu"
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

$stageRoot = "$Project\build-release"
Remove-Item -Recurse -Force "$Project\build", "$Project\build-linux", $stageRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stageRoot | Out-Null

function Build-Player([string]$method, [string]$exe, [string]$log) {
    Write-Host "Building $method..."
    $p = Start-Process $Unity -PassThru -ArgumentList @("-batchmode", "-quit", "-projectPath", "`"$Project`"",
        "-executeMethod", "AlchemistsArsenal.EditorTools.BuildWindows.$method",
        "-buildPath", "`"$exe`"", "-logFile", "`"$Project\$log`"")
    if (-not $p.WaitForExit(1500000)) { $p.Kill(); throw "The $method build took more than 25 minutes (see $log)." }
    if ($p.ExitCode -ne 0) { throw "The $method build failed (see $log)." }
}

function Stage([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    Get-ChildItem $from | Where-Object { $_.Name -notlike "*DontShip*" -and $_.Name -ne "smoke.log" } | Copy-Item -Destination $to -Recurse
}

# --- Windows -------------------------------------------------------------------
Build-Player "Build" "$Project\build\AlchemistsArsenal.exe" "build.log"
Write-Host "Checking that the Windows build starts..."
$smoke = Start-Process "$Project\build\AlchemistsArsenal.exe" -PassThru -ArgumentList @("-smoketest", "-screen-fullscreen", "0", "-logFile", "`"$Project\build\smoke.log`"")
if (-not $smoke.WaitForExit(120000)) { $smoke.Kill(); throw "Windows: the game did not reach the main menu in 2 minutes." }
if (-not (Select-String -Path "$Project\build\smoke.log" -Pattern "SMOKE: main menu reached" -Quiet)) { throw "Windows: the game did not reach the main menu (see build\smoke.log)." }
Stage "$Project\build" "$stageRoot\win\AlchemistsArsenal"
$winZip = "$stageRoot\AlchemistsArsenal-Windows.zip"
Compress-Archive -Path "$stageRoot\win\AlchemistsArsenal" -DestinationPath $winZip -CompressionLevel Optimal

# --- Linux ---------------------------------------------------------------------
Build-Player "BuildLinux" "$Project\build-linux\AlchemistsArsenal.x86_64" "build-linux.log"
Stage "$Project\build-linux" "$stageRoot\linux\AlchemistsArsenal"
$linuxTar = "$stageRoot\AlchemistsArsenal-Linux.tar.gz"
$wslStage = (wsl -d $WslDistro wslpath -a ($stageRoot -replace '\\', '/')) 2>$null
if ($LASTEXITCODE -eq 0 -and $wslStage) {
    # Packed in Linux, so the executable bit is in the archive; then started there once.
    wsl -d $WslDistro -- bash -lc "cd '$wslStage/linux' && chmod +x AlchemistsArsenal/AlchemistsArsenal.x86_64 && tar -czf ../AlchemistsArsenal-Linux.tar.gz AlchemistsArsenal"
    if ($LASTEXITCODE -ne 0) { throw "Linux: packing failed." }
    Write-Host "Checking that the Linux build starts (WSL)..."
    wsl -d $WslDistro -- bash -lc "rm -rf /tmp/aa-smoke && mkdir /tmp/aa-smoke && tar -xzf '$wslStage/AlchemistsArsenal-Linux.tar.gz' -C /tmp/aa-smoke && cd /tmp/aa-smoke/AlchemistsArsenal && timeout 150 ./AlchemistsArsenal.x86_64 -smoketest -screen-fullscreen 0 -logFile /tmp/aa-smoke/smoke.log; grep -q 'SMOKE: main menu reached' /tmp/aa-smoke/smoke.log"
    if ($LASTEXITCODE -ne 0) { throw "Linux: the game did not reach the main menu (see /tmp/aa-smoke/smoke.log in WSL)." }
} else {
    Write-Warning "No WSL '$WslDistro': packing the Linux build with Windows tar, unchecked. Players must chmod +x the executable."
    tar -czf $linuxTar -C "$stageRoot\linux" AlchemistsArsenal
}

foreach ($f in $winZip, $linuxTar) { "{0}: {1:N1} MB" -f (Split-Path $f -Leaf), ((Get-Item $f).Length / 1MB) }

Write-Host "Publishing $tag..."
gh release create $tag $winZip $linuxTar --title "Alchemist's Arsenal $Version" --notes-file $notes --target main
