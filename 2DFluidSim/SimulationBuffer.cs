using OpenTK.Mathematics;

namespace _2DFluidSim;

public class SimulationBuffers
{
    // buffers for collecting
    public Vector3[] RenderPositions;
    public Vector3[] PhysicsPositions;
    
    // Speed values for color
    public float[] RenderSpeeds;
    public float[] PhysicsSpeeds;

    private readonly object _lockObject = new object();

    public SimulationBuffers(int count)
    {
        RenderPositions = new Vector3[count];
        PhysicsPositions = new Vector3[count];
        RenderSpeeds = new float[count];
        PhysicsSpeeds = new float[count];
    }


    public void SwapBuffers()
    {
        lock (_lockObject)
        {
            var tempPos = RenderPositions;
            RenderPositions = PhysicsPositions;
            PhysicsPositions = tempPos;

            var tempSpeed = RenderSpeeds;
            RenderSpeeds = PhysicsSpeeds;
            PhysicsSpeeds = tempSpeed;
        }
    }
    
    public void GetRenderData(Vector3[] targetPositions, float[] targetSpeeds)
    {
        lock (_lockObject)
        {
            Array.Copy(RenderPositions, targetPositions, RenderPositions.Length);
            Array.Copy(RenderSpeeds, targetSpeeds, RenderSpeeds.Length);
        }
    }
}