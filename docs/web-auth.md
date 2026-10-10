# Web arayüzü: giriş ve korumalı sayfalar (10. gün)

Kod: `src/EnterpriseInventory.Web/src/auth`, `src/EnterpriseInventory.Web/src/api/http.ts`,
`src/EnterpriseInventory.Web/src/pages/LoginPage.tsx`, `src/EnterpriseInventory.Web/src/layout/UserMenu.tsx`. Testler:
`src/EnterpriseInventory.Web/src/**/*.test.ts(x)` (Vitest) ve `src/EnterpriseInventory.Web/e2e` (Playwright).

## Akış

- Giriş sayfası `/giris`: "Kullanıcı adı", "Parola" (göster/gizle düğmesiyle) ve "Giriş Yap". Kullanıcı adı
  `ad.soyad` veya `ad.soyad@domain` olarak yazılır.
- Diğer bütün sayfalar `RequireAuth` arkasındadır. Uygulama açılırken `GET /api/auth/me` ile oturum sorulur; oturum
  yoksa kullanıcı `/giris` sayfasına gönderilir ve girişten sonra istediği sayfaya döner. Bu yalnızca ekranda neyin
  gösterileceğine karar verir; asıl koruma API'dedir: her uç oturum ve `Administrator` rolü ister
  (bkz. [`api.md`](api.md#yetkilendirme-varsayılan-olarak-kapalı)).
- Oturum bilgisi alınamazsa (API'ye ulaşılamıyor) sayfa açılmaz; "Oturum bilgisi alınamadı" ve "Tekrar dene"
  gösterilir.
- Başarılı girişte üst çubukta kullanıcının görünen adı ve "Çıkış Yap" düğmesi görünür.
- "Çıkış Yap", oturumu sunucuda bitirir (`POST /api/auth/logout`) ve giriş sayfasına döner. İstek başarısız olsa
  bile tarayıcıdaki kullanıcı bilgileri ve önbellek silinir.
- Oturum başka bir nedenle biterse (boşta kalma, mutlak süre, AD yetkisinin kalkması, başka sekmeden çıkış), bunu
  fark eden ilk API isteği `401` alır. Uygulama önbellekteki bütün verileri siler ve "Oturumunuz sona erdi. Devam
  etmek için tekrar giriş yapın." mesajıyla giriş sayfasına döner. Kurallar: [`session-security.md`](session-security.md).

## Hata mesajları

Sunucunun Türkçe başlığı ve açıklaması olduğu gibi gösterilir; teknik ayrıntı (exception, sunucu adı) gösterilmez.
Başarısız bir denemeden sonra parola alanı temizlenir, kullanıcı adı korunur.

| Durum | Mesaj |
| --- | --- |
| Boş alan (sunucuya gönderilmez) | "Kullanıcı adı zorunludur." / "Parola zorunludur." |
| Kullanıcı adı veya parola yanlış, hesap kilitli | "Kullanıcı adı veya parola hatalı." ve olası kilitlenme açıklaması |
| `Bim_Envanter` üyesi değil | "Bu uygulamaya giriş yetkiniz yok." |
| Hesap pasif, süresi dolmuş, parola değişmeli | "Hesabınızla şu anda giriş yapılamıyor." |
| AD'ye ulaşılamıyor, giriş kaydedilemedi | "Giriş şu anda yapılamıyor." |
| Çok fazla deneme (`429`) | "Çok fazla giriş denemesi yapıldı. Lütfen N saniye sonra tekrar deneyin." |
| Sunucuya ulaşılamıyor | "Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip tekrar deneyin." |
| Sunucunun alan hatası (`400`) | İlgili alanın altında sunucunun mesajı |

## Tarayıcıda ne saklanır

- Oturum çerezi ve CSRF çerezi HttpOnly'dir; JavaScript ikisini de okuyamaz.
- CSRF token'ının istek yarısı yalnızca bellekte tutulur. İlk durum değiştiren istekten önce `GET /api/auth/csrf`
  ile alınır, girişte yanıttaki yeni token'la değiştirilir. Sunucu token'ı reddederse (ör. girişten önce alınmış)
  yeni token alınıp istek **bir kez** tekrarlanır.
- `localStorage`, `sessionStorage`, IndexedDB ve `document.cookie` kullanılmaz. Bir birim testi uygulamanın kaynak
  kodunu bu adlar için tarar; uçtan uca test girişten sonra depoların boş olduğunu tarayıcıda doğrular. Parola
  yalnızca giriş isteğinin gövdesinde gider; formda, önbellekte veya sayfada kalmaz.

## Testler

Hepsi geçti.

| Test | Nerede | Sonuç |
| --- | --- | --- |
| İstek katmanı: okumalar CSRF token'sız, durum değiştiren istekler token'la; token bir kez alınır; reddedilen token'da yeni token ve tek tekrar; `401` oturumun bittiğini bildirir; ProblemDetails `code` ve `Retry-After` okunur | Vitest | Geçti |
| Giriş sayfası: oturumsuz ziyaretçi `/giris`'e gider; Türkçe alanlar ve `autocomplete`; boş alanlar sunucuya gitmez; `401`/`403`/`503` mesajları ve parolanın temizlenmesi; `429` bekleme süresi; sunucuya ulaşılamıyor; sunucunun alan hataları; başarılı girişte istenen sayfa, CSRF başlığı, tarayıcıda bir şey saklanmaz; oturumu olan kullanıcı Gösterge Paneli'ne gider | Vitest | Geçti |
| Çıkış: sunucuda biter (CSRF token'ıyla), önbellek silinir, giriş sayfası; başka bir istekte `401` gelince "Oturumunuz sona erdi" | Vitest | Geçti |
| Kaynak kodda tarayıcı deposu kullanımı yok | Vitest | Geçti |
| Toplam Vitest: 41 test (önceki arayüz kabuğu testleri dahil) | Vitest | Geçti |
| Oturumsuz ziyaretçi `/`, `/envanter`, `/denetim-gecmisi` adreslerinden giriş sayfasına gider; korumalı API (`/api/auth/me`, `/api/health`, olmayan adres) `401` | Playwright | Geçti |
| Yanlış parola, grup dışı kullanıcı (doğru parolayla), pasif hesap: Türkçe mesaj, oturum çerezi yok, Gösterge Paneli açılmaz | Playwright | Geçti |
| Üye kullanıcı: istediği sayfaya (`/envanter`) döner, görünen adı görünür, Gösterge Paneli açılır, `/api/auth/me` `Administrator` döner | Playwright | Geçti |
| Çerezler `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, kalıcı değil; `localStorage`/`sessionStorage`/`document.cookie` boş; parola sayfada yok; sayfa yenilenince oturum sürer | Playwright | Geçti |
| Çıkış: giriş sayfası, `/api/auth/me` `401`; çıkıştan önce kopyalanan çerezle başka bir tarayıcıda hiçbir sayfa açılmaz | Playwright | Geçti |
| Kasıtlı bozma: yönlendirmeyi kaldırmak 3, çıkışta sunucuya gitmemek 1 uçtan uca testi kırdı | — | Yakalandı |

Uçtan uca testler React'in üretim derlemesini (`vite preview`) Chromium'da açar; Vite proxy'si doğrulanmış HTTPS
ile gerçek API'ye (Development, sahte dizin, SQL Server) bağlanır. Kurulum ve çalıştırma:
[`src/EnterpriseInventory.Web/e2e/README.md`](../src/EnterpriseInventory.Web/e2e/README.md).

Sınırlar:

- Tarayıcı testleri sahte dizinle yapıldı. Gerçek AD girişi API düzeyinde Samba AD ile test edildi; şirketin gerçek
  AD'siyle henüz hiç denenmedi (bkz. [`active-directory.md`](active-directory.md#ortam-engeli-şirketin-gerçek-adsi)).
- Geliştirmede React Vite üzerinden, uçtan uca testlerde `vite preview` üzerinden sunulur. Yayında React derlemesi
  API ile aynı klasörden ve aynı origin'den sunulur; sayfa CSP'si ve önbellek kuralları
  [`api.md`](api.md#react-sayfaları)'de.
