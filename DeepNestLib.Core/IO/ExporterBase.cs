namespace DeepNestLib.IO
{
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using DeepNestLib.Placement;

  public abstract class ExporterBase : IExport
  {
    public abstract string SaveFileDialogFilter { get; }

    public virtual async Task Export(string path, ISheetPlacement sheetPlacement, bool doMergeLines, IReadOnlyCollection<OffcutLine> offcutLines = null)
    {
      await Export(
        path,
        sheetPlacement.PolygonsForExport,
        new ISheet[] { sheetPlacement.Sheet, },
        doMergeLines,
        offcutLines).ConfigureAwait(false);
    }

    protected abstract Task Export(string path, IEnumerable<INfp> polygons, IEnumerable<ISheet> sheets, bool doMergeLines, IReadOnlyCollection<OffcutLine> offcutLines = null);
  }
}
