EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 1
KURUMSAL ENVANTER YÖNETİM SİSTEMİ
TEK PARÇA MASTER GELİŞTİRME PROMPTU + 40 GÜNLÜK UYGULAMA PLANI | Sürüm 1.1 | 9 Ekim 2026 | Entity
Framework Core ayrıntılandırıldı
YAPAY ZEKÂ KODLAMA ARACINA VERİLECEK MASTER PROMPT
Sen kıdemli yazılım mimarı, .NET backend mühendisi, React/TypeScript geliştiricisi, veritabanı mimarı,
güvenlik mühendisi, QA ve DevOps uzmanısın. Görevin aşağıdaki gereksinimlerle gerçek şirket içi kullanım için
güvenli, sürdürülebilir ve üretime uygun EnterpriseInventory adlı web uygulamasını geliştirmek. Yalnızca öneri
üretme: dosyaları oluştur, derle, test et ve doğrulanabilen sonuçları raporla. Yapamadığın adımları yapılmış
gösterme.
1. Kesin teknoloji ve dağıtım mimarisi
• Backend: C#, .NET 10 LTS ASP.NET Core Web API; Clean Architecture (Domain, Application, Infrastructure,
Api); FluentValidation, ProblemDetails, Serilog.
• Frontend: React + TypeScript + Vite; Material UI; TanStack Query; React Hook Form + Zod; React Router;
SignalR Client. Arayüz tamamen Türkçe, backend sınıf/metot/değişken/DB isimleri İngilizce.
• Veritabanı: mevcut Microsoft SQL Server; Entity Framework Core Code First, migration, transaction,
indeksler, RowVersion; gereksiz Generic Repository kullanma.
• Hosting: mevcut Windows Server ve IIS; yalnızca intranet; şirket içi DNS ve CA tarafından güvenilen HTTPS
sertifikası; aynı origin altında React, /api ve /hubs.
• Solution: EnterpriseInventory.Domain, .Application, .Infrastructure, .Api, .Web, .UnitTests, .IntegrationTests.
docs, scripts ve deploy klasörlerini oluştur.
1.1. Entity Framework Core - zorunlu veri erişimi gereksinimleri
• Microsoft SQL Server için Entity Framework Core kullan. Code First yaklaşımı zorunludur; tablo ve şema
değişiklikleri versiyonlanmış migration dosyaları üzerinden yönetilsin. EF Core, SqlServer provider, Design
paketi ve dotnet-ef aracının seçilen .NET sürümüyle uyumlu sürümlerini doğrula ve sabitle; çalıştırılmamış
komutları başarılı gösterme.
• Domain katmanı EF Core paketlerine bağımlı olmasın. Infrastructure/Persistence altında
ApplicationDbContext, Configurations, Migrations ve geliştirme verisi için Seed klasörlerini oluştur. Application
katmanında gerekli veri erişim sözleşmelerini tanımla; Infrastructure bunları DbContext/DbSet ile uygulasın.
Gereksiz Generic Repository ve ikinci bir Unit of Work katmanı ekleme; DbContext mevcut çalışma birimidir.
EF bağımlılığının katman sınırlarında nerede bulunduğunu README'de açıkla.
• ApplicationDbContext scoped olarak DI üzerinden sağlansın. Aynı DbContext üzerinde paralel sorgu
çalıştırma; background job ve uzun ömürlü servislerde ayrı scope veya IDbContextFactory kullan. Veritabanı
işlemlerini async/await, ToListAsync, SingleOrDefaultAsync ve SaveChangesAsync ile uygula;
CancellationToken aktar.
• Her entity için IEntityTypeConfiguration<T> ve Fluent API kullan. Tablo adları, PK/FK, zorunlu alanlar,
maksimum metin uzunlukları, ilişkiler, indeksler, check constraint ve delete davranışları açıkça tanımlansın.
AssetModel ile Brand ilişkisi ve cihazın seçilen model/marka tutarlılığı doğrulansın. Kontrolsüz cascade delete
ile zimmet veya audit geçmişi kaybolmasın.
• AssetCode için benzersiz indeks oluştur. SerialNumber boşluk/harf normalizasyonunu belirle; boş seri
numaralarını null olarak sakla ve şirket politikasına göre filtreli benzersiz indeks uygula. AssetAssignments
üzerinde ReturnedAt IS NULL koşullu AssetId benzersiz indeksiyle aynı cihaz için bir aktif zimmet kuralını SQL
Server seviyesinde de güvenceye al. Benzersizlik ihlallerini güvenli iş hatalarına dönüştür.
EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 2
• Assets.RowVersion alanını byte[] olarak modelle ve IsRowVersion() ile yapılandır. Güncelleme/arşivleme
isteğinde istemcinin okuduğu RowVersion zorunlu olsun; DTO'da Base64 olarak taşınsın. EF OriginalValue
istemcinin gönderdiği sürümle eşlensin. DbUpdateConcurrencyException için HTTP 409 ve Türkçe çakışma
mesajı döndür; istemci güncel kaydı tekrar yükleyebilsin. Silme/zimmet gibi kritik değişikliklerde de ilgili
durumun eşzamanlı değişmesine karşı koruma tasarla.
• Soft delete için HasQueryFilter uygula; arşivli kayıtları normal listelerden çıkar. IgnoreQueryFilters yalnızca
açıkça yetkilendirilmiş geçmiş/arşiv sorgularında kullanılabilsin. CreatedAt/UpdatedAt UTC olsun;
CreatedBy/UpdatedBy güvenilir oturum kimliğinden alınsın. AuditLogs iş verisi değişikliğiyle aynı transaction
içinde saklansın; audit kayıtlarına aynı audit mekanizmasını tekrar uygulayıp döngü oluşturma.
• Tek SaveChangesAsync ile atomik yapılabilen işlemlerde ek transaction zorunlu değildir. Çok adımlı zimmet,
iade ve ilişkili değişikliklerde açık transaction kullan. Retry stratejisi etkinse açık transaction işleminin
tamamını execution strategy içinde çalıştır. SignalR bildirimi yalnızca başarılı commit sonrasında yayımlansın;
bildirim hatası commit edilmiş veriyi geri alınmış gibi göstermesin.
• Salt okunur sorgularda AsNoTracking ve Select ile DTO projection kullan; entity nesnelerini doğrudan API
yanıtı yapma. Filtre/sıralama/sayfalama SQL üzerinde çalışsın; deterministik sıralama için Id gibi ikinci anahtar
ekle. Lazy loading varsayılan olarak kapalı olsun; N+1 sorgularını engelle. Include/AsSplitQuery yalnızca sorgu
ihtiyacına göre seçilsin; büyük veri üzerinde erken ToList kullanma. Parametreli sorgular kullan; kullanıcı
girdisini ham SQL metnine birleştirme.
• ConnectionStrings:DefaultConnection güvenli placeholder içersin; production şifreleri repoda tutulmasın.
Runtime SQL hesabına şema değiştirme yetkisi verme. Production başlangıcında otomatik Database.Migrate
veya EnsureCreated çalıştırma; migrationları onaylı dağıtım adımıyla uygula. Idempotent SQL migration
scripti, yedekleme ve veri kaybı riski değerlendirilmiş geri dönüş planı teslim et. Development/test seed
işlemleri idempotent olsun; production'a sahte AD kullanıcıları veya örnek parolalar ekleme.
• README'de solution'ın gerçek proje yollarına göre dotnet ef migrations add InitialCreate, dotnet ef database
update ve dotnet ef migrations script --idempotent komutlarını --project ve --startup-project parametreleriyle
ver. Gerekirse design-time DbContext factory oluştur. Migration dosyaları, SQL scriptleri ve model snapshot
Git'e eklensin.
• EF Core integration testleri gerçek SQL Server test veritabanı veya SQL Server container üzerinde çalışsın.
EF InMemory provider SQL Server davranışının kanıtı sayılmasın. FK, filtreli benzersiz indeks, çift aktif zimmet,
soft delete, rollback, audit atomikliği ve iki ayrı DbContext ile RowVersion çakışması test edilsin. Test altyapısı
yoksa engeli raporla; bu testleri geçti diye yazma.
2. Active Directory Login - değiştirilemez güvenlik kuralları
• Kullanıcı adı ve şifre alanları olan Türkçe login ekranı zorunludur. On-prem Active Directory üzerinden LDAPS
(TCP 636 veya ortamın güvenli yapılandırması) ile doğrula; TLS sertifika zincirini ve sunucu adını doğrula,
doğrulamayı asla devre dışı bırakma.
• Yalnızca Bim_Envanter adlı AD güvenlik grubunun üyeleri uygulamaya girebilir; üyelik güvenilir grup SID
üzerinden doğrulanmalı. İç içe grup üyeliği politikası açıkça belirlenmeli. Başarılı kullanıcıya Administrator rolü
ver.
• Üye olmayan kullanıcı login olamaz; tüm korumalı API uçları ve SignalR Hub erişimi reddedilir. Hesabı pasif
kullanıcılar reddedilir; AD erişilemiyorsa yeni girişler fail-closed olur.
• Şifreyi veritabanında, loglarda, tarayıcı localStorage/sessionStorage alanlarında saklama. Girişte HTTPS
kullan; HttpOnly, Secure, uygun SameSite cookie ve sunucu taraflı oturum; CSRF koruması, rate limiting,
logout, timeout, oturum iptali ve düzenli AD grup yetkisi tekrar kontrolü ekle.
• AD domain adı henüz bilinmiyor. Server FQDN, BaseDn, Domain, grup SID, servis hesabı ve connection string
için güvenli konfigürasyon placeholder kullan. Secret değerleri repoya ekleme.
EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 3
• Demirbaş zimmetlenecek çalışanlar AD içinden aranabilir; bu çalışanların Bim_Envanter üyesi olması
gerekmez. Giriş yapan admin kullanıcılar ile zimmetlenen çalışanları veri modelinde ayrı tut.
3. Veri modeli ve iş kuralları
• Assets: Id, AssetCode, ComputerName, BrandId, ModelId, SerialNumber, AssetType, Status, Description,
CityId, DepartmentId, LocationId, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted, RowVersion.
• AssetAssignments: Id, AssetId, EmployeeId, AssignmentDescription, AssignedAt, ReturnedAt, AssignedBy,
Notes. Bir cihaz için aynı anda en fazla bir aktif zimmet; iade geçmişi silinmez.
• Employees (AD ObjectGuid, SamAccountName, DisplayName, Department vb. gerekli profil alanları), Brands,
AssetModels, Cities, Departments, Locations, AuditLogs ve gerektiğinde kimlik eşleme tabloları.
• AssetCode benzersiz; SerialNumber benzersizliği boş değerler ve şirket politikasına uygun filtreli indeksle
tasarla. FK, check constraint, audit alanları, soft delete ve EF Core RowVersion uygula.
• Kritik iş kurallarını transaction içinde uygula; audit kaydıyla birlikte commit et; başarılı commit sonrası
SignalR bildirimi gönder. Çoklu sunucu ihtimali doğarsa güvenilir event/outbox stratejisi değerlendir.
4. Kullanıcı ekranları ve fonksiyonlar
• Modern kurumsal tasarım: lacivert (#142D4E), mavi (#2563EB), beyaz, açık gri; Material UI; responsive
Sidebar, Header, tema, loading/empty/error durumları.
• Login: kullanıcı adı, şifre, Giriş Yap, Türkçe güvenli hata mesajları. Dashboard: toplam, zimmetli, boşta,
arızalı demirbaş; şehir/departman dağılımı; son işlemler.
• Envanter tablosu ana sütunları: Kullanıcı Adı, Bilgisayar Adı, Marka, Model, Seri No, Zimmet Tanımı,
Lokasyon/Şehir, Lokasyon/Departman. Ayrıca Demirbaş Kodu, Durum, İşlemler.
• Demirbaş ekle/düzenle/detay/arşivle; sunucu taraflı filtre, arama, sıralama, sayfalama, kolon görünürlüğü,
Excel dışa aktarma.
• Zimmet oluştur/iade et/geçmişi görüntüle; AD çalışan arama; lokasyon, şehir, departman ve marka/model
yönetimi; raporlar; denetim geçmişi.
5. API, gerçek zamanlılık ve güvenlik
• API: /api/auth/login, /api/auth/me, /api/auth/logout; /api/assets GET/POST, /api/assets/{id} GET/PUT/DELETE;
/api/assets/{id}/assignments, /returns, /history; /api/dashboard/statistics; /api/brands, /models, /cities,
/departments, /audit-logs.
• SignalR: AssetCreated, AssetUpdated, AssetArchived, AssetAssigned, AssetReturned,
AssetLocationChanged. Olaylar yalnızca bildirim taşısın; React TanStack Query invalidate/refetch ile
sunucudan gerçeği alsın. Reconnect sonrası tam yeniden eşitle.
• Aynı kaydı iki kişi değiştirirse RowVersion uyuşmazlığında HTTP 409 ve Türkçe çakışma uyarısı; sessiz veri
ezme olmasın.
• ASP.NET Core authorization her endpoint ve hub için zorunlu. Input validation, güvenli CORS, CSP/XSS
önlemleri, CSRF, HTTPS, rate limiting, secret management, en az yetkili SQL hesabı, güvenli loglama ve audit
uygula.
• Audit: entity, kayıt kimliği, işlem, eski/yeni değerler, yapan kullanıcı, zaman, correlation ID; parola, token ve
hassas veri loglama.
EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 4
6. Kurulum ve konfigürasyon teslimleri
• appsettings.json, appsettings.Production.json (gizli değer içermeyen örnek), React .env.example, IIS
web.config, SQL migration komutları, PowerShell ön kontrol betikleri, README, güvenlik kontrol listesi,
deployment/rollback yönergeleri üret.
• Mevcut IIS üzerinde .NET Hosting Bundle, app pool No Managed Code, site binding/HTTPS, Data Protection
anahtarlarının kalıcı güvenli depolanması, log klasörleri ve izinleri yapılandır.
• SQL Server mevcut olduğundan yeni SQL sunucusu kurma. Migration yetkisiyle runtime yetkisini ayır;
backup/restore ve rollback senaryolarını test et.
• Domain/AD bilgileri belli değilse yalnızca development ortamına özel sahte AD adapteri kullan; production
için kesinlikle engelle. Gerçek AD testleri yapılmadan entegrasyon tamamlandı deme.
7. Geliştirme disiplinleri ve kabul şartları
• Her gün 4 saat: 30 dk planlama, 120 dk geliştirme, 60 dk test/hata düzeltme, 30 dk inceleme/Git. Gün
sonunda derleme, ilgili testler, değişiklik özeti ve sonraki gün görevlerini raporla.
• Unit, integration, authorization, LDAPS, concurrency, SignalR, React ve end-to-end testleri; hatalar
çözülmeden tamamlandı deme.
• Önce güvenli temel ve login; ardından envanter, zimmet, canlı güncelleme, raporlama, performans ve IIS
yayını. Üretim ortamında anonim yazma endpointi bırakma.
• Çalışan ilk sürüm hedefi 40 çalışma günü / 160 saat; bu tahmindir, AD ve altyapı engelleri süreyi uzatabilir.
8. KIRK GÜNLÜK GÖREV VE KABUL KRİTERLERİ
Hafta 1 - Mimari ve SQL
Gün 1: Solution ve Clean Architecture. Kabul: Solution build geçer; referans yönleri doğrudur.
Gün 2: React, Vite ve Material UI. Kabul: Türkçe ilk arayüz açılır.
Gün 3: Domain entity, ilişkiler ve EF Core Fluent API eşlemeleri. Kabul: Domain EF bağımlılığı taşımaz; PK/FK,
alan uzunlukları ve delete davranışları tanımlıdır.
Gün 4: ApplicationDbContext, SQL Server provider, ilk migration ve RowVersion. Kabul: Migration SQL Server
test DB üzerinde uygulanır; indeksler, model snapshot ve SQL scripti doğrulanır.
Gün 5: API altyapısı ve health endpoint. Kabul: API build ve health başarılı.
Hafta 2 - AD ve Login
Gün 6: LDAPS config ve TLS kontrolü. Kabul: Sertifika doğrulama testi geçer veya ortam engeli belgelenir.
Gün 7: AD kimlik doğrulama. Kabul: Doğru/yanlış parola senaryoları test edilir.
Gün 8: Bim_Envanter SID kontrolü. Kabul: Grup dışı kullanıcı reddedilir.
Gün 9: Cookie, CSRF, logout, timeout. Kabul: Oturum güvenlik testleri geçer.
Gün 10: React login ve korumalı sayfa. Kabul: Yetkili Dashboard açar; yetkisiz erişemez.
Hafta 3 - Envanter API
Gün 11: EF Core async liste/detay sorguları, AsNoTracking ve DTO projection. Kabul: Filtre ve deterministik
pagination SQL üzerinde çalışır; testler geçer.
EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 5
Gün 12: Asset ekleme ve validation. Kabul: Geçerli kayıt oluşur; hatalı reddedilir.
Gün 13: EF Core güncelleme ve istemci RowVersion kontrolü. Kabul: İki ayrı DbContext ile çakışma testinde
409 alınır; veri sessizce ezilmez.
Gün 14: EF global query filter, soft delete ve atomik audit. Kabul: Arşiv listeden çıkar; geçmiş korunur;
başarısız işlemde audit ve değişiklik birlikte geri alınır.
Gün 15: Filtreleme/sıralama. Kabul: Filtre testleri geçer.
Hafta 4 - Envanter UI
Gün 16: Dashboard KPI. Kabul: Gerçek DB istatistikleri görüntülenir.
Gün 17: Data Grid. Kabul: Tablo, sıralama, sayfalama çalışır.
Gün 18: Arama ve filtreler. Kabul: API ile tutarlı sonuçlar döner.
Gün 19: Ekleme/düzenleme formu. Kabul: Kayıt formdan kaydedilir.
Gün 20: Detay ve responsive UI. Kabul: Ekranlar ve hata durumları test edilir.
Hafta 5 - Zimmet
Gün 21: AD çalışan arama. Kabul: Yetkisiz AD çalışanı zimmet için seçilebilir.
Gün 22: Transaction içinde zimmet oluşturma ve filtreli benzersiz indeks. Kabul: Eşzamanlı iki istekte yalnızca
bir aktif zimmet oluşur.
Gün 23: Zimmet iadesi. Kabul: İade ve geçmiş doğru kaydedilir.
Gün 24: Zimmet UI. Kabul: Zimmet ver/iade et çalışır.
Gün 25: Lokasyon/departman. Kabul: Konum değişiklikleri doğrulanır.
Hafta 6 - SignalR
Gün 26: Yetkili Hub. Kabul: Yetkisiz bağlantı reddedilir.
Gün 27: Asset eventleri. Kabul: Commit sonrası bildirim gelir.
Gün 28: React Query senkronizasyon. Kabul: Diğer ekranda veri yenilenir.
Gün 29: Reconnect. Kabul: Yeniden bağlanınca veriler eşitlenir.
Gün 30: Eşzamanlılık testleri. Kabul: Veri kaybı ve çift zimmet olmaz.
Hafta 7 - Raporlama
Gün 31: Audit detayları. Kabul: Eski/yeni değerler izlenebilir.
Gün 32: Excel export. Kabul: Filtreye uygun Excel çıkar.
Gün 33: Dashboard raporları. Kabul: Rakamlar DB ile tutarlı.
Gün 34: Rapor ekranı. Kabul: Türkçe filtre ve export çalışır.
Gün 35: EF Core sorgu performansı, indeksler ve N+1 kontrolü. Kabul: Temsili yükte SQL/süre/sorgu sayısı
ölçülür; iyileştirmeler belgelenir.
EnterpriseInventory | Master Prompt | Sürüm 1.1 Sayfa 6
Hafta 8 - Güvenlik ve yayın
Gün 36: Güvenlik testleri. Kabul: Kritik erişim kontrolleri geçer.
Gün 37: Unit, SQL Server EF Core integration ve E2E testleri. Kabul: Constraint, concurrency, rollback ve audit
testleri gerçek sonuçlarla raporlanır.
Gün 38: IIS hazırlığı. Kabul: Test URL HTTPS ile açılır.
Gün 39: Backup, migration dağıtım scripti, rollback ve keys. Kabul: Restore ve key kalıcılığı doğrulanır;
runtime/migration SQL yetkileri ayrıdır.
Gün 40: Kabul ve teslim. Kabul: Kritik akışlar ve dokümanlar tamam.
9. İlk yürütme komutu
Şimdi 1. günden başla. Önce mevcut repository ve araç sürümlerini incele; solution, doğru katman
bağımlılıkları, React iskeleti, güvenli konfigürasyon placeholderları, README ve test projelerini oluştur.
Build/test çalıştır ve gerçek sonuçları raporla. Domain adı, AD SID, şifre veya SQL bağlantısı uydurma. Gerekli
erişimler eksikse açıkça belirt ve diğer güvenli geliştirme işlerine devam et.
