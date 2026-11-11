# Changelog

## v0.16.1 — 2026-11-11

- "Open last window" (hotkey and tray menu): if no window has been closed yet, an empty action window opens to type or paste text.
- Trailing empty lines of the selected text are dropped, so the text no longer scrolls out of view in the ORIGINAL box.

## v0.16 — 2026-11-11

- New hotkey action "Open last window" (Settings → Hotkeys → Action). Default hotkey: Ctrl+Shift+Win+W — added automatically to existing settings if that combination is free. Source and Then are not used for this action and are greyed out.

## v0.15 — 2026-11-11

Public beta.

- Tray menu → Help: "What's new" (releases on GitHub), "Report a problem..." (bug report form), "Project page (GitHub)".
- README.md is now in English (for everyone), with a Ukrainian translation in README.uk.md. A table of which model and how much to download for each amount of GPU memory; a privacy section.
- `CODE_SIGNING_POLICY.md` and `docs/SIGNING.md` — preparation for free SignPath Foundation code signing.
- GitHub Actions: build check on every push (`build.yml`, warnings are errors), bug report template.
- All documentation and code comments are in English.

## v0.14 — 2026-11-11

Reliable model downloads.

- The model catalog now also lives in the repository (`catalog.json`): half a minute after startup the app downloads a fresh copy and saves it in the data folder. Links can be fixed and models added without a new app version. Offline — the built-in list.
- Hugging Face links are pinned to a tested revision; every file has a SHA256. After downloading, the file is checked ("Checking …"); a damaged file is deleted and the next attempt downloads it again.
- llama.cpp is installed in the tested build (`b12248`, set in the catalog) rather than the newest. If that build has nothing for this graphics card, the latest one is used. llama.cpp archives are checked with SHA256 too (from GitHub). Models... shows the "tested version"; the button reads "Update to b12248" / "Reinstall".
- Models... → "Change models folder...": choose a folder for models (e.g. on another drive). Already downloaded models are not moved — the app tells you which folders to move.

## v0.13 — 2026-11-10

Installer and online updates (Velopack + GitHub Releases).

- First public release: https://github.com/nieacry/n17YuHelper/releases — `n17YuHelper-win-Setup.exe` installs the app into `%LocalAppData%\n17YuHelper` without admin rights, installs the .NET 10 Desktop Runtime if needed, adds a Start menu shortcut and turns on start with Windows. Uninstall via Installed apps (start with Windows is removed).
- The installed app checks GitHub for updates by itself: a minute after startup and every 6 hours; a new version is downloaded in the background (only the difference when possible).
- Settings → General: "Check for updates automatically" and "Install updates automatically (when the app is idle)". With the second one off you get a notification, and the tray menu shows "Restart to update to X".
- Tray menu → "Check for updates...". After an update the "n17 YuHelper updated" notification opens the release notes on GitHub when clicked.
- A portable copy (dev folder) updates the old way — with `n17YuHelper.update.exe`.

## v0.12 — 2026-11-10

Preparing the public release: data separate from the app.

- When installed by the installer (folder `…\n17YuHelper\current\`), settings, history, logs, models and llama.cpp are stored one level up — in `%LocalAppData%\n17YuHelper`. An update that replaces the app folder entirely loses nothing.
- Portable mode works as before: if `settings.json` (or an empty `portable.txt`) is next to the exe, everything is stored next to the exe. The dev folder keeps working unchanged.
- The startup log shows where the data is: `Data folder: … (portable)`.
- For new users: the glossary is empty by default, and the "Explain" action answers in the language of the text. Existing settings stay as they are.
- Open source: https://github.com/nieacry/n17YuHelper (MIT); third-party components are listed in `THIRD-PARTY-NOTICES.md`.

## v0.11.3 — 2026-11-10

- Tray menu → "Open last window": reopens the action window the way it was closed — with the text, the translation (with grammar highlights), the chosen language, the opened file and in the same place on screen. If the window is open, just brings it to the front. Remembered until the app exits.

## v0.11.2 — 2026-11-10

- Model manager: one recommended model is marked green; only models that do not fit into GPU memory (and would be very slow) are marked red. The rest are unmarked.

## v0.11.1 — 2026-11-10

- Model manager: the model recommended for this PC is marked green (side bar, border, "★ Recommended for your PC"), others red with a reason: "needs more GPU memory than your 11.7 GB, will be slow", "Gemma 4 12B fits your GPU and translates better", etc.

## v0.11 — 2026-11-10

Word and PowerPoint with formatting, microphone choice, persistent history.

- Translating Word (.docx) and PowerPoint (.pptx) keeping the formatting: only the text changes, while styles, fonts, tables, images, headers/footers, footnotes and slide notes stay as they were. A bold word or a link inside a paragraph stays highlighted in the translation (the model gets the paragraph parts in tags). The text language in the document is switched to the target language, so Word spell-checks correctly.
- Save for such a file offers `name.en.docx` / `name.en.pptx` right away; .txt can be chosen too. If the text in the window was edited by hand, that text is translated as plain text.
- Document paragraphs are translated in batches (up to ~1500 characters) per request; each translated paragraph appears in the window as it is ready.
- Settings → General → Microphone: choose the microphone for voice input (default — the Windows microphone).
- History is kept after restart (`history.json` next to the exe, the last 300 results). Turn it off in Settings → General. The History window has search (Ctrl+F), deleting one entry (Delete), clearing with confirmation and dates for older entries. History is not kept in memory while in the tray — it is read when needed.
- Settings → General → Keep window open: the action window does not close when you click elsewhere (like 📌, but for every window). Close with Esc or ×.

## v0.10 — 2026-11-10

Voice input and file translation.

- Voice input with the same model that translates: Gemma 4 understands speech when its "voice part" (mmproj) is loaded. Models... → "Add voice" downloads it (12B — 175 MB, E2B / E4B — 0.55 GB); new model downloads include it right away. Works with Gemma 4 E2B, E4B and 12B (26B and 31B do not understand speech).
- Ctrl+Shift+Win+V — speak → translation in the window (button 5 pastes the translation where the cursor was). Ctrl+Shift+Win+D — dictation: the recognized text is typed straight into the app with the cursor.
- Recording: a bar at the bottom of the screen with the sound level and a timer; it does not take focus. Finish — the same hotkey, the ■ button or a 2 s pause; Esc — cancel. Long speech is cut into pieces of up to 28 s at pauses. Mixed speech (e.g. Russian + English) is recognized with each part in its own language.
- The 🎤 button in the action window (Ctrl+M) — dictate into the box at the cursor. Tray menu → "Dictate...".
- Hotkeys: source "Voice" and action "Text as is"; "Replace selected text" is now called "Paste into the app".
- If the llama.cpp build does not support the model's voice part, the translation model starts without it (translation works) and voice input suggests updating llama.cpp.
- File translation: tray menu → "Translate file...", drag a file into the action window, Ctrl+O / 📂 in the window. Text formats (.txt, .md, .srt, .csv, .json, .html, .po…, UTF-8 / UTF-16 / Windows-1251 encodings), Word (.docx), PowerPoint (.pptx), RTF, PDF (text layer; scans are recognized page by page with OCR), images.
- Long text is translated in pieces by paragraphs (up to ~2000 characters) with "part 2 of 7" progress and output as it goes; empty lines and indentation are kept. The same works for long selected text.
- "Save" (Ctrl+S) — save the translation next to the file: `name.en.txt`; text formats keep their extension (`movie.en.srt`).
- Explorer "Send to": tray menu → Advanced → Add to Explorer "Send to" menu. A file sent to the running app opens in it (no second instance starts). You can also drag a file onto the exe.
- The 📌 button in the action window: do not close the window when clicking elsewhere (turns on by itself for files).
- Text dropped into the window is inserted into the box.

## v0.9 — 2026-11-10

Translation over the original, repeat the last region, QR codes, text-to-speech and pinyin.

- Translation over the original: the result is shown in a semi-transparent window right over the selected region (tray menu → "Translate region over the original", or "Then: Show over the region" for any hotkey). The window does not take focus, can be dragged; double-click copies. On hover — buttons: ▶ live mode, copy, close.
- Live mode: the window watches the region and, when the text under it changes (subtitles, an app's UI), recognizes and translates it again. The window is excluded from screen captures, so the app sees the original under it. Check interval — `LiveIntervalMs` (1.5 s).
- Repeat the last region: source "Last region" — the same region as last time, without the frame. New hotkey Ctrl+Shift+Win+A — translate the last region over the original.
- QR codes and barcodes: if the selected region has a code, the action window shows "Open link" (or "Copy QR"); the content goes to the status line and history.
- Text-to-speech with Windows voices: Actions → "Speak text" / "Speak result". The voice is chosen by language; if there is none, the app says where to install it.
- Pinyin: Actions → "Pinyin (Chinese)" — for Chinese text or a translation into Chinese: the characters, pinyin with tones below and a short translation.

## v0.8 — 2026-11-10

Tables and formulas, custom actions, glossary.

- Table recognition: in the action window, the ▾ arrow next to "Recognize text" → "Table" or "Formula"; in the tray menu — "Recognize region to clipboard" → Text / Table / Formula. A table is copied in two forms at once: for Excel (cell by cell) and for Word / Google Docs (a real table). A formula is copied as LaTeX.
- Hotkeys got the actions "Recognize table" and "Recognize formula".
- Custom actions: Settings → Actions — a name and what to do with the text. In the action window — the "Actions" button and keys 6–9 (Ctrl+6…9 while the cursor is in the box). The result appears below as it is generated; it can be copied or replace the selected text. A custom action can be bound to a hotkey (Hotkeys → Action → "Action: …", with "Copy result" or "Replace selected text" — no window). Starter examples: Make polite, Shorten, Explain, Reply.
- Glossary: Settings → Translation → Glossary, lines "term => translation" ("term => =" — keep untranslated). Only terms found in the text are used, in both directions.

## v0.7 — 2026-11-10

Model manager and first-run wizard, translate and replace.

- Tray menu → "Models...": the graphics card and its memory (DXGI — NVIDIA, AMD, Intel; driver version for NVIDIA), free disk space, recommended models for this card.
- Downloads right from the app: progress bar, speed, time left, cancel, resume after interruption, size check. Deleting models; "Use" picks the translation model.
- Catalog: PaddleOCR-VL 1.6; Gemma 4 E2B / E4B / 12B / 26B-A4B / 31B; Qwen3.5 9B. Recommendation: up to 5.5 GB — E2B, 6–9 GB — E4B, 10–14 GB — 12B, 16–22 GB — 26B-A4B, 24 GB and more — 31B.
- llama.cpp is installed and updated from the same place; the build is chosen by graphics card: CUDA 13 (driver 580+), CUDA 12, Vulkan for AMD and Intel, CPU without a GPU.
- On first start, if something is missing, a welcome window with "Download recommended" opens.
- GPU memory mode (in "Models..."): Auto — decide by the amount of memory; Keep both — keep both models loaded; One at a time — unload one model before loading the other (for 6–8 GB cards).
- New hotkey Ctrl+Shift+Win+R "Translate and replace": select text → it is replaced with the translation in place, no window. Works for any custom hotkey with `Translate` + "Replace selected text".
- Hints about a missing model now point to "Models...". `tools\setup-models.cmd` stays as a fallback.

## v0.6.1 — 2026-11-10

- Updates install themselves: as soon as the app is idle (no open windows, no work), it restarts into the new version and shows "n17 YuHelper updated".
- Action window: icons on buttons (Segoe Fluent Icons), captions above blocks (TEXT / ORIGINAL / TRANSLATION · ENGLISH / CORRECTED), a thin running accent bar under the header while working, a slightly bigger window.
- Settings: icons in the navigation.

## v0.6 — 2026-11-10

New UI and automatic model unloading.

- Windows 11 style: light and dark theme (default — as in Windows), Windows accent color, Segoe UI Variable font, rounded corners and dark window title bars.
- Action window without a system frame: icon, title and language picker like "Russian → English ▾" at the top, a close button; text and result in cards; rounded buttons with a key hint on the right (while the cursor is in the input box, hints show Ctrl+digit). The window is dragged by the header and resized by the edges.
- Settings: navigation on the left (Hotkeys, General, Models, Translation), new buttons, dark theme for fields and lists. General has the theme choice (System / Light / Dark, applied after restart).
- Tray menu, cursor hints, history and the region frame use the theme colors.
- While Maya, Blender, Houdini, ZBrush, 3ds Max, Cinema 4D, Unreal Editor, Substance Painter or Marvelous Designer is running, models are unloaded after a minute of idle time. The list is in Settings → Models.
- `tools\setup-models.cmd -Qwen` — download the second translation model Qwen3.5 9B; choose it in Settings → Models.

## v0.5 — 2026-11-10

Settings window, history, editing text in the action window.

- In the action window the source text can now be edited or typed anew, and the corrected text translated and checked. While the cursor is in the input box, digits are typed and actions are on Ctrl+1…Ctrl+5 (Ctrl+Enter — translate). Outside the box plain 1–5 work too.
- If the text was edited by hand, the window does not close by itself when switching to another app (close with Esc or ×).
- The text language and the default target language update as you type (until the target language is chosen manually).
- Tray menu → "Type text...": an empty window to type or paste text and translate / check it.
- Tray menu → "Settings...":
  - Hotkeys — a hotkey list like in ShareX: click the field and press the combination; source, action, what to do after, target language; add, delete, restore defaults. After Apply each hotkey shows its status (✓ OK / ✗ in use).
  - General — start with Windows, tray icon click action, frame snapping, magnifier, selected-text wait time, app lists (no selection / terminals).
  - Models — model and llama-server folders, translation model choice (from those found in models\llm), temperature, unload after N minutes, preloading, upscaling small regions, "Free GPU memory", "Download models...".
  - Translation — default target language for each language, reset remembered choices.
- Tray menu → "History": the last 50 results (recognition, translation, grammar), in memory only. Double-click copies the result.
- Manual editing of `settings.json` moved to the "Advanced" menu.
- Update without closing by hand: if `n17YuHelper.update.exe` appears next to the exe, the app shows "Update ready" and a "Restart to update" tray item — a click restarts it into the new version.

## v0.4 — 2026-11-10

Grammar check, text replacement, start with Windows.

- "3  Check grammar" in the action window and Ctrl+Shift+Win+G: spelling, grammar and punctuation for English, Russian, Ukrainian and Chinese. The corrected text is shown below: removed — red strikethrough, added — green, with explanations of the fixes in English.
- "5  Replace" — paste the result (corrected text or translation) instead of the selected text in the app where it was selected. The clipboard is restored afterwards. The button is visible only if text was selected (not a screen region).
- Hotkeys with `"Action": "Grammar"`: `"After": "Replace"` — fix and replace right away without a window; `"After": "Copy"` — copy the corrected text.
- Tray menu: "Start with Windows" — start at Windows sign-in (for the exe it was enabled from).
- The model is preloaded for the grammar hotkey too.

## v0.3 — 2026-11-10

Translation with the local Gemma 4 12B model.

- `tools\setup-models.cmd` now also downloads the translation model (Gemma 4 12B QAT Q4_0, ~7 GB). Already downloaded files are skipped.
- "1  Translate" works in the action window: source text on top, translation below, appearing as it is generated. The target language is chosen in the list next to the button; changing it translates again, and the choice is remembered.
- The text language is detected instantly by letters (CJK → Chinese; і/ї/є/ґ → Ukrainian; other Cyrillic → Russian; Latin → English). Defaults: Russian/Ukrainian → English, English → Russian, Chinese → Russian.
- For an image, "Translate" first recognizes the text, then translates.
- Ctrl+Shift+Win+T: selected text or a region → translation window. Hotkeys with `"Action": "Translate", "After": "Copy"` copy the translation right away. The language can be set per hotkey (`"TargetLanguage": "zh"`).
- The translation model starts loading as soon as the action window opens or the translate hotkey is pressed; it is unloaded after 5 idle minutes together with the OCR model.
- "4  Copy" copies the translation if there is one, otherwise the source text or image.
- The action window can be resized.
- If the model is not downloaded, the window suggests running `tools\setup-models.cmd`.
- Translation model log: `logs\llama-text.log`.

## v0.2 — 2026-11-10

Text recognition (OCR) with the local PaddleOCR-VL-1.6 model.

- `tools\setup-models.cmd` downloads llama-server (llama.cpp, CUDA 13 with driver 580+, otherwise CUDA 12.4) and the recognition model (~1.8 GB).
- The app starts llama-server itself: when the hotkey is pressed the model starts loading while the region is being selected. After 5 idle minutes the model is unloaded (`ModelIdleUnloadMinutes`). If the app exits or crashes, llama-server exits with it.
- Ctrl+Shift+Win+O: region → text to the clipboard, with a hint at the cursor ("Loading OCR model..." → "Recognizing text..." → "Copied N characters").
- "2  Recognize text" works in the action window: the image is replaced with the recognized text, "4  Copy" copies it.
- Small regions are upscaled before recognition; large ones are cut into strips along empty rows.
- Tray menu: model status, "Free GPU memory", "OCR region to clipboard", "Open logs folder".
- The `models` and `runtime\llama-server` folders are found automatically (next to the exe or above); paths can be set in `settings.json`.
- llama-server log: `logs\llama-ocr.log`.

## v0.1.2 — 2026-11-10

- The magnifier is no longer shown in the region frame (setting `ShowMagnifier`, off by default; turned off automatically in an existing `settings.json`).

## v0.1.1 — 2026-11-10

- Fixed: the region frame did not appear (screen capture error with `CopyPixelOperation`). The snapshot is now taken via BitBlt directly.
- The whole app UI is in English: tray menu, notifications, action window, messages. The log is in English too.
- An old `settings.json` is updated automatically: default hotkeys get English names.

## v0.1 — 2026-11-10

App skeleton, no models yet.

- Tray icon, single instance, tray menu.
- `settings.json` is created on first start; "Reload settings" in the menu.
- Global hotkeys from the settings (by physical key, work with any keyboard layout). Taken or invalid hotkeys are shown in a notification and in the "Hotkeys" menu with ✗. Ctrl+Shift+Win+B is forbidden.
- Smart key: waits for the keys to be released, copies the selection, restores the previous clipboard. In terminals — Ctrl+Insert instead of Ctrl+C; in Maya and Blender the selection is not taken.
- Region frame like in ShareX: all monitors, mixed scaling, dimming, crosshair, magnifier, snapping to windows and controls, arrow keys, Esc / right click.
- Action window at the cursor: shows text or an image, "Copy" works (key 4). Translation, recognition and grammar are stubs.
- "Recognize → clipboard" copies the region image for now.
- Memory is returned to the system when idle.
- Log `logs\yuhelper.log`.
