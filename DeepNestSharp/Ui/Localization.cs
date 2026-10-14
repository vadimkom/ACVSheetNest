namespace DeepNestSharp.Ui
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Globalization;

  /// <summary>
  /// Multi-language localization support for SheetNest (Russian, Bulgarian, English).
  /// </summary>
  public static class Localization
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


    /// <summary>
    /// The translations: <c>Ui/Strings.resx</c> (English, the neutral language) with <c>Strings.ru.resx</c>
    /// and <c>Strings.bg.resx</c> beside it. They used to be dictionaries in this file; as resource files they
    /// open in any translation editor (Visual Studio, ResX Resource Manager, Weblate, Crowdin), and another
    /// language is another file, with no code to touch. A key missing from a language falls back to English.
    /// </summary>
    private static readonly System.Resources.ResourceManager Strings =
      new System.Resources.ResourceManager("DeepNestSharp.Ui.Strings", typeof(Localization).Assembly) { IgnoreCase = true };

    /// <summary>The language codes the resources are looked up under.</summary>
    private static CultureInfo CultureOf(string language) => language switch
    {
      LangBulgarian => CultureInfo.GetCultureInfo("bg"),
      LangEnglish => CultureInfo.InvariantCulture,
      _ => CultureInfo.GetCultureInfo("ru"),
    };

    /// <summary>The text for <paramref name="key"/> in the current language, or the key itself when there is
    /// none at all, so a missing string shows as its name rather than as nothing.</summary>
    public static string Get(string key)
    {
      if (string.IsNullOrEmpty(key))
      {
        return key;
      }

      return Strings.GetString(key, CultureOf(currentLanguage)) ?? key;
    }

    /// <summary>The form of a counted word that goes with <paramref name="n"/>: Russian has three (1 лист,
    /// 2 листа, 5 листов, with 11-14 taking the last), Bulgarian and English two.</summary>
    public static string Plural(long n, string oneKey, string fewKey, string manyKey)
    {
      long abs = Math.Abs(n);
      if (currentLanguage == LangRussian)
      {
        long mod10 = abs % 10, mod100 = abs % 100;
        if (mod10 == 1 && mod100 != 11)
        {
          return Get(oneKey);
        }

        return mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14) ? Get(fewKey) : Get(manyKey);
      }

      return abs == 1 ? Get(oneKey) : Get(fewKey);
    }

    /// <summary>The text for <paramref name="key"/> with its {0}, {1}... filled in.</summary>
    public static string Format(string key, params object[] args)
      => string.Format(CultureInfo.CurrentCulture, Get(key), args);
  }
}
