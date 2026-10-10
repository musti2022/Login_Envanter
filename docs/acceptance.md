# Kabul ve teslim (40. gün)

Bu belge 40 günlük planın kabulüdür: kritik akışların yayın paketiyle denenmesi, her gereksinimin nerede ve hangi
testle karşılandığı, ve şirket ortamında yapılmadan "tamam" denmeyecek işler. Denenen kod `e82f0c7` commit'idir.
Tarih: 10 Ekim 2026.

## Özet

- **Bütün test takımları geçti:** birim 471, SQL Server entegrasyon 686 (1 ölçüm testi isteğe bağlı olduğu için
  atlandı), web birim 194, tarayıcı (E2E) 40; başarısız test yok. Ayrıntı: [`test-report.md`](test-report.md).
- **Yayın paketiyle kabul denemesi geçti:** veritabanı betiği 0 hata, duman testi 18 denetimde 0 hata, iki tarayıcıda
  iki yöneticiyle yapılan 19 adımlı kabul yürüyüşünün 19'u geçti ([aşağıda](#kabul-denemesi)).
- **Kabul sırasında bulunan eksik giderildi:** Zimmetler, Lokasyonlar ve Marka ve Modeller menüleri hâlâ "yakında"
  yazan boş sayfalardı. 40. günde tanımların adını değiştirme ve pasifleştirme (RowVersion, `409`, audit) ile bu üç
  ekran yapıldı ve test edildi ([`lookups-api.md`](lookups-api.md#değiştirme-kuralları-40-gün),
  [`inventory-ui.md`](inventory-ui.md#tanımlar-40-gün)).
- **Bu kabul test ortamındadır.** Active Directory olarak Samba test domain'i, SQL Server olarak SQL Server 2022
  container'ı, IIS yerine Kestrel kullanıldı. Şirketin gerçek AD'si, SQL Server'ı, IIS sunucusu, sertifikası ve
  Microsoft Excel ile **denenmedi**; AD entegrasyonu gerçek AD testi yapılmadan tamamlanmış sayılmaz. Yapılacaklar:
  [Şirket ortamında yapılacaklar](#şirket-ortamında-yapılacaklar).

## Kabul denemesi

### Ortam

| Öğe | Kullanılan |
| --- | --- |
| Uygulama | [`Publish-EnterpriseInventory.ps1`](../deploy/iis/Publish-EnterpriseInventory.ps1) ile üretilen yayın klasörü (85 dosya), `ASPNETCORE_ENVIRONMENT=Production`, Kestrel |
| Adres | `https://envanter.test.local`, test CA'sının verdiği sunucu sertifikası; `http://` adresi `https://`'ye yönlenir |
| Veritabanı | SQL Server 2022 container'ı, `EI_D39` veritabanı (39. gün tatbikatından), uygulama yalnızca runtime hesabıyla (`ei_runtime_test`) bağlanır, bağlantı şifreli ve sertifikası doğrulanır |
| Active Directory | Samba test domain'i (`envanter.test`), LDAPS 636, sertifika zinciri ve sunucu adı doğrulanır; iptal kontrolü kapalı (test CA'sının CRL'i yok) |
| Data Protection | Kalıcı anahtar klasörü (39. gün tatbikatındaki klasör) |
| Tarayıcı | Chromium (Playwright 1.56.1), iki ayrı tarayıcı bağlamı: A `ayse.admin` (`Bim_Envanter`'in doğrudan üyesi), B `primary.user` (birincil grubu `Bim_Envanter`) |

### Veritabanı ve duman testi

[`Update-EnterpriseInventoryDatabase.ps1`](../deploy/sql/Update-EnterpriseInventoryDatabase.ps1) (PowerShell 7.6,
go-sqlcmd):

```text
[PASS ] Bağlantı şifreli
[PASS ] EI_D39 var (Turkish_CI_AS)
[BİLGİ] Uygulanmış 4, bekleyen 0 migration
[PASS ] Bütün migration'lar uygulanmış (4); son: 20261010092652_AddReportingIndexes
[PASS ] Runtime yetkileri doğrulandı: ei_runtime_test (68 denetim; silme, şema değişikliği, audit değişikliği yok)
```

40. günün işi migration eklemedi; tanımların `RowVersion` sütunu ilk migration'dan beri vardır.

[`Test-Deployment.ps1`](../deploy/iis/Test-Deployment.ps1) `ayse.admin` ile: HTTPS ve sertifika, `live` ve `ready`
health uçları, HSTS ve güvenlik başlıkları, `Server` başlığının yokluğu, uygulama sayfaları ve CSP'leri, oturumsuz
API'nin `401` vermesi, HTTP'den yönlendirme, giriş, `/api/auth/me`, demirbaş listesi ve çıkış. Sonuç:
**18 PASS, 0 HATA, 0 UYARI**.

### Kabul yürüyüşü

İki yönetici aynı anda iki tarayıcıda çalışır. Her adım ekranda görüneni ve gerekiyorsa API'den okunan kaydı denetler;
bir denetim çalışmadıysa adım geçmiş sayılmaz. Betik depoya eklenmedi (test ortamının adreslerine bağlıdır); aynı
akışların depodaki karşılıkları sağ sütundadır.

| # | Adım | Ne denetlendi | Sonuç | Depodaki karşılığı |
| ---: | --- | --- | --- | --- |
| 1 | Yanlış parola | "Kullanıcı adı veya parola hatalı." görünür, parola alanı boşalır | PASS | `auth.spec.ts`, `SambaSignInTests` |
| 2 | `Bim_Envanter` üyesi olmayan AD kullanıcısı (`mehmet.user`) | "Bu uygulamaya giriş yetkiniz yok." | PASS | `SambaAccessCheckTests` |
| 3 | Devre dışı AD hesabı | "Hesabınızla şu anda giriş yapılamıyor." | PASS | `SambaSignInTests` |
| 4 | Üye girişi (LDAPS) | A Gösterge Paneli'ni açar ve "Canlı" (SignalR) görünür; B de girer | PASS | `SambaSignInTests`, `InventoryHubTests` |
| 5 | Tanımlar | Marka, model (markanın altına), şehir, departman ve şehre bağlı iki lokasyon ekranlardan eklenir | PASS | `definitions.spec.ts`, `LookupApiTests` |
| 6 | Demirbaş ekleme | Formdan tür, marka, model, şehir, lokasyon ve departmanla kaydedilir | PASS | `asset-form.spec.ts` |
| 7 | Arama | Sunucu tarafı arama demirbaşı bulur | PASS | `inventory.spec.ts` |
| 8 | Eşzamanlı düzenleme | B bilgisayar adını değiştirip kaydeder; aynı formu önceden açmış olan A'nın kaydı `409` ve "siz düzenlerken başka bir kullanıcı tarafından değiştirildi" uyarısıyla reddedilir; API'den okunan kayıtta B'nin değeri durur, A'nınki yazılmamıştır | PASS | `concurrency.spec.ts`, `AssetUpdateTests` |
| 9 | Zimmet | A, AD'de aranan ve `Bim_Envanter` üyesi olmayan Mehmet Öztürk'e zimmet verir; B'nin açık Zimmetler ekranı sayfa yenilenmeden kaydı gösterir | PASS | `assignment.spec.ts`, `live.spec.ts` |
| 10 | İade | İade alınır; zimmet geçmişinde dönem "İade alındı" olarak kalır | PASS | `assignment.spec.ts` |
| 11 | Konum değiştirme | Lokasyon Kat 1'den Kat 2'ye alınır | PASS | `location.spec.ts` |
| 12 | Denetim kaydı | API'de Created, Updated, Assigned, Returned, LocationChanged kayıtları; her birinde kullanıcı ve correlation ID; Updated kaydı B'nindir ve eski/yeni bilgisayar adını taşır; Denetim Geçmişi ekranında 5 kayıt satırı görünür | PASS | `audit.spec.ts`, `AuditLogApiTests` |
| 13 | Excel | Ekrandaki filtreyle `envanter-YYYY-AA-GG.xlsx` iner ve geçerli bir Open XML (zip) dosyasıdır | PASS | `export.spec.ts` (içeriği ve sırayı denetler) |
| 14 | Raporlar | Raporlar ekranı açılır, envanter özeti tablosu gelir | PASS | `reports.spec.ts`, `AssetSummaryReportTests` (rakamlar) |
| 15 | Tanım değiştirme | Markanın adı değiştirilip pasifleştirilir; listede "Pasif" görünür, yeni demirbaş formunda sunulmaz | PASS | `definitions.spec.ts`, `LookupUpdateTests` |
| 16 | Arşivleme | Demirbaş listeden çıkar, arşiv filtresiyle görünür | PASS | `inventory.spec.ts`, `SoftDeleteTests` |
| 17 | Tarayıcı deposu ve çerezler | İki tarayıcıda `localStorage` ve `sessionStorage` boş; bütün çerezler `__Host-`, `Secure`, `HttpOnly`, `SameSite=Strict` | PASS | `browserStorage.test.ts`, `SessionSecurityTests` |
| 18 | Çıkış | Oturum sunucuda biter (`/api/auth/me` `401`), korumalı sayfa giriş ekranına döner | PASS | `auth.spec.ts`, `SessionSecurityTests` |
| 19 | Konsol ve ağ | Konsolda hata ve CSP ihlali yok; hata yanıtı olarak yalnızca beklenenler var: 1. adımın `401`'i, 2. ve 3. adımın `403`'ü, 8. adımın `409`'u, girişten önce ve çıkıştan sonra `/api/auth/me` `401` | PASS | — |

Yürüyüş üç kez çalıştırıldı. İlk çalıştırmada 8., 12. ve 18. adımlar, betiğin API'yi okuyan kısmı (Node.js) test
CA'sına güvenmediği için sertifika hatasıyla düştü; tarayıcı tarafı geçmişti. 19. adım tarayıcının beklenen `401`,
`403` ve `409` yanıtları için yazdığı konsol satırlarını hata saymıştı. Betik test CA'sını Node.js'e tanıtacak ve hata
yanıtlarını tek tek beklenenlerle karşılaştıracak şekilde düzeltildi; uygulamada değişiklik gerekmedi. Üçüncü
çalıştırmanın sonucu yukarıdadır.

## 40 günlük plan

Her günün kabul ölçütü [`proje_talimatlari.md`](../proje_talimatlari.md)'dendir. Commit sütunu o günün
işini içeren commit'tir; günün sonundaki inceleme düzeltmeleri aynı satırdadır.

| Gün | Kabul ölçütü | Kanıt | Commit | Durum |
| ---: | --- | --- | --- | --- |
| 1 | Solution build geçer; referans yönleri doğrudur | `dotnet build`, `LayerDependencyTests` | `c2cfe3a` | Tamam |
| 2 | Türkçe ilk arayüz açılır | `AppLayout.test.tsx`, `theme.test.ts` | `7c38aaa`, `9e37325` | Tamam |
| 3 | Domain EF bağımlılığı taşımaz; PK/FK, uzunluk, delete davranışı tanımlı | `LayerDependencyTests`, `ModelConfigurationTests`, [`domain-model.md`](domain-model.md) | `ef7520e`–`bf8f3c9` | Tamam |
| 4 | Migration SQL Server test DB'de uygulanır; indeks, snapshot, SQL betiği doğrulanır | `MigrationTests`, `DatabaseConstraintTests`, [`database.md`](database.md) | `e3f9c5e`, `fafc7a0` | Tamam |
| 5 | API build ve health başarılı | `HealthEndpointTests`, [`api.md`](api.md#health-endpointleri) | `f4a200b`, `4c67c78` | Tamam |
| 6 | Sertifika doğrulama testi geçer veya ortam engeli belgelenir | `LdapsCertificateValidatorTests`, `SambaLdapsTests` | `4599f6c` | Test ortamında tamam (Samba) |
| 7 | Doğru/yanlış parola senaryoları test edilir | `SambaSignInTests` | `4599f6c`, `f1aeeb7` | Test ortamında tamam (Samba) |
| 8 | Grup dışı kullanıcı reddedilir | `SambaAccessCheckTests`, `GroupMembershipTests` | `4599f6c`, `f1aeeb7` | Test ortamında tamam (Samba) |
| 9 | Oturum güvenlik testleri geçer | `SessionSecurityTests`, [`session-security.md`](session-security.md) | `7e4ea61` | Tamam |
| 10 | Yetkili Dashboard açar; yetkisiz erişemez | `LoginPage.test.tsx`, `auth.spec.ts` | `f6818b4` | Tamam |
| 11 | Filtre ve deterministik sayfalama SQL'de çalışır | `AssetQueryTests`, [`assets-api.md`](assets-api.md) | `ec2c90a` | Tamam |
| 12 | Geçerli kayıt oluşur; hatalı reddedilir | `AssetCreateTests` | `b65bc97` | Tamam |
| 13 | İki ayrı DbContext ile çakışmada `409`; veri sessizce ezilmez | `AssetUpdateTests` | `d42de2f` | Tamam |
| 14 | Arşiv listeden çıkar; geçmiş korunur; başarısız işlemde audit ve değişiklik birlikte geri alınır | `SoftDeleteTests`, `AssetAuditTrailTests` | `94d3b7f` | Tamam |
| 15 | Filtre testleri geçer | `AssetQueryTests` | `be7c00c` | Tamam |
| 16 | Gerçek DB istatistikleri görüntülenir | `DashboardConsistencyTests`, `dashboard.spec.ts` | `9d1d2b7` | Tamam |
| 17 | Tablo, sıralama, sayfalama çalışır | `InventoryPage.test.tsx`, `inventory.spec.ts` | `f50b229` | Tamam |
| 18 | Arama ve filtreler API ile tutarlı | `inventory.spec.ts` | `95fd25f` | Tamam |
| 19 | Kayıt formdan kaydedilir | `AssetForm.test.tsx`, `asset-form.spec.ts` | `a8df1bf` | Tamam |
| 20 | Ekranlar ve hata durumları test edilir | `states.test.tsx`, `responsive.spec.ts` | `63636dc` | Tamam |
| 21 | Grup üyesi olmayan AD çalışanı zimmet için seçilebilir | `SambaEmployeeDirectoryTests` | `508506f` | Test ortamında tamam (Samba) |
| 22 | Eşzamanlı iki istekte yalnızca bir aktif zimmet | `ConcurrencyTests` | `bb075c5` | Tamam |
| 23 | İade ve geçmiş doğru kaydedilir | `AssetAssignmentTests` | `bb075c5` | Tamam |
| 24 | Zimmet ver/iade et çalışır | `assignment.spec.ts` | `0400698` | Tamam |
| 25 | Konum değişiklikleri doğrulanır | `AssetLocationTests`, `location.spec.ts` | `09a488d` | Tamam |
| 26 | Yetkisiz bağlantı reddedilir | `InventoryHubTests` | `a62e2c3` | Tamam |
| 27 | Commit sonrası bildirim gelir | `AssetEventTests` | `dbd6a31` | Tamam |
| 28 | Diğer ekranda veri yenilenir | `LiveUpdates.test.tsx`, `live.spec.ts` | `d126e8a` | Tamam |
| 29 | Yeniden bağlanınca veriler eşitlenir | `reconnect.test.ts`, `live.spec.ts` | `bac1824` | Tamam |
| 30 | Veri kaybı ve çift zimmet olmaz | `ConcurrencyTests`, `concurrency.spec.ts`, [`concurrency.md`](concurrency.md) | `f932291` | Tamam |
| 31 | Eski/yeni değerler izlenebilir | `AuditLogApiTests`, `audit.spec.ts`, [`audit.md`](audit.md) | `a345bb5` | Tamam |
| 32 | Filtreye uygun Excel çıkar | `export.spec.ts`, [`export.md`](export.md) | `9376810` | Tamam (Microsoft Excel'de açılmadı) |
| 33 | Rakamlar DB ile tutarlı | `DashboardConsistencyTests`, `AssetSummaryReportTests` | `60e923b` | Tamam |
| 34 | Türkçe filtre ve export çalışır | `reports.spec.ts`, [`reports.md`](reports.md) | `a762832` | Tamam |
| 35 | Temsili yükte SQL/süre/sorgu sayısı ölçülür; iyileştirmeler belgelenir | `LoadMeasurementTests`, `QueryCountTests`, [`performance.md`](performance.md) | `b41ee1c` | Tamam |
| 36 | Kritik erişim kontrolleri geçer | `EndpointAccessTests`, `InputSecurityTests`, [`security-checklist.md`](security-checklist.md) | `f3fb241`, `cea67f3` | Tamam |
| 37 | Kısıt, eşzamanlılık, geri alma ve audit testleri gerçek sonuçlarla raporlanır | [`test-report.md`](test-report.md) | `bd3b387` | Tamam |
| 38 | Test URL HTTPS ile açılır | `Test-Deployment.ps1` duman testi, [`deploy/iis/README.md`](../deploy/iis/README.md) | `d2b1a69` | Test ortamında tamam; IIS **denenmedi** |
| 39 | Restore ve key kalıcılığı doğrulanır; runtime/migration yetkileri ayrı | `DatabaseRecoveryTests`, `RuntimePermissionTests`, [`database-rollback.md`](../deploy/database-rollback.md) | `7551c09` | Test ortamında tamam; DPAPI **denenmedi** |
| 40 | Kritik akışlar ve dokümanlar tamam | Bu belge, kabul denemesi | `e82f0c7` ve bu belgenin commit'i | Test ortamında tamam |

## Gereksinimler ve kanıtları

Durum sütunu: **Tamam** test edildi veya çalıştırılarak denendi; **Test ortamında tamam** yalnızca test ortamında
denendi, şirket ortamında tekrarlanacak; **Denenmedi** yazıldı ama çalıştırılamadı (ortam yok).

### 1. Teknoloji ve dağıtım mimarisi

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| .NET 10 ASP.NET Core Web API, Clean Architecture (Domain, Application, Infrastructure, Api), FluentValidation, ProblemDetails, Serilog | `src/`, [`architecture.md`](architecture.md) | `LayerDependencyTests` (referans yönleri) | Tamam |
| React + TypeScript + Vite, Material UI, TanStack Query, React Hook Form + Zod, React Router, SignalR istemcisi; arayüz Türkçe, kod ve veritabanı adları İngilizce | `src/EnterpriseInventory.Web` | Vitest ve Playwright testleri, `npm run lint`, `tsc` | Tamam |
| Mevcut SQL Server, EF Core Code First, migration, transaction, indeks, RowVersion; Generic Repository yok | [`database.md`](database.md) | `MigrationTests`, `DatabaseConstraintTests`, `LayerDependencyTests` | Tamam |
| Windows Server + IIS, intranet, şirket CA'sının sertifikasıyla HTTPS, React, `/api` ve `/hubs` aynı adreste | [`deploy/iis`](../deploy/iis/README.md), `web.config`, `WebAppHosting` | `WebAppHostingTests`, `WebConfigTests`; yayın klasörü Production'da HTTPS ile denendi (38. ve 40. gün) | Test ortamında tamam; IIS **denenmedi** |
| Solution: Domain, Application, Infrastructure, Api, Web, UnitTests, IntegrationTests; `docs`, `scripts`, `deploy` | Depo kökü | `dotnet build EnterpriseInventory.slnx` | Tamam |

### 1.1. Entity Framework Core

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| EF Core, SqlServer, Design ve dotnet-ef sürümleri sabit ve uyumlu | `EnterpriseInventory.Infrastructure.csproj` (10.0.12), `dotnet-tools.json` | `dotnet tool restore`, `dotnet ef migrations script` çalıştı | Tamam |
| Domain EF'e bağımlı değil; `Persistence` altında DbContext, Configurations, Migrations, Seed; sözleşmeler Application'da | `src/EnterpriseInventory.Infrastructure/Persistence` | `LayerDependencyTests` | Tamam |
| EF bağımlılığının katman sınırları README'de | [README](../README.md#veritabanı-ef-core) | — | Tamam |
| Scoped DbContext, paralel sorgu yok, arka plan işlerinde ayrı scope; async ve CancellationToken | Infrastructure | `LayerDependencyTests`, kod incelemesi | Tamam |
| Her entity için `IEntityTypeConfiguration<T>`: tablo, PK/FK, zorunlu alan, uzunluk, ilişki, indeks, check constraint, delete davranışı; marka/model tutarlılığı; kontrolsüz cascade yok | `Persistence/Configurations` | `ModelConfigurationTests`, `DatabaseConstraintTests`, `AssetConsistencyTests` | Tamam |
| `AssetCode` benzersiz; seri numarası normalize, boş seri `NULL`, filtreli benzersiz indeks; tek aktif zimmet filtreli indeksi; benzersizlik ihlali Türkçe iş hatası | Configurations, `AssetStore` | `DatabaseConstraintTests`, `AssetCreateTests`, `ConcurrencyTests` | Tamam |
| `RowVersion` `byte[]`, `IsRowVersion()`; güncelleme/arşivde zorunlu, DTO'da base64; istemci sürümü `OriginalValue`; `409` ve Türkçe mesaj; zimmet ve arşivde de koruma | `AssetVersions`, `AssetStore` | `AssetUpdateTests` (iki ayrı DbContext), `ConcurrencyTests`, `concurrency.spec.ts` | Tamam |
| Soft delete `HasQueryFilter`; `IgnoreQueryFilters` yalnızca yetkili geçmiş/arşiv sorgularında | `ApplicationDbContext` | `SoftDeleteTests`, `LayerDependencyTests` | Tamam |
| `CreatedAt/UpdatedAt` UTC, `CreatedBy/UpdatedBy` oturumdan; audit aynı transaction'da, audit kaydı tekrar audit edilmez | `AuditableEntityInterceptor`, `AssetStore` | `AuditableEntityInterceptorTests`, `AssetAuditTrailTests`, `AuditLogApiTests` | Tamam |
| Çok adımlı işlemlerde açık transaction, execution strategy içinde; SignalR yalnızca commit sonrası, bildirim hatası commit'i geri alınmış göstermez | `Transactions`, `AssetChangePublisher` | `AssetAssignmentTests`, `AssetEventTests`, `AssetChangePublisherTests` | Tamam |
| Salt okunur sorgularda `AsNoTracking` + DTO projection; filtre/sıralama/sayfalama SQL'de, deterministik ikinci anahtar; lazy loading kapalı, N+1 yok; parametreli sorgu | `AssetStore`, `LookupStore`, `ReportStore` | `QueryCountTests`, `AssetQueryTests`, `InputSecurityTests` (14 girdi × 12 filtre) | Tamam |
| `DefaultConnection` güvenli placeholder; parola repoda yok; runtime hesabının şema yetkisi yok; başlangıçta `Migrate`/`EnsureCreated` yok | `appsettings*.json`, `grant-runtime-permissions.sql` | `RepositorySecretsTests`, `RuntimePermissionTests`, `The_API_never_creates_or_migrates_the_database_when_it_starts` | Tamam (şirket SQL Server'ında denenmedi) |
| Idempotent migration betiği, yedek ve veri kaybı riski değerlendirilmiş geri dönüş planı | [`deploy/database-rollback.md`](../deploy/database-rollback.md) | `DeploymentScriptTests`, `DatabaseRecoveryTests`, 39. gün tatbikatı | Test ortamında tamam |
| Seed idempotent; production'a sahte kullanıcı veya örnek parola yok | `Persistence/Seed` | `DevelopmentSeedTests` | Tamam |
| README'de `migrations add InitialCreate`, `database update`, `migrations script --idempotent` (`--project`, `--startup-project`); design-time factory; migration, betik ve snapshot Git'te | [README](../README.md#veritabanı-ef-core) | Komutlar 20. günde gerçekten çalıştırıldı; `MigrationTests` betiğin güncel olduğunu denetler | Tamam |
| EF testleri gerçek SQL Server'da; InMemory kanıt sayılmaz; FK, filtreli indeks, çift zimmet, soft delete, rollback, audit atomikliği, iki DbContext ile çakışma | `tests/EnterpriseInventory.IntegrationTests` | `LayerDependencyTests.No_project_uses_an_in_memory_or_sqlite_database_provider`; [test raporu](test-report.md) | Tamam |

### 2. Active Directory girişi

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| Türkçe giriş ekranı; LDAPS 636; sertifika zinciri ve sunucu adı doğrulanır, doğrulama kapatılamaz | `LoginPage`, `LdapConnectionFactory`, `LdapsCertificateValidator` | `LdapsCertificateValidatorTests` (yanlış ad, süresi dolmuş, güvenilmeyen kök), `SambaLdapsTests` | Test ortamında tamam (Samba) |
| Yalnızca `Bim_Envanter` üyeleri, grup SID'iyle; iç içe grup politikası açık ayar; üyeye Administrator rolü | `GroupMembership`, `LdapDirectoryService` | `SambaAccessCheckTests` (tuzak grup adı reddedilir), `GroupMembershipTests` | Test ortamında tamam (Samba) |
| Üye olmayan giriş yapamaz; API ve hub reddeder; pasif hesap reddedilir; AD erişilemezse yeni giriş fail-closed | `SignInHandler`, fallback policy | `SambaSignInTests`, `EndpointAccessTests`, `InventoryHubTests`, kabul denemesi | Test ortamında tamam (Samba) |
| Parola veritabanında, logda, tarayıcı deposunda yok; HTTPS; HttpOnly, Secure, SameSite çerez, sunucu taraflı oturum; CSRF, rate limiting, çıkış, zaman aşımı, oturum iptali, düzenli AD yeniden kontrolü | `Security/`, `UserSession` | `SessionSecurityTests`, `SignInRecordingTests`, `browserStorage.test.ts`, kabul denemesi (depo boş, çerez bayrakları) | Tamam |
| Domain, sunucu, BaseDn, grup SID, servis hesabı, bağlantı dizesi için güvenli placeholder; sır repoda yok | `appsettings.Production.json` | `Placeholders_from_the_sample_configuration_are_refused`, `RepositorySecretsTests` | Tamam |
| Zimmetlenecek çalışanlar AD'de aranır, `Bim_Envanter` üyesi olmaları gerekmez; yöneticiler ve çalışanlar ayrı tablolarda | `LdapEmployeeDirectory`, `AdminUser`/`Employee` | `SambaEmployeeDirectoryTests`, `AssignmentWithActiveDirectoryTests`, kabul denemesi (grup dışı `mehmet.user`'a zimmet) | Test ortamında tamam (Samba) |
| Gerçek (şirket) AD ile giriş, grup SID'i, servis hesabı ve DC sertifikası | — | — | **Denenmedi**; AD entegrasyonu bu yapılmadan tamamlanmış sayılmaz |

### 3. Veri modeli ve iş kuralları

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| Assets, AssetAssignments, Employees, Brands, AssetModels, Cities, Departments, Locations, AuditLogs alanları | [`domain-model.md`](domain-model.md) | `ModelConfigurationTests`, `AssetTests` | Tamam |
| Bir cihazda en fazla bir aktif zimmet; iade geçmişi silinmez | Filtreli benzersiz indeks, `Asset.Assign` | `ConcurrencyTests` (eşzamanlı iki zimmetten biri), `DatabaseConstraintTests` | Tamam |
| FK, check constraint, audit alanları, soft delete, RowVersion | Configurations | `DatabaseConstraintTests`, `SoftDeleteTests` | Tamam |
| Kritik kurallar transaction içinde, audit ile birlikte commit, sonra SignalR | `AssetAssignmentStore`, `AssetChangePublisher` | `AssetEventTests` (bildirim yalnızca commit'ten sonra), `AssetAssignmentTests` | Tamam |
| Çoklu sunucu için güvenilir olay stratejisi değerlendirmesi | [`realtime.md`](realtime.md) | Değerlendirme yazıldı (tek sunucu için tasarlandı, çoklu sunucuda outbox ve backplane gerekir) | Tamam (değerlendirme) |

### 4. Kullanıcı ekranları

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| Kurumsal tasarım (#142D4E, #2563EB, beyaz, açık gri), responsive kenar çubuğu ve başlık, yükleme/boş/hata durumları | `app/theme.ts`, `layout/` | `theme.test.ts`, `states.test.tsx`, `AppLayout.test.tsx`, `responsive.spec.ts` (390 px) | Tamam; tek tema (koyu tema yok) |
| Giriş: kullanıcı adı, şifre, Giriş Yap, Türkçe güvenli hata mesajları | `LoginPage` | `LoginPage.test.tsx`, `auth.spec.ts`, kabul denemesi | Tamam |
| Gösterge Paneli: toplam, zimmetli, boşta, arızalı; şehir/departman dağılımı; son işlemler | `DashboardPage` | `DashboardConsistencyTests` (düz SQL ile aynı), `dashboard.spec.ts` | Tamam |
| Envanter tablosu sütunları (Kullanıcı Adı, Bilgisayar Adı, Marka, Model, Seri No, Zimmet Tanımı, Lokasyon/Şehir, Lokasyon/Departman, Demirbaş Kodu, Durum, İşlemler) | `AssetTable` | `InventoryPage.test.tsx` | Tamam |
| Ekle/düzenle/detay/arşivle; sunucu taraflı filtre, arama, sıralama, sayfalama; kolon görünürlüğü; Excel | Envanter sayfaları | `inventory.spec.ts`, `asset-form.spec.ts`, `export.spec.ts` | Tamam (Excel dosyası Microsoft Excel'de **açılmadı**; Open XML SDK ile okundu) |
| Zimmet oluştur/iade et/geçmiş; AD çalışan arama | Detay sayfası, Zimmetler | `assignment.spec.ts`, `definitions.spec.ts`, kabul denemesi | Tamam |
| Lokasyon, şehir, departman ve marka/model yönetimi | Lokasyonlar, Marka ve Modeller (40. gün) | `LookupUpdateTests`, `DefinitionsPages.test.tsx`, `definitions.spec.ts`, kabul denemesi | Tamam |
| Raporlar; denetim geçmişi | Raporlar, Denetim Geçmişi | `reports.spec.ts`, `audit.spec.ts`, `AssetSummaryReportTests` | Tamam |

### 5. API, gerçek zaman ve güvenlik

| Gereksinim | Nerede | Kanıt | Durum |
| --- | --- | --- | --- |
| `/api/auth/login`, `/me`, `/logout`; `/api/assets` GET/POST, `/api/assets/{id}` GET/PUT/DELETE; `/assignments`, `/returns`, `/history`; `/api/dashboard/statistics`; `/api/brands`, `/models`, `/cities`, `/departments`, `/audit-logs` | [`api.md`](api.md), [`assets-api.md`](assets-api.md) | `EndpointAccessTests` yönlendirme tablosunu dolaşır | Tamam |
| SignalR olayları (AssetCreated, AssetUpdated, AssetArchived, AssetAssigned, AssetReturned, AssetLocationChanged) yalnızca bildirim; React sunucudan yeniden okur; yeniden bağlanınca tam eşitleme | [`realtime.md`](realtime.md) | `AssetEventTests`, `LiveUpdates.test.tsx`, `reconnect.test.ts`, `live.spec.ts`, kabul denemesi | Tamam |
| Aynı kaydı iki kişi değiştirirse `409` ve Türkçe uyarı | Demirbaş, zimmet, konum, arşiv, tanımlar | `AssetUpdateTests`, `LookupUpdateTests`, `concurrency.spec.ts`, kabul denemesi | Tamam |
| Her endpoint ve hub için yetkilendirme; doğrulama, CORS, CSP/XSS, CSRF, HTTPS, rate limiting, sır yönetimi, en az yetkili SQL hesabı, güvenli log, audit | [`security-checklist.md`](security-checklist.md) | `EndpointAccessTests`, `InputSecurityTests`, `TransportSecurityTests`, `RuntimePermissionTests`, `RequestLoggingTests` | Tamam |
| Audit: varlık, kimlik, işlem, eski/yeni değer, kullanıcı, zaman, correlation ID; parola ve token yok | [`audit.md`](audit.md) | `AuditLogApiTests`, `SignInRecordingTests`, kabul denemesi | Tamam |

### 6. Kurulum ve konfigürasyon teslimleri

| Teslim | Dosya | Kanıt | Durum |
| --- | --- | --- | --- |
| `appsettings.json`, `appsettings.Production.json` (gizli değer yok) | `src/EnterpriseInventory.Api` | `Shipped_production_settings_cannot_start_until_the_placeholders_are_replaced` | Tamam |
| React `.env.example` | `src/EnterpriseInventory.Web/.env.example` | — | Tamam |
| IIS `web.config` | `src/EnterpriseInventory.Api/web.config` | `WebConfigTests` | Tamam (IIS'te **denenmedi**) |
| SQL migration komutları | [README](../README.md#veritabanı-ef-core), [`database.md`](database.md#migration-komutları), `deploy/sql` | `MigrationTests`, 39. gün tatbikatı | Tamam |
| PowerShell ön kontrol betikleri | `deploy/iis/Test-ServerPrerequisites.ps1`, `Test-Deployment.ps1`, `deploy/sql/*.ps1` | `PowerShellScriptTests`; duman testi ve veritabanı betikleri PowerShell 7.6 ile çalıştı | Test ortamında tamam (IIS bölümleri ve Windows PowerShell 5.1 **denenmedi**) |
| README, güvenlik kontrol listesi, deployment/rollback yönergeleri | [README](../README.md), [`security-checklist.md`](security-checklist.md), [`deploy/iis/README.md`](../deploy/iis/README.md), [`deploy/database-rollback.md`](../deploy/database-rollback.md) | Bağlantı denetimi (bütün göreli bağlantılar ve başlık bağlantıları geçerli) | Tamam |
| IIS: Hosting Bundle, No Managed Code havuz, binding/HTTPS, Data Protection anahtarlarının kalıcı ve güvenli depolanması, log klasörleri ve izinleri | `Install-EnterpriseInventorySite.ps1` | Betik yazıldı; anahtar klasörünün yedekten dönüşü ve klasör yokken başlamama denendi | IIS'te **denenmedi** |
| Yeni SQL sunucusu kurulmaz; migration ve runtime yetkileri ayrı; backup/restore ve rollback test edilir | `deploy/sql` | `RuntimePermissionTests`, `DatabaseRecoveryTests`, 39. gün tatbikatı | Test ortamında tamam |
| Domain bilinmediği için sahte AD yalnızca Development'ta; production'da engelli | `FakeDirectoryService` | `Fake_directory_stops_the_application_outside_development` | Tamam |

### 7. Geliştirme disiplinleri

| Gereksinim | Kanıt | Durum |
| --- | --- | --- |
| Unit, integration, authorization, LDAPS, concurrency, SignalR, React ve end-to-end testleri; hatalar çözülmeden tamam denmez | [Test raporu](test-report.md): bütün takımlar 0 başarısız | Tamam |
| Üretimde anonim yazma endpoint'i yok | `Only_the_health_probes_csrf_token_and_sign_in_allow_anonymous_access` (anonim yalnızca health, CSRF token ve giriş) | Tamam |
| Gün sonunda derleme, test, özet ve sonraki gün | Her günün commit'i ve PR açıklaması | Tamam |

## Şirket ortamında yapılacaklar

Aşağıdakiler bu depoda denenemedi, çünkü şirketin ortamı (domain, sunucular, sertifikalar) belli değil. Hiçbiri
yapılmadan üretime geçilmemeli ve bu işler "tamam" sayılmamalıdır. Sıra, kurulum sırasıdır.

1. **Ayarlar.** `appsettings.Production.json`'daki bütün yer tutucular (domain, DC adı, `BaseDn`, `Bim_Envanter`
   grup SID'i, servis hesabı, bağlantı dizesi, site adresi) şirketin değerleriyle, sırlar ortam değişkeni veya IIS
   yapılandırmasıyla verilmeli. Yer tutucu kalırsa uygulama başlamaz
   (`Shipped_production_settings_cannot_start_until_the_placeholders_are_replaced`).
2. **SQL Server.** Migration hesabı ve runtime hesabı ayrı olmalı; şirketin SQL Server'ında
   [`Update-EnterpriseInventoryDatabase.ps1`](../deploy/sql/Update-EnterpriseInventoryDatabase.ps1) 0 HATA vermeli ve
   runtime yetkileri denetiminden geçmeli. Gerçek yedek klasörüyle yedek alma ve
   [yedekten dönüş tatbikatı](../deploy/database-rollback.md#yedekten-dönüş) yapılmalı. Windows kimlik doğrulaması,
   gMSA ve Windows'taki ODBC `sqlcmd` ile betikler burada denenmedi.
3. **IIS.** Hosting Bundle, uygulama havuzu (No Managed Code), HTTPS bağlaması ve şirket CA'sının sertifikası:
   sunucuda [`Test-ServerPrerequisites.ps1`](../deploy/iis/Test-ServerPrerequisites.ps1),
   [`Install-EnterpriseInventorySite.ps1`](../deploy/iis/Install-EnterpriseInventorySite.ps1) ve
   [`Test-Deployment.ps1`](../deploy/iis/Test-Deployment.ps1) 0 HATA vermeli ([`deploy/iis`](../deploy/iis/README.md)).
   Kurulum betiği ve ön kontrolün IIS bölümleri hiç çalıştırılmadı; Windows PowerShell 5.1 ile de denenmedi.
4. **Data Protection anahtarları.** Anahtar klasörü yalnızca uygulama havuzu kimliğine açık ve DPAPI ile şifreli
   olmalı; anahtarların yedekten geri yüklenmesi Windows'ta denenmeli
   ([`deploy/iis`](../deploy/iis/README.md#data-protection-anahtarları)). DPAPI burada denenmedi.
5. **Active Directory.** [`active-directory.md`](active-directory.md#ortam-engeli-şirketin-gerçek-adsi)'deki sekiz
   madde: DC sertifikasının adı ve CA'sı, CRL/OCSP erişimi (iptal kontrolü açık kalmalı), 636 portu, `active-directory`
   health kontrolü, gerçek grup SID'i ve iç içe grup kararı, servis hesabının okuma izinleri. Bir üye, bir üye olmayan,
   bir pasif ve (varsa) bir iç içe üye hesapla giriş ve çalışan araması denenmeli. **Bu yapılmadan AD entegrasyonu
   tamamlanmış sayılmaz.**
6. **Kabul yürüyüşü.** Yukarıdaki 19 adım şirket ortamında, gerçek iki yönetici hesabı ve grup dışı bir çalışanla
   tekrarlanmalı.
7. **Excel.** Dışa aktarılan dosyalar Microsoft Excel'de açılmalı (burada Open XML SDK ile okunarak doğrulandı).
8. **Tarayıcılar.** Şirkette kullanılan tarayıcılarda (ör. Edge, Chrome) giriş, liste ve canlı yenileme denenmeli;
   burada yalnızca Chromium kullanıldı.
9. **Güvenlik.** Şirket güvenlik ekibinin sızma testi; [`security-checklist.md`](security-checklist.md) onun yerini
   tutmaz.

## Teslim edilenler

| Teslim | Nerede |
| --- | --- |
| Kaynak kod (Domain, Application, Infrastructure, Api, Web) ve testler (UnitTests, IntegrationTests, Vitest, Playwright) | `src/`, `tests/`, `src/EnterpriseInventory.Web/e2e` |
| Kurulum ve geliştirme | [README](../README.md), [`architecture.md`](architecture.md) |
| Ayar örnekleri (sır yok) | `src/EnterpriseInventory.Api/appsettings.json`, `appsettings.Production.json`, `src/EnterpriseInventory.Web/.env.example` |
| IIS | `src/EnterpriseInventory.Api/web.config`, [`deploy/iis`](../deploy/iis/README.md) |
| Veritabanı | EF Core migration'ları, idempotent betik, [`database.md`](database.md), [`deploy/sql`](../deploy/sql), [`database-rollback.md`](../deploy/database-rollback.md) |
| API | [`api.md`](api.md), [`assets-api.md`](assets-api.md), [`assignments-api.md`](assignments-api.md), [`lookups-api.md`](lookups-api.md), [`audit.md`](audit.md), [`export.md`](export.md), [`reports.md`](reports.md) |
| Güvenlik | [`security-checklist.md`](security-checklist.md), [`session-security.md`](session-security.md), [`active-directory.md`](active-directory.md), [`web-auth.md`](web-auth.md) |
| Gerçek zaman ve eşzamanlılık | [`realtime.md`](realtime.md), [`concurrency.md`](concurrency.md) |
| Ekranlar | [`inventory-ui.md`](inventory-ui.md) |
| Performans | [`performance.md`](performance.md) |
| Test sonuçları | [`test-report.md`](test-report.md), bu belge |

Depoda parola, anahtar veya bağlantı dizesi yoktur (`RepositorySecretsTests` her çalıştırmada tarar). Production
başlangıcında migration veya `EnsureCreated` çalışmaz, sahte dizin Development dışında başlamaz, anonim yazma uç noktası
yoktur.
