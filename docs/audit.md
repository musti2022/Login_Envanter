# Denetim kayıtları (31. gün)

Her veri değişikliği, değişiklikle aynı transaction'da bir `AuditLogs` satırı yazar: hangi kayıt (`EntityName`,
`EntityId`), ne yapıldı (`Action`), yalnızca değişen alanların **eski ve yeni değerleri** (`OldValues`, `NewValues`,
JSON), kim (`UserName`, oturumdaki AD kullanıcı adı), ne zaman (`Timestamp`, UTC) ve hangi istekte (`CorrelationId`).
Kayıtlar uygulama üzerinden değiştirilemez veya silinemez. Hangi işlemin hangi alanları yazdığı:
[`assets-api.md`](assets-api.md#audit-kayıtları), [`assignments-api.md`](assignments-api.md#audit-kayıtları),
[`lookups-api.md`](lookups-api.md), [`session-security.md`](session-security.md).

31. günde bu kayıtlar ekrandan ve API'den izlenebilir hale geldi.

## API: `GET /api/audit-logs`

Yalnızca oturumu olan yöneticiler (`Administrator`); oturumsuz `401`, rolsüz `403`. Yazma uç noktası yoktur:
`POST`, `PUT`, `DELETE` `405` döner. Kayıtlar en yeni önce (yazılma sırasıyla) sıralanır, sayfalanır (`page`,
`pageSize`; varsayılan 25, en fazla 100). Filtrelerin hepsi birlikte uygulanır (VE):

| Parametre | Eşleşme |
| --- | --- |
| `entityName` | `Asset`, `Brand`, `AssetModel`, `City`, `Department`, `Location`, `AdminUser` (büyük/küçük harf fark etmez) |
| `entityId` | Kaydın kimliği, tam eşleşme; yalnızca `entityName` ile |
| `assetCode` | Kodu bu metni içeren demirbaşların (arşivlenmişler dahil) kayıtları; büyük/küçük harf ve aksan fark etmez (`pc-ist` → `PC-IST-01`) |
| `action` | Bir veya birkaç işlem: `Created`, `Updated`, `StatusChanged`, `LocationChanged`, `Assigned`, `Returned`, `Archived`, `SignedIn`, `SignedOut`, `AccessRevoked` |
| `userName` | İşlemi yapan kullanıcı adının bir kısmı |
| `from`, `to` | Zaman aralığı: `from` dahil, `to` hariç. Saat dilimiyle ISO 8601 zorunlu (`2026-10-01T00:00:00+03:00` veya `…Z`); saat dilimi olmayan değer, sunucunun saat dilimine göre yorumlanmasın diye reddedilir |
| `correlationId` | Tek bir isteğin yazdığı bütün kayıtlar (ör. bir düzenlemenin `Updated`, `LocationChanged`, `StatusChanged` kayıtları) |

Geçersiz filtre `400` ve alanın altında Türkçe mesajla reddedilir (bilinmeyen kayıt türü veya işlem, türsüz
`entityId`, demirbaş dışı türle `assetCode`, saat dilimsiz tarih, başlangıçtan önce biten aralık, sayfa sınırları).

```json
{
  "items": [
    {
      "id": 912, "entityName": "Asset", "entityId": "41", "entityLabel": "PC-IST-01",
      "action": "Updated", "userName": "ayse.admin", "timestamp": "2026-10-10T06:02:45.4814318+00:00",
      "correlationId": "3f6c0b9e4a2d4c55a8a0f1d7c2e9b411",
      "oldValues": { "computerName": "PC-TEST", "description": "Test demirbaşı" },
      "newValues": { "computerName": "PC-YENI", "description": null }
    }
  ],
  "page": 1, "pageSize": 25, "totalCount": 1, "totalPages": 1
}
```

`entityLabel` kaydın **bugünkü** adıdır (demirbaş kodu, tanım adı, yöneticinin kullanıcı adı); kayıt artık yoksa
`null`. Değerler o anki haliyle saklandığı için bir tanımın adı sonradan değişse de eski kayıt okunabilir kalır.
Etiketler sayfadaki her kayıt türü için tek sorguyla okunur (N+1 yok).

`GET /api/audit-logs/{id}` tek kaydı aynı biçimde döndürür; yoksa `404 audit_log_not_found`.

## Ekran: Denetim Geçmişi (`/denetim-gecmisi`)

- Filtreler: kayıt türü, işlem (çoklu), kullanıcı, demirbaş kodu, başlangıç ve bitiş tarihi (seçilen gün dahil;
  tarayıcının yerel saatine göre günün başı ve sonu API'ye saat dilimiyle gönderilir), işlem numarası. Filtreler
  sayfa adresinde tutulur; kopyalanan bağlantı aynı listeyi açar. Telefonda filtreler bir düğmenin altına katlanır.
- Tablo: zaman (saniyesiyle), kullanıcı, işlem, kayıt (demirbaşsa detay sayfasına bağlantı) ve değişiklikler
  (`Alan: önce → sonra`, ilk üçü; gerisi "+n alan daha").
- "Ayrıntı": kaydın bütün alanları **Önceki değer / Yeni değer** tablosunda, işlem numarasıyla birlikte;
  "Bu işlemin tüm kayıtları" aynı isteğin bütün kayıtlarını listeler.
- Demirbaş detay sayfasındaki "Geçmiş" kartından "Denetim kayıtlarında aç" o demirbaşın kayıtlarını açar.
- Canlı bildirim gelince veya kullanıcı bir değişiklik kaydedince liste API'den yeniden okunur.

## 31. günde bulunan ve düzeltilen hata

Belgede "bir düzenlemenin kayıtları aynı kullanıcı, **zaman** ve correlation ID'yi taşır" yazıyordu, fakat her kayıt
saati ayrı okuyordu: aynı düzenlemenin `Updated`, `LocationChanged`, `StatusChanged` kayıtları mikro saniyelerle
farklı zaman taşıyordu (yeni test bunu yakaladı). `AssetStore` artık bir değişikliğin bütün kayıtlarına tek zaman verir.

## Testler

| Test | Doğrulanan | Sonuç |
| --- | --- | --- |
| `AuditLogApiTests` (SQL Server, kendi veritabanı) | Bir düzenleme alan alan izlenir: dört kayıt en yeni önce, eski/yeni değerler birebir, üç kayıt tek correlation ID ve tek zaman; ekleme kaydı tüm alanları taşır. Zimmet ve iade, sahibi ve durumu önce/sonra taşır. Filtreler: kod parçası (`pc-ist`), kullanıcı parçası, işlem, correlation ID, zaman aralığı (`from` dahil, `to` hariç). Tanım kaydı bugünkü adıyla; arşivlenmiş demirbaşın kayıtları okunur. Giriş kaydı yanıtta parola veya `password` geçmeden listelenir. Sayfalar çakışmaz; tek kayıt okunur, olmayan `404`. On iki hatalı filtre için Türkçe alan hatası. Oturumsuz `401`, rolsüz `403`; `POST`/`PUT`/`DELETE` `405` | 23/23 geçti |
| `AuditLogRequestTests` (birim) | Filtrelerin okunması, saat dilimli/dilimsiz zamanlar, kayıt türleri, metin sınırları, aralık sırası | 16/16 geçti |
| `AuditLogPage.test.tsx` (Vitest) | Tablo ve değişiklik metni, istek parametreleri ve sayfa adresi, gün → yerel saatle aralık, demirbaş kodu yalnızca uygun türde, detaydan gelen kayıt filtresi, ayrıntı penceresi ve aynı isteğin kayıtları, giriş kaydı ve silinmiş kayıt, boş ve hata durumları, telefonda katlanan filtreler | 11/11 geçti |
| `audit.spec.ts` (Playwright, gerçek API ve SQL Server) | Formdaki düzenleme detay sayfasından denetim kaydına kadar izlenir, ayrıntıda önce/sonra; kod ve işlem filtresi sunucuda | 2/2 geçti |
| `responsive.spec.ts` (390 px) | Denetim listesi ve ayrıntı penceresi telefon genişliğine sığar | geçti |

Bozma denemeleri: demirbaş kodu filtresi kaldırılınca filtre testi, arşivlenmiş demirbaşlar etiketlemeden çıkarılınca
arşiv testi, düzenleme kayıtlarına ayrı zaman verilince düzenleme testi kırıldı.
