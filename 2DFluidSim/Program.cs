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
    private static int particleAmount = 1000;
    
    private static float smoothingRadius = 0.5f;
    
    public static float targetDensity = 15.0f;
    public static float pressureMultiplier = 0.9f;
    public static float viscosityStrength = 0.045f;
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
        //window options
        Toolkit.Window.SetMode(window, WindowMode.Normal); //Setting window mode to normal
        Toolkit.Window.SetSize(window, new Vector2i(screenWidth,screenHeight));
        Toolkit.Window.SetTitle(window, "2D Fluid Sim");
        GL.Viewport(0, 0, screenWidth,screenHeight); //important!!!
        // --- Camera Setup ---
        Toolkit.Window.GetClientSize(window, out Vector2i clientSize);
        Camera camera = new Camera((float)clientSize.X / clientSize.Y);
        CursorHandle defaultCursor = Toolkit.Cursor.Create(SystemCursorType.Default);
        bool grabbed = false;
        Vector2 last = Vector2.Zero; // vector that stores last mouse position
        // --- movement map ---
        Dictionary<Scancode, bool> keysPressed = new Dictionary<Scancode, bool>()
        {
            { Scancode.W, false },
            { Scancode.S, false },
            { Scancode.A, false },
            { Scancode.D, false },
            { Scancode.Q, false },
            { Scancode.E, false }
        };
        //event queue
        void HandleEvents(PalHandle? handle, PlatformEventType type, EventArgs args)
        {
            switch (args)
            {
                case CloseEventArgs closeEvent:
                    Toolkit.Window.Destroy(window);
                    break;
                
                case MouseMoveEventArgs mouseMove:
                    Vector2 diff = mouseMove.ClientPosition - last;
                    if (grabbed)
                    {
                        camera.Look(diff / 1000f);
                    }
                    last = mouseMove.ClientPosition;
                    break;
                
                case KeyDownEventArgs keyDown:
                    if(keyDown.IsRepeat) break;
                    if (keysPressed.ContainsKey(keyDown.Scancode))
                    {
                        keysPressed[keyDown.Scancode] = true;
                    }
                    switch (keyDown.Scancode)
                    {
                        case Scancode.LeftAlt:
                            Toolkit.Window.SetCursorCaptureMode(window, CursorCaptureMode.Locked);
                            Toolkit.Window.SetCursor(window, null);
                            grabbed = true;
                            break;
                    }
                    break;
                
                case KeyUpEventArgs keyUp:
                    if (keysPressed.ContainsKey(keyUp.Scancode))
                    {
                        keysPressed[keyUp.Scancode] = false;
                    }

                    switch (keyUp.Scancode)
                    {
                        case Scancode.LeftAlt:
                            Toolkit.Window.SetCursorCaptureMode(window, CursorCaptureMode.Normal);
                            Toolkit.Window.SetCursor(window, defaultCursor);
                            grabbed = false;
                            break;
                    }
                    break;
                
            }
        }
        EventQueue.EventRaised += HandleEvents;
        
        // --- Objects ---
        //Bounding Box
        BoundingBox3D box = new BoundingBox3D(-2.0f, 2.0f, -1.0f, 1.0f, -1.5f, 1.5f);
        Vector3[] boxVertices = new Vector3[]
        {
            // Bottom 
            new Vector3(box.MinX, box.MinY, box.MinZ),
            new Vector3(box.MaxX, box.MinY, box.MinZ),
            new Vector3(box.MaxX, box.MinY, box.MaxZ),
            new Vector3(box.MinX, box.MinY, box.MaxZ),
            new Vector3(box.MinX, box.MinY, box.MinZ),
            //Connect top from bottom
            new Vector3(box.MinX, box.MaxY, box.MinZ),
            // Top
            new Vector3(box.MaxX, box.MaxY, box.MinZ),
            new Vector3(box.MaxX, box.MaxY, box.MaxZ),
            new Vector3(box.MinX, box.MaxY, box.MaxZ),
            new Vector3(box.MinX, box.MaxY, box.MinZ),
            //Rest
            new Vector3(box.MaxX, box.MaxY, box.MinZ),
            new Vector3(box.MaxX, box.MinY, box.MinZ),
            new Vector3(box.MaxX, box.MinY, box.MaxZ),
            new Vector3(box.MaxX, box.MaxY, box.MaxZ),
            new Vector3(box.MinX, box.MaxY, box.MaxZ),
            new Vector3(box.MinX, box.MinY, box.MaxZ)
        };
        //Fluid particles
        List<FluidParticle> particles = new List<FluidParticle>();
        Random random = new Random();
        // Spawning volume
        float spawnMinX = box.MinX + 0.2f; 
        float spawnMaxX = box.MinX + 1.2f; 
        float spawnMinY = box.MaxY - 1.0f;
        float spawnMaxY = box.MaxY - 0.1f;
        float spawnMinZ = box.MinZ + 0.3f; 
        float spawnMaxZ = box.MaxZ - 0.3f; 
        for (int i = 0; i < particleAmount; i++)
        {
            // Generate a random position constrained entirely within the upper-left sub-box
            float randomX = spawnMinX + (float)random.NextDouble() * (spawnMaxX - spawnMinX);
            float randomY = spawnMinY + (float)random.NextDouble() * (spawnMaxY - spawnMinY);
            float randomZ = spawnMinZ + (float)random.NextDouble() * (spawnMaxZ - spawnMinZ); 

            FluidParticle particle = new FluidParticle(new Vector3(randomX, randomY, randomZ), 0.05f);
            particles.Add(particle);
        }
        //Single particle sphere
        var sphereData = GenerateSphere(1.0f, 16, 16);
        Vector3[] vertices = sphereData.Vertices;
        uint[] indices = sphereData.Indices;
        
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
        //EBO (index buffer for spheres)
        int particleEbo = GL.GenBuffer();
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
        
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, particleEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsage.StaticDraw);
        //Bounding box
        GL.BindVertexArray(boundVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, boundVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, boxVertices.Length * Vector3.SizeInBytes, boxVertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(boundPosition, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(boundPosition); //telling openGL that data is coming from VAO
        //Identity matrix for perspective
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
        Stopwatch frameTimer = new Stopwatch();
        stopwatch.Start();
        float lastTime = 0f;
        //FPS variable
        float fpsTimer = 0f;
        int frameCount = 0;
        float titleUpdateTimer = 0f;
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
            // --- Camera movement ---
            Vector3 moveDirection = Vector3.Zero;
            if (keysPressed[Scancode.W]) moveDirection += new Vector3(0f, 0f, 1f);
            if (keysPressed[Scancode.S]) moveDirection += new Vector3(0f, 0f, -1f);
            if (keysPressed[Scancode.D]) moveDirection += new Vector3(-1f, 0f, 0f);
            if (keysPressed[Scancode.A]) moveDirection += new Vector3(1f, 0f, 0f);
            if (keysPressed[Scancode.Q]) moveDirection += new Vector3(0f, 1f, 0f);
            if (keysPressed[Scancode.E]) moveDirection += new Vector3(0f, -1f, 0f);

            if (moveDirection != Vector3.Zero)
            {
                // Adjust the multiplier value (e.g., 4.0f) to make the fly speed faster or slower
                camera.Move(moveDirection * (4.0f * dt)); 
            }
            
            // --- Loop Code ---
            //GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); //clearing buffer with color
            frameTimer.Restart();
            //calculating density for all particles
            foreach (var particle in particles)
            {
                particle.UpdateDensity(particles);
            }
            // --- Rendering Particles ---
            particleShader.Use(); //Shader for particles
            GL.BindVertexArray(particleVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformParticle, 1, true, camera.Projection);
            GL.UniformMatrix4f(viewUniformParticle, 1, true, camera.View);
            //Draw every particle
            foreach (var particle in particles) 
            {
                //calculating simulation
                particle.UpdatePosition(box, particles, dt);/*
                
                //calculating speed for color
                float speed = particle.Velocity.Length;
                float maxExpectedSpeed = 3.0f; 
                float normalizedSpeed = speed / maxExpectedSpeed;
                GL.Uniform1f(speedUniformParticle, normalizedSpeed);
                
                Matrix4 scale = Matrix4.CreateScale(particle.Radius);
                Matrix4 translate = Matrix4.CreateTranslation(particle.CurrentPosition);
                Matrix4 model = scale * translate;
                
                GL.UniformMatrix4f(modelUniformParticle, 1, true, ref model);
                GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0); //drawing*/
            }
            //Time elapsed
            frameTimer.Stop();
            double frameTimeMs = frameTimer.Elapsed.TotalMilliseconds;
            double instantFps = frameTimeMs > 0.0 ? 1000.0 / frameTimeMs : 99999.0;
            //--- Print FPS ---
            titleUpdateTimer += dt;
            if (titleUpdateTimer >= 0.1f)
            {
                Toolkit.Window.SetTitle(window, $"Time: {frameTimeMs:F3} ms | Instant FPS: {instantFps:F0}");
                Console.WriteLine($"Time: {frameTimeMs:F3} ms | Instant FPS: {instantFps:F0}");
                titleUpdateTimer = 0f;
            }/*
            // --- Rendering Bounding box ---
            boundShader.Use(); //Shader for bounding box
            GL.BindVertexArray(boundVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformBound, 1, true, camera.Projection);
            GL.UniformMatrix4f(viewUniformBound, 1, true, camera.View);
            GL.UniformMatrix4f(modelUniformBound, 1, false, ref identity);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, boxVertices.Length);// */
            
            
            Toolkit.OpenGL.SwapBuffers(context); //swap back and front buffers
            //Event Handling
            Toolkit.Window.ProcessEvents(false);
            if (Toolkit.Window.IsWindowDestroyed(window))
            {
                break;
            }
        }
    }
    
    static (Vector3[] Vertices, uint[] Indices) GenerateSphere(float radius, int sectors = 16, int rings = 16)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<uint> indices = new List<uint>();

        float lengthInv = 1.0f / radius;
        float sectorStep = 2 * MathF.PI / sectors;
        float ringStep = MathF.PI / rings;

        for (int i = 0; i <= rings; ++i)
        {
            float ringAngle = MathF.PI / 2 - i * ringStep; // starting from pi/2 to -pi/2
            float xy = radius * MathF.Cos(ringAngle);    // r * cos(u)
            float z = radius * MathF.Sin(ringAngle);     // r * sin(u)

            for (int j = 0; j <= sectors; ++j)
            {
                float sectorAngle = j * sectorStep;      // starting from 0 to 2pi

                float x = xy * MathF.Cos(sectorAngle);   // r * cos(u) * cos(v)
                float y = xy * MathF.Sin(sectorAngle);   // r * cos(u) * sin(v)
                vertices.Add(new Vector3(x, y, z));
            }
        }

        for (int i = 0; i < rings; ++i)
        {
            uint k1 = (uint)(i * (sectors + 1));     // beginning of current ring
            uint k2 = (uint)(k1 + sectors + 1);      // beginning of next ring

            for (int j = 0; j < sectors; ++j, ++k1, ++k2)
            {
                // 2 triangles per sector except for the top and bottom poles
                if (i != 0)
                {
                    indices.Add(k1);
                    indices.Add(k2);
                    indices.Add(k1 + 1);
                }

                if (i != (rings - 1))
                {
                    indices.Add(k1 + 1);
                    indices.Add(k2);
                    indices.Add(k2 + 1);
                }
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }
    
    // === density calcualtions ===
    public static float SmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        
        float volume = (Single.Pi * float.Pow(radius, 5)) / 10f;
        return (radius - dst) * (radius - dst) * (radius - dst) / volume;
    }
    //derivative of smoothing kernel used for getting the slope
    public static float SmoothingKernelDerivative(float radius, float dst)
    {
        if (dst >= radius) return 0;
        
        float scale = 30f / (float.Pow(radius, 5) * Single.Pi);
        return -((radius - dst) * (radius - dst)) * scale;
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

        float volume = (2f * Single.Pi * float.Pow(radius, 5)) / 15f;
        return (radius - dst) / volume;
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
        return viscosityForce * viscosityStrength;
    }

}

