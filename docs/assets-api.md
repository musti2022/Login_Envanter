# Demirbaş API'si (11–15. gün)

Kod: `src/EnterpriseInventory.Api/Assets`, `src/EnterpriseInventory.Application/Assets`,
`src/EnterpriseInventory.Infrastructure/Assets`. Testler: `tests/EnterpriseInventory.IntegrationTests/Assets` ve
`tests/EnterpriseInventory.UnitTests/Assets`.

## Genel kurallar

- Her uç nokta oturum açmış **Administrator** ister (oturum yoksa `401`, rol yoksa `403`). Bunu bir test her uç
  nokta için denetler.
- Durum değiştiren istekler (POST, PUT, DELETE) `X-CSRF-TOKEN` başlığı ister; yoksa `400` `csrf_invalid`.
- Gövdeler yalnızca JSON'dur (`application/json`, en fazla 16 KB); form gövdesi `415` alır.
- Enum değerleri adıyla gönderilir ve döner, büyük/küçük harf fark etmez: `"Laptop"`, `"faulty"`. Sayı (`"2"`)
  ve virgüllü değer (`"Laptop, Desktop"`) kabul edilmez.
  - Türler: `Desktop`, `Laptop`, `Monitor`, `Printer`, `Phone`, `Tablet`, `Server`, `NetworkDevice`, `Peripheral`, `Other`.
  - Durumlar: `Available` (Boşta), `Assigned` (Zimmetli), `Faulty` (Arızalı), `Retired` (Hurda).
- Hatalar ProblemDetails'tır: Türkçe `title` ve `detail`, makinenin okuyacağı `code` ve `correlationId`
  (bkz. [`api.md`](api.md#hata-yanıtları)). Alan hatalarında `errors` nesnesi alan adıyla (camelCase) gelir.

| Adres | İşlem | Başarılı yanıt |
| --- | --- | --- |
| `GET /api/assets` | Liste: arama, filtre, sıralama, sayfalama | `200` sayfa |
| `GET /api/assets/{id}` | Detay (arşivlenmiş dahil) | `200` |
| `POST /api/assets` | Ekleme | `201`, `Location: /api/assets/{id}` ve detay |
| `PUT /api/assets/{id}` | Güncelleme (RowVersion ile) | `200` güncel detay |
| `DELETE /api/assets/{id}?rowVersion=…` | Arşivleme (soft delete) | `204` |
| `GET /api/assets/{id}/history` | Audit geçmişi | `200` sayfa |

## Listeleme: `GET /api/assets`

| Parametre | Açıklama |
| --- | --- |
| `page`, `pageSize` | 1–100000 ve 1–100; varsayılan 1 ve 25. Son sayfadan sonrası boş liste döner, toplamlar korunur. |
| `search` | En fazla 100 karakter ve 5 kelime. Her kelime şu sütunlardan birinde geçmelidir: demirbaş kodu, bilgisayar adı, seri no, marka, model, şehir, departman, lokasyon, zimmetli kişinin kullanıcı adı ve görünen adı, zimmet tanımı. |
| `status` | Tekrarlanabilir: `status=Faulty&status=Retired` (biri tutması yeterli). |
| `assetType` | Tekrarlanabilir, `status` gibi. |
| `brandId`, `modelId`, `cityId`, `departmentId`, `locationId` | Pozitif kimlik. |
| `archived` | `true`: yalnızca arşivlenmişler. Verilmezse veya `false` ise yalnızca arşivlenmemişler. |
| `sortBy` | `assetCode` (varsayılan), `computerName`, `serialNumber`, `brandName`, `modelName`, `assetType`, `status`, `cityName`, `departmentName`, `locationName`, `assignedUserName`, `assignedDisplayName`, `createdAt`, `updatedAt` |
| `sortDirection` | `asc` (varsayılan) veya `desc` |

- Verilen tüm filtreler birlikte uygulanır (VE). Toplam (`totalCount`, `totalPages`) yalnızca eşleşen kayıtları sayar.
- **Arama** büyük/küçük harfe ve aksana duyarsızdır; `i`, `ı`, `İ`, `I` aynı harf sayılır. Örneğin `canta`,
  `çanta`'yı; `pc-ist`, `PC-IST-01`'i; `ISTANBUL`, `İstanbul`'u bulur. Bunun için arama sütunları
  `Latin1_General_100_CI_AI` ile karşılaştırılır; veritabanının `Turkish_CI_AS` kuralıyla `pc-ist` `PC-IST`'i
  bulmazdı (Türkçede `i` ile `I` farklı harftir). Benzersizlik ve sıralama Türkçe kurallarla kalır.
- Aramada `%`, `_` ve `[` sıradan karakterdir; joker karakter olarak çalışmaz.
- **Sıralama** Türkçe kurallara uyar (`Ankara`, `İstanbul`, `İzmir`). Boş değerler artan sıralamada başta,
  azalanda sonda yer alır. `status` ve `assetType` Türkçe etiketlerine göre değil, değer sırasına göre sıralanır
  (yukarıdaki listelerin sırası). Eşitlikte demirbaş kodu ve kimlik kullanılır; böylece sayfalar arasında kayıt
  kaybolmaz ve tekrar etmez. `desc`, `asc` sırasının tam tersidir.
- Hatalı değer `400` ve alan bazında Türkçe mesaj alır, örneğin `errors.status`: "Durum filtresi geçersiz.
  Geçerli değerler: Available, Assigned, Faulty, Retired." Türü yanlış değer (`brandId=dell`, `archived=evet`)
  de `400` alır.

Yanıt:

```json
{
  "items": [
    {
      "id": 12, "assetCode": "DMR-0003", "computerName": "PC-DMR-0003", "brandName": "Dell",
      "modelName": "Latitude 5440", "serialNumber": "SN-DMR-0003", "assetType": "Laptop", "status": "Assigned",
      "cityName": "İstanbul", "departmentName": "Bilgi İşlem", "locationName": "Merkez Ofis",
      "assignedUserName": "ayse.yilmaz", "assignedDisplayName": "Ayşe Yılmaz",
      "assignmentDescription": "Dizüstü + çanta", "isArchived": false,
      "createdAt": "2026-10-09T06:00:00+00:00", "updatedAt": null
    }
  ],
  "page": 1, "pageSize": 25, "totalCount": 7, "totalPages": 1
}
```

Arama `LIKE '%…%'` ile yapıldığı için indeks kullanmaz. Birkaç bin ile on binlerce kayıt için yeterlidir; çok
daha büyük envanterde full-text arama değerlendirilmelidir.

## Detay: `GET /api/assets/{id}`

Demirbaşın tüm alanları; marka, model, şehir, departman ve lokasyon `{ "id", "name" }` olarak; aktif zimmet
(`activeAssignment`: kullanıcı adı, görünen ad, zimmet tanımı, zimmet tarihi ve zimmetleyen); `isArchived`;
`createdAt/By`, `updatedAt/By` ve `rowVersion` (8 baytlık SQL Server `rowversion`, base64). Arşivlenmiş
demirbaş da okunur. Olmayan kimlik `404` `asset_not_found` "Demirbaş bulunamadı." alır.

## Ekleme: `POST /api/assets`

```json
{
  "assetCode": "DMR-0100", "assetType": "Laptop", "status": "Available",
  "modelId": 3, "cityId": 1, "departmentId": 2, "locationId": 5,
  "computerName": "PC-IST-100", "serialNumber": "5CG1234XYZ", "description": "Yeni personel"
}
```

- Zorunlu: `assetCode`, `assetType`, `modelId`, `cityId`, `departmentId`. Marka gönderilmez, her zaman modelin
  markasıdır.
- `status` isteğe bağlıdır (varsayılan `Available`). `Faulty` veya `Retired` verilebilir; `Assigned` verilemez,
  zimmet yalnızca zimmet işlemiyle olur.
- Metinlerin başı ve sonundaki boşluklar silinir; boş metin `null` saklanır. Uzunluklar: kod 50, bilgisayar adı
  64, seri no 100, açıklama 1000 karakter. Kontrol karakteri reddedilir.
- Seçilen model, marka, şehir, departman ve lokasyon var ve aktif olmalıdır; lokasyon seçilen şehirde olmalıdır.
  Aksi durumda `400` ve ilgili alanda Türkçe mesaj, örneğin `errors.modelId`: "Seçilen model pasif; yeni
  seçimlerde kullanılamaz."
- Demirbaş kodu ve seri numarası tüm demirbaşlarda (arşivlenmişler dahil) benzersizdir; büyük/küçük harf
  fark etmez. Çakışma `409` `duplicate_value` ve `errors.assetCode` / `errors.serialNumber`. Aynı kodla aynı
  anda gelen istekler için de tek kayıt oluşur (benzersiz indeks yakalar). Seri numarası boş olan birden çok
  demirbaş olabilir.
- Kayıt ve `Created` audit kaydı tek transaction'da yazılır.

## Güncelleme: `PUT /api/assets/{id}`

Gövde, eklemedeki tüm alanlar ve okunan `rowVersion`'dır. Gönderilmeyen isteğe bağlı alanlar temizlenir
(tam değiştirme). `status` güncellemede zorunludur.

- **Çakışma:** `rowVersion`, kaydın şu anki sürümü değilse (başka biri arada kaydetti) `409`
  `concurrency_conflict` döner: "Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi." /
  "Değişiklikleriniz kaydedilmedi. Kaydı yeniden açıp güncel bilgiler üzerinde tekrar deneyin." Hiçbir alan
  değişmez, audit yazılmaz. Sürüm kontrolü kaydetme anında veritabanında da tekrarlanır (`UPDATE … WHERE
  RowVersion = …`); aynı sürüm üzerinde aynı anda gelen düzenlemelerden yalnızca biri kaydedilir, diğerleri
  `409` alır. Sessiz veri ezme olmaz.
- `rowVersion` eksik veya 8 baytlık base64 değilse `400` (`errors.rowVersion`).
- Zimmetli demirbaş düzenlenebilir ve `status` olarak `Assigned` gönderilir; başka bir duruma geçmek için önce
  iade gerekir (`409` `asset_assigned`). Zimmetli olmayan demirbaşa `Assigned` verilemez (`400` `errors.status`).
- Arşivlenmiş demirbaş değiştirilemez: `409` `asset_archived`.
- Kayıtta zaten seçili olup sonradan pasif yapılmış bir tanım korunabilir; yeni seçilen tanım aktif olmalıdır.
- Demirbaş kendi kodunu ve seri numarasını koruyabilir (büyük/küçük harf değişikliği dahil); başka bir
  demirbaşınkini alamaz (`409` `duplicate_value`).
- Hiçbir şey değişmiyorsa kayıt yazılmaz: `rowVersion` aynı kalır, audit oluşmaz.
- Yanıt, yeni `rowVersion` ile güncel detaydır.

## Arşivleme: `DELETE /api/assets/{id}?rowVersion=…`

Demirbaş silinmez, arşivlenir (`IsDeleted = 1`). `rowVersion` sorgu dizgisinde gönderilir ve URL-encode
edilmelidir (base64'teki `+`, `/`, `=` nedeniyle; örneğin JavaScript'te `encodeURIComponent`).

- Başarılıysa `204`. Demirbaş listeden çıkar (`archived=true` ile görülür), detayı ve geçmişi okunmaya devam eder.
- Kayıt satırı, zimmet geçmişi (iade edilmiş zimmetler) ve audit kayıtları korunur. Kodu ve seri numarası
  rezerve kalır; yeni demirbaşta kullanılamaz.
- Zimmetli demirbaş arşivlenemez: `409` `asset_assigned` "Önce demirbaşın iadesini alın, sonra tekrar deneyin."
- Zaten arşivlenmişse `409` `asset_already_archived`; eski sürümse `409` `concurrency_conflict`.
- Arşiv bayrağı ve `Archived` audit kaydı tek `SaveChanges` (tek transaction) ile yazılır.

## Geçmiş: `GET /api/assets/{id}/history`

Demirbaşın audit kayıtları, en yeni önce. `page`, `pageSize` (varsayılan 50, en fazla 100).

```json
{
  "items": [
    {
      "id": 912, "action": "Archived", "userName": "ayse.admin",
      "timestamp": "2026-10-09T08:12:40.1234567+00:00", "correlationId": "3f6c0b9e4a2d4c55a8a0f1d7c2e9b411",
      "oldValues": { "isArchived": false }, "newValues": { "isArchived": true }
    }
  ],
  "page": 1, "pageSize": 50, "totalCount": 3, "totalPages": 1
}
```

## Audit kayıtları

| İşlem | `action` | Eski / yeni değerler |
| --- | --- | --- |
| Ekleme | `Created` | — / tüm alanlar |
| Kod, tür, bilgisayar adı, seri no, açıklama, marka/model değişikliği | `Updated` | Yalnızca değişen alanlar |
| Şehir, departman veya lokasyon değişikliği | `LocationChanged` | Yalnızca değişen alanlar |
| Durum değişikliği | `StatusChanged` | `status` |
| Arşivleme | `Archived` | `isArchived` |

- Bir düzenlemede birden çok türde değişiklik olursa her tür için ayrı kayıt yazılır; hepsi aynı kullanıcı, zaman
  ve correlation ID'yi taşır ve demirbaş değişikliğiyle birlikte commit edilir.
- Tanımlar kimlik ve adıyla birlikte saklanır (`modelId` + `modelName`); tanımın adı sonradan değişse de kayıt
  okunabilir kalır.
- Kayıtta `EntityName = "Asset"`, `EntityId` (demirbaş kimliği), yapan kullanıcı (oturumdaki AD kullanıcı adı),
  UTC zaman ve isteğin correlation ID'si bulunur. Demirbaş alanları dışında bir şey (parola, token, çerez)
  yazılmaz.
- Oturum açmış kullanıcı olmadan demirbaş değiştirilemez; audit kaydı yazılamayacağı için işlem durdurulur.

## Hata kodları

| Durum | `code` | Ne zaman |
| --- | --- | --- |
| `400` | — (ValidationProblem, `errors`) | Alan veya parametre hatası |
| `400` | `csrf_invalid` | CSRF token'ı yok veya geçersiz |
| `404` | `asset_not_found` | Demirbaş yok |
| `409` | `duplicate_value` | Kod veya seri no başka demirbaşta (`errors` alanı söyler) |
| `409` | `concurrency_conflict` | `rowVersion` güncel değil |
| `409` | `asset_archived` | Arşivlenmiş demirbaşı değiştirme |
| `409` | `asset_already_archived` | Arşivlenmiş demirbaşı yeniden arşivleme |
| `409` | `asset_assigned` | Zimmetli demirbaşın durumunu değiştirme veya arşivleme |
| `409` | `status_requires_assignment`, `inactive_reference`, `location_city_mismatch`, `rule_violated` | Doğrulamadan geçip domain kuralına takılan nadir durumlar |
| `415` | — | JSON olmayan gövde |

## Testler

SQL Server 2022 (Docker, `Turkish_CI_AS`) üzerinde çalıştırıldı; son çalıştırmada birim testleri 326/326 ve
entegrasyon testleri 344/344 geçti.

| Kabul ölçütü | Testler | Sonuç |
| --- | --- | --- |
| **11. gün** Pagination testleri geçer | `AssetListTests`: kod sırası, sayfalar arası boşluk/tekrar yok, son sayfadan sonrası, sınırlar, Türkçe alan hataları, sayı olmayan sayfa, tablo sütunları, detay, `404`, arşivlenmişin listede olmaması | Geçti |
| **12. gün** Geçerli kayıt oluşur; hatalı reddedilir | `AssetCreateTests`: `201` ve geri okuma, `Faulty`/`Retired` başlangıç, isteğe bağlı alanlar, `Created` audit (kullanıcı, correlation ID), her alan için Türkçe hata, yok/pasif/yanlış şehir tanımları, büyük/küçük harften bağımsız tekrar kod ve seri no, aynı anda 6 istek tek kayıt, CSRF, form gövdesi | Geçti |
| **13. gün** Çakışmada 409 alınır | `AssetUpdateTests`: güncel sürümle kaydetme ve yeni `rowVersion`, değişiklik türü başına audit, değişiklik yoksa yazma yok, eski sürüm `409` ve diğer kullanıcının verisi korunur, uydurma sürüm `409`, aynı sürümle aynı anda 5 düzenleme tek kayıt, Türkçe alan hataları, pasif tanımı koruma, kod tekrarları, zimmetli ve arşivlenmiş demirbaş, `404`, CSRF | Geçti |
| **14. gün** Arşivleme geçmişi korur | `AssetArchiveTests`: listeden çıkar ama okunur, geçmiş (`Archived`, `Updated`, `Created`) en yeni önce, zimmet geçmişi korunur, arşivlenmiş düzenlenemez/tekrar arşivlenemez/kodu kullanılamaz, zimmetli arşivlenemez, eski sürüm `409`, aynı anda düzenleme ve arşivlemeden yalnızca biri, `rowVersion` zorunlu, CSRF, geçmiş sayfalama ve hataları, `404` | Geçti |
| **15. gün** Filtre testleri geçer | `AssetFilterTests`: sütun bazında arama, çok kelime, Türkçe harf ve aksan duyarsızlığı, `%` `_` `[` sıradan karakter, her filtre ve birleşimleri, arşiv görünümü, filtreli toplamlar, her sütunda sıralama, sıralı sayfalama, Türkçe hata mesajları, tür hataları | Geçti |
| Yetki | `AssetAuthorizationTests`: altı uç noktanın her biri oturumsuz `401`, Administrator olmayan `403` | Geçti |

Testlerin hatayı gerçekten yakaladığı, kod bilerek bozularak denendi (sonra geri alındı):

| Bozulan kod | Kırılan test |
| --- | --- |
| Pasif model kontrolü kaldırıldı (12. gün) | Pasif model testi |
| Güncellemede `rowVersion` karşılaştırması kaldırıldı | Eski sürüm ve uydurma sürüm testleri (2) |
| Kaydetmedeki eşzamanlılık hatası yakalanmadı | Aynı anda 5 düzenleme testi |
| Arşiv audit kaydı yazılmadı | Geçmiş, tekrar arşivleme ve eşzamanlı arşivleme testleri (3) |
| Arama `Turkish_CI_AS` ile yapıldı | `canta`, `pc-ist`, `ınsan`, `ISTANBUL` aramaları (4) |
| Lokasyon aramadan çıkarıldı | `merkez ofis` aramaları (2) |

## Henüz yapılmayanlar

- Başarılı kayıttan sonra SignalR bildirimi (`AssetCreated`, `AssetUpdated`, `AssetArchived`, `AssetLocationChanged`).
- Zimmet ve iade uç noktaları; şimdilik testler zimmeti doğrudan veritabanına yazar.
- Marka, model, şehir, departman ve lokasyon yönetimi uç noktaları.
- Envanter tablosu ve demirbaş formu ekranları (web arayüzü).
