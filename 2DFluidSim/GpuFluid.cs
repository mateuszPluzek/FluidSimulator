using ILGPU;
using OpenTK.Mathematics;

namespace _2DFluidSim;

// Fluid particle struct for easy data transfer to GPU
public struct GpuParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Density;
    public float Mass;
}
public static class FluidKernels
{
    // Basic helper functions based on the CPU based implementation
    public static float SmoothingKernel(float radius, float dst, float densityKernelVolumeScale)
    {
        if (dst >= radius) return 0f;
        float diff = radius - dst;
        return (diff * diff * diff) * densityKernelVolumeScale;
    }

    public static float SmoothingKernelDerivative(float radius, float dst, float pressureKernelScale)
    {
        if (dst >= radius) return 0f;
        float diff = radius - dst;
        return -(diff * diff) * pressureKernelScale;
    }

    public static float ViscositySmoothingKernel(float radius, float dst, float viscosityKernelVolume)
    {
        if (dst >= radius) return 0f;
        return (radius - dst) / viscosityKernelVolume;
    }

    public static float ConvertDensityToPressure(float density, float targetDensity, float pressureMultiplier)
    {
        float densityError = density - targetDensity;
        return float.Max(0f, densityError) * pressureMultiplier;
    }

    // Density calculation kernel
    public static void ComputeDensityKernel(
        Index1D index, //index of the particle
        ArrayView<GpuParticle> particles, //particles saved on VRAM
        float smoothingRadius,
        float densityKernelVolumeScale)
    {
        GpuParticle p = particles[index]; //getting particle assigned to the current gpu core
        float density = 0.0f;

        // Iteration on every particle
        for (int i = 0; i < particles.Length; i++)
        {
            float dst = (particles[i].Position - p.Position).Length;
            float influence = SmoothingKernel(smoothingRadius, dst, densityKernelVolumeScale);
            density += particles[i].Mass * influence;
        }

        p.Density = density;
        particles[index] = p; // Save the data back to the VRAM
    }

    // Position and physics kernel
    public static void UpdatePositionsKernel(
        Index1D index,
        ArrayView<GpuParticle> particles,
        float minX, float maxX, //simulation variables set on the start of the program
        float minY, float maxY,
        float minZ, float maxZ,
        float smoothingRadius,
        float pressureKernelScale,
        float viscosityKernelVolume,
        float targetDensity,
        float pressureMultiplier,
        float viscosityStrength,
        float dt)
    {
        GpuParticle p = particles[index];
        Vector3 samplePoint = p.Position;
        float currentPressure = ConvertDensityToPressure(p.Density, targetDensity, pressureMultiplier);

        Vector3 pressureForce = Vector3.Zero;
        Vector3 viscosityForce = Vector3.Zero;
        
        for (int i = 0; i < particles.Length; i++)
        {
            if (i == index) continue;

            GpuParticle neighbor = particles[i];
            Vector3 offset = neighbor.Position - samplePoint;
            float dst = offset.Length;

            if (dst >= smoothingRadius || dst == 0.0f) continue;

            Vector3 dir = offset / dst;

            // PressureForce
            float slope = SmoothingKernelDerivative(smoothingRadius, dst, pressureKernelScale);
            float neighborPressure = ConvertDensityToPressure(neighbor.Density, targetDensity, pressureMultiplier);
            float sharedPressure = (currentPressure + neighborPressure) / 2.0f;
            float neighborDensity = neighbor.Density <= 0.001f ? 0.001f : neighbor.Density;

            pressureForce += dir * slope * sharedPressure * neighbor.Mass / neighborDensity;

            // ViscosityForce
            float influence = ViscositySmoothingKernel(smoothingRadius, dst, viscosityKernelVolume);
            viscosityForce += (neighbor.Velocity - p.Velocity) * influence;
        }

        viscosityForce *= viscosityStrength;

        // Applying calculated force to the particle
        p.Velocity += new Vector3(0f, -1f, 0f) * 9.81f * dt; // gravity
        p.Velocity += (pressureForce / p.Mass) * dt;
        p.Velocity += (viscosityForce / p.Mass) * dt;
        p.Velocity += -p.Velocity * 2.0f * dt; // linear damping

        p.Position += p.Velocity * dt;

        // Bounding box collisions
        float radius = 0.05f;
        float damping = 0.75f;

        if (p.Position.X - radius < minX) { p.Position.X = minX + radius; p.Velocity.X = float.Abs(p.Velocity.X) * damping; }
        else if (p.Position.X + radius > maxX) { p.Position.X = maxX - radius; p.Velocity.X = -float.Abs(p.Velocity.X) * damping; }

        if (p.Position.Y - radius < minY) { p.Position.Y = minY + radius; p.Velocity.Y = float.Abs(p.Velocity.Y) * damping; }
        else if (p.Position.Y + radius > maxY) { p.Position.Y = maxY - radius; p.Velocity.Y = -float.Abs(p.Velocity.Y) * damping; }

        if (p.Position.Z - radius < minZ) { p.Position.Z = minZ + radius; p.Velocity.Z = float.Abs(p.Velocity.Z) * damping; }
        else if (p.Position.Z + radius > maxZ) { p.Position.Z = maxZ - radius; p.Velocity.Z = -float.Abs(p.Velocity.Z) * damping; }

        particles[index] = p; // New position saved back to the VRAM
    }
}
