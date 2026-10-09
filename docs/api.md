# API altyapısı (5. gün)

Kod: `src/EnterpriseInventory.Api`. Testler: `tests/EnterpriseInventory.IntegrationTests/Api`.

## Health endpoint'leri

| Adres | Erişim | Ne kontrol eder | Yanıt |
| --- | --- | --- | --- |
| `GET /api/health/live` | Anonim | Uygulama çalışıyor mu (bağımlılık yok) | `200 Healthy` |
| `GET /api/health/ready` | Anonim | Veritabanına bağlanılıyor mu ve tüm migration'lar uygulanmış mı | `200 Healthy` veya `503 Unhealthy` |
| `GET /api/health` | Administrator | Her kontrolün durumu, süresi ve açıklaması (JSON) | `200` veya `503` |

- Anonim endpoint'ler yalnızca `Healthy`/`Unhealthy` döner; sunucu adı, hata mesajı gibi ayrıntı vermez.
  Ayrıntı sunucu loguna yazılır.
- Migration uygulanmadan yayınlanan bir sürüm `ready` kontrolünde `503` döner (eksik migration adları
  `/api/health` yanıtında görünür).
- Veritabanı kontrolü en fazla 5 saniye sürer. SQL Server kapatıldığında `ready` 5 saniyede `503`
  döndü; SQL Server yeniden açılınca kendiliğinden `200`'e döndü.
- Runtime hesabı (`ei_app_runtime` rolü) ile `ready` kontrolü çalışır; ek yetki gerekmez.
- IIS veya izleme aracı `ready` adresini yoklayabilir. Başarılı yoklamalar logu doldurmasın diye Verbose
  seviyesinde loglanır.
- Active Directory (LDAPS) kontrolü 6. günde `ready` kontrolüne eklenecek.

## Yetkilendirme: varsayılan olarak kapalı

- Her endpoint, ayrıca belirtilmedikçe **oturum açmış ve `Administrator` rolüne sahip** kullanıcı ister
  (fallback policy). Hiçbir endpoint'e uymayan istekler de buna dahildir: oturumsuz istek, adres var olmasa
  bile `401` alır.
- Anonim erişim yalnızca `AllowAnonymous` ile açılır. Şu an yalnızca `live` ve `ready` anonimdir; bir test
  bu listeyi denetler, yeni bir anonim endpoint eklenirse test kırılır.
- API giriş sayfasına yönlendirmez; oturum yoksa `401`, yetki yoksa `403` döner.
- Oturum çerezi `__Host-EnterpriseInventory`: HttpOnly, Secure, SameSite=Strict. Girişin kendisi, oturum
  süresi, çıkış ve CSRF koruması 7–9. günlerde eklenecek. Şu an hiç çerez verilmez.
- React sayfaları API'den sunulmaya başlandığında (giriş ekranı dahil) statik dosyalar ayrıca anonim
  erişime açılacak.

## Hata yanıtları

Tüm hatalar RFC 7807 ProblemDetails (`application/problem+json`) biçimindedir:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "İstenen kaynak bulunamadı.",
  "status": 404,
  "correlationId": "3f6c0b9e4a2d4c55a8a0f1d7c2e9b411"
}
```

- Çerçevenin ürettiği hataların başlığı Türkçedir (400, 401, 403, 404, 405, 409, 415, 429, 500, 503).
  Bir endpoint kendi başlığını verirse o korunur.
- Beklenmeyen hatalarda `500` ve "Beklenmeyen bir hata oluştu." döner; exception mesajı ve stack trace
  yanıta yazılmaz, yalnızca loglanır.
- Alan doğrulama hataları (12. gün), domain kuralı ihlalleri ve RowVersion çakışmasında `409` (13. gün)
  ilgili endpoint'lerle birlikte eklenecek.

## Correlation ID

- Her isteğe sunucu yeni bir kimlik verir. `X-Correlation-ID` yanıt başlığında, hata yanıtlarında
  (`correlationId`) ve o isteğin tüm log satırlarında aynıdır; audit kayıtları da bunu kullanacak.
- İstemcinin gönderdiği `X-Correlation-ID` dikkate alınmaz; böylece log kayıtları sahte kimlikle
  karıştırılamaz. Kullanıcı bir hatayı bildirirken bu kimliği verir, loglarda o istek bulunur.

## Güvenlik başlıkları

Tüm yanıtlarda: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`Cross-Origin-Opener-Policy: same-origin`, kısıtlı `Permissions-Policy`. `/api` ve `/hubs` yanıtlarında ayrıca
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` ve `Cache-Control: no-store`. Kestrel
`Server` başlığını göndermez (IIS için `web.config` ayarı yayın aşamasında yapılacak). Üretimde HSTS açıktır.

React sayfalarının CSP'si, sayfalar API'den sunulmaya başlandığında ayrıca tanımlanacak.

## CORS

CORS yapılandırılmadı ve bu bilinçli bir karardır: React, `/api` ve `/hubs` aynı origin'den sunulur.
Başka bir origin'den gelen isteklere `Access-Control-Allow-Origin` verilmez, tarayıcı bu yanıtları okutmaz.

## Rate limiting

- Kullanıcı başına (oturum yoksa istemci adresi başına) sabit pencere: varsayılan **60 saniyede 300 istek**.
- Aşılınca `429`, `Retry-After` başlığı ve Türkçe ProblemDetails döner.
- Ayar: `RateLimiting:PermitLimit`, `RateLimiting:WindowSeconds`. Geçersiz değerle uygulama başlamaz.
- Giriş denemeleri için daha sıkı limit 7. günde eklenecek.
- Uygulamanın önüne tüm kullanıcıları tek adresten geçiren bir ters proxy konursa anonim limit herkes için
  ortak olur; o durumda `ForwardedHeaders` yapılandırılmalıdır.

## Loglama

Serilog. Her satırda `CorrelationId` vardır. Geliştirmede konsola, üretimde
`D:\Logs\EnterpriseInventory\log-<tarih>.txt` dosyasına (30 gün) yazılır. İstek gövdeleri, çerezler ve başlıklar
loglanmaz; üretimde ASP.NET Core'un kendi logları yalnızca Warning ve üstündedir.
