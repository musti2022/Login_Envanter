# Test AD (Samba)

`setup-samba-ad.sh`, Active Directory entegrasyon testleri için **geçici** bir Samba AD domain controller'ı kurar.
Gerçek bir domain'e dokunmaz; yalnızca test içindir.

```bash
sudo apt-get install samba-ad-dc samba ldb-tools ldap-utils openssl   # Ubuntu/Debian
sudo scripts/test-ad/setup-samba-ad.sh            # ilk çalıştırmada kurar ve başlatır (varsayılan: /opt/ei-test-ad)
sudo scripts/test-ad/setup-samba-ad.sh /opt/ei-test-ad stop

set -a; . /opt/ei-test-ad/test-ad.env; set +a    # EI_TEST_AD_* değişkenleri
dotnet test EnterpriseInventory.slnx              # AD testleri artık atlanmaz
```

Kurulanlar:

| Öğe | Değer |
| --- | --- |
| Domain | `envanter.test` (NetBIOS `ENVANTER`), `.test` RFC 2606 ile test için ayrılmıştır |
| Domain controller | `dc1.envanter.test` → `127.0.0.1` (`/etc/hosts`), yalnızca loopback'te dinler |
| LDAPS | 636; sertifika betiğin ürettiği test CA'sı ile imzalanır. CA'nın özel anahtarı imzadan sonra silinir. Düz LDAP'ta (389) parola ile bind reddedilir. |
| `Bim_Envanter` | İzin verilen grup |
| `Envanter_Ekibi` | `Bim_Envanter` içinde iç içe grup |
| `OU=Sahte` içinde `CN=Bim_Envanter` | Aynı adı taşıyan, SID'i farklı tuzak grup |

| Kullanıcı | Durum |
| --- | --- |
| `ayse.admin` | `Bim_Envanter` doğrudan üyesi |
| `mehmet.user` | Grup üyesi değil |
| `nested.user` | Yalnızca `Envanter_Ekibi` üzerinden (iç içe) üye |
| `primary.user` | `Bim_Envanter` birincil grubu (`primaryGroupID`) |
| `decoy.user` | Yalnızca tuzak gruptaki üye |
| `disabled.user` | Üye, hesap pasif |
| `expired.user` | Üye, hesap süresi dolmuş |
| `mustchange.user` | Üye, parolasını değiştirmesi gerekiyor |
| `svc.envanter` | Servis hesabı |

Test kullanıcılarının ortak parolası, servis hesabı parolası ve grup SID'leri rastgele üretilir ve yalnızca
`<hedef klasör>/test-ad.env` dosyasına yazılır. Dosyanın izni 600'dür ve sahibi betiği `sudo` ile çalıştıran
kullanıcıdır; testler root olmadan çalışır. Test CA sertifikası (`tls/ca.crt`) herkesçe okunabilir; domain
veritabanı (`private/`) ve DC'nin özel anahtarı yalnızca root'a açıktır. Bu dosyalar repoya eklenmez.

Betik `samba-tool`'u, Samba Python modüllerini yükleyebilen yorumlayıcıyla çalıştırır (varsayılan `python3`
farklı bir sürümse diye). Docker gerektirmez.
