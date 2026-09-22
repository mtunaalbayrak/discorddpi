# Discord DPI

Bize ait Discord bağlantı motoru geliştirme projesi. Hazır bir DPI uygulaması veya hizmet kontrol arayüzü kullanılmaz. Ağ erişim katmanı olarak bağımsız WinDivert 2.2.2 kullanılır.

## İki çalışma modu

**İzlemeyi başlat:** Discord işlemlerini tanır, yeni TCP/UDP bağlantı olaylarını gösterir. Paket içeriği okunmaz veya değiştirilmez.

**Motoru dene:** Tanınan Discord işlemlerinin yeni TCP/443 bağlantılarında ilk veri paketini inceler. Tam TLS ClientHello içinde izin verilen Discord alan adı bulunursa SNI alanını iki TCP parçasına böler. Veri baytları korunur, sıra numarası ve checksum yeniden hesaplanır. Bu bağımsız ve deneysel bir yöntemdir; internet sağlayıcısında işe yaradığı henüz doğrulanmamıştır.

Her iki modda da Windows DNS, proxy veya otomatik başlangıç ayarları değiştirilmez.

WinDivert FLOW katmanı `SNIFF | RECV_ONLY` bayraklarıyla kullanılır. Program başlamadan kurulmuş bağlantılar gösterilmez; gözlemciyi başlattıktan sonra Discord'da yeni bir bağlantı oluşturun.

## Masaüstü ekranı

`bin/DiscordDpiDesktop.exe` dosyasını açıp Windows yönetici iznini onaylayın. Pencere Discord işlem sayısını gösterir. **İzlemeyi başlat** yeni bağlantı olaylarını listeler; **Durdur** yalnızca kendi gözlemci sürecimizi sonlandırır. Pencereyi kapatmak da gözlemi durdurur. Liste en son 500 olayı tutar, diske trafik kaydı yazmaz.

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
- Yalnızca tanınan Discord işlemlerinin bağlantı bilgileri ekrana yazılır; kalıcı trafik kaydı tutulmaz.
- Canlı gözlem yönetici izni ister; derleme, işlem kontrolü ve öz test istemez.
- Paket yakalama tüm tarayıcı trafiğine uygulanmaz. Her ağ filtresi tanınmış bir Discord bağlantısının kaynak/hedef IP ve portlarına özeldir; bağlantı kimliği işlem katmanında, alan adı TLS katmanında kontrol edilir.
- Aynı anda en fazla 32 kısa ömürlü yakalayıcı açılır. İlk veriden sonra veya 8 saniye sonunda yakalama sonlandırılır; durdurmada kuyruktaki paketler geri gönderildikten sonra handle kapanır. Gönderim hataları TCP yeniden iletimi gerektirebilir.
- İlk veri paketi FLOW olayı işlenmeden geçerse kaçabilir. Parçalı ClientHello, ECH ile gizli SNI, QUIC, UDP, IP parçaları, IPv6 uzantı başlıkları ve DNS engelleri bu sürümde çözülmez. İlk veri denemesinden sonra yeniden iletimlere müdahale edilmez.
- Domain kapsamı: discord.com, discord.gg, discordapp.com, discordapp.net, discord.media, discordcdn.com ve bunların alt alan adları.
- Program başlarken başka bir DPI uygulamasını tespit etmek her çakışmayı önleyemez. Deneysel test sırasında başka motor başlatmayın.

## Küçük ders

PID çalışan programın kimliğidir. Yerel IP/port, uzak IP/port ve TCP/UDP bilgisi birlikte bağlantıyı tanımlar. WinDivert bağlantı olayının PID'sini verir; kendi kodumuz bunun Discord'a ait olup olmadığını kontrol eder.

- `src/DiscordProcess.cs`: Hedef işlemi tanıma.
- `src/Native.cs`: WinDivert çağrıları ve C# bellek düzeni.
- `src/Program.cs`: Gözlem döngüsü ve öz testler.

`src/ScopedEngine.cs` bağlantı başına filtreyi yönetir; `src/TlsSplitter.cs` yalnızca izin verilen ClientHello paketlerini böler. `src/PacketTests.cs` baytların korunmasını, sıra numarası taşmasını, bağımsız checksum doğrulamasını ve filtre sınırlarını test eder.

## Doğrulama

Gözlem modunda kullanıcının sesli kanaldan çıkıp tekrar girmesiyle bağlantı olayları görüldü. Deneysel motor için derleme, 10 işlem/bellek düzeni testi, 452 paket doğrulaması ve 5000 bozuk/kısmi girdi denemesi geçti. Bunlar sürücü açmadan yapılan testlerdir. Canlı paket gönderme, kapatma sırasında kuyruk boşaltma ve engel aşma henüz kullanıcı bağlantısında doğrulanmadı.

WinDivert belgeleri: https://reqrypt.org/windivert-doc.html

Üçüncü taraf lisans bilgisi: `THIRD-PARTY-NOTICES.md`.
