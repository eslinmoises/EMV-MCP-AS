<#
.SYNOPSIS
Clean Deployment & Synchronization Protocol for EMV Advance Steel Plugin.

.DESCRIPTION
Eliminates file conflicts, stale assemblies, and redundant DLLs between
build output and Autodesk ApplicationPlugins bundles in AppData and ProgramData.
Adheres to PackageContents.xml module paths.
#>

[CmdletBinding()]
param(
    [switch]$ForceKillAutoCAD,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " EMV-MCP-AS: Clean Plugin Deployment Protocol" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Check for running AutoCAD / Advance Steel processes
$acadProcs = Get-Process -Name "acad" -ErrorAction SilentlyContinue
if ($acadProcs) {
    if ($ForceKillAutoCAD) {
        Write-Warning "Closing running AutoCAD process (PID: $($acadProcs.Id))..."
        $acadProcs | Stop-Process -Force
        Start-Sleep -Seconds 2
    } else {
        Write-Warning "AutoCAD is currently running! DLL files may be locked."
        Write-Warning "Run with -ForceKillAutoCAD or close AutoCAD before deploying to prevent locked file errors."
    }
}

# 2. Build Release Binaries
$projectPath = "src/as_plugin/EMV.AdvanceSteel.Plugin.csproj"
if (-not $SkipBuild) {
    Write-Host "`n[1/4] Compiling plugin in Release mode..." -ForegroundColor Yellow
    $buildOutput = dotnet build $projectPath -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed! Fix compilation errors before deploying."
        exit $LASTEXITCODE
    }
    Write-Host "Build successful (0 errors)." -ForegroundColor Green
}

$sourceDir = "src/as_plugin/bin/Release/net8.0-windows"
$mainDll = Join-Path $sourceDir "EMV.AdvanceSteel.Plugin.dll"
if (-not (Test-Path $mainDll)) {
    Write-Error "Build artifact not found: $mainDll"
    exit 1
}

$sourceHash = (Get-FileHash -Path $mainDll -Algorithm SHA256).Hash
Write-Host "Source Assembly SHA256: $sourceHash" -ForegroundColor DarkGray

# 3. Define Bundle Targets
$bundleTargets = @(
    "$env:APPDATA/Autodesk/ApplicationPlugins/EMV-AdvanceSteel.bundle",
    "$env:ProgramData/Autodesk/ApplicationPlugins/EMV-AdvanceSteel.bundle"
)

# 4. Clean & Deploy to Each Target
foreach ($bundle in $bundleTargets) {
    Write-Host "`n[2/4] Deploying to: $bundle" -ForegroundColor Yellow

    if (-not (Test-Path $bundle)) {
        New-Item -ItemType Directory -Path $bundle -Force | Out-Null
    }

    # Ensure PackageContents.xml is present and correctly configured
    $xmlSource = "src/as_plugin/PackageContents.xml"
    if (Test-Path $xmlSource) {
        Copy-Item -Path $xmlSource -Destination "$bundle/PackageContents.xml" -Force
    }

    $contentsDir = "$bundle/Contents"
    $releaseDir = "$bundle/Contents/Release"

    # Clean legacy loose files in Contents/ that cause version collisions
    $legacyFiles = @(
        "$contentsDir/EMV.AdvanceSteel.Plugin.dll",
        "$contentsDir/EMV.AdvanceSteel.Plugin.pdb"
    )
    foreach ($f in $legacyFiles) {
        if (Test-Path $f) {
            Write-Host "  Removing conflicting loose file: $f" -ForegroundColor DarkYellow
            Remove-Item -Path $f -Force -ErrorAction SilentlyContinue
        }
    }

    # Ensure target Release directory exists
    if (-not (Test-Path $releaseDir)) {
        New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    }

    # Copy all necessary Release files (main DLL, PDB, deps.json, and Roslyn dependencies)
    $filesToCopy = Get-ChildItem -Path $sourceDir -File
    foreach ($file in $filesToCopy) {
        Copy-Item -Path $file.FullName -Destination $releaseDir -Force
    }

    # Verify deployed assembly hash
    $deployedDll = "$releaseDir/EMV.AdvanceSteel.Plugin.dll"
    if (Test-Path $deployedDll) {
        $deployedHash = (Get-FileHash -Path $deployedDll -Algorithm SHA256).Hash
        if ($deployedHash -eq $sourceHash) {
            Write-Host "  Verification OK: Assembly SHA256 matches build output." -ForegroundColor Green
        } else {
            Write-Error "  Hash mismatch on deployed assembly: $deployedDll"
        }
    }
}

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host " Plugin Deployment Complete - Zero Conflicts!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
