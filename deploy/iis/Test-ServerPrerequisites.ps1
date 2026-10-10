<#
.SYNOPSIS
    Yayından önce IIS sunucusunu denetler (ön kontrol). Hiçbir şeyi değiştirmez.

.DESCRIPTION
    Windows Server üzerinde, yönetici PowerShell'inde (5.1 veya 7) çalıştırılır. Her denetim PASS, UYARI veya HATA
    yazar; en az bir HATA varsa çıkış kodu 1'dir ve yayına devam edilmez.

    Denetlenenler:
      - IIS (W3SVC), WebSocket özelliği, ASP.NET Core Module V2 ve ASP.NET Core 10 çalışma zamanı (Hosting Bundle)
      - Uygulama havuzu: No Managed Code, Integrated, 64 bit, kimlik
      - Site: HTTPS bağlaması (SNI), sertifika (özel anahtar, geçerlilik, ad, Server Authentication, zincir)
      - Site klasöründeki web.config (in-process, Production, stdout kapalı), appsettings.Development.json yokluğu,
        wwwroot\index.html
      - Data Protection anahtar klasörü ve log klasörü: varlık ve izinler
      - Uygulama havuzunun ortam değişkenleri: gerekli ayarların varlığı (değerler yazılmaz)
      - SQL Server'a TCP erişimi, domain controller'a doğrulanmış LDAPS (636)

    -NetworkOnly yalnızca son iki denetimi yapar; IIS'i olmayan bir makineden (ör. yayın öncesi ağ kontrolü için)
    çalıştırılabilir.

.EXAMPLE
    .\Test-ServerPrerequisites.ps1 -HostName envanter.sirket.local -SqlServer sql01.sirket.local -DomainController dc01.sirket.local

.EXAMPLE
    .\Test-ServerPrerequisites.ps1 -NetworkOnly -SqlServer sql01.sirket.local -DomainController dc01.sirket.local
#>
[CmdletBinding()]
param(
    [string] $SiteName = 'EnterpriseInventory',
    [string] $AppPoolName = 'EnterpriseInventory',
    [string] $HostName,
    [string] $KeysDirectory = 'D:\EnterpriseInventory\DataProtection-Keys',
    [string] $LogDirectory = 'D:\Logs\EnterpriseInventory',
    [string] $SqlServer,
    [int] $SqlPort = 1433,
    [string] $DomainController,
    [int] $LdapsPort = 636,
    [switch] $NetworkOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Failures = 0
$script:Warnings = 0

function Write-Result {
    param([ValidateSet('PASS', 'UYARI', 'HATA')] [string] $Status, [string] $Message)
    $color = @{ PASS = 'Green'; UYARI = 'Yellow'; HATA = 'Red' }[$Status]
    Write-Host ('[{0,-5}] {1}' -f $Status, $Message) -ForegroundColor $color
    if ($Status -eq 'HATA') { $script:Failures++ }
    if ($Status -eq 'UYARI') { $script:Warnings++ }
}

function Get-InnermostMessage {
    param([Exception] $Exception)
    while ($null -ne $Exception.InnerException) { $Exception = $Exception.InnerException }
    return $Exception.Message
}

function Test-TcpPort {
    param([string] $Server, [int] $Port, [int] $TimeoutMilliseconds = 5000)
    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $tcp.ConnectAsync($Server, $Port)
        if (-not $connect.Wait($TimeoutMilliseconds)) { return 'zaman aşımı' }
        return $null
    }
    catch {
        return (Get-InnermostMessage $_.Exception)
    }
    finally {
        $tcp.Dispose()
    }
}

function Test-Ldaps {
    # Uygulamanın kuralları: sertifika DC'nin adına verilmiş, zincir güvenilir, süresi geçerli. Doğrulama kapatılmaz.
    param([string] $Server, [int] $Port)
    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $tcp.ConnectAsync($Server, $Port)
        if (-not $connect.Wait(5000)) {
            Write-Result HATA "${Server}:$Port bağlantısı zaman aşımına uğradı"
            return
        }
        $script:LdapsErrors = $null
        $callback = [System.Net.Security.RemoteCertificateValidationCallback] {
            param($sender, $certificate, $chain, $errors)
            $script:LdapsErrors = $errors
            return $errors -eq [System.Net.Security.SslPolicyErrors]::None
        }
        $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, $callback)
        try {
            $ssl.AuthenticateAsClient($Server)
            $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
            Write-Result PASS ("LDAPS {0}:{1} doğrulandı ({2}, son geçerlilik {3:yyyy-MM-dd})" -f $Server, $Port, $certificate.Subject, $certificate.NotAfter)
            if ($certificate.NotAfter -lt (Get-Date).AddDays(30)) { Write-Result UYARI 'DC sertifikasının süresi 30 günden az' }
        }
        catch {
            Write-Result HATA ("LDAPS {0}:{1} doğrulanamadı: {2} {3}" -f $Server, $Port, $script:LdapsErrors, (Get-InnermostMessage $_.Exception))
        }
        finally {
            $ssl.Dispose()
        }
    }
    catch {
        Write-Result HATA ("{0}:{1} açılamadı: {2}" -f $Server, $Port, (Get-InnermostMessage $_.Exception))
    }
    finally {
        $tcp.Dispose()
    }
}

function Test-FolderAccess {
    # -ReadOnly: hesabın okuması yeter (site klasörü), doğrudan veya IIS_IUSRS üzerinden; yoksa Modify gerekir.
    param([string] $Path, [string] $Identity, [string] $Purpose, [switch] $Private, [switch] $ReadOnly)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        Write-Result HATA "$Purpose klasörü yok: $Path"
        return
    }
    $acl = Get-Acl -LiteralPath $Path
    $rules = $acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' }
    if ($ReadOnly) {
        $needed = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
        $grantees = @($Identity, 'BUILTIN\IIS_IUSRS')
        $action = 'okuyabiliyor'
    }
    else {
        $needed = [System.Security.AccessControl.FileSystemRights]::Modify
        $grantees = @($Identity)
        $action = 'yazabiliyor'
    }
    $granted = $rules | Where-Object { $grantees -contains $_.IdentityReference.Value -and ($_.FileSystemRights -band $needed) -eq $needed }
    if ($granted) { Write-Result PASS "$Purpose klasöründe $Identity $action`: $Path" }
    else { Write-Result HATA "$Purpose klasöründe $Identity için gereken izin ($needed) yok: $Path" }

    if ($Private) {
        $broad = $rules | Where-Object { $_.IdentityReference.Value -match '^(Everyone|Herkes|BUILTIN\\Users|NT AUTHORITY\\Authenticated Users|BUILTIN\\IIS_IUSRS)$' }
        if ($broad) { Write-Result HATA ("{0} klasörüne geniş erişim var: {1}" -f $Purpose, (($broad | ForEach-Object { $_.IdentityReference.Value }) -join ', ')) }
        else { Write-Result PASS "$Purpose klasörüne yalnızca belirli hesaplar erişiyor" }
        if ($acl.AreAccessRulesProtected) { Write-Result PASS "$Purpose klasörü üst klasörden izin devralmıyor" }
        else { Write-Result UYARI "$Purpose klasörü üst klasörden izin devralıyor; kalıtım kapatılmalı" }
    }
}

function Test-Network {
    if ($SqlServer) {
        $problem = Test-TcpPort -Server $SqlServer -Port $SqlPort
        if ($null -eq $problem) { Write-Result PASS "SQL Server ${SqlServer}:$SqlPort erişilebilir" }
        else { Write-Result HATA "SQL Server ${SqlServer}:$SqlPort erişilemiyor: $problem" }
    }
    else {
        Write-Result UYARI 'SQL Server denetlenmedi (-SqlServer verilmedi)'
    }

    if ($DomainController) { Test-Ldaps -Server $DomainController -Port $LdapsPort }
    else { Write-Result UYARI 'Domain controller denetlenmedi (-DomainController verilmedi)' }
}

if ($NetworkOnly) {
    Test-Network
    Write-Host ("Sonuç: {0} HATA, {1} UYARI" -f $script:Failures, $script:Warnings)
    if ($script:Failures -gt 0) { exit 1 }
    exit 0
}

# --- Sunucu ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Result HATA 'Yönetici olarak çalıştırın (IIS ayarları ve sertifika okunamaz)'
    exit 1
}

$w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
if ($null -eq $w3svc) { Write-Result HATA 'IIS (W3SVC) kurulu değil'; exit 1 }
elseif ($w3svc.Status -eq 'Running') { Write-Result PASS 'IIS (W3SVC) çalışıyor' }
else { Write-Result HATA "IIS (W3SVC) durumu: $($w3svc.Status)" }

if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
    $webSockets = Get-WindowsFeature -Name Web-WebSockets
    if ($webSockets.Installed) { Write-Result PASS 'IIS WebSocket Protocol kurulu (SignalR WebSocket kullanır)' }
    else { Write-Result UYARI 'IIS WebSocket Protocol kurulu değil; SignalR daha yavaş yöntemlere düşer (Install-WindowsFeature Web-WebSockets)' }
}
else {
    Write-Result UYARI 'WebSocket özelliği denetlenemedi (Get-WindowsFeature yok; Windows Server değil mi?)'
}

if (Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2') { Write-Result PASS 'ASP.NET Core Module V2 kurulu' }
else { Write-Result HATA 'ASP.NET Core Module V2 yok: .NET 10 Hosting Bundle kurulmalı (IIS kurulduktan sonra)' }

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (Test-Path $dotnet) {
    $runtimes = & $dotnet --list-runtimes
    $aspNetCore = @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' })
    if ($aspNetCore.Count -gt 0) { Write-Result PASS ("ASP.NET Core çalışma zamanı: {0}" -f (($aspNetCore | ForEach-Object { ($_ -split ' ')[1] }) -join ', ')) }
    else { Write-Result HATA 'Microsoft.AspNetCore.App 10.x yok: .NET 10 Hosting Bundle kurulmalı' }
}
else {
    Write-Result HATA "$dotnet yok: .NET 10 Hosting Bundle kurulmalı"
}

Import-Module WebAdministration
$poolPath = "IIS:\AppPools\$AppPoolName"
$poolIdentity = "IIS AppPool\$AppPoolName"
if (Test-Path $poolPath) {
    $pool = Get-Item $poolPath
    if ($pool.managedRuntimeVersion -eq '') { Write-Result PASS "Uygulama havuzu ${AppPoolName}: No Managed Code" }
    else { Write-Result HATA "Uygulama havuzu ${AppPoolName}: .NET CLR sürümü '$($pool.managedRuntimeVersion)', No Managed Code olmalı" }
    if ($pool.managedPipelineMode -eq 'Integrated') { Write-Result PASS 'Uygulama havuzu Integrated' }
    else { Write-Result HATA "Uygulama havuzu pipeline modu: $($pool.managedPipelineMode)" }
    if (-not $pool.enable32BitAppOnWin64) { Write-Result PASS 'Uygulama havuzu 64 bit' }
    else { Write-Result HATA 'Uygulama havuzunda "Enable 32-Bit Applications" açık; 64 bit çalışma zamanıyla açılmaz' }
    $identityType = $pool.processModel.identityType
    if ($identityType -eq 'SpecificUser') {
        $poolIdentity = $pool.processModel.userName
        if ($poolIdentity -like '*$') { Write-Result PASS "Uygulama havuzu kimliği gMSA: $poolIdentity" }
        else { Write-Result UYARI "Uygulama havuzu parolalı bir hesapla çalışıyor ($poolIdentity); gMSA veya ApplicationPoolIdentity önerilir" }
    }
    elseif ($identityType -eq 'ApplicationPoolIdentity') { Write-Result PASS "Uygulama havuzu kimliği: $poolIdentity" }
    else { Write-Result HATA "Uygulama havuzu kimliği $identityType; ApplicationPoolIdentity veya gMSA olmalı" }

    $variables = @{}
    foreach ($variable in (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables" -Name '.').Collection) {
        $variables[$variable.name] = $variable.value
    }
    $required = @(
        'ConnectionStrings__DefaultConnection', 'ActiveDirectory__Domain', 'ActiveDirectory__ServerFqdn', 'ActiveDirectory__BaseDn',
        'ActiveDirectory__AllowedGroupSid', 'ActiveDirectory__ServiceAccountUserName', 'ActiveDirectory__ServiceAccountPassword', 'AllowedHosts')
    $missing = @($required | Where-Object { -not $variables.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($variables[$_]) })
    if ($missing.Count -eq 0) { Write-Result PASS 'Uygulama havuzunda gerekli ayarlar tanımlı (değerler yazılmadı)' }
    else { Write-Result HATA ("Uygulama havuzunda eksik ayarlar: {0}" -f ($missing -join ', ')) }
    if ($variables.ContainsKey('ASPNETCORE_ENVIRONMENT') -and $variables['ASPNETCORE_ENVIRONMENT'] -ne 'Production') {
        Write-Result HATA "Uygulama havuzunda ASPNETCORE_ENVIRONMENT=$($variables['ASPNETCORE_ENVIRONMENT']); Production olmalı"
    }
    if ($variables.ContainsKey('ConnectionStrings__DefaultConnection')) {
        $connection = $variables['ConnectionStrings__DefaultConnection']
        if ($connection -match '(?i)(password|pwd)\s*=') { Write-Result UYARI 'Bağlantı cümlesinde parola var; Integrated Security ile gMSA önerilir' }
        if ($connection -match '(?i)trustservercertificate\s*=\s*(true|yes)' -or $connection -match '(?i)encrypt\s*=\s*(false|no|optional)') {
            Write-Result HATA 'Bağlantı cümlesi SQL Server sertifika doğrulamasını kapatıyor'
        }
    }
}
else {
    Write-Result HATA "Uygulama havuzu yok: $AppPoolName"
}

$sitePath = "IIS:\Sites\$SiteName"
if (Test-Path $sitePath) {
    $site = Get-Item $sitePath
    $physicalPath = [Environment]::ExpandEnvironmentVariables($site.physicalPath)
    if ($site.applicationPool -eq $AppPoolName) { Write-Result PASS "Site $SiteName, $AppPoolName havuzunda" }
    else { Write-Result HATA "Site $SiteName başka bir havuzda: $($site.applicationPool)" }

    $https = @(Get-WebBinding -Name $SiteName -Protocol https)
    $binding = $https | Where-Object { -not $HostName -or $_.bindingInformation -like "*:$HostName" } | Select-Object -First 1
    if ($null -eq $binding) {
        Write-Result HATA "Sitede $HostName için HTTPS bağlaması yok"
    }
    else {
        Write-Result PASS "HTTPS bağlaması: $($binding.bindingInformation)"
        if ($binding.sslFlags -band 1) { Write-Result PASS 'SNI açık' } else { Write-Result UYARI 'SNI kapalı' }
        $thumbprint = $binding.certificateHash
        $certificate = if ($thumbprint) { Get-Item -LiteralPath "Cert:\LocalMachine\$($binding.certificateStoreName)\$thumbprint" -ErrorAction SilentlyContinue }
        if ($null -eq $certificate) {
            Write-Result HATA 'Bağlamaya sertifika atanmamış veya sertifika depoda yok'
        }
        else {
            $names = @($certificate.DnsNameList | ForEach-Object { $_.Unicode })
            if (-not $certificate.HasPrivateKey) { Write-Result HATA 'Sertifikanın özel anahtarı yok' } else { Write-Result PASS 'Sertifikanın özel anahtarı var' }
            if ($certificate.NotAfter -lt (Get-Date)) { Write-Result HATA ("Sertifikanın süresi dolmuş: {0:yyyy-MM-dd}" -f $certificate.NotAfter) }
            elseif ($certificate.NotAfter -lt (Get-Date).AddDays(30)) { Write-Result UYARI ("Sertifikanın süresi 30 günden az: {0:yyyy-MM-dd}" -f $certificate.NotAfter) }
            else { Write-Result PASS ("Sertifika {0:yyyy-MM-dd} tarihine kadar geçerli" -f $certificate.NotAfter) }
            if ($HostName -and $names -notcontains $HostName) { Write-Result HATA ("Sertifika {0} adına verilmemiş: {1}" -f $HostName, ($names -join ', ')) }
            elseif ($HostName) { Write-Result PASS "Sertifika $HostName adını içeriyor" }
            if (@($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.1' }).Count -gt 0) { Write-Result PASS 'Sertifika Server Authentication için' }
            else { Write-Result HATA 'Sertifikada Server Authentication kullanımı yok' }
            if ($HostName) {
                if (Test-Certificate -Cert $certificate -Policy SSL -DNSName $HostName -ErrorAction SilentlyContinue) { Write-Result PASS 'Sertifika zinciri bu sunucuda güvenilir' }
                else { Write-Result HATA 'Sertifika zinciri bu sunucuda doğrulanamadı (Test-Certificate)' }
            }
        }
    }
    if (@(Get-WebBinding -Name $SiteName -Protocol http).Count -gt 0) { Write-Result PASS 'HTTP bağlaması var; uygulama https adresine yönlendirir' }
    else { Write-Result UYARI 'HTTP bağlaması yok; http:// ile gelen kullanıcı yönlendirilmez, bağlantı reddedilir' }

    $webConfig = Join-Path $physicalPath 'web.config'
    if (Test-Path -LiteralPath $webConfig) {
        [xml] $xml = Get-Content -LiteralPath $webConfig -Raw
        $module = $xml.SelectSingleNode('//aspNetCore')
        $environment = $xml.SelectSingleNode("//aspNetCore/environmentVariables/environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
        if ($module -and $module.hostingModel -eq 'inprocess') { Write-Result PASS 'web.config: in-process' } else { Write-Result HATA 'web.config: hostingModel inprocess değil' }
        if ($environment -and $environment.value -eq 'Production') { Write-Result PASS 'web.config: Production' } else { Write-Result HATA 'web.config: ASPNETCORE_ENVIRONMENT Production değil' }
        if ($module -and $module.stdoutLogEnabled -eq 'false') { Write-Result PASS 'web.config: stdout logu kapalı' } else { Write-Result UYARI 'web.config: stdout logu açık; yalnızca başlangıç sorunu ararken açın' }
    }
    else {
        Write-Result HATA "web.config yok: $webConfig"
    }
    if (Test-Path -LiteralPath (Join-Path $physicalPath 'appsettings.Development.json')) { Write-Result HATA 'Site klasöründe appsettings.Development.json var; Development ayarları sunucuya konmaz' }
    else { Write-Result PASS 'appsettings.Development.json yok' }
    if (Test-Path -LiteralPath (Join-Path $physicalPath 'wwwroot\index.html')) { Write-Result PASS 'wwwroot\index.html var (React uygulaması)' }
    else { Write-Result HATA 'wwwroot\index.html yok: yayın klasörü Publish-EnterpriseInventory.ps1 ile hazırlanmalı' }
    Test-FolderAccess -Path $physicalPath -Identity $poolIdentity -Purpose 'Site' -ReadOnly
}
else {
    Write-Result HATA "Site yok: $SiteName"
}

Test-FolderAccess -Path $KeysDirectory -Identity $poolIdentity -Purpose 'Data Protection anahtar' -Private
Test-FolderAccess -Path $LogDirectory -Identity $poolIdentity -Purpose 'Log'

Test-Network

Write-Host ("Sonuç: {0} HATA, {1} UYARI" -f $script:Failures, $script:Warnings)
if ($script:Failures -gt 0) { exit 1 }
exit 0
