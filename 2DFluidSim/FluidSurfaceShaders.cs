﻿using OpenTK.Graphics.OpenGL;

namespace _2DFluidSim;

public class FluidSurfaceShader
{
    public int Id { get; private set; }

    private string vertexShaderSource = @"
     #version 330 core
     layout (location = 0) in vec3 aPos;
     layout (location = 1) in vec3 aNormal;

     uniform mat4 projection;
     uniform mat4 view;

     out vec3 fNormal;
     out vec3 fFragPos;

     void main() {
        fNormal = aNormal;
        fFragPos = aPos;
        gl_Position = projection * view * vec4(aPos, 1.0);
     }";

    private string fragmentShaderSource = @"
    #version 330 core
    in vec3 fNormal;
    in vec3 fFragPos;
    out vec4 fragColor;

    void main() {
        vec3 fluidColor = vec3(0.0, 0.4, 0.8); // Głęboki niebieski
        vec3 lightPos = vec3(2.0, 10.0, 5.0);
        vec3 viewPos = vec3(0.0, 0.0, 5.0); // Uproszczona pozycja kamery

        // Ambient
        float ambient = 0.2;

        // Diffuse
        vec3 norm = normalize(fNormal);
        vec3 lightDir = normalize(lightPos - fFragPos);
        float diffuse = max(dot(norm, lightDir), 0.0) * 0.6;

        // Specular (Blinn-Phong) - refleksy świetlne na wodzie
        vec3 viewDir = normalize(viewPos - fFragPos);
        vec3 halfwayDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(norm, halfwayDir), 0.0), 64.0) * 0.9; 

        vec3 result = (ambient + diffuse) * fluidColor + vec3(spec);
        fragColor = vec4(result, 0.85); // Delikatna przezroczystość
    }";

    public void Setup()
    {
        int vertexHandle = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexHandle, vertexShaderSource); GL.CompileShader(vertexHandle);
        int fragmentHandle = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentHandle, fragmentShaderSource); GL.CompileShader(fragmentHandle);
        
        Id = GL.CreateProgram();
        GL.AttachShader(Id, vertexHandle); GL.AttachShader(Id, fragmentHandle);
        GL.LinkProgram(Id);
    }
    public void Use() => GL.UseProgram(Id);
}