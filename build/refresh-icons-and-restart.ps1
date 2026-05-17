# One-shot icon iteration: regenerate the .ico files, rebuild Debug (so the new tray icon
# gets embedded into the .dll and the new ApplicationIcon is baked into the .exe), kill any
# locally-built Debug instance (NOT a Velopack-installed copy), then relaunch.
#
# Usage: pwsh build\refresh-icons-and-restart.ps1
#
# Tweak the icon design by editing build\generate-placeholder-icons.ps1, then run this script.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# Force UTF-8 on the console so MSBuild's localized output (French in our case) doesn't
# come back as mojibake like "g_n_ration" in PowerShell 5.1's default cp1252 console.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==> Regenerating placeholder icons"
& "$PSScriptRoot\generate-placeholder-icons.ps1"

Write-Host ""
Write-Host "==> Stopping locally-built Debug instances (sparing any Velopack install)"
# Filter by path so we don't accidentally kill a v0.1.x install at %LOCALAPPDATA%\EmilsClaudeLaunchpad\
Get-Process -Name EmilsClaudeLaunchpad -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "    killing PID $($_.Id) at $($_.Path)"
        Stop-Process -Id $_.Id -Force
    }
Start-Sleep -Milliseconds 400

Write-Host ""
Write-Host "==> Rebuilding Debug"
$csproj = Join-Path $repoRoot 'src\EmilsClaudeLaunchpad\EmilsClaudeLaunchpad.csproj'
dotnet build $csproj -c Debug -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "==> Launching"
$exe = Join-Path $repoRoot 'src\EmilsClaudeLaunchpad\bin\Debug\net10.0-windows\EmilsClaudeLaunchpad.exe'
Start-Process -FilePath $exe
Write-Host "Done. New icon should be visible in the tray + on the .exe file."
