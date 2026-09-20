<#
.SYNOPSIS
  Build a self-contained Windows x64 release and (if Velopack's `vpk` is installed) a Setup.exe.

  pwsh scripts\publish-windows.ps1

  Install vpk once:  dotnet tool install -g vpk
  Output: out\win-x64\  and  out\Releases\  (Setup.exe + update packages)
#>
$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Rid = 'win-x64'
$Version = ([xml](Get-Content "$Root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { throw '<Version> not found in Directory.Build.props' }
if (-not (Test-Path "$Root\natives\$Rid")) { throw "natives\$Rid missing — run scripts\fetch-natives.ps1 first" }

$Out = "$Root\out\$Rid"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
Write-Host "→ dotnet publish $Rid v$Version"
dotnet publish "$Root\src\UltrastarDJ.App" -c Release -r $Rid --self-contained `
  -p:PublishSingleFile=false -p:DebugType=none -nologo -v q -o $Out

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
  Write-Host "✓ published to $Out (install Velopack for an installer: dotnet tool install -g vpk)"
  exit 0
}

Write-Host "→ vpk pack"
& $vpk.Source pack -u UltrastarDJ -v $Version -p $Out -e UltrastarDJ.exe `
  --packTitle 'Ultrastar DJ' --packAuthors 'Reto Küpfer' `
  --icon "$Root\src\UltrastarDJ.App\Assets\icon.ico" -o "$Root\out\Releases"
Write-Host "✓ out\Releases\UltrastarDJ-win-Setup.exe"
