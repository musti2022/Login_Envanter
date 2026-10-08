# EnterpriseInventory — Kurumsal Envanter Yönetim Sistemi

Şirket içi (intranet) demirbaş ve zimmet takibi için web uygulaması. Giriş, on-prem Active Directory
üzerinden LDAPS ile yapılır ve yalnızca `Bim_Envanter` güvenlik grubunun üyeleri uygulamaya girebilir.
Proje gereksinimleri ve 40 günlük plan: [`proje_talimatlari.md`](proje_talimatlari.md).

> **Durum:** 1. gün — solution iskeleti. Login, envanter ve zimmet modülleri henüz yok.

## Teknolojiler

| Katman | Teknoloji |
| --- | --- |
| Backend | .NET 10, ASP.NET Core Web API, Clean Architecture, FluentValidation, ProblemDetails, Serilog |
| Frontend | React, TypeScript, Vite, Material UI, TanStack Query, React Hook Form + Zod, React Router, SignalR |
| Veritabanı | Mevcut Microsoft SQL Server, EF Core Code First (sonraki aşamalarda) |
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

Katman bağımlılık kuralları için bkz. [`docs/architecture.md`](docs/architecture.md).

## Gereksinimler

- .NET SDK 10.0.100 veya üstü (`global.json`)
- Node.js 22 veya üstü, npm

## Derleme ve test

```bash
# Backend
dotnet build EnterpriseInventory.slnx
dotnet test EnterpriseInventory.slnx

# Frontend
cd src/EnterpriseInventory.Web
npm ci
npm run build
npm test
npm run lint
```

## Geliştirme ortamında çalıştırma

```bash
dotnet run --project src/EnterpriseInventory.Api --launch-profile https   # https://localhost:7261

cd src/EnterpriseInventory.Web
cp .env.example .env.local
npm run dev                                                               # /api ve /hubs API'ye yönlendirilir
```

## Konfigürasyon ve gizli değerler

`appsettings.json` içindeki Active Directory ve SQL Server alanları **boş placeholder** olarak bırakılmıştır.
AD domain adı, sunucu FQDN, BaseDn, `Bim_Envanter` grup SID'i, servis hesabı ve connection string henüz
belli değildir ve **repoya hiçbir gizli değer eklenmez**.

| Anahtar | Açıklama |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server bağlantısı (en az yetkili runtime hesabı) |
| `ActiveDirectory:Domain` | AD domain adı |
| `ActiveDirectory:ServerFqdn` | LDAPS sunucusunun FQDN'i (sertifikadaki adla aynı olmalı) |
| `ActiveDirectory:Port` / `UseLdaps` | Varsayılan 636 / `true`; sertifika doğrulaması kapatılamaz |
| `ActiveDirectory:BaseDn` | Arama kökü, ör. `DC=ornek,DC=local` |
| `ActiveDirectory:AllowedGroupSid` | `Bim_Envanter` grubunun SID'i (yetki kontrolü isim değil SID üzerinden yapılır) |
| `ActiveDirectory:NestedGroupPolicy` | İç içe grup politikası; varsayılan `DirectMembershipOnly` |
| `ActiveDirectory:ServiceAccountUserName` / `ServiceAccountPassword` | Çalışan araması için servis hesabı |

Değerler şu yollarla verilir:

- **Geliştirme:** `dotnet user-secrets` (API projesinde `UserSecretsId` tanımlı)
  ```bash
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<değer>" --project src/EnterpriseInventory.Api
  ```
- **Üretim (IIS):** Ortam değişkenleri, ör. `ConnectionStrings__DefaultConnection`,
  `ActiveDirectory__ServiceAccountPassword`. `appsettings.Production.json` gizli değer içermez.

Frontend tarafında `.env.example` yalnızca geliştirme proxy adresini içerir; `VITE_` değişkenleri tarayıcıya
gömüldüğü için oraya gizli değer yazılmaz.
