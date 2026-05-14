# Local Velopack pack — produces Releases\Setup.exe without pushing anywhere.
# Usage: .\build\publish.ps1 0.1.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "==> Restoring vpk local tool"
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)" }

    Write-Host "==> dotnet publish (self-contained, single-file)"
    $publishDir = Join-Path $root 'publish'
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    dotnet publish src\EmilsClaudeLaunchpad\EmilsClaudeLaunchpad.csproj `
        -c Release `
        -r win-x64 `
        --self-contained `
        -p:PublishingForVelopack=true `
        -p:Version=$Version `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    Write-Host "==> vpk pack"
    $releasesDir = Join-Path $root 'Releases'
    dotnet vpk pack `
        --packId EmilsClaudeLaunchpad `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe EmilsClaudeLaunchpad.exe `
        --packTitle "Emil's Claude Launchpad" `
        --outputDir $releasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

    Write-Host ""
    Write-Host "==> Done. Setup at: $releasesDir\EmilsClaudeLaunchpad-win-Setup.exe"
}
finally {
    Pop-Location
}
