# IIS yayını

Uygulama mevcut Windows Server'daki IIS'te tek bir site olarak çalışır. API ve React derlemesi aynı klasörde ve aynı
adreste sunulur (`/` React, `/api` API, `/hubs` SignalR); CORS gerekmez. IIS, uygulamayı ASP.NET Core Module V2 ile
kendi sürecinde (in-process) çalıştırır.

| Dosya | Ne işe yarar | Nerede çalışır |
| --- | --- | --- |
| [`Publish-EnterpriseInventory.ps1`](Publish-EnterpriseInventory.ps1) | React ve API'yi derler, tek yayın klasörü hazırlar, klasörü denetler, `release.json` yazar | Derleme makinesi (.NET 10 SDK, Node.js 22) |
| [`Test-ServerPrerequisites.ps1`](Test-ServerPrerequisites.ps1) | Sunucuyu denetler (ön kontrol); hiçbir şeyi değiştirmez | Sunucu, yönetici PowerShell |
| [`Install-EnterpriseInventorySite.ps1`](Install-EnterpriseInventorySite.ps1) | Uygulama havuzunu, HTTPS siteyi, klasör izinlerini ve ayarları kurar veya günceller; `-WhatIf` destekler | Sunucu, yönetici PowerShell |
| [`Test-Deployment.ps1`](Test-Deployment.ps1) | Yayından sonra siteyi HTTPS üzerinden dener (duman testi); veri değiştirmez | Sunucu veya bir istemci |
| [`../../src/EnterpriseInventory.Api/web.config`](../../src/EnterpriseInventory.Api/web.config) | ASP.NET Core Module ayarı: in-process, `Production`, stdout logu kapalı, `Server`/`X-Powered-By` başlıkları yok, istek gövdesi en fazla 1 MB | Yayın klasörüyle gelir |
| [`../database-rollback.md`](../database-rollback.md) | Veritabanı yedeği, migration ve geri dönüş | DBA |

Betikler Windows PowerShell 5.1 ve PowerShell 7 için yazıldı; çıktıları Türkçedir. Ön kontrol ve duman testi her
denetim için `PASS`, `UYARI` veya `HATA` yazar; en az bir `HATA` varsa çıkış kodu 1'dir ve yayına devam edilmez.
Hiçbir betik TLS veya sertifika doğrulamasını kapatma seçeneği sunmaz.

## Sunucu gereksinimleri

1. **IIS özellikleri:** Web Server, **WebSocket Protocol** (yoksa canlı bağlantı daha yavaş taşımalara düşer) ve
   PowerShell yönetim modülü:

   ```powershell
   Install-WindowsFeature Web-Server, Web-WebSockets, Web-Scripting-Tools
   ```

2. **.NET 10 Hosting Bundle**, IIS'ten **sonra** kurulur (IIS sonradan kurulursa Hosting Bundle onarılır). Kurulumdan
   sonra `net stop was /y` ve `net start w3svc`. Uygulama framework-dependent yayınlanır; sunucuda SDK gerekmez.
3. **Sertifika:** `LocalMachine\My` deposunda, özel anahtarıyla, site adını (SAN) içeren, *Server Authentication*
   kullanımlı ve şirket CA'sı tarafından verilmiş. Kullanıcıların tarayıcıları bu CA'ya güvenmelidir.
4. **Uygulama havuzu kimliği:** önerilen, bir **gMSA** (`SIRKET\svc-envanter$` gibi; parolası yoktur). SQL Server'a bu
   hesapla Windows kimlik doğrulaması yapılır; bağlantı cümlesinde parola olmaz.
5. **SQL Server:** veritabanında gMSA için bir Windows login ve kullanıcı;
   [`scripts/sql/grant-runtime-permissions.sql`](../../scripts/sql/grant-runtime-permissions.sql) ile yalnızca runtime
   yetkileri (şema değiştiremez, silemez, audit kayıtlarını değiştiremez). Migration'ları ayrı bir hesap (DBA)
   uygular; bkz. [`docs/database.md`](../../docs/database.md#hesaplar-migration-ve-runtime-ayrı).
6. **AD servis hesabı:** LDAPS ile bağlanıp kullanıcı ve grup okuyan, yetkisiz bir domain hesabı. Parolası kurulumda
   `Get-Credential` ile alınır.
7. **Ağ:** sunucudan SQL Server'a (varsayılan 1433) ve domain controller'a LDAPS (636); CA'nın CRL/OCSP adreslerine
   erişim (sertifika iptal kontrolü açıktır, erişilemezse AD girişi reddedilir).
8. **Klasörler** (varsayılanlar, betik parametreleriyle değiştirilebilir):

   | Klasör | İçerik | İzin |
   | --- | --- | --- |
   | `D:\EnterpriseInventory\releases\<tarih>` | Her yayın kendi klasöründe; önceki klasörler geri dönüş için saklanır | Havuz kimliği okur |
   | `D:\EnterpriseInventory\DataProtection-Keys` | Oturum çerezlerini koruyan anahtarlar | Kalıtım kapalı; yalnızca SYSTEM, Administrators ve havuz kimliği |
   | `D:\Logs\EnterpriseInventory` | Günlük log dosyaları (`log-YYYYMMDD.txt`, 30 gün) | Havuz kimliği yazar |

## Ayarlar

Sunucuya özel değerler site klasörüne (her yayında değişir) değil, **uygulama havuzunun ortam değişkenlerine**
(`applicationHost.config`, yalnızca yöneticiler ve SYSTEM okur) yazılır. `Install-EnterpriseInventorySite.ps1` bunu
yapar; parola veya `TrustServerCertificate` içeren bağlantı cümlesini, yer tutucu (`CHANGE-ME`, `<...>`) kalmış
değeri ve `-Settings` içinde gizli değeri kabul etmez.

| Ortam değişkeni | Değer | Kaynak |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `web.config` |
| `ConnectionStrings__DefaultConnection` | `Server=<sql sunucusu>;Database=<veritabanı>;Integrated Security=true;Encrypt=true` | `-Settings` |
| `ActiveDirectory__Domain`, `__ServerFqdn`, `__BaseDn`, `__AllowedGroupSid` | Şirketin AD bilgileri; `Bim_Envanter` grubunun SID'i | `-Settings` |
| `ActiveDirectory__EmployeeSearchBaseDn`, `__TrustedCaCertificatePath` | İsteğe bağlı, bkz. [`docs/active-directory.md`](../../docs/active-directory.md) | `-Settings` |
| `ActiveDirectory__ServiceAccountUserName`, `__ServiceAccountPassword` | AD servis hesabı | `-DirectoryServiceAccount` |
| `AllowedHosts` | Site adı | `-Settings` |
| `DataProtection__KeysDirectory` | Anahtar klasörü | `-KeysDirectory` |
| `Serilog__WriteTo__0__Args__path` | `<log klasörü>\log-.txt` | `-LogDirectory` |

Diğer ayarların (oturum süreleri, rate limit, rapor saat dilimi) varsayılanları `appsettings.json`'dadır. Uygulama
başlarken AD ayarlarını denetler; eksik veya hatalı değerle başlamaz.

## İlk kurulum

1. **Ağ ön kontrolü** (sunucuda, kurulumdan önce):

   ```powershell
   .\Test-ServerPrerequisites.ps1 -NetworkOnly -SqlServer <sql sunucusu> -DomainController <dc fqdn>
   ```

2. **Yayın klasörü** (derleme makinesinde, temiz bir checkout'tan):

   ```powershell
   pwsh ./deploy/iis/Publish-EnterpriseInventory.ps1 -OutputPath C:\Releases\EnterpriseInventory\2026-10-10
   ```

   Klasörü sunucuda `D:\EnterpriseInventory\releases\2026-10-10` altına kopyalayın. Kopyalanan dosyalar
   `release.json`'daki özetlerle karşılaştırılabilir; yayın klasöründe çalıştırılan aşağıdaki komut farklı olanları
   listeler, boş çıktı beklenir:

   ```powershell
   $release = Get-Content .\release.json -Raw | ConvertFrom-Json
   $release.files | Where-Object { (Get-FileHash -LiteralPath (Join-Path $PWD $_.path) -Algorithm SHA256).Hash -ne $_.sha256 }
   ```

3. **Veritabanı:** [`../database-rollback.md`](../database-rollback.md) "Yayın adımları" (yedek, migration hesabıyla
   `migrate-idempotent.sql`), ardından `grant-runtime-permissions.sql` ile gMSA'ya runtime yetkileri.
4. **Site kurulumu:** önce `-WhatIf` ile ne yapılacağını görün, sonra `-WhatIf` olmadan çalıştırın:

   ```powershell
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
   ```

5. **Ön kontrol** (tam): `.\Test-ServerPrerequisites.ps1 -HostName <site adı> -SqlServer <sql sunucusu> -DomainController <dc fqdn>`
   0 HATA vermelidir.
6. **Duman testi:** `.\Test-Deployment.ps1 -BaseUrl https://<site adı> -Credential (Get-Credential)` (`Bim_Envanter`
   üyesi bir hesapla) 0 HATA vermelidir. Ardından bir tarayıcıda giriş yapıp üst çubukta "Canlı" göstergesini görün.

## Güncelleme

1. Yeni yayın klasörünü hazırlayıp sunucuya kopyalayın (yukarıdaki 2. adım); önceki klasörü silmeyin.
2. Uygulama havuzunu durdurun ve veritabanını yeni sürüme getirin:
   [`../sql/Update-EnterpriseInventoryDatabase.ps1`](../sql/Update-EnterpriseInventoryDatabase.ps1) bekleyen migration
   varsa yedek alır, uygular ve runtime yetkilerini denetler ([`../database-rollback.md`](../database-rollback.md#yayın-adımları)).
3. `Install-EnterpriseInventorySite.ps1`'i yeni `-PhysicalPath` ile ve **aynı** `-HostName`, `-CertificateThumbprint`,
   `-GroupManagedServiceAccount` ile çalıştırın. `-Settings` ve `-DirectoryServiceAccount` verilmezse kayıtlı ayarlar
   değişmez. Havuz bir gMSA ile çalışırken `-GroupManagedServiceAccount` unutulursa betik durur.
4. Ön kontrol ve duman testi.

## Geri dönüş

- **Yalnızca uygulama:** `Install-EnterpriseInventorySite.ps1 -PhysicalPath <önceki klasör> ...` (aynı parametrelerle)
  siteyi önceki klasöre döndürür ve havuzu yeniden başlatır. Veritabanı değişmediyse yeterlidir.
- **Migration uygulandıysa:** [`../database-rollback.md`](../database-rollback.md#geri-dönüş-seçenekleri)'deki tabloya
  göre önce veritabanı ([`../sql/Restore-EnterpriseInventoryDatabase.ps1`](../sql/Restore-EnterpriseInventoryDatabase.ps1)
  ile yayından önceki yedeğe), sonra uygulama geri alınır. Eski uygulama yeni şemayla çalıştırılmaz.

## Data Protection anahtarları

Oturum çerezleri ve CSRF token'ları bu anahtarlarla korunur. Anahtarlar site klasörünün dışında tutulur, böylece
yayınlar ve havuzun yeniden başlaması oturumları düşürmez. Windows'ta anahtar dosyaları ayrıca makine düzeyinde DPAPI
ile şifrelenir; bu yüzden yalnızca bu sunucuda çözülebilir. Sunucu değişirse anahtarlar taşınamaz, kullanıcılar
yeniden giriş yapar.

- **Klasörü kurulum betiği oluşturur** ve yalnızca uygulama havuzu kimliğine ve yöneticilere açar. Development dışında
  uygulama klasörü kendisi oluşturmaz: klasör yoksa (ör. yol yanlış yazıldı) başlamaz ve nedeni loga yazar. Aksi halde
  üst klasörün izinleriyle yeni bir klasör ve yeni anahtarlar oluşur, herkesin oturumu sessizce düşerdi
  (`ApiPipelineTests.A_keys_folder_that_does_not_exist_stops_the_application_instead_of_being_created`).
- **Yedek:** klasör sunucu yedeğine dahil edilir. Kaybolursa yalnızca açık oturumlar düşer (kullanıcılar yeniden giriş
  yapar); veri kaybolmaz. Aynı sunucuya geri kopyalanan klasör oturumları geri getirir. DPAPI nedeniyle yedek başka
  sunucuda işe yaramaz.
- **Geri yükleme:** havuzu durdurun, klasörün içeriğini yedekten kopyalayın (yeni oluşan anahtarlar da kalabilir),
  izinleri denetleyin ve havuzu başlatın.

39. günde denendi (yayın klasörü, Production, Linux; DPAPI yok): giriş yapılmış bir oturum uygulama yeniden başlayınca
sürdü; anahtar klasörü boşaltılınca aynı çerez `401` aldı; klasörün kopyası geri konunca aynı çerez yeniden `200`
aldı; klasör silinince uygulama "does not exist" hatasıyla başlamadı ve klasörü oluşturmadı. Aynısı otomatik testtedir
(`SessionSecurityTests.A_copy_of_the_key_folder_brings_the_sessions_back_and_new_keys_do_not`).

## Loglar

Loglar `D:\Logs\EnterpriseInventory` altında günlük dosyalardadır; her satırda correlation ID vardır. Parola, token ve
çerez loglanmaz. `web.config`'te stdout logu kapalıdır. Uygulama log yazamadan durursa (ör. eksik ayar): Olay
Görüntüleyicisi > Windows Günlükleri > Uygulama, kaynak `IIS AspNetCore Module V2`.

## 38. günde denenenler

Geliştirme ortamında Windows ve IIS yoktur (Linux container). Bu yüzden IIS'in kendisi denenmedi; onun dışındaki her
parça, yayın klasörünün kendisiyle ve Production ortamında denendi:

- `Publish-EnterpriseInventory.ps1` PowerShell 7.6 ile çalıştı: 85 dosya, denetimler geçti, `release.json` yazıldı.
- Yayın klasörü `ASPNETCORE_ENVIRONMENT=Production` ile Kestrel'de `https://envanter.test.local` adresinde çalıştırıldı
  (test CA'sının verdiği sertifika, HTTP/2). Veritabanı: SQL Server 2022 test container'ı, yalnızca
  `ei_app_runtime` rolündeki ayrı bir SQL hesabı, `Encrypt=true` ve sertifika doğrulaması açık (TrustServerCertificate
  yok). AD: Samba test domain'i, LDAPS. Bu bir test ortamıdır; şirketin sunucuları değildir.

| Denenen | Sonuç |
| --- | --- |
| `/api/health/ready` | `Healthy` (runtime hesabıyla SQL bağlantısı, bekleyen migration yok); LDAPS bağlantısı giriş ve çalışan aramasıyla denendi |
| `Test-Deployment.ps1 -Credential` (`Bim_Envanter` üyesi test kullanıcısı) | 18 PASS, 0 UYARI, 0 HATA: sağlık uçları, HSTS ve güvenlik başlıkları, `Server` başlığı yok, ana sayfa ve sayfa adresi CSP'siyle, oturumsuz API `401`, `http://` → `https://` (`307`), AD ile giriş, `/api/auth/me`, demirbaş sayfası, çıkış ve çıkıştan sonra `401` |
| Duman testi, olumsuz durumlar | Sertifikadaki ada uymayan adres (`RemoteCertificateNameMismatch`), güvenilmeyen CA (`PartialChain`) ve `Bim_Envanter` üyesi olmayan kullanıcı (`403 not_authorized`): her biri HATA ve çıkış kodu 1 |
| `Test-ServerPrerequisites.ps1 -NetworkOnly` | SQL Server 1433 TCP ve domain controller LDAPS (sertifika doğrulandı) PASS; olumsuz: kapalı port ve IP adresiyle (sertifika adı uymaz) bağlantı HATA, çıkış kodu 1 |
| Chromium ile tarayıcıda | `/` → giriş sayfası, AD ile giriş, demirbaş listesi, Gösterge Paneli, Raporlar, Denetim Geçmişi, Marka ve Modeller sayfaları; canlı bağlantı WebSocket (`wss`) ile kuruldu, gösterge "Canlı"; hiç CSP ihlali yok; konsoldaki tek hata ilk açılıştaki beklenen `401` (`/api/auth/me`, oturum yokken); `localStorage` ve `sessionStorage` boş; çerezler `__Host-`, Secure, HttpOnly, SameSite=Strict |
| Yazma işlemleri (runtime SQL hesabıyla) | Tanım ekleme `201`, demirbaş ekleme `201`, güncelleme `200`, eski RowVersion ile güncelleme `409` ve Türkçe çakışma mesajı; AD'de çalışan araması; zimmet verme `201`, iade `200`; demirbaşın denetim geçmişinde dört kayıt (ekleme, güncelleme, zimmet, iade) |
| Data Protection | Anahtar ayarlanan klasöre yazıldı. Linux'ta DPAPI olmadığı için anahtar şifrelenmeden yazıldı (uygulama bunu uyarı olarak logladı); Windows'taki DPAPI şifrelemesi denenmedi |

Bu denemede bulunup düzeltilen sorunlar (her biri için test eklendi):

1. HTTP/2 üzerinde tarayıcı WebSocket'i `CONNECT` ile açar; CSRF kontrolü bunu veri değiştiren istek sayıp reddediyordu
   ve canlı bağlantı yavaş taşımaya düşüyordu (`Http2WebSocketTests`).
2. Zod, şema oluştururken `new Function` deniyordu; sayfa CSP'si bunu ihlal olarak raporluyordu. Zod `jitless`
   ayarıyla çalışıyor (`zodConfig.test.ts`).
3. Sayfayı ilk sürümde bir catch-all route veriyordu. Yönlendirme, yöntem ve içerik türü kontrollerini route
   kısıtından önce uyguladığı için bu route var olmayan bir `/api` adresine `POST`'u `405`, API'nin kendi `405` ve
   `415` yanıtlarını da `404` yapıyordu (ikincisini tüm entegrasyon testleri buldu). Sayfa artık hiçbir endpoint'e
   uymayan okuma isteklerine bir ara katmanla verilir; API'nin yanıtları değişmez ve `/api`, `/hubs` hiçbir yöntemle
   sayfaya düşmez (`WebAppHostingTests`).
4. Duman testi, adresin sonundaki `/` yüzünden `//api/...` istiyordu; betik düzeltildi, uygulama da `//api` adresini
   API sayar.

Düzeltmelerden sonra yayın klasörü betikle yeniden hazırlandı (85 dosya); duman testi (18 PASS, 0 HATA) ve Chromium
denemesi (sayfa adreslerine doğrudan açılış, `wss` bağlantısı, CSP ihlali yok) yeni klasörle tekrarlandı.

## Denenmeyenler

- **IIS:** ASP.NET Core Module V2, in-process çalışma, `web.config`'in IIS'teki etkisi, uygulama havuzu, HTTPS
  bağlaması ve SNI, IIS'te HTTP'den yönlendirme, WebSocket Protocol özelliği, .NET Hosting Bundle.
- `Install-EnterpriseInventorySite.ps1` hiç çalıştırılmadı; `Test-ServerPrerequisites.ps1`'in IIS, sertifika deposu
  ve klasör izni denetimleri çalıştırılmadı. İkisi de yalnızca PowerShell 7.6 ile sözdizimi açısından denetlendi.
- Windows PowerShell 5.1 (betikler 5.1 uyumlu yazıldı ama 5.1 ile çalıştırılmadı).
- DPAPI ile anahtar şifreleme ve DPAPI ile şifrelenmiş anahtarların yedekten geri yüklenmesi, gMSA, SQL Server'a
  Windows kimlik doğrulaması, Windows'ta ODBC sqlcmd 18 ile veritabanı betikleri.
- Şirketin AD'si, SQL Server'ı, sertifikası ve CA'sı. Testte AD sertifika iptal kontrolü kapalıydı (Samba test CA'sının
  CRL'i yok); üretimde açıktır ve açık kalmalıdır.
