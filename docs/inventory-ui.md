# Envanter ekranları (16–20. gün)

Kod: `src/EnterpriseInventory.Web/src/pages` (`DashboardPage`, `InventoryPage`, `AssetCreatePage`, `AssetEditPage`,
`AssetDetailPage`) ve `src/EnterpriseInventory.Web/src/inventory`. Testler: aynı klasörlerdeki `*.test.ts(x)`
(Vitest, sahte API) ve `src/EnterpriseInventory.Web/e2e` (Playwright, gerçek API ve SQL Server; Development modunda
sahte AD). Sunucu tarafı: [`assets-api.md`](assets-api.md), [`lookups-api.md`](lookups-api.md),
`GET /api/dashboard/statistics`.

## Gösterge Paneli (16. gün)

- Rakamlar `GET /api/dashboard/statistics`'ten gelir: toplam, zimmetli, boşta, arızalı; altında hurda ve arşivlenmiş
  sayısı. Arşivlenmiş demirbaşlar toplamlara ve dağılımlara girmez.
- Şehir ve departman dağılımı büyükten küçüğe; son 10 demirbaş işlemi (kim, ne zaman, hangi demirbaş). İşlem satırı
  demirbaşın detayına gider.
- Her kart envanteri o duruma göre süzülmüş açar (ör. Zimmetli → `/envanter?status=Assigned`).

## Envanter listesi (17–18. gün)

- Tablo: Demirbaş Kodu, Kullanıcı Adı, Bilgisayar Adı, Marka, Model, Seri No, Zimmet Tanımı, Lokasyon/Şehir,
  Lokasyon/Departman, Durum, İşlemler (Detay, Düzenle). Tür ve Son Değişiklik "Sütunlar" menüsünden açılır; en az bir
  sütun açık kalır. Sütun tercihi yalnızca bellekte tutulur (tarayıcı deposu kullanılmaz).
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

## Test sonuçları

| Kapsam | Araç | Sonuç |
| --- | --- | --- |
| Web birim/bileşen testleri (giriş, yerleşim, panel, liste, filtreler, form, detay) | Vitest | 91/91 geçti |
| Tanım listeleri API'si (sıralama, filtre, ekleme, audit, aynı ad, doğrulama, CSRF, 401/403) | xUnit + SQL Server | 55/55 geçti |
| Tüm .NET testleri | xUnit | 326 birim + 394 entegrasyon geçti |
| Tarayıcı testleri: giriş/çıkış, panel = API, filtre/arama/sıralama/sayfalama = API, arşiv listesi, formdan ekleme, aynı kod, eski sürümle düzenleme reddi ve yeniden yapma, formdan tanım ekleme, 390 px telefon görünümü, bulunamadı sayfaları | Playwright (Chromium) | 19/19 geçti |

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
