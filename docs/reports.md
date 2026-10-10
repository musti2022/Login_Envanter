# Raporlar (33. gün: gösterge paneli raporları)

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
göstermez. Tam liste 34. günün rapor ekranındadır.

## Ekran

- Dört rakam kartı (Toplam, Zimmetli, Boşta, Arızalı), altında hurda ve arşivlenmiş sayısı. Her kart envanteri o
  duruma göre süzülmüş açar.
- **Şehirlere / Departmanlara göre dağılım:** her satırda ad, "600 zimmetli · 800" ve iki parçalı çubuk (zimmetli ve
  zimmetsiz, aralarında 2 piksel boşluk; üstte açıklama). Ad, envanteri o şehre/departmana süzülmüş açar
  (`/envanter?cityId=…`). Kalanlar "Diğer 12 şehir" satırında toplanır (bağlantısız).
- **Aylık zimmet hareketleri:** her ay için yan yana iki çubuk (Zimmet verildi, İade alındı), 0–orta–üst ölçek
  çizgileri, altta Türkçe kısa ay adları (ilk ayda ve Ocak'ta yıl). Bir aya fare ile gelince veya klavyeyle odaklanınca
  "Ekim 2026: 5 zimmet, 3 iade" ipucu çıkar; ekran okuyucu her ayı bu adla okur. "Tablo olarak göster" aynı rakamları
  Ay / Zimmet verildi / İade alındı tablosunda gösterir. On iki ayda hareket yoksa "Son 12 ayda zimmet verilmedi ve
  iade alınmadı." yazar.
- **Türlere göre dağılım:** Türkçe tür adları (Dizüstü, Masaüstü…); her satır envanteri o türe süzer.
- **Markalara göre dağılım:** en büyük 8 marka (her biri envanteri o markaya süzer) ve "Diğer N marka".
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
