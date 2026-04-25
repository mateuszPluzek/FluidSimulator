using OpenTK.Mathematics;
using OpenTK.Platform;
using OpenTK.Graphics.OpenGL;

namespace _2DFluidSim;

class Program
{
    public static int screenHeight = 800;
    public static int screenWidth = 1200;
    
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
        
        // --- Setup Code ---
        Shader shader = new Shader(); //shader
        shader.Setup();
        shader.Use();
        GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f); //background
        GL.Enable(EnableCap.DepthTest); //Enables Depth Test for correct rendering
    
        //static point for test
        Vector3 center = (0.0f, 0.0f, 0.0f);
        Vector3[] vertices = GenerateCircle(center, 0.1f);
        
        // --- Vertex Array Object Setup ---
        //VAO (references objects)
        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);
        //VBO (buffer for VAO that stores the actual data)
        int vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsage.StaticDraw);
        //connecting openGL shaders and vbo
        uint position = (uint)GL.GetAttribLocation(shader.Id, "vPosition"); //getting index of the field from OpenGL
        GL.EnableVertexAttribArray(position); //telling openGL that data is coming from VAO
        GL.VertexAttribPointer(position, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        
        // --- Camera Setup ---
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), (float)screenWidth / screenHeight, 0.01f, 100.0f);
        Matrix4 view = Matrix4.LookAt(new Vector3(0, 0, 3), new Vector3(0, 0, 0), new Vector3(0, 1, 0));
        Matrix4 model = Matrix4.Identity;
        //getting field index
        int viewUniform = GL.GetUniformLocation(shader.Id, "view");
        int projectionUniform = GL.GetUniformLocation(shader.Id, "projection");
        int modelUniform = GL.GetUniformLocation(shader.Id, "model");
        
        // --- Main Loop ---
        while (true)
        {
            // --- Loop Code ---
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); //clearing buffer with color
            
            GL.UniformMatrix4f(projectionUniform, 1, true, ref projection);
            GL.UniformMatrix4f(viewUniform, 1, true, ref view);
            GL.UniformMatrix4f(modelUniform, 1, true, ref model);
            
            GL.DrawArrays(PrimitiveType.TriangleFan, 0, vertices.Length); //drawing
            
            Toolkit.OpenGL.SwapBuffers(context); //swap back and front buffers
            //Event Handling
            Toolkit.Window.ProcessEvents(false);
            if (Toolkit.Window.IsWindowDestroyed(window))
            {
                break;
            }
        }
    }

    public static Vector3[] GenerateCircle(Vector3 center, float radius, int segments = 32)
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
}

