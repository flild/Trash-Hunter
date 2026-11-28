# I-240 · Первая глава в Windows Player

28 ноября 2026 года. Ветка `main`, исходная ревизия `95d4428`. Приёмка пройдена в Windows x64 Development Player на Unity 6000.6.9f1 (8c45277fb9c5), RU, окно 1280 × 720. Сборка `build_f0c4d7b3364b` завершилась без ошибок и предупреждений. Локальный комплект `Builds/I-240/TrashHunterChapterQA.exe` содержит пять игровых сцен и уведомление о лицензии.

| Запуск | Результат |
|---|---|
| `route`, PID 35152, код 0 | 46 проверок. Подбор сумки, продажа двух находок, покупка рюкзака, осмотр обеих сторон метки и получение наводки. Игрок прошёл от ворот второго двора до контейнера и осмотрел кассетник с заклинившей крышкой и номером 17-Б. |
| `resume`, PID 12976, код 0 | 6 проверок на том же изолированном слоте. `SecondYard` открылся в режиме продолжения. Формат 6, ревизия 32, четвёртая вылазка, 23 ₽, рюкзак 3 × 3, сюжетные факты и все 25 записей укладки совпали с загруженным снимком. В руке один `batch-recorder`, номер 17-Б читается повторно. |

Первый процесс закрылся штатно с записью слота. Повторный осмотр во втором процессе сохранил содержимое файла до выхода. Штатный выход записал ревизию 33 с тем же кассетником и фактами. Девять контрольных значений снимка сверялись в памяти второго процесса, включая ID прохождения, факты, руку и записи всех предметов.

Сценарий подаёт команды через Input System и ставит игрока в контрольные точки для взаимодействий. От ворот до контейнера пройден участок движением. Проверка отказа записи охватывает оборот метки и отправление во второй двор. Габариты текста измерены с учётом переносов. [Сценарий главы](../../AgentScripts/Verification/VerifyChapterEvidence.cs) · [Точка входа Player](../../AgentScripts/Verification/RunChapterPlayer.cs).

Полный отчёт сборки находится в `Logs/I-240/build-report.json`. В `Logs/I-240/ru-acceptance/` находятся `route.json`, `resume.json`, журналы, копия входного слота `slot-before-resume.json` и итоговый `slot.json`. Сверка 572 записей инвентаризации подтвердила сохранность старых комплектов I-040, I-120 и I-190 и пользовательских слотов. Журналы `protected-before.txt` и `protected-after.txt` лежат в `Logs/I-240/`.

## Воспроизведение

В корне проекта прочитать [процедуру Unity CLI](../DEVELOPMENT.md). Проверочный Player собран через Editor Pipeline. Автопроверка отклонила изменение настройки Runtime Pipeline, поскольку она добавляет HTTP-сервер в Development Player. Для проверки использована отдельная точка входа, которая выполняется при указанной фазе и завершает процесс. Перед сборкой исходники временно копировались в `Assets/TrashHunterI240Probe/`. В копии `VerifyChapterEvidence.cs` весь файл обёрнут в `#if !UNITY_EDITOR && DEBUG`. После сборки обе копии и их метаданные удалены через AssetDatabase, автоматически перезаписанные файлы настроек Unity возвращены по Git. Настройки Runtime Pipeline сверены после прогона.

```powershell
$project = 'F:\UnityProjects\Trash-Hunter'
Set-Location -LiteralPath $project
$p = @('--caller','plugin','--skill','unity-pipeline','--project-path',$project,'--format','json')
$probe = 'Assets/TrashHunterI240Probe'
if (Test-Path -LiteralPath $probe) { throw 'Probe folder already exists' }
New-Item -ItemType Directory -Path $probe | Out-Null
Copy-Item -LiteralPath 'AgentScripts/Verification/RunChapterPlayer.cs' -Destination $probe
$route = Get-Content -LiteralPath 'AgentScripts/Verification/VerifyChapterEvidence.cs' -Raw -Encoding UTF8
Set-Content -LiteralPath "$probe/VerifyChapterEvidence.cs" -Value ("#if !UNITY_EDITOR && DEBUG`n" + $route + "`n#endif") -Encoding UTF8

unity command script eval @p -- 'UnityEditor.AssetDatabase.Refresh();'
unity status --until-ready --timeout 45 --project-path $project --format json
unity command build run @p -- --target StandaloneWindows64 --output-path Builds/I-240/TrashHunterChapterQA.exe --options '[\"Development\",\"DetailedBuildReport\"]' --scenes '[\"Assets/TrashHunter/Scenes/Startup.unity\",\"Assets/TrashHunter/Scenes/ContainerSearch.unity\",\"Assets/TrashHunter/Scenes/Home.unity\",\"Assets/TrashHunter/Scenes/Trader.unity\",\"Assets/TrashHunter/Scenes/SecondYard.unity\"]' --confirm true
unity command build status @p
```

После `Succeeded` удалить временную папку через Editor и положить в комплект `docs/THIRD_PARTY_NOTICES.md`. При повторной сборке сохранять исходный проверенный комплект, указав другой путь вывода.

```powershell
$exe = (Resolve-Path -LiteralPath 'Builds/I-240/TrashHunterChapterQA.exe').Path
$slot = Join-Path $project ('Logs/I-240/ru-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $slot | Out-Null
foreach ($phase in 'route','resume') {
    $playerArgs = @('-trash-hunter-i240-check', $phase, '-trash-hunter-save-dir',
        ('"{0}"' -f $slot), '-trash-hunter-locale','ru','-screen-fullscreen','0',
        '-screen-width','1280','-screen-height','720','-logFile',('"{0}/{1}.log"' -f $slot,$phase))
    $player = Start-Process -FilePath $exe -WorkingDirectory $project -ArgumentList $playerArgs -PassThru
    if (!$player.WaitForExit(55000)) { throw "Player $($player.Id) is still running" }
    if ($player.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$slot/$phase.json")) {
        throw "Check $slot/$phase.log"
    }
    if ($phase -eq 'route') {
        Copy-Item -LiteralPath "$slot/slot.json" -Destination "$slot/slot-before-resume.json"
    }
}
```

## Наблюдения и охват

В обоих итоговых журналах по четыре сообщения URP о шейдерах GaussianDepthOfField, BokehDepthOfField и PaniniProjection. Такие сообщения были и в [I-190](2026-11-27-i190-world-acceptance.md#приёмка). Маршрут завершился. Визуальное качество эффектов, Release, EN в Player и проход владельцем остаются вне приёмки I-240. Исторические проверки I-230 на RU и EN относятся к Editor.

Промежуточные прогоны сохранены в `Logs/I-240/`. Скрытый запуск с активным сценарием остановился на подборе сумки (`ru-baseline`). При смене флага условной компиляции проверочный сценарий выпал из двух запусков (`ru-splash-wait` и `ru-windowed`), это выявлено по DLL. В `ru-chapter` ошибочно сработала проверка ширины переносимой строки. Итоговая сборка содержит сценарий с `DEBUG`, а проверка ширины учитывает переносы. Игровой код и сцены этапу менять не потребовалось.

При подготовке использованы локальные руководства Unity `build-run-test.md`, `runtime-setup.md` и `commands/build-and-compilation.md`.
