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
    private static int particleAmount = 1000; 
    
    private static float smoothingRadius = 0.5f;
    private static float densityKernelVolumeScale;
    private static float pressureKernelScale;
    private static float viscosityKernelVolume;
    
    public static float targetDensity = 90.0f;
    public static float pressureMultiplier = 0.7f;
    public static float viscosityStrength = 0.045f;

    private const int CELL_MAX_CAPACITY = 64;

    // --- ROZDZIELCZOŚĆ TEKSTURY 3D DLA RAYMARCHINGU ---
    private static int volumeResX = 64;
    private static int volumeResY = 64;
    private static int volumeResZ = 64;

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
        Toolkit.Window.SetTitle(window, "3D Fluid Sim - Raymarching Density Field");
        GL.Viewport(0, 0, screenWidth, screenHeight);

        // --- Cuda Setup ---
        using var cudaContext = Context.Create(builder => builder.Cuda());
        var device = cudaContext.GetCudaDevice(0);
        using var accelerator = device.CreateAccelerator(cudaContext);
        
        // Ładowanie kerneli fizycznych
        var densityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig
        >(FluidKernels.ComputeDensityKernel);
        
        var positionKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig, float
        >(FluidKernels.UpdatePositionsKernel);

        // POPRAWKA: Dodanie Stride3D.Dense do sygnatury ładowania kerneli, aby pasowało do GpuFluid.cs
        var clearVolumeKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index3D, ArrayView3D<float, Stride3D.DenseXY>
        >(FluidKernels.ClearVolumeKernel);

        var populateVolumeKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView3D<float, Stride3D.DenseXY>, FluidConfig, int, int, int
        >(FluidKernels.PopulateVolumeKernel);

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
        
        // Pełne wielokąty (36 wierzchołków) dla Bounding Boxa jako bryły 3D do raymarchingu
        float[] solidBoxVertices = {
            // Tył
            -3.5f, -3.0f, -4.0f,  3.5f, -3.0f, -4.0f,  3.5f,  3.0f, -4.0f,  3.5f,  3.0f, -4.0f, -3.5f,  3.0f, -4.0f, -3.5f, -3.0f, -4.0f,
            // Przód
            -3.5f, -3.0f,  4.0f,  3.5f, -3.0f,  4.0f,  3.5f,  3.0f,  4.0f,  3.5f,  3.0f,  4.0f, -3.5f,  3.0f,  4.0f, -3.5f, -3.0f,  4.0f,
            // Lewo
            -3.5f,  3.0f,  4.0f, -3.5f,  3.0f, -4.0f, -3.5f, -3.0f, -4.0f, -3.5f, -3.0f, -4.0f, -3.5f, -3.0f,  4.0f, -3.5f,  3.0f,  4.0f,
            // Prawo
             3.5f,  3.0f,  4.0f,  3.5f,  3.0f, -4.0f,  3.5f, -3.0f, -4.0f,  3.5f, -3.0f, -4.0f,  3.5f, -3.0f,  4.0f,  3.5f,  3.0f,  4.0f,
            // Dół
            -3.5f, -3.0f, -4.0f,  3.5f, -3.0f, -4.0f,  3.5f, -3.0f,  4.0f,  3.5f, -3.0f,  4.0f, -3.5f, -3.0f,  4.0f, -3.5f, -3.0f, -4.0f,
            // Góra
            -3.5f,  3.0f, -4.0f,  3.5f,  3.0f, -4.0f,  3.5f,  3.0f,  4.0f,  3.5f,  3.0f,  4.0f, -3.5f,  3.0f,  4.0f, -3.5f,  3.0f, -4.0f
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

        // Alokacja buforów cząsteczek na GPU
        using MemoryBuffer1D<GpuParticle, Stride1D.Dense> gpuParticlesBuffer = accelerator.Allocate1D<GpuParticle>(particleAmount);
        gpuParticlesBuffer.CopyFromCPU(hostParticles);
        ArrayView<GpuParticle> mainBufferView = gpuParticlesBuffer.View;

        // --- ALOKACJA STRUKTUR DLA TEKSTURY WOLUMETRYCZNEJ (ILGPU) ---
        using MemoryBuffer3D<float, Stride3D.DenseXY> gpuVolumeBuffer = accelerator.Allocate3DDenseXY<float>(new Index3D(volumeResX, volumeResY, volumeResZ));
        float[] hostVolumeGrid = new float[volumeResX * volumeResY * volumeResZ];

        // --- INICJALIZACJA SHADERA RAYMARCHINGU ---
        VolumeShader volumeShader = new VolumeShader();
        volumeShader.Setup();
        
        GL.ClearColor(0.02f, 0.02f, 0.04f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        
        // Aktywacja Blendingu (niezbędne do przezroczystości w Raymarchingu)
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // VAO i VBO dla Sześcianu Wolumetrycznego (Raymarching Box)
        int boundVaoSolid = GL.GenVertexArray();
        int boundVboSolid = GL.GenBuffer();
        
        GL.BindVertexArray(boundVaoSolid);
        GL.BindBuffer(BufferTarget.ArrayBuffer, boundVboSolid);
        GL.BufferData(BufferTarget.ArrayBuffer, solidBoxVertices.Length * sizeof(float), solidBoxVertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(0);

        // --- POPRAWKA: Zmiana z Texture3D na Texture3d ---
        int volumeTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture3d, volumeTexture);
        GL.TexImage3D(TextureTarget.Texture3d, 0, InternalFormat.R32f, volumeResX, volumeResY, volumeResZ, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);
        
        GL.TexParameteri(TextureTarget.Texture3d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameteri(TextureTarget.Texture3d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameteri(TextureTarget.Texture3d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameteri(TextureTarget.Texture3d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameteri(TextureTarget.Texture3d, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        Stopwatch stopwatch = new Stopwatch();
        Stopwatch frameTimer = new Stopwatch();
        stopwatch.Start();
        float lastTime = 0f;
        float titleUpdateTimer = 0f;
        
        Index1D gridExtent = new Index1D(particleAmount);
        Index3D volumeExtent = new Index3D(volumeResX, volumeResY, volumeResZ);

        // Reużywalna tablica synchronizacyjna dla siatki przestrzennej na CPU
        GpuParticle[] tempCpuSyncArray = new GpuParticle[particleAmount];

        while (true)
        {
            float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
            float dt = currentTime - lastTime;
            lastTime = currentTime;
            if (dt > 0.05f) dt = 0.05f;

            frameTimer.Restart();

            // --- KROK 1: ŚCIĄGNIĘCIE DANYCH DO PRZELICZENIA SIATKI ---
            mainBufferView.CopyToCPU(tempCpuSyncArray);

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

            // Uruchomienie fizyki płynu
            densityKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config);
            positionKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config, dt);
            
            // --- GENEROWANIE POLA GĘSTOŚCI DLA RAYMARCHINGU (GPU) ---
            clearVolumeKernel(volumeExtent, gpuVolumeBuffer.View);
            populateVolumeKernel(gridExtent, mainBufferView, gpuVolumeBuffer.View, config, volumeResX, volumeResY, volumeResZ);

            accelerator.Synchronize();
            
            // Pobranie wygenerowanej siatki voxelowej gęstości do RAM...
            gpuVolumeBuffer.View.BaseView.CopyToCPU(hostVolumeGrid);

            // --- POPRAWKA: Zmiana z Texture3D na Texture3d ---
            GL.BindTexture(TextureTarget.Texture3d, volumeTexture);
            GL.TexSubImage3D(TextureTarget.Texture3d, 0, 0, 0, 0, volumeResX, volumeResY, volumeResZ, PixelFormat.Red, PixelType.Float, hostVolumeGrid);

            // --- RENDEROWANIE WOLUMETRYCZNE ---
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Aby móc renderować wnętrze kostki, kiedy kamera w nią wjedzie, wyłączamy wycinanie ścian (Culling)
            GL.Disable(EnableCap.CullFace);

            volumeShader.Use();
            GL.BindVertexArray(boundVaoSolid);
            
            GL.UniformMatrix4f(GL.GetUniformLocation(volumeShader.Id, "projection"), 1, false, camera.Projection);
            GL.UniformMatrix4f(GL.GetUniformLocation(volumeShader.Id, "view"), 1, false, camera.View);
            
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4f(GL.GetUniformLocation(volumeShader.Id, "model"), 1, false, ref identity);

            // Przekazanie wektorów pozycji i granic świata
            GL.Uniform3f(GL.GetUniformLocation(volumeShader.Id, "cameraPos"), camera.Position.X, camera.Position.Y, camera.Position.Z);
            GL.Uniform3f(GL.GetUniformLocation(volumeShader.Id, "boxMin"), box.MinX, box.MinY, box.MinZ);
            GL.Uniform3f(GL.GetUniformLocation(volumeShader.Id, "boxMax"), box.MaxX, box.MaxY, box.MaxZ);

            // --- POPRAWKA: Zmiana z Texture3D na Texture3d ---
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture3d, volumeTexture);
            GL.Uniform1i(GL.GetUniformLocation(volumeShader.Id, "volumeTex"), 0);

            // Rysowanie sześcianu jako pełnej bryły wolumetrycznej
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);

            frameTimer.Stop();
            double frameTimeMs = frameTimer.Elapsed.TotalMilliseconds;
            double instantFps = frameTimeMs > 0.0 ? 1000.0 / frameTimeMs : 9999.0;

            titleUpdateTimer += dt;
            if (titleUpdateTimer >= 0.1f)
            {
                Toolkit.Window.SetTitle(window, $"Volume Frame Total: {frameTimeMs:F2} ms | FPS: {instantFps:F0}");
                titleUpdateTimer = 0f;
            }
            
            Toolkit.OpenGL.SwapBuffers(context);

            // --- Obsługa Sterowania (Poprawione Osie Ruchu) ---
            Vector3 moveDirection = Vector3.Zero;
            if (keysPressed[Scancode.W]) moveDirection += new Vector3(0f, 0f, 1f);  // Przód
            if (keysPressed[Scancode.S]) moveDirection += new Vector3(0f, 0f, -1f); // Tył
            if (keysPressed[Scancode.D]) moveDirection += new Vector3(1f, 0f, 0f);  // Prawo
            if (keysPressed[Scancode.A]) moveDirection += new Vector3(-1f, 0f, 0f); // Lewo
            if (keysPressed[Scancode.Q]) moveDirection += new Vector3(0f, 1f, 0f);  // Góra
            if (keysPressed[Scancode.E]) moveDirection += new Vector3(0f, -1f, 0f); // Dół

            if (moveDirection != Vector3.Zero) camera.Move(moveDirection * (4.0f * dt));

            Toolkit.Window.ProcessEvents(false);
            if (Toolkit.Window.IsWindowDestroyed(window)) break;
        }

        GL.DeleteBuffer(boundVboSolid);
        GL.DeleteVertexArray(boundVaoSolid);
        // POPRAWKA: Zmiana z Texture3D na Texture3d przy usuwaniu
        GL.DeleteTexture(volumeTexture);
    }
}