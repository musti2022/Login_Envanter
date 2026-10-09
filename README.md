# EnterpriseInventory — Kurumsal Envanter Yönetim Sistemi

Şirket içi (intranet) demirbaş ve zimmet takibi için web uygulaması. Giriş, on-prem Active Directory
üzerinden LDAPS ile yapılır ve yalnızca `Bim_Envanter` güvenlik grubunun üyeleri uygulamaya girebilir.
Proje gereksinimleri ve 40 günlük plan: [`proje_talimatlari.md`](proje_talimatlari.md).

> **Durum:** 24. gün — solution iskeleti, Türkçe arayüz kabuğu,
> domain modeli, SQL Server şeması (EF Core migration, RowVersion, kısıtlar, soft delete sorgu filtresi, idempotent
> yayın betiği, yedek ve geri dönüş planı, geliştirme seed'i), API altyapısı (health endpoint'leri, varsayılan olarak kapalı yetkilendirme, hata yanıtları,
> güvenlik başlıkları, rate limiting), Active Directory LDAPS bağlantısı (sıkı TLS sertifika doğrulaması), giriş API'si
> (AD parola doğrulaması, `Bim_Envanter` grup SID kontrolü, giriş audit kaydı), oturum güvenliği (sunucu taraflı
> oturum, CSRF koruması, çıkış, boşta kalma ve mutlak süre, açık oturumların AD'de düzenli yeniden kontrolü, kalıcı
> Data Protection anahtarları), Türkçe giriş ekranı ile korumalı sayfalar, demirbaş API'si (listeleme, arama,
> filtre, sıralama, sayfalama, detay, ekleme, RowVersion ile güncelleme ve `409` çakışma uyarısı, arşivleme, audit
> geçmişi), tanım listeleri API'si (marka, model, şehir, lokasyon, departman) ve envanter ekranları (gösterge paneli,
> tablo, arama ve filtreler, ekleme/düzenleme formu, detay, geçmiş, arşivleme, telefon görünümü), AD'de çalışan
> araması, zimmet API'si (tek transaction'da zimmet verme ve iade, filtreli benzersiz indeksle tek aktif zimmet,
> zimmet geçmişi) ve detay sayfasında zimmet ver/iade al ekranları. AD entegrasyonu Samba test domain'i ile test
> edildi; şirketin gerçek AD'si ile henüz denenmedi. Tanım yönetimi (ad değiştirme, pasifleştirme) ve SignalR henüz
> yok.

## Teknolojiler

| Katman | Teknoloji |
| --- | --- |
| Backend | .NET 10, ASP.NET Core Web API, Clean Architecture, FluentValidation, ProblemDetails, Serilog |
| Frontend | React, TypeScript, Vite, Material UI, TanStack Query, React Hook Form + Zod, React Router, SignalR |
| Veritabanı | Mevcut Microsoft SQL Server, EF Core Code First, migration, RowVersion |
| Barındırma | Mevcut Windows Server + IIS, yalnızca intranet, HTTPS |

## Klasör yapısı

```
EnterpriseInventory.slnx
src/
  EnterpriseInventory.Domain/          Entity'ler ve iş kuralları (bağımlılık yok)
  EnterpriseInventory.Application/     Kullanım senaryoları, doğrulama
  EnterpriseInventory.Infrastructure/  SQL Server, Active Directory adaptörleri
  EnterpriseInventory.Api/             ASP.NET Core Web API ve SignalR
  EnterpriseInventory.Web/             React + Vite arayüzü
tests/
  EnterpriseInventory.UnitTests/       Birim ve mimari testleri
  EnterpriseInventory.IntegrationTests/ API entegrasyon testleri
docs/                                  Mimari ve teknik dokümanlar
scripts/                               Yardımcı betikler
deploy/                                IIS yayın dosyaları
```

Katman bağımlılık kuralları için bkz. [`docs/architecture.md`](docs/architecture.md); veritabanı tasarımı, migration
komutları ve SQL hesap yetkileri için [`docs/database.md`](docs/database.md); health endpoint'leri, yetkilendirme, hata
yanıtları, güvenlik başlıkları ve giriş API'si için [`docs/api.md`](docs/api.md); Active Directory LDAPS bağlantısı,
sertifika doğrulaması, giriş akışı ve grup yetkisi için [`docs/active-directory.md`](docs/active-directory.md); oturum,
CSRF, çıkış ve zaman aşımı kuralları için [`docs/session-security.md`](docs/session-security.md); giriş ekranı ve
korumalı sayfalar için [`docs/web-auth.md`](docs/web-auth.md); demirbaş uç noktaları, arama ve filtreler,
RowVersion çakışması, arşivleme ve audit için [`docs/assets-api.md`](docs/assets-api.md); marka, model, şehir,
lokasyon ve departman listeleri için [`docs/lookups-api.md`](docs/lookups-api.md); çalışan araması, zimmet ve iade
için [`docs/assignments-api.md`](docs/assignments-api.md); gösterge paneli, envanter tablosu,
filtreler, form ve detay ekranları için [`docs/inventory-ui.md`](docs/inventory-ui.md).

## Gereksinimler

- .NET SDK 10.0.100 veya üstü (`global.json`)
- Node.js 22 veya üstü, npm
- SQL Server (geliştirme veritabanı ve SQL testleri için; SQL Server 2022 ile denendi)

## Derleme ve test

```bash
# Backend
dotnet tool restore
dotnet build EnterpriseInventory.slnx
dotnet test EnterpriseInventory.slnx

# Frontend
cd src/EnterpriseInventory.Web
npm ci
npm run build
npm test
npm run lint
npm run test:e2e   # tarayıcı testleri; hazırlık: src/EnterpriseInventory.Web/e2e/README.md
```

SQL Server testleri `EI_TEST_SQL_CONNECTION` tanımlı değilse atlanır (skipped). Bir test sunucusunda geçici bir
veritabanı oluşturup silerler; ayrıntı için [`docs/database.md`](docs/database.md#testler).

Active Directory testleri `EI_TEST_AD_SERVER` tanımlı değilse atlanır. Gerçek bir LDAPS sunucusuyla denemek için
Samba ile geçici bir test domain'i kurulabilir: [`scripts/test-ad`](scripts/test-ad/README.md). Ayrıntı:
[`docs/active-directory.md`](docs/active-directory.md).

## Veritabanı: EF Core

Veri erişimi Entity Framework Core 10.0.12 (Code First, SQL Server provider) ile yapılır. Paket ve araç sürümleri
sabittir: `Microsoft.EntityFrameworkCore.SqlServer` ve `.Design` 10.0.12
([`EnterpriseInventory.Infrastructure.csproj`](src/EnterpriseInventory.Infrastructure/EnterpriseInventory.Infrastructure.csproj)),
`dotnet-ef` 10.0.12 ([`dotnet-tools.json`](dotnet-tools.json)).

**EF bağımlılığı katmanlarda nerede:**

| Katman | EF Core | İçerik |
| --- | --- | --- |
| Domain | Yok (paket referansı yok) | Entity'ler ve iş kuralları; `RowVersion` yalnızca `byte[]` |
| Application | Yok | Veri erişim sözleşmeleri (`IAssetStore`, `ILookupStore`, `IDashboardStore`, `IUserSessionService`) ve DTO'lar; servisler bunları çağırır |
| Infrastructure | **Tek yer** | `Persistence/ApplicationDbContext`, `Configurations` (Fluent API), `Migrations`, `Seed`, interceptor'lar; sözleşmeleri `DbContext`/`DbSet` ile uygulayan `*Store` sınıfları |
| Api | Doğrudan kullanmaz | `AddInfrastructure` ile DI'a kaydeder; endpoint'ler Application servislerini çağırır, `DbContext` görmez |

Generic repository veya ikinci bir Unit of Work yoktur: `ApplicationDbContext` (scoped) çalışma birimidir.
`LayerDependencyTests` Domain ve Application'ın EF'e bağlanmadığını, `ModelConfigurationTests` tablo, anahtar, uzunluk,
ilişki ve silme davranışlarının açıkça tanımlandığını denetler.

**Komutlar** (depo kökünden; `--startup-project` olarak Infrastructure verilir, çünkü design-time factory oradadır ve
bağlantıyı `ConnectionStrings__Migrations` değişkeninden okur; Api'ye Design paketi eklenmez):

```bash
dotnet tool restore

# İlk migration böyle oluşturuldu; model değişince yeni bir adla aynı komut
dotnet ef migrations add InitialCreate --project src/EnterpriseInventory.Infrastructure \
  --startup-project src/EnterpriseInventory.Infrastructure --output-dir Persistence/Migrations

# Geliştirme veritabanına uygulama (şema değiştirme yetkili migration hesabıyla)
export ConnectionStrings__Migrations="<migration hesabının bağlantı dizesi>"
dotnet ef database update --project src/EnterpriseInventory.Infrastructure \
  --startup-project src/EnterpriseInventory.Infrastructure

# Yayın betiği (idempotent, Git'te): deploy/sql/migrate-idempotent.sql
dotnet ef migrations script --idempotent --project src/EnterpriseInventory.Infrastructure \
  --startup-project src/EnterpriseInventory.Infrastructure -o deploy/sql/migrate-idempotent.sql

# Geliştirme veritabanına örnek tanımlar (yalnızca Development; tekrar çalıştırılabilir)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/EnterpriseInventory.Api -- seed-development-data
```

Bu dört komut 20. gün denetiminde gerçekten çalıştırıldı (`migrations add InitialCreate` migration klasörü silinmiş bir
kopyada). Uygulama başlarken migration çalıştırmaz; üretimde betiği DBA onaylı yayın adımında uygular. Yedek ve geri
dönüş planı: [`deploy/database-rollback.md`](deploy/database-rollback.md); tasarım, kısıtlar ve testler:
[`docs/database.md`](docs/database.md).

## Geliştirme ortamında çalıştırma

```bash
dotnet run --project src/EnterpriseInventory.Api --launch-profile https   # https://localhost:7261

cd src/EnterpriseInventory.Web
cp .env.example .env.local
npm run dev                                                               # /api ve /hubs API'ye yönlendirilir
```

API ayaktaysa `https://localhost:7261/api/health/live` `Healthy` döner; `.../api/health/ready` veritabanı bağlantısı ve
migration durumunu da kontrol eder (bkz. [`docs/api.md`](docs/api.md#health-endpointleri)).

Giriş ekranı `http://localhost:5173/giris` adresindedir. Geliştirmede giriş için sahte dizin kullanıcıları
`user-secrets` ile tanımlanır ([ayrıntı](docs/active-directory.md#geliştirme-ortamı-sahte-dizin)) ve
`ConnectionStrings:DefaultConnection` veritabanında migration'lar uygulanmış olmalıdır.

Vite proxy'si API'nin HTTPS sertifikasını doğrular (`secure: true`); doğrulama kapatılmaz. Node.js işletim
sisteminin sertifika deposunu varsayılan olarak kullanmadığı için ASP.NET Core geliştirme sertifikasını
Node'a ayrıca tanıtın:

```bash
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$HOME/.aspnet/https/aspnet-dev-cert.pem" --format Pem   # yalnızca açık sertifika
export NODE_EXTRA_CA_CERTS="$HOME/.aspnet/https/aspnet-dev-cert.pem"
# Windows PowerShell: $env:NODE_EXTRA_CA_CERTS = "$HOME\.aspnet\https\aspnet-dev-cert.pem"
```

Proxy hedefi `localhost` dışında bir adres olacaksa sertifika adı doğrulaması için `vite.config.ts` içindeki
`changeOrigin` ayarı gözden geçirilmelidir.

## Konfigürasyon ve gizli değerler

`appsettings.json` içindeki Active Directory ve SQL Server alanları **boş placeholder** olarak bırakılmıştır.
AD domain adı, sunucu FQDN, BaseDn, `Bim_Envanter` grup SID'i, servis hesabı ve connection string henüz
belli değildir ve **repoya hiçbir gizli değer eklenmez**.

| Anahtar | Açıklama |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server bağlantısı (en az yetkili runtime hesabı) |
| `ActiveDirectory:Mode` | `Ldap`; `Fake` (sahte dizin) yalnızca Development'ta kabul edilir |
| `ActiveDirectory:FakeUsers` | Yalnızca Development: sahte dizinin kullanıcıları, yalnızca `user-secrets` ile ([ayrıntı](docs/active-directory.md#geliştirme-ortamı-sahte-dizin)) |
| `ActiveDirectory:Domain` | AD domain'inin DNS adı |
| `ActiveDirectory:ServerFqdn` | LDAPS sunucusunun FQDN'i (sertifikadaki adla aynı olmalı; IP kabul edilmez) |
| `ActiveDirectory:Port` / `UseLdaps` | Varsayılan 636 / `true`; düz LDAP ile uygulama başlamaz, sertifika doğrulaması kapatılamaz |
| `ActiveDirectory:BaseDn` | Arama kökü, ör. `DC=ornek,DC=local` (domain'in içinde olmalı) |
| `ActiveDirectory:AllowedGroupSid` | `Bim_Envanter` grubunun SID'i (yetki kontrolü isim değil SID üzerinden yapılır) |
| `ActiveDirectory:NestedGroupPolicy` | İç içe grup politikası; açıkça yazılmalı, örnek ayarlarda `DirectMembershipOnly` |
| `ActiveDirectory:ServiceAccountUserName` / `ServiceAccountPassword` | Zorunlu; açık oturumların yetki tekrar kontrolü ve çalışan araması için yalnızca okuma yetkili servis hesabı |
| `ActiveDirectory:TrustedCaCertificatePath` | İsteğe bağlı; doluysa DC sertifikası yalnızca bu CA'ya zincirlenmeli |
| `ActiveDirectory:CheckCertificateRevocation` | Sertifika iptal kontrolü; varsayılan `true` |
| `RateLimiting:PermitLimit` / `WindowSeconds` | Kullanıcı başına istek limiti; varsayılan 60 saniyede 300 |
| `RateLimiting:LoginPermitLimit` / `LoginWindowSeconds` | IP başına giriş denemesi limiti; varsayılan 60 saniyede 10 |
| `Session:IdleTimeoutMinutes` / `AbsoluteTimeoutHours` | Oturum boşta kalma ve mutlak süresi; varsayılan 20 dakika / 8 saat |
| `Session:AccessRecheckMinutes` / `DirectoryOutageGraceMinutes` | AD yetki tekrar kontrolü aralığı ve AD kesintisinde oturumun ek süresi; varsayılan 5 / 15 dakika |
| `DataProtection:KeysDirectory` | Oturum çerezi anahtarlarının klasörü; Development dışında zorunlu, tam yol ([ayrıntı](docs/session-security.md#data-protection-anahtarları)) |

AD ayarları uygulama başlarken denetlenir; eksik, hatalı veya `CHANGE-ME` içeren değerlerle uygulama başlamaz
(bkz. [`docs/active-directory.md`](docs/active-directory.md#ayarlar-ve-başlangıç-denetimi)).

Değerler şu yollarla verilir:

- **Geliştirme:** `dotnet user-secrets` (API projesinde `UserSecretsId` tanımlı)
  ```bash
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<değer>" --project src/EnterpriseInventory.Api
  ```
- **Üretim (IIS):** Ortam değişkenleri, ör. `ConnectionStrings__DefaultConnection`,
  `ActiveDirectory__ServiceAccountPassword`. `appsettings.Production.json` gizli değer içermez.

Frontend tarafında `.env.example` yalnızca geliştirme proxy adresini içerir; `VITE_` değişkenleri tarayıcıya
gömüldüğü için oraya gizli değer yazılmaz.
