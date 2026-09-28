namespace DeepNestSharp.CiTests
{
  using System;
  using DeepNestSharp.Ui;
  using Xunit;

  /// <summary>
  /// The UI language is one static setting for the whole process, so the fixtures that assert on
  /// translated text run in this collection, one at a time, each under the language it asserts in.
  /// </summary>
  [CollectionDefinition(Name)]
  public class UiLanguageCollection
  {
    public const string Name = "UI language";
  }

  /// <summary>Sets the UI language for the life of a test and puts the previous one back.</summary>
  public sealed class UiLanguageScope : IDisposable
  {
    private readonly string previous = Localization.CurrentLanguage;

    public UiLanguageScope(string language)
    {
      Localization.CurrentLanguage = language;
    }

    public void Dispose()
    {
      Localization.CurrentLanguage = this.previous;
    }
  }
}
