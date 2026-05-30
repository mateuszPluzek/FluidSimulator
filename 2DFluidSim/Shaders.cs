using OpenTK.Graphics.OpenGL;

namespace _2DFluidSim;

public class ParticleShader
{
    public int Id { get; private set; }

    private string vertexShaderSource = @"
     #version 330 core

     layout (location = 0) in vec3 vPosition;       // Lokalny wierzchołek sfery
     layout (location = 1) in vec3 aInstancePos;   // Mapowane bezpośrednio z GpuParticle.Position
     layout (location = 2) in vec3 aInstanceVel;   // Mapowane bezpośrednio z GpuParticle.Velocity

     uniform mat4 projection;
     uniform mat4 view;
     float particleRadius = 0.04;

     out vec3 fNormal;
     out vec3 fFragPos;
     out float fSpeed;

     void main() 
     {
        fSpeed = length(aInstanceVel); // Prędkość obliczana natychmiast na GPU
        fNormal = normalize(vPosition);
        
        vec3 worldPos = (vPosition * particleRadius) + aInstancePos;
        fFragPos = worldPos;
        
        // POPRAWIONA KOLEJNOŚĆ MNOŻENIA MACIERZY (od lewej do prawej)
        gl_Position = projection * view * vec4(worldPos, 1.0);
     }";

    private string fragmentShaderSource = @"
    #version 330 core
    
    in vec3 fNormal;
    in vec3 fFragPos;
    in float fSpeed;

    out vec4 fragColor;

    void main()
    {
        vec3 slowColor = vec3(0.171, 0.95, 0.21);
        vec3 fastColor = vec3(1.0, 0.3, 0.3); 
        
        float maxExpectedSpeed = 4.0;
        float normSpeed = clamp(fSpeed / maxExpectedSpeed, 0.0, 1.0);
        vec3 baseColor = mix(slowColor, fastColor, normSpeed);

        vec3 lightPos = vec3(5.0, 15.0, 8.0);
        vec3 norm = normalize(fNormal);
        vec3 lightDir = normalize(lightPos - fFragPos);
        
        float ambient = 0.3;
        float diffuse = max(dot(norm, lightDir), 0.0) * 0.7;

        fragColor = vec4((ambient + diffuse) * baseColor, 1.0);
    }";

    public void Setup()
    {
        int vertexHandle = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexHandle, vertexShaderSource); 
        GL.CompileShader(vertexHandle);
        
        int fragmentHandle = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentHandle, fragmentShaderSource);
        GL.CompileShader(fragmentHandle);
        
        Id = GL.CreateProgram();
        GL.AttachShader(Id, vertexHandle);
        GL.AttachShader(Id, fragmentHandle);
        GL.LinkProgram(Id);
        
        GL.DeleteShader(vertexHandle);
        GL.DeleteShader(fragmentHandle);
    }

    public void Use() => GL.UseProgram(Id);
}

public class BoundShader
{
    public int Id { get; private set; }

    private string vertexShaderSource = @"
     #version 330 core

     layout (location = 0) in vec3 vPosition;
     uniform mat4 projection;
     uniform mat4 view;
     uniform mat4 model;

     void main() 
     {
        gl_Position = projection * view * model * vec4(vPosition, 1.0);
     }";

    private string fragmentShaderSource = @"
    #version 330 core
    out vec4 fragColor;
    void main() { fragColor = vec4(0.8, 0.8, 0.9, 1.0); }";

    public void Setup()
    {
        int vertexHandle = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexHandle, vertexShaderSource); 
        GL.CompileShader(vertexHandle);
        int fragmentHandle = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentHandle, fragmentShaderSource);
        GL.CompileShader(fragmentHandle);
        
        Id = GL.CreateProgram();
        GL.AttachShader(Id, vertexHandle);
        GL.AttachShader(Id, fragmentHandle);
        GL.LinkProgram(Id);
        
        GL.DeleteShader(vertexHandle);
        GL.DeleteShader(fragmentHandle);
    }

    public void Use() => GL.UseProgram(Id);
}