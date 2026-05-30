using OpenTK.Mathematics;
using OpenTK.Platform;
using OpenTK.Graphics.OpenGL;
using System.Diagnostics;
using OpenTK.Windowing.Common;
using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using MouseMoveEventArgs = OpenTK.Platform.MouseMoveEventArgs;

namespace _2DFluidSim;

class Program
{
    private static int screenHeight = 720;
    private static int screenWidth = 1280;
    private static int particleAmount = 8000; 
    
    private static float smoothingRadius = 0.5f;
    private static float densityKernelVolumeScale;
    private static float pressureKernelScale;
    private static float viscosityKernelVolume;
    
    public static float targetDensity = 15.0f;
    public static float pressureMultiplier = 0.7f;
    public static float viscosityStrength = 0.045f;

    private const int CELL_MAX_CAPACITY = 64;

    static void Main()
    {
        // --- OpenGL Setup ---
        ToolkitOptions tkOptions = new ToolkitOptions();
        Toolkit.Init(tkOptions);
        OpenGLGraphicsApiHints apiHints = new OpenGLGraphicsApiHints();
        WindowHandle window = Toolkit.Window.Create(apiHints);
        OpenGLContextHandle context = Toolkit.OpenGL.CreateFromWindow(window);
        Toolkit.OpenGL.SetCurrentContext(context);
        OpenTK.Graphics.GLLoader.LoadBindings(Toolkit.OpenGL.GetBindingsContext(context));
        
        Toolkit.Window.SetMode(window, WindowMode.Normal);
        Toolkit.Window.SetSize(window, new Vector2i(screenWidth, screenHeight));
        Toolkit.Window.SetTitle(window, "3D Fluid Sim - Method 1 (No-Alloc Zero-Unsafe Bridge)");
        GL.Viewport(0, 0, screenWidth, screenHeight);

        // --- Cuda Setup ---
        using var cudaContext = Context.Create(builder => builder.Cuda());
        var device = cudaContext.GetCudaDevice(0);
        using var accelerator = device.CreateAccelerator(cudaContext);
        
        var densityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig
        >(FluidKernels.ComputeDensityKernel);
        
        var positionKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig, float
        >(FluidKernels.UpdatePositionsKernel);

        float r = smoothingRadius;
        densityKernelVolumeScale = 10f / (Single.Pi * float.Pow(r, 5));
        pressureKernelScale = 30f / (float.Pow(r, 5) * Single.Pi);
        viscosityKernelVolume = (2f * Single.Pi * float.Pow(r, 5)) / 15f;

        // --- Camera Setup ---
        Toolkit.Window.GetClientSize(window, out Vector2i clientSize);
        Camera camera = new Camera((float)clientSize.X / clientSize.Y);
        CursorHandle defaultCursor = Toolkit.Cursor.Create(SystemCursorType.Default);
        bool grabbed = false;
        Vector2 last = Vector2.Zero;
        
        Dictionary<Scancode, bool> keysPressed = new Dictionary<Scancode, bool>()
        {
            { Scancode.W, false }, { Scancode.S, false }, { Scancode.A, false },
            { Scancode.D, false }, { Scancode.Q, false }, { Scancode.E, false }
        };

        void HandleEvents(PalHandle? handle, PlatformEventType type, EventArgs args)
        {
            switch (args)
            {
                case CloseEventArgs:
                    Toolkit.Window.Destroy(window);
                    break;
                case MouseMoveEventArgs mouseMove:
                    Vector2 diff = mouseMove.ClientPosition - last;
                    if (grabbed) camera.Look(diff / 1000f);
                    last = mouseMove.ClientPosition;
                    break;
                case KeyDownEventArgs keyDown:
                    if (keyDown.IsRepeat) break;
                    if (keysPressed.ContainsKey(keyDown.Scancode)) keysPressed[keyDown.Scancode] = true;
                    if (keyDown.Scancode == Scancode.LeftAlt)
                    {
                        Toolkit.Window.SetCursorCaptureMode(window, CursorCaptureMode.Locked);
                        Toolkit.Window.SetCursor(window, null);
                        grabbed = true;
                    }
                    break;
                case KeyUpEventArgs keyUp:
                    if (keysPressed.ContainsKey(keyUp.Scancode)) keysPressed[keyUp.Scancode] = false;
                    if (keyUp.Scancode == Scancode.LeftAlt)
                    {
                        Toolkit.Window.SetCursorCaptureMode(window, CursorCaptureMode.Normal);
                        Toolkit.Window.SetCursor(window, defaultCursor);
                        grabbed = false;
                    }
                    break;
            }
        }
        EventQueue.EventRaised += HandleEvents;
        
        // --- Objects & Grid Config ---
        BoundingBox3D box = new BoundingBox3D(-3.5f, 3.5f, -3.0f, 3.0f, -4.0f, 4.0f);
        Vector3[] boxVertices = {
            new(-3.5f, -3.0f, -4.0f), new(3.5f, -3.0f, -4.0f), new(3.5f, -3.0f, 4.0f), new(-3.5f, -3.0f, 4.0f), new(-3.5f, -3.0f, -4.0f),
            new(-3.5f, 3.0f, -4.0f),  new(3.5f, 3.0f, -4.0f),  new(3.5f, 3.0f, 4.0f),  new(-3.5f, 3.0f, 4.0f),  new(-3.5f, 3.0f, -4.0f),
            new(3.5f, 3.0f, -4.0f),   new(3.5f, -3.0f, -4.0f), new(3.5f, -3.0f, 4.0f),  new(3.5f, 3.0f, 4.0f),   new(-3.5f, 3.0f, 4.0f),  new(-3.5f, -3.0f, 4.0f)
        };

        Vector3 gridMin = new Vector3(box.MinX - 0.5f, box.MinY - 0.5f, box.MinZ - 0.5f);
        Vector3 gridMax = new Vector3(box.MaxX + 0.5f, box.MaxY + 0.5f, box.MaxZ + 0.5f);
        Vector3i gridDimensions = new Vector3i(
            (int)MathF.Ceiling((gridMax.X - gridMin.X) / smoothingRadius),
            (int)MathF.Ceiling((gridMax.Y - gridMin.Y) / smoothingRadius),
            (int)MathF.Ceiling((gridMax.Z - gridMin.Z) / smoothingRadius)
        );

        int totalCells = gridDimensions.X * gridDimensions.Y * gridDimensions.Z;
        int[] hostGridParticles = new int[totalCells * CELL_MAX_CAPACITY];
        int[] hostCellCounts = new int[totalCells];

        GpuParticle[] hostParticles = new GpuParticle[particleAmount];
        Random random = new Random();
        for (int i = 0; i < particleAmount; i++)
        {
            hostParticles[i].Position = new Vector3(
                box.MinX + 0.5f + (float)random.NextDouble() * 2.0f,
                box.MaxY - 1.5f + (float)random.NextDouble() * 1.5f,
                box.MinZ + 0.5f + (float)random.NextDouble() * 3.0f
            );
            hostParticles[i].Velocity = Vector3.Zero;
            hostParticles[i].Mass = 1.0f;
        }

        using MemoryBuffer1D<int, Stride1D.Dense> gpuGridParticles = accelerator.Allocate1D<int>(hostGridParticles.Length);
        using MemoryBuffer1D<int, Stride1D.Dense> gpuCellCounts = accelerator.Allocate1D<int>(hostCellCounts.Length);

        // --- ALOKACJA BUFORA PO STRONIE ILGPU ---
        using MemoryBuffer1D<GpuParticle, Stride1D.Dense> gpuParticlesBuffer = accelerator.Allocate1D<GpuParticle>(particleAmount);
        gpuParticlesBuffer.CopyFromCPU(hostParticles);

        ArrayView<GpuParticle> mainBufferView = gpuParticlesBuffer.View;
        
        int particleStructSize = Marshal.SizeOf<GpuParticle>();
        int totalBufferSizeInBytes = particleAmount * particleStructSize;

        // --- INICJALIZACJA OPENGL VBO ---
        int particleVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, particleVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, totalBufferSizeInBytes, IntPtr.Zero, BufferUsage.StreamDraw);

        var sphereData = GenerateSphere(1.0f, 8, 8);
        Vector3[] vertices = sphereData.Vertices;
        uint[] indices = sphereData.Indices;
        
        ParticleShader particleShader = new ParticleShader();
        particleShader.Setup();
        BoundShader boundShader = new BoundShader();
        boundShader.Setup();
        
        GL.ClearColor(0.05f, 0.05f, 0.08f, 1.0f);
        GL.Enable(EnableCap.DepthTest);

        int particleVao = GL.GenVertexArray();
        int boundVao = GL.GenVertexArray();
        int sphereVbo = GL.GenBuffer();
        int boundVbo = GL.GenBuffer();
        int particleEbo = GL.GenBuffer();

        // Konfiguracja Instanced Renderingu w VAO
        GL.BindVertexArray(particleVao);
        
        GL.BindBuffer(BufferTarget.ArrayBuffer, sphereVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(0);

        GL.BindBuffer(BufferTarget.ArrayBuffer, particleVbo);
        
        // GpuParticle.Position (offset = 0)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, particleStructSize, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribDivisor(1, 1);

        // GpuParticle.Velocity (offset = 12 bajtów)
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, particleStructSize, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribDivisor(2, 1);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, particleEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsage.StaticDraw);

        // Bounding Box
        GL.BindVertexArray(boundVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, boundVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, boxVertices.Length * Vector3.SizeInBytes, boxVertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(0);

        Matrix4 identity = Matrix4.Identity;
        int viewUniformParticle = GL.GetUniformLocation(particleShader.Id, "view");
        int projectionUniformParticle = GL.GetUniformLocation(particleShader.Id, "projection");
        int viewUniformBound = GL.GetUniformLocation(boundShader.Id, "view");
        int projectionUniformBound = GL.GetUniformLocation(boundShader.Id, "projection");
        int modelUniformBound = GL.GetUniformLocation(boundShader.Id, "model");
        
        Stopwatch stopwatch = new Stopwatch();
        Stopwatch frameTimer = new Stopwatch();
        stopwatch.Start();
        float lastTime = 0f;
        float titleUpdateTimer = 0f;
        Index1D gridExtent = new Index1D(particleAmount);

        // Stały bufor synchronizacyjny na CPU – alokowany RAZ, wielokrotnie używany
        GpuParticle[] tempCpuSyncArray = new GpuParticle[particleAmount];

        while (true)
        {
            float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
            float dt = currentTime - lastTime;
            lastTime = currentTime;
            if (dt > 0.05f) dt = 0.05f;

            frameTimer.Restart();

            // --- KROK 1: ŚCIĄGNIĘCIE DANYCH DO REUZYWALNEJ TABLICY ---
            mainBufferView.CopyToCPU(tempCpuSyncArray);

            // Przeliczanie siatki przestrzennej na CPU przy użyciu tej samej tablicy
            Array.Clear(hostCellCounts, 0, hostCellCounts.Length);
            for (int i = 0; i < particleAmount; i++)
            {
                int cellX = (int)MathF.Floor((tempCpuSyncArray[i].Position.X - gridMin.X) / smoothingRadius);
                int cellY = (int)MathF.Floor((tempCpuSyncArray[i].Position.Y - gridMin.Y) / smoothingRadius);
                int cellZ = (int)MathF.Floor((tempCpuSyncArray[i].Position.Z - gridMin.Z) / smoothingRadius);

                if (cellX >= 0 && cellX < gridDimensions.X && cellY >= 0 && cellY < gridDimensions.Y && cellZ >= 0 && cellZ < gridDimensions.Z)
                {
                    int cellLinearIndex = cellX + cellY * gridDimensions.X + cellZ * gridDimensions.X * gridDimensions.Y;
                    int currentCount = hostCellCounts[cellLinearIndex];
                    if (currentCount < CELL_MAX_CAPACITY)
                    {
                        hostGridParticles[cellLinearIndex * CELL_MAX_CAPACITY + currentCount] = i;
                        hostCellCounts[cellLinearIndex]++;
                    }
                }
            }
            gpuGridParticles.CopyFromCPU(hostGridParticles);
            gpuCellCounts.CopyFromCPU(hostCellCounts);

            FluidConfig config = new FluidConfig
            {
                MinX = box.MinX, MaxX = box.MaxX, MinY = box.MinY, MaxY = box.MaxY, MinZ = box.MinZ, MaxZ = box.MaxZ,
                SmoothingRadius = smoothingRadius, DensityKernelVolumeScale = densityKernelVolumeScale,
                PressureKernelScale = pressureKernelScale, ViscosityKernelVolume = viscosityKernelVolume,
                TargetDensity = targetDensity, PressureMultiplier = pressureMultiplier, ViscosityStrength = viscosityStrength,
                GridDimensions = gridDimensions, GridMin = gridMin
            };

            // Wykonanie fizyki w pamięci GPU
            densityKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config);
            positionKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config, dt);
            
            // Konieczna synchronizacja przed przesłaniem danych do renderu
            accelerator.Synchronize();

            // --- KROK 2: METODA 1 – REUZYWALNA TABLICA TRAFIA DO OPENGL ---
            // Ponieważ dane z GPU zostały zaktualizowane, musimy ponownie pobrać stan końcowy fizyki do naszej tablicy...
            mainBufferView.CopyToCPU(tempCpuSyncArray);

            // ...i natychmiast wysłać ją bezpośrednio do VBO OpenGL bez żadnych dodatkowych alokacji w pętli!
            GL.BindBuffer(BufferTarget.ArrayBuffer, particleVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, totalBufferSizeInBytes, tempCpuSyncArray);

            // --- RENDEROWANIE ---
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            particleShader.Use();
            GL.BindVertexArray(particleVao);
            
            GL.UniformMatrix4f(projectionUniformParticle, 1, false, camera.Projection);
            GL.UniformMatrix4f(viewUniformParticle, 1, false, camera.View);

            GL.DrawElementsInstanced(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, IntPtr.Zero, particleAmount);

            frameTimer.Stop();
            double frameTimeMs = frameTimer.Elapsed.TotalMilliseconds;
            double instantFps = frameTimeMs > 0.0 ? 1000.0 / frameTimeMs : 9999.0;

            titleUpdateTimer += dt;
            if (titleUpdateTimer >= 0.1f)
            {
                Toolkit.Window.SetTitle(window, $"Frame Total: {frameTimeMs:F2} ms | FPS: {instantFps:F0}");
                Console.WriteLine($"Frame Total: {frameTimeMs:F2} ms | FPS: {instantFps:F0}");
                titleUpdateTimer = 0f;
            }

            // Renderowanie Bounding Boxa
            boundShader.Use();
            GL.BindVertexArray(boundVao);
            GL.UniformMatrix4f(projectionUniformBound, 1, false, camera.Projection);
            GL.UniformMatrix4f(viewUniformBound, 1, false, camera.View);
            GL.UniformMatrix4f(modelUniformBound, 1, false, ref identity);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, boxVertices.Length);
            
            Toolkit.OpenGL.SwapBuffers(context);

            Vector3 moveDirection = Vector3.Zero;
            if (keysPressed[Scancode.W]) moveDirection += new Vector3(0f, 0f, 1f);
            if (keysPressed[Scancode.S]) moveDirection += new Vector3(0f, 0f, -1f);
            if (keysPressed[Scancode.D]) moveDirection += new Vector3(-1f, 0f, 0f);
            if (keysPressed[Scancode.A]) moveDirection += new Vector3(1f, 0f, 0f);
            if (keysPressed[Scancode.Q]) moveDirection += new Vector3(0f, 1f, 0f);
            if (keysPressed[Scancode.E]) moveDirection += new Vector3(0f, -1f, 0f);

            if (moveDirection != Vector3.Zero) camera.Move(moveDirection * (4.0f * dt));

            Toolkit.Window.ProcessEvents(false);
            if (Toolkit.Window.IsWindowDestroyed(window)) break;
        }

        GL.DeleteBuffer(particleVbo);
        GL.DeleteBuffer(sphereVbo);
    }
    
    static (Vector3[] Vertices, uint[] Indices) GenerateSphere(float radius, int sectors = 8, int rings = 8)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<uint> indices = new List<uint>();
        float sectorStep = 2 * MathF.PI / sectors;
        float ringStep = MathF.PI / rings;

        for (int i = 0; i <= rings; ++i)
        {
            float ringAngle = MathF.PI / 2 - i * ringStep;
            float xy = radius * MathF.Cos(ringAngle);
            float z = radius * MathF.Sin(ringAngle);

            for (int j = 0; j <= sectors; ++j)
            {
                float sectorAngle = j * sectorStep;
                float x = xy * MathF.Cos(sectorAngle);
                float y = xy * MathF.Sin(sectorAngle);
                vertices.Add(new Vector3(x, y, z));
            }
        }

        for (int i = 0; i < rings; ++i)
        {
            uint k1 = (uint)(i * (sectors + 1));
            uint k2 = (uint)(k1 + sectors + 1);

            for (int j = 0; j < sectors; ++j, ++k1, ++k2)
            {
                if (i != 0) { indices.Add(k1); indices.Add(k2); indices.Add(k1 + 1); }
                if (i != (rings - 1)) { indices.Add(k1 + 1); indices.Add(k2); indices.Add(k2 + 1); }
            }
        }
        return (vertices.ToArray(), indices.ToArray());
    }
}