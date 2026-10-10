param([switch]$SuppressValidation)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$payloadPath = Join-Path $repositoryRoot 'artifacts\msi\payload'

dotnet publish (Join-Path $repositoryRoot 'UsageViewer.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $payloadPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$installerArgs = @('--configuration', 'Release')
if ($SuppressValidation) {
    $installerArgs += '-p:SuppressValidation=true'
}

dotnet build (Join-Path $PSScriptRoot 'UsageViewer.Installer.wixproj') @installerArgs
exit $LASTEXITCODE
