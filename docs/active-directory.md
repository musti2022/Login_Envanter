# Active Directory (6. gün: LDAPS ve TLS, 7. gün: giriş, 8. gün: Bim_Envanter yetkisi)

Kod: `src/EnterpriseInventory.Infrastructure/ActiveDirectory`, `src/EnterpriseInventory.Application/Authentication`,
`src/EnterpriseInventory.Api/Auth`. Testler: `tests/EnterpriseInventory.UnitTests/ActiveDirectory`,
`tests/EnterpriseInventory.UnitTests/Authentication`, `tests/EnterpriseInventory.IntegrationTests/ActiveDirectory`,
`tests/EnterpriseInventory.IntegrationTests/Api` (`ActiveDirectoryConfigurationTests`, `LoginEndpointTests`,
`SignInRecordingTests`).

## Bağlantı: yalnızca LDAPS

- Uygulama domain controller'a yalnızca **LDAPS** (varsayılan TCP 636) ile bağlanır. `UseLdaps=false` veya düz
  LDAP yapılandırılırsa uygulama **başlamaz**. Parola, doğrulanmış bir TLS bağlantısı kurulmadan hiçbir zaman
  gönderilmez (TLS el sıkışması bitmeden sunucuya tek bayt LDAP verisi gitmez).
- TLS 1.2 ve 1.3 dışındaki sürümler kapalıdır.
- LDAP istemcisi: `Novell.Directory.Ldap.NETStandard` 4.0.0 (MIT). TLS'i .NET'in kendi `SslStream` sınıfı ile
  kurar; sertifika kararı bu projedeki `LdapsCertificateValidator` tarafından verilir. Böylece kurallar Windows
  Server/IIS'te ve Linux test ortamında aynı kodla çalışır ve aynı testlerle doğrulanır.
  (`System.DirectoryServices.Protocols` Windows'ta SChannel'a, Linux'ta OpenLDAP'a dayandığı için iki ortamda
  farklı davranır; bu yüzden seçilmedi.)

## Sertifika doğrulama kuralları

Hiçbiri ayarla kapatılamaz; doğrulamayı atlatan bir kod yolu yoktur.

| Kural | Sonuç |
| --- | --- |
| Sertifika `ActiveDirectory:ServerFqdn` adına verilmiş olmalı (SAN) | Ad uyuşmazsa bağlantı reddedilir. IP adresiyle bağlanılamaz; ayar DNS adı ister. |
| Zincir güvenilir bir köke ulaşmalı | `TrustedCaCertificatePath` boşsa sunucunun (Windows) güven deposu kullanılır. Doluysa zincir **yalnızca** o CA'ya ulaşmalı; sunucunun gönderdiği ara sertifikalar zincir kurmak için kullanılır ama güven vermez. |
| Geçerlilik tarihleri | Süresi dolmuş veya henüz geçerli olmayan sertifika reddedilir. |
| Kullanım amacı | Sertifika "Server Authentication" (1.3.6.1.5.5.7.3.1) kullanımına izin vermeli. |
| İptal (revocation) | `CheckCertificateRevocation=true` (varsayılan) ise CRL/OCSP kontrol edilir; iptal durumu öğrenilemezse bağlantı **reddedilir** (fail-closed). |

Reddedilen sertifika için sunucu loguna uyarı yazılır: sunucu, ret nedeni, sertifikanın subject ve issuer
bilgisi. Bağlantı hataları dört türe ayrılır: sertifika reddi, TLS el sıkışması hatası (ör. port LDAPS
konuşmuyor), erişilemiyor ve zaman aşımı. Hepsinde yeni giriş **reddedilir** (fail-closed).

## Giriş (7. gün)

`POST /api/auth/login`, gövde JSON: `{ "userName": "ayse.yilmaz", "password": "…" }`.

1. İstek doğrulanır: kullanıcı adı ve parola zorunlu, en fazla 256 karakter, kullanıcı adında kontrol karakteri
   yok. Hatalar Türkçe ve alan bazında döner (`400`).
2. Kullanıcı adı `ayse.yilmaz` veya `ayse.yilmaz@<Domain>` olarak yazılabilir (yalnızca yapılandırılan domain).
   `DOMAIN\kullanici`, başka domain, AD'nin oturum adında izin vermediği karakterler (`" / \ [ ] : ; | = , + * ? < > @`)
   ve 20 karakterden uzun adlar dizine hiç sorulmadan reddedilir. Boş parola da dizine gönderilmez: boş parolalı
   bind AD'de anonim bind sayılır.
3. LDAPS bağlantısı açılır (yukarıdaki sertifika kuralları), `kullanici@Domain` ile **simple bind** yapılır.
   Parolayı AD doğrular; uygulama parolayı hiçbir yerde saklamaz, karşılaştırmaz, loglamaz.
4. Aynı bağlantıda, kullanıcının kendi kimliğiyle, hesap `BaseDn` altında `sAMAccountName` ile aranır. Arama
   değeri LDAP filtresine kaçışlanarak (RFC 4515) konur. Hesap `BaseDn` dışındaysa giriş reddedilir.
5. Hesap pasifse reddedilir; ardından grup üyeliği kontrol edilir (8. gün).

AD bind hatasındaki alt kod (`data 52e` gibi) şöyle yorumlanır. AD bu alt kodları yalnızca parola doğruysa
gösterir; yanlış parolada her hesap için aynı yanıt döner, böylece hesabın durumu parolayı bilmeyen birine
açık edilmez.

| AD alt kodu | Anlamı | Yanıt |
| --- | --- | --- |
| `525`, `52e` (ve tanınmayan her kod) | Kullanıcı yok / parola yanlış | `401` "Kullanıcı adı veya parola hatalı." |
| `533` (veya `userAccountControl` pasif bayrağı) | Hesap pasif | `403` "Hesabınızla şu anda giriş yapılamıyor." |
| `775` | Hesap kilitli | `403` (aynı mesaj) |
| `701` | Hesabın süresi dolmuş | `403` (aynı mesaj) |
| `532`, `773` | Parolanın süresi dolmuş / değiştirilmesi gerekiyor | `403` (aynı mesaj) |
| `530`, `531` | Bu saatte veya bu bilgisayardan girişe izin yok | `403` (aynı mesaj) |
| — | `Bim_Envanter` üyesi değil | `403` "Bu uygulamaya giriş yetkiniz yok." |
| — | AD'ye ulaşılamıyor, zaman aşımı, sertifika reddi, beklenmeyen yanıt | `503` "Giriş şu anda yapılamıyor." |

Yanıtlar ProblemDetails'tir ve istemcinin ayırt edebilmesi için `code` alanı taşır: `invalid_credentials`,
`account_unavailable`, `not_authorized`, `directory_unavailable`. Ret nedeni (ör. "hesap kilitli") yalnızca
sunucu loguna, kullanıcı adı ve istemci adresiyle birlikte yazılır.

Başka korumalar:

- **Deneme limiti:** istemci adresi başına 60 saniyede 10 giriş denemesi (`RateLimiting:LoginPermitLimit`,
  `LoginWindowSeconds`); aşılınca `429` ve `Retry-After`. AD'nin kendi hesap kilitleme politikası ayrıca geçerlidir.
- **Yalnızca JSON:** form gönderimi kabul edilmez; başka bir siteden gönderilen form ile giriş yapılamaz.
  Gövde en fazla 8 KB'tır (`413`).
- **Parola hiçbir yerde kalmaz:** veritabanına, loga, çereze yazılmaz. Testler, giriş denemelerinden sonra log
  dosyasında parolanın geçmediğini denetler.

Başarılı girişte:

- `AdminUsers` tablosunda kullanıcı AD `objectGUID`'i ile bulunur veya oluşturulur; ilk/son giriş zamanı, oturum
  adı ve görünen adı güncellenir. Giriş yapan yöneticiler (`AdminUsers`) zimmetlenen çalışanlardan (`Employees`)
  ayrı tutulur.
- Aynı transaction içinde `AuditLogs`'a `SignedIn` kaydı yazılır (kullanıcı, istemci adresi, correlation ID).
- Tarayıcıya `__Host-EnterpriseInventory` oturum çerezi verilir: HttpOnly, Secure, SameSite=Strict, `Path=/`,
  tarayıcı kapanınca silinir, 20 dakika işlem yapılmazsa geçersizleşir. Çerez kullanıcıya `Administrator`
  rolünü verir. Sunucu taraflı oturum, çıkış, mutlak oturum süresi, CSRF koruması ve düzenli yetki kontrolü
  9. günde eklenir.

## Bim_Envanter yetkisi (8. gün)

- Yetki **grup SID'i** (`AllowedGroupSid`) ile verilir; grup adı yetki için kullanılmaz. Başka bir OU'da aynı
  adı taşıyan grup (`Bim_Envanter`) yetki vermez.
- `NestedGroupPolicy` açıkça seçilmelidir; örnek ayarlarda `DirectMembershipOnly` vardır:
  - `DirectMembershipOnly`: kullanıcı gruba **doğrudan** üye olmalı. Grup, SID'i ile domain kökünden aranır ve
    kullanıcının `memberOf` listesinde o grubun DN'i aranır. Grubun kullanıcının birincil grubu olması
    (`primaryGroupID`, aynı domain) da doğrudan üyelik sayılır. Başka bir grup üzerinden üyelik yetki vermez.
    Kimin girebileceği, grubun üye listesine bakılarak görülebilir; bu yüzden önerilen budur.
  - `IncludeNested`: AD'nin hesapladığı `tokenGroups` (kullanıcının iç içe dahil tüm güvenlik grupları) içinde
    grup SID'i aranır.
- Yapılandırılan SID ile bir grup bulunamazsa kimse giremez ve sunucu loguna hata yazılır.
- Üye olmayan kullanıcı parolası doğru olsa da `403` alır ve çerez verilmez. Çerez olmadan tüm korumalı API uçları
  `401` döner (varsayılan olarak kapalı yetkilendirme, bkz. [`api.md`](api.md)). SignalR Hub'ları eklendiğinde aynı
  kural onlara da uygulanır.
- Kullanıcı aramaları girişi yapan kullanıcının kendi kimliğiyle yapılır; servis hesabı girişte kullanılmaz.
  Servis hesabı 9. günde, açık oturumların grup üyeliğini düzenli aralıklarla yeniden kontrol etmek için
  kullanılacak.

## Ayarlar ve başlangıç denetimi

`ActiveDirectory` bölümü uygulama başlarken doğrulanır (`ValidateOnStart`); hatalıysa uygulama hangi anahtarın
neden hatalı olduğunu yazarak durur.

| Anahtar | Kural |
| --- | --- |
| `Mode` | `Ldap` (varsayılan) veya `Fake`. `Fake` yalnızca `Development` ortamında kabul edilir (bkz. aşağı). |
| `Domain` | Domain'in DNS adı, ör. `ornek.local`. Kullanıcılar `kullanici@Domain` ile doğrulanır. |
| `ServerFqdn` | Domain controller'ın tam DNS adı. Şema (`ldaps://`), port veya IP adresi kabul edilmez. |
| `Port` / `UseLdaps` | 1–65535 / mutlaka `true`. |
| `BaseDn` | DN biçiminde ve domain'in içinde olmalı, ör. `DC=ornek,DC=local` veya `OU=Personel,DC=ornek,DC=local`. |
| `AllowedGroupSid` | `Bim_Envanter` grubunun SID'i, `S-1-5-21-…-RID` biçiminde. Grup adı yetki için kullanılmaz. |
| `NestedGroupPolicy` | Varsayılanı yoktur, açıkça `DirectMembershipOnly` veya `IncludeNested` yazılmalı (bkz. [8. gün](#bim_envanter-yetkisi-8-gün)). |
| `ServiceAccountUserName` / `ServiceAccountPassword` | Düzenli yetki kontrolü ve çalışan araması için (9. gün ve sonrası). |
| `TrustedCaCertificatePath` | İsteğe bağlı; domain controller sertifikasını veren CA'nın PEM/DER dosyası. Dosya yoksa, okunamıyorsa veya CA sertifikası değilse uygulama başlamaz. |
| `CheckCertificateRevocation` | Varsayılan `true`. |
| `ConnectTimeoutSeconds` / `OperationTimeoutSeconds` | TCP+TLS için 1–60 sn (varsayılan 10) / her bind veya arama için 1–120 sn (varsayılan 15). |

`CHANGE-ME` içeren her değer reddedilir. `appsettings.Production.json` bu yer tutucularla gelir; gerçek değerler
girilmeden üretimde uygulama başlamaz (bir test bunu dosyanın kendisi üzerinde denetler).

### Geliştirme ortamı: sahte dizin

AD bilgileri belli olmadığı için `appsettings.Development.json` `Mode=Fake` ile gelir. Sahte dizin hiçbir sunucuya
bağlanmaz. Kullanıcıları yalnızca geliştiricinin `user-secrets` deposundan okunur; repoda kullanıcı veya parola yoktur:

```bash
cd src/EnterpriseInventory.Api
dotnet user-secrets set "ActiveDirectory:FakeUsers:0:UserName" "dev.admin"
dotnet user-secrets set "ActiveDirectory:FakeUsers:0:Password" "<kendi belirlediğiniz parola>"
dotnet user-secrets set "ActiveDirectory:FakeUsers:0:DisplayName" "Geliştirici Yönetici"   # isteğe bağlı
# İsteğe bağlı: ":IsAllowedGroupMember" "false" (grup üyesi olmayan), ":IsDisabled" "true" (pasif hesap)
```

Sahte dizin gerçek dizinle aynı sonuçları üretir (yanlış parola, üye değil, pasif) ve her kullanıcıya adından
türetilen sabit bir `objectGUID`/SID verir. Parolalar sabit sürede karşılaştırılır. `Development` dışındaki her
ortamda hem `Mode=Fake` hem de `FakeUsers` ayarı uygulamayı durdurur; sınıf da Development dışında oluşturulamaz.
Geliştirme ortamında gerçek bir dizinle çalışmak için `ActiveDirectory:Mode=Ldap` ve diğer anahtarlar
`user-secrets` ile verilir.

## Health kontrolü

`/api/health` (yalnızca Administrator) içinde `active-directory` adlı kontrol:

- LDAPS bağlantısını tüm sertifika kurallarıyla açar ve RootDSE'yi kimlik bilgisi göndermeden okur.
- `BaseDn` dizinin domain'i dışında kalıyorsa bunu raporlar.
- Sorun varsa sonuç `Degraded` olur, açıklamada neden yazar (ör. sertifika reddi nedeni). Uygulama `503`
  döndürmez: AD kesintisinde açık oturumlar çalışmaya devam eder, yalnızca yeni girişler reddedilir.
- Anonim `ready` yoklamasına dahil değildir; AD'ye yalnızca yönetici isteğiyle bağlanılır.
- `Fake` modunda bağlantı kurmaz, "sahte dizin kullanılıyor" bilgisini döner.

## Test ortamı ve sonuçlar

Gerçek bir AD bağlantısını denemek için repoda bir **Samba AD test domain'i** kurulum betiği var:
[`scripts/test-ad`](../scripts/test-ad/README.md). Betik `envanter.test` domain'ini (RFC 2606 ile test için
ayrılmış ad), kendi test CA'sı ile imzalanmış `dc1.envanter.test` LDAPS sertifikasını ve test kullanıcı/gruplarını
oluşturur. Parolalar rastgele üretilir ve repo dışında kalır.

Samba AD test domain'inde (bkz. betiğin kullanıcı tablosu) ve testlerin kendi kurduğu sunucularda koşturulan
testler; hepsi geçti (birim 210, entegrasyon 131 test, SQL Server ve Samba AD açıkken art arda 3 kez):

| Test | Nerede | Sonuç |
| --- | --- | --- |
| Ayar kuralları (düz LDAP, IP adresi, eksik/yer tutucu değer, SID biçimi, `Fake` ve `FakeUsers` yalnızca Development, CA dosyası) | Unit | Geçti |
| Sertifika kararları: doğru CA, ara CA, yabancı CA, ad uyuşmazlığı, süresi dolmuş, henüz geçerli olmayan, yanlış kullanım amacı, CA olmayan imzalayan, iptal durumu bilinmeyen | Integration | Geçti |
| Gerçek TLS el sıkışması (loopback sunucu): geçerli sertifika bağlanır; yanlış ad, yabancı CA, süresi dolmuş, güven deposunda olmayan CA, iptal durumu bilinmeyen sertifika reddedilir; TLS konuşmayan port, kapalı port, yanıt vermeyen sunucu (1 sn'de zaman aşımı) | Integration | Geçti |
| Samba AD (gerçek LDAPS): doğru adla ve test CA ile bağlanır, RootDSE okunur; sertifikada olmayan bir adla (IP adresi) bağlanma ve test CA verilmeden bağlanma reddedilir; health kontrolü `Healthy`, domain dışı `BaseDn` `Degraded` | Integration (`EI_TEST_AD_*` tanımlıysa) | Geçti |
| API: üretim örnek ayarlarıyla başlamaz, `Fake` Production/Staging'de başlamaz, düz LDAP ile başlamaz, erişilemeyen AD health'i `Degraded` yapar ama `ready`'yi etkilemez | Integration | Geçti |
| **7. gün** Samba AD: doğru parola giriş yapar, AD kimliği (GUID, SID, görünen ad) döner, büyük/küçük harf ve `@domain` aynı kişiyi verir; yanlış parola ve olmayan kullanıcı `401`; pasif, süresi dolmuş, parola değiştirmesi gereken hesap doğru parolayla bile reddedilir; yanlış parolada hesap durumu açığa çıkmaz; boş parola, başka domain, `*`, filtre enjeksiyonu, `DOMAIN\kullanici` dizine hiç sorulmaz; erişilemeyen AD `503` | Integration | Geçti |
| **7. gün** API: Türkçe alan hataları (`400`), form gönderimi reddi, 8 KB üstü gövde `413` (Kestrel), erişilemeyen AD `503`, deneme limiti `429` + `Retry-After`, ret yanıtlarında çerez yok, parola log dosyasında yok; başarılı girişte çerez özellikleri (`__Host-`, Secure, HttpOnly, SameSite=Strict, kalıcı değil), çerezle korumalı uca erişim, `AdminUsers` tek kayıt + her giriş için `SignedIn` audit kaydı (correlation ID ile) | Integration (SQL Server ile) | Geçti |
| **8. gün** Samba AD: üye olmayan (`mehmet.user`), yalnızca aynı adlı tuzak gruba üye (`decoy.user`) ve `DirectMembershipOnly` altında iç içe üye (`nested.user`) parolası doğru olsa da `403`; doğrudan üye ve birincil grubu `Bim_Envanter` olan iki politikada da girer; iç içe üye yalnızca `IncludeNested` ile girer; SID tuzak gruba çevrilince yetki onu izler (ad değil SID); `BaseDn` dışındaki üye reddedilir; var olmayan SID ile kimse giremez | Integration | Geçti |
| Kasıtlı bozma (mutation) denemeleri: üyelik kontrolünü kaldırmak, grubu adla eşleştirmek, politikaları karıştırmak, boş parolayı göndermek, `BaseDn` sınırını kaldırmak, sertifika ad kontrolünü veya kullanım amacı kontrolünü kaldırmak testleri kırdı | — | Yakalandı |

### Ortam engeli: şirketin gerçek AD'si

Şirketin domain adı, domain controller'ı, CA'sı ve `Bim_Envanter` SID'i henüz belli olmadığı için **gerçek şirket
AD'sine karşı test yapılamadı; AD entegrasyonu bu yüzden henüz tamamlanmış sayılmaz.** Testler Samba AD ile yapıldı;
Samba, LDAP bind hatalarında Windows AD ile aynı alt kodları döndürür (ör. `data 52e`, `data 533`) ve
`memberOf`, `primaryGroupID`, `tokenGroups` özniteliklerini AD gibi hesaplar. Kilitli hesap (`775`), süresi dolmuş
parola (`532`) ve giriş saati kısıtı (`530`) test domain'inde kurulmadı; bunların yorumu yalnızca birim testiyle denendi. Gerçek ortamda kurulumdan önce şunlar
doğrulanmalı:

1. Domain controller'ın LDAPS sertifikası, `ServerFqdn` olarak yazılacak adı SAN alanında taşıyor olmalı.
2. Sertifikayı veren CA, IIS sunucusunun güven deposunda olmalı (domain'e üye sunucularda kurumsal CA genelde
   otomatik gelir) ya da `TrustedCaCertificatePath` ile verilmeli.
3. IIS sunucusu CA'nın CRL/OCSP adresine erişebilmeli; erişemiyorsa girişler reddedilir.
4. IIS sunucusundan domain controller'ın 636 portuna erişim açık olmalı.
5. Kurulumdan sonra `/api/health` içinde `active-directory` kontrolü `Healthy` görünmeli.
6. Gerçek `Bim_Envanter` grubunun SID'i alınmalı (ör. `Get-ADGroup Bim_Envanter | Select SID`) ve
   `NestedGroupPolicy` kararı verilmeli.
7. Bir üye, bir üye olmayan, bir pasif ve (varsa) bir iç içe üye hesapla giriş denenmeli; kullanıcıların kendi
   `memberOf`/`tokenGroups` değerlerini okuyabildiği (AD varsayılanı) doğrulanmalı.
