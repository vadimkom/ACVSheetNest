namespace DeepNestLib.NestProject
{
  using System;

  /// <summary>How the operator flipped a part itself (not its mirrored copies). Both at once is the part
  /// turned half a turn, which is still a flip as far as the file is concerned.</summary>
  [Flags]
  public enum PartFlip
  {
    None = 0,

    /// <summary>Left becomes right: mirrored across a vertical line.</summary>
    LeftRight = 1,

    /// <summary>Top becomes bottom: mirrored across a horizontal line.</summary>
    UpDown = 2,
  }
}
