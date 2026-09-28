namespace DeepNestSharp.Domain.Services
{
  using System;
  using System.Globalization;

  /// <summary>
  /// Text the domain shows the operator, in the operator's language. The translations live with the app,
  /// which sets <see cref="Resolve"/> at start-up; until then a key comes back as itself.
  /// </summary>
  public static class UiText
  {
    public static Func<string, string> Resolve { get; set; } = key => key;

    public static string Get(string key) => Resolve(key);

    public static string Format(string key, params object[] args)
      => string.Format(CultureInfo.CurrentCulture, Resolve(key), args);
  }
}
