using OpenTK.Mathematics;
using OpenTK.Platform;
using OpenTK.Graphics.OpenGL;
using System.Diagnostics;
using OpenTK.Windowing.Common;
using MouseMoveEventArgs = OpenTK.Platform.MouseMoveEventArgs;

namespace _2DFluidSim;

class Program
{
    private static int screenHeight = 720;
    private static int screenWidth = 1280;
    private static int particleAmount = 300;
    
    private static float smoothingRadius = 0.4f;
    
    public static float targetDensity = 80.0f;
    public static float pressureMultiplier = 5.0f;
    public static float viscosityStrenght = 0.09f;
    static void Main()
    {
        // --- OpenGL Setup ---
        //Toolkit setup
        ToolkitOptions tkOptions = new ToolkitOptions();
        Toolkit.Init(tkOptions);
        //OpenGL API
        OpenGLGraphicsApiHints apiHints = new OpenGLGraphicsApiHints();
        WindowHandle window = Toolkit.Window.Create(apiHints);
        OpenGLContextHandle context = Toolkit.OpenGL.CreateFromWindow(window);
        //Binding context to the Window
        Toolkit.OpenGL.SetCurrentContext(context);
        OpenTK.Graphics.GLLoader.LoadBindings(Toolkit.OpenGL.GetBindingsContext(context));
        //event queue
        void HandleEvents(PalHandle? handle, PlatformEventType type, EventArgs args)
        {
            switch (args)
            {
                case CloseEventArgs closeEvent:
                    Toolkit.Window.Destroy(window);
                    break;
            }
        }
        EventQueue.EventRaised += HandleEvents;
        
        //window options
        Toolkit.Window.SetMode(window, WindowMode.Normal); //Setting window mode to normal
        Toolkit.Window.SetSize(window, new Vector2i(screenWidth,screenHeight));
        Toolkit.Window.SetTitle(window, "2D Fluid Sim");
        GL.Viewport(0, 0, screenWidth,screenHeight); //important!!!
        
        // --- Objects ---
        //Fluid particles
        List<FluidParticle> particles = new List<FluidParticle>();
        Random random = new Random();
        for (int i = 0; i < particleAmount; i++)
        {
            FluidParticle particle = new FluidParticle(new Vector3((float)random.NextDouble() * 4.0f - 2.0f, (float)random.NextDouble() * 4.0f - 3.0f, 0f), 0.025f);
            particles.Add(particle);
        }
        //Single particle vertices at 0,0,0 (origin)
        Vector3[] vertices = GenerateCircle(new Vector3(0, 0, 0), 1.0f);
        //Bounding Box
        BoundingBox box = new BoundingBox(-2.5f, 2.5f, -1.5f, 1.5f);
        Vector3[] boxVertices = new Vector3[]
        {
            new Vector3(box.MinX, box.MinY, 0),
            new Vector3(box.MaxX, box.MinY, 0),
            new Vector3(box.MaxX, box.MaxY, 0),
            new Vector3(box.MinX, box.MaxY, 0),
            new Vector3(box.MinX, box.MinY, 0) // Close the loop
        };
        
        // --- Setup Code ---
        //Particle Shader
        ParticleShader particleShader = new ParticleShader();
        particleShader.Setup();
        //Bounding Shader
        BoundShader boundShader = new BoundShader();
        boundShader.Setup();
        
        GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f); //background
        GL.Enable(EnableCap.DepthTest); //Enables Depth Test for correct rendering
        // --- Vertex Array Object Setup ---
        //VAO (references objects)
        int particleVao = GL.GenVertexArray();
        int boundVao = GL.GenVertexArray();
        //VBO (buffer for VAO that stores the actual data)
        int particleVbo = GL.GenBuffer();
        int boundVbo = GL.GenBuffer();
        //shaders
        uint particlePosition = (uint)GL.GetAttribLocation(particleShader.Id, "vPosition"); //getting index of the field from OpenGL
        uint boundPosition = (uint)GL.GetAttribLocation(boundShader.Id, "vPosition");
        //connecting openGL shaders and vbo
        //Particle
        GL.BindVertexArray(particleVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, particleVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(particlePosition, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(particlePosition); //telling openGL that data is coming from VAO
        //Bounding box
        GL.BindVertexArray(boundVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, boundVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, boxVertices.Length * Vector3.SizeInBytes, boxVertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(boundPosition, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(boundPosition); //telling openGL that data is coming from VAO
        // --- Camera Setup ---
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), (float)screenWidth / screenHeight, 0.01f, 1000.0f);
        Matrix4 view = Matrix4.LookAt(new Vector3(0, 0, 3), new Vector3(0, 0, 0), new Vector3(0, 1, 0));
        Matrix4 identity = Matrix4.Identity;
        //getting uniforms index
        int viewUniformParticle = GL.GetUniformLocation(particleShader.Id, "view");
        int projectionUniformParticle = GL.GetUniformLocation(particleShader.Id, "projection");
        int modelUniformParticle = GL.GetUniformLocation(particleShader.Id, "model");
        int speedUniformParticle = GL.GetUniformLocation(particleShader.Id, "uSpeed");
        
        int viewUniformBound = GL.GetUniformLocation(boundShader.Id, "view");
        int projectionUniformBound = GL.GetUniformLocation(boundShader.Id, "projection");
        int modelUniformBound = GL.GetUniformLocation(boundShader.Id, "model");
        
        // --- Delta time ---
        //calculating FPS and delta time for smooth simulation
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        float lastTime = 0f;
        //FPS variable
        float fpsTimer = 0f;
        int frameCount = 0;
        // --- Main Loop ---
        while (true)
        {
            // --- DELTA TIME ---
            // Calculating Delta Time
            float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
            float dt = currentTime - lastTime;
            lastTime = currentTime;
            // Cap for safety
            if (dt > 0.1f) dt = 0.1f;
            //FPS calculation
            fpsTimer += dt;
            frameCount++;
            if (fpsTimer >= 0.5f) // Update the console every 0.5 seconds
            {
                float fps = frameCount / fpsTimer;
                Toolkit.Window.SetTitle(window,  $"2D Fluid Sim | FPS: {fps:F0}");
                // Console.WriteLine($"FPS: {fps:F0}"); DEBUG MODE
                fpsTimer = 0f;
                frameCount = 0;
            }
            
            // --- Loop Code ---
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); //clearing buffer with color
            
            //calculating density for all particles
            foreach (var particle in particles)
            {
                particle.UpdateDensity(particles);
            }
            // --- Rendering Particles ---
            particleShader.Use(); //Shader for particles
            GL.BindVertexArray(particleVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformParticle, 1, true, ref projection);
            GL.UniformMatrix4f(viewUniformParticle, 1, true, ref view);
            //Draw every particle
            foreach (var particle in particles) 
            {
                //calculating simulation
                particle.UpdatePosition(box, particles, dt);
                
                //calculating speed for color
                float speed = particle.Velocity.Length;
                float maxExpectedSpeed = 5.0f; 
                float normalizedSpeed = speed / maxExpectedSpeed;
                GL.Uniform1f(speedUniformParticle, normalizedSpeed);
                
                Matrix4 scale = Matrix4.CreateScale(particle.Radius);
                Matrix4 translate = Matrix4.CreateTranslation(particle.CurrentPosition);
                Matrix4 model = scale * translate;
                
                GL.UniformMatrix4f(modelUniformParticle, 1, true, ref model);
                GL.DrawArrays(PrimitiveType.TriangleFan, 0, vertices.Length); //drawing
            }
            // --- Rendering Bounding box ---
            boundShader.Use(); //Shader for bounding box
            GL.BindVertexArray(boundVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformBound, 1, true, ref projection);
            GL.UniformMatrix4f(viewUniformBound, 1, true, ref view);
            GL.UniformMatrix4f(modelUniformBound, 1, false, ref identity);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, boxVertices.Length);
            
            
            Toolkit.OpenGL.SwapBuffers(context); //swap back and front buffers
            //Event Handling
            Toolkit.Window.ProcessEvents(false);
            if (Toolkit.Window.IsWindowDestroyed(window))
            {
                break;
            }
        }
    }

    static Vector3[] GenerateCircle(Vector3 center, float radius, int segments = 32)
    {
        Vector3 [] vertices = new Vector3[segments + 2];
        //Center point
        vertices[0] = center;
        //Points around the circle
        float angleStep = 2 * MathF.PI / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = angleStep * i;
            float x = center.X + radius * MathF.Cos(angle);
            float y = center.Y + radius * MathF.Sin(angle);
            vertices[i + 1] = new Vector3(x, y, center.Z);
        }
        return vertices;
    }
    
    // === density calcualtions ===
    public static float SmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        
        float volume = (Single.Pi * float.Pow(radius, 4)) / 6;
        return (radius - dst) * (radius - dst) / volume;
    }
    //derivative of smoothing kernel used for getting the slope
    public static float SmoothingKernelDerivative(float radius, float dst)
    {
        if (dst >= radius) return 0;

        float scale = 12 / (float.Pow(radius, 4) * Single.Pi);
        return (dst - radius) * scale;
    }

    public static float CalculateDensity(Vector3 samplePoint, List<FluidParticle> particles)
    {
        float density = 0.0f;
        //TODO optimize by only looking at particles in the radius (lookup and grid)
        foreach (FluidParticle particle in particles)
        {
            float dst = (particle.CurrentPosition - samplePoint).Length;
            float influence = SmoothingKernel(smoothingRadius, dst);
            density += particle.Mass * influence;
        }
        return density;
    }
    // === pressure calculations ===
    public static float ConvertDensityToPressure(float density)
    {
        float densityError = density - targetDensity;
        float pressure = float.Max(0, densityError) * pressureMultiplier;
        return pressure;
    }
    
    // gradient calculations (how to change density)
    public static Vector3 CalculatePressureForce(FluidParticle currentParticle, List<FluidParticle> particles)
    {
        Vector3 pressureForce = Vector3.Zero;
        Vector3 samplePoint = currentParticle.CurrentPosition;

        // Calculate the pressure of the current particle itself
        float currentPressure = ConvertDensityToPressure(currentParticle.Density);

        foreach (FluidParticle neighbor in particles)
        {
            if (neighbor == currentParticle) continue; // Skip self
            Vector3 offset = neighbor.CurrentPosition - samplePoint;
            float dst = offset.Length;
            if (dst >= smoothingRadius || dst == 0.0f) continue; //skip if outiside smoothing radius
            Vector3 dir = offset / dst;

            float slope = SmoothingKernelDerivative(smoothingRadius, dst);

            float neighborPressure = ConvertDensityToPressure(neighbor.Density);
            float sharedPressure = (currentPressure + neighborPressure) / 2.0f;
            float neighborDensity = neighbor.Density <= 0.001f ? 0.001f : neighbor.Density;

            pressureForce += dir * slope * sharedPressure * neighbor.Mass / neighborDensity;
        }

        return pressureForce;
    }
    
    // === viscosity calculations ===
    public static float ViscositySmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;

        float volume = Single.Pi * float.Pow(radius, 8) / 4;
        float value = float.Max(0, radius * radius - dst * dst);
        return value * value * value / volume;
    }
    public static Vector3 CalculateViscosityForce(FluidParticle currentParticle, List<FluidParticle> particles)
    {
        Vector3 viscosityForce = Vector3.Zero;
        Vector3 samplePoint = currentParticle.CurrentPosition;
        
        foreach (FluidParticle neighbor in particles)
        {
            if (neighbor == currentParticle) continue; // Skip self
            float dst = (samplePoint - neighbor.CurrentPosition).Length;
            if (dst >= smoothingRadius || dst == 0.0f) continue;
            float influence = ViscositySmoothingKernel(smoothingRadius, dst);
            viscosityForce += (neighbor.Velocity - currentParticle.Velocity) * influence;
        }
        return viscosityForce * viscosityStrenght;
    }

}

