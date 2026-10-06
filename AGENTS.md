# SheetNest — contexto para agentes

## Inicio rápido

SheetNest es una aplicación Windows de nesting para chapa metálica, derivada de DeepNestSharp. Importa DXF, distribuye piezas, permite edición manual y exporta DXF y reportes PDF. C# / .NET 6 / WPF, motor Sparrow y geometría administrada/nativa.

Al retomar:
1. Ejecutar `git status --short --branch` y `git log -5 --oneline` en este repositorio.
2. Leer este archivo y abrir documentación adicional solo según la tarea.
3. Conservar cambios existentes y trabajar sobre la rama actual.
4. Si el usuario solo pide trabajar en SheetNest, resumir el estado y preguntar qué quiere hacer. Los commits previos no son tareas pendientes.

## Rutas de esta estación

- Código: C:/Users/rosam/source/repos/DeepNestSharp
- Origin: https://github.com/ManuelMRosa/SheetNest.git
- Upstream histórico: https://github.com/9swampy/DeepNestSharp.git
- Sitio web (repositorio separado): C:/Users/rosam/sheetnest-website
- Archivos de trabajo: C:/Users/rosam/SheetNest (projects/*.dnest, test-data/dxf, reports, releases, diagnostics).
- App instalada: C:/Users/rosam/AppData/Local/Programs/SheetNest/SheetNest.exe
- Datos de ejecución: C:/Users/rosam/AppData/Local/SheetNest
- SheetNest/backups contiene copias históricas, no el código activo.
- Las notas de junio/julio en SheetNest/docs y LEEME.txt son históricas; no determinan la rama, versión ni arquitectura actuales.

## Mapa del código

- DeepNestSharp/: app WPF; MainWindow.xaml.cs (4 pantallas), Ui/Themes/SheetNestTheme.xaml (paleta; controles estándar de WPF, sin tema Classic), Ui/UserControls/DxfViewer, SessionState.cs.
- DeepNestSharp/RasterNest/: SparrowNestService.cs, CommonEdgeClearance.cs, RasterCompact.cs.
- DeepNestLib.Core/: IO/ para DXF, Placement/ para geometría y CommonEdge, NestProject/ para persistencia.
- DeepNestSharp.Domain/: modelos y configuración observable.
- SparrowEngine/: sparrow.exe y sheetnest-sparrow.patch.
- DeepNestSharp.CiTests/ y DeepNestLib.CiTests/: pruebas de aplicación y núcleo.
- NestBench/ y GpuNestLab/: benchmarks y laboratorio.
- installer/: empaquetado y política de versiones.

## Documentos según la tarea

- README.md: funciones del producto.
- BUILDING.md: requisitos y compilación.
- COMMON_LINE_IMPLEMENTATION.md: arquitectura, invariantes, límites y pruebas del corte por línea común. Leer antes de modificar esta función.
- installer/RELEASING.md: pruebas y publicación. Betas como ZIP portable; estables como MSI.
- DeepNestSharp/DeepNestSharp.csproj: versión declarada del código. No inferir la versión publicada por nombres de MSI o carpetas publish-*.

## Comandos desde la raíz del repositorio

Usar PowerShell. PowerShell 5.1 no admite && y ||.

```powershell
dotnet build .\DeepNestSharp\DeepNestSharp.csproj -c Release
dotnet test .\DeepNestSharp.CiTests\DeepNestSharp.CiTests.csproj -c Release
dotnet test .\DeepNestLib.CiTests\DeepNestLib.CiTests.csproj -c Release
& '.\DeepNestSharp\bin\Release\net10.0-windows\SheetNest.exe'
```

Ejecutar las suites por proyecto: installer/RELEASING.md documenta que dotnet test sobre toda la solución puede quedarse en restore. Elegir pruebas relacionadas con el cambio; no compilar ni ejecutar suites solo para recuperar contexto.

Para pruebas nativas de Hodgson/rendimiento seguir COMMON_LINE_IMPLEMENTATION.md y comprobar las rutas del motor/DXF. SPARROW_EXE y SHEETNEST_REQUIRE_SPARROW controlan la ejecución nativa. No presentar pruebas omitidas como verificadas.

## Estado observado el 2026-09-26

Verificar con Git al iniciar; esta sección es una referencia fechada.
- Rama: `main`.
- **Hay mucho trabajo SIN COMITEAR sobre HEAD.** `git status` antes de tocar nada.
- Rediseño completo de la interfaz de usuario (WPF) con flujo de trabajo en 4 pantallas:
  1. Модели (Детали): таблица во весь экран, inline-счетчик количества, Drag & Drop (DXF, .dnest), кнопка "⚙ Подробности...", множитель комплектов (× / ÷ с шагом).
  2. Листы: таблица листового материала, размеры, количество, безлимитный режим, приоритет листа (обрезки/остатки расходуются первыми), отступ от края.
  3. Нестинг: панель запуска и остановки, выбор усилий, список вариантов/прогонов с метриками и интерактивный предпросмотр выбранного варианта.
  4. Раскладка и отчёт: переключение чертежа DXF и сводного отчета раскроя, прямой вывод на печать, экспорт в DXF (со слоем имен деталей) и PDF.
- Платформа: решение полностью переведено на **.NET 10.0** (`net10.0` / `net10.0-windows`). Все 10 проектов обновлены, устранены зависимости от устаревшего пакета `System.Text.Json 6.0.5`.
- Suites: 1004 (núcleo), 422 (aplicación), 5 (minkowski), cero fallos на .NET 10.
- Correcciones y mejoras:
  - Множитель деталей: поле ввода с шагом по умолчанию = 1, кнопка "× Применить" (умножение количества и зеркальных копий для выделенных или всех деталей). Кнопка деления удалена по запросу.
  - Поддержка новых образцов DXF разверток тройников воздуховодов из `test-data/dxf` (`Flat-Pattern - bottoms/side/top^Tr_...`).
  - Приоритет листов: листы с наивысшим приоритетом (деловые отходы) утилизируются алгоритмом Sparrow в первую очередь.
  - Потоковая передача промежуточных вариантов нестинга в таблицу (`runsListView`): каждый seed добавляется в список с метриками (утилизация, размещено, листов, время) и доступен для интерактивного предпросмотра и перехода к раскладке.
  - Векторный интерактивный просмотр отчёта раскроя PDF на экране: единая абстракция `IReportCanvas` (`WpfPageCanvas` и `NestReportPdf.PageContent`), формирующая идентичные векторные страницы A4 альбомной ориентации (842×595 пт) с тенями, таблицами, деталями с заливками по цветам и номерами позиций.
  - Полнофункциональная панель управления просмотром отчёта на 4-м экране: зум (кнопки + / -, 100%, «По ширине», Ctrl+колесо мыши), навигация по страницам (Стр. X из Y), сохранение в PDF в 1 клик, прямая многостраничная печать отчета на принтер через `System.Windows.Documents.FixedDocument` в векторном режиме (`PageOrientation.Landscape`).
  - NRE в `OnOffcutSettings` решен; mm по умолчанию в приложении (`unitsMm = true`, `EffectiveUnitsMm`).
- Версия 1.1.27 (последний тег `v1.1.10`; 1.1.11–1.1.26 собирались только локально, MSI 1.1.11 ещё с FreeCAD). `installer/build-msi-1.1.27.ps1` → `SheetNest-1.1.27-win-x64.msi` (54 MB). Origin (`ManuelMRosa/SheetNest`) чужой: коммитим только локально, не пушим; там 30 коммитов сверх нашего HEAD (маркировка по цветам, имена из надписей, швы общего реза) — не влиты.
- 2026-09-27: FreeCAD и 3D-импорт (STEP/IGES, K-фактор) убраны из проекта полностью: код, 3D-поля `DetailLoadInfo` (старые .dnest с ними грузятся — неизвестные поля игнорируются), скрипты установщика, `SheetNest.wxs`, README/BUILDING/RELEASING, THIRD_PARTY_NOTICES. `build-msi-1.1.11-lite.ps1` удалён как дубль, `build-msi-1.1.11.ps1` переименован в 1.1.12.
- 2026-09-27: «Параллельных поисков» в расширенных настройках (Авто / 1–8, `SessionState.NestConcurrency`, `SparrowNestService.ConcurrencyLimit`; `SHEETNEST_MAX_CONC` по-прежнему главнее). Авто = (логич. ядра − 1) / потоков на поиск (Fast 1, Normal/Best 2), не больше 8: Celeron на 4 потока → 1 поиск на Normal, 3 на Fast. `sparrow.exe` идёт с приоритетом BelowNormal. Раскладка от этого не меняется: оба банка дали те же цифры и с автозначением, и с `SHEETNEST_MAX_CONC=1`.
- 2026-09-27: убран мёртвый UI от DeepNestSharp: AvalonDock (пакет, Ui/Docking, панели-инструменты), 12 старых UserControl (осталось DxfViewer, RotationSelector), VM невидимых документов (Preview/Properties/NestResult/SheetPlacement/PartEditor/NfpCandidateList), старый путь раскладки через NestMonitorViewModel (ExecuteNestCommand/TryStartAsync/NestWorker; NestMonitorViewModel теперь только держит результаты), Observable-обёртки NFP/размещений, неиспользуемые поведения, конвертеры и сервисы, пакеты Xaml.Behaviors и Light.GuardClauses в приложении. Ретро-тема Windows Classic и Tahoma 11 убраны: стандартные контролы WPF, Segoe UI, светлая палитра с синим акцентом Windows. Командная строка открывает .dnest и DXF тем же путём, что и перетаскивание (в очереди диспетчера, иначе await продолжался в пуле потоков). Шаги навигации и вид «Чертёж/Отчёт» переключаются по Checked (клавиатура/диктор). В «О программе» починены ссылки (Process.Start без UseShellExecute падал).
- 2026-09-27: весь интерфейс переведён (ru/bg/en): в XAML `{ui:Tr Ключ}` (Ui/TrExtension.cs, живое переключение языка), новые строки — `Ui/Localization.Texts.cs` (три языка в одной строке), `Localization.Format/Plural`; тексты Domain — через `Domain/Services/UiText` (App подключает). Отчёт PDF переведён и пишется встроенным Arial как Unicode (Type0/Identity-H, `Reports/PdfTrueTypeFont.cs`; без Arial — прежний Helvetica): раньше кириллица в PDF выходила «?». Исправлено: предпросмотр и печать отчёта передавали единицы «мм» вместо "mm", и отчёт считал в дюймах (`NestReportPdf.IsMillimetres`). Тесты, проверяющие переведённый текст, — в коллекции `UiLanguageCollection`.
- 2026-09-27: экспорт DXF — один диалог сохранения, файлы `имя_N_ШxВ_xПовторы.dxf` по плану раскроя (`MainViewModel.ExportLayoutsAsync/LayoutFileName`, тест `LayoutExportFixture`). `App` вызывает `InitializeComponent` в начале конструктора: иначе окна, созданные до загрузки темы, падали на `StaticResource` (так падал «О программе» в сборке 1.1.14).
- 2026-09-26: исправлено — DXF, брошенный на таблицу деталей, добавлялся дважды (Drop всплывал до окна) и без проверки загрузки; превью-кандидаты нестинга делили объекты с победителем, которого `DeferUnfit` мутирует в фоне; кэш габаритов `NoFitPolygon` сбрасывается после мутаций точек на месте. Размеры листов с худшим приоритетом больше не прогоняются через Sparrow, если лист лучшего приоритета уже упакован.
- 2026-09-27 проверено: núcleo 1001/1004 и aplicación 422/425 (0 fallos, 3+3 omitidos); с `SPARROW_EXE` + Fast оба банка: test2 совпадает с таблицей ниже полностью; **mixto даёт 30 colocadas, 31 aristas вместо 21** — и на коде без правок этого дня тоже, т.е. сдвиг внесён более ранней незакоммиченной работой. Решить: обновить эталон или искать причину. Полный нативный прогон всей suite не завершён (`MixedStockUtilizationFixture` ~11 мин).

- 2026-09-28: подписи деталей в раскладке DXF (`DxfExporter.GetPartNameLabel`) без префикса «Flat-Pattern - ». Линии сгиба из файлов развёрток (слой 0, цвет 8, либо слой 9) выходят в слой 9 с поворотом детали — проверено на `test-data/dxf/Flat-Pattern - *` (`DxfPartNameExportFixture`). Сгибы берутся из исходного DXF при экспорте.

- 2026-09-28 (1.1.17): PDF больше не зависит от нативного zlib (на ПК в цеху «The underlying compression routine could not be loaded correctly» — шрифт тогда пишется без сжатия, `PdfTrueTypeFont.FontFileStream`). Таблицы деталей в отчёте в одну колонку с полными именами, не влезшее переносится на страницы «(продолжение)»; номера страниц и колонка «Стр.» в плане считаются пробным проходом (`NestReportPdf.Render` дважды). Окно под вертикальный монитор 1080×1920: имя детали тянется, «Площадь» прячется при узкой таблице; на шаге «Нестинг» при портретном окне варианты над просмотром; просмотр DXF вписывает лист, когда получает размер (раньше скрытый шаг 4 открывался с неверным масштабом), пока пользователь не двигал вид.
- 2026-09-28: `.git\objects` был удалён при уборке; восстановлено `git fetch` + один коммит fa6abb6 со всей работой (прежние локальные коммиты потеряны).
- 2026-09-28 (1.1.18): **слои выгрузки DXF — только три, `DeepNestLib.Core/IO/DxfOutputLayers.cs`**: CUT (всё, что режется: контуры, отверстия, общие резы, линия остатка), PAINT (линии сгиба + имена для гравировки), BACKGROUND (имена для просмотра). Все сущности ByLayer, рез первым (`DxfExporter.Finish`). Сейчас CUT=«0»/7, PAINT=«9»/1, BACKGROUND=«Background»/8 — как SolidWorks/Solid Edge Nesting. Настройка «Выделять цветом отверстия» (`DifferentiateChildren`) удалена вместе с параметром из API экспорта. Тесты, проверяющие роли геометрии общего реза, пишут `RoleLayers = true` / `ExportDxf(..., roleLayers: true)`. **Проверено в SC2000 (2026-09-29):** верен вариант C (отверстия без своего цвета) — текущие слои правильные, оставляем.
- 2026-09-28 (скорость, для J4125 у лазера): **чтение DXF** — `DxfParser.ConnectElements` был O(n²) (сетка скруббера 1393 отверстия/88k сегментов = 4 мин при добавлении, раскладке и экспорте); теперь сетка концов (`EndpointGrid`) с тем же правилом выбора — на 324 чертежах с E: контуры побайтно те же, 280 с → 1,4 с (`DxfChainingSpeedFixture`, опционально `DumpChainedContours`). `FlatDxfJoiner` (склейка листа при экспорте) — то же. `RasterCompact.Intersects` берёт только контуры, чей габарит задевает габарит соседа (точно). Fast/Normal: поисков = ядра / потоки на поиск (раньше ядра−1: на 4 ядрах 4 гонки последнего листа шли как 3+1). **Sparrow пересобран** (Rust в `%USERPROFILE%\.cargo`, исходник `961ec31` + 2 патча в `SparrowEngine/sheetnest-sparrow.patch`, см. BUILDING.md): флаги усилия поиска; без флагов вывод побайтно прежний. Fast передаёт `--shrink-step 0.005 --max-fails 3 --compress-share 0 --no-improve-limit 50` (`SparrowNestService.EngineSearchFlags`), кроме листов с деталями общего реза (там прежний поиск — эталоны общего реза сняты на нём). Замер 4 задачи × 2 набора seed, 4 ядра: 421 с → 100 с, листов 47 → 46. Замер проекта: `NestSpeedBenchFixture` (SHEETNEST_BENCH_PROJECT, SHEETNEST_SPARROW_ARGS, SHEETNEST_DUMP_JOBS). Проверено: весь набор приложения с SPARROW_EXE 451/453 (2 пропуска — банки), банки общего реза на Fast совпали с таблицей ниже (mixed 30/31, как и до этой работы). Открыто: уплотнение сетки скруббера всё ещё ~20–40 с суммарно.
- 2026-09-29 (1.1.20): стандартные листы (мм) — только 2000×1000, 2500×1250, 1250×500, остальное через «Свой размер»; в диалоге листа фокус сразу в «Ширину». Дубли кнопок PDF/печати в панели просмотра отчёта убраны. В просмотре DXF (шаги 3 и 4) на деталях подписи — имя как в DXF (`DxfExporter.GetPartNameLabel`, центр на металле `ComputePartCentroid`, перенос по «^»), щелчок по детали вне ручной правки подсвечивает её и пишет в строке состояния имя, габариты и X/Y левого нижнего угла (`DxfViewer.PartInspected` → `statusPartInfo`). Normal оставлен как есть: замер 3 наборов seed — Normal 7 листов на 72 деталях 1 раз из 3 при ×15–20 времени Fast, на скруббере хуже Fast; мягкие варианты не дают выигрыша. Перепаковка хвоста (`SparrowNestService.ShrinkTail`) написана, но **выключена** (`TailRepackEnabled = false`) — не замерена; арифметика: срабатывает только при маленьком хвосте.
- 2026-09-29 (1.1.21): таблица «Варианты раскладки» — только готовые раскладки всего заказа (как в Solid Edge Nesting): одна строка на «Пуск», одинаковые не повторяются, сверху лучшая ★ (все детали → меньше листов → больше использование), MainWindow.AddFinishedNest. Промежуточные попытки по листам только рисуются в превью («Идёт поиск: N из M»), в список больше не попадают (раньше неполный первый лист с «68%» стоял рядом с готовой раскладкой).

- 2026-09-29 (1.1.22): **CHF (формат SC2000) как входной чертёж** — `DeepNestLib.Core/IO/ChfReader.cs` (текст `scFlie`: graph → Gly тип 2 отрезок, 4 окружность, 7 B-сплайн; после `<End Glyphs>` слой CAM (0 = рез → слой `0`, иначе → `9`) и внешний/внутренний контур; неизвестный тип — ошибка с номером). Все чтения исходников деталей идут через `DrawingFile.Load` (узнаёт CHF по содержимому), `IsDxf` принимает `.chf`. Образец `test-data/chf/sides.chf`, тест `ChfReaderFixture`. Линия отреза остатка больше не пишется в DXF (контур остатка в просмотре остался).
- 2026-09-29 (1.1.23): таблица деталей — колонка «Площадь» убрана, вместо неё «Зеркальные» (`MirrorQuantity` прямо в строке); «Подробности…» стали «…»; кнопки ↔/↕ отражают саму деталь (`DeepNestLib.Core/IO/PartMirror.cs`: копия чертежа в `%TEMP%\sheetnest_flip`, то же имя файла; `DetailLoadInfo.Flip`/`UnflippedPath` сохраняются, при открытии проекта копия пишется заново, повторное нажатие возвращает исходный путь). «+ Фигура…» (`AddShapeWindow`, `PartShapes`): прямоугольник/квадрат Ш×В или круг Ø с количеством, DXF в `%LOCALAPPDATA%\SheetNest\shapes\Квадрат 100x100.dxf`, повтор той же фигуры добавляет количество в ту же строку. Исправлено попутно: DXF-читатель брал дугу эллипса с концом < начала за целый эллипс; экспорт зеркальных копий не менял концы дуги эллипса; CHF теперь в мм (`$INSUNITS`). «Габариты» теперь сами запускают загрузку геометрии (раньше это делала колонка площади). Тест `PartMirrorFixture` (отражение 6 чертежей × 3 варианта, дуги/эллипс/сплайн, возврат, восстановление, фигуры). Ядро 1046/1048, приложение 448/451; проверено вручную в опубликованной сборке.
- 2026-09-29 (1.1.24): габариты в таблице деталей пропадали у части строк — `DxfParser.LoadDxfFile` синхронен, пока файл не занят, и уведомление о загрузке приходило изнутри геттера `SizeLabel` (WPF его теряет); теперь `ObservableDetailLoadInfo.Nfp` грузит в `Task.Run` один раз, результат для старого пути (отражение во время загрузки) отбрасывается. Колонки «Действия» 180, «Приоритет» 135, «Зеркальные» 100 (при 150/125 обрезались ✕ и стрелка). Отчёт: кнопка «▣ Страница целиком», она же масштаб по умолчанию; вписывание (страница/ширина) держится при изменении размера окна, ручной масштаб его снимает (`ReportFit`). Проверено вручную на наборе пользователя + sides.chf; ядро 1046/1048, приложение 448/451.
- 2026-09-30 (1.1.25): **CHF — весь формат по 578 файлам пользователя (`E:\chf`)**: виды graph 8 контур / 9 группа (деталь на раскладке листа, после членов 2 числа) / 10 текст (буквы контурами); элементы 2 отрезок, 3 дуга (центр, R, углы от/до в радианах; до > от — против часовой), 4 окружность, 6 полилиния (флаг 1 = замкнута, 0/128 открыта; вершина + bulge как в DXF), 7 сплайн (2 первых числа — мусор из памяти CAM, степень = узлов − точек − 1). Прочтение сверено с длиной, которую CAM пишет над каждым контуром (py-скрипты в scratchpad). **Слои SC2000 (со слов пользователя, 10 слоёв с разными режимами)**: 0 зелёный — рез; 1 голубой (сгибы) и 8 красный (надписи) — гравировка → слой 9; 2–7, 9 — рез другими режимами (металлы); 10 — background (рамка листа, номера деталей от CAM) — не читается вовсе (`ChfReader.LayerFor`). Пустой файл — понятная ошибка (`DrawingFile.Load`). Тест `ChfReaderFixture.EveryChfInAFolderImports` (SHEETNEST_CHF_DIR, SHEETNEST_CHF_REPORT): 572/572 импортируются, 6 пустых. Ядро 1056/1058, приложение 448/451.
- 2026-09-30 (1.1.26): данные щёлкнутой детали — крупно в верхней панели просмотра (`DxfViewer.partInfoPanel`), из строки состояния убраны. Приоритетный запас первым: таблица листов сортируется по приоритету с сохранением порядка добавления (`SortSheetsByPriority`, live), раскрои в просмотре/отчёте/экспорте — по приоритету листа (`DxfViewer.SheetPriority` ← `MainWindow.StockPriorityOf` по размеру). **«Вырезаны — разложить остальное»**: на шаге 4 под чертежом полоса миниатюр раскроев (`DxfViewer.GetLayouts/LayoutsChanged/SheetIndexChanged`, `MainWindow.LayoutCutItem`), у каждого «вырезан» / «вырезано N из M»; кнопка снимает детали вырезанных листов с заказа (`CutSheets.DeductParts`: зеркальные — с «Зеркальных», прочие — с количества, затем с запаса) и оставляет листы списанными (`CutSheets.KeepConsumed` из `lastNestConsumed`), чистит список вариантов и запускает раскладку. Количество детали может быть 0 (такие строки не идут в движок). Тест `CutSheetsFixture`. Проверено вручную: 32 детали на 2 листа, приоритетный 1250×500 первым, отмечен вырезанным → 28/28 на 2500×1250, bottoms 10→6, 1250×500 остаток 0. Приложение 453/456.
- 2026-10-06 (1.1.27): падение у лазера при закрытии («сохранить?» → Save As) — диалог открывался в папке проекта на вынутой флешке, оболочка бросает Win32Exception «cannot find the drive». `FileIoService.UsableFolder` отбрасывает несуществующую папку, `Show` при отказе оболочки открывает диалог заново без неё; `MainViewModel.Save` при пропавшей папке проекта спрашивает куда сохранить, ошибка записи — сообщение `Msg_SaveFailed` вместо падения (закрытие тогда отменяется). Предупреждение «похоже на готовую раскладку» в «Подробностях» ложно срабатывало на панно (прямоугольник с орнаментом): теперь нужно ещё ≥2 контура внутри рамки, держащих другие контуры (детали со своими отверстиями) — `EditPartWindow.ContoursHoldingContours`. Тесты `NestedSheetWarningFixture`, `FileDialogFolderFixture`. Приложение 458/461. MSI не скопирован на флешку (не подключена).

### El corte común se valida contra DOS bancos, nunca uno

Es la lección más cara de esta rama. `Test2SamePartDirectRegressionFixture` usa **una sola pieza** y
`MixedCommonLineRegressionFixture` usa **piezas distintas**, y miden cosas diferentes: un cambio puede
dejar el primero clavado y vaciar el segundo. Quitar el desplazamiento de vecinos hizo exactamente eso,
y se publicó un MSI con trabajos mixtos dando cero cortes compartidos.

Valores que no deben moverse (con `SHEETNEST_BENCH_EFFORT=Fast`):

| banco | valor |
|---|---|
| test2, 16/17/24/25 copias | 16/16 · 17/17 · 24/26 · 25/27 |
| test2, 26 copias | 26 colocadas, 27 aristas, `isolated=1`, cadena 16 |
| test2, 27 copias | 26 colocadas, 28 aristas |
| mixto | 30 colocadas, 21 aristas |
| todos | `unsafePairs=0`, `minUnrecordedGap=0.24998` |

Necesitan `SPARROW_EXE` y `SHEETNEST_MIXED_COMMON_LINE_DXF_DIR` con `test2.dxf`, `Hodgson.dxf` e
`inhole.dxf`. **Sin `SPARROW_EXE` las pruebas nativas se SALTAN y se ven exactamente igual que
verdes**; así estuvieron cinco rotas un día entero en esta misma rama.

## Mantener el contexto

Al terminar trabajo relevante, actualizar el estado con fecha, cambios, validación realmente ejecutada y pendientes concretos. Mantenerlo breve y guardar detalles técnicos en el documento correspondiente. No almacenar credenciales ni conversaciones completas. Buscar primero con rg en el módulo relevante; evitar recorrer backups, bin, obj, publish-* y todo el perfil para orientarse.
