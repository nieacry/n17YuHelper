# Plans and ideas

## Done (0.1 → 0.15)

0.12–0.15: public release — installer and auto-update (Velopack, GitHub Releases), data in `%LocalAppData%\n17YuHelper`, model catalog with SHA256 on GitHub, tested llama.cpp build, models folder choice, English README, Help menu.

Region and selected-text capture, OCR (text / table / formula), translation EN/RU/UK/ZH, grammar, custom actions, glossary, repeat the last region and translation over the original, pinyin and text-to-speech, QR, model manager with GPU-based recommendations and automatic llama.cpp install, auto-update, start with Windows, dark theme, voice input (translation and dictation into apps), file translation (txt/srt/docx/pptx/pdf/images), docx/pptx keeping formatting, history on disk with search, Keep window open, Open last window.

## Not tested live yet (ask for screenshots when possible)

- Installer and online update on a clean Windows install (v0.13+).
- Open last window (0.11.3), .docx/.pptx translation on real documents, microphone choice, Explorer "Send to", dragging files into the window.

## Suggested to the owner, not chosen yet

- Subtitles .srt/.vtt: translate only the dialogue text, guaranteed not to touch numbers and timings.
- Batch translation of a folder of files.
- "Clipboard translator" mode: a pinned window translates everything that is copied (Ctrl+C).
- Microsoft Store (MSIX) version: free Microsoft signing, no SmartScreen warnings (llama.cpp would have to be bundled in the package).

## Other ideas

- A global "translate and replace" hotkey for Word documents right in the open file.
- Keep the "last window" across restarts.
- A separate voice model for 26B/31B (a small Gemma E2B just for speech).
- Grammar check of long texts in pieces (currently up to 8000 characters).
