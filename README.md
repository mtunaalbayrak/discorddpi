# Discord DPI

Bize ait Discord bağlantı motoru geliştirme projesi. Hazır bir DPI uygulaması veya hizmet kontrol arayüzü kullanılmaz. Ağ erişim katmanı olarak bağımsız WinDivert 2.2.2 kullanılır.

## İki çalışma modu

**İzlemeyi başlat:** Discord işlemlerini tanır, yeni TCP/UDP bağlantı olaylarını gösterir. Paket içeriği okunmaz veya değiştirilmez.

**Motoru dene:** Tanınan Discord işlemlerinin yeni TCP/443 bağlantılarında ilk veri paketini inceler. Tam TLS ClientHello içinde izin verilen Discord alan adı bulunursa SNI alanını iki TCP parçasına böler. Veri baytları korunur, sıra numarası ve checksum yeniden hesaplanır. Bu bağımsız ve deneysel bir yöntemdir. DNS eklendikten sonra kullanıcı Discord'a girişin çalıştığını bildirdi; o başarılı denemede TLS bölme gerçekleşmedi. Bu nedenle TLS bölmenin etkinliği henüz doğrulanmış değildir.

DNS adımı: Giden UDP/53 sorguları incelenir; sadece Discord alan adı listesindeki sorular Cloudflare'ın `https://1.1.1.1/dns-query` HTTPS uç noktasına iletilir. TLS sertifikası standart olarak doğrulanır. Yanıtın işlem kimliği, soru adı ve kayıt türü eşleşmelidir. Geçerli yanıt IPv4/IPv6 UDP paketi olarak istemciye döndürülür. Bu seçim **işleme değil alan adına** bağlıdır: tarayıcı veya Windows DNS hizmetinden gelen Discord sorguları da kapsamdadır. Diğer alan adları özgün çözümleyiciye iletilir. Windows DNS sunucu ayarı veya hosts dosyası değiştirilmez; işletim sistemi normal DNS yanıtlarını önbelleğe alabilir.

Cloudflare bu Discord DNS sorgularını ve istemcinin dış IP adresini görebilir. Yerel tanılama dosyaları gönderilmez. Uç nokta ulaşılamazsa, 4 saniyelik zaman aşımında veya kapasite dolduğunda özgün sorgu yeniden normal çözümleyiciye iletilir. Bu geri dönüş Discord için eski DNS hatasının devam etmesine yol açabilir; logda görünür. Aynı anda en çok 16 DoH isteği yapılır.

Her iki modda da Windows DNS, proxy veya otomatik başlangıç ayarları değiştirilmez.

WinDivert FLOW katmanı `SNIFF | RECV_ONLY` bayraklarıyla kullanılır. Program başlamadan kurulmuş bağlantılar gösterilmez; gözlemciyi başlattıktan sonra Discord'da yeni bir bağlantı oluşturun.

## Masaüstü ekranı

`bin/DiscordDpiDesktop.exe` dosyasını açıp Windows yönetici iznini onaylayın. Pencere Discord işlem sayısını gösterir. **İzlemeyi başlat** yeni bağlantı olaylarını listeler; **Durdur** yalnızca kendi gözlemci sürecimizi sonlandırır. Pencereyi kapatmak da gözlemi durdurur. Liste en son 500 olayı tutar.

Her denemede uygulamanın yanındaki `logs/session-*.txt` dosyasına tanılama kaydı yazılır. Kayıtlar zaman, işlem sayısı/PID, bağlantı IP/portları, işlenen Discord alan adı ve hata bilgisi içerir; mesajlar, ses veya paket içerikleri kaydedilmez. Oturum başına yaklaşık 250.000 karakterle sınırlıdır; eski kayıtlar gerektiğinde kullanıcı tarafından silinebilir. Dosyalar Git'e alınmaz ve kendiliğinden internete gönderilmez.

Motor başlayınca discord.com, gateway.discord.gg ve updates.discord.com için sistem DNS çözümü ve tek bir adrese TCP/443 erişimi ayrıca kontrol edilir. Bunlar motor sürecinden yapılan tanılama bağlantılarıdır; TLS, Discord oturumu veya tüm IPv4/IPv6 adresleri için başarı testi değildir. Sürücü açmadan aynı kontrol `DiscordDpi.exe --diagnose` ile yapılabilir.

GoodbyeDPI gözlem modunda açık kalabilir. Deneysel motor, GoodbyeDPI çalışırken başlamaz; onu kendiliğinden kapatmaz. Bu sadece eşzamanlı müdahaleyi önlemek için bir süreç kontrolüdür, başka bir motorun kodu veya çalıştırılabilir dosyası kullanılmaz.

Motor testi sırası: GoodbyeDPI'ı durdurun, **Motoru dene** düğmesine basın, Discord'u bildirim alanından tamamen kapatıp yeniden açın. `HEDEF` bağlantının seçildiğini, `BÖLÜNDÜ` paket işlemini, `DEĞİŞMEDİ` uygun başlangıç paketi bulunmadığını gösterir. `BÖLÜNDÜ` erişimin açıldığı anlamına gelmez; mesaj, ses ve yayın ayrı test edilmelidir.

Her mesaj bir bağlantı oluşturmaz. Önceden kurulmuş bağlantılar işlenmez.

## Kaynaktan derleme

Windows x64 ve .NET Framework 4.x gerekir. Proje klasöründe PowerShell:

```powershell
.\build.ps1
.\bin\DiscordDpi.exe --check
.\bin\DiscordDpi.exe --self-test
.\setup-windivert.ps1
.\bin\DiscordDpi.exe --native-check
.\bin\DiscordDpi.exe --packet-test
.\bin\DiscordDpi.exe --dns-test
# Sürücü açmadan gerçek DoH çözümleyicisini kontrol et:
.\bin\DiscordDpi.exe --doh-check
```

`setup-windivert.ps1` sürücüyü resmi WinDivert dağıtımından indirir ve sabit SHA-256 özetiyle doğrular. Başka bir uygulamanın sürücü dosyalarını kopyalamaz.

Canlı gözlem için yönetici olarak açılmış terminalde:

```powershell
.\bin\DiscordDpi.exe --observe
# Deneysel paket işleme:
.\bin\DiscordDpi.exe --engine
```

Ctrl+C ile sonlandırılır. Parametresiz açılış sadece işlem kontrolü yapar; sürücü açmaz. Çalışma modları ilk çalıştırmada WinDivert sürücüsünü yükleyebilir. Diğer WinDivert uygulamalarını durdurmaz veya kaldırmaz.

Açık olan eski arayüzün dosyalarını değiştirmeden derlemek için iki betiğe de `-OutputDirectory bin-engine` verilebilir. Bu çalışma sırasında yeni sürüm `bin-engine/DiscordDpiDesktop.exe` konumuna hazırlanmıştır.

## Sınırlar

- Standart `%LOCALAPPDATA%\Discord`, `DiscordPTB`, `DiscordCanary` kurulumları tanınır. İşlem adı ve kurulum yolu birlikte kontrol edilir; bu kontrol yayıncı/imza doğrulaması değildir.
- Tarayıcıdaki Discord, özel kurulum yolları ve güncelleyici henüz kapsamda değildir.
- Kimliği okunamayan veya kapanmış işlemler atlanır. PID yeniden kullanımını elemek için olay ve işlem başlangıç zamanları karşılaştırılır; bu gözlem kodu henüz üretim düzeyinde bir güvenlik sınırı değildir.
- Tanınan Discord işlemlerinin bağlantı bilgileri ve programın tanılama sonuçları ekrana/yerel tanılama kaydına yazılır. Paket içerikleri tutulmaz.
- Canlı gözlem yönetici izni ister; derleme, işlem kontrolü ve öz test istemez.
- TLS paket filtresi tanınmış bir Discord bağlantısının kaynak/hedef IP ve portlarına özeldir. DNS filtresi ise UDP/53 sorgularını alan adı ayrımı için yakalar; yalnızca Discord sorularını değiştirir. Diğer DNS paketleri kısa süreli yakalamadan sonra özgün hâliyle iletilir.
- Aynı anda en fazla 32 kısa ömürlü yakalayıcı açılır. İlk veriden sonra veya 8 saniye sonunda yakalama sonlandırılır; durdurmada kuyruktaki paketler geri gönderildikten sonra handle kapanır. Gönderim hataları TCP yeniden iletimi gerektirebilir.
- İlk veri paketi FLOW olayı işlenmeden geçerse kaçabilir. Parçalı ClientHello, ECH ile gizli SNI, QUIC, ses UDP trafiği, IP parçaları ve IPv6 uzantı başlıkları işlenmez. İlk veri denemesinden sonra yeniden iletimlere müdahale edilmez. DNS çözümü yalnızca standart UDP sorguları içindir; TCP DNS, uygulamanın kendi DoH'u, loopback resolver, sıkıştırılmış/çok sorulu DNS ve 1232 bayttan büyük DoH yanıtları kapsam dışıdır.
- Domain kapsamı: discord.com, discord.gg, discordapp.com, discordapp.net, discord.media, discordcdn.com ve bunların alt alan adları.
- Program başlarken başka bir DPI uygulamasını tespit etmek her çakışmayı önleyemez. Deneysel test sırasında başka motor başlatmayın.

## Küçük ders

PID çalışan programın kimliğidir. Yerel IP/port, uzak IP/port ve TCP/UDP bilgisi birlikte bağlantıyı tanımlar. WinDivert bağlantı olayının PID'sini verir; kendi kodumuz bunun Discord'a ait olup olmadığını kontrol eder.

- `src/DiscordProcess.cs`: Hedef işlemi tanıma.
- `src/Native.cs`: WinDivert çağrıları ve C# bellek düzeni.
- `src/Program.cs`: Gözlem döngüsü ve öz testler.

`src/ScopedEngine.cs` bağlantı başına filtreyi yönetir; `src/TlsSplitter.cs` yalnızca izin verilen ClientHello paketlerini böler. `src/PacketTests.cs` baytların korunmasını, sıra numarası taşmasını, bağımsız checksum doğrulamasını ve filtre sınırlarını test eder.

## Doğrulama

Gözlem modunda kullanıcının sesli kanaldan çıkıp tekrar girmesiyle bağlantı olayları görüldü. Deneysel motor için derleme, 10 işlem/bellek düzeni testi, 452 paket doğrulaması ve 5000 bozuk/kısmi girdi denemesi geçti. İlk kullanıcı denemesinde Discord'a giriş sağlanamadı; o denemede paket işleme satırları kaydedilmediğinden neden henüz belirlenemedi. Tanılama kayıtları bu ayrımı yapabilmek için eklendi. Canlı paket gönderme ve kapatma sırasında kuyruk boşaltma ayrıca doğrulanmalıdır.

İkinci kullanıcı denemesinin kaydı DNS hatalarını gösterdi: iki Discord alan adı çözülemedi; discord.com için dönen adrese TCP erişimi kurulamadı. TLS işleme satırı oluşmadı. Bu bulgu üzerine seçici DoH eklendi. 140 DNS doğrulaması ve 3000 bozuk girdi testi geçti; üç Discord alan adı resmi DoH uç noktasından başarılı yanıt aldı. Son kontrol sırasında kullanıcının GoodbyeDPI'ı yeniden açık olduğundan bu, DoH'un o kapalıyken erişilebilir olduğunu kanıtlamaz. Canlı DNS yanıt enjeksiyonu ve Discord girişi sonraki kullanıcı denemesinde doğrulanacaktır.

22 Eylül 2026, sonraki kullanıcı denemesi: **Discord'a giriş çalıştı.** Oturum kaydında Discord alan adları için başarılı DoH yanıtları ve yeni Discord bağlantıları görüldü. TLS hedef bağlantıları için yalnızca `DEĞİŞMEDİ` satırları vardı, `BÖLÜNDÜ` yoktu. Seçici DNS çözümü bu oturumda yeterli görünmektedir; ayrı DNS-only karşılaştırması yapılmadı. Ses sunucusu bağlantıları görüldü, ancak çift yönlü ses, ekran paylaşımı, diğer siteler üzerindeki etki ve uzun süreli kararlılık kullanıcı tarafından henüz ayrı ayrı doğrulanmadı. Motor durdurulurken işlem çıkış kodu 0 olarak kaydedildi. Ham oturum kayıtları ve kişisel bağlantı adresleri depoya eklenmedi.

DoH protokolü: https://developers.cloudflare.com/1.1.1.1/encryption/dns-over-https/make-api-requests/dns-wireformat/

WinDivert belgeleri: https://reqrypt.org/windivert-doc.html

Üçüncü taraf lisans bilgisi: `THIRD-PARTY-NOTICES.md`.
# Arka planda kullanım

Masaüstü arayüzünde X veya küçültme düğmesi pencereyi bildirim alanına gizler; çalışan motoru durdurmaz. Saatin yanındaki Discord DPI simgesine çift tıklayarak pencereyi açabilirsin. Simgenin sağ tık menüsündeki **Tamamen çık**, motoru durdurup uygulamayı kapatır. Simge Windows'un gizli simgeler okunun altında olabilir. Motor yine arayüzden başlatılır; Windows açılışında otomatik başlatma eklenmedi.

## v0.1.5 trafik kapsamı
DNS paketleri artık WinDivert filtresinde alan adı sonuna göre elenir. Küçük harfli Discord alan adları ve alt alanları için tek sorulu standart DNS veya seçeneksiz EDNS desteklenir. Büyük/karışık harfli, sıkıştırılmış ya da EDNS seçenekleri içeren sorgular özgün resolver'a gider; geniş yakalama yedeği yoktur. Bu biçimlerde Discord desteği azalabilir. Diğer uygulamaların desteklenen Discord alan adı sorguları yine kapsamdadır.
Motor modunda pasif FLOW bildirimleri yalnız TCP/443'e daraltıldı. Diğer uygulamaların bu porttaki bağlantı metadata'sı kimlik kontrolü için görülebilir; bu bildirimler paketleri bekletmez. İzleme modu ayrı olarak geniş bağlantı gözlemini korur. Tüm trafiğin görünmez olduğu veya gecikmenin sıfır olduğu iddia edilmez. Genel DNS/proxy ayarları değişmez.
444 DNS, 452 paket ve 10 temel doğrulama ile 8000 bozuk/kısmi girdi testi geçti. Canlı sürücü kuyruğu, tarayıcı gecikmesi ve farklı sağlayıcılarda uzun süreli kararlılık henüz ölçülmedi. v0.1.4'ün güncelleme bağlantısındaki deneysel ters gönderimi korunmuştur; modem bulgusundan dolayı bunun gerekli olduğu kanıtlanmış değildir.
