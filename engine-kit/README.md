# engine-kit: всё для разработки движка раскладки SheetNest

Задача: [TASK.md](TASK.md). Протокол: [ENGINE_PROTOCOL.md](ENGINE_PROTOCOL.md).

Работа ведётся в своей ветке `agent/<имя>` репозитория ssh://git@g.vkom.top/vadim/sheetnest-engine.git: где работать, как представиться и что сдать — в конце
TASK.md.

## Содержимое

| Путь | Что |
|---|---|
| `jobs/*.json` | настоящие задания, снятые из SheetNest (`SHEETNEST_DUMP_JOBS`), и одно со свободным углом |
| `stress/*.json` | дополнительная проверка: задания из тестов SheetNest, на которых движки зависали или клали в разы длиннее (`run-all.ps1 -Set stress`) |
| `feedback/<имя>.md` | отзыв владельца на работу агента: что исправить |
| `bin/sparrow.exe` | эталонный движок Sparrow (jagua-rs, коммит 961ec31 с патчем SheetNest) |
| `bin/rnest-sparrow.exe` | наш быстрый движок RNest |
| `reference/sparrow/`, `reference/rnest/` | их ответы, картинки и `summary.csv` при 300 итерациях |
| `tools/check.mjs` | проверка ответа и SVG-картинка. Нужен только Node.js 18+ |
| `run-all.ps1` | прогон движка по всем заданиям, проверка, таблица и сравнение с эталоном |

## Запуск одного задания руками

Движок запускается в **пустой** рабочей папке. Ответ появляется в `output\final_job.json` этой папки.

```powershell
mkdir try1; cd try1
..\bin\sparrow.exe -i ..\jobs\stoika.json --max-iterations 300 -x -s 1 --workers 1
node ..\tools\check.mjs ..\jobs\stoika.json output\final_job.json --svg layout.svg
start layout.svg      # откроется в браузере
```

`check.mjs` печатает одну строку и, если есть нарушения, их список:

```
OK   placed 68/68  width 2546.786  density 91.65%  overlaps 0
BAD  placed 14/15  width 3995.092  density 91.49%  overlaps 2
  - положено не всё: item 7: 0/1
  - наложений: 2 (#0×#1, #0×#2)
```

- Номера `#k` — индексы в `placed_items`.
- На картинке наложившиеся детали красные. При наведении мыши видны номер, деталь и угол.
- Ключ `--json` выводит то же одной строкой JSON, для скриптов.
- Код выхода: 0 — верно, 1 — нарушения, 2 — не прочитать файлы.

Что проверяется:

- каждой детали положено ровно `demand` штук;
- угол из `allowed_orientations`;
- по Y раскладка не выше `strip_height`;
- контуры не заходят друг в друга глубже 0.01.

## Прогон по всем заданиям

```powershell
.\run-all.ps1 -Engine .\bin\rnest-sparrow.exe -Name rnest-try
.\run-all.ps1 -Engine C:\work\my-engine.exe -Seeds 1,2,3
.\run-all.ps1 -Engine C:\work\my-engine.exe -Jobs stoika -Seeds (1..10) -Iterations 100
```

Параметры:

| параметр | смысл |
|---|---|
| `-Seeds` | зёрна |
| `-Iterations` | бюджет `--max-iterations` (по умолчанию 300) |
| `-Jobs` | только эти задания |
| `-Set` | папка с заданиями: `jobs` (по умолчанию) или `stress` |
| `-Name` | имя папки в `runs\` |
| `-ExtraArgs` | добавить флаги, например незнакомые, чтобы проверить, что движок их терпит |
| `-TimeoutSec` | убить зависший процесс (по умолчанию 300) |

Результат — таблица:

```
job         seed   ok placed width  density overlaps seconds delta note
porta          1 True 15/15  4270.0 88.00          0 0.12    +6.9%
test1          1 True 4/4    4027.3 83.18          0 4.81    0.0%
```

- `delta` — ширина относительно лучшей ширины Sparrow на этом задании. Минус значит лучше эталона.
- Всё лежит в `runs\<имя>\<job>-s<seed>\`: `log.txt` (stdout и stderr), `output\final_job.json`,
  `layout.svg`, а также `runs\<имя>\summary.csv`.
- Код выхода 1, если хоть один запуск не прошёл проверку.

## Сравнение агентов (для владельца)

```powershell
.\compare-agents.ps1                      # все ветки origin/agent/*, зёрна 1,2,3
.\compare-agents.ps1 -UseTags             # последний тег <имя>-vN вместо головы ветки
.\compare-agents.ps1 -RerunReference      # перемерить и Sparrow с RNest в том же прогоне
.\compare-agents.ps1 -Agents iceman,claude-opus -Jobs stoika
.\compare-agents.ps1 -Set stress            # дополнительная проверка, итог в runs\compare-stress\
```

Скрипт работает так:

1. Делает `git fetch`.
2. Из каждой ветки берёт `dist\*.exe` через `git archive`. Рабочая копия и текущая ветка не меняются.
3. Прогоняет все движки через `run-all.ps1` строго по очереди, чтобы время было сравнимо.
4. Сводит результат в таблицу.

Колонки таблицы:

| колонка | что в ней |
|---|---|
| ширина по заданию | лучшая из зёрен, в скобках разница с лучшей у RNest |
| `max_s`, `sum_s` | время: самый долгий прогон и сумма всех |
| `minimum` | выполнен ли минимум из TASK.md |
| `copy_of_rnest` | `ДА`, если все ширины совпадают с RNest |

Итог пишется в `runs\compare\summary.md` и `summary.csv`. Ответы и картинки каждого движка лежат в
`runs\compare\run-<имя>\`.

Пока идёт сравнение, агенты на машине работать не должны: они отнимают процессор, и время получается
неверным.

## Отладка

- **Ничего не появилось.** Ответ должен лежать в `output\` **рабочей** папки, а не рядом с `job.json`.
  Смотрите `log.txt`.
- **Наложения на ровном месте.** Почти всегда это поворот не вокруг (0, 0). Проверьте одну деталь: возьмите
  её `shape.data`, поверните на `rotation` против часовой вокруг начала координат, прибавьте `translation`.
  Сравните с тем, где она лежит у вас.
- **Деталь чуть-чуть заходит на соседа.** Допуск проверки 0.01. В SheetNest зазор считается по
  номинальным контурам, так что любое наложение расширенных контуров — это деталь, которую нельзя
  вырезать.
- **Время.** Сравнивайте при одинаковом `--max-iterations`. Скрипт меряет полное время процесса.
- **Свои задания.** В SheetNest задайте `SHEETNEST_DUMP_JOBS=C:\папка` и нажмите «▶ Nest»: каждое
  отданное движку задание скопируется туда как `job_N.json`.
- **В самой программе.** `SHEETNEST_SPARROW=C:\путь\engine.exe`, затем запустить SheetNest. Полоска
  прогресса двигается по строкам `dens: 87.4%` в stdout.
