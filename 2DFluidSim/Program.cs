﻿using OpenTK.Mathematics;
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
    private static int particleAmount = 4000; 
    
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
        Toolkit.Window.SetTitle(window, "3D Fluid Sim - Marching Cubes GPU Mesh");
        GL.Viewport(0, 0, screenWidth, screenHeight);

        // --- Cuda / ILGPU Setup ---
        using var cudaContext = Context.Create(builder => builder.Cuda());
        var device = cudaContext.GetCudaDevice(0);
        using var accelerator = device.CreateAccelerator(cudaContext);
        
        // Kernele Fizyki SPH
        var densityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig
        >(FluidKernels.ComputeDensityKernel);
        
        var positionKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<GpuParticle>, ArrayView<int>, ArrayView<int>, int, FluidConfig, float
        >(FluidKernels.UpdatePositionsKernel);

        // Kernele Marching Cubes
        var scalarFieldGridKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index3D, ArrayView3D<float, Stride3D.DenseXY>, ArrayView<GpuParticle>, FluidConfig, McConfig
        >(MarchingCubesKernels.ComputeScalarFieldKernel);

        var marchingCubesKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index3D, ArrayView3D<float, Stride3D.DenseXY>, ArrayView<McVertex>, ArrayView<int>, ArrayView<int>, McConfig
        >(MarchingCubesKernels.MarchCubesKernel);

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

        using MemoryBuffer1D<GpuParticle, Stride1D.Dense> gpuParticlesBuffer = accelerator.Allocate1D<GpuParticle>(particleAmount);
        gpuParticlesBuffer.CopyFromCPU(hostParticles);

        ArrayView<GpuParticle> mainBufferView = gpuParticlesBuffer.View;

        // --- Konfiguracja i alokacja struktur Marching Cubes ---
        Vector3i mcResolution = new Vector3i(64, 64, 64); 
        McConfig mcConfig = new McConfig {
            GridMin = new Vector3(box.MinX - 0.1f, box.MinY - 0.1f, box.MinZ - 0.1f),
            GridMax = new Vector3(box.MaxX + 0.1f, box.MaxY + 0.1f, box.MaxZ + 0.1f),
            Resolution = mcResolution,
            IsoLevel = 8.5f, 
            VoxelSize = new Vector3(
                (box.MaxX - box.MinX + 0.2f) / (mcResolution.X - 1),
                (box.MaxY - box.MinY + 0.2f) / (mcResolution.Y - 1),
                (box.MaxZ - box.MinZ + 0.2f) / (mcResolution.Z - 1)
            )
        };

        int maxTriangles = mcResolution.X * mcResolution.Y * mcResolution.Z * 5; 
        using var dScalarField = accelerator.Allocate3DDenseXY<float>(new LongIndex3D(mcResolution.X, mcResolution.Y, mcResolution.Z));
        using var dAppendCounter = accelerator.Allocate1D<int>(1);
        using var dOutVertices = accelerator.Allocate1D<McVertex>(maxTriangles * 3);
        using var dTriTable = accelerator.Allocate1D<int>(MarchingCubesTables.TriTable);

        // --- Inicjalizacja OpenGL ---
        FluidSurfaceShader liquidShader = new FluidSurfaceShader();
        liquidShader.Setup();
        BoundShader boundShader = new BoundShader();
        boundShader.Setup();
        
        GL.ClearColor(0.05f, 0.05f, 0.08f, 1.0f);
        GL.Enable(EnableCap.DepthTest);

        // VAO i VBO dla Bounding Boxa
        int boundVao = GL.GenVertexArray();
        int boundVbo = GL.GenBuffer();
        GL.BindVertexArray(boundVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, boundVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, boxVertices.Length * Vector3.SizeInBytes, boxVertices, BufferUsage.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.EnableVertexAttribArray(0);

        // VAO i VBO dla Wygenerowanego Mesha Cieczy (Marching Cubes)
        int liquidVao = GL.GenVertexArray();
        int liquidVbo = GL.GenBuffer();
        GL.BindVertexArray(liquidVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, liquidVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, maxTriangles * 3 * Marshal.SizeOf<McVertex>(), IntPtr.Zero, BufferUsage.StreamDraw);

        // Atrybuty wierzchołka cieczy (0: Pozycja, 1: Normalna)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Marshal.SizeOf<McVertex>(), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Marshal.SizeOf<McVertex>(), 12);
        GL.EnableVertexAttribArray(1);

        Matrix4 identity = Matrix4.Identity;
        int viewUniformBound = GL.GetUniformLocation(boundShader.Id, "view");
        int projectionUniformBound = GL.GetUniformLocation(boundShader.Id, "projection");
        int modelUniformBound = GL.GetUniformLocation(boundShader.Id, "model");
        
        Stopwatch stopwatch = new Stopwatch();
        Stopwatch frameTimer = new Stopwatch();
        stopwatch.Start();
        float lastTime = 0f;
        float titleUpdateTimer = 0f;
        Index1D gridExtent = new Index1D(particleAmount);

        // Reużywalne tablice CPU (Zero-Allocation w pętli)
        GpuParticle[] tempCpuSyncArray = new GpuParticle[particleAmount];
        McVertex[] hostMeshSyncArray = new McVertex[maxTriangles * 3];
        int[] hostCounterArray = new int[1];

        while (true)
        {
            float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
            float dt = currentTime - lastTime;
            lastTime = currentTime;
            if (dt > 0.05f) dt = 0.05f;

            frameTimer.Restart();

            // --- FIZYKA SPH ---
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

            densityKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config);
            positionKernel(gridExtent, mainBufferView, gpuGridParticles.View, gpuCellCounts.View, CELL_MAX_CAPACITY, config, dt);
            
            // --- GENEROWANIE POWIERZCHNI (MARCHING CUBES) ---
            hostCounterArray[0] = 0;
            dAppendCounter.CopyFromCPU(hostCounterArray);

            scalarFieldGridKernel(dScalarField.Extent.ToIntIndex(), dScalarField.View, mainBufferView, config, mcConfig);
            marchingCubesKernel(dScalarField.Extent.ToIntIndex(), dScalarField.View, dOutVertices.View, dAppendCounter.View, dTriTable.View, mcConfig);
            
            // Synchronizacja GPU przed renderowaniem i pobraniem struktury dla następnej klatki
            accelerator.Synchronize();

            // Pobranie danych cząstek na potrzeby generowania struktury przestrzennej w kolejnej klatce
            mainBufferView.CopyToCPU(tempCpuSyncArray);

            // Pobranie wygenerowanej geometrii mesh
            dAppendCounter.CopyToCPU(hostCounterArray);
            int generatedVerticesCount = hostCounterArray[0];

            if (generatedVerticesCount > 0)
            {
                dOutVertices.View.SubView(0, generatedVerticesCount).CopyToCPU(hostMeshSyncArray);
                GL.BindBuffer(BufferTarget.ArrayBuffer, liquidVbo);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, generatedVerticesCount * Marshal.SizeOf<McVertex>(), hostMeshSyncArray);
            }

            // --- RENDEROWANIE ---
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Włączenie przezroczystości dla wody
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Rysowanie powierzchni cieczy
            liquidShader.Use();
            GL.BindVertexArray(liquidVao);
            GL.UniformMatrix4f(GL.GetUniformLocation(liquidShader.Id, "projection"), 1, false, camera.Projection);
            GL.UniformMatrix4f(GL.GetUniformLocation(liquidShader.Id, "view"), 1, false, camera.View);

            if (generatedVerticesCount > 0)
            {
                GL.DrawArrays(PrimitiveType.Triangles, 0, generatedVerticesCount);
            }

            GL.Disable(EnableCap.Blend);

            // Rysowanie Bounding Boxa
            boundShader.Use();
            GL.BindVertexArray(boundVao);
            GL.UniformMatrix4f(projectionUniformBound, 1, false, camera.Projection);
            GL.UniformMatrix4f(viewUniformBound, 1, false, camera.View);
            GL.UniformMatrix4f(modelUniformBound, 1, false, ref identity);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, boxVertices.Length);
            
            frameTimer.Stop();
            double frameTimeMs = frameTimer.Elapsed.TotalMilliseconds;
            double instantFps = frameTimeMs > 0.0 ? 1000.0 / frameTimeMs : 9999.0;

            titleUpdateTimer += dt;
            if (titleUpdateTimer >= 0.1f)
            {
                Toolkit.Window.SetTitle(window, $"Triangles: {generatedVerticesCount / 3} | Total Frame: {frameTimeMs:F2} ms | FPS: {instantFps:F0}");
                titleUpdateTimer = 0f;
            }

            Toolkit.OpenGL.SwapBuffers(context);

            // Obsługa klawiatury (ruch kamery)
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

        GL.DeleteBuffer(liquidVbo);
        GL.DeleteBuffer(boundVbo);
        GL.DeleteVertexArray(liquidVao);
        GL.DeleteVertexArray(boundVao);
    }
}