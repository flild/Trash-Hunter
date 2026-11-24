# Разработка Trash-Hunter

## Проверенное окружение — 16 ноября 2026

- Корень Git и Unity-проекта: `F:\UnityProjects\Trash-Hunter`.
  `Assets`, `Packages`, `ProjectSettings` находятся в корне; подпапки `Game` нет.
- Windows 10.0.19045 (build 19045), Windows PowerShell 5.1.19041.3803.
- Unity **6000.6.9f1**, revision **8c45277fb9c5**. Версия не менялась.
- Официальный Unity-плагин Codex **0.1.9-beta**; применены инструкции
  `unity-cli` и `unity-pipeline` из установленного плагина.
- Unity CLI **1.0.0**, `%LOCALAPPDATA%\Unity\bin\unity.exe`, доступен в PATH.
  `unity self-update --check --format json` подтвердил актуальность 1.0.0.
- Unity Pipeline **1.0.0**, установлен командой `unity pipeline install`.
  Версия подтверждена в manifest, packages-lock и package.json загруженного пакета.
- Universal RP 17.6.0, Input System 1.20.1, uGUI 2.6.0 по manifest проекта.

Дополнительных AGENTS.md в проекте и родительских каталогах не обнаружено.
Применены глобальные инструкции пользователя.

## Подключение и запуск Editor

CLI управляет открытым Editor через локальный HTTP API Unity Pipeline.
Отдельный MCP-сервер для этой работы не требуется. Установка плагина Codex сама
по себе не подтверждает соединение: проверять `status` и ответ `editor status`.

```powershell
Set-Location 'F:\UnityProjects\Trash-Hunter'
$projectPath = 'F:\UnityProjects\Trash-Hunter'

unity --version
unity self-update --check --format json
# Если проект ещё не открыт:
unity open $projectPath

# Уже выполнено; повторная установка для этого checkout не нужна:
# unity pipeline install --project-path $projectPath --format json --non-interactive

unity pipeline editors --format json
unity status --until-ready --timeout 45 --project-path $projectPath --format json
unity command editor status --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity command script run --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --file AgentScripts/SetupCheck.cs --entry SetupCheck.Inspect
```

Фактически получены: `state: ready`, Editor PID 20700, порт 7800,
`projectPath: F:\UnityProjects\Trash-Hunter`, версия 6000.6.9f1.
Дополнительный вызов внутри Editor подтвердил
`Application.dataPath: F:/UnityProjects/Trash-Hunter/Assets`.
PID и порт относятся к проверенной сессии и могут измениться при перезапуске.
Всегда передавать полный `--project-path`, особенно при нескольких Editor.

В CLI 1.0.0 использовать `unity pipeline editors`: старой команды
`unity pipeline list` нет. Схемы команд получать из установленного пакета:

```powershell
unity command --query 'script run' --detail full --caller plugin --skill unity-pipeline --project-path $projectPath --format json
```

После установки сначала возник `STATUS_PIPELINE_LOAD_PENDING`: manifest уже
обновился, но Editor ещё не импортировал пакет. Пользователь переключился в
окно Unity и дождался импорта, после чего соединение заработало. Если это
повторится, переключиться в Editor и дождаться импорта/компиляции, затем повторить
`unity status`. Диалоги в Editor закрывать вручную; Safe Mode требует исправления
ошибок компиляции. В этой сессии дополнительная авторизация не потребовалась.
Если CLI явно запросит вход, выполнить `unity auth login` и завершить его в
браузере своей учётной записью. Не копировать данные авторизации в репозиторий.

На новой машине CLI устанавливается официальной командой
`irm https://unity.com/install.ps1 | iex`; после установки открыть новый PowerShell.
Unity-проект открывать установленной версией из ProjectVersion.txt.

## Git и настройки Unity

`.gitignore` настроен для проекта в корне. Library, Temp, Obj, Logs,
UserSettings, Build/Builds, генерируемые проекты IDE и локальные данные
инструментов/аккаунтов исключены. Сборки размещать в `Builds/`.
Хранить в Git Assets вместе с `.meta`, Packages вместе с `packages-lock.json`
и ProjectSettings. Перемещать ассеты через Editor, сохраняя GUID.

Проверены и на диске, и внутри подключённого Editor:

- Visible Meta Files: `VersionControlSettings.mode == "Visible Meta Files"`.
- Force Text: `EditorSettings.serializationMode == SerializationMode.ForceText`.

Менять значения не потребовалось. Игнорирование проверено для всех 70 файлов
под Assets, Packages, ProjectSettings: ни один не исключён; среди них 23 `.meta`.
В Git пока нет отслеживаемых файлов, поэтому исходники отображаются как untracked.
Кеши и файлы IDE в `git status --ignored` отмечены как ignored.
В 72 файлах под Assets, Packages, ProjectSettings, AgentScripts и docs
не найдены распространённые сигнатуры приватных ключей, AWS access key ID
и GitHub-токенов. Подозрительных имён файлов вне кешей не обнаружено.
Это ограниченная проверка; `.gitignore` не заменяет проверку содержимого
исходников перед будущим добавлением. Logs, включая снимок проверки,
и типовые локальные пути аккаунтов подтверждённо игнорируются.

## SetupCheck

Создана и сохранена **Assets/Scenes/SetupCheck.unity** вместе с `.meta`.
Сцена содержит четыре корневых объекта:

- Floor: Plane с коллайдером, размер 20 × 20.
- Cube: куб 1 × 1 × 1 на полу, центр на высоте 0.5.
- Main Camera: камера с AudioListener, направленная на куб.
- Directional Light: направленный свет.

Сцена создана через Editor командой `script run`, а не ручной записью YAML.
`AgentScripts/SetupCheck.cs` хранится вне Assets, компилируется Pipeline в памяти
и не включается в игру. Entry point `SetupCheck.Inspect` читает окружение,
`SetupCheck.Create` создаёт сцену, `SetupCheck.Verify` проверяет активную сцену.
Create отказывается перезаписывать существующую SetupCheck и закрывать
несохранённые пользовательские сцены. Повторно Create запускать не нужно.
Исходная SampleScene была сохранена и не изменялась. SetupCheck оставлена открытой;
список сцен сборки не менялся. Игровые механики не реализованы.

## Фактически выполненные проверки

- `script run` для Inspect, Create и Verify завершились без diagnostics.
- `script recompile` вернул `up_to_date`; `script recompile_status` подтвердил
  `failed: false`, `compilationFailed: false`, `errors: []`.
- Console прочитана до Play Mode, во время и после него, без очистки:
  `consoleErrors: 0`, `consoleWarnings: 0`, `compilationFailed: false`.
- Editor сначала был в Edit Mode. Включены временные auto tick и background
  override, выполнены `editor.playmode start` и ожидание готовности.
- Серверный `job wait` с условием изменения `UnityEngine.Time.frameCount`
  вернул `met: true`, `timedOut: false`: кадр изменился с 4765 на 4779.
  Дополнительно `status` сообщил `playerLoopTicking: true`.
- Получен снимок Main Camera 1280 × 720:
  `Logs/SetupCheck-play.png`. Изображение просмотрено: видны пол и освещённый куб.
  Это снимок рендера камеры, а не захват окна Game View.
- Выполнен `editor.playmode stop`; повторный Verify подтвердил сохранённую сцену
  и `playing: false`. Финальный `editor status`: ready, playMode stopped,
  compiling false. Background override снят, auto tick выключен.
- Проверены наличие сцены и `.meta`, прежняя версия Unity и Git ignore.

Команды повторной проверки при открытой SetupCheck:

```powershell
unity command script recompile --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity command script recompile_status --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity command console status --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity command console get --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --tail 30
unity command script run --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --file AgentScripts/SetupCheck.cs --entry SetupCheck.Verify
```

Для повторного Play Mode из остановленного Editor:

```powershell
unity command editor set_auto_tick --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --enabled true
unity command editor.playmode set_run_in_background --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --enabled true
unity command editor.playmode start --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity status --until-ready --timeout 45 --project-path $projectPath --format json
# Экранирование JSON ниже рассчитано на Windows PowerShell 5.1 и native unity.exe.
unity command job wait --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --condition '{\"member\":\"UnityEngine.Time.frameCount\",\"op\":\"changed\"}' --timeout 10
unity command capture game_view --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --source camera --output-path Logs/SetupCheck-play.png
unity command console status --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity command editor.playmode stop --caller plugin --skill unity-pipeline --project-path $projectPath --format json
unity status --until-ready --timeout 45 --project-path $projectPath --format json
unity command editor.playmode set_run_in_background --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --enabled false
unity command editor set_auto_tick --caller plugin --skill unity-pipeline --project-path $projectPath --format json -- --enabled false
```

Не останавливать Play-сессию пользователя, если она уже выполнялась до проверки.
Временные настройки восстанавливать и при неудачной проверке.
Набор тестов и сборка Player не запускались: проверена готовность Editor и
минимальной сцены. Commit и push не выполнялись. Обязательных ручных действий
для завершения этой подготовки не осталось.

Перед будущим добавлением файлов:

```powershell
git status --short
git status --short --ignored
git check-ignore -v Library/ Temp/ Logs/ UserSettings/ Builds/
git diff --check
```

## Проверка сцены поиска

Сцена — [ContainerSearch.unity](../Assets/TrashHunter/Scenes/ContainerSearch.unity).
Её, префабы и материалы создал Editor из [CreateSearchPrototype.cs](../AgentScripts/CreateSearchPrototype.cs).
Повторный запуск Create для существующей сцены не требуется. VContainer 1.19.0
зафиксирован в manifest и lock по ревизии. Уведомление для Player —
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

[VerifySearchPrototype.cs](../AgentScripts/VerifySearchPrototype.cs) проходит
маршрут виртуальными клавиатурой и мышью через Input System. Запускать на
свежем Play Mode или в только что открытом Windows Player с исходной укладкой.
Скрипт находится вне `Assets/` и не компилируется в игру.

```powershell
$projectPath = 'F:\UnityProjects\Trash-Hunter'
$pipeline = @('--caller','plugin','--skill','unity-pipeline','--project-path',$projectPath,'--format','json')
unity command script eval @pipeline -- --file AgentScripts/VerifySearchPrototype.cs --timeout 35
```

Для прогона в Player нужна локальная Development-сборка с временно включённым
Pipeline Runtime. Собирается только ContainerSearch. Запускать исполняемый файл
с рабочей папкой проекта. [VerifySearchPlayer.cs](../AgentScripts/VerifySearchPlayer.cs)
выбирает маршрут по версии собранной игры и передаёт ему ссылки на библиотеки
ввода и DI через компилятор Pipeline 1.0.0. Для сборки I-010 используется команда:

```powershell
unity command script eval --runtime TrashHunterSearch --caller plugin --skill unity-pipeline --format json -- --file AgentScripts/VerifySearchPlayer.cs --timeout 35
```

Для итогового Player Runtime включать только на время проверки.
После локальной сборки вернуть исходные настройки Editor и приложить уведомление
о лицензии рядом с исполняемым файлом.

## Проверка поясной сумки

[VerifyBagSelection.cs](../AgentScripts/VerifyBagSelection.cs) проходит I-020
виртуальным вводом на свежем Play Mode. [VerifySearchPrototype.cs](../AgentScripts/VerifySearchPrototype.cs)
учитывает подбор сумки при повторе маршрута I-010 в актуальной сцене.
Оба скрипта находятся вне `Assets/`.

~~~powershell
$projectPath = 'F:\UnityProjects\Trash-Hunter'
$pipeline = @('--caller','plugin','--skill','unity-pipeline','--project-path',$projectPath,'--format','json')
unity command script eval @pipeline -- --file AgentScripts/VerifyBagSelection.cs --timeout 55
unity command script eval --runtime TrashHunterBag --caller plugin --skill unity-pipeline --format json -- --file AgentScripts/VerifySearchPlayer.cs --timeout 55
~~~

Использовать свежую сессию для каждого маршрута. Player запускать с рабочей
папкой проекта. Управление, результаты и границы — в [отчёте I-020](sessions/2026-11-24-i020-bag-selection.md).

## Проверка автослота и продажи

В I-030 [ContainerSearch](../Assets/TrashHunter/Scenes/ContainerSearch.unity)
получила отдельный выход со двора и вторую укладку. Экран дома, предложение,
пауза и продолжение находятся в [SearchHud](../Assets/TrashHunter/Content/Search/SearchHud.prefab).
Сцена и префаб сохранены через `CreateSearchPrototype.AddExpedition`.
Подробный маршрут сохраняет [VerifyExpedition.cs](../AgentScripts/VerifyExpedition.cs).

Обычный Windows Player хранит один слот в `prototype/` внутри
`Application.persistentDataPath`. В Editor используется `prototype-editor/`.
`slot.json` — последняя опубликованная запись, `slot.backup.json` —
предыдущая проверенная, `slot.pending` — временный файл перед публикацией.
`slot.lock` удерживается открытой игрой. Повреждённый основной файл
архивируется под именем `slot.damaged-*.json` перед его заменой.

Для тестов в Editor перед Play Mode задать переменные **в процессе Unity**
через `script eval`: `TRASH_HUNTER_EDITOR_SAVE_DIR` с абсолютным путём под
`Logs/I-030/` и `TRASH_HUNTER_CHECK` со значением `prepare` или `resume`.
Запустить `script eval -- --file AgentScripts/VerifyExpedition.cs --timeout 55`
с флагами проекта и Pipeline из предыдущего раздела. Между фазами выйти из
Play Mode и начать новый. После теста снять переменные. Для регрессии сумки
использовать свежий слот и `VerifyBagSelection.cs`.

Для независимой проверки в Windows запустить локальную Development-сборку
`Builds/I-030/TrashHunterCycle.exe` с рабочей папкой проекта. Флаги
`-trash-hunter-save-dir <абсолютный-путь>` и `-trash-hunter-check <фаза>`
задают изолированный слот и маршрут. Фазы `prepare`, `resume` и `home`
используют тот же тестовый каталог и каждый раз новый процесс.

~~~powershell
unity command script eval --runtime TrashHunterCycle --caller plugin --skill unity-pipeline --format json -- --file AgentScripts/VerifySearchPlayer.cs --timeout 55
~~~

Фаза `recovery` читает ранее подготовленную копию при пропавшем или
повреждённом основном файле. Фаза `invalid` проверяет отказ загрузки и
повтор чтения. Менять файлы для таких проверок только в отдельном тестовом
каталоге. Сборка, снимки, результаты и замеры — в
[отчёте I-030](sessions/2026-11-25-i030-expedition-save.md).

## Официальные инструкции

- [Unity CLI](https://docs.unity.com/en-us/unity-cli/use-unity-cli)
- [Установка и подключение Pipeline](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package)
- [Команды Editor и проектов](https://docs.unity.com/en-us/unity-cli/reference/editor-project-commands)
- [Официальный Unity-плагин](https://github.com/Unity-Technologies/unity-agent-plugin)

Сверены 16 ноября 2026. Команды script run дополнительно сверены с локальной
документацией Pipeline 1.0.0 в Library/PackageCache/com.unity.pipeline@*/Documentation~.
