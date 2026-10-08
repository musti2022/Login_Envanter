# Mimari

## Katmanlar ve bağımlılık yönü

```
EnterpriseInventory.Domain          (hiçbir projeye ve pakete bağımlı değil)
        ▲
EnterpriseInventory.Application     → Domain
        ▲
EnterpriseInventory.Infrastructure  → Application
        ▲
EnterpriseInventory.Api             → Application, Infrastructure
```

- **Domain:** Entity'ler, değer nesneleri ve iş kuralları. Dış bağımlılık yok.
- **Application:** Kullanım senaryoları, arayüzler (port'lar), FluentValidation doğrulayıcıları.
- **Infrastructure:** EF Core / SQL Server, Active Directory (LDAPS) ve diğer dış sistem adaptörleri.
- **Api:** ASP.NET Core Web API, SignalR hub'ları, kimlik doğrulama/yetkilendirme, ProblemDetails, Serilog.
- **Web:** React + TypeScript + Vite arayüzü. Üretimde Api ile aynı origin altında (`/`, `/api`, `/hubs`) yayınlanır.

Bu kurallar `tests/EnterpriseInventory.UnitTests/Architecture/LayerDependencyTests.cs` içinde test edilir;
yanlış yönde bir proje referansı eklenirse test kırılır.

## İsimlendirme

Arayüz metinleri Türkçe; backend sınıf, metot, değişken ve veritabanı isimleri İngilizcedir.
