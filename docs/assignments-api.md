# Zimmet API'si (21–23. gün)

Kod: `src/EnterpriseInventory.Application/Assets/AssetAssignments.cs`, `src/EnterpriseInventory.Application/Employees`,
`src/EnterpriseInventory.Infrastructure/Assets/AssetAssignmentStore.cs`,
`src/EnterpriseInventory.Infrastructure/ActiveDirectory/LdapEmployeeDirectory.cs`. Genel kurallar (yetki, CSRF, JSON,
hata biçimi) [`assets-api.md`](assets-api.md#genel-kurallar) ile aynıdır.

| Adres | İşlem | Başarılı yanıt |
| --- | --- | --- |
| `GET /api/employees/search?q=…` | AD'de zimmet verilecek çalışanı arama | `200` `{ items, hasMore }` |
| `POST /api/assets/{id}/assignments` | Zimmet verme | `201`, `Location: /api/assets/{id}/assignments` ve demirbaşın güncel detayı |
| `POST /api/assets/{id}/returns` | İade alma | `200` demirbaşın güncel detayı |
| `GET /api/assets/{id}/assignments?page=&pageSize=` | Zimmet geçmişi (en yeni önce) | `200` sayfa |

## Çalışan araması (21. gün)

Ayrıntılar: [`active-directory.md`](active-directory.md#çalışan-araması-21-gün). Kısaca: servis hesabıyla LDAPS,
yalnızca etkin kişi hesapları, her kelime ad/soyad/görünen ad/oturum adı/e-postanın başıyla eşleşir, en fazla 20
sonuç. **Bim_Envanter üyeliği aranmaz**: uygulamaya giremeyen her çalışan zimmet alabilir.

## Zimmet verme: `POST /api/assets/{id}/assignments` (22. gün)

```json
{
  "employeeObjectGuid": "0b3e…",
  "assignmentDescription": "Dizüstü bilgisayar + şarj adaptörü",
  "notes": "Kutusuyla teslim edildi",
  "rowVersion": "AAAAAAAAB9E="
}
```

| Alan | Kural |
| --- | --- |
| `employeeObjectGuid` | Zorunlu; çalışan aramasının döndürdüğü AD `objectGUID`. |
| `assignmentDescription` | Zorunlu "Zimmet Tanımı", en fazla 500 karakter, baştaki/sondaki boşluk kırpılır. |
| `notes` | İsteğe bağlı, en fazla 1000 karakter; yalnız boşluksa boş kaydedilir. |
| `rowVersion` | Zorunlu; demirbaşın okunduğu sürüm (detaydaki `rowVersion`). |

İşleyiş:

1. Girdi doğrulanır (Türkçe alan hataları, `400`).
2. Çalışan, gönderilen bilgilere güvenilmeden **AD'den `objectGUID` ile yeniden okunur** (transaction açılmadan,
   servis hesabıyla). Dizinde yoksa `400` (`employeeObjectGuid` alan hatası); hesabı pasifse `409 employee_inactive`;
   AD'ye ulaşılamazsa `503 directory_unavailable` ve hiçbir şey yazılmaz.
3. Tek transaction içinde (execution strategy ile, bkz. aşağı): demirbaş zimmetleriyle birlikte okunur; `rowVersion`
   istemcinin sürümüyle karşılaştırılır ve EF Core'un `OriginalValue`'su yapılır; çalışanın `Employees` kaydı AD'deki
   bilgilerle eklenir veya güncellenir; domain kuralları uygulanır; zimmet kaydı eklenir, demirbaş `Assigned` olur;
   `Assigned` audit kaydı (zimmet kimliğiyle) yazılır; commit edilir.
4. Yanıt, demirbaşın yeni `rowVersion`'ı ve `activeAssignment` ile güncel detayıdır.

Uygulamaya giren yöneticiler `AdminUsers`, zimmet alan çalışanlar `Employees` tablosundadır; zimmet hiçbir zaman
`AdminUsers`'a kayıt eklemez (testle denetlenir).

### Bir demirbaşta tek aktif zimmet

Aynı demirbaş için aynı anda gelen istekleri iki ayrı koruma durdurur:

- **RowVersion:** zimmet demirbaş satırını da günceller (`Status` ve `UpdatedAt`), `UPDATE … WHERE RowVersion = <istemcinin
  sürümü>` ile. İlk commit sürümü değiştirir; aynı sürümü okuyan diğer istekler `409 concurrency_conflict` alır.
- **Filtreli benzersiz indeks:** `UX_AssetAssignments_AssetId_Active` (`AssetId`, `WHERE [ReturnedAt] IS NULL`). Demirbaş
  satırına dokunmadan yazılan bir aktif zimmet (başka bir uygulama, elle SQL, ileride yapılabilecek bir hata) olsa bile
  SQL Server ikinci aktif zimmeti reddeder; API bunu `409 asset_already_assigned` olarak döndürür ve transaction'daki her
  şeyi (çalışan kaydı dahil) geri alır.

Aynı anda ilk kez zimmet alan aynı çalışan için iki istek gelirse `IX_Employees_ObjectGuid` birini reddeder; o istek
bir kez baştan çalışır (kayıt artık vardır ve güncellenir).

## İade alma: `POST /api/assets/{id}/returns` (23. gün)

```json
{ "rowVersion": "AAAAAAAAB9I=" }
```

Tek transaction içinde aktif zimmet kapatılır (`ReturnedAt`, `ReturnedBy`), demirbaş `Available` olur ve `Returned` audit
kaydı yazılır; tek `SaveChanges` ile commit edilir. Zimmet kaydı **silinmez**; geçmişte kalır. Aynı `rowVersion` ile gelen
eşzamanlı iadelerden yalnızca biri başarılı olur, diğerleri `409` alır.

## Zimmet geçmişi: `GET /api/assets/{id}/assignments`

En yeni zimmet önce (`AssignedAt`, eşitse `Id`), sayfalı (varsayılan 50, en fazla 100). Arşivlenmiş demirbaşın geçmişi de
okunur. Her kayıt: `id`, `employeeId`, `userName`, `displayName`, `department` (AD'deki), `assignmentDescription`,
`notes`, `assignedAt`, `assignedBy`, `returnedAt`, `returnedBy` (iade edilmemişse son ikisi `null`).

## Audit kayıtları

| İşlem | `action` | Eski değerler | Yeni değerler |
| --- | --- | --- | --- |
| Zimmet | `Assigned` | `status: Available` | `status`, `assignmentId`, `employeeId`, `employeeObjectGuid`, `employeeUserName`, `employeeDisplayName`, `assignmentDescription`, `notes`, `assignedAt` |
| İade | `Returned` | `status: Assigned`, `assignmentId`, `employeeId`, `employeeUserName`, `employeeDisplayName`, `assignedAt` | `status`, `returnedAt` |

Kayıtlar demirbaşa (`EntityName = "Asset"`) yazılır, `GET /api/assets/{id}/history`'de görünür ve değişiklikle aynı
transaction'da commit edilir. Yapan kullanıcı oturumdaki AD kullanıcı adıdır; correlation ID isteğinkidir.

## Transaction ve execution strategy

Çok adımlı yazmalar (demirbaş ekleme, zimmet, iade) `ApplicationDbContext.InTransactionAsync` ile yapılır: işlem, açık
transaction'ıyla birlikte EF Core'un execution strategy'si içinde çalışır ve her denemeye boş change tracker ile başlar.
Bugün retry stratejisi kapalıdır (`EnableRetryOnFailure` yok; `DbContextRegistrationTests` denetler); ileride açılırsa
geçici bir hatada işlemin tamamı (okuma, kontroller, yazma, commit) yeniden çalışır, yarım tekrar olmaz.

## Hata kodları

| Durum | `code` | Ne zaman |
| --- | --- | --- |
| `400` | — (ValidationProblem) | Alan hatası; dizinde olmayan çalışan (`employeeObjectGuid`) |
| `404` | `asset_not_found` | Demirbaş yok |
| `409` | `concurrency_conflict` | `rowVersion` güncel değil (başka biri değiştirdi, zimmetledi veya iade aldı) |
| `409` | `asset_already_assigned` | Demirbaş zaten zimmetli (eşzamanlı istekte indeks reddi dahil) |
| `409` | `asset_not_available` | Arızalı veya hurda demirbaş zimmetlenemez |
| `409` | `asset_not_assigned` | İade edilecek zimmet yok |
| `409` | `asset_archived` | Arşivlenmiş demirbaş |
| `409` | `employee_inactive` | Çalışanın AD hesabı pasif |
| `503` | `directory_unavailable` | AD'ye ulaşılamadı; hiçbir şey yazılmadı |

## Testler

| Kabul ölçütü | Testler | Sonuç |
| --- | --- | --- |
| **21. gün** Yetkisiz AD çalışanı zimmet için seçilebilir | `SambaEmployeeDirectoryTests`, `EmployeeSearchApiTests` (bkz. [`active-directory.md`](active-directory.md)); `AssignmentWithActiveDirectoryTests`: Samba AD'de `Bim_Envanter` üyesi olmayan `mehmet.user` bulunur ve demirbaş ona zimmetlenir, `Employees`'e e-postasıyla eklenir, `AdminUsers`'a eklenmez; pasif `disabled.user` GUID'iyle gönderilince `409 employee_inactive` | Geçti |
| **22. gün** Eşzamanlı iki istekte yalnızca bir aktif zimmet | `AssignmentConsistencyTests`: aynı sürümü okuyup bütün kontrollerden geçen 8 istek kaydetmeden hemen önce bekletilip birlikte bırakılır: tam bir `201`, yedi `409`, veritabanında tek aktif zimmet, tek `Assigned` audit. API okuduktan sonra demirbaş satırına dokunmadan SQL ile aktif zimmet yazılınca filtreli benzersiz indeks API'nin zimmetini reddeder (`409 asset_already_assigned`), API'nin eklediği çalışan kaydı da geri alınır. Audit yazılamayınca zimmet ve çalışan kaydı birlikte geri alınır (`500`) | Geçti |
| **22. gün** Zimmet kuralları | `AssetAssignmentTests`: sahte dizindeki grup dışı çalışana zimmet (`201`, `Location`, yeni `rowVersion`, `activeAssignment`, `Employees` kaydı, `Assigned` audit ve değerleri, listede zimmetli kişi); her alan için Türkçe hata ve hiçbir şeyin yazılmaması; dizinde olmayan çalışan; pasif hesap; eski sürüm `409`; zimmetliye ikinci zimmet; arızalı/hurda; arşivlenmiş ve olmayan demirbaş; erişilemeyen AD `503`; çalışan kaydının AD'den yenilenmesi | Geçti |
| **23. gün** İade ve geçmiş doğru kaydedilir | `AssetAssignmentTests`: iade `Available`, zimmet geçmişinde iki dönem en yeni önce, iade eden ve zamanı, ikinci çalışana yeniden zimmet, audit sırası (`Assigned`, `Returned`, `Assigned`, `Created`), arşivlenmiş demirbaşın zimmet geçmişi okunur, zimmetsiz demirbaş iadesi `409`, eski sürümle iade `409`. `AssignmentConsistencyTests`: aynı anda 8 iadeden biri başarılı; audit yazılamayınca iade geri alınır | Geçti |
| Yetki | `AssetAuthorizationTests`: yeni dört uç nokta oturumsuz `401`, rolsüz `403` | Geçti |
| Birim | `AssetAssignmentServiceTests`: AD'den yeniden okunan çalışan ve kırpılmış metinler; geçersiz istekte AD'ye sorulmaz; dizinde olmayan, pasif ve erişilemeyen dizin | Geçti |

Bozma denemesi: indeks ihlalinin `409`'a çevrilmesi kaldırılınca "arkadan yazılan aktif zimmet" testi kırıldı
(`500`). Eşzamanlı 8 istekte kaybedenler her çalıştırmada RowVersion korumasına takıldı; indeks koruması bu yüzden
ayrı testle denetlenir.
