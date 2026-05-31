using OpenTK.Mathematics;

namespace _2DFluidSim;

public class Camera
{
    private Vector3 _position;
    private float _yaw;
    private float _pitch;

    private Vector3 _front = -Vector3.UnitZ;
    private Vector3 _up = Vector3.UnitY;
    private Vector3 _right = Vector3.UnitX;
    
    public Matrix4 Projection { get; set; }
    public Matrix4 View { get; set; }
    
    // Dodana właściwość Position
    public Vector3 Position => _position;

    private float _near = 0.01f;
    private float _far = 1000.0f;

    public Camera(float aspectRatio)
    {
        _position = new Vector3(0f, 0.5f, 7.0f);
        _yaw = 0f;
        _pitch = 0f;
        
        this.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90f), aspectRatio, _near, _far);

        Update();
    }

    private void Update()
    {
        Vector3 forward;
        forward.X = MathF.Sin(_yaw) * MathF.Cos(_pitch);
        forward.Y = MathF.Sin(_pitch);
        forward.Z = -MathF.Cos(_yaw) * MathF.Cos(_pitch);
        
        _front = Vector3.Normalize(forward);
        _right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, _front));
        _up = Vector3.Normalize(Vector3.Cross(_front, _right));
        
        this.View = Matrix4.LookAt(_position, _position + _front, _up);
    }
    
    public void Look(Vector2 delta)
    {
        _yaw -= delta.X;
        _pitch -= delta.Y;

        _yaw = MathHelper.NormalizeRadians(_yaw);
        _pitch = float.Clamp(_pitch, -1.5f, 1.5f);

        Update();
    }
    
    public void Move(Vector3 move)
    {
        Vector3 direction = (move.X * _right) + (move.Y * _up) + (move.Z * _front);

        _position += direction;

        Update();
    }
}