# Produces the single-file, self-contained Windows x64 build that ships with each
# GitHub release. Matches the command used for v0.2.0-preview.
#
# Usage:
#   pwsh ./publish.ps1            # clean Release publish
#   pwsh ./publish.ps1 -SkipClean # faster rebuild on top of the existing Release output
#
# Output:
#   ImageGenerator.MAUI/bin/Release/net10.0-windows10.0.22621.0/win-x64/publish/ImageGenerator.MAUI.exe
#
# Close the running app first — MSBuild can't overwrite a locked .exe.

param(
    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project  = Join-Path $repoRoot 'ImageGenerator.MAUI\ImageGenerator.MAUI.csproj'
$tfm      = 'net10.0-windows10.0.22621.0'
$rid      = 'win-x64'

if (-not $SkipClean) {
    Write-Host 'Cleaning Release output…' -ForegroundColor Cyan
    dotnet clean $project -c Release -f $tfm -r $rid | Out-Null
}

Write-Host 'Publishing self-contained single-file exe…' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -f $tfm `
    -r $rid `
    --self-contained `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $repoRoot "ImageGenerator.MAUI\bin\Release\$tfm\$rid\publish\ImageGenerator.MAUI.exe"
if (-not (Test-Path $exe)) {
    throw "Expected exe not found at $exe"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Published: $exe" -ForegroundColor Green
Write-Host "Size:      $sizeMb MB" -ForegroundColor Green
Write-Host ""
Write-Host 'Next: gh release create vX.Y.Z-preview "' -NoNewline
Write-Host $exe -NoNewline -ForegroundColor Yellow
Write-Host '" --prerelease --target master --title "…" --notes "…"'
