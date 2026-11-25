# Передача — I-070

I-060 завершён. Следующий этап — **I-070, перетаскивание с весом и бросок**.
Ветка I-060 — `main`, корень — `F:\UnityProjects\Trash-Hunter`. На старте
дерево было чистым, HEAD `c4dfdd8`. Код `SearchItem.cs` совпадал с HEAD.
Изменения I-060 сохранены одним локальным коммитом. Снимки и тестовые
автослоты находятся в игнорируемом Git каталоге `Logs/I-060/`.

ПКМ по уложенной вещи начинает перенос. `R` поворачивает её, ЛКМ применяет
допустимое размещение. ПКМ или `Esc` отменяют перенос, `Tab` закрывает сумку
и отменяет предпросмотр. [SearchSession](../../../Assets/TrashHunter/Runtime/SearchSession.cs)
держит предпросмотр, [BagInventory](../../../Assets/TrashHunter/Runtime/BagInventory.cs)
проверяет клетки, [GameState](../../../Assets/TrashHunter/Runtime/GameState.cs)
публикует подтверждённое положение через прежний автослот. Строки сумки и её
подсказок теперь берутся из таблиц [RU](../../../Assets/TrashHunter/Resources/Localization/ru.json)
и [EN](../../../Assets/TrashHunter/Resources/Localization/en.json).

В ContainerSearch этап проверен через Unity Pipeline и
[VerifyBagRearrangement](../../../AgentScripts/VerifyBagRearrangement.cs).
Сначала воспроизведена исходная блокировка. Фазы `bag-move` и `bag-resume`
пройдены в двух локалях с отдельными автослотами. Проверены пересечение,
выход за край, отмена, сбой записи и повтор, сохранность ID, укладка
и продолжение после остановки Play Mode. Снимки и команды перечислены в
[отчёте I-060](../../sessions/2026-11-25-i060-bag-rearrangement.md).
Приёмка физической мышью и Windows Player остаётся на итоговый прогон I-120.

Для I-070 прочитать [план](PLAN.md), [контекст](CONTEXT.md),
[очередь](../../BACKLOG.md#поиск-в-плотной-куче) и
[отзыв владельца](../../sessions/2026-11-25-owner-playtest.md).
В Editor зафиксировать, как сейчас удерживаются лёгкая и крупная вещи.
Проследить `TakeInHand`, `PresentHand`, `TryPlace` и сохранение позы
по [SearchItem](../../../Assets/TrashHunter/Runtime/SearchItem.cs) и
[SearchSession](../../../Assets/TrashHunter/Runtime/SearchSession.cs).
Подобрать способы удержания по поведению известных предметов сцены и
действующим критериям. Способ физического удержания и значения масс
пока открыты.
