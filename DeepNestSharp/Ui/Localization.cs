namespace DeepNestSharp.Ui
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;

  /// <summary>
  /// Multi-language localization support for SheetNest (Russian, Bulgarian, English).
  /// </summary>
  public static partial class Localization
  {
    public const string LangRussian = "ru";
    public const string LangBulgarian = "bg";
    public const string LangEnglish = "en";

    private static string currentLanguage = LangRussian;

    public static event Action? LanguageChanged;

    public static string CurrentLanguage
    {
      get => currentLanguage;
      set
      {
        string normalized = NormalizeLanguage(value);
        if (currentLanguage != normalized)
        {
          currentLanguage = normalized;
          PriorityHelper.UpdateLanguage(currentLanguage);
          LanguageChanged?.Invoke();
        }
      }
    }

    public static string NormalizeLanguage(string? lang)
    {
      if (string.IsNullOrWhiteSpace(lang))
      {
        return LangRussian;
      }

      string lower = lang.Trim().ToLowerInvariant();
      if (lower.StartsWith("bg"))
      {
        return LangBulgarian;
      }

      if (lower.StartsWith("en"))
      {
        return LangEnglish;
      }

      return LangRussian;
    }

    public static string Get(string key)
    {
      if (Translations.TryGetValue(currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
      {
        return val;
      }

      // Fallback to Russian, then English
      if (Translations[LangRussian].TryGetValue(key, out var ruVal))
      {
        return ruVal;
      }

      if (Translations[LangEnglish].TryGetValue(key, out var enVal))
      {
        return enVal;
      }

      return key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations =
      new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
      {
        [LangRussian] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          // Menus
          ["Menu_File"] = "_Файл",
          ["Menu_NewProject"] = "Новый проект",
          ["Menu_OpenProject"] = "Открыть проект...",
          ["Menu_RecentProjects"] = "Последние проекты",
          ["Menu_RecentEmpty"] = "(пусто)",
          ["Menu_Save"] = "Сохранить",
          ["Menu_SaveAs"] = "Сохранить как...",
          ["Menu_Exit"] = "Выход",
          ["Menu_Settings"] = "_Настройки",
          ["Menu_Offcut"] = "Деловые отходы...",
          ["Menu_AdvancedSettings"] = "Расширенные настройки...",
          ["Menu_Language"] = "_Язык (Language)",
          ["Menu_Help"] = "_Справка",
          ["Menu_CheckUpdates"] = "Проверить обновления...",
          ["Menu_SendFeedback"] = "Обратная связь...",
          ["Menu_About"] = "О программе",

          // Toolbar Buttons
          ["Btn_New"] = "Новый",
          ["Btn_Open"] = "Открыть...",
          ["Btn_Save"] = "Сохранить",

          // Workflow Tabs
          ["Tab_Parts"] = "1. Модели (Детали)",
          ["Tab_Sheets"] = "2. Листы",
          ["Tab_Nesting"] = "3. Нестинг",
          ["Tab_Layout"] = "4. Раскладка и отчёт",

          // Screen 1: Parts
          ["Btn_AddDxf"] = "+ Добавить DXF...",
          ["Btn_DeletePart"] = "Удалить деталь",
          ["Btn_ClearAll"] = "Очистить всё",
          ["Hint_DragDrop"] = "💡 Можно перетащить файлы DXF прямо в эту таблицу (Drag & Drop)",
          ["Btn_NextSheets"] = "Далее: Листы ➔",
          ["Parts_CountSummary"] = "Деталей: {0} (запрошено: {1} шт.)",
          ["Col_Preview"] = "Эскиз",
          ["Col_PartName"] = "Деталь (Название / Файл)",
          ["Col_Quantity"] = "Количество",
          ["Col_Dimensions"] = "Габариты (Ш × В)",
          ["Col_NetArea"] = "Площадь",
          ["Col_Color"] = "Цвет",
          ["Col_Priority"] = "Приоритет",
          ["Col_Status"] = "Статус",
          ["Col_Actions"] = "Действия",
          ["Btn_EditDetails"] = "⚙ Подробности...",
          ["Unit_Pcs"] = "шт.",

          // Screen 2: Sheets
          ["Btn_BackToParts"] = "⮜ Назад к моделям",
          ["Btn_NextNesting"] = "Далее: Нестинг ➔",
          ["Btn_AddSheet"] = "+ Добавить лист...",
          ["Btn_DeleteSheet"] = "Удалить лист",
          ["Lbl_SheetEdgeMargin"] = "Отступ от края листа:",
          ["Sheets_CountSummary"] = "Типоразмеров: {0} (листов: {1})",
          ["Sheets_UnlimitedNote"] = "есть безлимитные",
          ["Col_SheetSize"] = "Размер листа (Ш × В)",
          ["Col_Unlimited"] = "Неограниченно",
          ["Col_AvailableUsed"] = "Доступно / Использовано",
          ["Chk_Unlimited"] = "Без ограничений",
          ["Menu_MySheets"] = "Мои форматы",
          ["Menu_RemoveMySheets"] = "Удалить из моих форматов",
          ["Menu_CustomSize"] = "Произвольный размер…",

          // Screen 3: Nesting
          ["Btn_StartNest"] = "▶ Пуск нестинга",
          ["Btn_StopNest"] = "⏹ Остановить",
          ["Lbl_NestingEffort"] = "Усилия нестинга:",
          ["Effort_Fast"] = "Fast (Быстрый)",
          ["Effort_Normal"] = "Normal (Стандартный)",
          ["Effort_Best"] = "Best (Глубокий поиск)",
          ["Lbl_RunsList"] = "Варианты раскладки:",
          ["Btn_GoToLayout"] = "Перейти к раскладке ➔",
          ["Col_RunNum"] = "№",
          ["Col_RunTime"] = "Время",
          ["Col_RunSheets"] = "Листов",
          ["Col_RunUtil"] = "Утилизация",
          ["Col_RunPlaced"] = "Размещено",
          ["Nesting_VariantMetrics"] = "Вариант #{0}: Утилизация {1} | Размещено {2} | Листов: {3} (время: {4})",

          // Screen 4: Layout & Report
          ["Btn_BackToNesting"] = "⮜ Назад к нестингу",
          ["Radio_DxfView"] = "Чертеж DXF",
          ["Radio_ReportView"] = "Сводный отчет раскроя",
          ["Btn_PrintReport"] = "🖨 Печать отчета...",
          ["Btn_ExportDxf"] = "Экспорт в DXF...",
          ["Btn_ExportPdf"] = "Экспорт в PDF...",
          ["Kpi_Utilization"] = "Общая утилизация:",
          ["Kpi_SheetsUsed"] = "Листов израсходовано:",
          ["Kpi_PartsPlaced"] = "Деталей размещено:",
          ["Kpi_Unplaced"] = "Не поместилось:",
          ["Report_Title"] = "СВОДНЫЙ ОТЧЕТ РАСКРОЯ",
          ["Report_SheetsMapTitle"] = "1. Карта раскроя по листам",
          ["Report_PartsSpecTitle"] = "2. Спецификация раскроенных деталей",
          ["Report_ColSheetNum"] = "Лист №",
          ["Report_ColSheetFormat"] = "Формат листа",
          ["Report_ColSheetRepeat"] = "Кол-во листов",
          ["Report_ColSheetParts"] = "Деталей на листе",
          ["Report_ColSheetUtil"] = "Утилизация",
          ["Report_ColPartPos"] = "Поз. №",
          ["Report_ColPartName"] = "Деталь / Файл",
          ["Report_ColPartDim"] = "Габариты детали",
          ["Report_ColPartQty"] = "Всего шт.",
          ["Report_ColPartArea"] = "Площадь (шт.)",
          ["Report_UnitsPrefix"] = "Единицы: ",
          ["Report_DatePrefix"] = "Дата: ",
          ["Report_ProjectPrefix"] = "Проект: ",
          ["Report_UntitledProject"] = "Безымянный",
          ["Report_PreviewToolbar_SavePdf"] = "📄 Сохранить PDF",
          ["Report_PreviewToolbar_Print"] = "🖨️ Печать отчета",
          ["Report_PreviewToolbar_ZoomFit"] = "⤢ По ширине",
          ["Report_PreviewToolbar_Summary"] = "Производственный отчёт раскроя",
          ["Report_EmptyTitle"] = "Отчёт раскроя ещё не сформирован",
          ["Report_EmptySub"] = "Выполните нестинг деталей на 3-м экране, чтобы просмотреть и напечатать готовый производственный отчёт со спецификацией и картами резки.",
          ["Report_PageXofY"] = "Стр. {0} из {1}",
          ["Msg_NestFirstToPrint"] = "Сначала выполните нестинг и выберите результат для печати.",
          ["Msg_PrintError"] = "Ошибка печати",

          // Priority levels
          ["Priority_Highest"] = "Наивысший",
          ["Priority_High"] = "Высокий",
          ["Priority_Normal"] = "Обычный",
          ["Priority_Low"] = "Низкий",
          ["Priority_Lowest"] = "Наименьший",

          // Status bar
          ["Status_Utilization"] = "Утилизация:",
          ["Status_Sheets"] = "Листов:",
          ["Status_Placed"] = "Размещено:",
          ["Status_Unplaced"] = "Не поместилось:",

          // Parts Multiplier
          ["Lbl_Multiplier"] = "Множитель (комплектов):",
          ["Btn_Multiply"] = "× Умножить",
          ["Btn_Divide"] = "÷ Разделить",
          ["Tip_Multiplier"] = "Множитель количества деталей (например, число изготавливаемых комплектов)",
          ["Tip_Multiply"] = "Умножить количество всех (или выбранных) деталей на указанный множитель",
          ["Tip_Divide"] = "Разделить количество всех (или выбранных) деталей на указанное число",

          // Sheet Priority
          ["Col_SheetPriority"] = "Приоритет",
          ["Tip_SheetPriority"] = "Приоритет расходования листов: листы с наивысшим приоритетом расходуются в первую очередь (например, остатки и обрезки)",

          // Advanced Settings Dialog
          ["Settings_Title"] = "Расширенные настройки",
          ["Settings_Group_Nesting"] = "Параметры раскроя",
          ["Settings_Rotations"] = "Углы поворота деталей:",
          ["Settings_Rot_AsDrawn"] = "Как начертано",
          ["Settings_Rot_0_180"] = "0 / 180°",
          ["Settings_Rot_90"] = "Шаг 90°",
          ["Settings_Rot_45"] = "Шаг 45°",
          ["Settings_Rot_Free"] = "Свободный (36 шагов)",
          ["Settings_Spacing"] = "Зазор между деталями",
          ["Settings_Margin"] = "Отступ от края листа",
          ["Settings_Effort"] = "Усилие поиска:",
          ["Settings_Effort_Fast"] = "Быстрый",
          ["Settings_Effort_Normal"] = "Оптимальный (по умолчанию)",
          ["Settings_Effort_Best"] = "Глубокий (медленнее)",
          ["Settings_Concurrency"] = "Параллельных поисков:",
          ["Settings_Concurrency_Auto"] = "Авто ({0})",
          ["Settings_Concurrency_Tip"] = "Сколько поисков раскладки идёт одновременно. Меньше — слабый компьютер не тормозит, больше — быстрее на мощном. Сама раскладка от этого не меняется.",
          ["Settings_Group_CommonLine"] = "Резка по общей линии (совмещенный рез)",
          ["Settings_CommonLineGap"] = "Зазор резки по общей линии",
          ["Settings_Reset"] = "Сброс",
          ["Settings_MaxCluster"] = "Макс. деталей в кластере:",
          ["Settings_Auto"] = "Авто",
          ["Settings_Manual"] = "Вручную",
          ["Settings_Group_Dxf"] = "Параметры экспорта DXF",
          ["Settings_MergeLines"] = "Объединять совпадающие линии реза",
          ["Settings_PartNames"] = "Имена деталей в DXF:",
          ["Settings_PartNames_None"] = "Не экспортировать",
          ["Settings_PartNames_Layer9"] = "PAINT — гравировка, вместе с линиями сгиба",
          ["Settings_PartNames_Background"] = "BACKGROUND — только для просмотра",
          ["Settings_Group_App"] = "Параметры приложения",
          ["Settings_Units"] = "Единицы измерения:",
          ["Settings_Units_In"] = "Дюймы (in)",
          ["Settings_Units_Mm"] = "Миллиметры (мм)",
          ["Settings_Autosave"] = "Автосохранение открытого проекта",
          ["Settings_AutosaveTime"] = "Интервал автосохранения (мин):",
          ["Dxf_EmptyHint"] = "Запустите нестинг, чтобы увидеть результат.",
          ["Dxf_NavHint"] = "колесо = масштаб  ·  зажатое колесо = перемещение  ·  правый клик = по размеру окна",
          ["Dxf_EditNavHint"] = "тяните деталь для перемещения  ·  Alt+отпустить = примагнитить  ·  стрелки = сдвиг  ·  Esc = сброс",
          ["Dxf_EditNest"] = "✋ Ручная правка",
          ["Dxf_EditNestTip"] = "Ручная доводка раскроя: кликните деталь для выбора, перетаскивайте мышью, вращайте кнопками панели.",
          ["Dxf_Measure"] = "Измерение",
          ["Dxf_MeasureTip"] = "Измерение расстояний: укажите две точки на чертеже.",
          ["Dxf_Mirror"] = "⇄ Зеркало",
          ["Dxf_PrevLayout"] = "Предыдущий раскрой",
          ["Dxf_NextLayout"] = "Следующий раскрой",
          ["Dxf_UndoTip"] = "Отменить (Ctrl+Z)",
          ["Dxf_RedoTip"] = "Повторить (Ctrl+Y)",
          ["Dxf_RotateCw90Tip"] = "Повернуть на 90° по часовой стрелке",
          ["Dxf_RotateCcw90Tip"] = "Повернуть на 90° против часовой стрелки",
          ["Dxf_RotateCw5Tip"] = "Повернуть на 5° по часовой стрелке",
          ["Dxf_RotateCcw5Tip"] = "Повернуть на 5° против часовой стрелки",
          ["Dxf_MirrorTip"] = "Отразить зеркально выбранную деталь",
          ["Dxf_CommonLineLocked"] = "детали совмещенного реза заблокированы · запустите нестинг заново для изменения их положения",
          ["Btn_ClearRuns"] = "Очистить список",
          ["Tip_ClearRuns"] = "Очистить список накопленных вариантов раскладки",
          ["Btn_OK"] = "ОК",
          ["Btn_Cancel"] = "Отмена",

          // Sheet dialog
          ["Sheet_Dlg_Title"] = "Новый размер листа",
          ["Sheet_Dlg_Title_Edit"] = "Редактировать лист",
          ["Sheet_Dlg_Header"] = "Размеры листа в {0} (ширина = длинная сторона).",
          ["Sheet_Dlg_Width"] = "Ширина ({0})",
          ["Sheet_Dlg_Height"] = "Высота ({0})",
          ["Sheet_Dlg_Qty"] = "Количество",
          ["Sheet_Dlg_Priority"] = "Приоритет расходования:",
          ["Sheet_Dlg_Unlimited"] = "Без ограничений",
          ["Sheet_Dlg_Remember"] = "Сохранить как шаблон",
          ["Sheet_Dlg_Add"] = "Добавить",
        },

        [LangBulgarian] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          // Menus
          ["Menu_File"] = "_Файл",
          ["Menu_NewProject"] = "Нов проект",
          ["Menu_OpenProject"] = "Отвори проект...",
          ["Menu_RecentProjects"] = "Скорошни проекти",
          ["Menu_RecentEmpty"] = "(празно)",

          ["Menu_Save"] = "Запази",
          ["Menu_SaveAs"] = "Запази като...",
          ["Menu_Exit"] = "Изход",
          ["Menu_Settings"] = "_Настройки",
          ["Menu_Offcut"] = "Изрезки на листа...",
          ["Menu_AdvancedSettings"] = "Разширени настройки...",
          ["Menu_Language"] = "_Език (Language)",
          ["Menu_Help"] = "_Помощ",
          ["Menu_CheckUpdates"] = "Провери за обновления...",
          ["Menu_SendFeedback"] = "Обратна връзка...",
          ["Menu_About"] = "За програмата",

          // Toolbar Buttons
          ["Btn_New"] = "Нов",
          ["Btn_Open"] = "Отвори...",
          ["Btn_Save"] = "Запази",

          // Workflow Tabs
          ["Tab_Parts"] = "1. Модели (Детайли)",
          ["Tab_Sheets"] = "2. Листове",
          ["Tab_Nesting"] = "3. Нестинг",
          ["Tab_Layout"] = "4. Разкрой и отчет",

          // Screen 1: Parts
          ["Btn_AddDxf"] = "+ Добави DXF...",

          ["Btn_DeletePart"] = "Изтрий детайл",
          ["Btn_ClearAll"] = "Изчисти всичко",
          ["Hint_DragDrop"] = "💡 Можете да пуснете DXF файлове директно в таблицата (Drag & Drop)",
          ["Btn_NextSheets"] = "Напред: Листове ➔",
          ["Parts_CountSummary"] = "Детайли: {0} (заявени: {1} бр.)",
          ["Col_Preview"] = "Скица",
          ["Col_PartName"] = "Детайл (Име / Файл)",
          ["Col_Quantity"] = "Количество",
          ["Col_Dimensions"] = "Габарити (Ш × В)",
          ["Col_NetArea"] = "Площ",
          ["Col_Color"] = "Цвят",
          ["Col_Priority"] = "Приоритет",
          ["Col_Status"] = "Статус",
          ["Col_Actions"] = "Действия",
          ["Btn_EditDetails"] = "⚙ Подробности...",
          ["Unit_Pcs"] = "бр.",

          // Screen 2: Sheets
          ["Btn_BackToParts"] = "⮜ Назад към моделите",
          ["Btn_NextNesting"] = "Напред: Нестинг ➔",
          ["Btn_AddSheet"] = "+ Добави лист...",
          ["Btn_DeleteSheet"] = "Изтрий лист",
          ["Lbl_SheetEdgeMargin"] = "Отстъп от края на листа:",
          ["Sheets_CountSummary"] = "Размери: {0} (листове: {1})",
          ["Sheets_UnlimitedNote"] = "с неограничени",
          ["Col_SheetSize"] = "Размер на листа (Ш × В)",
          ["Col_Unlimited"] = "Без ограничение",
          ["Col_AvailableUsed"] = "Достъпно / Използвано",
          ["Chk_Unlimited"] = "Без ограничение",
          ["Menu_MySheets"] = "Мои размери",
          ["Menu_RemoveMySheets"] = "Премахни от моите размери",
          ["Menu_CustomSize"] = "Индивидуален размер…",

          // Screen 3: Nesting
          ["Btn_StartNest"] = "▶ Старт нестинг",
          ["Btn_StopNest"] = "⏹ Спри",
          ["Lbl_NestingEffort"] = "Усилия за нестинг:",
          ["Effort_Fast"] = "Fast (Бърз)",
          ["Effort_Normal"] = "Normal (Стандартен)",
          ["Effort_Best"] = "Best (Дълбоко търсене)",
          ["Lbl_RunsList"] = "Варианти на разкроя:",
          ["Btn_GoToLayout"] = "Към разкроя ➔",
          ["Col_RunNum"] = "№",
          ["Col_RunTime"] = "Време",
          ["Col_RunSheets"] = "Листове",
          ["Col_RunUtil"] = "Оползотворяване",
          ["Col_RunPlaced"] = "Разположени",
          ["Nesting_VariantMetrics"] = "Вариант #{0}: Оползотворяване {1} | Разположени {2} | Листове: {3} (време: {4})",

          // Screen 4: Layout & Report
          ["Btn_BackToNesting"] = "⮜ Назад към нестинга",
          ["Radio_DxfView"] = "Чертеж DXF",
          ["Radio_ReportView"] = "Обобщен отчет за разкроя",
          ["Btn_PrintReport"] = "🖨 Печат на отчет...",
          ["Btn_ExportDxf"] = "Експорт в DXF...",
          ["Btn_ExportPdf"] = "Експорт в PDF...",
          ["Kpi_Utilization"] = "Общо оползотворяване:",
          ["Kpi_SheetsUsed"] = "Използвани листове:",
          ["Kpi_PartsPlaced"] = "Разположени детайли:",
          ["Kpi_Unplaced"] = "Непоместени:",
          ["Report_Title"] = "ОБОБЩЕН ОТЧЕТ ЗА РАЗКРОЯ",
          ["Report_SheetsMapTitle"] = "1. Карта на разкроя по листове",
          ["Report_PartsSpecTitle"] = "2. Спецификация на детайлите",
          ["Report_ColSheetNum"] = "Лист №",
          ["Report_ColSheetFormat"] = "Размер на листа",
          ["Report_ColSheetRepeat"] = "Брой листове",
          ["Report_ColSheetParts"] = "Детайли на листа",
          ["Report_ColSheetUtil"] = "Оползотворяване",
          ["Report_ColPartPos"] = "Поз. №",
          ["Report_ColPartName"] = "Детайл / Файл",
          ["Report_ColPartDim"] = "Габарити на детайла",
          ["Report_ColPartQty"] = "Общо бр.",
          ["Report_ColPartArea"] = "Площ (бр.)",
          ["Report_UnitsPrefix"] = "Единици: ",
          ["Report_DatePrefix"] = "Дата: ",
          ["Report_ProjectPrefix"] = "Проект: ",
          ["Report_UntitledProject"] = "Безименен",
          ["Report_PreviewToolbar_SavePdf"] = "📄 Запази PDF",
          ["Report_PreviewToolbar_Print"] = "🖨️ Печат на отчет",
          ["Report_PreviewToolbar_ZoomFit"] = "⤢ По ширина",
          ["Report_PreviewToolbar_Summary"] = "Производствен отчет за разкрой",
          ["Report_EmptyTitle"] = "Отчетът за разкрой все още не е генериран",
          ["Report_EmptySub"] = "Изпълнете нестинг на детайлите на 3-ти екран, за да прегледате и отпечатате готов производствен отчет със спецификация и карти за рязане.",
          ["Report_PageXofY"] = "Стр. {0} от {1}",
          ["Msg_NestFirstToPrint"] = "Първо изпълнете нестинг и изберете резултат за печат.",
          ["Msg_PrintError"] = "Грешка при печат",

          // Priority levels
          ["Priority_Highest"] = "Най-висок",
          ["Priority_High"] = "Висок",
          ["Priority_Normal"] = "Нормален",
          ["Priority_Low"] = "Нисък",
          ["Priority_Lowest"] = "Най-нисък",

          // Status bar
          ["Status_Utilization"] = "Оползотворяване:",
          ["Status_Sheets"] = "Листове:",
          ["Status_Placed"] = "Разположени:",
          ["Status_Unplaced"] = "Непоместени:",

          // Parts Multiplier
          ["Lbl_Multiplier"] = "Множител (комплекти):",
          ["Btn_Multiply"] = "× Умножи",
          ["Btn_Divide"] = "÷ Раздели",
          ["Tip_Multiplier"] = "Множител на количеството детайли (напр. брой комплекти от изделието)",
          ["Tip_Multiply"] = "Умножи количеството на всички (или избрани) детайли по указания множител",
          ["Tip_Divide"] = "Раздели количеството на всички (или избрани) детайли на указаното число",

          // Sheet Priority
          ["Col_SheetPriority"] = "Приоритет",
          ["Tip_SheetPriority"] = "Приоритет за разходване на листовете: листовете с най-висок приоритет се използват първи (напр. остатъци и изрезки)",

          // Advanced Settings Dialog
          ["Settings_Title"] = "Разширени настройки",
          ["Settings_Group_Nesting"] = "Параметри на разкроя",
          ["Settings_Rotations"] = "Ъгли на завъртане на детайли:",
          ["Settings_Rot_AsDrawn"] = "Както е начертано",
          ["Settings_Rot_0_180"] = "0 / 180°",
          ["Settings_Rot_90"] = "Стъпка 90°",
          ["Settings_Rot_45"] = "Стъпка 45°",
          ["Settings_Rot_Free"] = "Свободен (36 стъпки)",
          ["Settings_Spacing"] = "Разстояние между детайли",
          ["Settings_Margin"] = "Отстъп от ръба на листа",
          ["Settings_Effort"] = "Усилие за търсене:",
          ["Settings_Effort_Fast"] = "Бърз",
          ["Settings_Effort_Normal"] = "Оптимален (по подразбиране)",
          ["Settings_Effort_Best"] = "Дълбок (по-бавен)",
          ["Settings_Concurrency"] = "Паралелни търсения:",
          ["Settings_Concurrency_Auto"] = "Авто ({0})",
          ["Settings_Concurrency_Tip"] = "Колко търсения на разкрой вървят едновременно. По-малко пази бавния компютър отзивчив, повече приключва по-бързо на мощен. Самият разкрой излиза същият.",
          ["Settings_Group_CommonLine"] = "Рязане по обща линия",
          ["Settings_CommonLineGap"] = "Разстояние при рязане по обща линия",
          ["Settings_Reset"] = "Възстанови",
          ["Settings_MaxCluster"] = "Макс. детайли в клъстер:",
          ["Settings_Auto"] = "Авто",
          ["Settings_Manual"] = "Ръчно",
          ["Settings_Group_Dxf"] = "Параметри за експорт на DXF",
          ["Settings_MergeLines"] = "Обединяване на съвпадащи линии на рязане",
          ["Settings_PartNames"] = "Имена на детайлите в DXF:",
          ["Settings_PartNames_None"] = "Без експорт",
          ["Settings_PartNames_Layer9"] = "PAINT — гравиране, заедно с линиите на огъване",
          ["Settings_PartNames_Background"] = "BACKGROUND — само за преглед",
          ["Settings_Group_App"] = "Параметри на приложението",
          ["Settings_Units"] = "Мерни единици:",
          ["Settings_Units_In"] = "Инчове (in)",
          ["Settings_Units_Mm"] = "Милиметри (mm)",
          ["Settings_Autosave"] = "Автоматично запазване на проекта",
          ["Settings_AutosaveTime"] = "Интервал за автозапазване (мин):",

          ["Dxf_EmptyHint"] = "Стартирайте нестинг, за да видите резултата тук.",
          ["Dxf_NavHint"] = "колелце = мащабиране  ·  влачене с колелце = преместване  ·  десен клик = побиране в екрана",
          ["Dxf_EditNavHint"] = "влачете детайл за преместване  ·  Alt+пускане = прилепване  ·  стрелки = преместване  ·  Esc = отказ",
          ["Dxf_EditNest"] = "✋ Ръчна корекция",
          ["Dxf_EditNestTip"] = "Ръчна корекция на разкроя: кликнете детайл за избор, влачете с мишката, завъртайте с бутоните.",
          ["Dxf_Measure"] = "Измерване",
          ["Dxf_MeasureTip"] = "Измерване на разстояния: посочете две точки на чертежа.",
          ["Dxf_Mirror"] = "⇄ Огледално",
          ["Dxf_PrevLayout"] = "Предишен разкрой",
          ["Dxf_NextLayout"] = "Следващ разкрой",
          ["Dxf_UndoTip"] = "Отмени (Ctrl+Z)",
          ["Dxf_RedoTip"] = "Повтори (Ctrl+Y)",
          ["Dxf_RotateCw90Tip"] = "Завърти на 90° по часовниковата стрелка",
          ["Dxf_RotateCcw90Tip"] = "Завърти на 90° обратно на часовниковата стрелка",
          ["Dxf_RotateCw5Tip"] = "Завърти на 5° по часовниковата стрелка",
          ["Dxf_RotateCcw5Tip"] = "Завърти на 5° обратно на часовниковата стрелка",
          ["Dxf_MirrorTip"] = "Огледално обръщане на избрания детайл",
          ["Dxf_CommonLineLocked"] = "детайлите със споделен срез са заключени · стартирайте нестинга отново за промяна",
          ["Btn_ClearRuns"] = "Изчисти списъка",
          ["Tip_ClearRuns"] = "Изчисти списъка с натрупани варианти на разкроя",
          ["Btn_OK"] = "ОК",
          ["Btn_Cancel"] = "Отказ",

          // Sheet dialog
          ["Sheet_Dlg_Title"] = "Нов размер на листа",
          ["Sheet_Dlg_Title_Edit"] = "Редактиране на лист",
          ["Sheet_Dlg_Header"] = "Размери на листа в {0} (ширина = дългата страна).",
          ["Sheet_Dlg_Width"] = "Ширина ({0})",
          ["Sheet_Dlg_Height"] = "Височина ({0})",
          ["Sheet_Dlg_Qty"] = "Количество",
          ["Sheet_Dlg_Priority"] = "Приоритет на използване:",
          ["Sheet_Dlg_Unlimited"] = "Без ограничение",
          ["Sheet_Dlg_Remember"] = "Запази като шаблон",
          ["Sheet_Dlg_Add"] = "Добави",
        },

        [LangEnglish] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          // Menus
          ["Menu_File"] = "_File",
          ["Menu_NewProject"] = "New Project",
          ["Menu_OpenProject"] = "Open Project...",
          ["Menu_RecentProjects"] = "Recent Projects",
          ["Menu_RecentEmpty"] = "(empty)",

          ["Menu_Save"] = "Save",
          ["Menu_SaveAs"] = "Save As...",
          ["Menu_Exit"] = "Exit",
          ["Menu_Settings"] = "_Settings",
          ["Menu_Offcut"] = "Offcut...",
          ["Menu_AdvancedSettings"] = "Advanced Settings...",
          ["Menu_Language"] = "_Language",
          ["Menu_Help"] = "_Help",
          ["Menu_CheckUpdates"] = "Check for Updates...",
          ["Menu_SendFeedback"] = "Send Feedback...",
          ["Menu_About"] = "About",

          // Toolbar Buttons
          ["Btn_New"] = "New",
          ["Btn_Open"] = "Open...",
          ["Btn_Save"] = "Save",

          // Workflow Tabs
          ["Tab_Parts"] = "1. Parts (Models)",
          ["Tab_Sheets"] = "2. Sheets",
          ["Tab_Nesting"] = "3. Nesting",
          ["Tab_Layout"] = "4. Layout & Report",

          // Screen 1: Parts
          ["Btn_AddDxf"] = "+ Add DXF...",

          ["Btn_DeletePart"] = "Delete part",
          ["Btn_ClearAll"] = "Clear all",
          ["Hint_DragDrop"] = "💡 You can drag and drop DXF files directly into this table",
          ["Btn_NextSheets"] = "Next: Sheets ➔",
          ["Parts_CountSummary"] = "Parts: {0} (requested: {1} pcs)",
          ["Col_Preview"] = "Preview",
          ["Col_PartName"] = "Part (Name / File)",
          ["Col_Quantity"] = "Quantity",
          ["Col_Dimensions"] = "Dimensions (W × H)",
          ["Col_NetArea"] = "Area",
          ["Col_Color"] = "Color",
          ["Col_Priority"] = "Priority",
          ["Col_Status"] = "Status",
          ["Col_Actions"] = "Actions",
          ["Btn_EditDetails"] = "⚙ Details...",
          ["Unit_Pcs"] = "pcs",

          // Screen 2: Sheets
          ["Btn_BackToParts"] = "⮜ Back to Parts",
          ["Btn_NextNesting"] = "Next: Nesting ➔",
          ["Btn_AddSheet"] = "+ Add Sheet...",
          ["Btn_DeleteSheet"] = "Delete Sheet",
          ["Lbl_SheetEdgeMargin"] = "Sheet edge margin:",
          ["Sheets_CountSummary"] = "Sheet sizes: {0} (sheets: {1})",
          ["Sheets_UnlimitedNote"] = "unlimited included",
          ["Col_SheetSize"] = "Sheet size (W × H)",
          ["Col_Unlimited"] = "Unlimited",
          ["Col_AvailableUsed"] = "Available / Used",
          ["Chk_Unlimited"] = "Unlimited",
          ["Menu_MySheets"] = "My sheets",
          ["Menu_RemoveMySheets"] = "Remove from my sheets",
          ["Menu_CustomSize"] = "Custom size…",

          // Screen 3: Nesting
          ["Btn_StartNest"] = "▶ Start Nesting",
          ["Btn_StopNest"] = "⏹ Stop",
          ["Lbl_NestingEffort"] = "Nesting effort:",
          ["Effort_Fast"] = "Fast",
          ["Effort_Normal"] = "Normal",
          ["Effort_Best"] = "Best",
          ["Lbl_RunsList"] = "Layout runs:",
          ["Btn_GoToLayout"] = "Go to Layout ➔",
          ["Col_RunNum"] = "#",
          ["Col_RunTime"] = "Time",
          ["Col_RunSheets"] = "Sheets",
          ["Col_RunUtil"] = "Utilization",
          ["Col_RunPlaced"] = "Placed",
          ["Nesting_VariantMetrics"] = "Variant #{0}: Utilization {1} | Placed {2} | Sheets: {3} (time: {4})",

          // Screen 4: Layout & Report
          ["Btn_BackToNesting"] = "⮜ Back to Nesting",
          ["Radio_DxfView"] = "DXF Drawing",
          ["Radio_ReportView"] = "Summary Report",
          ["Btn_PrintReport"] = "🖨 Print Report...",
          ["Btn_ExportDxf"] = "Export to DXF...",
          ["Btn_ExportPdf"] = "Export to PDF...",
          ["Kpi_Utilization"] = "Overall utilization:",
          ["Kpi_SheetsUsed"] = "Sheets consumed:",
          ["Kpi_PartsPlaced"] = "Parts placed:",
          ["Kpi_Unplaced"] = "Unplaced parts:",
          ["Report_Title"] = "NESTING SUMMARY REPORT",
          ["Report_SheetsMapTitle"] = "1. Cutting layout by sheets",
          ["Report_PartsSpecTitle"] = "2. Cut parts specification",
          ["Report_ColSheetNum"] = "Sheet #",
          ["Report_ColSheetFormat"] = "Sheet size",
          ["Report_ColSheetRepeat"] = "Sheet count",
          ["Report_ColSheetParts"] = "Parts on sheet",
          ["Report_ColSheetUtil"] = "Utilization",
          ["Report_ColPartPos"] = "Pos #",
          ["Report_ColPartName"] = "Part / File",
          ["Report_ColPartDim"] = "Part dimensions",
          ["Report_ColPartQty"] = "Total pcs",
          ["Report_ColPartArea"] = "Area (pc)",
          ["Report_UnitsPrefix"] = "Units: ",
          ["Report_DatePrefix"] = "Date: ",
          ["Report_ProjectPrefix"] = "Project: ",
          ["Report_UntitledProject"] = "Untitled",
          ["Report_PreviewToolbar_SavePdf"] = "📄 Save PDF",
          ["Report_PreviewToolbar_Print"] = "🖨️ Print Report",
          ["Report_PreviewToolbar_ZoomFit"] = "⤢ Fit Width",
          ["Report_PreviewToolbar_Summary"] = "Production Nesting Report",
          ["Report_EmptyTitle"] = "Nesting report not generated yet",
          ["Report_EmptySub"] = "Run nesting on screen 3 to view and print the production report with specifications and cutting maps.",
          ["Report_PageXofY"] = "Page {0} of {1}",
          ["Msg_NestFirstToPrint"] = "Please run nesting and select a result to print.",
          ["Msg_PrintError"] = "Print error",

          // Priority levels
          ["Priority_Highest"] = "Highest",
          ["Priority_High"] = "High",
          ["Priority_Normal"] = "Normal",
          ["Priority_Low"] = "Low",
          ["Priority_Lowest"] = "Lowest",

          // Status bar
          ["Status_Utilization"] = "Utilization:",
          ["Status_Sheets"] = "Sheets:",
          ["Status_Placed"] = "Placed:",
          ["Status_Unplaced"] = "Unplaced:",

          // Parts Multiplier
          ["Lbl_Multiplier"] = "Multiplier (kits):",
          ["Btn_Multiply"] = "× Multiply",
          ["Btn_Divide"] = "÷ Divide",
          ["Tip_Multiplier"] = "Part quantity multiplier (e.g. number of assemblies/kits needed)",
          ["Tip_Multiply"] = "Multiply quantity of all (or selected) parts by this factor",
          ["Tip_Divide"] = "Divide quantity of all (or selected) parts by this factor",

          // Sheet Priority
          ["Col_SheetPriority"] = "Priority",
          ["Tip_SheetPriority"] = "Sheet consumption priority: sheets with highest priority are consumed first (e.g. offcuts and remnants)",

          // Advanced Settings Dialog
          ["Settings_Title"] = "Advanced Settings",
          ["Settings_Group_Nesting"] = "Nesting",
          ["Settings_Rotations"] = "Default part rotations:",
          ["Settings_Rot_AsDrawn"] = "As drawn",
          ["Settings_Rot_0_180"] = "0 / 180°",
          ["Settings_Rot_90"] = "90° steps",
          ["Settings_Rot_45"] = "45° steps",
          ["Settings_Rot_Free"] = "Free (36 steps)",
          ["Settings_Spacing"] = "Part spacing",
          ["Settings_Margin"] = "Sheet edge margin",
          ["Settings_Effort"] = "Nesting effort:",
          ["Settings_Effort_Fast"] = "Fast",
          ["Settings_Effort_Normal"] = "Normal (default)",
          ["Settings_Effort_Best"] = "Best (slowest)",
          ["Settings_Concurrency"] = "Parallel searches:",
          ["Settings_Concurrency_Auto"] = "Auto ({0})",
          ["Settings_Concurrency_Tip"] = "How many nesting searches run at the same time. Fewer keeps a slow computer responsive; more finishes sooner on a fast one. The nest itself comes out the same either way.",
          ["Settings_Group_CommonLine"] = "Common Line Cutting",
          ["Settings_CommonLineGap"] = "Common Line Gap",
          ["Settings_Reset"] = "Reset",
          ["Settings_MaxCluster"] = "Maximum cluster parts:",
          ["Settings_Auto"] = "Auto",
          ["Settings_Manual"] = "Manual",
          ["Settings_Group_Dxf"] = "DXF Export",
          ["Settings_MergeLines"] = "Merge coincident cut lines",
          ["Settings_PartNames"] = "Part name text:",
          ["Settings_PartNames_None"] = "None",
          ["Settings_PartNames_Layer9"] = "PAINT — engraved, with the bend lines",
          ["Settings_PartNames_Background"] = "BACKGROUND — display only",
          ["Settings_Group_App"] = "Application",
          ["Settings_Units"] = "Units:",
          ["Settings_Units_In"] = "Inches (in)",
          ["Settings_Units_Mm"] = "Millimeters (mm)",
          ["Settings_Autosave"] = "Auto save the open project",
          ["Settings_AutosaveTime"] = "Auto Save Time (min):",

          ["Dxf_EmptyHint"] = "Run a nest to see the result here.",
          ["Dxf_NavHint"] = "scroll = zoom  ·  wheel-drag = pan  ·  right-click = fit",
          ["Dxf_EditNavHint"] = "drag to move  ·  Alt+drop = slide  ·  arrows = nudge  ·  Esc = drop",
          ["Dxf_EditNest"] = "✋ Edit nest",
          ["Dxf_EditNestTip"] = "Manual nesting: click a part to select it, drag to move, rotate with the buttons.",
          ["Dxf_Measure"] = "Measure",
          ["Dxf_MeasureTip"] = "Measure distances: click two points on the drawing.",
          ["Dxf_Mirror"] = "⇄ Mirror",
          ["Dxf_PrevLayout"] = "Previous layout",
          ["Dxf_NextLayout"] = "Next layout",
          ["Dxf_UndoTip"] = "Undo (Ctrl+Z)",
          ["Dxf_RedoTip"] = "Redo (Ctrl+Y)",
          ["Dxf_RotateCw90Tip"] = "Rotate 90° clockwise",
          ["Dxf_RotateCcw90Tip"] = "Rotate 90° counter-clockwise",
          ["Dxf_RotateCw5Tip"] = "Rotate 5° clockwise",
          ["Dxf_RotateCcw5Tip"] = "Rotate 5° counter-clockwise",
          ["Dxf_MirrorTip"] = "Mirror the selected part",
          ["Dxf_CommonLineLocked"] = "common-line cluster parts are locked  ·  re-nest to change their position",
          ["Btn_ClearRuns"] = "Clear history",
          ["Tip_ClearRuns"] = "Clear the list of accumulated nest run variants",
          ["Btn_OK"] = "OK",
          ["Btn_Cancel"] = "Cancel",

          // Sheet dialog
          ["Sheet_Dlg_Title"] = "Custom sheet size",
          ["Sheet_Dlg_Title_Edit"] = "Edit sheet",
          ["Sheet_Dlg_Header"] = "Sheet size in {0} (width = the long side).",
          ["Sheet_Dlg_Width"] = "Width ({0})",
          ["Sheet_Dlg_Height"] = "Height ({0})",
          ["Sheet_Dlg_Qty"] = "Quantity",
          ["Sheet_Dlg_Priority"] = "Priority:",
          ["Sheet_Dlg_Unlimited"] = "Unlimited",
          ["Sheet_Dlg_Remember"] = "Remember as preset",
          ["Sheet_Dlg_Add"] = "Add",
        },
      };
  }
}
