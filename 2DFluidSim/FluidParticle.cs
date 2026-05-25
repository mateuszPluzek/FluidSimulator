using OpenTK.Mathematics;
namespace _2DFluidSim;

public class FluidParticle
{
    public Vector3 StartPosition { get; set; }
    public Vector3 CurrentPosition { get; set; }
    public float Radius { get; set; }
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

    private void Setup()
    {
        this.CurrentPosition = StartPosition;
        this.Gravity = 9.81f;
        this.CollisionDamping = 0.95f;
        this.Density = 0.0f;
        this.Mass = 1.0f;
    }

    public void UpdateDensity(List<FluidParticle> particles)
    {
        this.Density = Program.CalculateDensity(this.CurrentPosition, particles);
    }
    
    public void UpdatePosition(BoundingBox boundingBox, BoundingBox pillar, List<FluidParticle> particles, float dt)
    {
        // Gravity
        this.Velocity += new Vector3(0f, -1f, 0f) * Gravity * dt;

        // Forces
        Vector3 pressureForce = Program.CalculatePressureForce(this, particles);
        Vector3 pressureAcceleration = pressureForce / this.Mass;
        this.Velocity += pressureAcceleration * dt;
        
        Vector3 viscosityForce = Program.CalculateViscosityForce(this, particles);
        Vector3 viscosityAcceleration = viscosityForce / this.Mass;
        this.Velocity += viscosityAcceleration * dt;
        
        // Linear damping
        this.Velocity += -this.Velocity * 2f * dt;
        this.CurrentPosition += Velocity * dt;
        
        // Collisions
        this.ResolveCollision(boundingBox, pillar);
    }

    private void ResolveCollision(BoundingBox bounds, BoundingBox pillar)
    {
        Vector3 resolvedPosition = this.CurrentPosition;
        Vector3 resolvedVelocity = this.Velocity;

        // Outer Bounding Box Collisions (Push Inward)
        if (this.CurrentPosition.X - this.Radius < bounds.MinX)
        {
            resolvedPosition.X = bounds.MinX + this.Radius;
            resolvedVelocity.X = Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.X + this.Radius > bounds.MaxX)
        {
            resolvedPosition.X = bounds.MaxX - this.Radius;
            resolvedVelocity.X = -Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
        }

        if (this.CurrentPosition.Y - this.Radius < bounds.MinY)
        {
            resolvedPosition.Y = bounds.MinY + this.Radius;
            resolvedVelocity.Y = Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }
        else if (this.CurrentPosition.Y + this.Radius > bounds.MaxY)
        {
            resolvedPosition.Y = bounds.MaxY - this.Radius;
            resolvedVelocity.Y = -Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }

        // Inner Pillar Obstacle Collisions (Push Outward)
        if (resolvedPosition.X + this.Radius > pillar.MinX && resolvedPosition.X - this.Radius < pillar.MaxX &&
            resolvedPosition.Y + this.Radius > pillar.MinY && resolvedPosition.Y - this.Radius < pillar.MaxY)
        {
            // Determine shortest penetration depth to push the particle out cleanly
            float overlapLeft = (resolvedPosition.X + this.Radius) - pillar.MinX;
            float overlapRight = pillar.MaxX - (resolvedPosition.X - this.Radius);
            float overlapBottom = (resolvedPosition.Y + this.Radius) - pillar.MinY;
            float overlapTop = pillar.MaxY - (resolvedPosition.Y - this.Radius);

            float minOverlap = Math.Min(Math.Min(overlapLeft, overlapRight), Math.Min(overlapBottom, overlapTop));

            if (minOverlap == overlapLeft)
            {
                resolvedPosition.X = pillar.MinX - this.Radius;
                resolvedVelocity.X = -Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
            }
            else if (minOverlap == overlapRight)
            {
                resolvedPosition.X = pillar.MaxX + this.Radius;
                resolvedVelocity.X = Math.Abs(resolvedVelocity.X) * this.CollisionDamping;
            }
            else if (minOverlap == overlapBottom)
            {
                resolvedPosition.Y = pillar.MinY - this.Radius;
                resolvedVelocity.Y = -Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
            }
            else if (minOverlap == overlapTop)
            {
                resolvedPosition.Y = pillar.MaxY + this.Radius;
                resolvedVelocity.Y = Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
            }
        }
        
        this.Velocity = resolvedVelocity;
        this.CurrentPosition = resolvedPosition;
    }
}