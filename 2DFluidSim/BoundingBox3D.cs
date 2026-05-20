using OpenTK.Mathematics;

namespace _2DFluidSim;

public class BoundingBox3D
{
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public float MinZ { get; set; }
    public float MaxZ { get; set; }
    
    public BoundingBox3D(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        MinX = minX; MaxX = maxX;
        MinY = minY; MaxY = maxY;
        MinZ = minZ; MaxZ = maxZ;
    }
    
    public bool Contains(Vector3 position, float radius)
    {
        return position.X - radius >= MinX && 
               position.X + radius <= MaxX && 
               position.Y - radius >= MinY && 
               position.Y + radius <= MaxY;
    }
    
}