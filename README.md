# n17 YuHelper

**English** · [Русский](README.ru.md)

A Windows tray app in the spirit of ShareX: press a hotkey, select a screen region, take the selected text or just speak — and **recognize, translate or proofread** it with AI models that run **on your own computer** (llama.cpp). Nothing is sent to the internet.

- **OCR** of any screen region: text, tables (paste into Excel / Word), formulas (LaTeX), QR codes
- **Translation** between English, Russian, Ukrainian, Chinese and many other languages, with a glossary of your own terms
- **Grammar and spelling check** with highlighted corrections, one key to replace the original text
- **Voice input**: speak and translate, or dictate straight into any app
- **Translate files**: txt, srt, docx and pptx (formatting kept), pdf (scans too), images
- **Translate over the original**: the translation is shown right on top of the region, with a live mode for subtitles
- Custom actions ("make polite", "shorten", your own prompts), history with search, dark theme

Free and open source (MIT). Third-party components and model licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Install

1. Download **`n17YuHelper-win-Setup.exe`** from the [latest release](https://github.com/nieacry/n17YuHelper/releases/latest) and run it. No admin rights needed; .NET 10 Desktop Runtime is installed automatically if missing.
   The installer is not code-signed yet ([code signing policy](CODE_SIGNING_POLICY.md)), so Windows SmartScreen may say *"Windows protected your PC"*: click **More info → Run anyway**.
2. On first start the app detects your graphics card and offers the right models: click **Download recommended** (about 6–10 GB for most graphics cards). You can choose another folder for them (**Change models folder...**).
3. The **Yu** icon appears in the system tray. The app starts with Windows and updates itself (Settings → General → updates).

Prefer no installer? Use `n17YuHelper-win-Portable.zip` and unpack it anywhere.

**Requirements:** Windows 10/11 x64. A graphics card with 6 GB+ of memory is recommended (NVIDIA, AMD or Intel); it also runs on the CPU, slowly.

| GPU memory | Translation model | Download |
|---|---|---|
| 4 GB or less / no GPU | Gemma 4 E2B | ~5.5 GB |
| 6–8 GB | Gemma 4 E4B | ~7.4 GB |
| 10–12 GB | Gemma 4 12B | ~9 GB |
| 16 GB+ | Gemma 4 26B / 31B | 16–19.5 GB |

(Sizes include PaddleOCR-VL for text recognition, ~1.8 GB. llama.cpp adds 0.1–0.7 GB depending on the graphics card.)

## Default hotkeys

| Hotkey | Action |
|---|---|
| Ctrl+Shift+Win+Q | Smart key: selected text if any, otherwise a region → action window |
| Ctrl+Shift+Win+O | Region → recognize text → clipboard |
| Ctrl+Shift+Win+T | Translate (selected text or a region) |
| Ctrl+Shift+Win+G | Check grammar of the selected text |
| Ctrl+Shift+Win+R | Replace the selected text with its translation |
| Ctrl+Shift+Win+A | Translate the last region over the original (▶ live mode) |
| Ctrl+Shift+Win+V | Speak → translation |
| Ctrl+Shift+Win+D | Speak → text typed into the app (dictation) |

Left-click the tray icon to select a region; right-click for the menu. Every hotkey can be changed or added in **Settings → Hotkeys**.

**In the action window:** 1 translate · 2 recognize · 3 grammar · 4 copy · 5 replace in the app · 6–9 custom actions · Ctrl+M dictate · Ctrl+O open a file · Ctrl+S save · Esc close.

**In the region frame:** drag to select · click to take the window or control under the cursor · arrows move by 1 px (Shift — 10 px) · right-click resets · Esc exits.

## Privacy

Text, screenshots and voice are processed locally by llama.cpp on your PC. The app goes online only to download models (Hugging Face), llama.cpp (GitHub) and its own updates (GitHub). No telemetry.

## Settings and files

Tray menu → **Settings...**. Everything is stored in `settings.json` in the data folder (`%LocalAppData%\n17YuHelper` for the installed app; tray menu → *Open app folder*). Logs: `logs\yuhelper.log`.

## Problems and ideas

[Open an issue](https://github.com/nieacry/n17YuHelper/issues) — please attach `logs\yuhelper.log` if something does not work.

## Building from source

.NET 10 SDK: `dotnet publish src/n17YuHelper/n17YuHelper.csproj -c Release -o publish`. Developer notes (in Russian): [CLAUDE.md](CLAUDE.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
