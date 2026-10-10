<#
.SYNOPSIS
    Veritabanını Update-EnterpriseInventoryDatabase.ps1'in yayından önce aldığı yedeğe döndürür.

.DESCRIPTION
    Son çaredir: yedekten sonra yazılan her şey (demirbaş değişiklikleri, oturumlar, audit kayıtları) kaybolur.
    Önce uygulama havuzunu durdurun ve saklanması gerekenleri dışa aktarın (deploy/database-rollback.md). DBA
    tarafından, veritabanında db_owner ve sunucuda dbcreator olan bir hesapla çalıştırılır.

      1. Yedek dosyası msdb geçmişinde bu veritabanının tam yedeği olarak aranır ve bilgisi yazılır
      2. Onay istenir (-Confirm:$false ile sorulmaz); restore-from-backup.sql: önce RESTORE VERIFYONLY, sonra açık
         bağlantılar kapatılır ve veritabanı geri yüklenir
      3. Veritabanının ONLINE ve MULTI_USER olduğu, uygulanmış migration'lar ve runtime yetkileri denetlenir

    Ardından uygulamanın yedekteki son migration'a uyan önceki sürümü yayınlanır (deploy/iis/README.md#geri-dönüş).

.EXAMPLE
    .\Restore-EnterpriseInventoryDatabase.ps1 -SqlServer sql01.sirket.local -DatabaseName EnterpriseInventory `
        -BackupFile 'E:\Backups\EnterpriseInventory\EnterpriseInventory_20261010-140000_migration-oncesi.bak' `
        -RuntimeUser 'SIRKET\svc-envanter$'
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string] $SqlServer,

    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [Parameter(Mandatory = $true)]
    [string] $BackupFile,

    [Parameter(Mandatory = $true)]
    [string] $RuntimeUser,

    [System.Management.Automation.PSCredential] $Credential
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Import-Module ([IO.Path]::Combine($PSScriptRoot, 'SqlDeployment.psm1')) -Force

$restoreScript = [IO.Path]::Combine($PSScriptRoot, 'restore-from-backup.sql')
$verifyScript = [IO.Path]::Combine($PSScriptRoot, 'verify-runtime-permissions.sql')

try {
    Assert-SqlName -Name 'DatabaseName' -Value $DatabaseName -Kind Database
    Assert-SqlName -Name 'BackupFile' -Value $BackupFile -Kind Path
    Assert-SqlName -Name 'RuntimeUser' -Value $RuntimeUser -Kind Account

    $session = New-SqlcmdSession -Server $SqlServer -Credential $Credential
    Test-SqlConnection -Session $session | Out-Null

    $backup = Get-QueryLines -Session $session -Database 'msdb' -Variables @{ DatabaseName = $DatabaseName; BackupFile = $BackupFile } -Query (
        "SET NOCOUNT ON; SELECT TOP (1) CONCAT(CONVERT(nvarchar(30), b.backup_finish_date, 120), ' | ', b.name, ' | ', CAST(b.backup_size / 1048576.0 AS decimal(10, 1)), ' MB') " +
        "FROM dbo.backupset b JOIN dbo.backupmediafamily m ON m.media_set_id = b.media_set_id " +
        "WHERE m.physical_device_name = N'`$(BackupFile)' AND b.database_name = N'`$(DatabaseName)' AND b.type = 'D' ORDER BY b.backup_finish_date DESC")
    if ($backup.Count -eq 0) { throw "$BackupFile bu sunucuda $DatabaseName veritabanının tam yedeği olarak kayıtlı değil." }
    Write-Result 'BİLGİ' "Yedek: $($backup[0])"

    if ($PSCmdlet.ShouldProcess($DatabaseName, "Yedekten geri yükle (yedekten sonra yazılan her şey silinir): $BackupFile")) {
        Invoke-SqlcmdTool -Session $session -InputFile $restoreScript -Variables @{ DatabaseName = $DatabaseName; BackupFile = $BackupFile } | Out-Null
        Write-Result PASS 'Geri yükleme tamamlandı'

        $state = Get-QueryLines -Session $session -Database 'master' -Variables @{ DatabaseName = $DatabaseName } `
            -Query "SET NOCOUNT ON; SELECT CONCAT(state_desc, ' ', user_access_desc) FROM sys.databases WHERE name = N'`$(DatabaseName)'"
        if ($state.Count -eq 1 -and $state[0] -eq 'ONLINE MULTI_USER') { Write-Result PASS "$DatabaseName ONLINE, MULTI_USER" }
        else { Write-Result HATA "$DatabaseName durumu: $($state -join ' ')" }

        $applied = Get-AppliedMigrations -Session $session -Database $DatabaseName
        Write-Result 'BİLGİ' ("Uygulanmış {0} migration; son: {1}. Uygulamanın bu migration'a uyan sürümünü yayınlayın." -f $applied.Count, $(if ($applied.Count) { $applied[-1] } else { '-' }))

        Invoke-SqlcmdTool -Session $session -Database $DatabaseName -InputFile $verifyScript -Variables @{ RuntimeUser = $RuntimeUser } | Out-Null
        Write-Result PASS "Runtime yetkileri yedekteki gibi doğru: $RuntimeUser"
    }
}
catch {
    Write-Result HATA $_.Exception.Message
}

if ((Get-ResultFailures) -gt 0) { exit 1 }
exit 0
