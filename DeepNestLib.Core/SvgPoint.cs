namespace DeepNestLib
{
  using System;
  using System.Text.Json.Serialization;

  public class SvgPoint : IEquatable<SvgPoint>, IPointXY
  {
    public bool Exact
    {
      get;
      set;
    }

      = true;

    public override string ToString()
    {
      return "x: " + X + "; y: " + Y;
    }

    public SvgPoint(double x, double y)
    {
      this.X = x;
      this.Y = y;
    }

    internal SvgPoint(SvgPoint point)
    {
      this.Exact = point.Exact;
      this.Marked = point.Marked;
      this.X = point.X;
      this.Y = point.Y;
    }

    // Almost never set, and written for every point of every part a project saves: left out when false.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Marked { get; set; }

    [JsonConverter(typeof(DoublePrecisionConverter))]
    public double X { get; internal set; }

    double IPointXY.X => X;

    double IPointXY.Y => Y;

    [JsonConverter(typeof(DoublePrecisionConverter))]
    public double Y { get; internal set; }

    public SvgPoint Clone()
    {
      return new SvgPoint(this);
    }

    public override int GetHashCode()
    {
      return HashCode.Combine(Exact, Marked, Math.Round(X, 4), Math.Round(Y, 4));
    }

    public bool Equals(SvgPoint other)
    {
      return this.GetHashCode() == other.GetHashCode();
    }
  }
}