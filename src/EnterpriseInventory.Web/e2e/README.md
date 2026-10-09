# Uçtan uca testler (Playwright)

`npm run test:e2e`, React uygulamasının üretim derlemesini (`vite preview`) Chromium'da açar ve Vite proxy'si
üzerinden gerçek API'ye bağlanır. API `Development` ortamında, sahte dizinle (`ActiveDirectory:Mode=Fake`) ve gerçek
bir SQL Server veritabanıyla çalışır. Testlerin ne denediği: [`docs/web-auth.md`](../../../docs/web-auth.md#testler) ve
[`docs/inventory-ui.md`](../../../docs/inventory-ui.md#test-sonuçları).

## Bir kerelik hazırlık

1. Yalnızca bu testler için boş bir veritabanı oluşturup migration'ları uygulayın (test sunucusunda; testler
   `AdminUsers`, `UserSessions` ve `AuditLogs` tablolarına; envanter testleri de her çalıştırmada benzersiz bir önekle
   demirbaş, marka, model, şehir, lokasyon ve departman ekler, bu yüzden üretim veritabanına bağlanmayın):

   ```bash
   # SQL Server'da: CREATE DATABASE [EI_E2E] COLLATE Turkish_CI_AS;
   export ConnectionStrings__Migrations="<test sunucusu bağlantı dizesi>;Database=EI_E2E"
   dotnet tool restore
   dotnet ef database update --project src/EnterpriseInventory.Infrastructure
   ```

2. ASP.NET Core geliştirme sertifikasını Node'a tanıtın (`NODE_EXTRA_CA_CERTS`, bkz. kök
   [`README.md`](../../../README.md#geliştirme-ortamında-çalıştırma)). Proxy API sertifikasını doğrular; doğrulama
   kapatılmaz.

3. Chromium (Playwright 1.56.1 sürümüyle eşleşen): `npx playwright install chromium`.

## Çalıştırma

```bash
export EI_E2E_SQL_CONNECTION="<test sunucusu bağlantı dizesi>;Database=EI_E2E"
export NODE_EXTRA_CA_CERTS="$HOME/.aspnet/https/aspnet-dev-cert.pem"
cd src/EnterpriseInventory.Web
npm run test:e2e
```

Playwright API'yi `https://localhost:7262`, uygulamayı `http://localhost:4174` adresinde kendisi başlatır ve sonunda
kapatır; bu portlar boş olmalıdır. İki ortam değişkeni de eksikse testler hiç başlamaz.

Sahte dizinde üç kullanıcı vardır: `e2e.admin` (`Bim_Envanter` üyesi), `e2e.outsider` (grup dışı) ve `e2e.disabled`
(pasif). Parolaları her çalıştırmada rastgele üretilir ve yalnızca API sürecine ortam değişkeniyle verilir; repoya,
`user-secrets` deposuna veya bir dosyaya yazılmaz.
