using System;

namespace r10_bridge.Api
{
  public class BallData
  {
    public double Speed { get; set; }
    public double SpinAxis { get; set; }
    public double TotalSpin { get; set; }
    public double BackSpin { get; set; }
    public double SideSpin { get; set; }
    public double HLA { get; set; }
    public double VLA { get; set; }
    public double CarryDistance { get; set; }
  }

  public class ClubData
  {
    public double Speed { get; set; }
    public double AngleOfAttack { get; set; }
    public double FaceToTarget { get; set; }
    public double Path { get; set; }
    public double SpeedAtImpact { get; set; }
    public double VerticalFaceImpact { get; set; }
    public double HorizontalFaceImpact { get; set; }
    public double ClosureRate { get; set; }
  }
}


