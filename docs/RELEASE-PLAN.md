# Public release plan

**Status (2026-11-11): 0.12–0.15 released.** The SignPath Foundation application was declined for now (see `docs/SIGNING.md`); releases are unsigned.

Owner's decisions (2026-11-10):
- Open source, repository `github.com/nieacry/n17YuHelper`, MIT license.
- Installation and updates — **Velopack** (free, MIT). Updates come from this repository's GitHub Releases.
- No paid certificate. Free signing via **SignPath Foundation** once the project qualifies; until then an unsigned beta (README: "More info → Run anyway").
- Models are not redistributed — the app downloads them from Hugging Face. Licenses — `THIRD-PARTY-NOTICES.md`.
- Documentation and code comments are in English; the second README language is Ukrainian (`README.uk.md`).

## Versions

- **0.12** — data separate from the exe: the installed app keeps everything in `%LocalAppData%\n17YuHelper` (one level above Velopack's `current` folder); portable mode (settings.json or `portable.txt` next to the exe) works as before; neutral defaults for new users.
- **0.13** — Velopack: installer, online updates from GitHub Releases (check at startup and every 6 h, background download, install when idle), "Check for updates..." in the tray, update settings in Settings → General, release builds in GitHub Actions.
- **0.14** — `catalog.json` in the repository (models with URL, size and SHA256, tested llama.cpp tag), SHA256 checks after download, models folder choice.
- **0.15** — public beta: English README, Help menu, code signing policy, CI build check.
