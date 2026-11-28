# I-250 · Данные разговора старьёвщика

28 ноября 2026. База этапа — `c313805` в ветке `main`, рабочее дерево на старте чистое.

[TraderDialogue.json](../../Assets/TrashHunter/Resources/TraderDialogue.json) задаёт порядок тем, их стабильные ID, ключи текста и ответа, условия и тип действия. [DialogueCatalog](../../Assets/TrashHunter/Runtime/World/DialogueCatalog.cs) загружает JSON через `Resources`, проверяет определения и выдаёт доступные темы у старьёвщика. Ключи разрешаются по существующим таблицам [RU](../../Assets/TrashHunter/Resources/Localization/ru.json) и [EN](../../Assets/TrashHunter/Resources/Localization/en.json).

| ID | Тема и ответ | Видимость | Действие |
|---|---|---|---|
| `trader-turntable-offer` | «Поручение о РП-07». Старьёвщик просит привезти проигрыватель на проверку. | `QuestJournal.IsAvailable("trader-turntable")`: есть первая продажа, поручение ещё не принято. | `AcceptQuest` (1), цель в `availableQuestId`. |
| `trader-home-tag` | «Спросить о метке». Старьёвщик узнаёт партию и указывает двор мастерских. | Известны обе стороны метки (`requiresFacts: 3`), наводка ещё не получена (`excludesFacts: 4`). | `AskAboutBatch` (2). |

Маски используют существующий [StoryFact](../../Assets/TrashHunter/Runtime/Run/RunSnapshot.cs). `requiresFacts` требует все указанные факты, `excludesFacts` скрывает тему, когда известны все факты этой маски. Нулевая маска не ограничивает доступность. Две записи описывают исполняемые варианты. Ответы о состоянии поручения, предъявление проигрывателя и повторный просмотр полученной наводки по-прежнему доступны через прежние экраны.

## Проверка в Unity Editor

Unity 6000.6.9f1, CLI 1.0.0 и Pipeline 1.0.0. Использованы локальные справочники `integration-advanced.md`, `scripts.md`, `build-and-compilation.md` и `runtime.md`.

```powershell
$unityArgs = @('--caller', 'plugin', '--skill', 'unity-pipeline',
    '--project-path', 'F:\UnityProjects\Trash-Hunter', '--format', 'json')
unity command script recompile @unityArgs
unity status --until-ready --timeout 45 --project-path 'F:\UnityProjects\Trash-Hunter' --format json
unity command script recompile_status @unityArgs
unity command script run @unityArgs -- --file Logs/I-250/VerifyDialogueCatalog.cs --entry VerifyDialogueCatalog.Run
unity command console status @unityArgs
```

- Перекомпиляция завершилась со статусом `completed`, `failed: false`, `compilationFailed: false`. В конце Editor готов, Play Mode остановлен, в консоли 0 ошибок и 0 предупреждений.
- В Edit Mode загружены рабочий каталог квестов и `TraderDialogue`. Пройдено 33 проверки. Десять состояний покрыли первую продажу, каждую сторону метки по отдельности, обе стороны, принятое и завершённое поручение, известную наводку, дом и вылазку. После продажи и чтения обеих сторон вернулись одновременно `trader-turntable-offer` и `trader-home-tag`.
- Проверены типы двух действий, цель поручения и четыре ключа текста и ответа в каждой локали. Каталог отклонил повторный ID, неизвестный квест и нулевое действие. Чтение тем оставляло снимок без изменений, слот в пробе не открывался.

Игра, исполнение действий и Windows Player в I-250 не запускались. Результат относится к загрузке и читающей проверке на воссозданных состояниях в памяти. Проверка новой игровой точки входа начинается с исполнителя [I-260](../BACKLOG.md#декабрь-2026).
