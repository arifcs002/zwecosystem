param (
    [Parameter(Mandatory=$true)]
    [string]$DbPassword
)

$appSettingsPath = "appsettings.json"
$examplePath = "appsettings.example.json"

if (-not (Test-Path $examplePath)) {
    Write-Error "appsettings.example.json not found! Cannot create base settings."
    exit
}

$config = Get-Content -Raw -Path $examplePath | ConvertFrom-Json
$newConnectionString = "Host=194.5.152.74;Port=5432;Database=ecommerce_db;Username=dev_user;Password=$DbPassword;Include Error Detail=true;"
$config.ConnectionStrings.DefaultConnection = $newConnectionString
$config | ConvertTo-Json -Depth 10 | Set-Content -Path $appSettingsPath

Write-Host "Successfully updated appsettings.json securely!" -ForegroundColor Green
