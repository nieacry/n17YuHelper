# Подпись выпусков (SignPath Foundation)

Бесплатная подпись для открытых проектов: сертификат выдаётся на SignPath Foundation, она подтверждает, что exe собран из этого публичного репозитория. Пока подписи нет, SmartScreen показывает «Windows protected your PC» (README объясняет «More info → Run anyway»).

## Что уже готово
- Лицензия MIT (одобрена OSI), публичный репозиторий.
- Выпуски собирает только GitHub Actions (`.github/workflows/release.yml`) на Windows-машинах GitHub.
- `CODE_SIGNING_POLICY.md` — страница политики подписи (обязательна по условиям программы), ссылка на неё — в README.
- Есть выпуски (v0.13.0, v0.14.0, …).

## Что сделать владельцу
1. Включить двухфакторную аутентификацию на GitHub (обязательное условие).
2. Заявка подана 2026-11-11 — **отказ 2026-11-11**: проект пока мало известен. Ждут внешних признаков: звёзды/форки/участники на GitHub, статьи, обсуждения (Reddit, YouTube и т.п.), стабильная активность. Подать заново, когда это появится. Платная подписка SignPath не решает вопрос — нужен свой сертификат. (проект: n17 YuHelper, репозиторий https://github.com/nieacry/n17YuHelper, описание — первая строка README).
3. После одобрения SignPath выдаст организацию и проект. В настройках репозитория → Secrets добавить `SIGNPATH_API_TOKEN`, а в Variables — `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` (обычно `release-signing`).
4. В workflow добавить подпись (сделает Claude): между `dotnet publish` и `vpk pack` — выгрузить `publish\n17YuHelper.exe` как артефакт, `signpath/github-action-submit-signing-request@v1` (wait-for-completion), подписанный exe положить обратно в `publish`; Setup.exe подписать тем же способом после `vpk pack` (или через `--signTemplate`).
5. В CODE_SIGNING_POLICY.md поменять «Status» на «signed».

Подпись не убирает предупреждение SmartScreen мгновенно: репутация набирается по мере скачиваний.
