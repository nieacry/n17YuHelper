# Устройство n17 YuHelper

Всё в `src/n17YuHelper/` (≈10,6 тыс. строк C#). Пространство имён `N17YuHelper.*`.

## Поток работы

```
Горячая клавиша (HotkeyManager, RegisterHotKey)
  → TrayApp.RunAsync(HotkeyEntry)
      → GetInputAsync: Source = Auto | SelectedText | Region | Clipboard | LastRegion | Voice
          SelectionGrabber (Ctrl+C / Ctrl+Insert, восстановление буфера)
          RegionCaptureForm (рамка, прилипание к окнам WindowFinder)
          RecordingToast + VoiceService (голос → текст)
      → Action = Menu | Ocr | OcrTable | OcrFormula | Translate | Grammar | Custom | AsIs
      → After  = ShowWindow (ActionForm) | Copy | Replace (вставить в программу) | Overlay (поверх области)
```
Файлы: меню трея «Translate file...», перетаскивание в ActionForm, «Send to» Проводника (второй запуск передаёт путь через `IpcWindow`, WM_COPYDATA).

## Модели (llama.cpp)

- `Llm/LlamaServer` — свой процесс `llama-server.exe` на модель (OCR и текст), свободный порт, ожидание `/health`, Job Object (убивается вместе с программой), выгрузка через `ModelIdleUnloadMinutes` и при запуске тяжёлых программ (`UnloadWhileRunning`). `ServerLaunch.FallbackArgs` — повтор без необязательной части.
- `Llm/ChatClient` — OpenAI `/v1/chat/completions`, стриминг SSE.
- `TrayApp.BuildOcrLaunch / BuildTextLaunch` — аргументы; GPU-режим Auto / Both / OneAtATime (`BeforeStart` выгружает другую модель).
- `Models/ModelCatalog` — пакеты моделей: URL на ревизию HF, размер, SHA256; формат catalog.json (`Apply`, `ExportBuiltin`); проверенный тег llama.cpp. `Models/CatalogUpdater` — сохранённый catalog.json при запуске, свежий с GitHub через 30 с.
  `Audio` — mmproj для голоса; `RecommendedText(gpu)` (правила из каталога) по видеопамяти: ≥23 → 31B, ≥15 → 26B, ≥9.5 → 12B, ≥5.5 → E4B, иначе E2B.
- `Models/Downloader` — докачка (.part + Range), проверка SHA256, установка llama.cpp из GitHub-релизов (проверенный тег, иначе свежий) (CUDA 13 / 12 / Vulkan / CPU по `GpuInfo`).
- `Models/GpuInfo` — DXGI (видеопамять), запасной путь nvidia-smi.
- `UI/ModelManagerForm` — менеджер моделей / мастер первого запуска, «Change models folder...»; зелёная рамка — рекомендуемая, красная — не влезает в память.

## Сервисы

- `Ocr/OcrService` — PaddleOCR-VL: текст (с нарезкой на плитки и увеличением мелкого), таблица (OTSL → TSV/HTML, `TableFormat`), формула (LaTeX).
- `Translate/TranslationService` — перевод: длинный текст по частям (`TextChunker`, ~2000 символов, разделители сохраняются); `TranslateBatchAsync` — пачка абзацев за запрос с json_schema (для документов); словарь терминов (`Glossary`) подмешивается в промпт.
- `Translate/Languages` — определение языка по письменности/буквам (ru/uk различаются по ґєії / ыэъё).
- `Translate/PromptService` — свои действия (`CustomActions`).
- `Grammar/GrammarService` + `TextDiff` — исправления в JSON, подсветка правок по словам.
- `Voice/AudioRecorder` — waveIn 16 кГц моно, выбор микрофона; `Voice/VoiceService` — обрезка тишины, нарезка ≤28 с по паузам, `input_audio`, проверка `/props` modalities.audio.
- `Files/DocumentReader` — txt (UTF-8/16/1251), docx, pptx, rtf, PDF (PdfPig; сканы → картинка страницы → OCR), картинки.
- `Files/OfficeTranslator` — перевод docx/pptx с оформлением: абзацы → части с одинаковым форматированием → теги `<1>…</1>` → пачки → запись текста обратно в прогоны, язык документа меняется.
- `Tools/QrReader` (ZXing), `Tools/Speech` (SAPI, озвучка), пиньинь — через модель.

## Интерфейс

- `UI/Theme` — цвета (System/Light/Dark, акцент Windows из реестра DWM), шрифты, DWM (тёмная рамка, скругления).
- `UI/ModernControls` — ModernButton (Normal/Accent/Ghost, глиф, подсказка-цифра), Card (с цветной отметкой `Mark`), BusyLine, Caption.
- `UI/ActionForm` — главное окно действий: заголовок (язык, 📂, 🎤, 📌, ×), поле текста / картинка, результат, кнопки 1–9 (Translate, Recognize ▾, Grammar, Copy, Replace, Actions, Save). Снимок при закрытии (`WindowSnapshot`) → «Open last window».
- `UI/OverlayForm` — перевод поверх области (исключён из захвата экрана, живой режим).
- `UI/RecordingToast` — плашка записи (без фокуса, Esc — глобально на время записи).
- `UI/SettingsForm` — страницы Hotkeys / General / Models / Translation / Actions.
- `UI/History` — `HistoryStore` (history.json, ленивое чтение) и окно истории с поиском.
- `UI/ProgressToast`, `UI/HotkeyBox`.

## Прочее

- `AppPaths` — где данные: переносной режим (рядом с exe) или установленная программа (`%LocalAppData%\n17YuHelper`, на уровень выше папки `current`).
- `AppSettings` — settings.json в папке данных, `SettingsVersion` + `Migrate()` (сейчас 10). Новые клавиши по умолчанию добавлять через миграцию.
- `Updater` — `n17YuHelper.update.exe` рядом с exe → применяется, когда программа простаивает (переносная копия).
- `OnlineUpdater` — Velopack + GitHub Releases для установленной программы: проверка через минуту и раз в 6 ч, скачивание в фоне, установка через `WaitExitThenApplyUpdates` (когда программа простаивает или по команде). `Program.Main` начинается с `VelopackApp.Build()…Run()` (хуки установки/удаления: автозапуск).
- `Startup` — автозапуск (HKCU Run). `Log` — `publish\logs\yuhelper.log` (+ llama-ocr.log, llama-text.log).
- `tools/setup-models.ps1` — запасной скрипт скачивания моделей и llama.cpp.
- `tests/samples/` — картинки для проверки OCR на 4 языках.
