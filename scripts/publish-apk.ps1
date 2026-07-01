<#
.SYNOPSIS
    Locally builds the Capacitor Android APK and publishes it to the production backend.

.USAGE
    cd D:\OutSourcing\Project\EcommerceApp
    .\scripts\publish-apk.ps1 -VersionName "1.2.0" -VersionCode 5 -ReleaseNotes "Bug fixes"

.NOTES
    Requires:
      - Node.js, npm (admin/ dependencies installed)
      - Android SDK at D:\Android\sdk  (ANDROID_HOME must be set)
      - Java 21 in PATH
      - JAVA_HOME set or java in PATH
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$VersionName,

    [Parameter(Mandatory=$true)]
    [int]$VersionCode,

    [string]$ReleaseNotes = "",

    # Production API base URL
    [string]$ApiBase = "http://194.5.152.74:5500",

    # Login credentials  (can also set env vars CI_EMAIL / CI_PASSWORD)
    [string]$Email    = $env:CI_EMAIL,
    [string]$Password = $env:CI_PASSWORD
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ROOT   = Split-Path $PSScriptRoot
$ADMIN  = Join-Path $ROOT "admin"
$ANDROID = Join-Path $ADMIN "android"
$APK    = Join-Path $ANDROID "app\build\outputs\apk\debug\app-debug.apk"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function OK($msg)   { Write-Host "    OK: $msg" -ForegroundColor Green }
function Err($msg)  { Write-Host "    ERROR: $msg" -ForegroundColor Red; exit 1 }

# ── Validate credentials ──────────────────────────────────────────────────────
if (-not $Email)    { $Email    = Read-Host "CI Email (superadmin)" }
if (-not $Password) { $Password = Read-Host "CI Password" -AsSecureString | ConvertFrom-SecureString -AsPlainText }

# ── 1. Angular production build ───────────────────────────────────────────────
Step "Building Angular (production)…"
Push-Location $ADMIN
try {
    npx ng build --configuration production
    if ($LASTEXITCODE -ne 0) { Err "ng build failed" }
    OK "Angular build complete — dist/admin/"
} finally { Pop-Location }

# ── 2. Capacitor sync ─────────────────────────────────────────────────────────
Step "Syncing Capacitor…"
Push-Location $ADMIN
try {
    npx cap sync android
    if ($LASTEXITCODE -ne 0) { Err "cap sync failed" }
    OK "Capacitor sync complete"
} finally { Pop-Location }

# ── 3. Gradle assembleDebug ───────────────────────────────────────────────────
Step "Building APK with Gradle…"
Push-Location $ANDROID
try {
    .\gradlew.bat assembleDebug --no-daemon
    if ($LASTEXITCODE -ne 0) { Err "Gradle build failed" }
    OK "APK built: $APK"
} finally { Pop-Location }

if (-not (Test-Path $APK)) { Err "APK file not found at: $APK" }
$SizeMB = [math]::Round((Get-Item $APK).Length / 1MB, 1)
Write-Host "    APK size: $SizeMB MB" -ForegroundColor Gray

# ── 4. Login to production API ────────────────────────────────────────────────
Step "Logging in to $ApiBase…"
$LoginBody = @{ email = $Email; password = $Password; loginContext = $null } | ConvertTo-Json
try {
    $LoginResp = Invoke-RestMethod -Uri "$ApiBase/api/auth/login" `
        -Method POST -ContentType "application/json" -Body $LoginBody
    $Token = $LoginResp.token
    if (-not $Token) { Err "Login succeeded but no token returned" }
    OK "Authenticated as $($LoginResp.fullName ?? $Email)"
} catch {
    Err "Login failed: $_"
}

# ── 5. Upload APK to backend ──────────────────────────────────────────────────
Step "Uploading APK to production ($VersionName / code $VersionCode)…"

# PowerShell multipart form upload
$boundary = [System.Guid]::NewGuid().ToString()
$apkBytes = [System.IO.File]::ReadAllBytes($APK)
$apkName  = Split-Path $APK -Leaf

$bodyParts = @(
    "--$boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"$apkName`"`r`nContent-Type: application/vnd.android.package-archive`r`n`r`n",
    $apkBytes,
    "`r`n--$boundary`r`nContent-Disposition: form-data; name=`"versionName`"`r`n`r`n$VersionName",
    "`r`n--$boundary`r`nContent-Disposition: form-data; name=`"versionCode`"`r`n`r`n$VersionCode",
    "`r`n--$boundary`r`nContent-Disposition: form-data; name=`"releaseNotes`"`r`n`r`n$ReleaseNotes",
    "`r`n--$boundary--`r`n"
)

$bodyStream = New-Object System.IO.MemoryStream
foreach ($part in $bodyParts) {
    if ($part -is [byte[]]) { $bodyStream.Write($part, 0, $part.Length) }
    else { $bytes = [System.Text.Encoding]::UTF8.GetBytes($part); $bodyStream.Write($bytes, 0, $bytes.Length) }
}
$bodyBytes = $bodyStream.ToArray()

try {
    $UploadResp = Invoke-RestMethod -Uri "$ApiBase/api/appversions" `
        -Method POST `
        -Headers @{ Authorization = "Bearer $Token" } `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body $bodyBytes
    OK "Published! Version $($UploadResp.versionName) (code $($UploadResp.versionCode)) is now active."
} catch {
    Err "Upload failed: $($_.Exception.Message)"
}

Write-Host "`nDone. Staff can download from Settings → Mobile App section." -ForegroundColor Green
