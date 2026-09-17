# MillaRemote

Daxili IT üçün **AnyDesk tipli uzaqdan dəstək aləti** — sıfırdan yazılıb (RDP-siz).
Admin (IT) bir kompüterə qoşulmaq istəyəndə, həmin kompüterdəki istifadəçiyə
əvvəlcə Azərbaycanca icazə pəncərəsi çıxır. İstifadəçi **Bəli** deyəndə admin
onun **ekranını canlı görür və idarə edir** (istifadəçi hər şeyi görür).

RDP-dən fərqi: RDP istifadəçini sessiyadan çıxarır və çoxlarında bağlıdır.
MillaRemote isə istifadəçinin **əsl (attended) sessiyasını** paylaşır.

```
┌────────────────┐  RequestConnection  ┌───────────────┐  ConnectionRequest  ┌─────────────────┐
│ Server (admin) │ ──────────────────► │  Relay (hub)  │ ──────────────────► │ Client (user PC)│
│                │ ◄────────────────── │  /remotehub   │ ◄────────────────── │  icazə MessageBox│
└───────┬────────┘  ConnectionResponse └───────────────┘  ConnectionResponse └────────┬────────┘
        │ (accepted + IP + port)                                    (Bəli → ekran serveri)
        │                                                                             │
        │            birbaşa TCP: ekran kadrları + input (siçan/klaviatura)           │
        └─────────────────────────── Viewer  ◄──────────────────────────────────────┘
```

## Layihələr

| Layihə | Tip | Təyinat |
|--------|-----|---------|
| **MillaRemote.Relay**  | ASP.NET Core 9 SignalR hub (`/remotehub`) | Mərkəzi siqnal serveri (kəşf + icazə) |
| **MillaRemote.Client** | Konsol (net9.0-windows, WinExe/səssiz) | User PC-lərinə GPO ilə yayılır; icazədən sonra ekran serveri |
| **MillaRemote.Server** | Konsol (admin aləti) | Adminin (Sulxay) istifadə etdiyi menyu |
| **MillaRemote.Viewer** | WinForms (net9.0-windows) | Uzaq ekranı göstərən + idarə edən pəncərə |
| **MillaRemote.Core**   | Kitabxana | Ekran tutma, kadr protokolu, stream server/receiver, input |
| **MillaRemote.Host**   | Konsol (test) | Streaming/protokol testləri: `serve`/`selftest`/`inputtest`/`relaytest` |
| **MillaRemote.CaptureProbe** | Konsol (test) | Ekran tutma testi |

## Arxitektura

- **Siqnal / kəşf / icazə** → SignalR relay (`/remotehub`).
- **Media (ekran + input)** → Client ilə Viewer arasında **birbaşa TCP** (hazırda LAN).
  Kadr protokolu: `[4 bayt uzunluq][JPEG]`. Input əks istiqamətdə eyni bağlantıda.
- İstifadəçi **Bəli** deyəndə Client ekran serverini başladır (sessiyaya bağlı:
  viewer ayrılanda və ya heç kim qoşulmayanda avtomatik dayanır — icazəsiz
  qoşulmanın qarşısını alır).

> **Uzaq/NAT arxası** kompüterlər üçün media axınını relay üzərindən keçirmək və
> şifrələmə **Mərhələ 6**-da planlaşdırılır. Hazırkı versiya **LAN** üçündür.

---

## 1. Relay serverini işə salmaq

Şəbəkədə bütün maşınların çata biləcəyi bir serverdə (Windows və ya Linux — relay
platformadan asılı deyil):

```bash
cd src/MillaRemote.Relay
dotnet run
```

Standart: `http://0.0.0.0:5100` (hub: `/remotehub`). Portu `appsettings.json` →
`Urls` ilə dəyişin. Yoxlama: `GET /health`, `GET /api/clients`.

---

## 2. Klienti GPO ilə yaymaq

Self-contained tək fayl (klientdə .NET tələb olunmur):

```bash
cd src/MillaRemote.Client
dotnet publish -c Release -r win-x64
```

Nəticə: `bin/Release/net9.0-windows/win-x64/publish/MillaRemote.Client.exe` (səssiz,
konsolsuz WinExe) + `appsettings.json`.

Yaymadan əvvəl `appsettings.json`-u tənzimləyin:

```json
{
  "Relay": { "Url": "http://RELAY-IP:5100/remotehub", "ReconnectDelaySeconds": 10 },
  "Stream": { "Port": 7000, "TargetFps": 12 }
}
```

Hər iki faylı NETLOGON-a kopyalayıb GPO ilə avtomatik başladın
(`deploy/install-client.bat` startup skripti və ya `deploy/MillaRemote-Client.xml`
zamanlanmış tapşırıq). Client-lərdə **7000 (stream) portu firewall-da açıq** olmalıdır.

---

## 3. Admin (server) alətini istifadə etmək

Admin maşınında **Server + Viewer** exe-ləri **yanaşı** olmalıdır (Server, Viewer-i
işə salır). `appsettings.json` → `Relay:Url` relay-ə işarə etməlidir.

```bash
cd src/MillaRemote.Server
dotnet run
```

Menyu:

```
  [1] Online olan kompüterlərin siyahısı
  [2] Kompüterə qoşul (hostname daxil et)
  [3] Çıx
```

- **[1]** — relay-dən onlayn kompüterlərin real-vaxt siyahısı.
- **[2]** — hostname yazırsınız → user-də icazə pəncərəsi çıxır. **Bəli** olanda
  Viewer avtomatik açılır və uzaq ekranı göstərir; siçan/klaviatura ilə idarə edirsiniz.
  **Xeyr** olanda "İstifadəçi rədd etdi".

---

## Test rejimləri (MillaRemote.Host)

```bash
# Bir maşında birbaşa streaming + idarə (relay olmadan):
MillaRemote.Host.exe serve 7000
MillaRemote.Viewer.exe --connect 127.0.0.1 7000

# Avtomatik testlər:
MillaRemote.Host.exe selftest    # şəbəkə yayımı
MillaRemote.Host.exe inputtest   # input protokolu
MillaRemote.Host.exe relaytest   # relay siqnal axını (relay işləməlidir)
```

## Relay-i Ubuntu-da Docker ilə qaldırmaq

Relay cross-platform-dur; Ubuntu server üçün Docker paketi `deploy/relay/`-dədir.

Ubuntu-da (relay serveri):

```bash
# 1) Docker (yoxdursa)
sudo apt update && sudo apt install -y docker.io docker-compose-v2
sudo systemctl enable --now docker

# 2) Firewall — 5100 portu
sudo ufw allow 5100/tcp

# 3) Layihəni serverə köçürün (məs. src/MillaRemote.Relay + deploy/relay),
#    sonra relay-i qaldırın:
cd deploy/relay
sudo docker compose up -d --build

# 4) Yoxlama
curl http://localhost:5100/health        # {"status":"ok",...}
sudo docker compose logs -f              # logları izlə

# 5) Serverin IP-si
hostname -I
```

Sonra **admin** və **client** `appsettings.json`-larında `Relay:Url`-i Ubuntu
serverin IP-si ilə əvəz edin, məs. `http://192.168.0.50:5100/remotehub`.

> **Şəbəkə qeydi:** Relay yalnız siqnal mübadiləsi edir. Media (ekran + input)
> hələ də **admin → client birbaşa** gedir. Ona görə admin, bütün client-lər və
> Ubuntu relay **bir-birinə çatan şəbəkədə** olmalıdır (eyni LAN). Relay client-in
> gördüyü IP-ni admin-ə ötürür; aralarında NAT olmamalıdır.

## Bant genişliyi optimizasiyası (tile-diffing)

Ekran 128×128 xanalara bölünür; hər kadrda yalnız **dəyişən xanalar** göndərilir.
Ekran tam sabit olanda **heç nə göndərilmir**.

- İlk kadr (keyframe): ~280–350 KB (bütün ekran, bir dəfə).
- Sonrakılar: yalnız dəyişən xanalar — adətən **2–6 KB**, sabit ekranda **0**.
- Nəticə: köhnə tam-kadr üsuluna nəzərən **~95%+ az bant**.

Konfiqurasiya (Client `appsettings.json` → `Stream`): `Port`, `TargetFps`, `TileSize`.

## Təhlükəsizlik və qalan işlər (hardening)

Hazırkı versiya **daxili LAN** üçün nəzərdə tutulub:

- ✅ **İcazə (consent):** hər sessiya istifadəçinin təsdiqini tələb edir; ekran
  serveri yalnız "Bəli"-dən sonra başlayır və viewer ayrılanda / heç kim
  qoşulmayanda (30 s) avtomatik dayanır.
- ⏳ **Sessiya tokeni:** icazə pəncərəsi açıq olan qısa müddətdə portu yalnız
  təsdiqlənmiş admin-in aça bilməsi üçün birdəfəlik token (tövsiyə olunan növbəti addım).
- ⏳ **Şifrələmə (TLS):** media axını hazırda şifrələnmir — internet üzərindən
  istifadədən əvvəl TLS əlavə edilməlidir.
- ⏳ **NAT / uzaq keçid:** fərqli şəbəkələrdəki kompüterlər üçün media axınını
  relay üzərindən tunelləmək (hazırda birbaşa LAN bağlantısı).

## Tələblər

- Relay: .NET 9 (ASP.NET Core), Windows və ya Linux.
- Server/Viewer/Client: net9.0-windows. Client self-contained yayımda runtime tələb etmir.
