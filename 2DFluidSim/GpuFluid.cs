using ILGPU;
using ILGPU.Runtime;
using OpenTK.Mathematics;

namespace _2DFluidSim;

public struct GpuParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Density;
    public float Mass;
}

public struct FluidConfig
{
    public float MinX; public float MaxX;
    public float MinY; public float MaxY;
    public float MinZ; public float MaxZ;
    public float SmoothingRadius;
    public float DensityKernelVolumeScale;
    public float PressureKernelScale;
    public float ViscosityKernelVolume;
    public float TargetDensity;
    public float PressureMultiplier;
    public float ViscosityStrength;
    public Vector3i GridDimensions;
    public Vector3 GridMin;
}

public static class FluidKernels
{
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

    private static int GetLinearIndex(int x, int y, int z, Vector3i dims)
    {
        return x + y * dims.X + z * dims.X * dims.Y;
    }

    public static void ComputeDensityKernel(
        Index1D index,
        ArrayView<GpuParticle> particles,
        ArrayView<int> gridParticles, 
        ArrayView<int> cellCounts,   
        int cellMaxCapacity,
        FluidConfig config)
    {
        GpuParticle p = particles[index];
        float density = 0.0f;

        int cellX = (int)float.Floor((p.Position.X - config.GridMin.X) / config.SmoothingRadius);
        int cellY = (int)float.Floor((p.Position.Y - config.GridMin.Y) / config.SmoothingRadius);
        int cellZ = (int)float.Floor((p.Position.Z - config.GridMin.Z) / config.SmoothingRadius);

        for (int x = -1; x <= 1; x++)
        {
            int nx = cellX + x;
            if (nx < 0 || nx >= config.GridDimensions.X) continue;

            for (int y = -1; y <= 1; y++)
            {
                int ny = cellY + y;
                if (ny < 0 || ny >= config.GridDimensions.Y) continue;

                for (int z = -1; z <= 1; z++)
                {
                    int nz = cellZ + z;
                    if (nz < 0 || nz >= config.GridDimensions.Z) continue;

                    int cellLinearIndex = GetLinearIndex(nx, ny, nz, config.GridDimensions);
                    int count = cellCounts[cellLinearIndex];
                    
                    for (int i = 0; i < count; i++)
                    {
                        int neighborIndex = gridParticles[cellLinearIndex * cellMaxCapacity + i];
                        float dst = (particles[neighborIndex].Position - p.Position).Length;
                        float influence = SmoothingKernel(config.SmoothingRadius, dst, config.DensityKernelVolumeScale);
                        density += particles[neighborIndex].Mass * influence;
                    }
                }
            }
        }

        p.Density = density;
        particles[index] = p;
    }

    public static void UpdatePositionsKernel(
        Index1D index,
        ArrayView<GpuParticle> particles,
        ArrayView<int> gridParticles,
        ArrayView<int> cellCounts,
        int cellMaxCapacity,
        FluidConfig config,
        float dt)
    {
        GpuParticle p = particles[index];
        Vector3 samplePoint = p.Position;
        float currentPressure = ConvertDensityToPressure(p.Density, config.TargetDensity, config.PressureMultiplier);

        Vector3 pressureForce = Vector3.Zero;
        Vector3 viscosityForce = Vector3.Zero;
        
        int cellX = (int)float.Floor((p.Position.X - config.GridMin.X) / config.SmoothingRadius);
        int cellY = (int)float.Floor((p.Position.Y - config.GridMin.Y) / config.SmoothingRadius);
        int cellZ = (int)float.Floor((p.Position.Z - config.GridMin.Z) / config.SmoothingRadius);
        
        for (int x = -1; x <= 1; x++)
        {
            int nx = cellX + x;
            if (nx < 0 || nx >= config.GridDimensions.X) continue;

            for (int y = -1; y <= 1; y++)
            {
                int ny = cellY + y;
                if (ny < 0 || ny >= config.GridDimensions.Y) continue;

                for (int z = -1; z <= 1; z++)
                {
                    int nz = cellZ + z;
                    if (nz < 0 || nz >= config.GridDimensions.Z) continue;

                    int cellLinearIndex = GetLinearIndex(nx, ny, nz, config.GridDimensions);
                    int count = cellCounts[cellLinearIndex];
                    
                    for (int i = 0; i < count; i++)
                    {
                        int neighborIndex = gridParticles[cellLinearIndex * cellMaxCapacity + i];
                        if (neighborIndex == index) continue;

                        GpuParticle neighbor = particles[neighborIndex];
                        Vector3 offset = neighbor.Position - samplePoint;
                        float dst = offset.Length;

                        if (dst >= config.SmoothingRadius || dst == 0.0f) continue;

                        Vector3 dir = offset / dst;
                        float slope = SmoothingKernelDerivative(config.SmoothingRadius, dst, config.PressureKernelScale);
                        float neighborPressure = ConvertDensityToPressure(neighbor.Density, config.TargetDensity, config.PressureMultiplier);
                        float sharedPressure = (currentPressure + neighborPressure) / 2.0f;
                        float neighborDensity = neighbor.Density <= 0.001f ? 0.001f : neighbor.Density;

                        pressureForce += dir * slope * sharedPressure * neighbor.Mass / neighborDensity;

                        float influence = ViscositySmoothingKernel(config.SmoothingRadius, dst, config.ViscosityKernelVolume);
                        viscosityForce += (neighbor.Velocity - p.Velocity) * influence;
                    }
                }
            }
        }

        viscosityForce *= config.ViscosityStrength;

        p.Velocity += new Vector3(0f, -1f, 0f) * 9.81f * dt;
        p.Velocity += (pressureForce / p.Mass) * dt;
        p.Velocity += (viscosityForce / p.Mass) * dt;
        p.Velocity += -p.Velocity * 2.0f * dt;

        p.Position += p.Velocity * dt;

        float radius = 0.05f;
        float damping = 0.75f;

        if (p.Position.X - radius < config.MinX) { p.Position.X = config.MinX + radius; p.Velocity.X = float.Abs(p.Velocity.X) * damping; }
        else if (p.Position.X + radius > config.MaxX) { p.Position.X = config.MaxX - radius; p.Velocity.X = -float.Abs(p.Velocity.X) * damping; }

        if (p.Position.Y - radius < config.MinY) { p.Position.Y = config.MinY + radius; p.Velocity.Y = float.Abs(p.Velocity.Y) * damping; }
        else if (p.Position.Y + radius > config.MaxY) { p.Position.Y = config.MaxY - radius; p.Velocity.Y = -float.Abs(p.Velocity.Y) * damping; }

        if (p.Position.Z - radius < config.MinZ) { p.Position.Z = config.MinZ + radius; p.Velocity.Z = float.Abs(p.Velocity.Z) * damping; }
        else if (p.Position.Z + radius > config.MaxZ) { p.Position.Z = config.MaxZ - radius; p.Velocity.Z = -float.Abs(p.Velocity.Z) * damping; }

        particles[index] = p;
    }
    // Używamy jawnego Stride3D.Dense, aby dopasować sygnaturę do alokacji z Program.cs
    public static void ClearVolumeKernel(Index3D index, ArrayView3D<float, Stride3D.DenseXY> volume)
    {
        // Teraz kompilator wie, że volume przechowuje konkretnie float, a nie generyczne 'T'
        volume[index] = 0.0f;
    }

    // --- POPRAWIONY KERNEL AKUMULACJI GĘSTOŚCI ---
    public static void PopulateVolumeKernel(
        Index1D index,
        ArrayView<GpuParticle> particles,
        ArrayView3D<float, Stride3D.DenseXY> volume,
        FluidConfig config,
        int resX, int resY, int resZ)
    {
        Vector3 pPos = particles[index].Position;
        
        // Mapowanie pozycji świata do przestrzeni tekstury [0, 1]
        float normX = (pPos.X - config.MinX) / (config.MaxX - config.MinX);
        float normY = (pPos.Y - config.MinY) / (config.MaxY - config.MinY);
        float normZ = (pPos.Z - config.MinZ) / (config.MaxZ - config.MinZ);

        if (normX < 0f || normX > 1f || normY < 0f || normY > 1f || normZ < 0f || normZ > 1f) return;

        int vX = (int)(normX * (resX - 1));
        int vY = (int)(normY * (resY - 1));
        int vZ = (int)(normZ * (resZ - 1));

        // Zabezpieczenie przed wyjściem poza indeksy tablicy (indeksowanie 3D)
        Index3D volumeIndex = new Index3D(vX, vY, vZ);

        // Wyciągamy referencję do konkretnej komórki typu float
        ref float voxelRef = ref volume[volumeIndex];

        // Ponieważ kompilator wie, że voxelRef to 'float', Atomic.Add(ref float, float) dopasuje się idealnie
        Atomic.Add(ref voxelRef, particles[index].Density * 0.05f);
    }
    
    // Reszta Twoich kerneli SPH (ComputeDensityKernel, UpdatePositionsKernel)...
}
