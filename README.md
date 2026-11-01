# LightBrowser

Windows için hafif, Chromium (Microsoft Edge WebView2) tabanlı bir tarayıcı. Bu sürüm sağlam ve genişletilebilir bir temel sunar; düşük RAM kullanımı için optimizasyonlar sonraki aşamalarda eklenecektir.

## Teknolojiler

- C# / .NET 10 (LTS), yalnızca Windows
- WPF + XAML, MVVM
- Microsoft Edge WebView2 (`Microsoft.Web.WebView2`)
- `Microsoft.Extensions.DependencyInjection` ve `Microsoft.Extensions.Logging`
- xUnit

## Geliştirme ortamı

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2026 (veya .NET 10 destekleyen bir sürüm) — "Masaüstü geliştirme (.NET)" iş yükü
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Evergreen). Windows 11'de ve güncel Edge ile birlikte genellikle kuruludur. Kurulu değilse uygulama anlaşılır bir hata mesajı gösterip kapanır.

## Visual Studio ile çalıştırma

1. `LightBrowser.sln` dosyasını açın.
2. Başlangıç projesinin `LightBrowser.App` olduğundan emin olun.
3. NuGet paketleri otomatik geri yüklenir. `F5` (hata ayıklama) veya `Ctrl+F5` ile çalıştırın.

Komut satırı: `dotnet run --project src/LightBrowser.App` — testler: `dotnet test`.

Günlükler `%LOCALAPPDATA%\LightBrowser\logs`, WebView2 kullanıcı verisi `%LOCALAPPDATA%\LightBrowser\WebView2`, son siteler `%LOCALAPPDATA%\LightBrowser\recent-sites.json` altında tutulur.

## Klasörler

| Klasör | Görev |
|---|---|
| `src/LightBrowser.Core` | WPF/WebView2'den bağımsız model, arayüz (`IWebViewHost`, `IWebViewHostFactory`, `IAddressParser`, `ISystemMetricsProvider`, `IRecentSitesStore`), adres ayrıştırma (`Policies`), `TabManager` ve `RecentSites`. |
| `src/LightBrowser.Infrastructure` | WebView2 adaptörü (`WebView2/`), dosya günlüğü (`Logging/`), CPU/RAM okuma (`Monitoring/`), son siteler JSON deposu (`Storage/`). |
| `src/LightBrowser.App` | WPF arayüzü: `Views/` (üst çubuk, sekmeler, gezinme, ana sayfa), `Controls/` (WebView paneli), `ViewModels/`, `Services/` (tema), `Models/`, `Resources/` (stiller, temalar, arka plan resimleri), DI kurulumu. |
| `tests/LightBrowser.Tests` | WebView2 Runtime gerektirmeyen xUnit testleri (sahte `IWebViewHost` ile). |
| `docs/` | Mimari dokümantasyon. |

## Desteklenen özellikler

- Çoklu sekme: açma, seçme, kapatma (son sekme kapanınca yeni boş sekme açılır); sekmelerde favicon
- Geri, ileri, yenile; düğmeler gerçek WebView2 durumuna bağlıdır
- Adres çubuğu: URL veya arama; şemasız alan adları HTTPS ile açılır, `localhost`/IP için HTTP
- Yükleme göstergesi, sayfa başlığı ve adresin canlı güncellenmesi
- Ortak tek `CoreWebView2Environment`, sekme başına ayrı `WebView2` kontrolü
- Özel (çerçevesiz) pencere: sol üstte saat / RAM / CPU kutusu (`<` `>` ile geçiş), renkli pencere düğmeleri (yeşil: büyüt, sarı: küçült, kırmızı: kapat), ayarlar ve profil simgesi
- Ana sayfa: arka plan resmi, odaklanınca iki yana genişleyen arama kutusu, son 3 sitenin kısayol kutuları
- Ayarlar (geçici, kalıcı değil): ana sayfa arka planı (6 manzara) ve tema (açık gri / renkli mor)
- Klavye: `Ctrl+T`, `Ctrl+W`, `Ctrl+L`/`Alt+D`/`F6`, `F5`/`Ctrl+R`, `Alt+←`/`Alt+→`, `Enter`
- `target=_blank` / açılır pencereler şimdilik aynı sekmede açılır

## Henüz uygulanmayan özellikler

Yer imleri (düğme pasif), profil/hesap girişi, ayarların kalıcı olması, RAM optimizasyonu / sekme askıya alma, geçmiş (SQLite), parola yöneticisi, reklam engelleyici, eklentiler, VPN/proxy, senkronizasyon, yapay zekâ özellikleri, otomatik güncelleme, sekme sürükle-bırak, indirme yönetimi.

## Manuel test adımları (WebView2 entegrasyonu)

1. Uygulamayı başlatın: tek bir boş sekme (ana sayfa) açılmalı; sol üstteki kutuda saat görünmeli, `>` ile RAM ve CPU'ya geçilmeli.
2. Ana sayfada arama kutusuna tıklayın: kutu iki yana yumuşakça genişlemeli; `example.com` yazıp Enter: sayfa `https://example.com/` olarak açılmalı; yükleme sırasında üstte ince bir çubuk görünmeli; sekme başlığı, favicon ve adres güncellenmeli.
3. `merhaba dünya` yazıp Enter: arama sonuç sayfası açılmalı.
4. Bir bağlantıya tıklayın: Geri düğmesi etkinleşmeli; Geri/İleri çalışmalı, Yenile sayfayı yeniden yüklemeli.
5. `+` (veya `Ctrl+T`) ile yeni sekme açın: ana sayfada az önce gezilen siteler kısayol kutusu olarak görünmeli (en fazla 3); birine tıklayınca site açılmalı.
6. Sekmeler arasında geçiş yapın: her sekme kendi sayfasını/adresini korumalı.
7. Sekmedeki `×` veya `Ctrl+W` ile sekme kapatın: komşu sekme etkinleşmeli. Son sekmeyi kapatın: uygulama çökmemeli, yeni boş sekme açılmalı.
8. Üç çizgi (ayarlar) düğmesi: arka plan küçük resmine tıklayınca ana sayfa arka planı, temaya tıklayınca tüm renkler değişmeli.
9. Pencere düğmeleri (yeşil/sarı/kırmızı), boş üst çubuğu sürükleme ve çift tıklayıp büyütme çalışmalı.
10. Pencereyi küçültün (≈640 px): adres çubuğu taşmamalı, sekmeler kesilmeli.
11. `Tab`/`Shift+Tab` ile düğmelere odaklanın: odak çerçevesi görünmeli.
12. Runtime yokluğu (isteğe bağlı): WebView2 Runtime kurulu olmayan makinede açılışta hata iletisi görülmeli.
13. Kapanışta günlükte hata olmadığını ve `msedgewebview2` işlemlerinin uygulama kapanınca bittiğini doğrulayın.
