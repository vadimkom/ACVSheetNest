namespace DeepNestSharp.CiTests
{
  using System.Collections;
  using System.Globalization;
  using System.Linq;
  using System.Resources;
  using DeepNestSharp.Ui;
  using FluentAssertions;
  using Xunit;

  /// <summary>
  /// The translations live in Ui/Strings.resx (English) with Strings.ru/bg/de/fr/es/pt.resx beside it.
  /// Every text has to be in all of them: a key missing from one language would quietly show in English.
  /// </summary>
  [Collection(UiLanguageCollection.Name)]
  public class TranslationsFixture
  {
    private static readonly ResourceManager Strings = new ResourceManager("DeepNestSharp.Ui.Strings", typeof(Localization).Assembly);

    private static string[] Keys(CultureInfo culture)
      => Strings.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
        .Cast<DictionaryEntry>()
        .Select(e => (string)e.Key)
        .OrderBy(k => k)
        .ToArray();

    [Fact]
    public void EveryTextIsInEveryLanguage()
    {
      var english = Keys(CultureInfo.InvariantCulture);
      english.Should().HaveCountGreaterThan(400);
      foreach (string language in Localization.Languages.Where(l => l != Localization.LangEnglish))
      {
        Keys(CultureInfo.GetCultureInfo(language)).Should().Equal(english, "every text has to be in {0}", language);
      }
    }

    [Fact]
    public void EveryTranslationKeepsItsPlaceholders()
    {
      var english = Strings.GetResourceSet(CultureInfo.InvariantCulture, true, false)
        .Cast<DictionaryEntry>()
        .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
      foreach (string language in Localization.Languages.Where(l => l != Localization.LangEnglish))
      {
        foreach (DictionaryEntry entry in Strings.GetResourceSet(CultureInfo.GetCultureInfo(language), true, false)!)
        {
          string key = (string)entry.Key;
          Placeholders((string)entry.Value!).Should().Equal(
            Placeholders(english[key]), "{0} in {1} must fill the same blanks as the English", key, language);
        }
      }
    }

    private static string[] Placeholders(string text)
      => System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(p => p).ToArray();

    [Fact]
    public void EveryLanguageInTheMenuIsKnown()
    {
      foreach (string language in Localization.Languages)
      {
        Localization.NormalizeLanguage(language).Should().Be(language);
        Localization.NormalizeLanguage(language.ToUpperInvariant() + "-XX").Should().Be(language);
      }

      Localization.NormalizeLanguage("zz").Should().Be(Localization.LangRussian);
    }

    [Fact]
    public void EachLanguageReadsFromItsOwnFile()
    {
      using (new UiLanguageScope(Localization.LangRussian))
      {
        Localization.Get("Btn_Close").Should().Be("Закрыть");
      }

      using (new UiLanguageScope(Localization.LangBulgarian))
      {
        Localization.Get("Btn_Close").Should().Be("Затвори");
      }

      using (new UiLanguageScope(Localization.LangGerman))
      {
        Localization.Get("Btn_Close").Should().Be("Schließen");
      }

      using (new UiLanguageScope(Localization.LangFrench))
      {
        Localization.Get("Btn_Close").Should().Be("Fermer");
        Localization.Plural(0, "Rep_SheetOne", "Rep_SheetFew", "Rep_SheetMany").Should().Be("tôle");
      }

      using (new UiLanguageScope(Localization.LangSpanish))
      {
        Localization.Get("Btn_Close").Should().Be("Cerrar");
      }

      using (new UiLanguageScope(Localization.LangPortuguese))
      {
        Localization.Get("Btn_Close").Should().Be("Fechar");
      }

      using (new UiLanguageScope(Localization.LangEnglish))
      {
        Localization.Get("Btn_Close").Should().Be("Close");
        Localization.Get("No_Such_Key").Should().Be("No_Such_Key", "a missing text shows its name, not nothing");
      }
    }
  }
}
