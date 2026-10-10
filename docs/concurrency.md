# Eşzamanlılık (30. gün)

Birden fazla yönetici aynı demirbaş üzerinde aynı anda çalıştığında **veri kaybı ve çift zimmet olmaz**. Bu belge,
bunu sağlayan önlemleri ve gerçek SQL Server üzerinde yarış koşturan testleri özetler.

## Önlemler

| Risk | Önlem | Ayrıntı |
| --- | --- | --- |
| İki kullanıcı aynı sürümü düzenler, ikincisi birincinin değişikliğini sessizce ezer | `Assets.RowVersion`; her yazma istemcinin okuduğu sürümle `UPDATE … WHERE RowVersion = @sürüm` çalıştırır, tutmazsa `409 concurrency_conflict` ve Türkçe çakışma uyarısı | [`database.md`](database.md), [`assets-api.md`](assets-api.md) |
| Bir demirbaşa aynı anda iki zimmet | Zimmet, iade, konum değişikliği ve arşivleme de demirbaş satırını `RowVersion` ile günceller; ayrıca `UX_AssetAssignments_AssetId_Active` (filtreli benzersiz indeks, `ReturnedAt IS NULL`) ikinci aktif zimmeti veritabanında reddeder (`409 asset_already_assigned`) | [`assignments-api.md`](assignments-api.md) |
| Aynı demirbaş koduyla aynı anda iki kayıt | `IX_Assets_AssetCode` benzersiz indeksi; ikinci kayıt `409 duplicate_value` | [`database.md`](database.md) |
| Değişiklik yarım kalır veya audit'siz kalır | Değişiklik ve audit kaydı tek transaction'da, EF Core execution strategy içinde commit edilir; biri yazılamazsa ikisi de geri alınır | [`architecture.md`](architecture.md) |
| Ekran eski veriyi gösterir | Canlı bildirimler ve yeniden bağlanınca tam eşitleme; eski sürümle kaydetme yine `409` ile reddedilir | [`realtime.md`](realtime.md) |

Veritabanında `READ_COMMITTED_SNAPSHOT` açık değildir; testler bu varsayılan ayarla koşar.

## Testler

Testler gerçek SQL Server ile ve API'nin tamamından geçerek çalışır (EF Core InMemory kullanılmaz). Zamanlamanın
önemli olduğu testlerde istekler, aynı sürümü okuyup bütün kontrollerden geçtikten sonra kaydetmeden hemen önce
bekletilir ve hepsi gelince birlikte bırakılır (`SaveGate`); böylece yarış gerçekten yaşanır, istekler sırayla
çalışıp testi kolaylaştırmaz.

| Test | Senaryo | Doğrulanan |
| --- | --- | --- |
| `Of_eight_simultaneous_edits_of_one_version_exactly_one_is_saved_whole` | 8 yönetici aynı sürümü farklı değerlerle aynı anda kaydeder | Tam bir `200`, yedi `409 concurrency_conflict`; kazananın iki alanı birlikte kayıtlı, kaybedenlerden hiçbir alan yok; audit: `Created`, `Updated` |
| `Different_changes_racing_on_one_version_leave_exactly_one_of_them` | Aynı sürüm üzerinde düzenleme, konum değişikliği, zimmet ve arşivleme aynı anda (3 tur) | Her turda tam biri başarılı, diğerleri `409`; kayıt yalnızca kazananın değişikliğini taşır (açıklama, şehir, zimmet, durum, arşiv); audit'te yalnızca kazananın kaydı |
| `Ten_assets_wanted_by_four_employees_at_once_each_get_exactly_one_holder` | 10 demirbaş × 4 çalışan = 40 zimmet isteği aynı anda, bekletme yok | Her demirbaşta tam bir `201` ve tek açık zimmet; kazananın çalışanı kayıtlı; durum `Assigned`; audit: `Created`, `Assigned`; `500` yok |
| `Twelve_assets_given_to_one_new_employee_at_once_are_all_assigned` | Henüz kaydı olmayan bir çalışana 12 farklı demirbaş aynı anda | 12'si de `201`; tek çalışan kaydı, 12 açık zimmet |
| `Assignments_and_returns_racing_for_one_asset_keep_every_committed_change_and_a_consistent_history` | 4 istemci aynı demirbaşı 8'er kez okuyup zimmet alır veya iade eder | Her istek `201`/`200` veya `409`; istemcilere bildirilen her başarı veritabanında ve audit'te birebir var; dönemler üst üste binmez, en fazla bir açık dönem; son durum açık döneme uyar |
| `Six_simultaneous_creates_with_one_asset_code_store_one_asset` | Aynı demirbaş koduyla 6 ekleme aynı anda | Tam bir `201`, beş `409 duplicate_value`; veritabanında tek kayıt |
| `AssignmentConsistencyTests` (22.–23. gün, 30. günde uyarlandı) | 8 zimmet ve 8 iade, demirbaşı kilitlemeden hemen önce bekletilip birlikte bırakılır | Tam biri başarılı, diğerleri `409`; tek açık zimmet, tek audit |
| `concurrency.spec.ts` (Playwright, iki tarayıcı) | İki yönetici aynı demirbaşı aynı anda farklı kişilere zimmetler; aynı demirbaşı aynı anda farklı değerlerle kaydeder | Zimmette API'de tek sahip ve tek dönem; kazanan başarı mesajı, kaybeden çakışma uyarısı ve güncel sahibi görür. Kayıtta iki alan aynı kayıttan gelir, kaybedenin formu `409` uyarısıyla yazdıklarını korur |

## Bulunan ve düzeltilen hatalar

Yarış testleri ilk çalıştırmada üç gerçek hata buldu (veri kaybı veya çift zimmet yoktu; yanlış yanıtlar vardı):

1. **Aynı çalışana aynı anda zimmet → `500`.** İlk kez zimmet alan çalışanın kaydını birden fazla istek aynı anda
   eklemeye çalışıyor, `IX_Employees_ObjectGuid` ihlali tek yeniden denemeyle her zaman çözülmüyordu.
2. **Aynı çalışana aynı anda zimmet → yanlış `409`.** İstekler çalışan kaydını birbirinin altında güncelliyor, çalışan
   kaydının `RowVersion` çakışması demirbaş değişmemiş olsa da "kayıt başka bir kullanıcı tarafından değiştirildi"
   diye dönüyordu (geçici tanı kaydı çakışan kaydın `Employee` olduğunu gösterdi). Bir çalıştırmada bir demirbaşın
   dört isteği de `409` aldı ve demirbaş kimseye zimmetlenmedi.
3. **Zimmet ve iade yarışında `500`.** Demirbaş ve zimmetleri iki ayrı sorguyla okunuyordu; arada commit edilen bir
   iade, "zimmetli ama açık zimmeti yok" görünen bir demirbaş bırakıyordu. Kilit sırası belli olmadığı için ara sıra
   SQL Server kilitlenmesi (deadlock, `1205`) de görüldü.

Düzeltme (`AssetAssignmentStore`): zimmet önce çalışana bağlı bir uygulama kilidi (`sp_getapplock`), sonra demirbaş
satırı kilidi (`UPDLOCK`) alır; iade yalnızca demirbaş satırını kilitler. Kilitler transaction sonuna kadar tutulur ve
hep aynı sırayla alınır. Aynı çalışana veya aynı demirbaşa yönelik istekler sıraya girer; diğerleri birbirini beklemez.
Kilitlerin runtime hesabının yetkileriyle (`SELECT`, `INSERT`, `UPDATE`; şema değişikliği yok) alınabildiği test SQL
Server'ında `grant-runtime-permissions.sql` ile kurulan bir hesapla denendi.

Bozma denemeleri (kod bilerek bozuldu, testlerin yakaladığı görüldü, sonra geri alındı):

| Bozulan | Yakalayan |
| --- | --- |
| Çalışan kilidi kaldırıldı | `Twelve_assets_given_to_one_new_employee_at_once_are_all_assigned` (3/3 çalıştırmada), `Ten_assets_…` (1/3) |
| Demirbaş satırı kilidi kaldırıldı | `Assignments_and_returns_racing_…` ve iki `AssignmentConsistencyTests` yarış testi (2/2 çalıştırmada) |

## Sonuçlar

Yerel çalıştırma: SQL Server 2022 container'ı (`READ_COMMITTED_SNAPSHOT` kapalı), Chromium. Depoda CI yoktur; şirketin
SQL Server'ında bu testler henüz çalıştırılmadı.

| Çalıştırma | Düzeltmeden önceki kodla | Düzeltmeyle |
| --- | --- | --- |
| `ConcurrencyTests` (6 test), art arda | 4 çalıştırmanın her birinde 2-3 test başarısız; çalıştırma başına 1-8 `500` yanıtı ve 4-28 deadlock kaydı | 10 çalıştırmanın hepsinde `AssignmentConsistencyTests` ile birlikte 12/12; deadlock yok; görülen `500`'ler yalnızca audit hatası testlerinin bilerek ürettiği üç yanıt |
| Tüm entegrasyon testleri (SQL Server ve Samba AD açık) | | 510/510 |
| Birim testleri | | 396/396 |
| `concurrency.spec.ts` (iki tarayıcı) | | 3 tekrarda 6/6 |
| Tüm Playwright testleri | | 31/31 |

