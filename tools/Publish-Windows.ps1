param(
    [string] $InnoCompiler = (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "JPL.csproj"
$publishDir = Join-Path $repo "releases\Windows\Cric360"
$installerScript = Join-Path $repo "Installer\Cric360.iss"
$version = [xml](Get-Content -LiteralPath $project -Raw)
$appVersion = $version.Project.PropertyGroup | Where-Object { $_.ApplicationDisplayVersion } | Select-Object -First 1 -ExpandProperty ApplicationDisplayVersion

if (-not $appVersion) { throw "ApplicationDisplayVersion was not found in JPL.csproj." }
if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup 6 compiler not found at '$InnoCompiler'. Install Inno Setup 6 or pass -InnoCompiler with its ISCC.exe path."
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $project -f net9.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed with exit code $LASTEXITCODE." }

Push-Location $repo
try {
    & $InnoCompiler "/DAppVersion=$appVersion" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Host "Created installer: $(Join-Path $repo "releases\Windows\Cric360-Setup-$appVersion.exe")"
