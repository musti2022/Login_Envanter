# Canlı bildirimler (26–29. gün)

Kod: `src/EnterpriseInventory.Api/Realtime`, `src/EnterpriseInventory.Api/Security/SessionCookieEvents.cs`,
`src/EnterpriseInventory.Infrastructure/Identity/UserSessionService.cs` (`CheckAsync`),
`src/EnterpriseInventory.Application/Assets/AssetChanges.cs`. Testler:
`tests/EnterpriseInventory.IntegrationTests/Realtime/InventoryHubTests.cs`,
`tests/EnterpriseInventory.IntegrationTests/Realtime/AssetEventTests.cs`,
`tests/EnterpriseInventory.UnitTests/Assets/AssetChangePublisherTests.cs`; web: `src/EnterpriseInventory.Web/src/realtime`,
`src/EnterpriseInventory.Web/e2e/live.spec.ts`.

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

## Demirbaş olayları (27. gün)

| Olay | Ne zaman |
| --- | --- |
| `AssetCreated` | Yeni demirbaş kaydedildi |
| `AssetUpdated` | Demirbaş düzenlendi (en az bir alan değişti) |
| `AssetArchived` | Demirbaş arşivlendi |
| `AssetAssigned` | Demirbaş zimmetlendi |
| `AssetReturned` | Zimmet iade alındı |
| `AssetLocationChanged` | Şehir, lokasyon veya departman değişti |

- Her olay `{ assetId, occurredAt }` taşır; `occurredAt`, demirbaşın kayıttaki son güncelleme zamanıdır (yeni
  kayıtta oluşturulma zamanı).
- **Yalnızca commit sonrası:** olay, işlem ve audit kaydı aynı transaction'da commit edildikten sonra, servis
  katmanında (`AssetChangePublisher`) yayımlanır. Transaction geri alınırsa (ör. audit kaydı yazılamazsa) istek
  hata ile biter ve olay hiç yayımlanmaz.
- Reddedilen işlemler (doğrulama hatası, `409` çakışma, iş kuralı, bulunamayan kayıt, AD'ye ulaşılamaması) ve
  hiçbir şeyi değiştirmeyen kayıtlar (aynı değerlerle düzenleme, aynı yere taşıma) olay üretmez. Hiçbir şey
  yazılmadığı RowVersion'ın değişmemesinden anlaşılır.
- **Bildirim hatası commit'i geri almaz:** istek olayı yalnızca bellekteki bir kuyruğa bırakır ve döner; gönderim
  arka planda yapılır (`AssetChangeBroadcaster`). Kuyruğa bırakma bile hata verirse hata loglanır, istemci başarılı
  cevabı alır; veri ve audit kaydı yerinde kalır. Gönderim hataları da yalnızca loglanır.
- Kuyruk 1.000 olayla sınırlıdır; dolarsa yeni olaylar atılır ve uyarı loglanır, istek beklemez.
- Olaylar her bağlantıya, verildiği sırayla gider. Bağlantıların hepsi aynı yetkiye sahip yöneticilere ait olduğu
  için olaylar herkese gönderilir; ileride farklı okuma yetkileri eklenirse SignalR grupları kullanılmalıdır.
- Olaylar yalnızca bir ipucudur: ekran güncel veriyi API'den okur (28. gün). Kaçan bir olay (ör. süreç commit ile
  gönderim arasında durursa) veri kaybı değildir; ekran yeniden bağlanınca veya yenilenince eşitlenir (29. gün).

## Ekranların canlı yenilenmesi (28. gün)

- Oturum açmış kullanıcı uygulamanın herhangi bir sayfasındayken tek bir hub bağlantısı açılır (`useLiveUpdates`,
  `AppLayout` içinde); oturum kapanınca veya uygulamadan çıkılınca bağlantı kapatılır. Giriş sayfasında bağlantı yoktur.
- Bağlantının ilk isteği (negotiate) ve Long Polling gönderimleri, API istekleri gibi `X-CSRF-TOKEN` başlığı taşır;
  token reddedilirse bir kez yenilenip tekrar denenir. Token yalnızca bellekte tutulur.
- Bir olay geldiğinde TanStack Query önbelleği geçersiz kılınır ve **veri yalnızca API'den okunur** (olay veri
  taşımaz):

  | Ne yenilenir | Hangi olayda |
  | --- | --- |
  | Envanter listeleri (her sayfa, filtre ve sıralama) | Her demirbaş olayında |
  | Gösterge paneli | Her demirbaş olayında |
  | Demirbaş detayı, geçmişi ve zimmet geçmişi | Yalnızca o demirbaşın olayında |

  Ekranda görünen sorgular hemen, diğerleri bir sonraki gösterimde yeniden alınır.
- Bu yenileme istekleri `X-Background-Request: 1` başlığı taşır. Sunucu oturumu yine kontrol eder (bitmişse `401`)
  ama isteği **kullanıcı etkinliği saymaz**: başkalarının değişiklikleriyle sürekli yenilenen, başında kimsenin
  olmadığı bir ekran oturumu boşta kalma süresinin ötesine taşıyamaz. Başlık yalnızca GET/HEAD isteklerinde geçerlidir
  ve oturumu ancak kısaltabilir, uzatamaz.
- Düzenleme sayfası açıkken başka bir kullanıcı aynı demirbaşı kaydederse form kullanıcının yazdıklarını korur ve
  "Bu demirbaş siz düzenlerken başka bir kullanıcı tarafından değiştirildi. Şimdi kaydederseniz kayıt çakışması
  uyarısı alırsınız." uyarısı çıkar; "Güncel kaydı yükle" ile yeni sürüme geçilir. Form hiçbir zaman sessizce yeni
  sürüme taşınmaz; eski sürümle kaydetmek `409` ile reddedilir. Açık diyaloglar (zimmet, iade, konum, arşiv) da
  açıldıkları sürümle çalışır.
- Üst çubuktaki durum göstergesi (`role="status"`): **Canlı** (yeşil), **Bağlanıyor** (gri), **Yeniden
  bağlanıyor** (turuncu; diğer kullanıcıların değişiklikleri o sırada otomatik görünmez), **Canlı güncelleme yok**
  (gri; oturum sona erdi). Telefonda yalnızca renkli nokta görünür, metin ekran okuyucuya okunur; açıklama ipucunda
  yazar.

## Yeniden bağlanma ve tam eşitleme (29. gün)

- Bağlantı kurulamazsa (API kapalı, ağ yok) veya kurulduktan sonra koparsa (API yeniden başladı, ağ kesildi, sunucu
  bağlantıyı kapattı) uygulama açık olduğu sürece **vazgeçmeden** yeniden dener. Kopan bağlantı hemen, sonraki
  denemeler 2, 5, 10 saniye sonra, ardından 30 saniyede bir denenir (`reconnect.ts`). İlk kurulum hatası ile kopma
  aynı döngüden geçer; SignalR'ın `withAutomaticReconnect` özelliği bu yüzden kullanılmadı (ilk kurulum hatasını
  kapsamaz, sınırlı sayıda dener).
- Bağlantı geri geldiğinde **tam eşitleme** yapılır: kopukluk sırasında gelen olaylar bu ekrana ulaşmadığı için
  oturum sorguları (`auth`) dışındaki tüm TanStack Query önbelleği geçersiz kılınır. Ekranda görünenler hemen,
  diğerleri bir sonraki gösterimde API'den yeniden okunur. Bu okumalar da arka plan okumasıdır
  (`X-Background-Request`), oturumu uzatmaz. Bağlantı ilk denemede kurulursa eşitleme yapılmaz.
- Düzenleme sayfası açıkken kopukluk sırasında başka biri kaydetmişse, eşitleme 28. gündeki uyarıyı çıkarır; form
  yine sessizce yeni sürüme taşınmaz.
- Oturum bittiyse (çıkış başka sekmede yapıldı, süre doldu, AD erişimi kaldırdı) sunucu bağlantıyı kapatır;
  yeniden deneme `401` alır. Bu durumda yeniden deneme durur, API isteklerindeki `401` ile aynı yol izlenir:
  kullanıcının önbellekteki verileri silinir ve giriş sayfasında "Oturumunuz sona erdi." uyarısı gösterilir.
  `403` ve `5xx` oturumu bitirmez, deneme sürer.
- Yeniden deneme istekleri (negotiate) `/hubs` altında olduğundan etkinlik sayılmaz; açık kalmış bir ekran
  yeniden bağlanarak oturumu uzatamaz.
- Bilinen sınır: ilk bağlantı, sayfanın ilk verisi okunduktan hemen sonra kurulur; bu arada (genellikle saniyenin
  altında) yapılan bir değişiklik bir sonraki olaya veya yenilemeye kadar görünmeyebilir. Kayıt bundan etkilenmez:
  eski sürümle kaydetmek RowVersion ile `409` olarak reddedilir.

## Birden fazla sunucu

Açık bağlantıların listesi (`HubConnectionRegistry`) ve olay kuyruğu her sunucunun kendi belleğindedir. Çıkış,
isteği karşılayan sunucudaki bağlantıları hemen kapatır; diğer sunuculardaki bağlantılar, oturum veritabanında
bittiği için bir sonraki kontrolde kapanır. Olaylar ise yalnızca değişikliği yapan sunucudaki bağlantılara gider.

Uygulama tek IIS sunucusunda çalışacak şekilde tasarlandı. Birden fazla sunucu (web farm) gündeme gelirse
seçenekler:

1. **SignalR backplane** (ör. Redis): her sunucunun olayları diğerlerine iletilir. Olaylar yine bellekten
   gönderildiği için süreç durursa kaybolabilir; ekranlar yeniden bağlanınca eşitlenir.
2. **Outbox tablosu:** olay, değişiklikle aynı transaction'da bir `OutboxMessages` tablosuna yazılır; her sunucuda
   çalışan bir arka plan işi tabloyu okuyup kendi bağlantılarına gönderir. Kayıp olmaz, sunucu sayısından
   bağımsızdır; bedeli ek tablo, temizlik işi ve birkaç saniyelik gecikmedir.

Olaylar yalnızca ipucu olduğu ve ekranlar her yeniden bağlantıda tam eşitlendiği için tek sunucuda outbox
kullanılmadı.

## IIS

WebSocket için sunucuda IIS "WebSocket Protocol" özelliği kurulu olmalıdır. Kurulu değilse istemci Server-Sent
Events'e veya Long Polling'e düşer; bağlantı çalışır ama daha fazla istek üretir.
[`Test-ServerPrerequisites.ps1`](../deploy/iis/Test-ServerPrerequisites.ps1) özelliğin kurulu olduğunu denetler.

HTTP/2 üzerinde tarayıcı WebSocket'i `CONNECT` isteğiyle açar (RFC 8441). Bu istek veri değiştirmez; CSRF kontrolü
onu WebSocket el sıkışması olarak tanır ve token istemez. Origin kontrolü yine uygulanır. 38. günde yayın klasörü
Chromium'da HTTP/2 ile denenirken bulundu ve düzeltildi (`Http2WebSocketTests`, gerçek Kestrel üzerinde).

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
| `Http2WebSocketTests` (gerçek Kestrel, HTTP/2) | HTTP/2 `CONNECT` ile açılan WebSocket hub'a bağlanır (`200`); başka origin'den `403`; WebSocket olmayan `CONNECT` CSRF token'ı ister (`400`) |

| `Every_kind_of_change_is_announced_once_it_is_committed` | Altı olayın her biri doğru demirbaş ve zamanla gelir; olay geldiği anda kilit beklemeyen bir okuma değişikliği ve audit kaydını commit edilmiş görür |
| `Every_open_connection_hears_of_a_change_made_by_another_administrator` | Bir yöneticinin değişikliğini başka yöneticinin iki sekmesi de duyar |
| `Refused_and_unchanged_writes_are_not_announced` | `409`, `400`, `404`, kural ihlali ve değişiklik içermeyen kayıtlar olay üretmez |
| `A_change_that_is_rolled_back_is_not_announced` | Audit kaydı yazılamayıp geri alınan taşıma olay üretmez |
| `A_failing_notifier_neither_fails_nor_undoes_a_committed_change` | Bildirim katmanı hata verse de istek başarılı, veri ve audit kaydı yerinde |
| `AssetChangePublisherTests` (birim) | Yalnızca başarılı ve bir şey değiştiren yazmalar yayımlanır; bildirim hatası loglanır, sonuç değişmez |
| `A_background_read_is_checked_but_does_not_keep_an_idle_session_alive` | `X-Background-Request` taşıyan okuma oturumu uzatmaz; yazma isteği başlıkla bile etkinlik sayılır |
| `LiveUpdates.test.tsx` (Vitest) | Bağlanma ve durum göstergesi; detay sayfası başka kullanıcının değişikliğini arka plan okumasıyla gösterir; altı olayın her biri listeyi ve gösterge panelini yeniler; başka demirbaşın olayı detayı yenilemez; sayfadan çıkınca bağlantı kapanır; düzenlemede uyarı ve "Güncel kaydı yükle". 29. gün: kopan bağlantı hemen, kurulamayan artan aralıklarla yeniden denenir; bağlantı dönünce görünen her şey arka plan okumasıyla yeniden okunur, görünmeyenler geçersiz kılınır, oturum sorgusu okunmaz; ilk denemede kurulan bağlantı eşitleme yapmaz; oturum bitmişse deneme durur ve giriş sayfasına "Oturumunuz sona erdi" ile gidilir; sayfadan çıkınca bekleyen deneme iptal edilir |
| `inventoryHub.test.ts` (Vitest) | Hub istekleri CSRF token'ı taşır; reddedilen token bir kez yenilenir; `401` (POST ve GET) oturum sonu olarak bildirilir ve tekrar denenmez; `403`, `500`, `503` oturumu bitirmez |
| `reconnect.test.ts` (Vitest) | Bekleme süreleri 0, 2, 5, 10 saniye, ardından hep 30 saniye |
| `live.spec.ts` (Playwright, iki tarayıcı) | Başka bilgisayardaki yöneticinin konum değişikliği detay sayfasına sayfa yenilenmeden yansır (arka plan okumasıyla); başka ekranda eklenen demirbaş açık listede görünür; düzenleme sırasında başka ekranda yapılan kayıt uyarı olarak gösterilir, yazılanlar korunur; 29. gün: WebSocket kesilip hub'a ulaşılamazken gösterge "Yeniden bağlanıyor" der, başka yöneticinin bu sırada yaptığı kayıt ekrana gelmez, ağ dönünce bağlantı kurulur ve kayıt arka plan okumasıyla görünür; başka sekmede çıkış yapılınca bu sekme giriş sayfasına "Oturumunuz sona erdi" uyarısıyla gider |

Testler gerçek SQL Server ile çalışır. 26. gün testlerinde saat bir test saatidir ve AD, cevabını her testin
belirlediği bir test dublörüdür (gerçek AD kontrolü `SambaAccessCheckTests` içindedir); 27. gün testlerinde
yöneticiler Development sahte dizini ile gerçek giriş çereziyle oturum açar.
