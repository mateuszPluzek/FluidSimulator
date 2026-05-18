using System.Linq.Expressions;
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
        this.CollisionDamping = 0.95f;
        this.Density = 0.0f;
        this.Mass = 1.0f;
    }
    public void UpdateDensity(List<FluidParticle> particles)
    {
        this.Density = Program.CalculateDensity(this.CurrentPosition, particles);
    }
    
    public void UpdatePosition(PipeGeometry pipe, List<FluidParticle> particles, float dt)
    {
        // Gravity
        this.Velocity += new Vector3(0f, -1f, 0f) * Gravity * dt;

        // Pressure based on the density
        Vector3 pressureForce = Program.CalculatePressureForce(this, particles);
        Vector3 pressureAcceleration = pressureForce / this.Mass;
        this.Velocity += pressureAcceleration * dt;
        
        Vector3 viscosityForce = Program.CalculateViscosityForce(this, particles);
        Vector3 viscosityAcceleration = viscosityForce / this.Mass;
        this.Velocity += viscosityAcceleration * dt;
        
        //linear damping
        this.Velocity += -this.Velocity * 2.0f * dt;
        
        this.CurrentPosition += Velocity * dt;
        
        //bounding box
        this.ResolveGenericPipeCollision(pipe);
    }
    
private void ResolveGenericPipeCollision(PipeGeometry pipe)
    {
        Vector3 pos = this.CurrentPosition;
        Vector3 vel = this.Velocity;

        // --- GLOBAL BOUNDS (Universal Entrance, Exit & Absolute Floor) ---
        if (pos.X - this.Radius < pipe.LeftWall) { pos.X = pipe.LeftWall + this.Radius; vel.X = Math.Abs(vel.X) * this.CollisionDamping; }
        if (pos.Y - this.Radius < pipe.Floor) { pos.Y = pipe.Floor + this.Radius; vel.Y = Math.Abs(vel.Y) * this.CollisionDamping; }
        if (pos.Y + this.Radius > pipe.TopEntrance) { pos.Y = pipe.TopEntrance - this.Radius; vel.Y = -Math.Abs(vel.Y) * this.CollisionDamping; }

        // --- SHAPE SPECIFIC WALL LOGIC ---
        if (pipe.Type == PipeType.SharpL)
        {
            if (pos.Y + this.Radius > pipe.InnerHorizontalWall && pos.X + this.Radius > pipe.InnerVerticalWall)
            {
                pos.X = pipe.InnerVerticalWall - this.Radius; vel.X = -Math.Abs(vel.X) * this.CollisionDamping;
            }
            else if (pos.X >= pipe.InnerVerticalWall && pos.Y + this.Radius > pipe.InnerHorizontalWall)
            {
                pos.Y = pipe.InnerHorizontalWall - this.Radius; vel.Y = -Math.Abs(vel.Y) * this.CollisionDamping;
            }
        }
        else if (pipe.Type == PipeType.MiteredL)
        {
            // 45 degree outer chamfer wall constraint
            float outerChamferLine = (pos.X - pipe.LeftWall) + (pos.Y - pipe.Floor);
            if (outerChamferLine < 0.5f) {
                float normalX = 0.7071f; float normalY = 0.7071f;
                float penetration = 0.5f - outerChamferLine;
                pos.X += normalX * (penetration + this.Radius); pos.Y += normalY * (penetration + this.Radius);
                vel = (vel - 2 * Vector3.Dot(vel, new Vector3(normalX, normalY, 0)) * new Vector3(normalX, normalY, 0)) * this.CollisionDamping;
            }
            // Inner vertical/horizontal constraints
            if (pos.Y > pipe.InnerHorizontalWall + 0.3f && pos.X + this.Radius > pipe.InnerVerticalWall) { pos.X = pipe.InnerVerticalWall - this.Radius; vel.X = -Math.Abs(vel.X) * this.CollisionDamping; }
            if (pos.X > pipe.InnerVerticalWall + 0.3f && pos.Y + this.Radius > pipe.InnerHorizontalWall) { pos.Y = pipe.InnerHorizontalWall - this.Radius; vel.Y = -Math.Abs(vel.Y) * this.CollisionDamping; }
            // Inner corner chamfer constraint
            if (pos.X > pipe.InnerVerticalWall && pos.Y > pipe.InnerHorizontalWall) {
                float innerChamferLine = (pos.X - pipe.InnerVerticalWall) + (pos.Y - pipe.InnerHorizontalWall);
                if (innerChamferLine > 0.3f) {
                    pos.X -= 0.01f; pos.Y -= 0.01f; vel *= -this.CollisionDamping;
                }
            }
        }
        else if (pipe.Type == PipeType.CurvedL)
        {
            // Outer bend circle radius collision
            Vector3 outerCenter = new Vector3(pipe.LeftWall + 0.6f, pipe.Floor + 0.6f, 0);
            float outerDist = (pos - outerCenter).Length;
            if (pos.X < outerCenter.X && pos.Y < outerCenter.Y && outerDist > 0.6f) {
                Vector3 normal = (outerCenter - pos).Normalized();
                pos = outerCenter - normal * (0.6f - this.Radius);
                vel = (vel - 2 * Vector3.Dot(vel, normal) * normal) * this.CollisionDamping;
            }
            // Inner bend circle radius collision
            Vector3 innerCenter = new Vector3(pipe.InnerVerticalWall, pipe.InnerHorizontalWall, 0);
            float innerDist = (pos - innerCenter).Length;
            if (pos.X > innerCenter.X && pos.Y > innerCenter.Y && innerDist < 0.05f) {
                Vector3 normal = (pos - innerCenter).Normalized();
                pos = innerCenter + normal * (0.05f + this.Radius);
                vel = (vel - 2 * Vector3.Dot(vel, normal) * normal) * this.CollisionDamping;
            }
            // Straight bounding portions
            if (pos.Y > innerCenter.Y && pos.X + this.Radius > pipe.InnerVerticalWall) { pos.X = pipe.InnerVerticalWall - this.Radius; vel.X = -Math.Abs(vel.X) * this.CollisionDamping; }
            if (pos.X > innerCenter.X && pos.Y + this.Radius > pipe.InnerHorizontalWall) { pos.Y = pipe.InnerHorizontalWall - this.Radius; vel.Y = -Math.Abs(vel.Y) * this.CollisionDamping; }
        }
else if (pipe.Type == PipeType.DiagonalDrop)
        {
            float optOuterRadius = 1.1f;
            // FIXED: Using a shared center for both outer and inner concentric rings
            Vector3 optCenter = new Vector3(pipe.LeftWall + optOuterRadius, pipe.Floor + optOuterRadius, 0f);

            // --- ZONE 1: VERTICAL INTAKE CHUTE ---
            if (pos.Y > optCenter.Y)
            {
                if (pos.X - this.Radius < pipe.LeftWall) { pos.X = pipe.LeftWall + this.Radius; vel.X = Math.Abs(vel.X) * this.CollisionDamping; }
                if (pos.X + this.Radius > pipe.InnerVerticalWall) { pos.X = pipe.InnerVerticalWall - this.Radius; vel.X = -Math.Abs(vel.X) * this.CollisionDamping; }
            }
            // --- ZONE 2: HORIZONTAL OUTLET ---
            else if (pos.X > optCenter.X)
            {
                if (pos.Y - this.Radius < pipe.Floor) { pos.Y = pipe.Floor + this.Radius; vel.Y = Math.Abs(vel.Y) * this.CollisionDamping; }
                if (pos.Y + this.Radius > pipe.InnerHorizontalWall) { pos.Y = pipe.InnerHorizontalWall - this.Radius; vel.Y = -Math.Abs(vel.Y) * this.CollisionDamping; }
            }
            // --- ZONE 3: EXPANED SMOOTH RADIUS BEND CORNER ---
            else
            {
                // Outer Curved Boundary Handling
                float outerDist = (pos - optCenter).Length;
                if (outerDist > optOuterRadius - this.Radius)
                {
                    Vector3 normal = (optCenter - pos).Normalized();
                    pos = optCenter - normal * (optOuterRadius - this.Radius);
                    vel = (vel - 2 * Vector3.Dot(vel, normal) * normal) * this.CollisionDamping;
                }

                // Inner Curved Boundary Handling
                float optInnerRadius = optOuterRadius - pipe.PipeWidth; // 0.5f Radius channel clearance
                float innerDist = (pos - optCenter).Length;
                if (innerDist < optInnerRadius + this.Radius)
                {
                    Vector3 normal = (pos - optCenter).Normalized();
                    pos = optCenter + normal * (optInnerRadius + this.Radius);
                    vel = (vel - 2 * Vector3.Dot(vel, normal) * normal) * this.CollisionDamping;
                }
            }
        }

        this.Velocity = vel;
        this.CurrentPosition = pos;
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
            resolvedPosition.X = bounds.MaxX - this.Radius;
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
            resolvedPosition.Y = bounds.MaxY - this.Radius;
            resolvedVelocity.Y = -Math.Abs(resolvedVelocity.Y) * this.CollisionDamping;
        }

        this.Velocity = resolvedVelocity;
        this.CurrentPosition = resolvedPosition;
    }

}