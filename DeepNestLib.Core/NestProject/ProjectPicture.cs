namespace DeepNestLib.NestProject
{
  /// <summary>
  /// A picture the operator added to the job for the report (a photo, a drawing, a screenshot; the user,
  /// 2026-11-23): the image's own bytes (PNG or JPEG) in base64, and a caption. Kept inside the .dnest, so the
  /// job carries it to another PC on a flash drive with nothing beside it.
  /// </summary>
  public sealed class ProjectPicture
  {
    public string Caption { get; set; }

    public string Data { get; set; }
  }
}
