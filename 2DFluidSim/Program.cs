using OpenTK.Mathematics;
using OpenTK.Platform;
using OpenTK.Graphics.OpenGL;
using System.Diagnostics;
using OpenTK.Windowing.Common;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using MouseMoveEventArgs = OpenTK.Platform.MouseMoveEventArgs;

namespace _2DFluidSim;

class Program
{
    private static int screenHeight = 720;
    private static int screenWidth = 1280;
    private static int particleAmount = 25000;
    
    private static float smoothingRadius = 0.5f;
    //variables based on smoothingRadius
    private static float densityKernelVolumeScale;
    private static float pressureKernelScale;
    private static float viscosityKernelVolume;
    
    public static float targetDensity = 15.0f;
    public static float pressureMultiplier = 0.7f;
    public static float viscosityStrength = 0.045f;
    
    // Spatial Hash Grid For Determining particles cell - READONLY when using parallel
    private static Dictionary<Vector3i, List<FluidParticle>> spatialGrid = new();
    // Static neighbour List
    private static List<FluidParticle> neighborCache = new List<FluidParticle>(500);
    static void Main()
    {
        // --- Cuda Setup ---
        //ILGPU initialization and CUDA drivers
        using var cudaContext = Context.Create(builder => builder.Cuda());
        var device = cudaContext.GetCudaDevice(0); //Getting GPU
        using var accelerator = device.CreateAccelerator(cudaContext);
        //Compilation JIT of C# kernel for GPU code (compiling kernels)
        var densityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, float, float
        >(FluidKernels.ComputeDensityKernel);
        
        var positionKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, float, float, float, float, float, float, float, float, float, float, float, float, float
        >(FluidKernels.UpdatePositionsKernel);
        //Calculating Pow of Radius
        float r = smoothingRadius;
        densityKernelVolumeScale = 10f / (Single.Pi * float.Pow(r, 5));
        pressureKernelScale = 30f / (float.Pow(r, 5) * Single.Pi);
        viscosityKernelVolume = (2f * Single.Pi * float.Pow(r, 5)) / 15f;
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
        Toolkit.Window.SetTitle(window, "3D Fluid Sim CUDA");
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
        BoundingBox3D box = new BoundingBox3D(-3.5f, 3.5f, -3.0f, 3.0f, -4.0f, 4.0f);
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
        //Fluid particles (static array for CPU memory)
        GpuParticle[] hostParticles = new GpuParticle[particleAmount];
        Random random = new Random();
        float spawnMinX = box.MinX + 0.2f; float spawnMaxX = box.MinX + 1.2f; 
        float spawnMinY = box.MaxY - 1.0f; float spawnMaxY = box.MaxY - 0.1f;
        float spawnMinZ = box.MinZ + 0.3f; float spawnMaxZ = box.MaxZ - 0.3f; 

        for (int i = 0; i < particleAmount; i++)
        {
            hostParticles[i].Position.X = spawnMinX + (float)random.NextDouble() * (spawnMaxX - spawnMinX);
            hostParticles[i].Position.Y = spawnMinY + (float)random.NextDouble() * (spawnMaxY - spawnMinY);
            hostParticles[i].Position.Z = spawnMinZ + (float)random.NextDouble() * (spawnMaxZ - spawnMinZ); 
            hostParticles[i].Velocity = Vector3.Zero;
            hostParticles[i].Mass = 1.0f;
            hostParticles[i].Density = 0.0f;
        }
        // Allocation of memory buffer in VRAM
        using MemoryBuffer1D<GpuParticle, Stride1D.Dense> gpuParticlesBuffer = accelerator.Allocate1D(hostParticles); 
        
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
            frameTimer.Restart();
            // --- Starting the CUDA kernels ---
            //Calculating the density for all the particles
            densityKernel((int)gpuParticlesBuffer.Length, gpuParticlesBuffer.View, smoothingRadius, densityKernelVolumeScale);
            // Calculating forces and updating the positions
            positionKernel(
                (int)gpuParticlesBuffer.Length, 
                gpuParticlesBuffer.View, 
                box.MinX, box.MaxX, box.MinY, box.MaxY, box.MinZ, box.MaxZ,
                smoothingRadius, pressureKernelScale, viscosityKernelVolume,
                targetDensity, pressureMultiplier, viscosityStrength, dt
            );
            //Waiting for the frame calculations (synchronization of the GPU accelerator)
            accelerator.Synchronize();
            //Copy data from device to the CPU for rendering needs
            gpuParticlesBuffer.CopyToCPU(hostParticles);
            // --- Render loop code ---
            
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); //clearing buffer with color
            //Updating particles cell location
            // --- Rendering Particles ---
            particleShader.Use(); //Shader for particles
            GL.BindVertexArray(particleVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformParticle, 1, true, camera.Projection);
            GL.UniformMatrix4f(viewUniformParticle, 1, true, camera.View);
            //Draw every particle 
            for (int i = 0; i < particleAmount; i++)
            {
                float speed = hostParticles[i].Velocity.Length;
                float maxExpectedSpeed = 3.0f;
                float normalizedSpeed = speed / maxExpectedSpeed;
                GL.Uniform1f(speedUniformParticle, normalizedSpeed);

                Matrix4 scale = Matrix4.CreateScale(0.05f); // TODO make particle radius a variable
                Matrix4 translate = Matrix4.CreateTranslation(hostParticles[i].Position);
                Matrix4 model = scale * translate;

                GL.UniformMatrix4f(modelUniformParticle, 1, true, ref model);
                GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
            } //*/
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
            }
            // --- Rendering Bounding box ---
            boundShader.Use(); //Shader for bounding box
            GL.BindVertexArray(boundVao); //using correct Vao
            //Projection info
            GL.UniformMatrix4f(projectionUniformBound, 1, true, camera.Projection);
            GL.UniformMatrix4f(viewUniformBound, 1, true, camera.View);
            GL.UniformMatrix4f(modelUniformBound, 1, false, ref identity);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, boxVertices.Length);
            
            Toolkit.OpenGL.SwapBuffers(context); //swap back and front buffers*/
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
    /*
    // === Spatial Grid Code ===
    private static void UpdateSpatialGrid(List<FluidParticle> particles)
    {
        //clearing list inside the dictionary
        foreach (var cellList in spatialGrid.Values)
        {
            cellList.Clear();
        }

        foreach (var particle in particles)
        {
            Vector3i cellKey = GetCellKey(particle.CurrentPosition);
            if (!spatialGrid.TryGetValue(cellKey, out var cellList))
            {
                // new list are only created when cell first appears in the simulation
                cellList = new List<FluidParticle>(32);
                spatialGrid[cellKey] = cellList;
            }
            cellList.Add(particle);
        }
    }
    
    public static Vector3i GetCellKey(Vector3 position)
    {
        return new Vector3i(
            (int)MathF.Floor(position.X / smoothingRadius),
            (int)MathF.Floor(position.Y / smoothingRadius),
            (int)MathF.Floor(position.Z / smoothingRadius)
        );
    }
    
    public static List<FluidParticle> GetNearbyNeighbors(Vector3 position) //Returns local allocation for the thread
{
        List<FluidParticle> neighbors = new List<FluidParticle>(64);
        Vector3i centerKey = GetCellKey(position);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3i targetKey = new Vector3i(centerKey.X + x, centerKey.Y + y, centerKey.Z + z);
                    
                    // Bezpieczne, ponieważ struktura słownika nie zmienia się w tym kroku
                    if (spatialGrid.TryGetValue(targetKey, out var cellParticles))
                    {
                        neighbors.AddRange(cellParticles);
                    }
                }
            }
        }
        return neighbors;
    }
    // === density calcualtions ===
    public static float SmoothingKernel(float radius, float dst)
    {
        if (dst >= radius) return 0;
        float diff = radius - dst;
        return (diff * diff * diff) * densityKernelVolumeScale;
    }
    //derivative of smoothing kernel used for getting the slope
    public static float SmoothingKernelDerivative(float radius, float dst)
    {
        if (dst >= radius) return 0;
        float diff = radius - dst;
        return -(diff * diff) * pressureKernelScale;
    }

    public static float CalculateDensity(Vector3 samplePoint)
    {
        float density = 0.0f;
        var neighbours = GetNearbyNeighbors(samplePoint);
        foreach (FluidParticle particle in neighbours)
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
    public static Vector3 CalculatePressureForce(FluidParticle currentParticle)
    {
        Vector3 pressureForce = Vector3.Zero;
        Vector3 samplePoint = currentParticle.CurrentPosition;

        // Calculate the pressure of the current particle itself
        float currentPressure = ConvertDensityToPressure(currentParticle.Density);

        var neighbors = GetNearbyNeighbors(samplePoint);
        foreach (FluidParticle neighbor in neighbors)
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

        return (radius - dst) / viscosityKernelVolume;
    }
    public static Vector3 CalculateViscosityForce(FluidParticle currentParticle)
    {
        Vector3 viscosityForce = Vector3.Zero;
        Vector3 samplePoint = currentParticle.CurrentPosition;
        var neighbors = GetNearbyNeighbors(samplePoint); //Now the particle looks at the neighbours based on the spatial hash
        
        foreach (FluidParticle neighbor in neighbors)
        {
            if (neighbor == currentParticle) continue; // Skip self
            float dst = (samplePoint - neighbor.CurrentPosition).Length;
            if (dst >= smoothingRadius || dst == 0.0f) continue;
            float influence = ViscositySmoothingKernel(smoothingRadius, dst);
            viscosityForce += (neighbor.Velocity - currentParticle.Velocity) * influence;
        }
        return viscosityForce * viscosityStrength;
    }
    
    */
}

