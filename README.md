# Discord DPI

Bize ait Discord bağlantı motoru geliştirme projesi. Hazır bir DPI uygulaması veya hizmet kontrol arayüzü kullanılmaz. Ağ erişim katmanı olarak bağımsız WinDivert 2.2.2 kullanılır.

## İlk aşama: bağlantı gözlemcisi

Discord işlemlerini tanır ve yeni TCP/UDP bağlantı olaylarını gösterir. **Henüz engel aşmaz.** Paket içeriği okunmaz, değiştirilmez veya yeniden gönderilmez. DNS ve diğer uygulamaların ayarları değiştirilmez.

WinDivert FLOW katmanı `SNIFF | RECV_ONLY` bayraklarıyla kullanılır. Program başlamadan kurulmuş bağlantılar gösterilmez; gözlemciyi başlattıktan sonra Discord'da yeni bir bağlantı oluşturun.

## Derleme

Windows x64 ve .NET Framework 4.x gerekir. Proje klasöründe PowerShell:

```powershell
.\build.ps1
.\bin\DiscordDpi.exe --check
.\bin\DiscordDpi.exe --self-test
.\setup-windivert.ps1
.\bin\DiscordDpi.exe --native-check
```

`setup-windivert.ps1` sürücüyü resmi WinDivert dağıtımından indirir ve sabit SHA-256 özetiyle doğrular. Başka bir uygulamanın sürücü dosyalarını kopyalamaz.

Canlı gözlem için yönetici olarak açılmış terminalde:

```powershell
.\bin\DiscordDpi.exe --observe
```

Ctrl+C ile sonlandırılır. Parametresiz açılış sadece işlem kontrolü yapar; sürücü açmaz. `--observe` ilk çalıştırmada WinDivert sürücüsünü yükleyebilir. Diğer WinDivert uygulamalarını durdurmaz veya kaldırmaz.

## Sınırlar

- Standart `%LOCALAPPDATA%\Discord`, `DiscordPTB`, `DiscordCanary` kurulumları tanınır. İşlem adı ve kurulum yolu birlikte kontrol edilir; bu kontrol yayıncı/imza doğrulaması değildir.
- Tarayıcıdaki Discord, özel kurulum yolları ve güncelleyici henüz kapsamda değildir.
- Kimliği okunamayan veya kapanmış işlemler atlanır. PID yeniden kullanımını elemek için olay ve işlem başlangıç zamanları karşılaştırılır; bu gözlem kodu henüz üretim düzeyinde bir güvenlik sınırı değildir.
- Yalnızca tanınan Discord işlemlerinin bağlantı bilgileri ekrana yazılır; kalıcı trafik kaydı tutulmaz.
- Canlı gözlem yönetici izni ister; derleme, işlem kontrolü ve öz test istemez.

## Küçük ders

PID çalışan programın kimliğidir. Yerel IP/port, uzak IP/port ve TCP/UDP bilgisi birlikte bağlantıyı tanımlar. WinDivert bağlantı olayının PID'sini verir; kendi kodumuz bunun Discord'a ait olup olmadığını kontrol eder.

- `src/DiscordProcess.cs`: Hedef işlemi tanıma.
- `src/Native.cs`: WinDivert çağrıları ve C# bellek düzeni.
- `src/Program.cs`: Gözlem döngüsü ve öz testler.

Sonraki aşama: bu tanımayı paket katmanına taşımak ve seçilen bağlantılara kendi paket işleme yöntemimizi uygulamak. FLOW olayı ilk paketten sonra gelebilir; gözlemci doğrudan bir paket filtresi olarak kullanılamaz.

## Doğrulama

Derleme, işlem tanıma, 10 öz test ve sürücüyü açmadan DLL/filtre/IP dönüşümü kontrolü yapıldı. Canlı bağlantı gözlemi ve engel aşma henüz doğrulanmadı.

WinDivert belgeleri: https://reqrypt.org/windivert-doc.html

Üçüncü taraf lisans bilgisi: `THIRD-PARTY-NOTICES.md`.
