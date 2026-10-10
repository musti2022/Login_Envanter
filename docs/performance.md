# Sorgu performansı (35. gün)

Ekranların API istekleri temsili bir envanterde ölçüldü: istek başına SQL komutu, SQL Server'ın okuduğu sayfa sayısı,
SQL CPU süresi ve isteğin API içindeki süresi. Yavaş olanlar düzeltildi, sonra aynı veriyle yeniden ölçüldü. Bir listenin
satır sayısıyla artan sorgu (N+1) olmadığı testle güvence altında.

## Ortam ve veri

| | |
| --- | --- |
| Makine | 4 vCPU, 15 GB bellek; API ve SQL Server aynı makinede |
| SQL Server | SQL Server 2022 CU27 (Docker container), `Turkish_CI_AS` |
| API | Test sunucusu (`WebApplicationFactory`), ağ, TLS ve IIS yok |
| Veri | 20.000 demirbaş, 38.607 zimmet dönemi, 85.464 denetim kaydı, 3.000 çalışan, 81 il, 387 lokasyon, 40 departman, 25 marka, 200 model |

Veri [`LoadSeed`](../tests/EnterpriseInventory.IntegrationTests/Performance/LoadSeed.cs) ile üretilir ve sabit tohumla
her seferinde aynıdır. Demirbaşların yüzde 70'i zimmetli, yüzde 7'si arızalı, yüzde 3'ü hurda, yüzde 2'si arşivde.
İller gerçekteki gibi dengesizdir (ilk birkaç il demirbaşların çoğunu tutar). İki yıllık zimmet ve iade geçmişi vardır ve
her hareketin denetim kaydı, API'nin yazacağı biçimde eklenir. Üretim verisi kullanılmadı.

Ölçülmeyenler: şirketin SQL Server'ı, IIS, ağ ve tarayıcı süresi, **eşzamanlı kullanıcı yükü**. İstekler tek
istemciden sırayla gönderildi. Şirketin sunucusunda sayılar farklı olur; okunan sayfa ve komut sayısı ise sorgunun
kendisine bağlıdır ve oradaki ölçüm için başlangıç noktasıdır.

## Yöntem

[`LoadMeasurementTests`](../tests/EnterpriseInventory.IntegrationTests/Performance/LoadMeasurementTests.cs) her senaryo
için:

1. İsteği iki kez ısınma için gönderir (plan önbelleği ve bağlantı havuzu dolsun).
2. Bir kez daha gönderir; EF Core komut yakalayıcısı ([`SqlMeter`](../tests/EnterpriseInventory.IntegrationTests/Performance/SqlMeter.cs))
   giden SQL komutlarını sayar, SQL Server'ın `sys.dm_exec_query_stats` görünümündeki artış okunan sayfaları ve CPU
   süresini verir (yalnızca ölçülen veritabanının ifadeleri).
3. On beş kez gönderip süreyi ölçer; medyan ve 95. yüzdelik raporlanır.

Yazma senaryoları (zimmet ver, iade al) her çalıştırmada farklı bir demirbaş üzerinde gerçek transaction'la çalışır.

## Sonuçlar

"Önce" 34. gün sonundaki kod, "sonra" bugünkü kod; ikisi de aynı veriyle, ayrı ve yeni oluşturulmuş veritabanlarında.

| Senaryo | SQL komutu | Okunan sayfa: önce → sonra | SQL CPU ms: önce → sonra | Süre medyan ms: önce → sonra | p95 sonra |
| --- | ---: | ---: | ---: | ---: | ---: |
| Envanter listesi, ilk sayfa (25) | 2 | 37.645 → 848 | 124,8 → 3,2 | 102,8 → 11,2 | 31,1 |
| Envanter, 100 satırlık sayfa | 2 | 57.907 → 3.168 | 208,0 → 8,2 | 225,8 → 17,7 | 24,9 |
| Envanter, son sayfa | 2 | 80.149 → 42.501 | 133,9 → 68,0 | 133,1 → 79,2 | 86,3 |
| Envanter, demirbaş koduyla arama | 2 → 3 | 631.073 → 1.570 | 1.136,2 → 188,1 | 1.103,4 → 178,6 | 265,2 |
| Envanter, çalışan adıyla arama | 2 → 3 | 483.789 → 2.176 | 957,6 → 217,2 | 1.013,2 → 179,4 | 233,7 |
| Envanter, en büyük şehir + zimmetli | 2 | 31.282 → 1.221 | 70,7 → 6,7 | 57,2 → 10,2 | 12,5 |
| Envanter, zimmetli kişiye göre sıralı | 2 | 376.597 → 2.332 | 675,6 → 138,1 | 441,5 → 125,8 | 193,7 |
| Envanter, arşiv | 2 | 1.347 → 459 | 25,1 → 0,8 | 26,4 → 4,2 | 6,6 |
| Demirbaş detayı | 1 | 16 → 16 | 0,2 → 0,2 | 2,1 → 2,4 | 3,0 |
| Demirbaş geçmişi | 3 | 20 → 20 | 0,2 → 0,2 | 3,5 → 4,1 | 8,2 |
| Demirbaşın zimmet geçmişi | 3 | 14 → 14 | 0,2 → 0,2 | 3,7 → 4,0 | 7,2 |
| Gösterge paneli | 9 | 2.783 → 2.209 | 38,5 → 45,5 | 85,4 → 76,9 | 173,6 |
| Denetim kayıtları, ilk sayfa | 3 | 443 → 443 | 7,1 → 10,5 | 10,9 → 11,3 | 14,3 |
| Denetim kayıtları, demirbaş koduyla | 3 → 4 | 4.998 → 107 | 122,5 → 18,5 | 132,0 → 24,0 | 37,6 |
| Denetim kayıtları, bir ay | 3 | 128 → 128 | 1,6 → 1,4 | 5,0 → 6,0 | 10,7 |
| Envanter özeti, şehir | 1 | 590 → 590 | 6,4 → 6,6 | 8,8 → 8,7 | 13,1 |
| Envanter özeti, lokasyon | 1 | 436 → 436 | 48,1 → 52,4 | 39,9 → 41,0 | 71,8 |
| Zimmet hareketleri, bir ay | 3 | 2.236 → 396 | 17,1 → 1,4 | 20,1 → 5,2 | 9,6 |
| Zimmet hareketleri, tümü | 3 | 2.228 → 582 | 60,2 → 5,9 | 70,6 → 9,7 | 18,8 |
| Zimmet hareketleri, çalışan adıyla | 3 → 5 | 108.786 → 897 | 573,4 → 28,0 | 543,2 → 38,1 | 50,5 |
| Lokasyon listesi | 1 | 11 → 11 | 1,1 → 0,7 | 2,7 → 2,7 | 3,3 |
| Model listesi | 1 | 8 → 8 | 0,6 → 0,4 | 2,1 → 2,2 | 3,1 |
| Excel: bütün envanter | 2 | 1.629 → 2.858 | 132,6 → 191,0 | 1.178,4 → 1.185,0 | 1.193,0 |
| Excel: bir ayın zimmet hareketleri | 3 | 8.212 → 7.522 | 67,6 → 61,9 | 192,8 → 166,1 | 222,1 |
| Zimmet ver (yazma, transaction) | 9 | 163 → 166 | 2,3 → 1,9 | 14,2 → 12,2 | 32,4 |
| İade al (yazma, transaction) | 5 | 108 → 111 | 0,8 → 0,6 | 12,0 → 10,1 | 11,3 |

Bir sayfa 8 KB'tır. 1 ms'nin altındaki farklar ve tek ölçümdeki p95 sıçramaları ölçüm gürültüsüdür.

## Ne değişti, neden

### 1. Zimmetli kişi sütunları: satır satır arama yerine tek birleştirme

Envanter tablosu her demirbaşın zimmetli kişisini gösterir. Sorgu bunu `FirstOrDefault` ile alıyordu; SQL'de `TOP(1)`
alt sorgusu olur ve SQL Server bunu **her demirbaş için ayrı ayrı** çalıştırır. 25 satırlık ilk sayfa bile 37.645 sayfa
okuyordu, çünkü sıralama ve sayfalama alt sorgudan önce bütün demirbaşlar için yapılıyordu.

Bir demirbaşın en fazla bir açık zimmeti olduğundan (filtreli benzersiz indeks) `MAX` aynı sonucu verir. SQL Server
`MAX` alt sorgularını tek bir birleştirmeye çevirir. Zimmetli kişiye göre sıralama da aynı yolu kullanır
([`AssetStore`](../src/EnterpriseInventory.Infrastructure/Assets/AssetStore.cs)).

### 2. Arama: önce eşleşen kimlikler, sonra liste

Arama kutusu kod, bilgisayar adı, seri no, marka, model, il, departman, lokasyon, zimmetli kişi ve zimmet açıklamasında
arar (büyük/küçük harf ve aksan duyarsız, `LIKE '%…%'`). Eski sorgu bunları `OR` ile bağlıyordu; SQL Server her
demirbaşı sayfa sırasında tek tek ve her yerde ayrı ayrı denedi: bir kod araması 631.073 sayfa ve 1,1 saniye.

İki adımda düzeldi:

1. Her arama yeri `UNION ALL` ile ayrı bir dal oldu; SQL Server her dalı kendi birleştirmesiyle okur.
2. Bu eşleşme sorgusu artık önce **tek başına** çalışır. En fazla 1.000 demirbaş eşleşirse kimlikleri listeye
   `IN (…)` olarak girer. Arama listenin içinde kalınca SQL Server, sayfa hemen dolar umuduyla (row goal) demirbaşları
   sayfa sırasında tek tek denemeye devam ediyordu; `UNION` tek başına sayımı düzeltti ama sayfa sorgusunu düzeltmedi.
   1.000'den fazla eşleşen geniş aramalarda sayfa zaten çabuk dolar; onlar eskisi gibi tek sorgudadır.

Böylece aramalı bir liste istek başına bir komut fazla gönderir (eşleşmeleri bulan sorgu), ama okunan sayfa 631.073'ten
1.570'e indi. Zimmet hareketleri raporunun araması (demirbaş kodu ve çalışan adı, her biri ayrı) ve Denetim Geçmişi'nin
demirbaş kodu filtresi de aynı şekilde önce eşleşmeleri bulur
([`ReportStore`](../src/EnterpriseInventory.Infrastructure/Reports/ReportStore.cs),
[`AuditLogStore`](../src/EnterpriseInventory.Infrastructure/Auditing/AuditLogStore.cs)).

Kalan maliyet `LIKE '%…%'` taramasıdır: indeks kullanamaz, 20.000 demirbaşta 160–220 ms CPU. Envanter birkaç kat
büyürse SQL Server Full-Text Search değerlendirilmelidir (yapılmadı).

### 3. Üç indeks (`AddReportingIndexes` migration'ı)

| İndeks | Kullanan | Etki |
| --- | --- | --- |
| `IX_Assets_IsDeleted_AssetCode` | Envanter ve arşiv listesi, varsayılan sıra (kod) | Arşiv 1.347 → 459 sayfa |
| `IX_AssetAssignments_AssignedAt` | Zimmet hareketleri raporu, gösterge panelinin aylık hareket grafiği | Hareketler, tümü 2.228 → 582 sayfa |
| `IX_AssetAssignments_ReturnedAt` (filtreli, `ReturnedAt IS NOT NULL`) | Aynı rapor ve panelde iadeler | Bir ayın hareketleri 2.236 → 396 sayfa |

İndeksler yazma senaryolarını ölçülebilir biçimde yavaşlatmadı (zimmet ver 14,2 → 12,2 ms, iade 12,0 → 10,1 ms).
Geri alınırsa veri kaybı olmaz ([`deploy/database-rollback.md`](../deploy/database-rollback.md)).

### Dikkat: arşiv filtresi bütün sorguyu etkiler

EF Core'da `IgnoreQueryFilters` (projede `IncludingArchived()`) bir alt sorguda kullanılsa bile **bütün sorgunun**
arşiv filtresini kapatır. Zimmetli kişi alt sorgusunda kullanıldığında normal listeye arşivdeki demirbaşlar da
gelecekti; ölçüm sırasında fark edildi, alt sorgular filtreyi kapatmaz. Kural kodda açıklama olarak duruyor.

### EF Core 10 ve uzun kimlik listeleri

EF Core 10, `liste.Contains(x)` için her değeri ayrı parametre olarak gönderir. SQL Server'ın 2.100 parametre sınırına
yaklaşıldığında kendiliğinden `OPENJSON` ile tek parametreye geçer; 3.000 değerle denendi, sorgu çalıştı. Aramada en
fazla 5 kelime ve kelime başına en fazla 1.000 (hareketlerde 1.000 demirbaş + 1.000 çalışan) kimlik vardır.

### Değişmeyenler

- **Excel: bütün envanter** 1,2 saniyedir ve neredeyse tamamı çalışma kitabının üretimidir; SQL CPU 191 ms.
  Yeni `MAX` sütunları dışa aktarmada eskisinden biraz fazla okur (1.629 → 2.858 sayfa, CPU 133 → 191 ms); isteğin
  süresi değişmedi. Dosya başına satır sınırı (`Reporting:MaxExportRows`, varsayılan 50.000; [`export.md`](export.md))
  bu süreyi sınırlar.
- **Son sayfa** (784. sayfa) 79 ms: `OFFSET` önceki 19.575 satırı da okur. Kullanıcılar arama ve filtreyle daraltır.
- **Gösterge paneli** 9 komutla 77 ms (p95 174 ms). Her kart kendi sorgusudur ve bu sayı testle sabit tutulur.

## N+1 güvencesi

[`QueryCountTests`](../tests/EnterpriseInventory.IntegrationTests/Performance/QueryCountTests.cs) her listeyi 2 ve 100
satırlık sayfayla ister; komut sayısı aynı olmalı ve aşağıdaki sabit sayıya eşit olmalıdır. Satır başına sorgu ekleyen
ya da bir isteğe sessizce sorgu ekleyen değişiklik bu testi kırar.

| İstek | Komut |
| --- | ---: |
| Envanter listesi (düz, zimmetli kişiye göre sıralı, arşiv) | 2 |
| Envanter listesi, aramalı | 3 |
| Denetim kayıtları (düz, işlem filtreli) | 3 |
| Zimmet hareketleri | 3 |
| Bir demirbaşın geçmişi, zimmet geçmişi | 3 |
| Excel: envanter / zimmet hareketleri | 2 / 3 |
| Gösterge paneli | 9 |
| Envanter özeti, demirbaş detayı | 1 |

[`SearchMatchLimitTests`](../tests/EnterpriseInventory.IntegrationTests/Performance/SearchMatchLimitTests.cs), 3.000
demirbaşlık bir veritabanında aramanın iki yolunun (kimlik listesi ve tek sorgu) aynı kayıtları bulduğunu, sonuçları
doğrudan tablolardan sayarak denetler: envanter, arşiv, zimmet hareketleri ve denetim kayıtları. Üç bilinçli hata
(arşiv için kimlikleri arşivsiz bulmak, geniş aramada filtreyi atlamak, hareket aramasında çalışanları unutmak) bu
testlerce yakalandı.

Bu testler SQL Server'da çalışır; EF InMemory sağlayıcısı sorgu planı ya da SQL davranışı için kanıt sayılmaz.

## Ölçümü yeniden çalıştırma

Ölçüm uzun sürdüğü için yalnızca istenince çalışır (veri üretimi yaklaşık 50 saniye, ölçüm 1–2 dakika):

```bash
export EI_TEST_SQL_CONNECTION="<test SQL Server bağlantı dizesi, veritabanı adı olmadan>"
export EI_PERF_REPORT=/tmp/perf.md          # sonuç tablosu; yanına perf.sql.md (ifade bazında SQL ve maliyet) yazılır
export EI_PERF_ASSETS=20000                 # isteğe bağlı, varsayılan 20.000
export EI_PERF_DATABASE=EI_Perf             # isteğe bağlı: veritabanını koru, sonraki çalıştırmada yeniden üretme
dotnet test tests/EnterpriseInventory.IntegrationTests --filter "FullyQualifiedName~LoadMeasurementTests"
```

`EI_PERF_DATABASE` verilmezse geçici bir veritabanı oluşturulup sonunda silinir. Ölçüm hesabı `VIEW SERVER STATE`
yetkisi ister (`sys.dm_exec_query_stats`); uygulamanın runtime hesabına bu yetki verilmez. Şirketin SQL Server'ında
çalıştırılacaksa ayrı bir test veritabanı ve DBA onayı gerekir; üretim veritabanında çalıştırılmaz.
