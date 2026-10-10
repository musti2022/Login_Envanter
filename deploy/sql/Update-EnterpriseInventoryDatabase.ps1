<#
.SYNOPSIS
    Veritabanı yayını: yedek, migration, runtime yetkileri ve yetki denetimi, sırasıyla ve ilk hatada durarak.

.DESCRIPTION
    DBA tarafından, yayın (migration) hesabıyla çalıştırılır; bu hesap veritabanında db_owner olmalıdır (yedek,
    şema değişikliği ve yetki verme). Uygulama havuzu yayından önce durdurulur. Depo, yayınlanan sürümün commit'inde
    olmalıdır (yayın klasöründeki release.json). Windows PowerShell 5.1 veya PowerShell 7; sqlcmd (ODBC Driver 18 ile
    gelen sqlcmd veya go-sqlcmd) gerekir.

      1. Bağlantı: sunucu, oturum açan hesap, şifreleme. Sunucu sertifikası doğrulanır; doğrulamayı kapatan seçenek yok.
      2. Veritabanı var ve Turkish_CI_AS (yoksa DBA önce oluşturur, bkz. docs/database.md).
      3. migrate-idempotent.sql'deki migration'lar ile __EFMigrationsHistory karşılaştırılır. Veritabanında bu sürümün
         bilmediği bir migration varsa durur (yanlış sürüm).
      4. Bekleyen migration varsa: backup-before-migration.sql (COPY_ONLY tam yedek ve RESTORE VERIFYONLY), ardından
         migrate-idempotent.sql. Hata olursa durur; yarım kalan migration'ın transaction'ı geri alınır.
      5. Bütün migration'ların uygulandığı doğrulanır.
      6. grant-runtime-permissions.sql, ardından verify-runtime-permissions.sql: runtime hesabı okur, ekler, günceller;
         silemez, şemayı değiştiremez, audit kayıtlarını değiştiremez, migration geçmişine yazamaz.

    -WhatIf ile yalnızca 1–3 çalışır ve yapılacaklar yazılır. Çıkış kodu: 0 başarılı, 1 hata.

.PARAMETER BackupDirectory
    Yedek klasörü, SQL Server makinesindeki yol (dosyayı SQL Server servis hesabı yazar).

.PARAMETER RuntimeUser
    Uygulamanın SQL hesabı, ör. SIRKET\svc-envanter$ (IIS uygulama havuzunun gMSA'sı). Login sunucuda olmalıdır.

.PARAMETER Credential
    Yalnızca Windows kimlik doğrulaması kullanılamıyorsa (ör. Linux'taki test ortamı): SQL hesabı. Parola komut
    satırına yazılmaz.

.EXAMPLE
    .\Update-EnterpriseInventoryDatabase.ps1 -SqlServer sql01.sirket.local -DatabaseName EnterpriseInventory `
        -BackupDirectory 'E:\Backups\EnterpriseInventory' -RuntimeUser 'SIRKET\svc-envanter$' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $SqlServer,

    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [Parameter(Mandatory = $true)]
    [string] $BackupDirectory,

    [Parameter(Mandatory = $true)]
    [string] $RuntimeUser,

    [System.Management.Automation.PSCredential] $Credential
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Import-Module ([IO.Path]::Combine($PSScriptRoot, 'SqlDeployment.psm1')) -Force

$root = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..'))
$migrateScript = [IO.Path]::Combine($root, 'deploy', 'sql', 'migrate-idempotent.sql')
$backupScript = [IO.Path]::Combine($root, 'deploy', 'sql', 'backup-before-migration.sql')
$grantScript = [IO.Path]::Combine($root, 'scripts', 'sql', 'grant-runtime-permissions.sql')
$verifyScript = [IO.Path]::Combine($root, 'deploy', 'sql', 'verify-runtime-permissions.sql')

try {
    Assert-SqlName -Name 'DatabaseName' -Value $DatabaseName -Kind Database
    Assert-SqlName -Name 'RuntimeUser' -Value $RuntimeUser -Kind Account
    Assert-SqlName -Name 'BackupDirectory' -Value $BackupDirectory -Kind Path

    # --- 1. Bağlantı ---
    $session = New-SqlcmdSession -Server $SqlServer -Credential $Credential
    $deployer = Test-SqlConnection -Session $session
    if ($deployer -eq $RuntimeUser) { throw 'Yayın, runtime hesabıyla yapılamaz; runtime hesabının şema değiştirme yetkisi yoktur.' }

    # --- 2. Veritabanı ---
    $collation = Get-QueryLines -Session $session -Database 'master' -Variables @{ DatabaseName = $DatabaseName } `
        -Query "SET NOCOUNT ON; SELECT collation_name FROM sys.databases WHERE name = N'`$(DatabaseName)'"
    if ($collation.Count -eq 0) { throw "$DatabaseName veritabanı yok. DBA, Turkish_CI_AS ile oluşturmalı (docs/database.md#collation)." }
    if ($collation[0] -ne 'Turkish_CI_AS') { throw "$DatabaseName veritabanının collation'ı $($collation[0]); Turkish_CI_AS olmalı." }
    Write-Result PASS "$DatabaseName var (Turkish_CI_AS)"

    # --- 3. Migration'lar ---
    $expected = Get-ExpectedMigrations -ScriptPath $migrateScript
    $applied = Get-AppliedMigrations -Session $session -Database $DatabaseName
    $unknown = @($applied | Where-Object { $expected -notcontains $_ })
    if ($unknown.Count -gt 0) { throw "Veritabanında bu sürümün bilmediği migration var: $($unknown -join ', '). Depo yayınlanan sürümün commit'inde mi?" }
    $pending = @($expected | Where-Object { $applied -notcontains $_ })
    Write-Result 'BİLGİ' ("Uygulanmış {0}, bekleyen {1} migration{2}" -f $applied.Count, $pending.Count, $(if ($pending.Count) { ': ' + ($pending -join ', ') } else { '' }))

    # --- 4. Yedek ve migration ---
    if ($pending.Count -gt 0) {
        $separator = if ($BackupDirectory -match '^([A-Za-z]:\\|\\\\)') { '\' } else { '/' }
        $backupFile = '{0}{1}{2}_{3:yyyyMMdd-HHmmss}_migration-oncesi.bak' -f $BackupDirectory.TrimEnd('\', '/'), $separator, $DatabaseName, (Get-Date)
        if ($PSCmdlet.ShouldProcess("$DatabaseName", "Yedek: $backupFile")) {
            Invoke-SqlcmdTool -Session $session -InputFile $backupScript -Variables @{ DatabaseName = $DatabaseName; BackupFile = $backupFile } | Out-Null
            Write-Result PASS "Yedek alındı ve doğrulandı: $backupFile"
        }
        if ($PSCmdlet.ShouldProcess("$DatabaseName", "migrate-idempotent.sql ($($pending.Count) migration)")) {
            Invoke-SqlcmdTool -Session $session -Database $DatabaseName -InputFile $migrateScript | Out-Null
            Write-Result PASS 'migrate-idempotent.sql çalıştı'
        }
    }

    if (-not $WhatIfPreference) {
        # --- 5. Doğrulama ---
        $applied = Get-AppliedMigrations -Session $session -Database $DatabaseName
        $missing = @($expected | Where-Object { $applied -notcontains $_ })
        if ($missing.Count -gt 0) { throw "Uygulanmamış migration kaldı: $($missing -join ', ')" }
        Write-Result PASS "Bütün migration'lar uygulanmış ($($applied.Count)); son: $($applied[-1])"
    }

    # --- 6. Runtime yetkileri ---
    if ($PSCmdlet.ShouldProcess("$DatabaseName", "Runtime yetkileri: $RuntimeUser (ei_app_runtime)")) {
        Invoke-SqlcmdTool -Session $session -Database $DatabaseName -InputFile $grantScript -Variables @{ RuntimeUser = $RuntimeUser } | Out-Null
        $report = Invoke-SqlcmdTool -Session $session -Database $DatabaseName -InputFile $verifyScript -Variables @{ RuntimeUser = $RuntimeUser }
        $checks = @($report | Where-Object { $_ -match '\bPASS\s*$' }).Count
        Write-Result PASS "Runtime yetkileri doğrulandı: $RuntimeUser ($checks denetim; silme, şema değişikliği, audit değişikliği yok)"
    }
}
catch {
    Write-Result HATA $_.Exception.Message
}

if ((Get-ResultFailures) -gt 0) {
    Write-Host 'Yayın tamamlanmadı. Yedek alındıysa geri dönüş: Restore-EnterpriseInventoryDatabase.ps1 (deploy/database-rollback.md).' -ForegroundColor Red
    exit 1
}
if ($WhatIfPreference) { Write-Host '-WhatIf: yalnızca yapılacaklar gösterildi, veritabanında hiçbir şey değişmedi.'; exit 0 }
Write-Host 'Veritabanı hazır. Sonraki adım: uygulamayı yayınlayıp /api/health/ready kontrolü (deploy/iis/README.md).' -ForegroundColor Green
exit 0
