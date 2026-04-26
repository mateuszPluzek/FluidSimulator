using OpenTK.Mathematics;
using OpenTK.Platform;

namespace _2DFluidSim;

public class FluidParticle
{
    // Values for rendering and collision
    public Vector3 StartPosition { get; set; }
    public Vector3 CurrentPosition { get; set; }
    public float Radius { get; set; }
    // Physics values
    public float Gravity { get; set; }
    public Vector3 Velocity { get; set; }
    public float CollisionDamping { get; set; }

    public FluidParticle()
    {
        this.StartPosition = new Vector3(0,0,0);
        this.Radius = 0.1f;
        this.CurrentPosition = StartPosition;
        Setup();
    }
    public FluidParticle(Vector3 startPosition, float radius)
    {
        this.StartPosition = startPosition;
        this.Radius = radius;
        this.CurrentPosition = startPosition;
        Setup();
    }

    private void Setup() //function that sets up simulation value
    {
        //simulation values
        this.Gravity = 0.01f;
        this.CollisionDamping = 0.7f;
    }

    public void Update(BoundingBox boundingBox)
    {
        this.Velocity += new Vector3(0f, -1f, 0f) * Gravity;
        this.CurrentPosition += Velocity;
        this.ResolveCollision(boundingBox);
    }

    private void ResolveCollision(BoundingBox bounds)
    {
        Vector3 resolvedPosition = this.CurrentPosition;
        Vector3 resolvedVelocity = this.Velocity;
        //Left Right
        if (this.CurrentPosition.X - this.Radius < bounds.MinX) //Left collision
        {
            resolvedPosition.X = bounds.MinX + this.Radius;
            resolvedVelocity.X = Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.X + this.Radius > bounds.MaxX) //Right collision
        {
            resolvedPosition.X = bounds.MaxX + this.Radius;
            resolvedVelocity.X = -Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }
        //Top bottom
        if (this.CurrentPosition.Y - this.Radius < bounds.MinY) //Bottom collision
        {
            resolvedPosition.Y = bounds.MinY + this.Radius;
            resolvedVelocity.Y = Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.Y + this.Radius > bounds.MaxY) //Top collision
        {
            resolvedPosition.Y = bounds.MaxY + this.Radius;
            resolvedVelocity.Y = -Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }
        
        this.Velocity = resolvedVelocity;
        this.CurrentPosition = resolvedPosition;
    }
}