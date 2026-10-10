<#
.SYNOPSIS
    IIS'te uygulama havuzunu, HTTPS siteyi, anahtar ve log klasörlerini ve ortam ayarlarını kurar veya günceller.

.DESCRIPTION
    Windows Server üzerinde, yönetici PowerShell'inde (5.1 veya 7, WebAdministration modülü) çalıştırılır.
    Önce -WhatIf ile ne yapacağını gösterin; tekrar çalıştırılabilir (var olanı günceller).

      - Uygulama havuzu: No Managed Code, Integrated, 64 bit, AlwaysRunning, boşta kapanma yok;
        kimlik ApplicationPoolIdentity veya bir gMSA
      - Data Protection anahtar klasörü: kalıtım kapalı; yalnızca SYSTEM, Administrators ve havuz kimliği
        (anahtarlar ayrıca DPAPI ile makineye şifrelenir)
      - Log klasörü: havuz kimliği yazabilir; site klasörü: havuz kimliği okuyabilir
      - Site: yalnızca HTTPS (443, SNI, verilen sertifika); -HttpRedirect ile 80 numaralı port da açılır ve
        uygulama https adresine yönlendirir
      - Ayarlar uygulama havuzunun ortam değişkenlerine yazılır (applicationHost.config). Her yayında değişen
        site klasörüne (web.config, appsettings.*.json) sunucuya özel değer yazılmaz.

    Gizli değerler: SQL Server'a gMSA ile Integrated Security kullanılır, bağlantı cümlesinde parola kabul edilmez.
    AD servis hesabının parolası yalnızca -DirectoryServiceAccount ile (Get-Credential) alınır ve havuzun ortam
    değişkenine yazılır; ekrana yazılmaz. applicationHost.config yalnızca yöneticiler ve SYSTEM tarafından okunur.

.EXAMPLE
    $settings = @{
        'ConnectionStrings__DefaultConnection' = 'Server=<sql sunucusu>;Database=<veritabanı>;Integrated Security=true;Encrypt=true'
        'ActiveDirectory__Domain'              = '<domain>'
        'ActiveDirectory__ServerFqdn'          = '<dc fqdn>'
        'ActiveDirectory__BaseDn'              = '<DC=...,DC=...>'
        'ActiveDirectory__AllowedGroupSid'     = '<Bim_Envanter grubunun SID değeri>'
        'AllowedHosts'                         = '<site adı>'
    }
    .\Install-EnterpriseInventorySite.ps1 -PhysicalPath D:\EnterpriseInventory\releases\2026-10-10 `
        -HostName <site adı> -CertificateThumbprint <parmak izi> -GroupManagedServiceAccount '<DOMAIN>\<gmsa>$' `
        -Settings $settings -DirectoryServiceAccount (Get-Credential) -HttpRedirect -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $PhysicalPath,

    [Parameter(Mandatory = $true)]
    [string] $HostName,

    [Parameter(Mandatory = $true)]
    [string] $CertificateThumbprint,

    [string] $SiteName = 'EnterpriseInventory',
    [string] $AppPoolName = 'EnterpriseInventory',
    [string] $KeysDirectory = 'D:\EnterpriseInventory\DataProtection-Keys',
    [string] $LogDirectory = 'D:\Logs\EnterpriseInventory',

    # Ör. SIRKET\svc-envanter$ ; verilmezse IIS AppPool\<havuz> (ApplicationPoolIdentity)
    [string] $GroupManagedServiceAccount,

    [hashtable] $Settings = @{},

    [System.Management.Automation.PSCredential] $DirectoryServiceAccount,

    [switch] $HttpRedirect
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Yönetici olarak çalıştırın.' }
Import-Module WebAdministration

# --- Girdilerin denetimi (hiçbir şey değişmeden önce) ---
$PhysicalPath = [IO.Path]::GetFullPath($PhysicalPath)
foreach ($required in @('web.config', 'EnterpriseInventory.Api.dll', 'wwwroot\index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PhysicalPath $required))) { throw "$PhysicalPath içinde $required yok; klasör Publish-EnterpriseInventory.ps1 ile hazırlanmalı." }
}
if (Test-Path -LiteralPath (Join-Path $PhysicalPath 'appsettings.Development.json')) { throw 'Yayın klasöründe appsettings.Development.json var.' }

$certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate) { throw "Sertifika LocalMachine\My deposunda yok: $CertificateThumbprint" }
if (-not $certificate.HasPrivateKey) { throw 'Sertifikanın özel anahtarı yok.' }
if ($certificate.NotAfter -lt (Get-Date)) { throw 'Sertifikanın süresi dolmuş.' }
if (@($certificate.DnsNameList | ForEach-Object { $_.Unicode }) -notcontains $HostName) { throw "Sertifika $HostName adına verilmemiş." }
if (@($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.1' }).Count -eq 0) { throw 'Sertifikada Server Authentication kullanımı yok.' }

foreach ($name in $Settings.Keys) {
    $value = [string] $Settings[$name]
    if ($name -notmatch '^[A-Za-z0-9_]+$') { throw "Geçersiz ayar adı: $name (bölüm ayırıcı olarak __ kullanın)" }
    if ($name -match '(?i)password|secret') { throw "$name bir gizli değer; AD servis hesabı için -DirectoryServiceAccount kullanın." }
    if ($value -match '(?i)CHANGE-ME|<[^>]+>') { throw "$name hâlâ yer tutucu içeriyor." }
    if ($name -eq 'ASPNETCORE_ENVIRONMENT' -and $value -ne 'Production') { throw 'Sunucuda ortam Production olmalı.' }
    if ($name -like 'ConnectionStrings__*') {
        if ($value -match '(?i)(password|pwd)\s*=') { throw 'Bağlantı cümlesinde parola var; gMSA ile Integrated Security kullanın.' }
        if ($value -match '(?i)trustservercertificate\s*=\s*(true|yes)' -or $value -match '(?i)encrypt\s*=\s*(false|no|optional)') { throw 'Bağlantı cümlesi SQL Server sertifika doğrulamasını kapatıyor.' }
    }
}

# Güncellemede gMSA unutulursa havuz sessizce ApplicationPoolIdentity'ye dönmesin (SQL ve anahtar klasörü erişimi kopar).
if (-not $GroupManagedServiceAccount -and (Test-Path "IIS:\AppPools\$AppPoolName")) {
    $currentModel = Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel
    if ([string] $currentModel.identityType -eq 'SpecificUser') {
        throw "$AppPoolName havuzu $($currentModel.userName) kimliğiyle çalışıyor; -GroupManagedServiceAccount ile aynı hesabı verin."
    }
}

$identity = if ($GroupManagedServiceAccount) { $GroupManagedServiceAccount } else { "IIS AppPool\$AppPoolName" }

function Set-PoolVariable {
    param([string] $Name, [string] $Value, [switch] $Secret)
    $filter = "system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables"
    $shown = if ($Secret) { '(gizli)' } else { $Value }
    if ($PSCmdlet.ShouldProcess("$AppPoolName ortam değişkeni $Name", "= $shown")) {
        Remove-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -AtElement @{ name = $Name } -ErrorAction SilentlyContinue
        Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -Value @{ name = $Name; value = $Value }
    }
}

function Grant-FolderAccess {
    param([string] $Path, [string] $Rights, [switch] $Private)
    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, 'Klasör oluştur')) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    }
    if ($PSCmdlet.ShouldProcess($Path, "$identity için $Rights izni" + $(if ($Private) { ', kalıtım kapalı, yalnızca SYSTEM ve Administrators' } else { '' }))) {
        $inherit = [System.Security.AccessControl.InheritanceFlags] 'ContainerInherit, ObjectInherit'
        $none = [System.Security.AccessControl.PropagationFlags]::None
        $allow = [System.Security.AccessControl.AccessControlType]::Allow
        if ($Private) {
            $acl = New-Object System.Security.AccessControl.DirectorySecurity
            $acl.SetAccessRuleProtection($true, $false)
            foreach ($admin in @('NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators')) {
                $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($admin, 'FullControl', $inherit, $none, $allow)))
            }
        }
        else {
            $acl = Get-Acl -LiteralPath $Path
        }
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, $Rights, $inherit, $none, $allow)))
        Set-Acl -LiteralPath $Path -AclObject $acl
    }
}

# --- Uygulama havuzu ---
$poolPath = "IIS:\AppPools\$AppPoolName"
if (-not (Test-Path $poolPath)) {
    if ($PSCmdlet.ShouldProcess($AppPoolName, 'Uygulama havuzu oluştur')) { New-WebAppPool -Name $AppPoolName | Out-Null }
}
if ($PSCmdlet.ShouldProcess($AppPoolName, 'No Managed Code, Integrated, 64 bit, AlwaysRunning, boşta kapanma yok')) {
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $poolPath -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'
    # Açık oturumların AD'de yeniden kontrolü ve SignalR bağlantıları için süreç boşta kapatılmaz.
    Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
}
if ($GroupManagedServiceAccount) {
    if ($PSCmdlet.ShouldProcess($AppPoolName, "Kimlik: $GroupManagedServiceAccount (gMSA, parolasız)")) {
        Set-ItemProperty $poolPath -Name processModel -Value @{ identityType = 'SpecificUser'; userName = $GroupManagedServiceAccount; password = '' }
    }
}
elseif ($PSCmdlet.ShouldProcess($AppPoolName, 'Kimlik: ApplicationPoolIdentity')) {
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
}

# --- Klasörler ---
Grant-FolderAccess -Path $KeysDirectory -Rights 'Modify' -Private
Grant-FolderAccess -Path $LogDirectory -Rights 'Modify'
Grant-FolderAccess -Path $PhysicalPath -Rights 'ReadAndExecute'

# --- Ayarlar ---
Set-PoolVariable -Name 'DataProtection__KeysDirectory' -Value $KeysDirectory
Set-PoolVariable -Name 'Serilog__WriteTo__0__Args__path' -Value (Join-Path $LogDirectory 'log-.txt')
foreach ($name in ($Settings.Keys | Sort-Object)) { Set-PoolVariable -Name $name -Value ([string] $Settings[$name]) }
if ($DirectoryServiceAccount) {
    Set-PoolVariable -Name 'ActiveDirectory__ServiceAccountUserName' -Value $DirectoryServiceAccount.UserName
    Set-PoolVariable -Name 'ActiveDirectory__ServiceAccountPassword' -Value $DirectoryServiceAccount.GetNetworkCredential().Password -Secret
}

# --- Site ---
$sitePath = "IIS:\Sites\$SiteName"
if (-not (Test-Path $sitePath)) {
    if ($PSCmdlet.ShouldProcess($SiteName, "HTTPS site oluştur: https://$HostName ($PhysicalPath)")) {
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port 443 -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null
    }
}
elseif ($PSCmdlet.ShouldProcess($SiteName, "Site klasörü: $PhysicalPath")) {
    Set-ItemProperty $sitePath -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty $sitePath -Name applicationPool -Value $AppPoolName
}

if (-not (Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -ErrorAction SilentlyContinue)) {
    if ($PSCmdlet.ShouldProcess($SiteName, "HTTPS bağlaması *:443:$HostName (SNI)")) {
        New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1
    }
}
# Tekrar çalıştırmada aynı sertifika yeniden eklenmez (http.sys aynı bağlamayı ikinci kez kabul etmez); sertifika
# yenilendiyse eskisi kaldırılıp yenisi bağlanır.
$sslBinding = Get-Item -LiteralPath "IIS:\SslBindings\!443!$HostName" -ErrorAction SilentlyContinue
if ($null -eq $sslBinding -or $sslBinding.Thumbprint -ne $CertificateThumbprint) {
    if ($PSCmdlet.ShouldProcess("https://$HostName", "Sertifika $CertificateThumbprint")) {
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName
        if ($null -ne $sslBinding) { $binding.RemoveSslCertificate() }
        $binding.AddSslCertificate($CertificateThumbprint, 'My')
    }
}
if ($HttpRedirect -and -not (Get-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $HostName -ErrorAction SilentlyContinue)) {
    if ($PSCmdlet.ShouldProcess($SiteName, "HTTP bağlaması *:80:$HostName (uygulama https adresine yönlendirir)")) {
        New-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $HostName
    }
}

# --- Başlatma ---
if ($PSCmdlet.ShouldProcess($SiteName, 'Uygulama havuzunu yeniden başlat ve siteyi başlat')) {
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started') { Restart-WebAppPool -Name $AppPoolName } else { Start-WebAppPool -Name $AppPoolName }
    Start-Website -Name $SiteName
}

Write-Host "Sonraki adımlar: .\Test-ServerPrerequisites.ps1 -HostName $HostName ... ve .\Test-Deployment.ps1 -BaseUrl https://$HostName"
