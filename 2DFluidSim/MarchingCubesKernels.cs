using ILGPU;
using ILGPU.Runtime;
using OpenTK.Mathematics;

namespace _2DFluidSim;

public static class MarchingCubesKernels
{
    // KROK 1: Obliczanie pola gęstości w przestrzeni trójwymiarowej (SDF Grid)
    public static void ComputeScalarFieldKernel(
        Index3D index,
        ArrayView3D<float, Stride3D.DenseXY> scalarField,
        ArrayView<GpuParticle> particles,
        FluidConfig fluidConfig,
        McConfig mcConfig)
    {
        // Oblicz pozycję świata dla tego woksela
        Vector3 voxelPos = mcConfig.GridMin + new Vector3(
            index.X * mcConfig.VoxelSize.X,
            index.Y * mcConfig.VoxelSize.Y,
            index.Z * mcConfig.VoxelSize.Z
        );

        float densitySum = 0.0f;

        // Przeszukujemy cząstki SPH, aby określić gęstość w tym punkcie przestrzeni
        for (int i = 0; i < particles.Length; i++)
        {
            float dst = (particles[i].Position - voxelPos).Length;
            if (dst < fluidConfig.SmoothingRadius)
            {
                float diff = fluidConfig.SmoothingRadius - dst;
                densitySum += particles[i].Mass * (diff * diff * diff) * fluidConfig.DensityKernelVolumeScale;
            }
        }

        scalarField[index] = densitySum;
    }

    // KROK 2: Generowanie trójkątów z wokseli
    public static void MarchCubesKernel(
        Index3D index,
        ArrayView3D<float, Stride3D.DenseXY> scalarField,
        ArrayView<McVertex> outTriangles,
        ArrayView<int> appendCounter,
        ArrayView<int> triTable, // Spłaszczona tabela trójkątów
        McConfig config)
    {
        // Pomijamy krawędzie siatki wokseli
        if (index.X >= config.Resolution.X - 1 || 
            index.Y >= config.Resolution.Y - 1 || 
            index.Z >= config.Resolution.Z - 1) return;

        // Pobieramy gęstości w 8 wierzchołkach kostki
        float[] cubeValues = new float[8];
        Vector3[] cubePositions = new Vector3[8];

// Dokładny, oficjalny układ współrzędnych dla Marching Cubes
        Vector3i[] offsets = new Vector3i[8] {
            new(0,0,0), // 0
            new(1,0,0), // 1
            new(1,0,1), // 2
            new(0,0,1), // 3
            new(0,1,0), // 4
            new(1,1,0), // 5
            new(1,1,1), // 6
            new(0,1,1)  // 7
        };

        for (int i = 0; i < 8; i++)
        {
            Vector3i targetIndex = new Vector3i(index.X + offsets[i].X, index.Y + offsets[i].Y, index.Z + offsets[i].Z);
            cubeValues[i] = scalarField[new Index3D(targetIndex.X, targetIndex.Y, targetIndex.Z)];
            cubePositions[i] = config.GridMin + new Vector3(
                targetIndex.X * config.VoxelSize.X, 
                targetIndex.Y * config.VoxelSize.Y, 
                targetIndex.Z * config.VoxelSize.Z
            );
        }

        // Wyznaczamy indeks kombinacji (0-255)
        int cubeIndex = 0;
        if (cubeValues[0] >= config.IsoLevel) cubeIndex |= 1;
        if (cubeValues[1] >= config.IsoLevel) cubeIndex |= 2;
        if (cubeValues[2] >= config.IsoLevel) cubeIndex |= 4;
        if (cubeValues[3] >= config.IsoLevel) cubeIndex |= 8;
        if (cubeValues[4] >= config.IsoLevel) cubeIndex |= 16;
        if (cubeValues[5] >= config.IsoLevel) cubeIndex |= 32;
        if (cubeValues[6] >= config.IsoLevel) cubeIndex |= 64;
        if (cubeValues[7] >= config.IsoLevel) cubeIndex |= 128;

        if (cubeIndex == 0 || cubeIndex == 255) return;

        // Generowanie wierzchołków na przecięciach krawędzi (Interpolacja liniowa)
// Generowanie wierzchołków na przecięciach 12 krawędzi kostki
        Vector3[] edgeVertices = new Vector3[12];

// Krawędzie dolne (Baza Y = 0)
        edgeVertices[0] = Interpolate(cubePositions[0], cubePositions[1], cubeValues[0], cubeValues[1], config.IsoLevel);
        edgeVertices[1] = Interpolate(cubePositions[1], cubePositions[2], cubeValues[1], cubeValues[2], config.IsoLevel);
        edgeVertices[2] = Interpolate(cubePositions[2], cubePositions[3], cubeValues[2], cubeValues[3], config.IsoLevel);
        edgeVertices[3] = Interpolate(cubePositions[3], cubePositions[0], cubeValues[3], cubeValues[0], config.IsoLevel);

// Krawędzie górne (Baza Y = 1)
        edgeVertices[4] = Interpolate(cubePositions[4], cubePositions[5], cubeValues[4], cubeValues[5], config.IsoLevel);
        edgeVertices[5] = Interpolate(cubePositions[5], cubePositions[6], cubeValues[5], cubeValues[6], config.IsoLevel);
        edgeVertices[6] = Interpolate(cubePositions[6], cubePositions[7], cubeValues[6], cubeValues[7], config.IsoLevel);
        edgeVertices[7] = Interpolate(cubePositions[7], cubePositions[4], cubeValues[7], cubeValues[4], config.IsoLevel);

// Krawędzie pionowe (Łączące dół z górą)
        edgeVertices[8]  = Interpolate(cubePositions[0], cubePositions[4], cubeValues[0], cubeValues[4], config.IsoLevel);
        edgeVertices[9]  = Interpolate(cubePositions[1], cubePositions[5], cubeValues[1], cubeValues[5], config.IsoLevel);
        edgeVertices[10] = Interpolate(cubePositions[2], cubePositions[6], cubeValues[2], cubeValues[6], config.IsoLevel);
        edgeVertices[11] = Interpolate(cubePositions[3], cubePositions[7], cubeValues[3], cubeValues[7], config.IsoLevel);

        // Tworzenie trójkątów na podstawie tablicy look-up
        int tableRowOffset = cubeIndex * 16;
        for (int i = 0; triTable[tableRowOffset + i] != -1 && i < 16; i += 3)
        {
            int globalVertIndex = Atomic.Add(ref appendCounter[0], 3);
            if (globalVertIndex + 2 >= outTriangles.Length) return;

            Vector3 v0 = edgeVertices[triTable[tableRowOffset + i]];
            Vector3 v1 = edgeVertices[triTable[tableRowOffset + i + 1]];
            Vector3 v2 = edgeVertices[triTable[tableRowOffset + i + 2]];

            // ROZWIĄZANIE: Pobieramy idealnie gładką normalną analityczną z otaczającego pola gęstości
            Vector3 normalSmooth = ComputeSmoothNormal(index, scalarField, config);

            // Przypisujemy wygładzoną normalną do każdego wierzchołka trójkąta (Gouraud/Phong Shading)
            outTriangles[globalVertIndex]     = new McVertex(v0, normalSmooth);
            outTriangles[globalVertIndex + 1] = new McVertex(v1, normalSmooth);
            outTriangles[globalVertIndex + 2] = new McVertex(v2, normalSmooth);
        }
    }

    // Pomocnicza metoda inline licząca wartość bezwzględną bezpośrednio na GPU
    private static float GpuAbs(float v)
    {
        return v < 0f ? -v : v;
    }

    private static Vector3 Interpolate(Vector3 p1, Vector3 p2, float val1, float val2, float iso)
    {
        // Jeśli gęstości są skrajnie blisko siebie, zwracamy idealny środek krawędzi, 
        // co gwarantuje taką samą geometrię dla sąsiednich kostek.
        if (GpuAbs(val1 - val2) < 0.00001f) 
        {
            return p1 + 0.5f * (p2 - p1);
        }
    
        // Klasyczna, stabilna interpolacja liniowa
        float t = (iso - val1) / (val2 - val1);
    
        // Zabezpieczenie przed wyjściem wierzchołka poza krawędź kostki (clamping)
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
    
        return p1 + t * (p2 - p1);
    }
    
    private static Vector3 ComputeSmoothNormal(Index3D index, ArrayView3D<float, Stride3D.DenseXY> scalarField, McConfig config)
    {
        // Sprawdzamy bezpieczne granice, aby nie wyjść poza tablicę scalarField
        int x = index.X;
        int y = index.Y;
        int z = index.Z;

        int resX = config.Resolution.X;
        int resY = config.Resolution.Y;
        int resZ = config.Resolution.Z;

        // Próbkowanie centralne gęstości w osiach X, Y, Z
        float nx = (x < resX - 1 ? scalarField[new Index3D(x + 1, y, z)] : scalarField[index]) - 
                   (x > 0 ? scalarField[new Index3D(x - 1, y, z)] : scalarField[index]);

        float ny = (y < resY - 1 ? scalarField[new Index3D(x, y + 1, z)] : scalarField[index]) - 
                   (y > 0 ? scalarField[new Index3D(x, y - 1, z)] : scalarField[index]);

        float nz = (z < resZ - 1 ? scalarField[new Index3D(x, y, z + 1)] : scalarField[index]) - 
                   (z > 0 ? scalarField[new Index3D(x, y, z - 1)] : scalarField[index]);

        // Gradient wskazuje kierunek wzrostu gęstości (do wnętrza płynu),
        // więc odwracamy go, aby normalna wskazywała na zewnątrz powierzchni wody.
        Vector3 grad = new Vector3(-nx, -ny, -nz);
    
        if (grad.LengthSquared > 0.0001f)
            return Vector3.Normalize(grad);
        
        return new Vector3(0, 1, 0); // Domyślna normalna w górę, w razie braku gradientu
    }
}