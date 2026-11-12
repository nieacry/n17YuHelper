# n17 YuHelper — development notes (read first)

A Windows tray app (works like ShareX): global hotkey → region frame, selected text, voice or a file → action window: translation, text recognition (OCR) to the clipboard, grammar check, custom actions. Everything runs on local models through llama.cpp.

Current version: **0.16.2** (see CHANGELOG.md). How it is built — docs/ARCHITECTURE.md, plans and ideas — docs/ROADMAP.md, for users — README.md (English) and README.uk.md (Ukrainian).

## Project rules (mandatory)

- The owner's personal notes (folder path, their PC) are in `CLAUDE.local.md`, which is not in git. Read it too.
- Open source: https://github.com/nieacry/n17YuHelper (MIT). Never commit models, llama.cpp, publish/, versions/, settings.json, history.json or logs.
- **Everything in the repository is in English**: UI, Markdown files (CHANGELOG, docs/, this file, release-notes/), code comments, commit messages. The only other language is Ukrainian in `README.uk.md` — update both READMEs together. No Russian text in the repository (Cyrillic only where the code needs it: language detection, grammar prompts, transcript prefixes, test samples).
- Reports to the owner in chat are in Russian.
- `publish\` always holds the current version; `versions\vX.Y.Z\` holds a copy of every released version.
- Models are in `models\` of the dev folder, llama-server in `runtime\llama-server\`.
- Languages: EN, RU, UK, ZH. Default translation: ru/uk → en, en → ru, zh → ru.
- Hotkeys are always Ctrl+Shift+Win+…; Q = Auto (smart key). Ctrl+Shift+Win+B is forbidden (graphics driver reset).
- Speed and low resource use in the tray matter. Design: modern and clean (Fluent: Segoe UI Variable, Segoe Fluent Icons, Windows accent, dark/light theme).
- No magnifier in the region frame.
- The owner prefers working in versions: change → build → deploy → short report.

## Stack

C# / .NET 10 WinForms, a single exe (framework-dependent single-file, ReadyToRun, needs the .NET 10 Desktop Runtime). NuGet: ZXing.Net 0.16.11 (QR), PdfPig 0.1.16 (PDF, excluded from R2R), Velopack 1.2.161 (install and updates). Everything else is Win32 P/Invoke without libraries.

## Build

On Windows: `build.cmd 0.16` → `publish\` and `versions\v0.16\`. Close the app first or use the update mechanism below.

In a cloud session (Linux): SDK in `/opt/dotnet` (if missing, install the .NET 10 SDK).
```
export PATH=/opt/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build src/n17YuHelper -c Release -warnaserror   # must have 0 warnings
dotnet publish src/n17YuHelper/n17YuHelper.csproj -c Release -o /tmp/pub
```
The version is `<Version>` in `src/n17YuHelper/n17YuHelper.csproj` (bump it on every release). `EnableWindowsTargeting` is already on.

## Releasing a version (order)

1. Bump `<Version>`, add a CHANGELOG.md entry at the top, update both READMEs if needed.
2. Build, copy the exe to `versions\vX.Y.Z\n17YuHelper.exe`.
3. Put the same file as `publish\n17YuHelper.update.exe` — the running app picks it up when idle (no open windows / recording / work), swaps the exe and restarts (`--updated`). No need to close the app.
4. Check `publish\logs\yuhelper.log` for `Starting n17 YuHelper X.Y.Z`.

**From a cloud session via the device bridge:** copy to `/mnt/user-data/outputs/...`, then `device_commit_files` with `force: true`. Important: commit `publish\n17YuHelper.update.exe` from a **new** staged path (`.../versions/vX.Y.Z/n17YuHelper.exe`), not from the same `publish/n17YuHelper.update.exe` — re-committing from the same path once wrote an old file. After committing wait ~20 s and check the version in the log. Files under `.github\` cannot be written via the bridge (protected) — they live only in git.

## Public release (GitHub Releases, Velopack)

For installed copies on users' PCs. Built by GitHub Actions (`.github/workflows/release.yml`) on Windows.
1. Write `release-notes/X.Y.Z.md` (English, users see it). Commit and `git push origin main`.
2. Start the build: `gh api -X POST repos/nieacry/n17YuHelper/actions/workflows/release.yml/dispatches -f ref=main -f 'inputs[version]=X.Y.Z'`.
   (From a cloud session, pushing tags and creating releases directly is not allowed — 403; `gh workflow run` does not work either — no GraphQL.)
3. Workflow: `dotnet publish` → `vpk download github` (previous release, for delta updates) → `vpk pack` → `vpk upload github --publish` (creates the vX.Y.Z tag). Check: `gh api repos/nieacry/n17YuHelper/actions/runs --jq '.workflow_runs[0]|[.status,.conclusion]'`, `gh api repos/nieacry/n17YuHelper/releases --jq '.[0].tag_name'`.
- The package id is `n17YuHelper` — never change it: updates are found by it.
- Releases are unsigned (SignPath Foundation declined for now, see docs/SIGNING.md).

## Model catalog (`catalog.json`)

The app takes it from `main` (raw.githubusercontent.com) and applies it if `format` ≤ `ModelCatalog.SupportedFormat`. It is generated from the built-in list (`ModelCatalog.ExportBuiltin()`): when changing models in `ModelCatalog.cs`, regenerate `catalog.json`. Get SHA256 from the Hugging Face API (`/api/models/<repo>/tree/<rev>` → `lfs.oid`); for small files use `curl -L … | sha256sum` (without `-L` you hash the redirect page). Links point to a revision (`/resolve/<commit>/`), not to `main`.

## What cannot be tested in the cloud

WinForms windows, hotkeys, microphone, clipboard — only on the owner's Windows (ask for a screenshot / log). On Linux you can test logic: build a console project including the needed .cs files (stubs for `JobObject` and `TrayApp.VersionText`) and run it with a Linux build of llama-server (models: Gemma 4 E2B + ggml-org mmproj, PaddleOCR-VL). OCR, chunked translation, voice and docx/pptx were tested this way.

## Pitfalls

- `CopyPixelOperation.CaptureBlt` throws in .NET — the screen is captured via BitBlt (`RegionCaptureForm.CaptureScreen`).
- Form properties set from code need `[DesignerSerializationVisibility(Hidden)]` (WFO1000).
- `Application.SetColorMode` is experimental (WFO5001 suppressed).
- The action window closes on focus loss, except: text was edited, 📌 / `KeepWindowOpen`, dictation in progress.
- llama.cpp: Gemma — `chat_template_kwargs.enable_thinking=false`; grammar and batch document translation — `response_format: json_schema`; PaddleOCR — `--jinja --chat-template-file`, prompts `OCR:` / `Table Recognition:` (OTSL) / `Formula Recognition:` (LaTeX).
- Voice: audio in `input_audio` (base64 WAV 16 kHz mono), ≤28 s per request; Gemma 26B/31B and Qwen have no audio part. If the llama.cpp build does not accept the mmproj, the server starts without it (`ServerLaunch.FallbackArgs`).
- llama.cpp releases: the newest ones may have no Windows builds — the installer checks the last 30 releases (unless the catalog pins a tag).
