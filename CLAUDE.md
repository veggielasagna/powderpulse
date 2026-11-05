# Powder Pulse

Cel-shaded street-style snowboarding roguelite for Xbox Series X (Dev Mode first, Store later). The music the player picks is reshaped live by the ride (altitude, speed, air time, crashes, combos).

## Layout
- `PowderPulse/web/index.html` — the whole game (Three.js r128, Web Audio). `three.min.js` is downloaded by CI, not committed.
- `PowderPulse/MainPage.xaml.cs` — UWP host: WebView2 on virtual host `game.powderpulse`; polls the controller with Windows.Gaming.Input and posts `{type:'pad', b, x, y, rt}` (bit i = standard Gamepad button i); serves USB songs at `https://music.powderpulse/<index><ext>` with CORS so Web Audio can process them.
- Page → host message: `{type:'music.requestLibrary'}`. Host → page: `music.library` (`tracks:[{title,url}]`, `source`) and `music.status` (`text`).
- Log: `LocalState\powderpulse_log.txt` (Device Portal → File explorer).

## Build
Push to `main` → `.github/workflows/build.yml` on windows-2022 → artifact `Powder-Pulse-1.0.<run>.0-install` (sideload .msix + dependency .appx files + HOW-TO-INSTALL.txt). Same pipeline as WMC SX. Nothing compiles locally; CI is the compile check.

## Next
- NAS music over DLNA (button already sends `sources:['dlna','usb']`).
- Bundle the Google fonts so the game looks right offline.
