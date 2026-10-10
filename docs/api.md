# API altyapısı (5. gün), giriş (7–8. gün) ve oturum (9. gün)

Kod: `src/EnterpriseInventory.Api`. Testler: `tests/EnterpriseInventory.IntegrationTests/Api`. Demirbaş uç
noktaları (11–15. gün) ayrı dokümanda: [`assets-api.md`](assets-api.md); tanım listeleri (18. gün):
[`lookups-api.md`](lookups-api.md).

## Health endpoint'leri

| Adres | Erişim | Ne kontrol eder | Yanıt |
| --- | --- | --- | --- |
| `GET /api/health/live` | Anonim | Uygulama çalışıyor mu (bağımlılık yok) | `200 Healthy` |
| `GET /api/health/ready` | Anonim | Veritabanına bağlanılıyor mu ve tüm migration'lar uygulanmış mı | `200 Healthy` veya `503 Unhealthy` |
| `GET /api/health` | Administrator | Her kontrolün durumu, süresi ve açıklaması (JSON) | `200` veya `503` |

- Anonim endpoint'ler yalnızca `Healthy`/`Unhealthy` döner; sunucu adı, hata mesajı gibi ayrıntı vermez.
  Ayrıntı sunucu loguna yazılır.
- Üçü de yalnızca `GET` ve `HEAD` kabul eder (yük dengeleyici ve IIS yoklamaları için `HEAD` yeterlidir).
- Migration uygulanmadan yayınlanan bir sürüm `ready` kontrolünde `503` döner (eksik migration adları
  `/api/health` yanıtında görünür).
- Veritabanı kontrolü en fazla 5 saniye bekler. SQL Server kapatıldığında `ready` 5 saniyede `503`
  döndü; SQL Server yeniden açılınca kendiliğinden `200`'e döndü.
- `ready` anonim olduğu için veritabanı aynı anda yalnızca bir kez kontrol edilir ve sonuç, kontrol
  başladıktan sonra 5 saniye boyunca tekrar kullanılır. Çok sayıda yoklama veritabanına yük bindiremez;
  buna karşılık durum değişikliği en geç birkaç saniye gecikmeyle görünür.
- Runtime hesabı (`ei_app_runtime` rolü) ile `ready` kontrolü çalışır; ek yetki gerekmez.
- IIS veya izleme aracı `ready` adresini yoklayabilir. Başarılı yoklamalar logu doldurmasın diye Verbose
  seviyesinde loglanır; reddedilen (`401`, `403`, `429`) ve başarısız (`5xx`) istekler normal logda kalır.
- Active Directory (LDAPS) kontrolü yalnızca `/api/health` içinde `active-directory` adıyla görünür ve sorun
  varsa `Degraded` olur; `ready` kontrolüne dahil değildir. AD kesintisinde açık oturumlar çalışmaya devam eder,
  yeni girişler reddedilir (bkz. [`active-directory.md`](active-directory.md#health-kontrolü)).

## Yetkilendirme: varsayılan olarak kapalı

- Her endpoint, ayrıca belirtilmedikçe **oturum açmış ve `Administrator` rolüne sahip** kullanıcı ister
  (fallback policy). Hiçbir endpoint'e uymayan istekler de buna dahildir: oturumsuz istek, adres var olmasa
  bile `401` alır.
- Anonim erişim yalnızca `AllowAnonymous` ile açılır. Şu an yalnızca `live`, `ready`, `GET /api/auth/csrf` ve
  `POST /api/auth/login` anonimdir; bir test bu listeyi denetler, yeni bir anonim endpoint eklenirse test kırılır.
  React derlemesinin dosyaları endpoint değildir; yetkilendirmeden önce statik dosya olarak sunulur, veri içermez
  (bkz. aşağıda "React sayfaları").
- API giriş sayfasına yönlendirmez; oturum yoksa `401`, yetki yoksa `403` döner.
- Oturum çerezi `__Host-EnterpriseInventory`: HttpOnly, Secure, SameSite=Strict, kalıcı değil. Yalnızca girişte
  `Bim_Envanter` üyelerine verilir ve sunucudaki bir oturuma bağlıdır; oturum 20 dakika işlem yapılmazsa, 8 saat
  dolunca, çıkışta veya AD yetkisi kalkınca biter. Ayrıntı: [`session-security.md`](session-security.md).
- Durum değiştiren her istek (GET/HEAD/OPTIONS/TRACE dışı) `X-CSRF-TOKEN` başlığında geçerli bir CSRF token'ı
  ister; yoksa `400` `csrf_invalid` "Güvenlik doğrulaması başarısız oldu." döner.

## Giriş: `POST /api/auth/login`

Anonim, yalnızca JSON (`application/json`, en fazla 8 KB), istemci adresi başına deneme limitli. Önce
`GET /api/auth/csrf` ile alınan token `X-CSRF-TOKEN` başlığında gönderilir.

```json
{ "userName": "ayse.yilmaz", "password": "…" }
```

| Durum | Yanıt | `code` |
| --- | --- | --- |
| Başarılı (`Bim_Envanter` üyesi) | `200` `{ "userName", "displayName", "roles": ["Administrator"], "csrfToken" }` ve oturum çerezi | — |
| CSRF token'ı eksik veya geçersiz | `400` "Güvenlik doğrulaması başarısız oldu." | `csrf_invalid` |
| Eksik/hatalı alan | `400` ValidationProblem, alan bazında Türkçe mesaj (`errors.userName`, `errors.password`) | — |
| Kullanıcı adı veya parola hatalı, ya da hesap kilitli | `401` "Kullanıcı adı veya parola hatalı." | `invalid_credentials` |
| Hesap pasif, süresi dolmuş, parola değişmeli | `403` "Hesabınızla şu anda giriş yapılamıyor." | `account_unavailable` |
| Grup üyesi değil | `403` "Bu uygulamaya giriş yetkiniz yok." | `not_authorized` |
| AD'ye ulaşılamıyor | `503` "Giriş şu anda yapılamıyor." | `directory_unavailable` |
| AD kabul etti ama giriş kaydedilemedi (veritabanı) | `503` "Giriş şu anda yapılamıyor." | `sign_in_unavailable` |
| Deneme limiti aşıldı | `429` ve `Retry-After` | — |

Ret yanıtlarında çerez verilmez. Akışın ayrıntısı, AD hata kodları ve grup politikası:
[`active-directory.md`](active-directory.md#giriş-7-gün). CSRF token'ı kullanıcıya bağlı olduğu için girişten
sonraki isteklerde yanıttaki `csrfToken` kullanılır.

## Oturum: `GET /api/auth/csrf`, `GET /api/auth/me`, `POST /api/auth/logout`

| Adres | Erişim | Yanıt |
| --- | --- | --- |
| `GET /api/auth/csrf` | Anonim | `200` `{ "token": "…" }` ve CSRF çerezi `__Host-EnterpriseInventory.Csrf`. Token o anki kullanıcıya (veya oturumsuz isteğe) bağlıdır. |
| `GET /api/auth/me` | Administrator | `200` `{ "userName", "displayName", "roles" }`; oturum yoksa veya bittiyse `401`. |
| `POST /api/auth/logout` | Administrator, CSRF token'ı | `204`; oturum sunucuda biter, çerez silinir, `SignedOut` audit kaydı yazılır. |

Web uygulaması token'ı yalnızca bellekte tutar (localStorage/sessionStorage kullanılmaz). Kurallar ve testler:
[`session-security.md`](session-security.md).

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
- Alan doğrulama hataları `400` ValidationProblem olarak `errors` nesnesinde alan adıyla (camelCase) ve Türkçe
  mesajla döner. Domain kuralı ihlalleri ve RowVersion çakışması `409` ve ayırt edici bir `code` alır
  (`concurrency_conflict`, `duplicate_value`, `asset_archived` …); liste:
  [`assets-api.md`](assets-api.md#hata-kodları).

## Correlation ID

- Her isteğe sunucu yeni bir kimlik verir. `X-Correlation-ID` yanıt başlığında, hata yanıtlarında
  (`correlationId`) ve o isteğin tüm log satırlarında aynıdır; audit kayıtları da bunu kullanacak.
- İstemcinin gönderdiği `X-Correlation-ID` dikkate alınmaz; böylece log kayıtları sahte kimlikle
  karıştırılamaz. Kullanıcı bir hatayı bildirirken bu kimliği verir, loglarda o istek bulunur.

## Güvenlik başlıkları

Tüm yanıtlarda: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`Cross-Origin-Opener-Policy: same-origin`, kısıtlı `Permissions-Policy`. `/api` ve `/hubs` yanıtlarında ayrıca
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` ve `Cache-Control: no-store`. Kestrel
`Server` başlığını göndermez; IIS'te `web.config` `Server` ve `X-Powered-By` başlıklarını kaldırır
(`WebConfigTests`). Üretimde HSTS açıktır.

## React sayfaları

Yayında React derlemesi API ile aynı klasörden (`wwwroot`) ve aynı adresten sunulur
([`deploy/iis/README.md`](../deploy/iis/README.md)).

- `/assets/...` altındaki dosyaların adı içerikleriyle değişir; `Cache-Control: public, max-age=31536000, immutable`.
- `index.html` ve diğer dosyalar `Cache-Control: no-cache`; yeni sürüm yayınlandığında tarayıcı hemen alır.
- Hiçbir endpoint'e uymayan, dosya adı olmayan bir sayfa adresine (`/`, `/envanter/123`) `GET`/`HEAD` isteği
  `index.html` alır; yönlendirmeyi React yapar. Uzantılı ama var olmayan bir dosya (`/eksik.js`) sayfa almaz, bilinmeyen
  her adres gibi `401`/`404` döner. Bu bir yönlendirme kuralı (catch-all route) değil, bir ara katmandır: catch-all
  route, yönlendirmenin yöntem ve içerik türü kontrollerine katıldığı için API'nin `405` ve `415` yanıtlarını `404`
  yapıyordu (38. günde tüm entegrasyon testleri çalıştırılınca bulundu).
- `/api/...` ve `/hubs/...` hiçbir zaman sayfaya düşmez: oturumsuz istek `401`, var olmayan API adresi `404`
  problem+json alır. Sayfa adresine `POST`/`PUT`/`PATCH`/`DELETE` sayfa almaz (oturumsuz `401`, yönetici `404`).
- Sayfaların CSP'si: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
  font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`.
  `eval` ve satır içi script yoktur (Zod `jitless` ayarıyla çalışır, bkz. `src/zodConfig.ts`); `style-src`'deki
  `'unsafe-inline'` Material UI'nin çalışma anında eklediği stiller içindir.

Testler: `WebAppHostingTests`; 38. günde yayın klasörü Chromium'da CSP ihlali olmadan çalıştı.

## CORS

CORS yapılandırılmadı ve bu bilinçli bir karardır: React, `/api` ve `/hubs` aynı origin'den sunulur.
Başka bir origin'den gelen isteklere `Access-Control-Allow-Origin` verilmez, tarayıcı bu yanıtları okutmaz.

## Rate limiting

- Kullanıcı başına (oturum yoksa istemci adresi başına) sabit pencere: varsayılan **60 saniyede 300 istek**.
- Aşılınca `429`, `Retry-After` başlığı ve Türkçe ProblemDetails döner.
- Ayar: `RateLimiting:PermitLimit`, `RateLimiting:WindowSeconds`. Geçersiz değerle uygulama başlamaz.
- Giriş denemeleri (`POST /api/auth/login`) için ayrıca istemci adresi başına **60 saniyede 10 deneme**
  (`RateLimiting:LoginPermitLimit`, `RateLimiting:LoginWindowSeconds`).
- Uygulamanın önüne tüm kullanıcıları tek adresten geçiren bir ters proxy konursa anonim limit herkes için
  ortak olur; o durumda `ForwardedHeaders` yapılandırılmalıdır.

## Loglama

Serilog. Her satırda `CorrelationId` vardır. Geliştirmede konsola, üretimde
`D:\Logs\EnterpriseInventory\log-<tarih>.txt` dosyasına (30 gün) yazılır. İstek gövdeleri, çerezler ve başlıklar
loglanmaz; üretimde ASP.NET Core'un kendi logları yalnızca Warning ve üstündedir.
