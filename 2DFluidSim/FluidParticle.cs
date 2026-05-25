using System.Linq.Expressions;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace _2DFluidSim;
public class FluidParticle
{
    /*
    // Values for rendering and collision
    public Vector3 StartPosition { get; set; }
    public Vector3 CurrentPosition { get; set; }
    public float Radius { get; set; }
    // Physics values
    public float Gravity { get; set; }
    public Vector3 Velocity { get; set; }
    public float CollisionDamping { get; set; }
    public float Mass { get; set; }
    public float Density { get; set; }

    public FluidParticle()
    {
        this.StartPosition = new Vector3(0,0,0);
        this.Radius = 0.1f;
        Setup();
    }
    public FluidParticle(Vector3 startPosition, float radius)
    {
        this.StartPosition = startPosition;
        this.Radius = radius;
        Setup();
    }

    private void Setup() //function that sets up simulation value
    {
        //simulation values
        this.CurrentPosition = StartPosition;
        this.Gravity = 9.81f;
        this.CollisionDamping = 0.75f;
        this.Density = 0.0f;
        this.Mass = 1.0f;
    }
    public void UpdateDensity(List<FluidParticle> particles)
    {
        this.Density = Program.CalculateDensity(this.CurrentPosition);
    }
    
    public void UpdatePosition(BoundingBox3D boundingBox, List<FluidParticle> particles, float dt)
    {
        // Gravity
        this.Velocity += new Vector3(0f, -1f, 0f) * Gravity * dt;

        // Pressure based on the density
        Vector3 pressureForce = Program.CalculatePressureForce(this);
        Vector3 pressureAcceleration = pressureForce / this.Mass;
        this.Velocity += pressureAcceleration * dt;
        
        Vector3 viscosityForce = Program.CalculateViscosityForce(this);
        Vector3 viscosityAcceleration = viscosityForce / this.Mass;
        this.Velocity += viscosityAcceleration * dt;
        
        //linear damping
        this.Velocity += -this.Velocity * 2.0f * dt;
        
        this.CurrentPosition += Velocity * dt;
        
        //bounding box
        this.ResolveCollision(boundingBox);
    }

    private void ResolveCollision(BoundingBox3D bounds)
    {
        Vector3 resolvedPosition = this.CurrentPosition;
        Vector3 resolvedVelocity = this.Velocity;
        // --- Left / Right (X-Axis) ---
        if (this.CurrentPosition.X - this.Radius < bounds.MinX) // Left collision
        {
            resolvedPosition.X = bounds.MinX + this.Radius;
            resolvedVelocity.X = Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.X + this.Radius > bounds.MaxX) // Right collision
        {
            resolvedPosition.X = bounds.MaxX - this.Radius;
            resolvedVelocity.X = -Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }
        // --- Top / Bottom (Y-Axis) ---
        if (this.CurrentPosition.Y - this.Radius < bounds.MinY) // Bottom collision
        {
            resolvedPosition.Y = bounds.MinY + this.Radius;
            resolvedVelocity.Y = Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.Y + this.Radius > bounds.MaxY) // Top collision
        {
            resolvedPosition.Y = bounds.MaxY - this.Radius;
            resolvedVelocity.Y = -Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }
        // --- Front / Back (Z-Axis) ---
        if (this.CurrentPosition.Z - this.Radius < bounds.MinZ) // Back collision
        {
            resolvedPosition.Z = bounds.MinZ + this.Radius;
            resolvedVelocity.Z = Math.Abs(resolvedVelocity.Z) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.Z + this.Radius > bounds.MaxZ) // Front collision
        {
            resolvedPosition.Z = bounds.MaxZ - this.Radius;
            resolvedVelocity.Z = -Math.Abs(resolvedVelocity.Z) * this.CollisionDamping;
        }
        
        this.Velocity = resolvedVelocity;
        this.CurrentPosition = resolvedPosition;
    }
*/
}