using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace _2DFluidSim;

public class ParticleShader
{
    public int Id {get; private set;}

     private string vertexShaderSource = @"
     #version 330

     in vec3 vPosition;
     uniform mat4 projection;
     uniform mat4 view;
     uniform mat4 model;

     void main() 
     {
        gl_Position = vec4(vPosition, 1.0) * model * view * projection;
     }";

    private string fragmentShaderSource = @"
    #version 330
    
    out vec4 fragColor;
    uniform float uSpeed;

    void main()
    {
        //base colors
        vec4 slow = vec4(0.171f, 0.95f, 0.21f, 1.0f);
        vec4 fast = vec4(1.0f, 0.03f, 0.03f, 1.0);

        //mixing of colors based on uSpeed
        fragColor = mix(slow, fast, clamp(uSpeed, 0.0, 1.0));
    }";


    public void Setup()
    {
        //seting up shaders
        int vertexHandle = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexHandle, vertexShaderSource); 
        GL.CompileShader(vertexHandle);
        int fragmentHandle = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentHandle, fragmentShaderSource);
        GL.CompileShader(fragmentHandle);
        
        // linking compiled shaders
        int shaderHandle = GL.CreateProgram();
        GL.AttachShader(shaderHandle, vertexHandle);
        GL.AttachShader(shaderHandle, fragmentHandle);
        GL.LinkProgram(shaderHandle);
        // detaching unused shaders
        GL.DetachShader(shaderHandle, vertexHandle);
        GL.DetachShader(shaderHandle, fragmentHandle);
        GL.DeleteShader(vertexHandle);
        GL.DeleteShader(fragmentHandle);
        
        Id = shaderHandle;
    }

    public void Use()
    {
        GL.UseProgram(Id);
    }
    
}