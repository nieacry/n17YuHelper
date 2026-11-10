# План публичного выпуска

**Статус (2026-11-11): 0.12–0.15 выпущены.** Осталось владельцу: подать заявку в SignPath Foundation (см. `docs/SIGNING.md`), после одобрения — добавить подпись в workflow.

Решения владельца (2026-11-10):
- Код открытый, репозиторий `github.com/nieacry/n17YuHelper`, лицензия MIT.
- Установка и обновления — **Velopack** (бесплатный, MIT). Источник обновлений — GitHub Releases этого репозитория.
- Платный сертификат не покупаем. Подаём заявку в **SignPath Foundation** (бесплатная подпись для open source); до этого — бета без подписи (в README: «More info → Run anyway»).
- Модели не раздаём сами — программа качает их с Hugging Face. Список лицензий — `THIRD-PARTY-NOTICES.md`.

## Версии

### 0.12 — данные отдельно от exe
- Установленная программа хранит всё в `%LocalAppData%\n17YuHelper\` (на уровень выше папки `current` Velopack): settings.json, history.json, logs, models, runtime.
- **Переносной режим**: если рядом с exe лежит settings.json (или файл `portable`), всё как раньше — рядом с exe. Папка разработки владельца работает без изменений.
- Нейтральные умолчания для новых пользователей (словарь, список «выгружать модели при запуске программ» — только для новых установок, у владельца остаётся как есть).
- Settings → General → Updates: «Check for updates automatically», «Install updates automatically».

### 0.13 — Velopack
- `VelopackApp.Build().Run()` в начале `Main`; `UpdateManager` с `GithubSource`.
- Проверка при запуске и раз в 6 ч; скачивание в фоне; установка — когда программа простаивает (наша логика), `ApplyUpdatesAndRestart`.
- Галочка «Install automatically» выключена → уведомление и пункт трея «Restart to update to X».
- Пункт трея «Check for updates…», окно «What's new» после обновления (заметки к выпуску на английском).
- `release.cmd X.Y.Z`: `dotnet publish` → `vpk pack --framework net10.0-x64-desktop` → `versions\` и `publish\` как раньше → `vpk upload github`.
- Локальный `n17YuHelper.update.exe` остаётся для переносного режима.

### 0.14 — каталог и надёжная загрузка
- `catalog.json` в репозитории: модели (URL, размер, SHA256), закреплённая проверенная сборка llama.cpp для cuda13 / cuda12 / vulkan / cpu, минимальная версия программы. Встроенная копия — запасной вариант.
- Проверка SHA256 после скачивания.
- Мастер первого запуска: выбор папки для моделей, общий размер и свободное место, загрузка в фоне с прогрессом в трее.

### 0.15 — публичная бета
- README на английском (русский — `README.ru.md`), скриншоты, раздел про SmartScreen.
- Заявка в SignPath Foundation, подпись в GitHub Actions.
- Сборка релиза в GitHub Actions.
