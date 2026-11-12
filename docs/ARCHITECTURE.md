# How n17 YuHelper is built

Everything is in `src/n17YuHelper/` (≈10.6k lines of C#). Namespace `N17YuHelper.*`.

## Workflow

```
Hotkey (HotkeyManager, RegisterHotKey)
  → TrayApp.RunAsync(HotkeyEntry)
      → GetInputAsync: Source = Auto | SelectedText | Region | Clipboard | LastRegion | Voice
          SelectionGrabber (Ctrl+C / Ctrl+Insert, clipboard restore)
          RegionCaptureForm (frame, snapping to windows via WindowFinder)
          RecordingToast + VoiceService (voice → text)
      → Action = Menu | Ocr | OcrTable | OcrFormula | Translate | Grammar | Custom | AsIs
      → After  = ShowWindow (ActionForm) | Copy | Replace (paste into the app) | Overlay (over the region)
```
Files: tray menu "Translate file...", drag and drop into ActionForm, Explorer "Send to" (a second start passes the path through `IpcWindow`, WM_COPYDATA).

## Models (llama.cpp)

- `Llm/LlamaServer` — one `llama-server.exe` process per model (OCR and text), a free port, waiting for `/health`, a Job Object (killed together with the app), unloading after `ModelIdleUnloadMinutes` and while heavy apps run (`UnloadWhileRunning`). `ServerLaunch.FallbackArgs` — retry without the optional part.
- `Llm/ChatClient` — OpenAI `/v1/chat/completions`, SSE streaming.
- `TrayApp.BuildOcrLaunch / BuildTextLaunch` — arguments; GPU mode Auto / Both / OneAtATime (`BeforeStart` unloads the other model).
- `Models/ModelCatalog` — model packages: URL pinned to an HF revision, size, SHA256; the catalog.json format (`Apply`, `ExportBuiltin`); the tested llama.cpp tag. `Models/CatalogUpdater` — the saved catalog.json at startup, a fresh one from GitHub after 30 s.
  `Audio` — mmproj for voice; `RecommendedText(gpu)` (rules from the catalog) by GPU memory: ≥23 → 31B, ≥15 → 26B, ≥9.5 → 12B, ≥5.5 → E4B, otherwise E2B.
- `Models/Downloader` — resume (.part + Range), SHA256 check, installing llama.cpp from GitHub releases (tested tag, otherwise the newest; CUDA 13 / 12 / Vulkan / CPU by `GpuInfo`).
- `Models/GpuInfo` — DXGI (GPU memory), nvidia-smi fallback.
- `UI/ModelManagerForm` — model manager / first-run wizard, "Change models folder..."; green border — recommended, red — does not fit into memory.

## Services

- `Ocr/OcrService` — PaddleOCR-VL: text (tiling and upscaling small regions), table (OTSL → TSV/HTML, `TableFormat`), formula (LaTeX).
- `Translate/TranslationService` — translation: long text in pieces (`TextChunker`, ~2000 characters, separators kept); `TranslateBatchAsync` — a batch of paragraphs per request with json_schema (for documents); the glossary is added to the prompt.
- `Translate/Languages` — language detection by script/letters (ru/uk are told apart by ґєії / ыэъё).
- `Translate/PromptService` — custom actions (`CustomActions`).
- `Grammar/GrammarService` + `TextDiff` — fixes as JSON, word-level highlighting.
- `Voice/AudioRecorder` — waveIn 16 kHz mono, microphone choice; `Voice/VoiceService` — silence trimming, cutting into ≤28 s pieces at pauses, `input_audio`, `/props` modalities.audio check.
- `Files/DocumentReader` — txt (UTF-8/16/1251), docx, pptx, rtf, PDF (PdfPig; scans → page image → OCR), images.
- `Files/OfficeTranslator` — docx/pptx translation keeping formatting: paragraphs → parts with the same formatting → tags `<1>…</1>` → batches → text written back into runs; the document language is switched.
- `Tools/QrReader` (ZXing), `Tools/Speech` (SAPI, text-to-speech), pinyin — via the model.

## UI

- `UI/Theme` — colors (System/Light/Dark, Windows accent from the DWM registry), fonts, DWM (dark frame, rounded corners).
- `UI/ModernControls` — ModernButton (Normal/Accent/Ghost, glyph, digit hint), Card (with a colored `Mark`; `Active` — accent tint, bar and border for the selected item), Pill (accent badge), BusyLine, Caption.
- `UI/ActionForm` — the main action window: header (language, 📂, 🎤, 📌, ×), text box / image, result, buttons 1–9 (Translate, Recognize ▾, Grammar, Copy, Replace, Actions, Save). Snapshot on close (`WindowSnapshot`) → "Open last window".
- `UI/OverlayForm` — translation over the region (excluded from screen capture, live mode).
- `UI/RecordingToast` — recording bar (no focus, Esc is global while recording).
- `UI/SettingsForm` — pages Hotkeys / General / Models / Translation / Actions.
- `UI/History` — `HistoryStore` (history.json, lazy reading) and the history window with search.
- `UI/ProgressToast`, `UI/HotkeyBox`.

## Other

- `AppPaths` — where the data is: portable mode (next to the exe) or installed app (`%LocalAppData%\n17YuHelper`, one level above the `current` folder).
- `AppSettings` — settings.json in the data folder, `SettingsVersion` + `Migrate()` (currently 10). Add new default hotkeys through a migration.
- `Updater` — `n17YuHelper.update.exe` next to the exe → applied when the app is idle (portable copy).
- `OnlineUpdater` — Velopack + GitHub Releases for the installed app: check after a minute and every 6 h, background download, install via `WaitExitThenApplyUpdates` (when idle or on command). `Program.Main` starts with `VelopackApp.Build()…Run()` (install/uninstall hooks: start with Windows).
- `Startup` — start with Windows (HKCU Run). `Log` — `logs\yuhelper.log` in the data folder (+ llama-ocr.log, llama-text.log).
- `tools/setup-models.ps1` — fallback script for downloading models and llama.cpp.
- `tests/samples/` — images for checking OCR in 4 languages.
