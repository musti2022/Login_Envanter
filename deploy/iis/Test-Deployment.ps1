<#
.SYNOPSIS
    Yayından sonra sitenin HTTPS üzerinden doğru açıldığını dener (duman testi).

.DESCRIPTION
    Yalnızca okur; veri değiştirmez. Windows PowerShell 5.1 ve PowerShell 7 ile çalışır, sunucuda veya bir istemci
    makinesinden çalıştırılabilir. Sertifika her zaman doğrulanır: adres sertifikadaki ada uymuyorsa veya zincir
    güvenilmiyorsa test HATA verir. Doğrulamayı kapatan bir seçenek yoktur.

    Denenenler:
      - /api/health/live ve /api/health/ready 200
      - HSTS ve güvenlik başlıkları, sunucu adının gizlenmesi
      - Ana sayfa (React uygulaması) ve sayfa adresleri, sayfanın Content-Security-Policy'si
      - Oturumsuz /api/assets isteğinin 401 alması
      - http:// adresinin https:// adresine yönlendirilmesi (HTTP bağlaması yoksa UYARI)
      - -Credential verilirse: AD ile giriş, /api/auth/me, bir demirbaş sayfası, çıkış ve çıkıştan sonra 401

    Parola yalnızca giriş isteğinin gövdesinde gönderilir; ekrana veya bir dosyaya yazılmaz.

.PARAMETER BaseUrl
    Sitenin https adresi, ör. https://envanter.sirket.local

.PARAMETER Credential
    İsteğe bağlı. Bim_Envanter üyesi bir hesap (Get-Credential). Verilmezse giriş denenmez.

.PARAMETER SkipHttpRedirect
    http:// yönlendirmesini denemez (sitede bilerek HTTP bağlaması yoksa).

.EXAMPLE
    .\Test-Deployment.ps1 -BaseUrl https://envanter.sirket.local -Credential (Get-Credential)

.NOTES
    Çıkış kodu: 0 hata yok, 1 en az bir HATA.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [System.Management.Automation.PSCredential] $Credential,

    [switch] $SkipHttpRedirect,

    [int] $TimeoutSeconds = 20
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Core') {
    Add-Type -AssemblyName System.Net.Http
    # Windows PowerShell 5.1 eski sunucularda TLS 1.2'yi kendiliğinden açmayabilir. Yalnızca sürüm eklenir;
    # sertifika doğrulaması değişmez.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$script:Failures = 0
$script:Warnings = 0

function Write-Result {
    param([ValidateSet('PASS', 'UYARI', 'HATA')] [string] $Status, [string] $Message)
    $color = @{ PASS = 'Green'; UYARI = 'Yellow'; HATA = 'Red' }[$Status]
    Write-Host ('[{0,-5}] {1}' -f $Status, $Message) -ForegroundColor $color
    if ($Status -eq 'HATA') { $script:Failures++ }
    if ($Status -eq 'UYARI') { $script:Warnings++ }
}

function Get-HeaderValue {
    param([System.Net.Http.HttpResponseMessage] $Response, [string] $Name)
    $values = $null
    if ($Response.Headers.TryGetValues($Name, [ref] $values)) { return ($values -join ', ') }
    if ($null -ne $Response.Content -and $Response.Content.Headers.TryGetValues($Name, [ref] $values)) { return ($values -join ', ') }
    return $null
}

function New-Client {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $true
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    return $client
}

function Send-Request {
    param(
        [System.Net.Http.HttpClient] $Client,
        [string] $Method = 'GET',
        [string] $Url,
        [hashtable] $Headers = @{},
        [string] $JsonBody
    )
    $request = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($Method)), $Url)
    foreach ($key in $Headers.Keys) { [void] $request.Headers.TryAddWithoutValidation($key, [string] $Headers[$key]) }
    if ($PSBoundParameters.ContainsKey('JsonBody')) {
        $request.Content = New-Object System.Net.Http.StringContent($JsonBody, [Text.Encoding]::UTF8, 'application/json')
    }
    try {
        return $Client.SendAsync($request).GetAwaiter().GetResult()
    }
    finally {
        $request.Dispose()
    }
}

function Read-Body {
    param([System.Net.Http.HttpResponseMessage] $Response)
    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

function Get-InnermostMessage {
    param([Exception] $Exception)
    while ($null -ne $Exception.InnerException) { $Exception = $Exception.InnerException }
    return $Exception.Message
}

# Sondaki / atılır: adresler "$base/api/..." diye kurulur ve // ile başlayan bir yol API'ye değil sayfaya gider.
$base = $BaseUrl.TrimEnd('/')
$baseUri = [Uri] $base
if ($baseUri.Scheme -ne 'https' -or $baseUri.AbsolutePath -ne '/') {
    Write-Result HATA "Adres https ile başlamalı ve yol içermemeli (ör. https://envanter.sirket.local): $BaseUrl"
    exit 1
}

Write-Host "Site: $base"
$client = New-Client
try {
    # 1. Canlılık ve hazır olma. İlk istek sertifikayı da doğrular.
    try {
        $live = Send-Request -Client $client -Url "$base/api/health/live"
    }
    catch {
        Write-Result HATA ("HTTPS bağlantısı kurulamadı (sertifika veya ağ): {0}" -f (Get-InnermostMessage $_.Exception))
        exit 1
    }
    Write-Result PASS 'HTTPS bağlantısı kuruldu, sertifika doğrulandı'
    if ([int] $live.StatusCode -eq 200) { Write-Result PASS '/api/health/live 200' }
    else { Write-Result HATA ("/api/health/live {0}" -f [int] $live.StatusCode) }

    $ready = Send-Request -Client $client -Url "$base/api/health/ready"
    if ([int] $ready.StatusCode -eq 200) { Write-Result PASS '/api/health/ready 200 (veritabanı erişilebilir, migration güncel)' }
    else { Write-Result HATA ("/api/health/ready {0}: veritabanı erişilemiyor veya bekleyen migration var; sunucu loguna bakın" -f [int] $ready.StatusCode) }

    # 2. Güvenlik başlıkları.
    $hsts = Get-HeaderValue $live 'Strict-Transport-Security'
    if ($hsts -and $hsts -match 'max-age=\d+') { Write-Result PASS "HSTS: $hsts" }
    else { Write-Result HATA 'Strict-Transport-Security başlığı yok (ASPNETCORE_ENVIRONMENT Production mı?)' }

    $expected = [ordered] @{ 'X-Content-Type-Options' = 'nosniff'; 'X-Frame-Options' = 'DENY'; 'Referrer-Policy' = 'no-referrer' }
    foreach ($name in $expected.Keys) {
        $value = Get-HeaderValue $live $name
        if ($value -eq $expected[$name]) { Write-Result PASS "$name`: $value" }
        else { Write-Result HATA ("{0} beklenen '{1}', gelen '{2}'" -f $name, $expected[$name], $value) }
    }

    $server = Get-HeaderValue $live 'Server'
    if ($server) { Write-Result UYARI "Server başlığı sunucuyu tanıtıyor: $server" }
    else { Write-Result PASS 'Server başlığı yok' }

    # 3. React uygulaması.
    foreach ($path in @('/', '/envanter')) {
        $page = Send-Request -Client $client -Url "$base$path"
        $body = Read-Body $page
        $csp = Get-HeaderValue $page 'Content-Security-Policy'
        if ([int] $page.StatusCode -eq 200 -and $body -match 'id="root"') { Write-Result PASS "$path uygulama sayfasını döndürdü" }
        else { Write-Result HATA ("{0} {1}: uygulama sayfası gelmedi (wwwroot\index.html var mı?)" -f $path, [int] $page.StatusCode) }
        if ($csp -and $csp -match "script-src 'self'" -and $csp -match "frame-ancestors 'none'") { Write-Result PASS "$path Content-Security-Policy uygulamanın politikası" }
        else { Write-Result HATA "$path Content-Security-Policy beklenen değil: $csp" }
    }

    # 4. Oturumsuz API.
    $assets = Send-Request -Client $client -Url "$base/api/assets"
    if ([int] $assets.StatusCode -eq 401) { Write-Result PASS 'Oturumsuz /api/assets 401' }
    else { Write-Result HATA ("Oturumsuz /api/assets {0} (401 bekleniyordu)" -f [int] $assets.StatusCode) }

    # 5. HTTP'den HTTPS'e yönlendirme.
    if (-not $SkipHttpRedirect) {
        $httpUrl = "http://$($baseUri.Host)/envanter"
        try {
            $plain = Send-Request -Client $client -Url $httpUrl
            $location = Get-HeaderValue $plain 'Location'
            if (@(301, 302, 307, 308) -contains [int] $plain.StatusCode -and $location -like 'https://*') { Write-Result PASS "http:// adresi https:// adresine yönlendiriliyor ($([int] $plain.StatusCode))" }
            else { Write-Result HATA ("{0} {1}: https yönlendirmesi yok" -f $httpUrl, [int] $plain.StatusCode) }
        }
        catch {
            Write-Result UYARI ("{0} açılamadı ({1}); HTTP bağlaması yoksa beklenen durumdur" -f $httpUrl, (Get-InnermostMessage $_.Exception))
        }
    }

    # 6. AD ile giriş ve çıkış.
    if ($Credential) {
        $csrf = Send-Request -Client $client -Url "$base/api/auth/csrf"
        $token = (Read-Body $csrf | ConvertFrom-Json).token
        $login = @{ userName = $Credential.UserName; password = $Credential.GetNetworkCredential().Password } | ConvertTo-Json -Compress
        $signIn = Send-Request -Client $client -Method POST -Url "$base/api/auth/login" -Headers @{ 'X-CSRF-TOKEN' = $token } -JsonBody $login
        $login = $null
        if ([int] $signIn.StatusCode -eq 200) {
            Write-Result PASS ("{0} ile giriş yapıldı" -f $Credential.UserName)
            $userToken = (Read-Body $signIn | ConvertFrom-Json).csrfToken

            $me = Send-Request -Client $client -Url "$base/api/auth/me"
            if ([int] $me.StatusCode -eq 200) { Write-Result PASS ("/api/auth/me: {0}" -f (Read-Body $me | ConvertFrom-Json).displayName) }
            else { Write-Result HATA ("/api/auth/me {0}" -f [int] $me.StatusCode) }

            $list = Send-Request -Client $client -Url "$base/api/assets?page=1&pageSize=1"
            if ([int] $list.StatusCode -eq 200) { Write-Result PASS ("Demirbaş listesi okundu ({0} kayıt)" -f (Read-Body $list | ConvertFrom-Json).totalCount) }
            else { Write-Result HATA ("/api/assets {0}" -f [int] $list.StatusCode) }

            $logout = Send-Request -Client $client -Method POST -Url "$base/api/auth/logout" -Headers @{ 'X-CSRF-TOKEN' = $userToken }
            $after = Send-Request -Client $client -Url "$base/api/auth/me"
            if ([int] $logout.StatusCode -eq 204 -and [int] $after.StatusCode -eq 401) { Write-Result PASS 'Çıkış yapıldı, oturum sunucuda bitti' }
            else { Write-Result HATA ("Çıkış {0}, çıkıştan sonra /api/auth/me {1}" -f [int] $logout.StatusCode, [int] $after.StatusCode) }
        }
        else {
            $problem = Read-Body $signIn
            Write-Result HATA ("Giriş {0}: {1}" -f [int] $signIn.StatusCode, $problem)
        }
    }
    else {
        Write-Result UYARI 'Giriş denenmedi (-Credential verilmedi)'
    }
}
finally {
    $client.Dispose()
}

Write-Host ("Sonuç: {0} HATA, {1} UYARI" -f $script:Failures, $script:Warnings)
if ($script:Failures -gt 0) { exit 1 }
exit 0
