namespace DeepNestSharp.CiTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;

  /// <summary>
  /// A fixture whose tests make folders under %TEMP%. xUnit disposes the fixture after every test,
  /// and the folders go with it, so test runs do not pile up in the shared temp folder.
  /// </summary>
  public abstract class TempFolderFixture : IDisposable
  {
    private readonly List<string> folders = new List<string>();

    public void Dispose()
    {
      foreach (string folder in this.folders)
      {
        try
        {
          Directory.Delete(folder, true);
        }
        catch (IOException)
        {
          // a file still held open; the folder stays, as before
        }
        catch (UnauthorizedAccessException)
        {
        }
      }

      this.folders.Clear();
      GC.SuppressFinalize(this);
    }

    /// <summary>A new empty folder under %TEMP% named prefix + random, deleted after the test.</summary>
    protected string NewTempFolder(string prefix)
    {
      string folder = Directory.CreateTempSubdirectory(prefix).FullName;
      this.folders.Add(folder);
      return folder;
    }
  }
}
