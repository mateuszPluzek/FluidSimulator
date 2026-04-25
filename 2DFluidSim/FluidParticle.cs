using OpenTK.Mathematics;
using OpenTK.Platform;

namespace _2DFluidSim;

public class FluidParticle
{
    public Vector3 StartPosition { get; set; }
    public Vector3 CurrentPosition { get; set; }

    public float Radius { get; set; }

    public FluidParticle()
    {
        this.StartPosition = new Vector3(0,0,0);
        this.Radius = 0.1f;
        this.CurrentPosition = StartPosition;
    }
    public FluidParticle(Vector3 startPosition, float radius)
    {
        this.StartPosition = startPosition;
        this.Radius = radius;
        this.CurrentPosition = startPosition;
    }
}