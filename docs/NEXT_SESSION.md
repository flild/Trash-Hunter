# I-250 · Данные разговора старьёвщика

Взять один этап по [текущей передаче](relay/HANDOFF.md). Сверить [инструкции проекта](../AGENTS.md), Git status, [строку I-250 в бэклоге](BACKLOG.md#декабрь-2026) и [контекст эстафеты](relay/CONTEXT.md). Исходный Windows-маршрут зафиксирован в [I-240](sessions/2026-11-28-i240-chapter-windows.md).

Первое действие — найти существующие поводы разговора в [SettlementSession](../Assets/TrashHunter/Runtime/World/SettlementSession.cs). Проследить доступность поручения по [QuestJournal](../Assets/TrashHunter/Runtime/Run/QuestJournal.cs) и наводки по [GameState](../Assets/TrashHunter/Runtime/Run/GameState.cs). Сверить их с [сюжетом](design/STORY.md) и [процедурой Unity](DEVELOPMENT.md).

Описать ID, текст, условия видимости, ответ и действие реплики. Загрузить пример, где одновременно доступны темы поручения и домашней метки. Исполнитель команд и окно разговора входят в I-260 и I-270. Записать свежий результат и обновить передачу по итоговому статусу I-250.
