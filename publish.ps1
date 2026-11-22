# Produces the single-file, self-contained Windows x64 build that ships with each
# GitHub release.
#
# Usage (PowerShell 7+ or Windows PowerShell 5.1):
#   pwsh ./publish.ps1                # clean Release publish (PowerShell 7+)
#   powershell -File ./publish.ps1    # Windows PowerShell 5.1 - ships with Windows
#   pwsh ./publish.ps1 -SkipClean     # faster rebuild on top of the existing output
#
# From Git Bash on Windows, `pwsh` is not on PATH by default; use `powershell -File ...`
# or install PowerShell 7.
#
# Output:
#   ImageGenerator.MAUI/bin/Release/net11.0-windows10.0.22621.0/win-x64/publish/Emberforge.exe
#   (the project folder/csproj stay ImageGenerator.MAUI; only the binary is Emberforge.exe via AssemblyName)

param(
    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'
$repoRoot   = $PSScriptRoot
$project    = Join-Path $repoRoot 'ImageGenerator.MAUI\ImageGenerator.MAUI.csproj'
$tfm        = 'net11.0-windows10.0.22621.0'
$rid        = 'win-x64'
$publishDir = Join-Path $repoRoot "ImageGenerator.MAUI\bin\Release\$tfm\$rid\publish"
$exe        = Join-Path $publishDir 'Emberforge.exe'

# Fail fast if the app is running and locking Emberforge.exe
if (Get-Process -Name 'Emberforge' -ErrorAction SilentlyContinue) {
    throw 'Emberforge is currently running. Close it before publishing.'
}

# Catch TFM drift between publish.ps1 and the csproj early
$csprojText = Get-Content $project -Raw
if ($csprojText -notmatch [regex]::Escape($tfm)) {
    throw "TFM '$tfm' was not found in $project. Update `$tfm in publish.ps1 to match <TargetFrameworks>."
}

if (-not $SkipClean) {
    Write-Host 'Cleaning Release output...' -ForegroundColor Cyan
    dotnet clean $project -c Release -f $tfm -r $rid -p:WindowsPackageType=None | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet clean failed with exit code $LASTEXITCODE"
    }
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
}

# The NETSDK1244 warning (IncludeAllContentForSelfExtract is legacy) is expected.
# Microsoft.WindowsAppSDK.SingleFile.targets sets IncludeAllContentForSelfExtract=true and
# fails the build without it (DLL SxS redirection), and the SDK then derives
# IncludeNativeLibrariesForSelfExtract=true from it. As a result the exe extracts its full
# content (~440 MB) to %TEMP%\.net\Emberforge\<hash> on the first start of each version.
Write-Host 'Publishing self-contained single-file exe...' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -f $tfm `
    -r $rid `
    --self-contained `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $exe)) {
    throw "Expected exe not found at $exe"
}

# Warn if any non-exe/non-pdb assets were emitted beside Emberforge.exe
$extraFiles = Get-ChildItem $publishDir -Recurse -File | Where-Object {
    $_.FullName -ne $exe -and $_.Extension -ne '.pdb'
}
if ($extraFiles) {
    Write-Warning "Found $($extraFiles.Count) extra file(s) in publish\; test running Emberforge.exe from an empty temp folder before shipping only the .exe:"
    $extraFiles | ForEach-Object { Write-Warning ("  " + $_.FullName.Substring($publishDir.Length + 1)) }
}

$version = ([xml]$csprojText).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "No <Version> found in $project"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Published: $exe" -ForegroundColor Green
Write-Host "Size:      $sizeMb MB" -ForegroundColor Green
Write-Host ""
Write-Host "Next: gh release create v$version `"" -NoNewline
Write-Host $exe -NoNewline -ForegroundColor Yellow
Write-Host '" --target master --title "..." --notes "..."'
