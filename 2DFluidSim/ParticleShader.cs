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
        vec4 blueSlow = vec4(0.113f, 0.454f, 0.870f, 1.0f);
        vec4 redFast = vec4(0.950, 0.200, 0.200, 1.0);

        //mixing of colors based on uSpeed
        fragColor = mix(blueSlow, redFast, clamp(uSpeed, 0.0, 1.0));
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