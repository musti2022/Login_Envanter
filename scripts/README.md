# scripts

Geliştirme ve kurulum için yardımcı betikler.

| Betik | Açıklama |
| --- | --- |
| [`sql/grant-runtime-permissions.sql`](sql/grant-runtime-permissions.sql) | Uygulamanın runtime SQL hesabına en az yetkiyi verir (DBA çalıştırır). Bkz. [`docs/database.md`](../docs/database.md). |
| [`test-report/summarize-results.py`](test-report/summarize-results.py) | Test sonuç dosyalarından (TRX, Playwright JSON) [`docs/test-report.md`](../docs/test-report.md)'deki kategori tablolarını üretir. |
| [`test-ad/setup-samba-ad.sh`](test-ad/README.md) | AD entegrasyon testleri için geçici bir Samba AD test domain'i (LDAPS) kurar. Yalnızca test içindir. |

IIS yayını için PowerShell betikleri (ön kontrol, kurulum, duman testi) [`deploy/iis`](../deploy/iis/README.md) altındadır.
