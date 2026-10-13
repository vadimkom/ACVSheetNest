namespace DeepNestSharp.CiTests
{
  using System;

  /// <summary>
  /// Engine tests that search at Normal effort or measure rather than check take from one to twenty-six
  /// minutes each, and the whole engine suite took 46 minutes with them. They run on request
  /// (SHEETNEST_SLOW_TESTS=1), before a release that changes how the engine searches; the rest of the engine
  /// suite checks the same rules on Fast in a few minutes.
  /// </summary>
  internal static class SlowTests
  {
    public static bool Enabled => Environment.GetEnvironmentVariable("SHEETNEST_SLOW_TESTS") == "1";
  }
}
