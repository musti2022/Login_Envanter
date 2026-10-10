# Ortak yardımcılar: Update-EnterpriseInventoryDatabase.ps1 ve Restore-EnterpriseInventoryDatabase.ps1.
# sqlcmd'yi (ODBC Driver 18 ile gelen sqlcmd veya go-sqlcmd) çağırır. Bağlantı her zaman şifrelidir (-Nm: zorunlu;
# go-sqlcmd varsayılan olarak şifrelemez) ve sunucu sertifikası doğrulanır: -C (TrustServerCertificate) hiçbir zaman
# verilmez. Parola, verilirse, komut satırında değil SQLCMDPASSWORD ortam değişkeninde ve yalnızca çağrı süresince durur.

Set-StrictMode -Version 2.0

$script:Failures = 0

function Write-Result {
    param([ValidateSet('PASS', 'UYARI', 'HATA', 'BİLGİ')] [string] $Status, [string] $Message)
    $color = @{ PASS = 'Green'; UYARI = 'Yellow'; HATA = 'Red'; 'BİLGİ' = 'Gray' }[$Status]
    Write-Host ('[{0,-5}] {1}' -f $Status, $Message) -ForegroundColor $color
    if ($Status -eq 'HATA') { $script:Failures++ }
}

function Get-ResultFailures { return $script:Failures }

function Assert-SqlName {
    # sqlcmd değişkenleri betiğe metin olarak yerleşir; bu yüzden adlar ve yollar dar bir karakter kümesiyle sınırlıdır.
    param([string] $Name, [string] $Value, [ValidateSet('Database', 'Account', 'Path')] [string] $Kind)
    $pattern = @{
        Database = '^[A-Za-z0-9_]{1,100}$'
        Account  = '^[A-Za-z0-9_.\-]{1,64}(\\[A-Za-z0-9_.\-]{1,64}\$?)?$'
        Path     = '^[A-Za-z0-9_.:\\/\- ]{1,240}$'
    }[$Kind]
    if ($Value -notmatch $pattern) { throw "$Name geçersiz karakter içeriyor: $Value" }
}

function New-SqlcmdSession {
    param([string] $Server, [System.Management.Automation.PSCredential] $Credential)
    $command = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { throw 'sqlcmd bulunamadı (ODBC Driver 18 ile gelen sqlcmd veya go-sqlcmd gerekir).' }
    return @{ Path = $command.Source; Server = $Server; Credential = $Credential }
}

function Invoke-SqlcmdTool {
    # Çıkış kodu 0 değilse sqlcmd çıktısıyla birlikte hata fırlatır.
    param([hashtable] $Session, [string] $Database, [string] $Query, [string] $InputFile, [hashtable] $Variables = @{}, [switch] $Raw)
    $arguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @('-S', $Session.Server, '-Nm', '-b', '-I')) { $arguments.Add($argument) }
    if ($Database) { $arguments.Add('-d'); $arguments.Add($Database) }
    if ($Session.Credential) { $arguments.Add('-U'); $arguments.Add($Session.Credential.UserName) } else { $arguments.Add('-E') }
    foreach ($name in ($Variables.Keys | Sort-Object)) { $arguments.Add('-v'); $arguments.Add("$name=$($Variables[$name])") }
    if ($InputFile) { $arguments.Add('-i'); $arguments.Add($InputFile) }
    if ($Query) {
        if (-not $Raw) { foreach ($argument in @('-h', '-1', '-W')) { $arguments.Add($argument) } }
        $arguments.Add('-Q'); $arguments.Add($Query)
    }

    $previousPassword = $env:SQLCMDPASSWORD
    try {
        if ($Session.Credential) { $env:SQLCMDPASSWORD = $Session.Credential.GetNetworkCredential().Password }
        $output = @(& $Session.Path @arguments 2>&1 | ForEach-Object { [string] $_ })
        $code = $LASTEXITCODE
    }
    finally {
        if ($null -eq $previousPassword) { Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue } else { $env:SQLCMDPASSWORD = $previousPassword }
    }
    if ($code -ne 0) {
        $what = if ($InputFile) { [IO.Path]::GetFileName($InputFile) } else { 'sorgu' }
        throw ("sqlcmd ({0}) çıkış kodu {1}:`n{2}" -f $what, $code, ($output -join "`n"))
    }
    return , $output
}

function Get-QueryLines {
    param([hashtable] $Session, [string] $Database, [string] $Query, [hashtable] $Variables = @{})
    $lines = Invoke-SqlcmdTool -Session $Session -Database $Database -Query $Query -Variables $Variables
    return , @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}

function Test-SqlConnection {
    # Bağlantı, oturum açan hesap ve şifreleme. Döndürür: oturum açan hesabın adı.
    param([hashtable] $Session)
    $info = Get-QueryLines -Session $Session -Database 'master' -Query "SET NOCOUNT ON; SELECT CONCAT(@@SERVERNAME, ' | ', SUSER_SNAME(), ' | ', CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')))"
    $parts = $info[0] -split ' \| '
    Write-Result PASS ("SQL Server {0} (sürüm {1}), oturum: {2}" -f $parts[0], $parts[2], $parts[1])
    try {
        $encrypted = Get-QueryLines -Session $Session -Database 'master' -Query 'SET NOCOUNT ON; SELECT encrypt_option FROM sys.dm_exec_connections WHERE session_id = @@SPID'
        if ($encrypted[0] -eq 'TRUE') { Write-Result PASS 'Bağlantı şifreli' } else { Write-Result HATA 'Bağlantı şifreli değil' }
    }
    catch {
        Write-Result UYARI 'Bağlantının şifreli olduğu sunucudan doğrulanamadı (VIEW SERVER STATE yetkisi yok); sqlcmd -Nm ile şifreli bağlantı istendi'
    }
    return $parts[1]
}

function Get-ExpectedMigrations {
    param([string] $ScriptPath)
    $text = [IO.File]::ReadAllText($ScriptPath)
    $ids = [regex]::Matches($text, "\[MigrationId\] = N'([^']+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    if (@($ids).Count -eq 0) { throw "$ScriptPath içinde migration bulunamadı." }
    return , @($ids)
}

function Get-AppliedMigrations {
    param([hashtable] $Session, [string] $Database)
    return Get-QueryLines -Session $Session -Database $Database -Query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId"
}

Export-ModuleMember -Function Write-Result, Get-ResultFailures, Assert-SqlName, New-SqlcmdSession, Invoke-SqlcmdTool, Get-QueryLines, Test-SqlConnection, Get-ExpectedMigrations, Get-AppliedMigrations
