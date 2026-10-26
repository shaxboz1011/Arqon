# Yo'l xaritasi (Roadmap)

**🌐 Til:** [English](ROADMAP.md) · **O'zbekcha**

Arqon keyin qayerga boradi. Bu reja, va'da emas: sinov natijalariga qarab tartib o'zgarishi mumkin. G'oya va yordamni [issue](https://github.com/shaxboz1011/Arqon/issues/new/choose) orqali yuboring.

**Belgilar:** ✅ tayyor · 🧪 tayyor, haqiqiy sharoitda sinash kerak · 🔑 egasi kerak (akkaunt, maxfiy kalit yoki qurilma) · 🗓 rejada

```mermaid
flowchart LR
    A["🟢 0.3<br>Ishonch va tarqatish"] --> B["🔵 0.4<br>Yuklashni boyitish"]
    B --> C["🟣 0.5–0.6<br>Arqon Relay"]
    C --> D["🟠 0.7<br>Ekotizim"]
    D --> E["🔴 0.8<br>Aqlliroq ulanishlar"]
    E --> F["⭐ 1.0<br>Silliq va barqaror"]
```

## 🟢 0.3: Ishonch va tarqatish

Odamlar Arqonni ogohlantirishlarsiz o'rnata olmasa, yangi funksiyalar foyda bermaydi.

| Ish | Holat |
|---|---|
| Har qanday kutilmagan xato uchun crash log (UI, fon oqimlari, task'lar), o'zi qisqarib turadi | ✅ |
| *Sozlamalar → Muammo hisoboti → Hisobotni nusxalash*: versiya, Windows, ulanishlar, tizim rejimi, oxirgi xatolar; foydalanuvchi nomi, to'liq manzillar va papkalarsiz | ✅ |
| Har relizda winget manifestlari; `WINGET_TOKEN` secret qo'shilsa winget-pkgs'ga o'zi yuboriladi ([docs/winget.md](docs/winget.md)) | ✅ / 🔑 token |
| Kod imzosi (SignPath, ochiq loyihalar uchun bepul): ariza berish, keyin `release.yml` da `Arqon.exe` ni imzolash | 🔑 |
| Android 1.0 reliz: `android-release.yml` tayyor, imzo kaliti secret'lari kerak | 🔑 |
| System mode stress-testlari: uzoq sessiya, bitta faylli exe, anti-cheatli haqiqiy o'yinlar (VAC, EAC) | 🔑 qurilma |
| Scoop bucket va Microsoft Store (MSIX) paketi | 🗓 |

## 🔵 0.4: Yuklashni boyitish

| Ish | Holat |
|---|---|
| Chrome, Edge va Firefox uchun **brauzer kengaytmasi**: har bir linkda *Arqon bilan yuklash*, xohlasangiz har bir yuklash ([browser-extension/](browser-extension/README.md)) | 🧪 |
| `arqon://add?url=…` linklari: brauzer, bookmarklet yoki skript linkni Arqonga bera oladi (faqat web va magnet linklar) | ✅ |
| **Yuklash jadvali**: navbatdagi yuklashlar faqat tanlangan soatlarda boshlanadi (arzon tungi tarif) | ✅ |
| **Tanlangan ulanishlarda trafikni tejash**: yuklash va torrentlar ularni faqat oxirgi ulanish sifatida ishlatadi; xohlasangiz kunlik chegaraga yetganda barcha telefon ulanishlari ham | ✅ |
| Torrent: UPnP / NAT-PMP port ochish ixtiyoriy (standart o'chiq) | ✅ |
| Kengaytmani Chrome Web Store va addons.mozilla.org ga joylash | 🔑 |
| **HLS video** (`.m3u8`): segmentlar barcha ulanishlarga bo'linadi, master playlistdan eng yaxshi sifat, AES-128, fMP4, davom ettirish mumkin | ✅ |
| YouTube kabi saytlardan video (yt-dlp: oqimni topish, DASH video + audioni ffmpeg bilan birlashtirish) | 🗓 |
| Bitta faylni bir nechta oynadan (mirror) birdaniga yuklash (Metalink, mirror linklar ro'yxati) | 🗓 |
| Tugagan yuklashni **SHA-256 bilan tekshirish**: linkdan (`#sha256=…`) yoki nusxalangan hash / SHA256SUMS dan | ✅ |
| Torrent: RSS, ketma-ket yuklash (yuklanayotganda tomosha qilish) | 🗓 |

## 🟣 0.5–0.6: Arqon Relay (katta qadam)

Bitta TCP ulanish narigi tomonda biror narsa bo'lmasa ikki ulanishni ishlata olmaydi. Relay o'sha narsa. Dizayn: [docs/relay.md](docs/relay.md).

| Ish | Holat |
|---|---|
| Protokol poydevori: imzolangan freymlar (HMAC-SHA256), dublikatlarni tashlab, yo'qolganlarini o'tkazib yuboradigan tartiblash buferi, kutilgan yetib borish vaqti bo'yicha link tanlovchi | ✅ |
| O'zingiz joylashtiradigan relay server (Docker, VPS'da bitta buyruq) | 🗓 |
| Windows'da tunnel klienti (Wintun yoki mavjud WinDivert), har bir ulanishga bitta UDP soket | 🗓 |
| QR kod yoki link bilan ulash; kalit almashish; tunnel ichida ChaCha20-Poly1305 shifrlash | 🗓 |
| O'yin paketlarini nusxalash: bir ulanish paketni yo'qotsa ham lag bo'lmaydi | 🗓 |
| Har bir ulanish uchun tirbandlik nazorati; pingi oshgan ulanishdan trafikni olib qo'yish | 🗓 |
| Relay bilan va relaysiz tezlik testi, qo'shimcha "hop" haqida ochiq | 🗓 |

## 🟠 0.7: Ekotizim

| Ish | Holat |
|---|---|
| 127.0.0.1 da token bilan **mahalliy API**: ulanishlar va yuklashlarni o'qish, yuklash qo'shish ([docs/api.md](docs/api.md)) | ✅ |
| Buyruq qatori: `Arqon.exe "<link>"` web, magnet, torrent va `arqon://` linklarini ishlab turgan nusxaga beradi | ✅ |
| Telefon ↔ kompyuter: Android ilova telefonni kompyuterning ikkinchi ulanishi qilib o'zi sozlaydi (USB yoki Wi-Fi), sarf bitta joyda | 🗓 |
| Linux va macOS: `Arqon.Core` ning ko'p qismi oddiy .NET; faqat Windows'ga xos qismlar (registr, WinDivert) interfeyslar ortida; interfeys Avalonia'ga o'tishi mumkin | 🗓 |
| OpenWrt paketi: butun uy uchun ulanishlarni routerda birlashtirish | 🗓 |
| Mahalliy API ustida Home Assistant / Prometheus endpoint | 🗓 |

## 🔴 0.8: Aqlliroq ulanishlar

| Ish | Nega |
|---|---|
| **DNS so'rovlarini hamma ulanishdan birdaniga yuborish**: birinchi javob yutadi | Sahifalar tezroq ochila boshlaydi, o'lik ulanish nom qidirishni to'xtatmaydi |
| **Ulanishlararo "happy eyeballs"**: yangi ulanish eng yaxshi ikki linkni sinaydi, birinchi javob berganini qoldiradi | Sekin boshlanishlar kamroq |
| **Har bir ulanish sifatini kun vaqtiga qarab o'rganish** (telefon tunda tez, kechqurun sekin) va shunga qarab taqsimlash | Sozlamasiz yaxshiroq ishlatish |
| ✅ Dashboard'da **tarmoq tekshiruvi**: kirish sahifalari, internetsiz ulanishlar, bitta liniyadagi ikki ulanish (bir xil tashqi manzil), DNS, Windows siyosati. Keyinroq: MTU | Yordam so'rovlari kamroq |
| Raqamli xabarlar: "bugun telefon 2,1 GB tashidi, butun trafikning 38%" | Odamlar foydani ko'radi |

## ⭐ 1.0: Silliq va barqaror

- Yorug' mavzu, yuqori kontrast, to'liq klaviatura bilan ishlash, ekran o'quvchi uchun nomlar.
- Barqaror va beta yangilanish kanallari.
- Portativ exe yonida o'rnatuvchi (MSIX yoki Inno Setup).
- Hujjatlar sayti (GitHub Pages), telefon brendi va operator bo'yicha qo'llanmalar.
- Bir oy davomida muammo hisobotlarida ma'lum crash yo'q.

## ⚪ Doimiy

- Yangi interfeys tillari: qozoq, qirg'iz, tojik, turk, fors (Weblate kabi tarjima xizmati buni osonlashtiradi).
- Operatorlar juftligi bo'yicha haqiqiy natijalar sahifasi (masalan Uzmobile, Beeline, Ucell, Mobiuz), [sinov hisobotlari](https://github.com/shaxboz1011/Arqon/issues/new/choose) dan yig'iladi.
- "Good first issue" belgilari va yangi hissa qo'shuvchilar uchun qisqa arxitektura qo'llanmasi.
- Telegram kanal, GitHub Sponsors.
