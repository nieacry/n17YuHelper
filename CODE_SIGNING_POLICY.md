# Code signing policy

n17 YuHelper publishes Windows release artifacts from the public repository
[nieacry/n17YuHelper](https://github.com/nieacry/n17YuHelper).

## Status

Release files are currently **not signed**. The project applied to the [SignPath Foundation](https://signpath.org) open-source program on 2026-11-11; it was declined until the project becomes more widely known, and will reapply later.
Until then Windows SmartScreen may show a warning. Once approved, Windows release artifacts will use:

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org)

## What is signed

- `n17YuHelper.exe` (inside the installer and update packages)
- `n17YuHelper-win-Setup.exe` (installer)

## How releases are built

- Every release is built by [GitHub Actions](.github/workflows/release.yml) on GitHub-hosted Windows runners,
  directly from the source code in this repository. Nothing is built or signed on personal computers.
- AI models and llama.cpp are **not** part of the signed files: the app downloads them from their original
  publishers (Hugging Face, ggml-org/llama.cpp on GitHub) and checks them with SHA256.

## Team and roles

- Committer, reviewer and approver: [@nieacry](https://github.com/nieacry)

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy

The app does not collect or send personal data. It connects to the internet only to download models
(Hugging Face), llama.cpp (GitHub) and its own updates (GitHub Releases). See [README.md](README.md#privacy).
