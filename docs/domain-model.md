# Domain modeli (3. gün)

Kod: `src/EnterpriseInventory.Domain`. Testler: `tests/EnterpriseInventory.UnitTests/DomainModel`.

## Varlıklar

| Varlık | Açıklama |
| --- | --- |
| `Asset` | Demirbaş. Spesifikasyondaki tüm alanlar: AssetCode, ComputerName, BrandId, ModelId, SerialNumber, AssetType, Status, Description, CityId, DepartmentId, LocationId, CreatedAt/By, UpdatedAt/By, IsDeleted, RowVersion. Zimmetlerin sahibi (aggregate root). |
| `AssetAssignment` | Zimmet. AssetId, EmployeeId, AssignmentDescription (Zimmet Tanımı), AssignedAt/By, ReturnedAt, Notes. Ek olarak iadeyi kimin aldığı için `ReturnedBy`. Kayıtlar silinmez. |
| `Employee` | Zimmet verilebilen AD çalışanı (ObjectGuid, SamAccountName, DisplayName, Email, Department, Title, IsActive). Uygulamaya giriş yetkisi gerekmez. |
| `AdminUser` | Uygulamaya giriş yapan kullanıcı. Çalışanlardan ayrı tutulur. Şifre veya token saklanmaz; yetki her girişte AD grubundan kontrol edilir. |
| `Brand`, `AssetModel` | Marka ve model. Model tek bir markaya aittir. |
| `City`, `Department`, `Location` | Şehir, departman ve lokasyon. Lokasyon bir şehre aittir. |
| `AuditLog` | Değiştirilemez denetim kaydı: varlık, kayıt kimliği, işlem, eski/yeni değerler, kullanıcı, zaman, correlation ID. |

## İş kuralları

- Bir demirbaşın aynı anda en fazla bir aktif zimmeti olur. Yeni zimmet, önceki iadeden önceki bir tarihle başlatılamaz.
- Durum zimmet akışını izler: `Zimmetli` yalnızca zimmet verilerek, `Boşta` iade edilerek olur. Zimmetli bir cihazın durumu değiştirilemez ve arşivlenemez; önce iade alınmalıdır.
- Arşivleme soft delete'tir (`IsDeleted`). Arşivlenen demirbaş değiştirilemez, zimmet geçmişi korunur.
- Model seçilince marka modelden alınır; marka ve model hiçbir zaman çelişmez.
- Seçilen lokasyon seçilen şehre ait olmalıdır.
- Pasif marka, model, şehir, departman veya lokasyon yeni kayıtta seçilemez. Mevcut kayıtta zaten seçili olan pasif değer korunabilir.
- AD hesabı pasif olan çalışana zimmet verilemez.
- Boş seri numarası `null` olarak saklanır; dolu değerlerin benzersizliği filtreli indeksle sağlanır (bkz. [`database.md`](database.md)).
- Kurallar ihlal edildiğinde `DomainException` sabit bir kodla atılır (ör. `Asset.AlreadyAssigned`). API bu kodları Türkçe mesajlara çevirecek.

## Varsayımlar (gerekirse değiştirilebilir)

- Durumlar: Boşta, Zimmetli, Arızalı, Hurda.
- Demirbaş türleri: Masaüstü, Dizüstü, Monitör, Yazıcı, Telefon, Tablet, Sunucu, Ağ cihazı, Çevre birimi, Diğer.
- Bilgisayar adı, seri numarası, lokasyon ve açıklama isteğe bağlı; demirbaş kodu, tür, model, şehir ve departman zorunlu.
- Kullanıcı alanları (CreatedBy, AssignedBy vb.) AD kullanıcı adını metin olarak saklar.
