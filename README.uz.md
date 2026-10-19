<div align="center">

# Arqon

**Windows'da Wi-Fi, Ethernet va telefon internetini bir vaqtda ishlating.**
Yuklash tezroq. Yuklash paytida o'yin qotmaydi. Bepul va ochiq kodli.

[**English**](README.md) · [**O'zbekcha**](README.uz.md) · [**Русский**](README.ru.md)

[![Build](https://github.com/OWNER/Arqon/actions/workflows/build.yml/badge.svg)](https://github.com/OWNER/Arqon/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/OWNER/Arqon?include_prereleases)](https://github.com/OWNER/Arqon/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/OWNER/Arqon/total)](https://github.com/OWNER/Arqon/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)

<img src="docs/img/uz-panel.png" alt="Arqon: Wi-Fi va USB telefon bitta ulanishga birlashtirilgan" width="820">

</div>

> *Arqon* so'zi "rope" (ip, arqon) degani: bir nechta ingichka ip, bitta mustahkam arqon.

## Nega Arqon

Ko'pchilikda internetga chiqishning bir nechta yo'li bor: uyda Wi-Fi, kabel, telefondagi mobil internet. Windows esa bir vaqtda faqat bittasini ishlatadi. Shuning uchun katta fayl sekin yuklanadi, yuklash ketayotganda o'yin esa qotadi.

Arqon **hammasini birga ishlatadi**:

- **Yuklash tezroq.** Fayl bo'laklarga bo'linadi va har bir bo'lak boshqa-boshqa ulanish orqali bir vaqtda yuklanadi. Haqiqiy sinovda Wi-Fi va USB telefon bilan 140 MB taxminan 10 soniyada yuklandi.
- **O'yin qotmaydi.** Arqon ishlayotgan o'yinni ko'radi, pingni kuzatadi va yuklashni faqat o'yin ishlatayotgan ulanishda, faqat kerakli darajada sekinlashtiradi. Keyin tezlikni qaytaradi.
- **Har qanday ilova foyda ko'radi.** *System mode* Telegram, Chrome, Steam va boshqa ilovalarning ulanishlarini barcha ulanishlarga taqsimlaydi. Birinchi haqiqiy sinovda 40 ta parallel yuklash 11,3 soniya o'rniga 5,3 soniyada tugadi.
- **Torrent ham.** Peer ulanishlari barcha ulanishlarga taqsimlanadi. Magnet link qo'ying yoki `.torrent` faylni oching, kerakli fayllarni tanlang.
- **Yashirin narsa yo'q.** Akkaunt yo'q, reklama yo'q, telemetriya yo'q. MIT litsenziyasi.

> Arqon faqat har bir ulanishning **o'z** internet yo'li bo'lganda tezlik qo'shadi (masalan, bitta routerning Wi-Fi'si va telefonning mobil interneti). Bitta router ortidagi ikkita adapter bitta chiziqni bo'lishadi va foyda bermaydi.

## Skrinshotlar

| Yuklashlar | Ilovalar | Statistika |
|---|---|---|
| <img src="docs/img/uz-downloads.png" width="270"> | <img src="docs/img/uz-apps.png" width="270"> | <img src="docs/img/uz-usage.png" width="270"> |

Interfeys **o'zbek va ingliz** tillarida (Sozlamalardan almashtiriladi). Skrinshotlar demo rejimda olingan.

## O'rnatish

1. [Oxirgi relizdan](https://github.com/OWNER/Arqon/releases/latest) **`Arqon.exe`** ni yuklab oling. Bu bitta fayl: o'rnatish shart emas, .NET ichida bor.
2. Ikki marta bosib oching. Windows SmartScreen "noma'lum ilova" deb ogohlantirishi mumkin, chunki fayl hali kod imzosiga ega emas ([Xavfsizlik](#xavfsizlik)). **More info → Run anyway** ni bosing.
3. Ikkinchi internetni ulang, masalan telefonni **USB modem** (USB tethering) rejimida (Android: Sozlamalar → Tarmoq → Hotspot va modem → USB modem).
4. **Boshqaruv** (Dashboard) sahifasini oching: ikkala ulanish ham ko'rinishi kerak. Sariq ogohlantirish chiqsa, **Fix** tugmasini bosing (aks holda Windows ikkinchi tarmoqni o'chirib qo'yishi mumkin).

Yuklab olingan faylni tekshirish uchun SHA-256 ni relizdagi `Arqon.exe.sha256` bilan solishtiring:

```powershell
(Get-FileHash .\Arqon.exe -Algorithm SHA256).Hash
```

### Qisqa tanishuv

| Sahifa | Nima qilinadi |
|---|---|
| **Boshqaruv** | Har bir ulanishni jonli ko'rish: tezlik, ping, jitter, yo'qotish. Windows ikkinchi tarmoqni o'chirib qo'yishini bir bosishda tuzatish. |
| **Yuklashlar** | Fayl yoki magnet link qo'ying yoki `.torrent` faylni tanlang. Papkani tanlang. Ko'p fayllik torrentda kerakli fayllarni belgilang. |
| **Ilovalar** | Ilova uchun qoida: avtomatik, faqat Wi-Fi, faqat telefon, ikkalasi yoki tezlik limiti. O'yinlar o'zi aniqlanadi. |
| **Statistika** | Har bir ulanish kuniga va oyiga qancha trafik sarflagani (telefon tarifi uchun qulay). |
| **Sozlamalar** | Til, tezlik birligi (Mbit/s yoki MB/s), o'yin himoyasi, system mode, torrentlar, Windows bilan ishga tushish. |

## System mode (hamma ilova uchun)

System mode barcha dasturlarning yangi ulanishlarini sizning ulanishlaringiz bo'yicha taqsimlaydi. Buning uchun imzolangan [WinDivert](https://reqrypt.org/windivert.html) paket drayveri va **administrator huquqi** kerak, shuning uchun u standart holatda o'chiq.

```powershell
# 1. Yordamchi skriptni yuklab oling (u drayverni o'rnatishdan oldin imzosini tekshiradi)
Invoke-WebRequest https://raw.githubusercontent.com/OWNER/Arqon/main/tools/get-windivert.ps1 -OutFile get-windivert.ps1

# 2. Drayverni Arqon.exe yoniga qo'ying (papkani o'zingizning Arqon.exe joyiga o'zgartiring)
powershell -ExecutionPolicy Bypass -File .\get-windivert.ps1 -Destination "C:\Tools\Arqon"
```

Keyin `Arqon.exe` ni **administrator sifatida** ishga tushiring va Sozlamalarda *System mode* ni yoqing. Biror narsa xato ketsa, Windows darhol odatdagidek yo'naltirishga qaytadi. Katalogdagi o'yin ishlayotganda Arqon system mode'ni o'chiradi va drayverni xotiradan chiqaradi (anti-cheat xavfsizligi uchun). Batafsil: [docs/steering.md](docs/steering.md).

## Buyruq qatori

Hammasi oynada bor, lekin quyidagi buyruqlar ham mavjud:

| Buyruq | Nima qiladi |
|---|---|
| `Arqon.exe` | Oddiy ishga tushirish. |
| `Arqon.exe "C:\yo'l\fayl.torrent"` | Torrent faylni qo'shadi (`.torrent` ustiga ikki marta bosganda shu ishlaydi). |
| `Arqon.exe "magnet:?xt=urn:btih:..."` | Magnet linkni qo'shadi. Ishlab turgan Arqon uni o'zi qabul qiladi. |
| `Arqon.exe --tray` | Tray'da yashirin ishga tushadi (*Windows bilan ishga tushish* shuni ishlatadi). |
| `Arqon.exe --demo` | Ikkita soxta ulanish, telefon shart emas. Interfeysni sinash uchun qulay. |
| `Arqon.exe --screenshots <papka>` | Har bir sahifaning rasmini papkaga saqlaydi. |

Sozlamalardagi **Torrent va magnet linklarni Arqon bilan ochish** tugmasi uni `.torrent` fayllar va `magnet:` linklar uchun ro'yxatdan o'tkazadi (faqat joriy foydalanuvchi uchun, administrator huquqisiz, boshqa dasturning tanlovini hech qachon tortib olmaydi).

## Manba koddan yig'ish

Windows 10/11 va [.NET 10 SDK](https://dotnet.microsoft.com/download) kerak.

```powershell
git clone https://github.com/OWNER/Arqon.git
cd Arqon

dotnet run --project src/Arqon.App            # haqiqiy adapterlar bilan ishga tushirish
dotnet run --project src/Arqon.App -- --demo  # ikkita soxta ulanish bilan
dotnet test                                    # 379 ta test

# bitta mustaqil Arqon.exe (taxminan 75 MB) publish\win-x64 papkasida
powershell -ExecutionPolicy Bypass -File tools\build.ps1
```

## Imkoniyatlar va holat

| Imkoniyat | Holat |
|---|---|
| Ulanishlarni topish (Wi-Fi / Ethernet / telefon) va jonli monitor: tezlik, ping, jitter, yo'qotish | Tayyor |
| Ko'p ulanishli yuklovchi: har bir ulanish orqali HTTP bo'laklar, davom ettirish, tezlik limiti, 429 / Retry-After'ni hurmat qilish | Tayyor |
| Yuklash navbati (bir vaqtda ko'pi bilan N ta, "Hozir boshlash") | Tayyor |
| O'yin ping himoyasi: yuklashni faqat o'yin ulanishida cheklaydi, keyin tezlikni sekin qaytaradi | Tayyor |
| Ilova qoidalari, o'yinlar katalogi, JSON sozlamalar | Tayyor |
| Windows ikkinchi tarmoqni o'chirib qo'yishini bir bosishda tuzatish | Tayyor |
| Har bir ulanish bo'yicha kunlik va oylik statistika | Tayyor |
| Tray, bildirishnomalar, buferdagi linkni taklif qilish, bitta nusxa, Windows bilan ishga tushish | Tayyor |
| Vazifalar panelida tezlik o'lchagichi | Tayyor (Windows 10, pastdagi panelda sinalgan) |
| Xavfsiz o'yin rejimi (o'yin paytida system mode o'chadi, drayver chiqariladi) | Tayyor (soxta o'yin bilan sinalgan, haqiqiy anti-cheat bilan emas) |
| Tezlik birligi (Mbit/s yoki MB/s) | Tayyor |
| Interfeys o'zbek va ingliz tilida | Tayyor (boshqa tillar xush kelibsiz) |
| **Torrent va magnet linklar** barcha ulanishlar bo'yicha; papka tanlash; fayl tanlash; Windows'dan ochish | Tayyor (haqiqiy Debian torrenti Wi-Fi + telefon bilan: ikkala adapter ham ma'lumot tashidi) |
| **System mode** (Telegram, Chrome va hamma ilovani tezlatish) | Birinchi haqiqiy sinovda ishladi (Wi-Fi + USB telefon). O'yin bilan va uzoq sessiyada hali sinalmagan. WinDivert va administrator huquqi kerak |
| Relay server (bitta ulanishni bo'laklash, o'yin paketlarini nusxalash) | Rejada |
| O'rnatuvchi va kod imzosi | Rejada |

Ochiq cheklovlar: torrent tugagach to'xtaydi (seeding hali yo'q), UPnP port ochish o'chiq, bitta faylli `Arqon.exe` dan system mode drayver bilan hali tekshirilmagan.

## Xavfsizlik

- **Telemetriya yo'q, akkaunt yo'q, yangilanish so'rovlari yo'q.** Arqon faqat siz so'raganga ulanadi. Qo'shimcha: ulanish monitori ping o'lchash uchun `1.1.1.1` / `8.8.8.8` (443-port) ga ulanadi, torrentlar esa har qanday torrent klient kabi ochiq trackerlar va DHT bilan bog'lanadi.
- Windows'ga o'zgartirish (registr, drayver) avval tushuntiriladi va qaytariladi. System mode standart holatda o'chiq va xato bo'lsa o'zi o'chadi.
- `Arqon.exe` hozircha **kod imzosiz**, shuning uchun SmartScreen ogohlantiradi. SHA-256 ni tekshirishingiz yoki shu manba koddan o'zingiz yig'ishingiz mumkin. Ochiq kodli loyihalar uchun bepul imzolash rejada.
- Zaiflik topdingizmi? [SECURITY.md](SECURITY.md) ni o'qing.

## Hissa qo'shish

Xato haqida xabar, g'oya, tarjima va pull request'lar xush kelibsiz. [CONTRIBUTING.md](CONTRIBUTING.md) dan boshlang. Yaxshi birinchi qadamlar: Arqon'ni o'zingizning ikki ulanishingizda sinab, natijani [issue](https://github.com/OWNER/Arqon/issues/new/choose) orqali yozing yoki interfeysga til qo'shing (`src/Arqon.App/Resources/Strings.*.xaml`; birinchi kerakli til: rus tili).

Arqon sizga foydali bo'lsa, GitHub'dagi ⭐ boshqalarga uni topishga yordam beradi.

## Litsenziya

MIT, [LICENSE](LICENSE) ga qarang. System mode [WinDivert](https://github.com/basil00/WinDivert) (LGPLv3) dan foydalanadi; u alohida yuklanadi va bu repozitoriyga kirmaydi. Torrentlar uchun [MonoTorrent](https://github.com/alanmcgovern/monotorrent) (MIT) ishlatiladi.
