# Oturum güvenliği (9. gün)

Kod: `src/EnterpriseInventory.Domain/Users/UserSession.cs`, `src/EnterpriseInventory.Infrastructure/Identity/UserSessionService.cs`,
`src/EnterpriseInventory.Api/Security`, `src/EnterpriseInventory.Api/Auth/AuthEndpoints.cs`. Testler:
`tests/EnterpriseInventory.IntegrationTests/Api/SessionSecurityTests.cs`, `tests/EnterpriseInventory.IntegrationTests/ActiveDirectory/SambaAccessCheckTests.cs`,
`tests/EnterpriseInventory.UnitTests/DomainModel/UserSessionTests.cs`.

## Sunucu taraflı oturum

- Başarılı girişte `UserSessions` tablosuna bir oturum yazılır. Çerez yalnızca oturumun anahtarını (32 bayt
  rastgele değer) ve kullanıcının AD kimliğini taşır; çerez Data Protection ile şifreli ve imzalıdır.
- Veritabanında anahtarın kendisi değil yalnızca SHA-256 özeti tutulur; tablo okunsa bile oturum ele geçirilemez.
- **Her istekte** çerezdeki oturum veritabanında kontrol edilir. Oturum bitmişse çerez reddedilir ve silinir
  (`401`). Geçerli şifrelenmiş bir çerez bile, arkasında açık bir oturum yoksa kabul edilmez.
- Kullanıcının son işlem zamanı en fazla dakikada bir yazılır; isteklerin çoğu yalnızca okur.
- Canlı bildirim bağlantısının istekleri (`/hubs`) ve ekranların bildirim sonrası `X-Background-Request: 1` ile
  yaptığı okumalar işlem sayılmaz; açık bağlantılar oturum bitince kapanır (bkz. [`realtime.md`](realtime.md)).
- Oturum açılışı, `AdminUsers` güncellemesi ve `SignedIn` audit kaydı tek transaction'dadır. Kaydedilemezse
  oturum açılmaz (`503 sign_in_unavailable`).

| Kural | Varsayılan | Ayar |
| --- | --- | --- |
| İşlem yapılmazsa oturum biter | 20 dakika | `Session:IdleTimeoutMinutes` (1–240) |
| Girişten sonra, işlem yapılsa da oturum biter | 8 saat | `Session:AbsoluteTimeoutHours` (1–24) |
| `Bim_Envanter` üyeliği ve hesap durumu AD'de yeniden kontrol edilir | 5 dakikada bir | `Session:AccessRecheckMinutes` (1–60) |
| AD'ye ulaşılamazken oturum en fazla şu kadar daha sürer | 15 dakika | `Session:DirectoryOutageGraceMinutes` (0–240) |

Boşta kalma süresi mutlak süreden uzun olamaz; geçersiz ayarla uygulama başlamaz. Çerez kalıcı değildir (tarayıcı
kapanınca silinir), yenilenmez ve en geç oturumun mutlak bitişinde geçersizleşir.

### Düzenli AD yetki kontrolü

Oturum açıkken, `AccessRecheckMinutes` dolduktan sonraki ilk istekte uygulama **servis hesabıyla** AD'ye bağlanır,
kullanıcıyı girişte kaydedilen `objectGUID` ile `BaseDn` altında arar ve şunları kontrol eder:

| Durum | Sonuç |
| --- | --- |
| Hâlâ `Bim_Envanter` üyesi (girişteki `NestedGroupPolicy` ile aynı kural), hesap etkin, süresi dolmamış | Oturum sürer |
| Gruptan çıkarılmış, hesap pasif, süresi dolmuş, silinmiş veya `BaseDn` dışına taşınmış | Oturum hemen biter (`401`), `AccessRevoked` audit kaydı (`system` adına, nedeniyle) |
| AD'ye ulaşılamıyor, servis hesabı reddedildi, beklenmeyen yanıt | Oturum `DirectoryOutageGraceMinutes` boyunca sürer, kontrol dakikada bir yeniden denenir; süre dolunca oturum biter |

Kilitli hesap (`lockout`) oturumu bitirmez: kilitlenme başkasının hatalı denemeleriyle de olabilir. Yeni girişte
AD zaten reddeder.

### Oturumun bitme nedenleri

`UserSessions.EndReason`: `1` çıkış, `2` boşta kalma, `3` mutlak süre, `4` AD yetkisi kalktı, `5` AD'ye uzun süre
ulaşılamadı, `6` yönetici tarafından iptal. Bir kullanıcının tüm açık oturumlarını hemen kapatmak için (ör. hesap
çalındığında) DBA şunu çalıştırabilir; bir sonraki istekte oturum reddedilir:

```sql
UPDATE s SET EndedAt = SYSDATETIMEOFFSET(), EndReason = 6
FROM UserSessions AS s JOIN AdminUsers AS u ON u.Id = s.AdminUserId
WHERE u.SamAccountName = N'<kullanıcı>' AND s.EndedAt IS NULL;
```

## Çıkış: `POST /api/auth/logout`

Oturumu sunucuda bitirir (`SignedOut` audit kaydı), çerezi siler ve `204` döner. CSRF token'ı ister. Çıkıştan
sonra eski çerez tekrar gönderilse bile `401` alır.

## CSRF koruması

- **Durum değiştiren her istek** (GET, HEAD, OPTIONS, TRACE dışındaki her yöntem) `X-CSRF-TOKEN` başlığında geçerli
  bir token ister; uç nokta tek tek işaretlenmez, var olmayan adresler dahil hepsine uygulanır. Eksik veya geçersiz
  token `400` "Güvenlik doğrulaması başarısız oldu." (`csrf_invalid`) alır.
- Token `GET /api/auth/csrf` ile alınır (`{ "token": "…" }`). Token'ın çerez yarısı `__Host-EnterpriseInventory.Csrf`
  (HttpOnly, Secure, SameSite=Strict); diğer yarısı yanıt gövdesindedir ve web uygulaması onu yalnızca bellekte
  tutar.
- Token kullanıcıya bağlıdır: giriş yapmadan alınan token girişten sonra, bir kullanıcının token'ı başka bir
  kullanıcının oturumunda geçmez. Bu yüzden giriş yanıtı yeni kullanıcı için bir `csrfToken` içerir.
- Giriş isteği de CSRF token'ı ister; oturumsuz bir istek yetkisiz olduğu için önce `401` alır.
- Ek katmanlar: çerezler SameSite=Strict, API yalnızca JSON kabul eder ve CORS kapalıdır.

## Data Protection anahtarları

Çerezleri şifreleyen anahtarlar `DataProtection:KeysDirectory` klasöründe tutulur; böylece IIS uygulama havuzu
yeniden başladığında kullanıcıların oturumu düşmez. Windows'ta anahtarlar DPAPI (makine) ile şifrelenir.
Development dışında bu ayar zorunludur; boşsa, göreli bir yolsa veya `CHANGE-ME` içeriyorsa uygulama başlamaz.
Klasöre yalnızca uygulama havuzu kimliği ve yöneticiler erişebilmelidir; yedeklenmesi, kaybolursa yalnızca açık
oturumların düşmesine yol açar.

## Test sonuçları

SQL Server ve Samba AD açıkken tüm testler geçti (birim 270, entegrasyon 176).

| Test | Nerede | Sonuç |
| --- | --- | --- |
| Oturum kuralları: boşta kalma, mutlak süre, yeniden kontrol zamanı, AD kesintisinde yeniden deneme aralığı ve izin süresi, bitmiş oturumun ilk nedenini koruması | Unit | Geçti |
| Oturum ayarları aralıkları, boşta kalma ≤ mutlak süre; geçersiz oturum ve anahtar ayarlarıyla uygulama başlamaz | Unit + Integration | Geçti |
| CSRF: token'sız, girişten önceki token'la, başka kullanıcının token'ıyla `POST`/`PUT` (var olmayan adres dahil) `400`; kullanıcının token'ıyla geçer; girişte de token gerekir; CSRF çerezi `__Host-`, Secure, HttpOnly, SameSite=Strict | Integration | Geçti |
| Çıkış `204`, çerez silinir, oturum `SignedOut` ile biter ve audit'e yazılır, eski çerez tekrar gönderilince `401` | Integration (SQL Server) | Geçti |
| 19+19 dakika işlemle oturum sürer, 20 dakika işlemsiz `401`; 8 saat dolunca işlem yapılsa da `401`; süre ayarı yapılandırmadan okunur | Integration (test saati) | Geçti |
| AD'de gruptan çıkma, pasif, süresi dolmuş, bulunamayan hesap: kontrol zamanı gelmeden oturum sürer, ilk kontrolde `401` ve `system` adına `AccessRevoked` | Integration | Geçti |
| AD kesintisi: oturum izin süresi boyunca sürer, kontrol en fazla dakikada bir denenir, 5+15 dakikada biter; kısa kesintiden sonra kontrol tekrar onaylanır | Integration | Geçti |
| Veritabanında iptal edilen oturum bir sonraki istekte `401`; geçerli şifrelenmiş ama oturumsuz çerez `401`; veritabanında anahtarın yalnızca SHA-256 özeti var | Integration | Geçti |
| Anahtar klasörü: yeniden başlatılan uygulama önceki çerezi kabul eder, klasörde anahtar dosyası oluşur | Integration | Geçti |
| Samba AD, servis hesabıyla yeniden kontrol: doğrudan, birincil grup ve (politikaya göre) iç içe üye `Allowed`; üye olmayan, tuzak grup üyesi `NotAuthorized`; pasif, süresi dolmuş hesap; bilinmeyen GUID ve `BaseDn` dışı `AccountNotFound`; yanlış servis parolası `DirectoryUnavailable` (parola loga yazılmaz) | Integration (Samba AD) | Geçti |
| Kasıtlı bozma: oturum kontrolünü atlamak 9, CSRF ara katmanını kaldırmak 2 testi kırdı | — | Yakalandı |

SignalR Hub'ı henüz yok; eklendiğinde aynı çerez, aynı oturum kontrolü ve varsayılan olarak kapalı yetkilendirme
ona da uygulanacak.
