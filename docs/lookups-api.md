# Tanım listeleri API'si (18. ve 40. gün)

Marka, model, şehir, lokasyon ve departman listeleri; filtreler ve demirbaş formu bunlardan seçer. Tanımlar
eklenir, adı değiştirilir, pasifleştirilir ve yeniden etkinleştirilir (40. gün; ekranlar:
[`inventory-ui.md`](inventory-ui.md#tanımlar-40-gün)).
Kod: `src/EnterpriseInventory.Api/Lookups`, `src/EnterpriseInventory.Application/Lookups`,
`src/EnterpriseInventory.Infrastructure/Lookups`. Testler: `tests/EnterpriseInventory.IntegrationTests/Lookups`.

## Genel kurallar

- Her uç nokta oturum açmış **Administrator** ister (oturum yoksa `401`, rol yoksa `403`); bunu bir test her uç
  nokta için denetler. POST ve PUT ayrıca `X-CSRF-TOKEN` ister; yoksa `400` `csrf_invalid`.
- Tanımlar silinmez (demirbaşlar ve geçmiş onlara bağlıdır), bu yüzden DELETE yoktur; kullanılmayacak tanım
  pasifleştirilir.
- Listeler pasif kayıtları da döner (`isActive: false`): eski demirbaşlar onları taşır ve filtrede seçilebilmeleri
  gerekir. Arayüz pasifleri "(pasif)" ekiyle gösterir; yeni kayıtlarda seçilemezler (demirbaş API'si reddeder).
- Sıralama veritabanının Türkçe kurallarıyla addır: C, Ç, D…; model ve lokasyonlarda önce üst kaydın adı.

| Adres | İşlem | Yanıt |
| --- | --- | --- |
| `GET /api/brands` | Markalar | `200` `[{ id, name, isActive, rowVersion }]` |
| `GET /api/models?brandId=` | Modeller (`brandId` verilirse yalnız o markanın) | `200` `[{ id, name, isActive, brandId, brandName, rowVersion }]` |
| `GET /api/cities` | Şehirler | `200` `[{ id, name, isActive, rowVersion }]` |
| `GET /api/locations?cityId=` | Lokasyonlar (`cityId` verilirse yalnız o şehrin) | `200` `[{ id, name, isActive, cityId, cityName, rowVersion }]` |
| `GET /api/departments` | Departmanlar | `200` `[{ id, name, isActive, rowVersion }]` |
| `POST /api/brands`, `/api/cities`, `/api/departments` | Ekleme: `{ "name": "…" }` | `201` yeni kayıt |
| `POST /api/models` | Ekleme: `{ "brandId": 1, "name": "…" }` | `201` yeni kayıt |
| `POST /api/locations` | Ekleme: `{ "cityId": 1, "name": "…" }` | `201` yeni kayıt |
| `PUT /api/brands/{id}`, `/api/models/{id}`, `/api/cities/{id}`, `/api/locations/{id}`, `/api/departments/{id}` | Değiştirme: `{ "name": "…", "isActive": true, "rowVersion": "…" }` | `200` kaydedilen hali (yeni `rowVersion` ile) |

`brandId` veya `cityId` filtresi pozitif değilse `400` (alan hatası `brandId`/`cityId`); sayı değilse `400`.

## Ekleme kuralları

| Durum | Yanıt |
| --- | --- |
| Ad boş, 100 karakterden uzun veya kontrol karakteri içeriyor | `400`, `errors.name` |
| `brandId`/`cityId` yok, sıfır veya negatif | `400`, `errors.brandId`/`errors.cityId` |
| Marka/şehir bulunamadı veya pasif | `400`, "Seçilen marka bulunamadı." / "Seçilen marka pasif; yeni seçimlerde kullanılamaz." |
| Ad zaten var (pasif kayıtlar dahil; model için aynı markada, lokasyon için aynı şehirde) | `409` `duplicate_value`, `errors.name` |

- Ad baştaki ve sondaki boşluklardan arındırılır.
- Aynılık, benzersiz indekslerle aynı Türkçe kurala göre büyük/küçük harfe bakmaz: "dell" = "Dell",
  "ÇİNE" = "Çine". Türkçede "I" ile "i" farklı harflerdir, bu yüzden "LATITUDE" ile "Latitude" farklı sayılır.
- Kontrol ile kayıt arasında başka bir istek aynı adı alırsa benzersiz indeks yakalar ve yine `409` döner.
  (Mutasyon denemesi: ön kontrol kaldırıldığında testler indeks sayesinde geçmeye devam etti.)
- Her ekleme, kayıtla aynı transaction'da bir audit kaydı yazar: varlık adı (`Brand`, `AssetModel`, `City`,
  `Location`, `Department`), kimlik, `Created`, yeni değerler (`name`, `isActive`, varsa `brandId`/`brandName` veya
  `cityId`/`cityName`), kullanıcı, zaman ve correlation ID.

## Değiştirme kuralları (40. gün)

`rowVersion`, listede okunan değerdir (base64). Ad ve durum birlikte gönderilir; modelin markası ve lokasyonun şehri
değişmez (gövdede gönderilse de yok sayılır), çünkü onları kullanan demirbaşların marka/model ve şehir/lokasyon
tutarlılığı buna dayanır.

| Durum | Yanıt |
| --- | --- |
| Başarılı | `200`, kaydedilen hali ve yeni `rowVersion` |
| Ad boş, 100 karakterden uzun, kontrol karakteri içeriyor; `isActive` yok; `rowVersion` yok veya geçersiz | `400`, alan bazında Türkçe mesaj |
| Kayıt okunduktan sonra başkası değiştirdi (`rowVersion` uyuşmuyor) | `409` `concurrency_conflict` "Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi." Hiçbir şey kaydedilmez |
| Yeni ad başka bir tanımda var (pasifler dahil, büyük/küçük harfe bakmadan; model için aynı markada, lokasyon için aynı şehirde) | `409` `duplicate_value`, `errors.name` |
| Markası pasif modeli veya şehri pasif lokasyonu etkinleştirme | `400`, `errors.isActive` "Markası pasif olan model etkinleştirilemez; önce markayı etkinleştirin." |
| Kimlik yok | `404` `lookup_not_found` |

- Yalnızca harf büyüklüğünü değiştirmek ("dell" → "Dell") ad değişikliği sayılır ve kaydedilir.
- Aynı değerler gönderilirse hiçbir şey yazılmaz, `rowVersion` değişmez, audit kaydı oluşmaz.
- Değişiklik ve audit kaydı tek `SaveChanges`'ta (tek transaction) yazılır: varlık adı, kimlik, `Updated`, yalnızca
  değişen alanların eski ve yeni değerleri (`name`, `isActive`), kullanıcı, zaman, correlation ID. `UpdatedAt` ve
  `UpdatedBy` oturumdaki kullanıcıdan doldurulur.
- İstemcinin `rowVersion`'ı `UPDATE`'in `WHERE` koşuluna girer: okuma ile kayıt arasında başkası kaydederse
  veritabanı yakalar ve `409` döner; sessiz üzerine yazma olmaz.
- Pasifleştirme demirbaşları değiştirmez: tanımı kullanan demirbaşlar onu taşımaya devam eder, listelerde "(pasif)"
  ekiyle görünür; yeni kayıtta seçilemez (demirbaş API'si reddeder). Markayı pasifleştirmek modellerini
  pasifleştirmez; ama markası pasif olan modeller de yeni kayıtta seçilemez.
- Ad değişikliği canlı bildirim yayımlamaz (SignalR olayları demirbaş içindir); değişikliği yapan ekran listeleri
  yeniden okur, diğer ekranlar bir sonraki okumada yeni adı görür.

Testler (`LookupUpdateTests`, SQL Server): ad değiştirme ve pasifleştirme her biri için audit'te eski/yeni değer;
eski `rowVersion` ile `409` ve diğer kullanıcının değeri yerinde; aynı sürümle eşzamanlı iki değişiklikten tam biri
kaydedilir (5 tur); aynı değerler audit yazmaz; pasif tanımın adı büyük harfle de alınamaz, kendi adının büyük
harflisi alınır; model adı yalnız kendi markasında benzersiz ve gövdedeki marka modeli taşımaz; pasif marka/şehir
altındaki model/lokasyon etkinleştirilemez; geçersiz gövde alan bazında `400`; bilinmeyen kimlik `404`; CSRF'siz
`400`. Rowversion denetimi kaldırılınca eski sürüm testi kırıldı (mutasyon denemesi).
