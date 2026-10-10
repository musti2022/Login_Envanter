# Test raporu

37. gün test çalıştırmasının gerçek sonuçları. Rakamlar `dotnet test --logger trx`, Vitest ve Playwright'ın JSON
çıktısından [`scripts/test-report/summarize-results.py`](../scripts/test-report/summarize-results.py) ile üretildi;
elle yazılmış sonuç yoktur. Çalıştırılan kod `cea67f3` commit'idir (bu rapor yalnızca doküman ve betik ekler).
Tarih: 10 Ekim 2026.

## Ortam

| Öğe | Sürüm |
| --- | --- |
| İşletim sistemi | Linux 6.18 (geliştirme container'ı; Windows Server ve IIS değil) |
| .NET SDK | 10.0.112, EF Core 10.0.12 |
| SQL Server | SQL Server 2022 CU27 (16.0.4295.3) Developer Edition, Docker container, `Turkish_CI_AS` |
| Active Directory | Samba 4.19.5 AD test domain'i (`envanter.test`), LDAPS 636, test CA'sı ([`scripts/test-ad`](../scripts/test-ad/README.md)) |
| Node.js | 22.22.0; Vitest 5, jsdom |
| Tarayıcı | Playwright 1.56.1, Chromium 1194 (headless) |

## Özet

| Takım | Ne çalışır | Test | Geçti | Başarısız | Atlandı | Süre |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Birim (`EnterpriseInventory.UnitTests`) | Domain, doğrulama, servisler, mimari kurallar, depo taraması | 450 (247 test metodu) | 450 | 0 | 0 | 5,5 sn |
| SQL Server entegrasyon (`EnterpriseInventory.IntegrationTests`) | Gerçek SQL Server'da API, EF Core, migration; Samba AD'ye LDAPS | 627 (346 test metodu) | 626 | 0 | 1 | 3 dk 48 sn |
| Web birim (Vitest) | React bileşenleri, formlar, API istemcisi | 184 (23 dosya) | 184 | 0 | 0 | 53,1 sn |
| Tarayıcı (Playwright E2E) | Üretim derlemesi + gerçek API + gerçek SQL Server | 36 | 36 | 0 | 0 | 72,1 sn |

Atlanan tek test `LoadMeasurementTests.Measure_the_screens_requests_on_a_representative_inventory`'dir: ölçüm için
dakikalar sürdüğü için yalnızca `EI_PERF_REPORT` tanımlıyken çalışır, sonuçları [`performance.md`](performance.md)'dedir. Başarısız test yoktur.

### Neyin gerçek olduğu

- **EF Core testleri gerçek SQL Server'da çalışır.** Testler `EI_Test_<guid>` adlı yeni veritabanları oluşturur
  (`Turkish_CI_AS`), migration'ları uygular ve sonunda siler. EF InMemory veya SQLite provider'ı kullanılmaz;
  `LayerDependencyTests.No_project_uses_an_in_memory_or_sqlite_database_provider` bunu denetler. Kısıt, collation,
  `rowversion`, transaction ve filtreli indeks davranışının kanıtı bu testlerdir.
- **AD testleri:** 66 test Samba test domain'ine gerçek LDAPS ile bağlanır (giriş, grup SID'i, iç içe grup, pasif ve
  süresi dolmuş hesap, çalışan araması, servis hesabıyla yeniden kontrol). 25 test sertifika kurallarını testin kendi
  kurduğu TLS sunucusuyla dener (yanlış ad, süresi dolmuş, güvenilmeyen kök, yanıt vermeyen sunucu). Şirketin gerçek
  AD'si ile **denenmedi**.
- **E2E testleri** React'in üretim derlemesini Chromium'da açar; API Development ortamında, gerçek SQL Server
  veritabanıyla (`EI_E2E`) ve **sahte dizinle** çalışır (sahte dizin Development dışında başlamaz). Gerçek AD ile giriş
  entegrasyon testlerinde Samba'ya karşı denenir.
- **Eşzamanlılık testleri** istekleri aynı anda gönderir ve hepsi aynı sürümü okuyana kadar kayıtlarını bekletir
  (`SaveGate`), böylece yarış gerçekten olur. Sonuç veritabanından okunur: yalnızca HTTP durumuna bakılmaz, satırlar
  ve audit kayıtları sayılır.
- **Geri alma testleri** audit kaydını veya bir adımı bilerek başarısız yapar ve veritabanında yarım değişiklik
  kalmadığını doğrular; geri alınan değişiklik için SignalR bildirimi de gitmez
  (`A_change_that_is_rolled_back_is_not_announced`). Migration geri alma testleri boş ve dolu veritabanında çalışır.

## Kabul kategorileri

Aşağıdaki tablolar 37. gün kabulünün istediği dört konudaki testlerin tek tek sonuçlarıdır. Bir test birden fazla
konuya girebilir (ör. aynı anda yapılan oluşturmalarda benzersiz indeks). Süre, TRX/JSON'daki test süresidir; bir
metodun birden fazla durumu (theory) varsa toplamdır.

### Veritabanı kısıtları

22 test, 22 geçti, 0 başarısız.

| Takım | Sınıf / dosya | Test | Sonuç | Süre (sn) |
| --- | --- | --- | --- | ---: |
| Birim | `AssetTests` | Blank serial number is stored as null so the unique index ignores it | geçti (3/3 durum) | 0.00 |
| Birim | `ModelConfigurationTests` | Codes serial numbers and active assignments are unique | geçti | 0.00 |
| SQL Server entegrasyon | `AssetCreateTests` | Simultaneous creates with the same code make exactly one asset | geçti | 0.31 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Asset codes are unique | geçti | 0.01 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Serial numbers are unique but many assets may have none | geçti | 0.03 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | An active assignment saved behind the apis back makes the unique index refuse the apis assignment | geçti | 0.47 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Assigned asset cannot be archived and cannot get a second active assignment | geçti | 0.03 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Audit records need a known action and old or new values | geçti | 0.01 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Brand and department names are unique | geçti | 0.01 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Location in another city is rejected | geçti | 0.02 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Location names are unique within a city only | geçti | 0.03 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Lookup names are unique with Turkish case rules | geçti | 0.01 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Model names are unique within a brand only | geçti | 0.02 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Model of another brand is rejected | geçti | 0.02 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Return must not precede the assignment and needs the returning user | geçti | 0.02 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Unknown status or type and blank code or serial number are rejected | geçti | 0.02 |
| SQL Server entegrasyon | `LookupApiTests` | A model name is unique within its brand and a location name within its city | geçti | 0.32 |
| SQL Server entegrasyon | `LookupApiTests` | A name that is taken ignoring case is refused even by an inactive lookup | geçti (5/5 durum) | 1.34 |
| SQL Server entegrasyon | `SoftDeleteTests` | An archived asset and its assignments are left out of normal queries but kept | geçti | 0.11 |
| SQL Server entegrasyon | `SoftDeleteTests` | An archived asset keeps its code and serial number | geçti | 0.03 |
| SQL Server entegrasyon | `SoftDeleteTests` | The filter runs in SQL | geçti | 0.01 |
| Tarayıcı (E2E) | `asset-form.spec.ts` | the API refuses a code that is taken and the form says so under the field | geçti | 7.86 |

### Eşzamanlılık

28 test, 28 geçti, 0 başarısız.

| Takım | Sınıf / dosya | Test | Sonuç | Süre (sn) |
| --- | --- | --- | --- | ---: |
| Birim | `ModelConfigurationTests` | Audited entities carry a rowversion concurrency token | geçti | 0.82 |
| SQL Server entegrasyon | `AssetArchiveTests` | An edit and an archive of the same version at once save exactly one | geçti | 0.32 |
| SQL Server entegrasyon | `AssetArchiveTests` | Archiving an older version gets 409 and archives nothing | geçti | 0.35 |
| SQL Server entegrasyon | `AssetAssignmentTests` | An assignment based on an old version of the asset is refused with 409 | geçti | 0.34 |
| SQL Server entegrasyon | `AssetConsistencyTests` | An archive after another context saved the asset gets 409 and leaves it active | geçti | 0.42 |
| SQL Server entegrasyon | `AssetConsistencyTests` | An update saved by another context between read and save gets 409 and keeps the other change | geçti | 0.38 |
| SQL Server entegrasyon | `AssetCreateTests` | Simultaneous creates with the same code make exactly one asset | geçti | 0.31 |
| SQL Server entegrasyon | `AssetLocationTests` | A move of an older version gets 409 and changes nothing | geçti | 0.33 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Return and handover of the same asset at once is a conflict | geçti | 0.03 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Saving a stale copy throws a concurrency exception and keeps the first change | geçti | 0.03 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Two admins assigning the same asset at once leave one active assignment | geçti | 0.08 |
| SQL Server entegrasyon | `AssetUpdateTests` | A made up row version gets 409 | geçti | 0.30 |
| SQL Server entegrasyon | `AssetUpdateTests` | An update of an older version gets 409 and keeps what the other user saved | geçti | 0.38 |
| SQL Server entegrasyon | `AssetUpdateTests` | Simultaneous updates of the same version save exactly one | geçti | 0.39 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | Of eight simultaneous assignments of one asset exactly one is committed | geçti | 0.49 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | Of eight simultaneous returns exactly one is committed | geçti | 0.52 |
| SQL Server entegrasyon | `ConcurrencyTests` | Assignments and returns racing for one asset keep every committed change and a consistent history | geçti | 0.80 |
| SQL Server entegrasyon | `ConcurrencyTests` | Different changes racing on one version leave exactly one of them | geçti | 0.95 |
| SQL Server entegrasyon | `ConcurrencyTests` | Of eight simultaneous edits of one version exactly one is saved whole | geçti | 0.43 |
| SQL Server entegrasyon | `ConcurrencyTests` | Six simultaneous creates with one asset code store one asset | geçti | 0.92 |
| SQL Server entegrasyon | `ConcurrencyTests` | Ten assets wanted by four employees at once each get exactly one holder | geçti | 1.06 |
| SQL Server entegrasyon | `ConcurrencyTests` | Twelve assets given to one new employee at once are all assigned | geçti | 0.87 |
| SQL Server entegrasyon | `SignInRecordingTests` | Simultaneous first sign ins create one administrator record | geçti | 1.32 |
| Tarayıcı (E2E) | `asset-form.spec.ts` | an edit made on an outdated version is refused and redone on the current one | geçti | 2.30 |
| Tarayıcı (E2E) | `assignment.spec.ts` | an assignment to an asset someone changed meanwhile is refused with a warning | geçti | 3.10 |
| Tarayıcı (E2E) | `concurrency.spec.ts` | two administrators giving one asset away at the same moment leave exactly one holder | geçti | 4.97 |
| Tarayıcı (E2E) | `concurrency.spec.ts` | two administrators saving one asset at the same moment: one change is kept whole, the other is refused | geçti | 3.63 |
| Tarayıcı (E2E) | `live.spec.ts` | an edit is warned when another screen saves the asset first | geçti | 2.65 |

### Geri alma (rollback)

13 test, 13 geçti, 0 başarısız.

| Takım | Sınıf / dosya | Test | Sonuç | Süre (sn) |
| --- | --- | --- | --- | ---: |
| Birim | `AssetChangePublisherTests` | A failing notifier is logged and the committed change still succeeds | geçti | 0.01 |
| Birim | `SignInHandlerTests` | A sign in that cannot be recorded is refused | geçti | 0.00 |
| SQL Server entegrasyon | `AssetConsistencyTests` | A create whose audit record fails leaves no asset behind | geçti | 0.46 |
| SQL Server entegrasyon | `AssetConsistencyTests` | An archive whose audit record fails leaves the asset in the list | geçti | 0.40 |
| SQL Server entegrasyon | `AssetConsistencyTests` | An update whose audit record fails changes nothing | geçti | 0.38 |
| SQL Server entegrasyon | `AssetEventTests` | A change that is rolled back is not announced | geçti | 0.38 |
| SQL Server entegrasyon | `AssetEventTests` | A failing notifier neither fails nor undoes a committed change | geçti | 0.34 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | A move whose audit record fails leaves the asset where it was | geçti | 0.43 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | A return whose audit record fails leaves the asset with its holder | geçti | 0.45 |
| SQL Server entegrasyon | `AssignmentConsistencyTests` | An assignment whose audit record fails leaves no assignment and no employee record | geçti | 0.34 |
| SQL Server entegrasyon | `DeploymentScriptTests` | Every migration can be rolled back and applied again on an empty database | geçti | 0.58 |
| SQL Server entegrasyon | `DeploymentScriptTests` | Rolling back past the sign in audit actions is refused once sign ins are recorded | geçti | 0.36 |
| SQL Server entegrasyon | `LoginEndpointTests` | A sign in that cannot be recorded starts no session | geçti | 0.46 |

### Denetim kaydı (audit)

35 test, 35 geçti, 0 başarısız.

| Takım | Sınıf / dosya | Test | Sonuç | Süre (sn) |
| --- | --- | --- | --- | ---: |
| Birim | `AssetAuditTrailTests` | A snapshot records every field with lookup names | geçti | 0.02 |
| Birim | `AssetAuditTrailTests` | An edit is recorded once per kind of change with only the changed fields | geçti | 0.01 |
| Birim | `AssetAuditTrailTests` | Nothing changed nothing recorded | geçti | 0.00 |
| Birim | `AssetAuditTrailTests` | Turkish letters stay readable but html is escaped | geçti | 0.06 |
| Birim | `AuditLogRequestTests` | Every filter together is accepted and read into the criteria | geçti | 0.01 |
| Birim | `AuditLogRequestTests` | Moments with an offset are accepted | geçti (3/3 durum) | 0.09 |
| Birim | `AuditLogRequestTests` | Moments without an offset or in another format are refused | geçti (5/5 durum) | 0.00 |
| Birim | `AuditLogRequestTests` | No filter lists everything from the first page | geçti | 0.00 |
| Birim | `AuditLogRequestTests` | Only the audited kinds of record are accepted | geçti (4/4 durum) | 0.01 |
| Birim | `AuditLogRequestTests` | Text filters are limited in length and refuse control characters | geçti | 0.00 |
| Birim | `AuditLogRequestTests` | The end of the window must come after its start | geçti | 0.00 |
| Birim | `AuditLogTests` | Audit log is append only | geçti | 0.00 |
| Birim | `AuditableEntityTests` | Audit stamps require a user | geçti | 0.00 |
| SQL Server entegrasyon | `AssetCreateTests` | Creating writes a created audit record with the user and correlation id | geçti | 0.25 |
| SQL Server entegrasyon | `AssetPersistenceTests` | Saved asset round trips with audit fields and a row version | geçti | 0.04 |
| SQL Server entegrasyon | `AssetUpdateTests` | Each kind of change gets its own audit record with old and new values | geçti | 0.40 |
| SQL Server entegrasyon | `AuditLogApiTests` | An edit can be followed field by field with its old and new values | geçti | 0.47 |
| SQL Server entegrasyon | `AuditLogApiTests` | Assignments returns and moves record the holder and the place before and after | geçti | 0.45 |
| SQL Server entegrasyon | `AuditLogApiTests` | Filters find records by user action asset code request and time | geçti | 0.61 |
| SQL Server entegrasyon | `AuditLogApiTests` | Invalid filters are refused with a turkish message under the field | geçti (12/12 durum) | 3.32 |
| SQL Server entegrasyon | `AuditLogApiTests` | Lookups are listed under their current names and archived assets stay traceable | geçti | 0.48 |
| SQL Server entegrasyon | `AuditLogApiTests` | Pages hold every record once newest first and one record can be read by its id | geçti | 0.52 |
| SQL Server entegrasyon | `AuditLogApiTests` | Records cannot be written changed or deleted through the api | geçti (3/3 durum) | 0.83 |
| SQL Server entegrasyon | `AuditLogApiTests` | Sign ins are listed without the password | geçti | 0.68 |
| SQL Server entegrasyon | `AuditLogApiTests` | Visitors get 401 and users without the administrator role get 403 | geçti (2/2 durum) | 0.62 |
| SQL Server entegrasyon | `AuditableEntityInterceptorTests` | Records without audit fields also need a signed in user | geçti | 0.07 |
| SQL Server entegrasyon | `AuditableEntityInterceptorTests` | Saving without a signed in user is refused before the database is reached | geçti (3/3 durum) | 0.58 |
| SQL Server entegrasyon | `DatabaseConstraintTests` | Audit records need a known action and old or new values | geçti | 0.01 |
| SQL Server entegrasyon | `LookupApiTests` | A location is created in its city and audited with it | geçti | 0.30 |
| SQL Server entegrasyon | `LookupApiTests` | A lookup is created trimmed listed and audited | geçti (3/3 durum) | 1.07 |
| SQL Server entegrasyon | `LookupApiTests` | A model is created under its brand and audited with it | geçti | 0.35 |
| SQL Server entegrasyon | `SearchMatchLimitTests` | The audit log finds the same records by asset code either way | geçti (2/2 durum) | 0.52 |
| SQL Server entegrasyon | `SignInRecordingTests` | Each sign in updates one administrator record and is audited | geçti | 0.55 |
| Tarayıcı (E2E) | `audit.spec.ts` | an edit made on the form can be followed in the audit log with its old and new values | geçti | 2.68 |
| Tarayıcı (E2E) | `audit.spec.ts` | the audit log filters by asset code and action on the server | geçti | 1.93 |

## Diğer konular

| Konu | Testler | Sonuç |
| --- | --- | --- |
| Uç nokta erişimi: yönlendirme tablosundaki her uç nokta ve yöntem için oturumsuz `401`, rolsüz `403`, CSRF'siz `400` | `EndpointAccessTests` | geçti |
| SQL/LIKE enjeksiyonu (14 girdi × 12 filtre), toplu atama | `InputSecurityTests` | geçti |
| HTTPS yönlendirmesi, HSTS | `TransportSecurityTests` | geçti |
| Depoda anahtar, parola ve sertifika doğrulamasını kapatan ayar yok | `RepositorySecretsTests` | geçti |
| Sabit sorgu sayısı (N+1 yok), 1.000 eşleşme sınırının iki yolu | `QueryCountTests`, `SearchMatchLimitTests` | geçti |
| Excel dosyası: başlıklar, tipler, satır sınırı, filtre ve sıralama | `OpenXmlSpreadsheetWriterTests`, `AssetExportTests`, `export.spec.ts` | geçti |
| Gösterge paneli ve rapor rakamları düz SQL ile aynı | `DashboardConsistencyTests`, `AssetSummaryReportTests`, `AssignmentReportTests` | geçti |

## Denenmeyenler

Bu rapordaki hiçbir test aşağıdakileri kanıtlamaz:

- Şirketin gerçek Active Directory'si (domain, grup SID'i, servis hesabı, DC sertifikası).
- Şirketin SQL Server'ı ve oradaki yetkiler; burada SQL Server 2022 container'ı kullanıldı.
- IIS, Windows Server, ASP.NET Core Hosting Bundle ve site bağlaması. 38. günde yayın klasörü Production'da Kestrel ile
  HTTPS üzerinden denendi: [`deploy/iis/README.md`](../deploy/iis/README.md#38-günde-denenenler).
- Excel dosyalarının Microsoft Excel'de açılması; dosyalar Open XML SDK ile okunarak doğrulandı.
- Chromium dışındaki tarayıcılar.
- Gerçek kullanıcı yükü ve birden fazla sunucuda SignalR.
- Yedekten dönüş ve Data Protection anahtarlarının kalıcılığı otomatik testte değil; 39. gün.

## Yeniden çalıştırma

```bash
dotnet test tests/EnterpriseInventory.UnitTests --logger "trx;LogFileName=unit.trx" --results-directory artifacts/tests
dotnet test tests/EnterpriseInventory.IntegrationTests --logger "trx;LogFileName=integration.trx" --results-directory artifacts/tests

cd src/EnterpriseInventory.Web
npx vitest run --reporter=default --reporter=json --outputFile.json=../../artifacts/tests/vitest.json
PLAYWRIGHT_JSON_OUTPUT_NAME=../../artifacts/tests/e2e.json npx playwright test --reporter=list,json
cd ../..

python3 scripts/test-report/summarize-results.py artifacts/tests/unit.trx artifacts/tests/integration.trx \
  artifacts/tests/e2e.json artifacts/tests/categories.md
```

SQL Server testleri için `EI_TEST_SQL_CONNECTION`, AD testleri için `EI_TEST_AD_*` değişkenleri gerekir
([README](../README.md#derleme-ve-test)); tanımlı değilse bu testler "skipped" görünür ve rapor geçerli olmaz.
E2E hazırlığı: [`e2e/README.md`](../src/EnterpriseInventory.Web/e2e/README.md).
