# Raporlar (33. gün: gösterge paneli, 34. gün: rapor ekranı)

Gösterge paneli, envanterin özetini ve son on iki ayın zimmet hareketlerini tek ekranda gösterir. Her rakam
`GET /api/dashboard/statistics` ile gelir ve aşağıdaki testlerde aynı veritabanından düz SQL ile sayılan rakamla
karşılaştırılır (kabul ölçütü: "Rakamlar DB ile tutarlı").

## API: `GET /api/dashboard/statistics`

Yalnızca oturumu olan yöneticiler (`Administrator`); oturumsuz `401`, rolsüz `403`. Arşivlenmiş demirbaşlar
`archivedCount` ve aylık hareketler dışında hiçbir sayıya girmez.

| Alan | İçerik |
| --- | --- |
| `totalCount`, `assignedCount`, `availableCount`, `faultyCount`, `retiredCount` | Arşivlenmemiş demirbaşlar, duruma göre. Dört durumun toplamı `totalCount`'tur |
| `archivedCount` | Arşivlenmiş demirbaşlar |
| `byCity`, `byDepartment`, `byBrand` | En çok demirbaşı olan **8** şehir/departman/marka (`items`: `id`, `name`, `count`, `assignedCount`), büyükten küçüğe; eşitlikte Türkçe alfabe sırası (C<Ç, I<İ, S<Ş). Kalanlar tek satırda: `otherGroupCount` (kaç şehir), `otherCount`, `otherAssignedCount`. `items` ve "diğer" toplamı `totalCount`'tur |
| `byType` | Türe göre sayılar, büyükten küçüğe |
| `monthlyMovements` | Son 12 ay, eskiden yeniye, içinde bulunulan ayla biter: `month` (`yyyy-MM`), `assignedCount` (o ay verilen zimmet), `returnedCount` (o ay alınan iade). Sonradan arşivlenen demirbaşların hareketleri de sayılır, çünkü o hareketler olmuştur |
| `recentActivity` | Son 10 demirbaş denetim kaydı (arşivlenen demirbaşlar dahil, güncel demirbaş koduyla) |

**Ay sınırları raporlama saat dilimindedir** (`Reporting:TimeZone`, varsayılan `Europe/Istanbul`; bkz.
[`export.md`](export.md#ayarlar)). İstanbul'da 30 Eylül 23:30'da verilen zimmet Eylül'e, 1 Ekim 00:30'da verilen
Ekim'e sayılır; ikisi de UTC'de 30 Eylül olduğu hâlde. Pencerenin başı yerel ayın ilk gece yarısıdır (saat
değişikliğinin atladığı bir yerel saat varsa ondan sonraki ilk geçerli saat).

Şehir, departman ve marka listesi 8 satırla sınırlıdır: 81 ilde demirbaşı olan bir şirkette panel 81 satırlık liste
göstermez. Tam liste [rapor ekranındadır](#rapor-ekranı-34-gün).

## Ekran

- Dört rakam kartı (Toplam, Zimmetli, Boşta, Arızalı), altında hurda ve arşivlenmiş sayısı. Her kart envanteri o
  duruma göre süzülmüş açar.
- **Şehirlere / Departmanlara göre dağılım:** her satırda ad, "600 zimmetli · 800" ve iki parçalı çubuk (zimmetli ve
  zimmetsiz, aralarında 2 piksel boşluk; üstte açıklama). Ad, envanteri o şehre/departmana süzülmüş açar
  (`/envanter?cityId=…`). Kalanlar "Diğer 12 şehir" satırında toplanır; bu satır (34. günden beri) tam listeyi rapor
  ekranında açar (`/raporlar`, `/raporlar?groupBy=department`).
- **Aylık zimmet hareketleri:** her ay için yan yana iki çubuk (Zimmet verildi, İade alındı), 0–orta–üst ölçek
  çizgileri, altta Türkçe kısa ay adları (ilk ayda ve Ocak'ta yıl). Bir aya fare ile gelince veya klavyeyle odaklanınca
  "Ekim 2026: 5 zimmet, 3 iade" ipucu çıkar; ekran okuyucu her ayı bu adla okur. "Tablo olarak göster" aynı rakamları
  Ay / Zimmet verildi / İade alındı tablosunda gösterir. On iki ayda hareket yoksa "Son 12 ayda zimmet verilmedi ve
  iade alınmadı." yazar.
- **Türlere göre dağılım:** Türkçe tür adları (Dizüstü, Masaüstü…); her satır envanteri o türe süzer.
- **Markalara göre dağılım:** en büyük 8 marka (her biri envanteri o markaya süzer) ve "Diğer N marka" (rapor
  ekranında marka bazında özeti açar).
- Son işlemler (16. gün) aynen kalır. Panel, canlı bildirim gelince ve bağlantı dönünce yeniden okunur
  ([`realtime.md`](realtime.md)).
- Renkler: zimmet/zimmet verildi mavi `#2563EB`, iade turuncu `#EB6834`, zimmetsiz gri `#94A3B8`. Mavi-turuncu çift
  renk körlüğü denetiminden (dataviz paleti doğrulayıcısı, beyaz zemin) geçti. Rakamlar çubuk renginde değil, metin
  renginde yazılır; renk tek başına anlam taşımaz (açıklama, rakam ve tablo her zaman var).
- Uzun adlar (ör. "E2E-MV1AVU11-L4JKJ Marka A") kartı taşırmaz, "…" ile kısalır; tam ad üzerine gelince görünür.
  1400 piksel masaüstü ve 390 piksel telefon görünümünde ekran görüntüsüyle kontrol edildi (33. gün).

## Testler

| Test | Doğrulanan | Sonuç |
| --- | --- | --- |
| `DashboardConsistencyTests` (SQL Server, kendi veritabanı) | 22 demirbaş, 10 marka, 10 şehir, 9 departman, her durum, 2 arşivlenmiş. **Her rakam düz SQL ile sayılanla aynı:** durum sayıları, şehir/departman/marka ilk 8'i ve "diğer" (sayı, demirbaş, zimmetli), tür sayıları, her dağılımın toplamı. Eşitlikte Türkçe sıra (Ankara, Bursa, Çorum, Iğdır, İstanbul, İzmir, Ordu, Şanlıurfa). Aylık hareketler **SQL Server'ın kendi** `AT TIME ZONE 'Turkey Standard Time'` hesabıyla aynı: ay sınırında 23:59/00:00, UTC'de aynı güne düşen 23:30/00:30, pencere dışı Ekim 2025, arşivlenen demirbaşın hareketi, aynı demirbaşın iade ve yeniden zimmeti. API üzerinden verilen zimmet ve iade rakamları bir artırır ve yine SQL ile tutar | 3/3 geçti |
| `DashboardStatisticsTests` | Sayılar, arşivin dışarıda kalması, değişikliklerin yansıması, son 10 işlem, `401`/`403` | 5/5 geçti |
| `DashboardPage.test.tsx` (Vitest) | Kartlar ve bağlantıları; şehir/departman satır metinleri ve "Diğer 3 şehir"; Türkçe türler; markalar ve "Diğer 2 marka"; 12 ay çubuğu, erişilebilir adları, klavye odağı ve tablo görünümü; boş envanter; hata ve yeniden deneme | 7/7 geçti |
| `monthlyMovements.test.ts` (Vitest) | Ölçek üst değeri (2, 4, 6, 8, 10 × 10ⁿ; orta çizgi tam sayı), Türkçe ay açıklaması | 10/10 geçti |
| `dashboard.spec.ts` (Playwright, gerçek API ve SQL Server) | Ekrandaki kart rakamları, şehir satırları ("diğer" dahil, en fazla 8 şehir), tür satırları ve bu ayın çubuğu API'nin verdiğiyle aynı; kart envanteri süzülmüş açar | 2/2 geçti |

Bozma denemeleri (33. gün, her biri geri alındı): ay UTC'ye göre gruplanınca iki test, arşivlenmiş demirbaşların
hareketleri dışarıda bırakılınca bir test, eşitlik Türkçe yerine sıra numarasıyla (ordinal) çözülünce bir test,
"diğer" satırının zimmetli sayısı sıfırlanınca bir test kırıldı.

Telefon görünümü testi (`responsive.spec.ts`) 33. günde düzeltildi: "Son sayfa" düğmesinin kesilmediği "tamamen
görünür" (oran 1) yerine en az %95 görünür olması ve kutusunun kartın içinde kalmasıyla (0,5 piksel pay) ölçülür.
Önceki ölçüm, yuvarlamadan kaybolan piksel kesrinde (görünen oran 0,98) aralıklı kırılıyordu. Düzeltmeden sonra dört
tekrarda 8/8 geçti.

## Rapor ekranı (34. gün)

`/raporlar` iki sekmedir; ikisi de yalnızca okur, filtreleri sayfa adresinde tutar (bağlantı paylaşılabilir, geri
düğmesi çalışır) ve ekrandakinin aynısını Excel'e aktarır. Kabul ölçütü: "Türkçe filtre ve export çalışır".

### Envanter özeti (`/raporlar`)

Arşivlenmemiş demirbaşlar, seçilen gruplamaya göre durum durum sayılır: **Şehir, Departman, Lokasyon, Marka, Model,
Tür, Durum**. Sütunlar Toplam, Zimmetli, Boşta, Arızalı, Hurda; en altta Toplam satırı.

- Filtreler envanter listesininkilerle aynıdır (arama, durum, tür, marka, model, şehir, lokasyon, departman); arşiv
  anahtarı yoktur, çünkü özet yalnızca kullanımdaki demirbaşları sayar.
- Satırlar büyükten küçüğe; eşitlikte Türkçe alfabe sırası (C<Ç, I<İ, S<Ş). Lokasyonu olmayanlar "Lokasyon belirtilmemiş" satırındadır. Lokasyon "Merkez Ofis
  (İstanbul)", model "Dell Latitude 5440" biçiminde yazılır; aynı adlı iki lokasyon karışmaz.
- **Her satır envanteri o demirbaşlarla açar:** satırın kendi filtresi (ör. `cityId=3&locationId=7`) raporun
  filtrelerine eklenir; aynı alan raporda da seçiliyse satırınki geçerlidir. Testte her satırın bağlantısıyla açılan
  envanterin sayısı satırdaki Toplam ile aynı çıktı. "Lokasyon belirtilmemiş" satırının envanterde karşılığı olan bir
  filtre olmadığı için bağlantısızdır.
- Excel: "Özet" sayfası ekrandaki tablo (başlık satırı, gruplar, Toplam) ve "Bilgi" sayfası (rapor adı, gruplama,
  kapsam "Arşivlenmemiş demirbaşlar", oluşturan, oluşturma zamanı ve saat dilimi, demirbaş ve grup sayısı, uygulanan
  her filtre Türkçe adıyla). Dosya adı `envanter-ozeti-{sehir|departman|lokasyon|marka|model|tur|durum}-YYYY-AA-GG.xlsx`.

### Zimmet hareketleri (`/raporlar/zimmet-hareketleri`)

Bir dönemde verilen zimmetler ve alınan iadeler, yeniden eskiye: tarih, hareket (Zimmet verildi / İade alındı),
demirbaş (kodu, türü; sonradan arşivlendiyse "· arşivlendi"), marka/model, çalışan (ad soyad ve kullanıcı adı),
şehir/departman, işlemi yapan yönetici.

- Filtreler: **Bu ay**, **Geçen ay** kısayolları, başlangıç ve bitiş tarihi (bitiş günü dahil), hareket türü, arama
  (demirbaş kodu, çalışanın kullanıcı adı veya ad soyadı; en fazla 100 karakter, 5 kelime), tür, şehir, departman.
- Üstte dönemin özeti: "1 Ekim 2026 – 31 Ekim 2026: 4 zimmet verildi, 2 iade alındı." (tarih yoksa "Tüm zamanlar").
- **Günler raporlama saat dilimindedir** (`Reporting:TimeZone`, varsayılan `Europe/Istanbul`): 1–31 Ekim, İstanbul'da
  1 Ekim 00:00 ile 1 Kasım 00:00 arasıdır (UTC'de 30 Eylül 21:00 – 31 Ekim 21:00). Testler bu sınırı SQL Server'ın
  kendi `AT TIME ZONE 'Turkey Standard Time'` hesabıyla karşılaştırır.
- Sonradan arşivlenen demirbaşların hareketleri de listelenir, çünkü o hareketler olmuştur.
- **Şehir ve departman, demirbaşın bugünkü yeridir** (hareket anındaki yeri saklanmaz); ekranda ve Excel'in Bilgi
  sayfasında bu not yazar. Saatler ekranda bu bilgisayarın saat dilimiyle, Excel'de raporlama saat dilimiyle yazılır.
- Excel: "Hareketler" sayfası (14 sütun: Tarih, Hareket, Demirbaş Kodu, Tür, Marka, Model, Seri No, Kullanıcı Adı,
  Ad Soyad, Zimmet Tanımı, Şehir, Departman, İşlemi Yapan, Arşivlenmiş) ve "Bilgi" sayfası (dönem "GG.AA.YYYY" veya "Tümü",
  filtreler, zimmet ve iade sayıları, not). Dosya adı `zimmet-hareketleri-YYYY-AA-GG.xlsx`. Bir dosyaya en fazla
  `Reporting:MaxExportRows` (varsayılan 50.000) hareket yazılır; fazlası okunmadan sayılır ve `400 export_too_large`
  ile Türkçe açıklama döner ("Filtrelerle eşleşen 61.200 hareket var; bir dosyaya en fazla 50.000 hareket
  aktarılabilir. Tarih aralığını veya filtreleri daraltıp tekrar deneyin.").

### API

Hepsi `GET`, yalnızca oturumu olan yöneticiler (`Administrator`); oturumsuz `401`, rolsüz `403`. Geçersiz değerler
`400` ve alan alan Türkçe hata ile döner (ör. "Gruplama geçersiz. Geçerli değerler: city, department, location,
brand, model, assetType, status.", "Bitiş tarihi başlangıç tarihinden önce olamaz.").

| Uç nokta | Parametreler | Yanıt |
| --- | --- | --- |
| `/api/reports/asset-summary` | `groupBy` (varsayılan `city`), envanter listesinin filtreleri (`search`, `status`, `assetType`, `brandId`, `modelId`, `cityId`, `departmentId`, `locationId`) | `groupBy`, `rows` (`key`, `name`, `inventoryFilter`, `totalCount`, `assignedCount`, `availableCount`, `faultyCount`, `retiredCount`), `total` |
| `/api/reports/asset-summary/export` | Aynı | `.xlsx` |
| `/api/reports/assignments` | `from`, `to` (`YYYY-AA-GG`, 2000–2099), `movement` (`Assigned`, `Returned`, birden çok olabilir), `search`, `assetType`, `cityId`, `departmentId`, `page`, `pageSize` | `items`, `page`, `pageSize`, `totalCount`, `assignedCount`, `returnedCount` |
| `/api/reports/assignments/export` | Sayfalama dışında aynı | `.xlsx` veya `400 export_too_large` |

Sorgular EF Core ile parametrelidir: özet tek `GROUP BY` sorgusudur (durum sayıları `COUNT(CASE …)`), hareketler
zimmet ve iade satırlarının `UNION ALL` birleşimi üzerinde sayfalanır. Arama, envanter listesindeki gibi Türkçe
büyük/küçük harf ve aksan duyarsız karşılaştırılır.

### Testler (34. gün)

| Test | Doğrulanan | Sonuç |
| --- | --- | --- |
| `AssetSummaryReportTests` (SQL Server, kendi veritabanı; 16 demirbaş, 3 marka, 4 model, 3 şehir (biri lokasyonsuz), 3 lokasyon, 3 departman, her durum, 2 arşivlenmiş) | Yedi gruplamanın her biri düz SQL ile sayılanla aynı; Türkçe adlar ve sıra (şehir: İstanbul, İzmir, Ankara, sayılarına göre; lokasyon: Lokasyon belirtilmemiş, Çankaya Ofis (Ankara), Merkez Ofis (İstanbul), Ümraniye Depo (İstanbul)); her satırın bağlantısıyla envanter API'si aynı sayıda demirbaş döndürür; envanter filtreleri özete uygulanır; Türkçe doğrulama hataları; Excel'in Özet ve Bilgi sayfaları; dört uç noktada `401`/`403` | 19/19 geçti |
| `AssignmentReportTests` (SQL Server) | Dönem sınırları SQL Server'ın `AT TIME ZONE` gün hesabıyla aynı (23:59 / 00:00, UTC'de aynı güne düşen saatler); sayfalama ve yeniden eskiye sıra; arşivlenen demirbaşın hareketleri işaretli; arama, tür, şehir, departman, hareket filtreleri; Türkçe doğrulama; Excel içeriği; sınır aşılınca `400 export_too_large` (sınır 5, eşleşen 9) | 7/7 geçti |
| `reportParams.test.ts` (Vitest) | Adresin okunması (bilinmeyen gruplamada şehir, arşiv anahtarı hiç yok; API'nin reddedeceği değerler atılır); satır bağlantısında satırın filtresi aynı alanı değiştirir; "Bu ay"/"Geçen ay" tam takvim ayı (yıl dönümü ve 29 Şubat dahil) | 4/4 geçti |
| `ReportsPage.test.tsx` (Vitest) | Özet: gruplar, Toplam satırı ve envantere bağlantılar; gruplama değişince yeni tablo, tür satırı yalnızca o türü açar; Excel isteği gruplama ve filtrelerle gider; filtreli boş durum ve temizleme; hata ve yeniden deneme. Hareketler: dönemin hareketleri ve özeti yeniden eskiye; "Bu ay", hareket türü ve şehir filtresi ilk sayfaya döner; sunucu tarafı sayfalama ve bütün sayfaların aktarılması; aktarma sınırı aşılınca Türkçe açıklama; hareketsiz dönem. Sekmeler arası geçiş | 11/11 geçti |
| `reports.spec.ts` (Playwright, gerçek API ve SQL Server) | Model bazında özet ekranda ve tarayıcının indirdiği Excel'de aynı (M1: 3 toplam, 2 boşta, 1 arızalı; M2: 1; Toplam 4); Bilgi sayfasında rapor, gruplama, oluşturan, arama; M1 satırı envanteri tam o 3 demirbaşla açar. Zimmet hareketleri sekmesinde "Bu ay" ve arama ile iki zimmet ve bir iade yeniden eskiye listelenir, "2 zimmet verildi, 1 iade alındı" yazar, indirilen Excel'in Hareketler sayfası aynı sırada | 2/2 geçti |

Bozma denemeleri (34. gün, her biri geri alındı): dönemin sonuna bir gün eklenmeyince (bitiş günü dahil olmayınca) iki
test, dönem başı İstanbul yerine UTC gece yarısından hesaplanınca iki test kırıldı.

Ekran 1400 piksel masaüstü ve 390 piksel telefon genişliğinde ekran görüntüsüyle kontrol edildi: sayfa yatay
taşmıyor; telefonda filtreler "Filtreler" düğmesinin altına katlanıyor, geniş tablolar kendi kutusunda yatay kayıyor.
Uzun adlar (ör. testlerin ürettiği "HRKT-MV25H9P2-7G4RJ Marka M1") hücrede satır kırarak sığıyor.

**Sınır:** Lokasyon gruplamasında yüzlerce lokasyon varsa tablo o kadar satır olur (sayfalama yok); Excel'e aktarmak
daha rahattır. Excel dosyası Microsoft Excel ile açılarak denenmedi; içerik testlerde dosyanın XML'inden okunarak
doğrulandı.
