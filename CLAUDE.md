# n17 YuHelper — заметки для разработки (читать первым)

Приложение в трее Windows (логика как у ShareX): глобальная клавиша → рамка области экрана или выделенный текст / голос / файл → окно действий: перевод, распознавание текста (OCR) в буфер, проверка грамматики, свои действия. Всё на локальных моделях через llama.cpp.

Текущая версия: **0.13** (см. CHANGELOG.md). Подробности устройства — docs/ARCHITECTURE.md, планы и идеи — docs/ROADMAP.md, для пользователя — README.md.

## Правила проекта (обязательно)

- Личные заметки владельца (путь к папке, его компьютер) — в `CLAUDE.local.md`, этот файл не попадает в git. Читать его тоже.
- Код открытый: https://github.com/nieacry/n17YuHelper (лицензия MIT). В репозиторий не класть модели, llama.cpp, publish/, versions/, settings.json, history.json, журналы.
- `publish\` — всегда актуальная версия; `versions\vX.Y.Z\` — копия каждой выпущенной версии.
- Модели — в `models\` папки разработки, llama-server — в `runtime\llama-server\`.
- **Весь интерфейс — только на английском.** Документация (README, CHANGELOG, этот файл) и комментарии в коде — на русском.
- Языки: EN, RU, UK, ZH. Перевод по умолчанию: ru/uk → en, en → ru, zh → ru.
- Горячие клавиши — всегда Ctrl+Shift+Win+…; Q = Auto (умная клавиша). Ctrl+Shift+Win+B запрещена (сброс видеодрайвера).
- Важны скорость и малое потребление ресурсов в трее. Дизайн — современный, аккуратный (Fluent: Segoe UI Variable, Segoe Fluent Icons, акцент Windows, тёмная/светлая тема).
- В рамке выделения не нужна лупа.
- Владелец предпочитает, чтобы работа шла версиями: сделал → собрал → выложил → коротко отчитался.

## Стек

C# / .NET 10 WinForms, один exe (framework-dependent single-file, ReadyToRun, нужен .NET 10 Desktop Runtime). NuGet: ZXing.Net 0.16.11 (QR), PdfPig 0.1.16 (PDF, исключён из R2R), Velopack 1.2.161 (установка и обновления). Остальное — Win32 P/Invoke без библиотек.

## Сборка

На Windows: `build.cmd 0.12` → `publish\` и `versions\v0.12\`. Перед этим закрыть программу или использовать обновление ниже.

В облачной сессии (Linux): SDK в `/opt/dotnet` (если нет — поставить .NET 10 SDK).
```
export PATH=/opt/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build src/n17YuHelper -c Release          # должно быть 0 предупреждений
dotnet publish src/n17YuHelper/n17YuHelper.csproj -c Release -o /tmp/pub
```
Версия — `<Version>` в `src/n17YuHelper/n17YuHelper.csproj` (поднимать при каждом выпуске). `EnableWindowsTargeting` уже включён.

## Выпуск версии (порядок)

1. Поднять `<Version>`, дописать CHANGELOG.md (сверху, по-русски), при необходимости README.md.
2. Собрать, скопировать exe в `versions\vX.Y.Z\n17YuHelper.exe`.
3. Тот же файл положить как `publish\n17YuHelper.update.exe` — запущенная программа сама подхватит его, когда простаивает (нет открытых окон / записи / работы), заменит exe и перезапустится (`--updated`). Закрывать программу не нужно.
4. Проверить в `publish\logs\yuhelper.log`: строка `Starting n17 YuHelper X.Y.Z`.

**Из облачной сессии через мост устройства:** копировать в `/mnt/user-data/outputs/...`, затем `device_commit_files` с `force: true`. Важно: `publish\n17YuHelper.update.exe` коммитить из **нового** staged-пути (`.../versions/vX.Y.Z/n17YuHelper.exe`), а не из одного и того же `publish/n17YuHelper.update.exe` — повторный коммит с того же пути однажды записал старый файл. После коммита подождать ~20 с и проверить версию в журнале.

## Публичный выпуск (GitHub Releases, Velopack)

Для установленных копий у пользователей. Делается после обычного выпуска (exe уже собран в `/tmp/pub`):
```
export DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$HOME/.dotnet/tools:$PATH   # vpk: dotnet tool install -g vpk --version 1.2.161
gh release download vX.Y.Z-предыдущая -D releases -p '*.nupkg' -p 'releases.win.json'   # для дельта-обновлений
vpk "[win]" pack -u n17YuHelper -v X.Y.Z -r win-x64 -p /tmp/pub -e n17YuHelper.exe -o releases \
    --packTitle "n17 YuHelper" --packAuthors nieacry -i src/n17YuHelper/Resources/app.ico \
    --framework net10-x64-desktop --shortcuts StartMenuRoot --releaseNotes release-notes/X.Y.Z.md
gh release create vX.Y.Z releases/n17YuHelper-X.Y.Z-*.nupkg releases/n17YuHelper-win-Setup.exe \
    releases/n17YuHelper-win-Portable.zip releases/releases.win.json releases/RELEASES releases/assets.win.json \
    --title "n17 YuHelper X.Y.Z" --notes-file release-notes/X.Y.Z.md
```
- `release-notes/X.Y.Z.md` — заметки к выпуску **на английском** (их видят пользователи), CHANGELOG — по-русски.
- Пакет (packId) — `n17YuHelper`, менять нельзя: по нему находятся обновления.

## Что нельзя проверить в облаке

Окна WinForms, хоткеи, микрофон, буфер обмена — только на Windows у владельца (просить скриншот / журнал). В Linux можно проверять логику: собрать консольный проект, подключив нужные .cs (заглушки для `JobObject` и `TrayApp.VersionText`), и гонять с собранным под Linux llama-server (модели: Gemma 4 E2B + mmproj ggml-org, PaddleOCR-VL). Так проверены OCR, перевод по частям, голос, docx/pptx.

## Подводные камни

- `CopyPixelOperation.CaptureBlt` в .NET бросает исключение — снимок экрана через BitBlt (`RegionCaptureForm.CaptureScreen`).
- Свойства форм, которые ставятся из кода, помечать `[DesignerSerializationVisibility(Hidden)]` (WFO1000).
- `Application.SetColorMode` — экспериментальный (WFO5001 подавлен).
- Окно действий закрывается при потере фокуса, кроме: правили текст, 📌 / `KeepWindowOpen`, идёт диктовка.
- llama.cpp: Gemma — `chat_template_kwargs.enable_thinking=false`; грамматика и пакетный перевод документов — `response_format: json_schema`; PaddleOCR — `--jinja --chat-template-file`, промпты `OCR:` / `Table Recognition:` (OTSL) / `Formula Recognition:` (LaTeX).
- Голос: аудио в `input_audio` (base64 WAV 16 кГц моно), ≤28 с за запрос; у Gemma 26B/31B и Qwen голосовой части нет. Если сборка llama.cpp не берёт mmproj — сервер запускается без него (`ServerLaunch.FallbackArgs`).
- Релизы llama.cpp: в последних может не быть Windows-сборок — установщик смотрит 30 последних релизов.
