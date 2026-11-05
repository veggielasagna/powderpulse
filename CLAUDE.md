# Powder Pulse

Cel-shaded street-style snowboarding roguelite for Xbox Series X (Dev Mode first, Store later). The music the player picks is reshaped live by the ride (altitude, speed, air time, crashes, combos).

## Layout
- `PowderPulse/web/index.html` — the whole game (Three.js r128, Web Audio). `three.min.js` is downloaded by CI, not committed.
- `PowderPulse/MainPage.xaml.cs` — UWP host: WebView2 on virtual host `game.powderpulse`; polls the controller with Windows.Gaming.Input and posts `{type:'pad', b, x, y, rt}` (bit i = standard Gamepad button i); serves USB songs at `https://music.powderpulse/<index><ext>` with CORS so Web Audio can process them.
- Page → host message: `{type:'music.requestLibrary', quiet}` (sent quietly at launch, and by Find my music). Host scans the NAS (DLNA, `Services/Dlna.cs` copied from WMC SX) and USB together, shuffles, and replies `music.library` (`tracks:[{title,url}]`, `source` nas|usb) or `music.status` (`text`).
- NAS crawl: Music-looking top containers, then the first "All Music/Songs/Tracks" list it meets; otherwise walks folders (max 300 browse calls, 3,000 songs). Songs are served at `https://music.powderpulse/s/<n>`: USB files stream directly, NAS songs are downloaded whole into memory (max 80 MB, 2 at a time) because WebResourceRequested needs a seekable stream.
- `Microsoft.Web.WebView2 1.0.4078.44` must stay referenced explicitly, or Xbox reports "WebView2 runtime not detected" (learned on WMC SX).
- Don't commit home-network details (NAS names, IPs); the repo may be public.
- Log: `LocalState\powderpulse_log.txt` (Device Portal → File explorer).

## Build
Push to `main` → `.github/workflows/build.yml` on windows-2022 → artifact `Powder-Pulse-1.0.<run>.0-install` (sideload .msix + dependency .appx files + HOW-TO-INSTALL.txt). Same pipeline as WMC SX. Nothing compiles locally; CI is the compile check.

## Next
- Bundle the Google fonts so the game looks right offline.
