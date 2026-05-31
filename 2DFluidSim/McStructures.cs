using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace _2DFluidSim;

[StructLayout(LayoutKind.Sequential)]
public struct McVertex
{
    public Vector3 Position;
    public Vector3 Normal;

    public McVertex(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
    }
}

public struct McConfig
{
    public Vector3 GridMin;
    public Vector3 GridMax;
    public Vector3i Resolution; // Np. 64, 64, 64
    public float IsoLevel;      // Próg gęstości, np. 5.0f
    public Vector3 VoxelSize;
}