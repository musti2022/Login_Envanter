# Excel'e aktarma (32. gün)

Envanter ekranındaki **Excel'e aktar** düğmesi, ekrandaki listeyi **aynı arama, filtreler ve sıralamayla, bütün
sayfalarıyla** bir `.xlsx` dosyası olarak indirir. Ekranın hangi sayfada olduğu ve sayfa boyutu dosyayı daraltmaz.
Arşiv listesi açıkken düğme arşivlenmiş demirbaşları aktarır.

## API: `GET /api/assets/export`

- Yalnızca oturumu olan yöneticiler (`Administrator`); oturumsuz `401`, rolsüz `403`. Okuma isteğidir, veri değiştirmez.
- Parametreler `GET /api/assets` ile aynıdır (`search`, `status`, `assetType`, `brandId`, `modelId`, `cityId`,
  `departmentId`, `locationId`, `archived`, `sortBy`, `sortDirection`) ve aynı doğrulamadan geçer: listenin reddettiği
  filtre aynı Türkçe alan hatasıyla `400` döner. `page` ve `pageSize` yok sayılır.
- Liste ve dışa aktarma aynı sorgu ve sıralama kodunu kullanır (`AssetStore.Matching`, `Sort`, `ReadListItemsAsync`),
  bu yüzden dosya listeyle aynı satırları aynı sırada taşır.
- Yanıt: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition: attachment`,
  dosya adı `envanter-YYYY-AA-GG.xlsx` (arşiv için `envanter-arsiv-YYYY-AA-GG.xlsx`; gün, raporlama saat diliminde).
  Diğer API yanıtları gibi `Cache-Control: no-store`.
- Sınır: bir dosyaya en fazla `Reporting:MaxExportRows` (varsayılan 50.000) demirbaş. Fazlası okunmadan reddedilir:

```json
{
  "status": 400,
  "title": "Aktarılacak demirbaş sayısı sınırı aşıyor.",
  "detail": "Filtrelerle eşleşen 63.412 demirbaş var; bir dosyaya en fazla 50.000 demirbaş aktarılabilir. Filtreleri daraltıp tekrar deneyin.",
  "code": "export_too_large"
}
```

- Her aktarma `{UserName} exported {RowCount} assets` bilgi logu yazar (parola, oturum veya veri içeriği loglanmaz).

## Dosya

**Envanter** sayfası: Demirbaş Kodu, Kullanıcı Adı, Ad Soyad, Bilgisayar Adı, Marka, Model, Seri No, Zimmet Tanımı,
Şehir, Lokasyon, Departman, Tür, Durum, Eklenme Zamanı, Son Değişiklik. Başlık satırı kalın ve kaydırırken sabit
kalır, Excel filtre düğmeleri açık gelir. Tür ve durum ekrandaki Türkçe adlarıyla yazılır (Dizüstü, Zimmetli…). Boş
alanlar boş hücredir.

**Bilgi** sayfası: rapor (envanter listesi veya arşiv), oluşturan kullanıcı, oluşturulma zamanı, saat dilimi, demirbaş
sayısı ve kullanılan filtreler adlarıyla (arama, durum, tür, marka, model, şehir, departman, lokasyon; seçilmeyenler
"Tümü") ve sıralama ("Bilgisayar Adı, azalan").

- **Tarihler** gerçek Excel tarihidir (`gg.aa.yyyy ss:dd` biçimli, sıralanabilir). Excel hücrelerinde saat dilimi
  olmadığı için zamanlar `Reporting:TimeZone` (varsayılan `Europe/Istanbul`) saatine çevrilir ve Bilgi sayfasında
  yazılır. Bilinmeyen bir saat dilimi uygulamayı başlatmaz. Windows'ta IANA adları ICU ile çözülür; ICU yoksa Windows
  adı (`Turkey Standard Time`) verilebilir.
- **Formül enjeksiyonu yok:** her metin, Excel'in formül olarak çalıştırmadığı metin hücresi (`inlineStr`) olarak
  yazılır. `=HYPERLINK(...)` ile başlayan bir zimmet tanımı dosyada yazıldığı gibi görünür; hücrede formül yoktur.
  (CSV'deki gibi bir kaçış karakteri eklenmez, değer bozulmaz.)
- XML'in taşıyamadığı denetim karakterleri atılır, bir hücreye sığmayan metin (32.767 karakter) kesilir.
- Dosya Open XML SDK (`DocumentFormat.OpenXml` 3.5.1, MIT, Microsoft) ile yazılır; satırlar akış olarak yazıldığı için
  büyük listede bile XML ağacı bellekte tutulmaz. Yazıcı uygulama katmanında `ISpreadsheetWriter` arkasındadır; 34.
  günün raporları aynı yazıcıyı kullanır.

## Ayarlar

```json
"Reporting": {
  "TimeZone": "Europe/Istanbul",
  "MaxExportRows": 50000
}
```

`MaxExportRows` 1 ile 1.000.000 arasında olmalıdır; dışı uygulamayı başlatmaz.

## Ekran

"Excel'e aktar" düğmesi Sütunlar düğmesinin yanındadır. Dosya hazırlanırken düğme "Hazırlanıyor..." yazar ve ikinci
kez basılamaz. Liste boşsa düğme kapalıdır. Sınır aşılırsa sunucunun Türkçe açıklaması ekranın altında bir uyarıda
gösterilir; sunucuya ulaşılamazsa "Sunucuya ulaşılamadı…" yazar. Dosya tarayıcının indirme işlemiyle kaydedilir; içerik
tarayıcı deposuna (localStorage/sessionStorage) yazılmaz.

## Testler

| Test | Doğrulanan | Sonuç |
| --- | --- | --- |
| `AssetExportTests` (SQL Server, kendi veritabanı) | Sekiz filtre/sıralama birleşiminde dosya, liste API'sinin aynı sorgudaki satırlarını aynı sırada ve sütun sütun taşır (Türkçe tür/durum, İstanbul saatiyle tarihler, boş hücreler); sayfa parametresi yok sayılır; 30 demirbaş 25'lik sayfaya rağmen tamamen aktarılır; yanıt türü, ek dosya adı, `no-store`, `nosniff`; Bilgi sayfası (oluşturan, zaman, saat dilimi, sayı, filtre adları, sıralama); formül gibi görünen zimmet tanımı metin hücresidir; sınır aşımı Türkçe `400 export_too_large`, dar filtre geçer; beş hatalı filtre listeyle aynı mesajla reddedilir. Dosyalar Open XML şema doğrulayıcısından hatasız geçer | 18/18 geçti |
| `AssetAuthorizationTests` | `GET /api/assets/export` oturumsuz `401`, rolsüz `403` | geçti |
| `OpenXmlSpreadsheetWriterTests` (birim) | Metin/sayı/tarih/boş hücreler, formül gibi metinler metin kalır, denetim karakterleri ve uzun metin, kalın ve sabit başlık, filtre aralığı ve gizli filtre adı, boş sayfa, Excel sütun adları, geçersiz sayfa adları ve hücre değerleri; şema doğrulaması | 19/19 geçti |
| `AssetExportServiceTests` (birim) | Türkçe başlıklar ve etiketler, İstanbul saati ve gün sınırı (UTC 22:30 → ertesi gün dosya adı), Bilgi sayfası satırları, bulunamayan filtre kimliği, arşiv dosya adı, sınır aşımında satır okunmaması, hatalı filtrede hiçbir şey okunmaması, başka saat dilimi (New York, UTC-04:00), ayar doğrulaması, etiketlerin ekranla aynı olması | 15/15 geçti |
| `InventoryExport.test.tsx` (Vitest) | İstek ekrandaki filtre ve sıralamayla, sayfa bilgisi olmadan gider; dosya sunucunun verdiği adla kaydedilir; arşiv aktarımı; hazırlanırken düğme kapalı; sınır aşımı ve bağlantı hatası Türkçe uyarı; boş listede düğme kapalı | 6/6 geçti |
| `http.test.ts` (Vitest) | `Content-Disposition` dosya adının okunması (UTF-8 adı, tırnaklı/tırnaksız ad, bozuk kodlama) | 7/7 geçti |
| `export.spec.ts` (Playwright, gerçek API ve SQL Server) | Tarayıcının indirdiği dosya açılıp okunur: filtre dışı (hurda) demirbaş yok, kalanlar ekrandaki sırada, Türkçe başlık ve etiketler; Bilgi sayfasında kullanıcı, sayı, arama, durum ve sıralama | 1/1 geçti |

Ayrıca e2e testinin indirdiği dosya iki bağımsız okuyucuyla açıldı (elle, 32. gün): Python `openpyxl` 3.1.5 iki
sayfayı, sabit başlığı (`A2`), filtre aralığını (`A1:O4`), tarih biçimini ve değerleri okudu; LibreOffice dosyayı
CSV'ye çevirdi ve aynı satırları verdi. **Microsoft Excel ile açılması bu ortamda denenmedi.**

Bozma denemeleri: dışa aktarmada sıralama kaldırılınca (ID sırası) iki sıralama testi, filtreler kaldırılınca sekiz
test kırıldı.
