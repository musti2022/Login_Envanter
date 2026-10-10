# Envanter ekranları (16–20., 24., 25., 28., 29. ve 40. gün)

Kod: `src/EnterpriseInventory.Web/src/pages` (`DashboardPage`, `InventoryPage`, `AssetCreatePage`, `AssetEditPage`,
`AssetDetailPage`, `BrandsModelsPage`, `LocationsPage`), `src/EnterpriseInventory.Web/src/inventory` ve
`src/EnterpriseInventory.Web/src/definitions`. Testler: aynı klasörlerdeki `*.test.ts(x)`
(Vitest, sahte API) ve `src/EnterpriseInventory.Web/e2e` (Playwright, gerçek API ve SQL Server; Development modunda
sahte AD). Sunucu tarafı: [`assets-api.md`](assets-api.md), [`lookups-api.md`](lookups-api.md),
[`assignments-api.md`](assignments-api.md), `GET /api/dashboard/statistics`.

## Gösterge Paneli (16. gün)

- Rakamlar `GET /api/dashboard/statistics`'ten gelir: toplam, zimmetli, boşta, arızalı; altında hurda ve arşivlenmiş
  sayısı. Arşivlenmiş demirbaşlar toplamlara ve dağılımlara girmez.
- Şehir ve departman dağılımı büyükten küçüğe; son 10 demirbaş işlemi (kim, ne zaman, hangi demirbaş). İşlem satırı
  demirbaşın detayına gider.
- Her kart envanteri o duruma göre süzülmüş açar (ör. Zimmetli → `/envanter?status=Assigned`).
- 33. gün: şehir/departman satırlarında zimmetli sayısı, tür ve marka dağılımı, aylık zimmet hareketleri grafiği;
  listeler en büyük 8 satır ve "Diğer N şehir" ile sınırlı. Ayrıntılar ve tutarlılık testleri:
  [`reports.md`](reports.md).
- 34. gün: "Diğer N şehir/departman/marka" satırı Raporlar ekranındaki tam listeyi açar. Raporlar ekranı (envanter
  özeti ve zimmet hareketleri, Türkçe filtreler, Excel): [`reports.md`](reports.md#rapor-ekranı-34-gün).

## Envanter listesi (17–18. gün)

- Tablo: Demirbaş Kodu, Kullanıcı Adı, Bilgisayar Adı, Marka, Model, Seri No, Zimmet Tanımı, Lokasyon/Şehir,
  Lokasyon/Departman, Durum, İşlemler (Detay, Düzenle). Tür ve Son Değişiklik "Sütunlar" menüsünden açılır; en az bir
  sütun açık kalır. Sütun tercihi yalnızca bellekte tutulur (tarayıcı deposu kullanılmaz).
- "Excel'e aktar" (32. gün) ekrandaki listeyi aynı arama, filtre ve sıralamayla, bütün sayfalarıyla indirir; bkz.
  [`export.md`](export.md).
- Sıralama, sayfalama (10/25/50/100), arama ve filtrelerin hepsi sunucu tarafındadır ve sayfa adresinde durur:
  adres = API sorgusu. Kopyalanan bağlantı aynı listeyi açar; varsayılanlar adrese yazılmaz; elle bozulmuş değerler
  varsayılana döner.
- Arama yazmayı bırakınca (400 ms) veya Enter'la çalışır. Filtreler: Durum ve Tür (çoklu), Marka → Model, Şehir →
  Lokasyon (üst seçim değişince alt seçim temizlenir), Departman, "Arşivlenmişleri göster". Her değişiklik ilk
  sayfaya döner. Listelerde olmayan bir kimlik (başka markanın modeli, silinmiş bağlantı) adresten çıkarılır.
- Boş durumlar: hiç demirbaş yok; filtrelerle eşleşen yok ("Filtreleri temizle"); sayfa listenin sonundan sonra
  ("İlk sayfaya dön"). Yükleme hatası "Tekrar dene" ile.
- Telefonda filtreler "Filtreler" düğmesinin altına katlanır (açık filtre sayısı düğmenin adında); tablo kendi
  kutusunda yatay kayar; sayfalama düğmeleri alt satıra geçer.

## Ekleme ve düzenleme (19. gün)

- `/envanter/yeni` ve `/envanter/{id}/duzenle`. React Hook Form + Zod; sınırlar API ile aynı (kod 50, bilgisayar adı
  64, seri no 100, açıklama 1000 karakter; kontrol karakteri yok). Zorunlular: kod, tür, durum, marka, model, şehir,
  departman.
- Seri No alanının altında "Boşluklar kaldırılır, harfler büyük harfle kaydedilir." yazar; API seri numarasını böyle
  saklar ve kayıttan sonra form API'nin döndürdüğü değeri gösterir.
- Yeni kayıtta yalnızca aktif tanımlar seçilebilir. Düzenlemede demirbaşın mevcut değeri pasif olsa da listede kalır
  ("(pasif)"), çünkü API mevcut değeri korumaya izin verir.
- Durum: Boşta, Arızalı, Hurda. "Zimmetli" yalnızca zimmet işlemiyle verilir; zimmetli demirbaşın durumu formda
  kilitlidir.
- Eksik marka, model, şehir, lokasyon veya departman formdan çıkmadan "+" ile eklenebilir (`POST /api/brands` …).
- API'nin alan hataları (`400`, `409 duplicate_value`) ilgili alanın altında görünür ve ilk hatalı alana odaklanılır.
  Diğer hatalar formun üstünde başlık, açıklama ve hata kodu (correlation ID) ile gösterilir; form içeriği korunur.
- **Eşzamanlılık:** düzenleme, başladığı andaki `rowVersion` ile kaydedilir. Sayfa arka planda demirbaşı yeniden
  çekse bile bu sürüm değişmez; böylece başkasının araya giren değişikliği sessizce ezilmez. Sunucu `409
  concurrency_conflict` döner, form Türkçe uyarı ve "Güncel kaydı yükle" gösterir; yükleyince form güncel değerlerle
  yeniden başlar ve kullanıcı değişikliğini tekrar yapar.
- Arşivlenmiş demirbaş düzenlenemez. Kayıttan sonra listeler, geçmiş ve gösterge paneli yeniden çekilir; demirbaşın
  detayı açılır.

## Detay ve arşivleme (20. gün)

- `/envanter/{id}`: demirbaş bilgileri, konum, aktif zimmet (kişi, zimmet tanımı, tarih, zimmetleyen), oluşturan ve
  son değiştiren. Boş değerler "—" (ekran okuyucuya "Boş").
- Geçmiş: en yeni üstte, 10'ar kayıt; işlem, kullanıcı, zaman ve değişen alanlar Türkçe adlarıyla "eski → yeni".
- "Arşivle" onay ister ve ekranda gösterilen sürümün `rowVersion`'ı ile gönderilir. Bu arada kayıt değiştiyse
  arşivlenmez; uyarı çıkar ve sayfa güncel kaydı gösterir. Zimmetli demirbaş arşivlenemez (önce iade). Arşivlenmiş
  demirbaş yalnızca görüntülenir.
- Durumlar: yükleniyor; bulunamadı (`404`, yeniden denenmez); yükleme hatası ("Tekrar dene"); geçmiş boş; geçmiş
  yüklenemedi.

## Zimmet ver ve iade al (24. gün)

Detay sayfasında, arşivlenmemiş demirbaşta:

- **Zimmet Ver** (yalnızca Boşta demirbaşta; Arızalı/Hurda'da kapalı ve nedeni ipucunda): pencerede "Çalışan"
  kutusu Active Directory'de arar (`GET /api/employees/search`). Arama en az 2 harf ister, yazma bitince 300 ms
  bekler, sonuçları sunucu süzdüğü için tarayıcıda yeniden süzmez. Seçenekte görünen ad, kullanıcı adı, departman
  ve unvan görünür. `Bim_Envanter` üyesi olması gerekmez; pasif hesaplar listelenmez. Sonuç sınırı aşılırsa "İlk
  sonuçlar gösteriliyor; ... daha ayrıntılı yazın", dizine ulaşılamazsa "Çalışan dizinine şu anda ulaşılamıyor"
  yazar. "Zimmet Tanımı" zorunlu (en fazla 500), "Not" isteğe bağlı (en fazla 1000). Gönderim, ekranda gösterilen
  sürümün `rowVersion`'ı ile yapılır.
- **İade Al** (zimmetli demirbaşta "Zimmet Ver" yerine): kimden iade alınacağını söyleyen onay penceresi.
- Başarılı işlemden sonra sunucunun döndürdüğü güncel kayıt gösterilir ("DMR-0001, Ali Kaya adlı çalışana
  zimmetlendi." / "DMR-0001 iade alındı."); liste, geçmişler ve gösterge paneli yeniden alınır.
- Çakışma (`409 concurrency_conflict`) veya demirbaşın bu arada zimmetlenmesi, arızalı yapılması, arşivlenmesi:
  pencerede Türkçe uyarı, gönder düğmesi kapanır ve sayfa güncel kaydı gösterir; veri ezilmez. Pasif hesap
  (`employee_inactive`) uyarısından sonra başka çalışan seçilebilir. Alan hataları alanın altında gösterilir.
- **Zimmet geçmişi** kartı (`GET /api/assets/{id}/assignments`): en yeni dönem üstte, 10'ar kayıt; kişi,
  departman, zimmet ve iade tarihi/yapan, tanım, not; "Zimmette" / "İade alındı" etiketi. Audit geçmişinde
  zimmet ve iade kayıtları kişi adı, kullanıcı adı, tanım ve tarihlerle Türkçe gösterilir; AD GUID'i ve iç
  kimlikler gösterilmez.

## Konum değiştirme (25. gün)

- Detay sayfasındaki "Konum" kartında **Konum Değiştir** (arşivlenmiş demirbaşta yok). Pencere demirbaşın şu anki
  şehir, lokasyon ve departmanıyla açılır; hiçbir şey değişmeden "Konumu Kaydet" kapalıdır.
- Listeler aktif tanımları sunar; demirbaşın zaten taşıdığı, sonradan pasif yapılmış değer "(pasif)" etiketiyle
  seçili kalır. Lokasyon yalnızca seçilen şehrin lokasyonlarından seçilir; şehir değişince lokasyon boşalır.
- `PUT /api/assets/{id}/location` ekranda gösterilen sürümün `rowVersion`'ı ile gönderilir. Sunucunun alan
  hatası (ör. pasif departman) alanın altında, çakışma uyarısı pencerede gösterilir; çakışmada sayfa güncel
  kaydı gösterir ve gönderim kapanır. Başarılıysa "DMR-0001 konumu değiştirildi." ve geçmişte "Konumu değişti:
  Şehir: İzmir → Bursa" kaydı görünür.
- Tam düzenleme formu da şehir/lokasyon/departmanı değiştirebilir; iki yol da aynı sunucu kurallarından geçer.

## Zimmetler (40. gün)

- Menüdeki "Zimmetler", şu an zimmetli demirbaşları listeler: envanter tablosunun aynısı (Kullanıcı Adı ve Zimmet
  Tanımı sütunlarıyla), aynı arama, filtre, sıralama, sayfalama ve Excel'e aktarma; istek her zaman
  `status=Assigned` taşır. Durum filtresi ve "Arşivlenmişleri göster" yoktur, adresteki durum ve arşiv değerleri yok
  sayılır.
- Zimmet verme ve iade alma demirbaşın sayfasındadır; satır oraya gider. "Zimmet hareketleri" düğmesi dönemlik
  zimmet/iade raporunu açar ([`reports.md`](reports.md)). Zimmetli demirbaş yoksa "Envantere git" önerilir.

## Tanımlar (40. gün)

- **Marka ve Modeller** (`/tanimlar/marka-model`) ve **Lokasyonlar** (`/tanimlar/lokasyonlar`: şehirler,
  departmanlar, lokasyonlar). Her liste adı ve durumu (Aktif/Pasif) gösterir; model ve lokasyon listesinde üst kayıt
  sütunu ve onunla süzme vardır.
- "Yeni marka/model/…" ekleme penceresini açar (demirbaş formundaki pencerenin aynısı). Yeni model ve lokasyon için
  önce aktif bir marka/şehir seçilir; seçilmeden düğme kapalıdır.
- Kalem simgesi düzenleme penceresini açar: ad ve Aktif/Pasif anahtarı. Modelin markası ve lokasyonun şehri
  değiştirilemez (pencerede yazar). Pencere listede okunan `rowVersion`'ı gönderir.
- Başkası arada değiştirdiyse "Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi." uyarısı çıkar,
  Kaydet kapanır ve liste yeniden okunur; kullanıcı kaydı güncel haliyle yeniden açar. Aynı ad ve pasif üst kayıt
  hataları ilgili alanın altında gösterilir. Tanımlar silinmez; sayfadaki not bunu ve pasifleştirmenin etkisini
  anlatır.
- Kaydedilince bütün açık sorgular yenilenir: listelerde, filtrelerde ve raporlarda yeni ad görünür.

## Canlı yenileme (28. gün)

Başka bir kullanıcının yaptığı ekleme, düzenleme, arşivleme, zimmet, iade ve konum değişikliği; açık listeye,
gösterge paneline, detay sayfasına, geçmişe ve zimmet geçmişine sayfa yenilenmeden yansır. Düzenleme sırasında gelen
değişiklik formu ezmez, uyarı olarak gösterilir. Üst çubuktaki gösterge bağlantının durumunu söyler. Ayrıntılar ve
testler: [`realtime.md`](realtime.md#ekranların-canlı-yenilenmesi-28-gün).

Bağlantı koparsa gösterge "Yeniden bağlanıyor" der ve uygulama kendiliğinden yeniden bağlanır; bağlantı dönünce
açık ekran API'den yeniden okunur, kopukluk sırasındaki değişiklikler de görünür (29. gün). Oturum bitmişse giriş
sayfasına gidilir. Ayrıntılar: [`realtime.md`](realtime.md#yeniden-bağlanma-ve-tam-eşitleme-29-gün).

## Test sonuçları

| Kapsam | Araç | Sonuç |
| --- | --- | --- |
| Web birim/bileşen testleri (giriş, yerleşim, panel, liste, filtreler, form, detay) | Vitest | 91/91 geçti |
| Tanım listeleri API'si (sıralama, filtre, ekleme, audit, aynı ad, doğrulama, CSRF, 401/403) | xUnit + SQL Server | 55/55 geçti |
| Tüm .NET testleri | xUnit | 326 birim + 394 entegrasyon geçti |
| Tarayıcı testleri: giriş/çıkış, panel = API, filtre/arama/sıralama/sayfalama = API, arşiv listesi, formdan ekleme, aynı kod, eski sürümle düzenleme reddi ve yeniden yapma, formdan tanım ekleme, 390 px telefon görünümü, bulunamadı sayfaları | Playwright (Chromium) | 19/19 geçti (20. gün) |
| **24. gün** Zimmet ver/iade et (Vitest): AD araması ve seçim, gövde ve CSRF, kırpılmış tanım, zimmetli kişi ve geçmiş; boş alan hataları; 2 harften önce arama yok; dizine ulaşılamıyor; pasif hesap sonrası yeniden seçim; çakışmada uyarı ve yenileme; arızalıda kapalı düğme; onaylı iade ve geçmişte kalan dönem; iade çakışması; audit'te zimmet/iade alanları | Vitest | 100/100 geçti (tüm web testleri) |
| **25. gün** Konum değiştirme (Vitest): mevcut değerlerle açılış, değişiklik yokken kapalı kaydet, pasif lokasyonun yeni seçim olarak sunulmaması, şehir değişince lokasyonun boşalması, gövde ve CSRF, sunucu alan hatası, çakışmada uyarı ve yenileme, sonradan pasif yapılan mevcut değerin korunması, arşivlenmişte düğme yok | Vitest | 105/105 geçti (tüm web testleri) |
| **25. gün** Konum değiştirme (tarayıcı): başka şehir, o şehrin lokasyonu ve departmana taşıma; yalnızca seçilen şehrin lokasyonları sunulur; geçmişte "Şehir: … → …"; API'de yeni değerler. Başka şehrin lokasyonunu doğrudan gönderen istek `400` ve Türkçe alan mesajı, kayıt değişmez | Playwright (Chromium) | 24/24 geçti (tüm tarayıcı testleri) |
| **40. gün** Tanımlar ve Zimmetler (Vitest): marka ve modellerin durumlarıyla listesi; pasif markada ve seçimsiz yeni model kapalı, seçilen markaya model ekleme; ad değiştirip pasifleştirme ve listedeki `rowVersion`'ın gönderilmesi; çakışmada uyarı, kapalı Kaydet, yeniden okunan liste; aynı ad ve pasif marka hataları alanın altında; boş ad gönderilmez; lokasyonların şehirleriyle listesi ve yeniden etkinleştirme; Zimmetler isteği `status=Assigned`, durum ve arşiv filtresi yok | Vitest | 9/9 geçti |
| **40. gün** Tanımlar ve Zimmetler (tarayıcı): ekrandan marka ve o markaya model ekleme, markanın adını değiştirip pasifleştirme, model listesinde yeni ad, audit'te `Created` ve `Updated`, yeni demirbaş formunda pasif marka yok; pencere açıkken başkası şehri değiştirirse uyarı ve onun değeri yerinde; Zimmetler menüsünde yalnızca zimmetli demirbaş ve detaya geçiş; üç ekran 390 px telefonda taşmıyor | Playwright (Chromium) | 4/4 geçti |
| **24. gün** Zimmet ver/iade et (tarayıcı, gerçek API + SQL Server, sahte AD): `Bim_Envanter` dışındaki `e2e.outsider` detay sayfasından aranıp seçilir ve zimmetlenir, API'de `Assigned`; iade alınır, API'de `Available`, zimmet geçmişinde tek dönem (iade eden `e2e.admin`); pasif hesap listelenmez; pencere açıkken başkası düzenlerse `409` uyarısı ve güncel kayıt | Playwright (Chromium) | 22/22 geçti (tüm tarayıcı testleri) |

Mutasyon denemeleri (kod bilerek bozuldu, testlerin yakaladığı görüldü, sonra geri alındı):

| Bozulan davranış | Yakalayan test |
| --- | --- |
| Filtre değişince ilk sayfaya dönmemek | Arama ve filtre testleri (2) |
| Marka değişince modeli korumak | Marka → model testi |
| Listelerde olmayan kimliği adreste bırakmak | Bağlantıdaki yabancı model testi |
| Filtreli boş sonuçta genel "Henüz demirbaş yok" demek | Filtreli boş durum testi |
| Düzenlemede arka planda gelen yeni `rowVersion`'ı göndermek | Eşzamanlılık testi |
| Çakışmadan sonra formu güncel değerlerle yenilememek | Eşzamanlılık testi |
| Lokasyonu gövdeye yazmamak | Ekleme ve düzenleme testleri |
| Geçmişte kimlik alanlarını (`brandId`) göstermek | Detay testi |
| Zimmetli demirbaşta "Arşivle"yi açık bırakmak | Zimmetli arşiv testi |
| Arşiv çakışmasında sayfayı yenilememek | Arşiv çakışması testi |
| Pasif marka altına model eklemek (API) | Tanım API testi |
| Tanım listelerini ada göre sıralamamak (API) | Türkçe sıralama testi |
| Telefonda sayfalama düğmelerinin kartın dışına taşması | 390 px Playwright testi |
| Zimmet çakışmasında sayfayı yenilememek | Zimmet çakışması testi (Vitest) |
