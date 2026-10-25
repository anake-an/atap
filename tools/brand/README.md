# Brand generator

`generate.cs` makes every Atap brand file from the one SVG master. Run it from the repo root:

```
dotnet run tools/brand/generate.cs
```

You need the .NET 10 SDK on Windows or macOS. SkiaSharp has no Linux native assets for this setup. The first run downloads SkiaSharp (MIT) from nuget.org. The outputs are checked in, so only run it after the master changes. Then commit what it wrote.

## Master

`assets/brand/atap-icon.svg` is the one Atap logo. Every size uses it, from the 16 px tray icon to the 1024 px picture, so the app, the installer, the Store and the website all show the same drawing.

The generator reads `<rect>` and `<path>` elements and their `x`, `y`, `width`, `height`, `rx`, `d` and `fill` values. It skips `<title>` and stops with an error on other elements. Other attributes such as `transform`, `fill-rule`, `opacity` and `stroke` are ignored without a warning, so do not use them in the master.

## What it writes

- `assets/brand/atap-mark.svg`, `atap-mark-on-dark.svg`, `atap-mark-mono-black.svg` and `atap-mark-mono-white.svg`. These are the full master without its tile, recoloured, in a tight viewBox.
- `assets/brand/png/atap-<size>.png` at 16, 20, 24, 32, 40, 48, 64, 128, 256, 512 and 1024 px.
- `src/Atap.App/Assets/atap.ico` with 16, 20, 24, 32, 40, 48, 64, 128 and 256 px images. The 128 and 256 px images are stored as PNG. The exe, taskbar, Start, tray and the Velopack installer use this file.
- `src/Atap.App/Ui/Theme/Brand.xaml`, the logo as WPF shapes under the key `Brand.Logo`. The Settings window uses it, so the logo stays sharp at any size and display scale.
- `assets/brand/web/favicon.svg`, `favicon-48.png`, `apple-touch-icon.png` and `og-image.png` for the website, which lives in its own repo. `og-image.png` is the picture shown when someone shares a link to the website.
- `assets/brand/store/<name>.scale-100.png` and `<name>.scale-200.png` for the Microsoft Store: StoreLogo, Square44x44Logo, Square71x71Logo, Square150x150Logo, Wide310x150Logo, Square310x310Logo and SplashScreen. Each one is the icon centered on ink `#0C0A09`. The icon size is set per tile in `generate.cs`.

## Not part of the app build

The generator is not in `Atap.sln`, so `dotnet build` and `dotnet test` never touch it. The `Directory.Build.props` and `Directory.Packages.props` files in this folder keep the repo-wide settings, such as MinVer and central package versions, away from it.
