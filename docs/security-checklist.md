# Güvenlik kontrol listesi (36. gün)

Her kontrolün nasıl sağlandığı ve onu kanıtlayan test. **Durum** sütunu:

- **Test edildi**: otomatik test geçiyor (test adı verildi; çalıştırma sonuçları [`test-report.md`](test-report.md)).
- **Samba ile test edildi**: Samba test domain'inde gerçek LDAPS ile geçiyor; şirketin AD'si ile denenmedi.
- **Ortamda doğrulanacak**: kodda hazır, ama şirketin sunucusunda, IIS'te veya gerçek AD'de denenmesi gerekiyor;
  [yayın öncesi listesi](#yayın-öncesi-ortamda-doğrulanacaklar).

## Erişim kontrolü

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Varsayılan olarak her uç nokta kapalı | Fallback politikası `Administrator` rolü ister; yalnızca dört uç nokta anonimdir (health live/ready, CSRF token, giriş) | `EndpointAccessTests.Only_the_health_probes_csrf_token_and_sign_in_are_marked_anonymous` | Test edildi |
| Ziyaretçi hiçbir uç noktaya erişemez | Yönlendirme tablosundaki **her** uç nokta ve metot (hub dahil) anonim istekle `401` | `EndpointAccessTests.A_visitor_is_refused_everywhere_but_the_health_probes_and_sign_in` | Test edildi |
| Rolü olmayan kullanıcı erişemez | Aynı tablo, `Administrator` rolü olmayan oturumla `403` | `EndpointAccessTests.A_signed_in_user_without_the_administrator_role_is_refused_everywhere_but_the_anonymous_endpoints` | Test edildi |
| Yeni eklenen uç nokta unutulmaz | Testler uç nokta listesini elle değil `EndpointDataSource`'tan okur; liste boş ya da eksikse koruma testi kırılır | `EndpointAccessTests.The_routing_table_holds_the_api_and_the_hub`; bilinçli hata (`AllowAnonymous` eklenmiş gösterge paneli) üç testçe yakalandı | Test edildi |
| Yalnızca `Bim_Envanter` grubu girer | Grup SID'i ile kontrol (ad ile değil); iç içe grup politikası ayardan | `SambaSignInTests`, `SambaAccessCheckTests`, `A_userprincipalname_claiming_a_members_logon_name_does_not_let_its_owner_in_as_that_member` | Samba ile test edildi |
| Erişimi AD'de alınan kullanıcının oturumu biter | Açık oturumlar düzenli aralıkla AD'de yeniden kontrol edilir; hub bağlantısı da kapanır | `Losing_access_in_the_directory_ends_the_session_at_the_next_check`, `A_connection_is_closed_when_the_directory_takes_the_users_access_away` | Test edildi |
| Health ayrıntıları gizli | Anonim probe'lar yalnızca `Healthy`/`Unhealthy` der; ayrıntı yöneticiye; probe'lar yalnızca `GET`/`HEAD` | `Details_require_an_administrator`, `Details_name_the_failing_check_without_exception_messages`, `EndpointAccessTests.The_health_probes_answer_only_reads` | Test edildi |
| SignalR hub'ı korumalı | Oturum, rol, CSRF token ve aynı origin şartı; istemci hiçbir metodu çağıramaz | `InventoryHubTests` (`An_anonymous_visitor_cannot_connect_with_any_transport`, `A_page_on_another_site_cannot_connect_even_with_the_users_cookie`, `A_client_can_call_nothing_on_the_hub`) | Test edildi |

36. günde bulunan ve düzeltilen: health check uç noktaları her HTTP metoduna cevap veriyordu (ör. anonim `POST
/api/health/live`, CSRF kontrolünde `400` ile duruyordu, veri değiştirmiyordu). Artık yalnızca `GET` ve `HEAD`.

## Oturum ve CSRF

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Durum değiştiren her istek CSRF token ister | Global ara katman; `GET`/`HEAD`/`OPTIONS` dışında token yoksa `400 csrf_invalid` | `EndpointAccessTests.Every_state_changing_endpoint_refuses_an_administrator_without_a_csrf_token` (yönlendirme tablosundaki her yazma uç noktası), `Every_state_changing_request_needs_the_signed_in_users_csrf_token` (başka kullanıcının ve girişten önceki token'ı da reddedilir) | Test edildi |
| Oturum sunucu tarafında | Çerez yalnızca anahtar taşır; veritabanında anahtarın özeti; çıkışta oturum sunucuda biter | `Only_a_hash_of_the_session_key_is_stored`, `Signing_out_ends_the_session_on_the_server_so_the_old_cookie_is_worthless`, `A_valid_cookie_without_a_server_side_session_is_refused` | Test edildi |
| Güvenli çerez | `Secure`, `HttpOnly`, `SameSite=Strict`, host'a bağlı (`__Host-`) | `A_member_gets_a_secure_session_cookie_that_grants_access`, `The_csrf_cookie_is_a_host_only_secure_http_only_cookie` | Test edildi |
| Boşta ve mutlak süre | 20 dakika boşta, 8 saat mutlak (ayardan) | `A_session_ends_after_twenty_idle_minutes_and_activity_keeps_it_alive`, `A_session_ends_eight_hours_after_sign_in_however_active` | Test edildi |
| Çerez anahtarları kalıcı | Data Protection anahtarları ayarlı klasörde; Development dışında klasör yoksa uygulama başlamaz | `Sessions_survive_a_restart_because_the_cookie_keys_are_kept`, `Without_a_safe_place_for_the_cookie_keys_the_application_does_not_start` | Test edildi; IIS'te klasör izni ortamda doğrulanacak |
| Giriş denemesi sınırlı | İstemci adresi başına giriş limiti, kullanıcı başına genel limit, `429` + `Retry-After` | `Sign_in_attempts_are_rate_limited_per_client_address`, `Requests_over_the_limit_get_429_with_retry_after`, `Each_signed_in_user_has_a_separate_limit` | Test edildi |
| Kullanıcı adı ifşa edilmez | Parola doğrulanmadan önceki retlerde hesap durumu söylenmez | `Account_state_is_not_revealed_without_the_right_password`, `Refusals_before_the_password_was_accepted_mask_the_user_name` | Test edildi |

## Parola ve hassas veri

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Parola loglanmaz | Giriş isteği gövdesi loglanmaz; hata mesajlarında parola yok | `Passwords_never_reach_the_log`, `Passwords_never_reach_the_log_with_the_test_directory`, `The_password_is_never_logged` | Test edildi |
| Parola saklanmaz | Veritabanında parola alanı yok; audit kaydında parola yok | `Sign_ins_are_listed_without_the_password`, model testleri | Test edildi |
| Tarayıcıda saklama yok | Uygulama kodu `localStorage`, `sessionStorage`, IndexedDB ve `document.cookie` kullanmaz | `browserStorage.test.ts` | Test edildi |
| Depoda sır yok | Git'teki dosyalarda özel anahtar, sertifika dosyası, yapılandırma/doküman/betikte parola değeri yok | `RepositorySecretsTests` (bilinçli olarak eklenen sızıntı dosyası yakalandı) | Test edildi |
| Üretim ayarları örnek | `appsettings.Production.json` yalnızca yer tutucu içerir; yer tutucularla uygulama başlamaz | `Shipped_production_settings_cannot_start_until_the_placeholders_are_replaced`, `Placeholders_from_the_sample_configuration_are_refused` | Test edildi |

## TLS ve sertifika doğrulaması

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| AD yalnızca LDAPS | Düz LDAP ayarı uygulamayı durdurur; sertifika zinciri, ad ve süre sıkı doğrulanır | `Plain_ldap_stops_the_application`, `LdapsCertificateValidatorTests`, `SambaLdapsTests` | Samba ile test edildi |
| Doğrulamayı kapatan ayar yok | Test dışındaki dosyalarda `TrustServerCertificate=true`, `Encrypt=false`, `rejectUnauthorized: false`, `NODE_TLS_REJECT_UNAUTHORIZED` yok | `RepositorySecretsTests.Nothing_outside_the_tests_switches_off_certificate_validation` | Test edildi |
| HTTPS zorunlu | HTTP isteği HTTPS'e yönlenir; Development dışında HSTS (geliştirici makinesi HTTPS'e kilitlenmez) | `TransportSecurityTests` | Test edildi; IIS binding ortamda doğrulanacak |

## Girdi ve enjeksiyon

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| SQL enjeksiyonu | Bütün sorgular EF Core ile parametreli; ham SQL'e kullanıcı girdisi eklenmez | `InputSecurityTests.Sql_and_like_metacharacters_match_nothing_and_change_nothing`: 14 saldırı girdisi × 12 serbest metin filtresi; hepsi `200` (bulunan yok) veya `400`, hiç `500` yok, girdi SQL metninde geçmiyor, tablolar değişmiyor | Test edildi |
| LIKE joker karakterleri | `%`, `_`, `[`, `\` düz karakter olarak aranır | `AssetFilterTests` (`%`, `_`, `[a]`), `InputSecurityTests` | Test edildi |
| LDAP enjeksiyonu | Filtre değerleri RFC 4515'e göre kaçışlanır; `*`, `)(` içeren kullanıcı adı dizine gitmeden reddedilir | `DirectoryValuesTests`, `LdapEmployeeDirectoryTests`, `Unusable_input_is_refused_without_contacting_the_directory`, `SambaEmployeeDirectoryTests` | Samba ile test edildi |
| Toplu atama (mass assignment) | İstek tipleri yalnızca düzenlenebilir alanları taşır; bilinmeyen JSON alanları yok sayılır | `InputSecurityTests.A_new_asset_ignores_fields_the_server_sets` (`id`, `isDeleted`, `createdBy`, `createdAt`, `rowVersion`, zimmet), `An_update_cannot_archive_reassign_or_rewrite_who_created_the_asset`, `A_new_asset_cannot_start_assigned` | Test edildi |
| Büyük gövde | Giriş isteği gövdesi sınırlı; okunamayan gövde `400` | `Oversized_bodies_are_refused`, `A_request_body_that_cannot_be_read_is_400_in_every_environment` | Test edildi |
| Excel formül enjeksiyonu | Hücreler metin olarak yazılır, `=`, `+`, `-`, `@` ile başlayan metin formül olmaz | `Text_that_looks_like_a_formula_is_written_as_text`, `Text_that_looks_like_a_formula_stays_text` | Test edildi; Microsoft Excel'de açılarak denenmedi |
| XSS | React metni kaçışlar; `dangerouslySetInnerHTML` kullanılmaz; API yanıtlarında `default-src 'none'` CSP | Kod taraması (36. gün: kullanım yok), `Api_responses_carry_security_headers_and_are_never_cached` | Test edildi |
| Açık yönlendirme | Girişten sonra dönülecek sayfa URL'den değil uygulama içi yönlendirme durumundan gelir | `LoginPage.tsx` | Kod incelemesi |

## Veri bütünlüğü ve denetim

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Sessiz veri ezme yok | RowVersion uyuşmazlığında `409` ve Türkçe çakışma uyarısı | `An_update_of_an_older_version_gets_409_and_keeps_what_the_other_user_saved`, `A_made_up_row_version_gets_409`, `ConcurrencyTests` | Test edildi |
| Kritik işlemler transaction içinde | Zimmet, iade, konum ve arşiv audit kaydıyla birlikte commit; SignalR bildirimi commit'ten sonra | `A_change_that_is_rolled_back_is_not_announced`, `AssignmentConsistencyTests` | Test edildi |
| Audit kaydı eksiksiz ve değişmez | Varlık, kayıt kimliği, işlem, eski/yeni değer, kullanıcı, zaman, correlation ID; yalnızca ekleme | `Each_kind_of_change_gets_its_own_audit_record_with_old_and_new_values`, `Audit_log_is_append_only` | Test edildi |

## Hata yanıtları ve başlıklar

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Hata ayrıntısı sızmaz | `500` yanıtında istisna mesajı ve yığın yok; Türkçe başlık ve correlation ID | `Unhandled_exceptions_return_500_without_exception_details`, `Errors_have_a_turkish_title_and_the_correlation_id_of_the_response` | Test edildi |
| Bilinmeyen adres bir şey söylemez | Oturumsuz istek önce `401` alır | `Unknown_routes_require_sign_in_before_revealing_anything` | Test edildi |
| Güvenlik başlıkları | `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, COOP, `Permissions-Policy`; API'de CSP ve `no-store` | `Api_responses_carry_security_headers_and_are_never_cached` | Test edildi |
| CORS yok | Başka origin'e izin verilmez | `Cross_origin_requests_get_no_cors_permission` | Test edildi |

## Veritabanı yetkileri ve ortam

| Kontrol | Nasıl | Kanıt | Durum |
| --- | --- | --- | --- |
| Uygulama şemayı değiştirmez | Başlangıçta `Migrate`/`EnsureCreated` yok; runtime hesabı yalnızca okuma/yazma | `The_API_never_creates_or_migrates_the_database_when_it_starts`, [`grant-runtime-permissions.sql`](../scripts/sql/grant-runtime-permissions.sql) | Test edildi; şirket SQL Server'ında ortamda doğrulanacak |
| Sahte AD üretimde çalışmaz | `Fake` modu Development dışında uygulamayı durdurur | `Fake_directory_stops_the_application_outside_development`, `Fake_directory_is_refused_outside_development` | Test edildi |

## Bağımlılık taraması

10 Ekim 2026'da çalıştırıldı:

| Komut | Sonuç |
| --- | --- |
| `dotnet list package --vulnerable --include-transitive` | Altı projenin hiçbirinde bilinen açık yok (nuget.org) |
| `npm audit` (`src/EnterpriseInventory.Web`) | 0 açık (geliştirme bağımlılıkları dahil) |

Tarama o günün açık veritabanına göredir; her yayından önce yeniden çalıştırılır.

## Yayın öncesi ortamda doğrulanacaklar

Bunlar bu depoda test edilemedi; yapılmadan "tamam" denmez:

- [ ] Şirketin AD'si ile LDAPS giriş, grup SID kontrolü, devre dışı/süresi dolmuş hesap ve çalışan araması
  (Samba'daki testlerin aynısı, [`active-directory.md`](active-directory.md)).
- [ ] Şirketin SQL Server'ında runtime ve migration hesaplarının ayrı olduğu, runtime hesabının şema değiştiremediği.
- [ ] IIS'te HTTPS binding, geçerli sertifika, HSTS ve HTTP'den yönlendirme.
- [ ] Data Protection anahtar klasörünün yalnızca app pool kimliğine açık olduğu, log klasörü izinleri.
- [ ] Excel dışa aktarımlarının Microsoft Excel'de açıldığı ve formül çalıştırmadığı.
- [ ] Şirket güvenlik ekibinin sızma testi (bu liste onun yerini tutmaz).
