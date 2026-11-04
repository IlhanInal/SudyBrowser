<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="src/LightBrowser.App/Resources/Icons/sudybrowser_logo_white.svg">
  <source media="(prefers-color-scheme: light)" srcset="src/LightBrowser.App/Resources/Icons/sudybrowserlogo.svg">
  <img alt="SudyBrowser Logo" src="src/LightBrowser.App/Resources/Icons/sudybrowserlogo.svg" width="340">
</picture>

### **S**imple · **U**ltra-light · **D**ynamic · **Y**ours

**Windows için düşük bellek tüketimi, hız ve modern kişiselleştirme odaklı yeni nesil masaüstü web tarayıcısı.**

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF%20%2F%20XAML-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![WebView2](https://img.shields.io/badge/Engine-Edge%20WebView2-0078D7?style=for-the-badge&logo=microsoftedge&logoColor=white)
![SQLite](https://img.shields.io/badge/Storage-SQLite%20%26%20JSON-003B57?style=for-the-badge&logo=sqlite&logoColor=white)
![Tests](https://img.shields.io/badge/Tests-126%20Passing-2EA043?style=for-the-badge)

</div>

---

## 🎯 SudyBrowser Nedir?

**SudyBrowser**, günümüz tarayıcılarının yüksek RAM tüketimi ve karmaşık arayüzlerine alternatif olarak geliştirilmiş; **C# / .NET 10**, **WPF (MVVM)** ve **Microsoft Edge WebView2 (Chromium)** altyapısını kullanan hafif, hızlı ve tamamen özelleştirilebilir bir Windows tarayıcısıdır.

Gereksiz arka plan servislerinden arındırılmış temiz mimarisi (Clean Architecture) sayesinde yalnızca ihtiyaç duyulan bileşenleri bellekte tutar, alt menü koleksiyonlarını işiniz bittiği anda temizler ve sistem kaynaklarınızı gerçek zamanlı olarak üst çubuktan takip etmenize olanak tanır.

---

## ✨ Mevcut Özellikler

### 🧭 Akıllı Gezinme ve Omnibox (Adres Çubuğu)
- **Hibrit Öneri Motoru (`SuggestionService`):** Siz yazarken yerel gezinme geçmişi (**SQLite**), sık kullanılan bilinen siteler (`KnownSites`) ve isteğe bağlı çevrimiçi arama önerilerini gerçek zamanlı olarak harmanlar.
- **Satır İçi Otomatik Tamamlama (Inline Completion):** Adres çubuğunda yazarken en uygun eşleşmeyi anında tamamlar.
- **Akıllı Adres Ayrıştırma (`AddressParser`):** Şemasız alan adlarını (`example.com`) otomatik olarak `HTTPS` ile, yerel adresleri (`localhost`, IP) `HTTP` ile açar; URL olmayan ifadeleri doğrudan arama motoruna yönlendirir.
- **Hızlı Favicon Önbelleği (`FaviconCacheService`):** Site simgelerini bellek ve disk üzerinde önbelleğe alarak tekrarlayan ağ isteklerini ve arayüz takılmalarını önler.

### 🎨 Kişiselleştirilebilir Ana Sayfa ve Akıllı Tema Motoru
- **Kalıcı Tema ve Arka Plan Seçimi (`JsonSettingsStore`):** Seçtiğiniz renk teması ve ana sayfa arka planı veritabanına (`settings.json`) kaydedilir; tarayıcıyı her açtığınızda bıraktığınız görünümle başlar.
- **4 Renk Teması & 10 Arka Plan Seçeneği:**
  - *Temalar:* Açık Gri, Renkli (Mor), Kırmızı ve Dark (Koyu).
  - *Arka Planlar:* 6 doğa manzarası (*Yeşil Tepeler, Nehir Vadisi, Orman ve Deniz, Karlı Dağlar, Kış Kampı, Fiyort*) + 4 modern gradyan (*Açık Gri, Gece Mavisi, Gün Batımı, Zümrüt Yeşili*).
- **Algısal Parlaklık (Luminance) Tabanlı Otomatik Logo (`ThemeService`):**
  - **ITU-R BT.709** algısal parlaklık formülü ($0.2126R + 0.7152G + 0.0722B$) ile aktif temanın açık veya koyu olduğunu **sıfır ek bellek maliyetiyle** hesaplar.
  - Açık temalarda koyu/gri logoyu, koyu ve canlı temalarda ise beyaz logoyu otomatik olarak seçer. Yakında eklenecek kullanıcı özel temalarına tam uyumludur.
- **Dinamik Yer İşareti Kısayolları:** Ana sayfadaki arama kutusunun altında en son kaydedilen **maksimum 6 yer işareti** hızlı erişim kutusu olarak listelenir. Hiç yer işareti yoksa kalabalık yapmamak adına bu alan tamamen gizlenir.

### 🗂️ Çoklu Sekme ve Özel Çerçevesiz Pencere
- **Dinamik Sekme Şeridi (`TabStripPanel`):** Sekme sayısı arttıkça sekme genişliklerini pürüzsüzce ölçeklendiren özel panel. Son sekme kapatıldığında tarayıcı kapanmaz, otomatik olarak yeni bir ana sayfa sekmesi açılır.
- **Canlı Sistem & Uygulama Monitörü (`StatusViewModel`):** Sol üst köşedeki bilgi kapsülünden `<` ve `>` oklarıyla **Canlı Saat**, **Tarayıcı RAM Kullanımı (MB)**, **Sistem RAM Oranı** ve **CPU Yüzdesi** arasında geçiş yapabilirsiniz.
- **Ortak WebView2 Ortamı:** Tüm sekmeler tek bir `CoreWebView2Environment` örneğini paylaşarak gereksiz süreç çoğalmasını engeller.

### ⚙️ Çoklu Görünümlü Ayarlar, Geçmiş ve İndirme Yönetimi
- **Yer İşaretleri (`BookmarkService`):** Adres çubuğundaki yıldız simgesiyle sayfaları tek tıkla kaydedin, ayarlar menüsünden yönetin veya ana sayfadan anında erişin.
- **Gelişmiş Arama Geçmişi (`SqliteHistoryStore`):**
  - *Bugün*, *Dün* ve tam tarih başlıklarıyla gruplandırılmış geçmiş listesi.
  - Geçmiş içinde anlık metin filtreleme, tekil/çoklu seçimle silme veya zaman aralığına göre (*Son yarım saat, Son 1 saat, Son 1 gün, Son 1 hafta, Tümü*) toplu temizleme.
- **İndirme Yöneticisi (`DownloadsViewModel`):** Dosya indirmelerini anlık ilerleme durumuyla takip etme, iptal etme, tamamlanan dosyayı açma veya klasörde gösterme.
- **Güvenli Mod & Performans Modu:**
  - **Güvenli Mod:** Etkinleştirildiğinde gezinme geçmişi ve son siteler kaydedilmez, çevrimiçi öneriler duraklatılır.
  - **Performans Modu:** Açılır pencereler kapandığında alt görünüm koleksiyonlarını bellekten तुरंत serbest bırakır ve agresif bellek temizliği (`GC.Collect`) uygular.

---

## 🚀 Yol Haritası ve Eklenecek Özellikler (Roadmap)

SudyBrowser'ı Windows'un en az kaynak tüketen ve en esnek tarayıcısı yapmak için üzerinde çalışılan yeni özellikler:

- [ ] 🧠 **Akıllı Sekme Uyutma (Auto Tab Sleeping & Hibernation):**
  - Belirli bir süre (örn. 5-15 dk) arka planda kalan pasif sekmelerin WebView2 süreçlerini otomatik olarak askıya alma (`TrySuspendAsync`) ve DOM belleğini sıkıştırma. Bu sayede çoklu sekme kullanımında **%65–75 oranında RAM tasarrufu**.
- [ ] ⚡ **Dinamik Bellek Bütçesi ve Süreç Havuzu Optimizasyonu (Memory Cap & Process Pooling):**
  - Sekme başına maksimum RAM limiti belirleme, arka plan sekmelerinde V8 JavaScript motoru için düşük bellek bayrakları (`--js-flags="--lite-mode --optimize-for-size"`) ve GPU önbellek budama mekanizması.
- [ ] 🎨 **Kullanıcı Özel Tema ve Duvar Kağıdı Yükleme Sistemi (Custom Theme Engine):**
  - Kullanıcıların kendi renk paletlerini (`.json` / `.xaml`) ve özel arka plan görsellerini içe aktarabilmesi (mevcut otomatik kontrast/parlaklık algoritması özel temalar için şimdiden hazır!).
- [ ] 🛡️ **Yerleşik Hafif Reklam ve İzleyici Engelleyici (Zero-Overhead AdBlocker):**
  - Eklenti yüküne ihtiyaç duymadan doğrudan WebView2 ağ katmanında (`WebResourceRequested`) çalışan, düşük bellek izli reklam ve telemetri filtreleme motoru.
- [ ] 🪟 **Bölünmüş Ekran (Split View) ve Çalışma Alanları (Workspaces):**
  - Tek tarayıcı penceresinde iki sekmeyi yan yana kullanabilme ve *Okul / İş / Günlük* sekmelerini ayrı çalışma alanlarında gruplayabilme.
- [ ] ⌨️ **Hızlı Komut Paleti (`Ctrl+K`):**
  - Fareye dokunmadan açık sekmeler, yer işaretleri, geçmiş ve tema/performans komutları arasında anında arama ve geçiş.
- [ ] 🔐 **Yerel Şifreli Kasa (Windows DPAPI Parola ve Oturum Yöneticisi):**
  - Buluta veri göndermeden, tamamen cihaz üzerinde Windows veri koruma API'si ile şifrelenmiş parola ve form otomatik doldurma desteği.

---

## 🏗️ Mimari ve Klasör Yapısı

Proje, sorumlulukların net ayrıldığı katmanlı bir yapıda tasarlanmıştır:

```text
SudyBrowser/
├── src/
│   ├── LightBrowser.Core/           # Arayüzden bağımsız çekirdek modeller, politikalar ve servisler
│   │   ├── Interfaces/              # IWebViewHost, IHistoryStore, IBookmarkStore, ISettingsStore vb.
│   │   ├── Models/                  # BrowserTab, BookmarkEntry, HistoryEntry, UserSettings, SystemMetrics
│   │   ├── Policies/                # AddressParser, SuggestionRanker, FaviconUrlPolicy, InlineCompletion
│   │   └── Services/                # TabManager, BookmarkService, SuggestionService, HistoryRecorder
│   │
│   ├── LightBrowser.Infrastructure/ # Dış sistem, veritabanı, işletim sistemi ve WebView2 adaptörleri
│   │   ├── Logging/                 # Dosya tabanlı günlükleme (FileLoggerProvider)
│   │   ├── Monitoring/              # Windows CPU ve RAM metrik okuyucusu (WindowsSystemMetricsProvider)
│   │   ├── Storage/                 # SqliteHistoryStore, JsonBookmarkStore, JsonSettingsStore
│   │   ├── Suggestions/             # HttpSearchSuggestionProvider & HttpFaviconFetcher
│   │   └── WebView2/                # WebView2Host, WebView2HostFactory, WebView2DownloadOperation
│   │
│   └── LightBrowser.App/            # WPF kullanıcı arayüzü (MVVM)
│       ├── Controls/                # TabStripPanel, WebViewHostPanel
│       ├── Models/                  # ThemeOption, BackgroundOption, ShortcutSlot
│       ├── Resources/               # Temalar (Light, Colorful, Red, Dark), İkonlar, Manzara & Gradyanlar
│       ├── Services/                # ThemeService (parlaklık duyarlı logo seçici), FaviconCacheService
│       ├── ViewModels/              # MainViewModel, SettingsViewModel, OmniboxViewModel, DownloadsViewModel
│       └── Views/                   # TopBarView, TabStripView, NavigationBarView, HomePageView
│
├── tests/
│   └── LightBrowser.Tests/          # WebView2 Runtime gerektirmeyen 126 adet kapsamlı xUnit birim testi
└── docs/
    └── architecture.md              # Mimari tasarım notları
```

### 💾 Yerel Veri Depolama (`%LOCALAPPDATA%\SudyBrowser`)
- `settings.json` — Seçili tema, arka plan ve öneri tercihleri
- `bookmarks.json` — Kaydedilen yer işaretleri
- `recent-sites.json` — Son ziyaret edilen siteler
- `history.db` — SQLite tabanlı gezinme geçmişi veritabanı
- `logs/` — Uygulama çalışma günlükleri
- `WebView2/` — Chromium profil ve önbellek dizini

---

## 🛠️ Kurulum ve Çalıştırma

### Gereksinimler
- **İşletim Sistemi:** Windows 10 veya Windows 11
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Motor:** [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) *(Windows 11 ve güncel Windows 10 sistemlerde yerleşik olarak bulunur)*

### Komut Satırı ile Çalıştırma

```bash
# Depoyu klonlayın
git clone https://github.com/KULLANICI_ADINIZ/SudyBrowser.git
cd SudyBrowser/LightBrowser

# Bağımlılıkları geri yükleyin ve derleyin
dotnet build LightBrowser.sln

# Uygulamayı başlatın
dotnet run --project src/LightBrowser.App/LightBrowser.App.csproj

# Birim testlerini çalıştırın (126 test)
dotnet test LightBrowser.sln
```

---

## ⌨️ Klavye Kısayolları

| Kısayol | İşlev |
| :--- | :--- |
| `Ctrl + T` | Yeni sekme aç |
| `Ctrl + W` | Aktif sekmeyi kapat |
| `Ctrl + L` / `Alt + D` / `F6` | Adres çubuğuna odaklan |
| `F5` / `Ctrl + R` | Sayfayı yenile |
| `Alt + Sol Ok` | Geri git |
| `Alt + Sağ Ok` | İleri git |
| `Enter` | Adres çubuğunda veya ana sayfada aramayı/adresi başlat |
| `Yukarı / Aşağı Ok` | Adres çubuğu önerileri arasında gezin |
| `Esc` | Açık olan öneri kutusunu veya ayarlar menüsünü kapat |

---

<div align="center">
  <sub><b>SudyBrowser</b> — Hız, sadelik ve düşük bellek tüketimi için özenle geliştirildi.</sub>
</div>
