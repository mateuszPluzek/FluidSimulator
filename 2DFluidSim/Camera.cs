using OpenTK.Mathematics;

namespace _2DFluidSim;

public class Camera
{
    private Vector3 position;
    private float yaw;
    private float pitch;

    public Matrix4 Projection {set; get;}
    public Matrix4 Transform {set; get;}
    public Matrix4 View {set; get;}

    private float near = 0.01f;
    private float far = 1000.0f;

    public Camera(float aspectRatio)
    {
        position = new Vector3(0f, 0.5f, 1.5f);
        yaw = 0f;
        pitch = 0f;
        
        this.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90f), aspectRatio, near, far);

        Update();
    }

    private void Update()
    {
        Matrix3 rotation =
            Matrix3.CreateRotationX(pitch) *
            Matrix3.CreateRotationY(yaw);
        
        Matrix4 tempTransform = new Matrix4(rotation);
        tempTransform.Row3 = new Vector4(position, 1);

        this.Transform = tempTransform;

        this.View = this.Transform.Inverted();
    }
    
    public void Look(Vector2 delta)
    {
        yaw -= delta.X;
        pitch -= delta.Y;

        Update();
    }
    
    public void Move(Vector3 move)
    {
        position -= move * 0.05f;
        Update();
    }
    
}