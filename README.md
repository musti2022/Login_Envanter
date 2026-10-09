# EnterpriseInventory — Kurumsal Envanter Yönetim Sistemi

Şirket içi (intranet) demirbaş ve zimmet takibi için web uygulaması. Giriş, on-prem Active Directory
üzerinden LDAPS ile yapılır ve yalnızca `Bim_Envanter` güvenlik grubunun üyeleri uygulamaya girebilir.
Proje gereksinimleri ve 40 günlük plan: [`proje_talimatlari.md`](proje_talimatlari.md).

> **Durum:** 9. gün — solution iskeleti, Türkçe arayüz kabuğu, domain modeli, SQL Server şeması (EF Core migration,
> RowVersion, kısıtlar), API altyapısı (health endpoint'leri, varsayılan olarak kapalı yetkilendirme, hata yanıtları,
> güvenlik başlıkları, rate limiting), Active Directory LDAPS bağlantısı (sıkı TLS sertifika doğrulaması), giriş API'si
> (AD parola doğrulaması, `Bim_Envanter` grup SID kontrolü, giriş audit kaydı) ve oturum güvenliği (sunucu taraflı
> oturum, CSRF koruması, çıkış, boşta kalma ve mutlak süre, açık oturumların AD'de düzenli yeniden kontrolü, kalıcı
> Data Protection anahtarları). AD entegrasyonu Samba test domain'i ile test edildi; şirketin gerçek AD'si ile henüz
> denenmedi. Giriş ekranı, envanter ve zimmet ekranları henüz yok.

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
CSRF, çıkış ve zaman aşımı kuralları için [`docs/session-security.md`](docs/session-security.md).

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
```

SQL Server testleri `EI_TEST_SQL_CONNECTION` tanımlı değilse atlanır (skipped). Bir test sunucusunda geçici bir
veritabanı oluşturup silerler; ayrıntı için [`docs/database.md`](docs/database.md#testler).

Active Directory testleri `EI_TEST_AD_SERVER` tanımlı değilse atlanır. Gerçek bir LDAPS sunucusuyla denemek için
Samba ile geçici bir test domain'i kurulabilir: [`scripts/test-ad`](scripts/test-ad/README.md). Ayrıntı:
[`docs/active-directory.md`](docs/active-directory.md).

## Geliştirme ortamında çalıştırma

```bash
dotnet run --project src/EnterpriseInventory.Api --launch-profile https   # https://localhost:7261

cd src/EnterpriseInventory.Web
cp .env.example .env.local
npm run dev                                                               # /api ve /hubs API'ye yönlendirilir
```

API ayaktaysa `https://localhost:7261/api/health/live` `Healthy` döner; `.../api/health/ready` veritabanı bağlantısı ve
migration durumunu da kontrol eder (bkz. [`docs/api.md`](docs/api.md#health-endpointleri)).

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
