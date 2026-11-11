# Release signing (SignPath Foundation)

Free signing for open source projects: the certificate is issued to SignPath Foundation, which confirms that the exe was built from this public repository. Until releases are signed, SmartScreen shows "Windows protected your PC" (the README explains "More info → Run anyway").

## Status

- Applied on 2026-11-11 — **declined on 2026-11-11**: the project is not widely known yet. They look for external signals: stars/forks/contributors on GitHub, articles, discussions (Reddit, YouTube, etc.) and sustained activity. Reapply once these exist (project: n17 YuHelper, repository https://github.com/nieacry/n17YuHelper, description — the first line of the README).
- A paid SignPath subscription does not solve it: you still need your own certificate.
- Free alternative without warnings: a Microsoft Store (MSIX) version — Microsoft signs it (see `docs/ROADMAP.md`).

## Already in place

- MIT license (OSI-approved), public repository.
- Releases are built only by GitHub Actions (`.github/workflows/release.yml`) on GitHub's Windows runners.
- `CODE_SIGNING_POLICY.md` — the code signing policy page (required by the program), linked from the README.
- Releases exist (v0.13.0, v0.14.0, v0.15.0).

## After approval

1. Two-factor authentication must be on for GitHub and SignPath.
2. SignPath provides an organization and a project. In the repository settings → Secrets add `SIGNPATH_API_TOKEN`, and in Variables — `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` (usually `release-signing`).
3. Add signing to the workflow: between `dotnet publish` and `vpk pack`, upload `publish\n17YuHelper.exe` as an artifact, run `signpath/github-action-submit-signing-request@v1` (wait-for-completion) and put the signed exe back into `publish`; sign Setup.exe the same way after `vpk pack` (or via `--signTemplate`).
4. Change the status in CODE_SIGNING_POLICY.md to signed.

Signing does not remove the SmartScreen warning instantly: reputation builds up with downloads.
