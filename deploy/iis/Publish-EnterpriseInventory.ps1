<#
.SYNOPSIS
    React uygulamasını ve API'yi derleyip tek bir IIS site klasörü olarak hazırlar.

.DESCRIPTION
    Derleme makinesinde (Windows veya Linux; .NET 10 SDK ve Node.js 22) çalıştırılır. Sunucuya dokunmaz; yalnızca
    -OutputPath klasörünü oluşturur:

      1. npm ci ve npm run build (src/EnterpriseInventory.Web)
      2. dotnet publish -c Release (framework-dependent; sunucuda .NET 10 Hosting Bundle çalıştırır)
      3. React derlemesi wwwroot klasörüne kopyalanır; API onu aynı adresten sunar
      4. Klasör denetlenir: web.config in-process ve Production, appsettings.Development.json yok,
         wwwroot\index.html var, anahtar veya sertifika dosyası yok
      5. release.json: commit, derleme zamanı ve dosyaların SHA-256 özetleri (sunucuya kopyalanan dosyalar
         bununla karşılaştırılabilir)

    Her yayın kendi klasörüne çıkar; önceki klasör silinmez, geri dönüş için IIS'te o klasöre dönülür.

.PARAMETER OutputPath
    Oluşturulacak klasör. Önceden var olmamalı.

.EXAMPLE
    pwsh ./deploy/iis/Publish-EnterpriseInventory.ps1 -OutputPath C:\Releases\EnterpriseInventory\2026-10-10
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $Configuration = 'Release'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Invoke-Tool {
    param([string] $Description, [string] $FilePath, [string[]] $Arguments)
    Write-Host "==> $Description"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Description başarısız oldu (çıkış kodu $LASTEXITCODE)." }
}

$root = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..'))
$web = [IO.Path]::Combine($root, 'src', 'EnterpriseInventory.Web')
$api = [IO.Path]::Combine($root, 'src', 'EnterpriseInventory.Api', 'EnterpriseInventory.Api.csproj')
$output = [IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $output) { throw "$output zaten var. Her yayın yeni bir klasöre çıkar; öncekiler geri dönüş için saklanır." }

Push-Location $web
try {
    Invoke-Tool 'npm ci' 'npm' @('ci', '--no-audit', '--no-fund')
    Invoke-Tool 'npm run build' 'npm' @('run', 'build')
}
finally {
    Pop-Location
}

Invoke-Tool 'dotnet publish' 'dotnet' @('publish', $api, '-c', $Configuration, '-o', $output, '--nologo')

$wwwroot = [IO.Path]::Combine($output, 'wwwroot')
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Copy-Item -Path ([IO.Path]::Combine($web, 'dist', '*')) -Destination $wwwroot -Recurse

# --- Denetim ---
$problems = New-Object System.Collections.Generic.List[string]
$webConfigPath = [IO.Path]::Combine($output, 'web.config')
if (Test-Path -LiteralPath $webConfigPath) {
    [xml] $webConfig = Get-Content -LiteralPath $webConfigPath -Raw
    $module = $webConfig.SelectSingleNode('//aspNetCore')
    $environment = $webConfig.SelectSingleNode("//aspNetCore/environmentVariables/environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
    if ($null -eq $module -or $module.hostingModel -ne 'inprocess') { $problems.Add('web.config: hostingModel inprocess değil') }
    if ($null -eq $module -or $module.arguments -notmatch 'EnterpriseInventory\.Api\.dll') { $problems.Add('web.config: arguments EnterpriseInventory.Api.dll değil') }
    if ($null -eq $environment -or $environment.value -ne 'Production') { $problems.Add('web.config: ASPNETCORE_ENVIRONMENT Production değil') }
}
else {
    $problems.Add('web.config yok')
}
if (Test-Path -LiteralPath ([IO.Path]::Combine($output, 'appsettings.Development.json'))) { $problems.Add('appsettings.Development.json yayın klasöründe') }
if (-not (Test-Path -LiteralPath ([IO.Path]::Combine($wwwroot, 'index.html')))) { $problems.Add('wwwroot/index.html yok') }
$keyFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Extension -match '^\.(pfx|p12|pem|key|snk)$' })
foreach ($file in $keyFiles) { $problems.Add("Anahtar veya sertifika dosyası: $($file.FullName)") }

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[HATA ] $_" -ForegroundColor Red }
    throw 'Yayın klasörü denetimden geçmedi; sunucuya kopyalamayın.'
}

# --- release.json ---
$commit = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $commit = (& git -C $root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = $null }
    elseif ((& git -C $root status --porcelain) ) { $commit = "$commit (commit edilmemiş değişikliklerle)" }
}
$files = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered] @{
        path   = $_.FullName.Substring($output.Length + 1).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$release = [ordered] @{
    commit  = $commit
    builtAt = (Get-Date).ToUniversalTime().ToString('o')
    files   = @($files)
}
$release | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ([IO.Path]::Combine($output, 'release.json')) -Encoding UTF8

Write-Host "[PASS ] Yayın klasörü hazır: $output ($(@($files).Count) dosya)" -ForegroundColor Green
