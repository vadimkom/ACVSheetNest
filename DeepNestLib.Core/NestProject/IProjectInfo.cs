namespace DeepNestLib.NestProject
{
  public interface IProjectInfo
  {
    ISvgNestConfig Config { get; }

    /// <summary>
    /// Gets or sets this document's canonical common-line gap in inches. The application config is
    /// process-wide, so geometry-affecting project values must also have a per-document snapshot.
    /// </summary>
    double CommonLineGapInches { get; set; }

    /// <summary>
    /// Gets or sets this document's common-line cluster limit. Zero selects automatic sizing;
    /// manual limits are restricted to 2..500 parts.
    /// </summary>
    int CommonLineMaxClusterParts { get; set; }

    IList<IDetailLoadInfo, DetailLoadInfo> DetailLoadInfos { get; }

    IList<ISheetLoadInfo, SheetLoadInfo> SheetLoadInfos { get; }

    /// <summary>
    /// Gets or sets the nest result that was on screen when the project was saved (NestResult json,
    /// the .dnr format), so reopening the project restores the nest. Null/empty = no result saved.
    /// </summary>
    string LastNestResultJson { get; set; }

    /// <summary>
    /// Gets or sets whether the sheet quantities are the stock as entered. Up to 1.1.50 a nest took its
    /// sheets off the quantities and a project was saved that way; such a project (false) gets them back once
    /// when it opens. From 1.1.51 a nest never changes the quantities.
    /// </summary>
    bool SheetsKeptWhole { get; set; }

    /// <summary>Gets or sets the material the job is cut from, a <see cref="SheetMaterials"/> key; null when not chosen.</summary>
    string Material { get; set; }

    /// <summary>Gets or sets the sheet thickness in millimetres; 0 when not chosen.</summary>
    double Thickness { get; set; }

    void Load(ISvgNestConfig config, string filePath);

    void Load(ProjectInfo source);

    string ToJson();
  }
}
