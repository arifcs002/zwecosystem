<#
.SYNOPSIS
    Builds the Capacitor Android APK and places it in backend/wwwroot/apks/
    After deploying, register it via: Mobile App Releases → Pick from server

.USAGE
    cd D:\OutSourcing\Project\EcommerceApp
    .\scripts\build-apk.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ROOT    = Split-Path $PSScriptRoot
$ADMIN   = Join-Path $ROOT "admin"
$ANDROID = Join-Path $ADMIN "android"
$APK_SRC = Join-Path $ANDROID "app\build\outputs\apk\debug\app-debug.apk"
$APK_DST = Join-Path $ROOT "backend\wwwroot\apks"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function OK($msg)   { Write-Host "    $msg" -ForegroundColor Green }

# ── 1. Angular production build ───────────────────────────────────────────────
Step "Building Angular (production)..."
Push-Location $ADMIN
npx ng build --configuration production
if ($LASTEXITCODE -ne 0) { throw "ng build failed" }
Pop-Location
OK "Angular build done"

# ── 2. Capacitor sync ─────────────────────────────────────────────────────────
Step "Syncing Capacitor..."
Push-Location $ADMIN
npx cap sync android
if ($LASTEXITCODE -ne 0) { throw "cap sync failed" }
Pop-Location
OK "Capacitor sync done"

# ── 3. Gradle assembleDebug ───────────────────────────────────────────────────
Step "Building APK..."
Push-Location $ANDROID
.\gradlew.bat assembleDebug --no-daemon
if ($LASTEXITCODE -ne 0) { throw "Gradle build failed" }
Pop-Location
OK "APK built"

# ── 4. Copy to wwwroot/apks/ ──────────────────────────────────────────────────
Step "Copying APK to backend/wwwroot/apks/..."
if (-not (Test-Path $APK_DST)) { New-Item -ItemType Directory -Path $APK_DST | Out-Null }

# Use timestamp in filename so multiple versions can coexist
$timestamp = Get-Date -Format "yyyyMMdd_HHmm"
$destName  = "app-$timestamp.apk"
$destPath  = Join-Path $APK_DST $destName

Copy-Item $APK_SRC $destPath
$sizeMB = [math]::Round((Get-Item $destPath).Length / 1MB, 1)

OK "Copied: backend\wwwroot\apks\$destName ($sizeMB MB)"

Write-Host @"

Done!
Next steps:
  1. git push  (deploy to server)
  2. Admin panel → Mobile App Releases → Pick from server
  3. Select '$destName' → fill version info → Publish Release

"@ -ForegroundColor Yellow
