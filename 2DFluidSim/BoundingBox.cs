using OpenTK.Mathematics;

namespace _2DFluidSim;

public class BoundingBox
{
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    
    public BoundingBox(float minX, float maxX, float minY, float maxY)
    {
        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }
    
    public bool Contains(Vector3 position, float radius)
    {
        return position.X - radius >= MinX && 
               position.X + radius <= MaxX && 
               position.Y - radius >= MinY && 
               position.Y + radius <= MaxY;
    }
    
}