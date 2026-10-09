# Canlı bildirimler (26. gün)

Kod: `src/EnterpriseInventory.Api/Realtime`, `src/EnterpriseInventory.Api/Security/SessionCookieEvents.cs`,
`src/EnterpriseInventory.Infrastructure/Identity/UserSessionService.cs` (`CheckAsync`). Testler:
`tests/EnterpriseInventory.IntegrationTests/Realtime/InventoryHubTests.cs`.

## Hub: `/hubs/inventory`

- ASP.NET Core SignalR hub'ı. Yalnızca `Administrator` politikasıyla açılır: hub sınıfında `[Authorize]`, uç
  noktada `RequireAuthorization` ve uygulamanın varsayılan olarak kapalı politikası birlikte geçerlidir. Anonim
  bağlantı `401`, rolü olmayan kullanıcı `403` alır; bu üç taşıma yöntemi için de (WebSocket, Server-Sent Events,
  Long Polling) ve anlaşma (negotiate) yapılmadan açılan doğrudan WebSocket için de aynıdır.
- Her bağlantı bir sunucu taraflı oturuma bağlıdır. Çerezinde oturum anahtarı olmayan bir kimlik (yalnızca testlerin
  kimlik doğrulama şeması üretebilir) bağlanır bağlanmaz kapatılır.
- Hub'ın istemcinin çağırabileceği hiçbir metodu yoktur; yalnızca sunucu gönderir (`IInventoryClient`). Bilinmeyen
  her çağrı hata ile döner, bağlantı açık kalır. İstemciden gelen mesaj en fazla 4 KB olabilir; ayrıntılı hata
  mesajları istemciye gönderilmez (`EnableDetailedErrors = false`), yalnızca sunucu logunda kalır.
- Bildirimler yalnızca "şu demirbaş değişti" der (`assetId`, `occurredAt`); ekran güncel veriyi normal API
  isteğiyle alır. Böylece bildirim kanalı, kullanıcının API'den okuyamayacağı hiçbir veriyi taşımaz.

## CSRF ve başka siteler

- Bağlantının ilk adımı (`POST /hubs/inventory/negotiate`) diğer tüm POST istekleri gibi `X-CSRF-TOKEN` başlığı
  ister; başlık yoksa `400 csrf_invalid`. Long Polling'in gönderme ve kapatma istekleri de bu başlığı taşır.
- Tarayıcılar WebSocket bağlantılarına CORS uygulamaz. Bu yüzden `/hubs` altındaki istekler `Origin` başlığı
  taşıyorsa, bu başlığın sunucu adı ve portu isteğin gönderildiği `Host` ile aynı olmalıdır; değilse
  `403 cross_origin` ("Bu bağlantıya izin verilmiyor."). `null` origin da reddedilir. Oturum çerezinin
  `SameSite=Strict` ayarı çerezi zaten başka sitelerden gelen isteklere eklemez; bu kontrol ikinci savunma hattıdır.
- Şema (http/https) karşılaştırılmaz: geliştirmede Vite sunucusu http sayfadan gelen isteği `Host` başlığını
  değiştirmeden https API'ye iletir. Üretimde API'nin önünde bir ters vekil sunucu varsa orijinal `Host` başlığını
  korumalıdır.

## Oturum bitince bağlantı da biter

| Durum | Bağlantı ne zaman kapanır |
| --- | --- |
| Kullanıcı çıkış yapar | Hemen: çıkış isteği, o sunucudaki oturumun tüm bağlantılarını kapatır |
| Boşta kalma veya mutlak süre dolar | Bir sonraki kontrolde |
| AD yeniden kontrolü erişimi kaldırır (gruptan çıkarma, hesap pasif, süresi dolmuş, silinmiş) | Bir sonraki kontrolde; `AccessRevoked` audit kaydı `system` adına, kendi correlation ID'si ile yazılır |
| AD'ye kısa süre ulaşılamaz | Kapanmaz; oturumun AD kesintisi toleransı (`Session:DirectoryOutageGraceMinutes`) aynen geçerlidir |
| Oturum kontrolü yapılamaz (ör. veritabanı hatası) | Hemen (güvenli tarafta kalınır); ekran yeniden bağlanır, yeni bağlantı yalnızca geçerli oturumla kabul edilir |
| Kimlik doğrulama bileti süresi dolar | SignalR'ın `CloseOnAuthenticationExpiration` ayarıyla hemen |

- Açık bir bağlantı istek göndermediği için reddedilemez; bunun yerine `HubSessionMonitor`, bağlantısı olan her
  oturumu `Realtime:SessionCheckInterval` aralıkla (varsayılan 30 saniye, 0,1 saniye–5 dakika) kontrol eder. Kontrol,
  istek kontrolüyle aynıdır (zaman aşımları, çıkış, AD yeniden kontrolü) ama **etkinlik sayılmaz**.
- `/hubs` altındaki istekler de (negotiate, Long Polling) etkinlik sayılmaz: sayfası açık kalan ama işlem yapmayan
  bir kullanıcının oturumu boşta kalma süresinde biter.
- İstek dışında yapılan bu kontrollerin logları ve audit kayıtları, her kontrol için üretilen bir correlation ID
  taşır (`BackgroundOperation`).

## Birden fazla sunucu

Açık bağlantıların listesi (`HubConnectionRegistry`) her sunucunun kendi belleğindedir. Çıkış, isteği karşılayan
sunucudaki bağlantıları hemen kapatır; diğer sunuculardaki bağlantılar, oturum veritabanında bittiği için bir
sonraki kontrolde kapanır. Bildirimlerin tüm sunuculardaki bağlantılara ulaşması için ise bir backplane veya
outbox gerekir; bkz. 27. gün notları.

## IIS

WebSocket için sunucuda IIS "WebSocket Protocol" özelliği kurulu olmalıdır. Kurulu değilse istemci Server-Sent
Events'e veya Long Polling'e düşer; bağlantı çalışır ama daha fazla istek üretir.

## Ayarlar

| Ayar | Varsayılan | Açıklama |
| --- | --- | --- |
| `Realtime:SessionCheckInterval` | `00:00:30` | Açık bağlantıların oturumlarının kontrol aralığı (0,1 saniye–5 dakika) |

## Testler

| Test | Ne doğrulanır |
| --- | --- |
| `An_anonymous_visitor_cannot_connect_with_any_transport` | Anonim ziyaretçi üç taşıma ile de ve doğrudan WebSocket ile `401` |
| `A_signed_in_user_without_the_administrator_role_is_refused` | Rolü olmayan kullanıcı `403` |
| `A_connection_that_belongs_to_no_session_is_closed_at_once` | Oturumu olmayan kimliğin bağlantısı hemen kapanır |
| `An_administrator_with_a_session_connects_with_every_transport` | Oturumlu yönetici WebSocket, SSE ve Long Polling ile bağlanır |
| `Opening_a_connection_needs_the_users_csrf_token` | CSRF başlığı olmadan `400` |
| `A_page_on_another_site_cannot_connect_even_with_the_users_cookie` | Başka origin `403 cross_origin` (negotiate ve doğrudan WebSocket); uygulamanın kendi origin'i kabul |
| `A_client_can_call_nothing_on_the_hub` | Hiçbir metot çağrılamaz, bağlantı açık kalır |
| `Signing_out_closes_the_sessions_connections_at_once` | Çıkış, kullanıcının iki bağlantısını hemen kapatır; başka kullanıcınınki açık kalır; eski çerezle yeni bağlantı `401` |
| `An_open_connection_does_not_keep_an_idle_session_alive` | Bağlantı trafiği etkinlik sayılmaz; boşta kalma süresinde bağlantı kapanır |
| `A_connection_is_closed_when_the_directory_takes_the_users_access_away` | AD erişimi kaldırınca bağlantı kapanır; audit `system` adına ve correlation ID ile |
| `A_short_directory_outage_does_not_close_the_connection` | AD'ye kısa süre ulaşılamaması bağlantıyı kapatmaz |

Testler gerçek SQL Server ve test saatiyle çalışır; AD, cevabını her testin belirlediği bir test dublörüdür (gerçek
AD kontrolü `SambaAccessCheckTests` içindedir).
