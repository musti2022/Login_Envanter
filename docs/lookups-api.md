# Tanım listeleri API'si (18. gün)

Marka, model, şehir, lokasyon ve departman listeleri; filtreler ve demirbaş formu bunlardan seçer.
Kod: `src/EnterpriseInventory.Api/Lookups`, `src/EnterpriseInventory.Application/Lookups`,
`src/EnterpriseInventory.Infrastructure/Lookups`. Testler: `tests/EnterpriseInventory.IntegrationTests/Lookups`.

## Genel kurallar

- Her uç nokta oturum açmış **Administrator** ister (oturum yoksa `401`, rol yoksa `403`); bunu bir test her uç
  nokta için denetler. POST ayrıca `X-CSRF-TOKEN` ister; yoksa `400` `csrf_invalid`.
- Tanımlar silinmez (demirbaşlar ve geçmiş onlara bağlıdır), bu yüzden DELETE yoktur. Ad değiştirme ve
  pasifleştirme ekranı 25. günün işidir.
- Listeler pasif kayıtları da döner (`isActive: false`): eski demirbaşlar onları taşır ve filtrede seçilebilmeleri
  gerekir. Arayüz pasifleri "(pasif)" ekiyle gösterir; yeni kayıtlarda seçilemezler (demirbaş API'si reddeder).
- Sıralama veritabanının Türkçe kurallarıyla addır: C, Ç, D…; model ve lokasyonlarda önce üst kaydın adı.

| Adres | İşlem | Yanıt |
| --- | --- | --- |
| `GET /api/brands` | Markalar | `200` `[{ id, name, isActive }]` |
| `GET /api/models?brandId=` | Modeller (`brandId` verilirse yalnız o markanın) | `200` `[{ id, name, isActive, brandId, brandName }]` |
| `GET /api/cities` | Şehirler | `200` `[{ id, name, isActive }]` |
| `GET /api/locations?cityId=` | Lokasyonlar (`cityId` verilirse yalnız o şehrin) | `200` `[{ id, name, isActive, cityId, cityName }]` |
| `GET /api/departments` | Departmanlar | `200` `[{ id, name, isActive }]` |
| `POST /api/brands`, `/api/cities`, `/api/departments` | Ekleme: `{ "name": "…" }` | `201` yeni kayıt |
| `POST /api/models` | Ekleme: `{ "brandId": 1, "name": "…" }` | `201` yeni kayıt |
| `POST /api/locations` | Ekleme: `{ "cityId": 1, "name": "…" }` | `201` yeni kayıt |

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
