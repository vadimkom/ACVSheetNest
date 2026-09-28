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
- Версия 1.1.17 (последний тег `v1.1.10`; 1.1.11–1.1.16 собирались только локально, MSI 1.1.11 ещё с FreeCAD). `installer/build-msi-1.1.17.ps1` → `SheetNest-1.1.17-win-x64.msi` (54 MB). Origin (`ManuelMRosa/SheetNest`) чужой: коммитим только локально, не пушим; там 30 коммитов сверх нашего HEAD (маркировка по цветам, имена из надписей, швы общего реза) — не влиты.
- 2026-09-27: FreeCAD и 3D-импорт (STEP/IGES, K-фактор) убраны из проекта полностью: код, 3D-поля `DetailLoadInfo` (старые .dnest с ними грузятся — неизвестные поля игнорируются), скрипты установщика, `SheetNest.wxs`, README/BUILDING/RELEASING, THIRD_PARTY_NOTICES. `build-msi-1.1.11-lite.ps1` удалён как дубль, `build-msi-1.1.11.ps1` переименован в 1.1.12.
- 2026-09-27: «Параллельных поисков» в расширенных настройках (Авто / 1–8, `SessionState.NestConcurrency`, `SparrowNestService.ConcurrencyLimit`; `SHEETNEST_MAX_CONC` по-прежнему главнее). Авто = (логич. ядра − 1) / потоков на поиск (Fast 1, Normal/Best 2), не больше 8: Celeron на 4 потока → 1 поиск на Normal, 3 на Fast. `sparrow.exe` идёт с приоритетом BelowNormal. Раскладка от этого не меняется: оба банка дали те же цифры и с автозначением, и с `SHEETNEST_MAX_CONC=1`.
- 2026-09-27: убран мёртвый UI от DeepNestSharp: AvalonDock (пакет, Ui/Docking, панели-инструменты), 12 старых UserControl (осталось DxfViewer, RotationSelector), VM невидимых документов (Preview/Properties/NestResult/SheetPlacement/PartEditor/NfpCandidateList), старый путь раскладки через NestMonitorViewModel (ExecuteNestCommand/TryStartAsync/NestWorker; NestMonitorViewModel теперь только держит результаты), Observable-обёртки NFP/размещений, неиспользуемые поведения, конвертеры и сервисы, пакеты Xaml.Behaviors и Light.GuardClauses в приложении. Ретро-тема Windows Classic и Tahoma 11 убраны: стандартные контролы WPF, Segoe UI, светлая палитра с синим акцентом Windows. Командная строка открывает .dnest и DXF тем же путём, что и перетаскивание (в очереди диспетчера, иначе await продолжался в пуле потоков). Шаги навигации и вид «Чертёж/Отчёт» переключаются по Checked (клавиатура/диктор). В «О программе» починены ссылки (Process.Start без UseShellExecute падал).
- 2026-09-27: весь интерфейс переведён (ru/bg/en): в XAML `{ui:Tr Ключ}` (Ui/TrExtension.cs, живое переключение языка), новые строки — `Ui/Localization.Texts.cs` (три языка в одной строке), `Localization.Format/Plural`; тексты Domain — через `Domain/Services/UiText` (App подключает). Отчёт PDF переведён и пишется встроенным Arial как Unicode (Type0/Identity-H, `Reports/PdfTrueTypeFont.cs`; без Arial — прежний Helvetica): раньше кириллица в PDF выходила «?». Исправлено: предпросмотр и печать отчёта передавали единицы «мм» вместо "mm", и отчёт считал в дюймах (`NestReportPdf.IsMillimetres`). Тесты, проверяющие переведённый текст, — в коллекции `UiLanguageCollection`.
- 2026-09-27: экспорт DXF — один диалог сохранения, файлы `имя_N_ШxВ_xПовторы.dxf` по плану раскроя (`MainViewModel.ExportLayoutsAsync/LayoutFileName`, тест `LayoutExportFixture`). `App` вызывает `InitializeComponent` в начале конструктора: иначе окна, созданные до загрузки темы, падали на `StaticResource` (так падал «О программе» в сборке 1.1.14).
- 2026-09-26: исправлено — DXF, брошенный на таблицу деталей, добавлялся дважды (Drop всплывал до окна) и без проверки загрузки; превью-кандидаты нестинга делили объекты с победителем, которого `DeferUnfit` мутирует в фоне; кэш габаритов `NoFitPolygon` сбрасывается после мутаций точек на месте. Размеры листов с худшим приоритетом больше не прогоняются через Sparrow, если лист лучшего приоритета уже упакован.
- 2026-09-27 проверено: núcleo 1001/1004 и aplicación 422/425 (0 fallos, 3+3 omitidos); с `SPARROW_EXE` + Fast оба банка: test2 совпадает с таблицей ниже полностью; **mixto даёт 30 colocadas, 31 aristas вместо 21** — и на коде без правок этого дня тоже, т.е. сдвиг внесён более ранней незакоммиченной работой. Решить: обновить эталон или искать причину. Полный нативный прогон всей suite не завершён (`MixedStockUtilizationFixture` ~11 мин).

- 2026-09-28: подписи деталей в раскладке DXF (`DxfExporter.GetPartNameLabel`) без префикса «Flat-Pattern - ». Линии сгиба из файлов развёрток (слой 0, цвет 8, либо слой 9) выходят в слой 9 с поворотом детали — проверено на `test-data/dxf/Flat-Pattern - *` (`DxfPartNameExportFixture`). Сгибы берутся из исходного DXF при экспорте.

- 2026-09-28 (1.1.17): PDF больше не зависит от нативного zlib (на ПК в цеху «The underlying compression routine could not be loaded correctly» — шрифт тогда пишется без сжатия, `PdfTrueTypeFont.FontFileStream`). Таблицы деталей в отчёте в одну колонку с полными именами, не влезшее переносится на страницы «(продолжение)»; номера страниц и колонка «Стр.» в плане считаются пробным проходом (`NestReportPdf.Render` дважды). Окно под вертикальный монитор 1080×1920: имя детали тянется, «Площадь» прячется при узкой таблице; на шаге «Нестинг» при портретном окне варианты над просмотром; просмотр DXF вписывает лист, когда получает размер (раньше скрытый шаг 4 открывался с неверным масштабом), пока пользователь не двигал вид.
- 2026-09-28: `.git\objects` удалён (уборка пользователя, в Корзине нет) — git не работает, локальные коммиты 460772e…d881728 потеряны; рабочее дерево цело. Открыто: цвета слоёв в программе станка (рез показывается голубым, гравировка зелёным, нужно наоборот) — пробные файлы `E:\550x550x500\probe_A_sloi_9_pervym.dxf` (слой 9 первым) и `probe_B_cveta_zel_sin.dxf` (рез цвет 3, слой 9 цвет 5), ждём, какой верный.
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
