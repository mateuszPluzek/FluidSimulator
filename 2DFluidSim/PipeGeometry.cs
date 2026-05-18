using OpenTK.Mathematics;

namespace _2DFluidSim;

public enum PipeType { SharpL = 1, MiteredL, CurvedL, DiagonalDrop }

public class PipeGeometry
{
    public PipeType Type { get; private set; } = PipeType.SharpL;

    // Boundary Coordinates matching your camera view space
    public float LeftWall => -1.5f;
    public float Floor => -1.5f;
    public float TopEntrance => 1.5f;
    public float PipeWidth => 0.6f;

    // Shortened exit pulling the mouth close to the curve bend
    public float RightExit => 0.5f;

    public float InnerVerticalWall => LeftWall + PipeWidth;   // -0.9f
    public float InnerHorizontalWall => Floor + PipeWidth;    // -0.9f

    public void ChangeType(PipeType newType) => Type = newType;

    public Vector3[] GetVisualVertices()
    {
        List<Vector3> v = new List<Vector3>();

        switch (Type)
        {
            case PipeType.SharpL:
                v.AddRange(new[] {
                    new Vector3(LeftWall, TopEntrance, 0),
                    new Vector3(LeftWall, Floor, 0),
                    new Vector3(RightExit, Floor, 0),
                    new Vector3(RightExit, InnerHorizontalWall, 0),
                    new Vector3(InnerVerticalWall, InnerHorizontalWall, 0),
                    new Vector3(InnerVerticalWall, TopEntrance, 0)
                });
                break;

            case PipeType.MiteredL:
                v.AddRange(new[] {
                    new Vector3(LeftWall, TopEntrance, 0),
                    new Vector3(LeftWall, Floor + 0.5f, 0), 
                    new Vector3(LeftWall + 0.5f, Floor, 0), 
                    new Vector3(RightExit, Floor, 0),
                    new Vector3(RightExit, InnerHorizontalWall, 0),
                    new Vector3(InnerVerticalWall + 0.3f, InnerHorizontalWall, 0),
                    new Vector3(InnerVerticalWall, InnerHorizontalWall + 0.3f, 0), 
                    new Vector3(InnerVerticalWall, TopEntrance, 0)
                });
                break;

            case PipeType.CurvedL:
                v.Add(new Vector3(LeftWall, TopEntrance, 0));
                
                // Outer Curve Bend (Tight 0.6f Radius)
                for (int i = 0; i <= 16; i++) {
                    float angle = MathF.PI + (MathF.PI * 0.5f * i / 16f); 
                    v.Add(new Vector3(LeftWall + 0.6f + 0.6f * MathF.Cos(angle), Floor + 0.6f + 0.6f * MathF.Sin(angle), 0));
                }
                v.Add(new Vector3(RightExit, Floor, 0));
                v.Add(new Vector3(RightExit, InnerHorizontalWall, 0));
                
                // Inner Curve Bend (Tight 0.05f Radius)
                for (int i = 16; i >= 0; i--) {
                    float angle = MathF.PI + (MathF.PI * 0.5f * i / 16f);
                    v.Add(new Vector3(InnerVerticalWall + 0.05f * MathF.Cos(angle), InnerHorizontalWall + 0.05f * MathF.Sin(angle), 0));
                }
                v.Add(new Vector3(InnerVerticalWall, TopEntrance, 0));
                break;

            case PipeType.DiagonalDrop:
                // Profile 4: Optimized Smooth Wide Sweep Corner
                v.Add(new Vector3(LeftWall, TopEntrance, 0));
                
                // Outer Sweeping Arc
                float optOuterRadius = 1.1f;
                Vector3 optCenter = new Vector3(LeftWall + optOuterRadius, Floor + optOuterRadius, 0f);
                
                for (int i = 0; i <= 16; i++) {
                    float angle = MathF.PI + (MathF.PI * 0.5f * i / 16f); 
                    v.Add(new Vector3(optCenter.X + optOuterRadius * MathF.Cos(angle), optCenter.Y + optOuterRadius * MathF.Sin(angle), 0));
                }
                
                v.Add(new Vector3(RightExit, Floor, 0));
                v.Add(new Vector3(RightExit, InnerHorizontalWall, 0));
                
                // Inner Sweeping Arc
                // FIXED: Concentric calculation using the same shared center point
                float optInnerRadius = optOuterRadius - PipeWidth; // 1.1f - 0.6f = 0.5f Constant Clearance
                
                for (int i = 16; i >= 0; i--) {
                    float angle = MathF.PI + (MathF.PI * 0.5f * i / 16f);
                    v.Add(new Vector3(optCenter.X + optInnerRadius * MathF.Cos(angle), optCenter.Y + optInnerRadius * MathF.Sin(angle), 0));
                }
                v.Add(new Vector3(InnerVerticalWall, TopEntrance, 0));
                break;
        }

        v.Add(v[0]);
        return v.ToArray();
    }
}