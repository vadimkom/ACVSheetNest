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
  /// The translations live in Ui/Strings.resx (English) with Strings.ru.resx and Strings.bg.resx beside it.
  /// Every text has to be in all three: a key missing from one language would quietly show in English.
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
      Keys(CultureInfo.GetCultureInfo("ru")).Should().Equal(english);
      Keys(CultureInfo.GetCultureInfo("bg")).Should().Equal(english);
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

      using (new UiLanguageScope(Localization.LangEnglish))
      {
        Localization.Get("Btn_Close").Should().Be("Close");
        Localization.Get("No_Such_Key").Should().Be("No_Such_Key", "a missing text shows its name, not nothing");
      }
    }
  }
}
