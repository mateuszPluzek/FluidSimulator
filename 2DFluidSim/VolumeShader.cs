using OpenTK.Graphics.OpenGL;

namespace _2DFluidSim;

public class VolumeShader
{
    public int Id { get; private set; }

    private string vertexShaderSource = @"
    #version 330 core
    layout (location = 0) in vec3 vPosition;

    uniform mat4 projection;
    uniform mat4 view;
    uniform mat4 model;

    out vec3 localPos; // Pozycja wewnątrz sześcianu [-0.5, 0.5] do mapowania UVW
    out vec3 worldPos;

    void main() 
    {
        localPos = vPosition; 
        vec4 wPos = model * vec4(vPosition, 1.0);
        worldPos = wPos.xyz;
        gl_Position = projection * view * wPos;
    }";

    private string fragmentShaderSource = @"
    #version 330 core
    in vec3 localPos;
    in vec3 worldPos;

    uniform sampler3D volumeTex;
    uniform vec3 cameraPos;
    uniform vec3 boxMin;
    uniform vec3 boxMax;

    out vec4 fragColor;

    // Funkcja wyznaczająca wejście i wyjście promienia z Bounding Boxa
    bool IntersectBox(vec3 ro, vec3 rd, vec3 boxMin, vec3 boxMax, out float t0, out float t1) 
    {
        vec3 invR = 1.0 / (rd + 1e-6);
        vec3 tbot = invR * (boxMin - ro);
        vec3 ttop = invR * (boxMax - ro);

        vec3 tmin = min(tbot, ttop);
        vec3 tmax = max(tbot, ttop);

        float near = max(max(tmin.x, tmin.y), tmin.z);
        float far = min(min(tmax.x, tmax.y), tmax.z);

        t0 = near;
        t1 = far;

        return near < far && far > 0.0;
    }

    void main()
    {
        vec3 ro = cameraPos;
        vec3 rd = normalize(worldPos - cameraPos);

        float tnear, tfar;
        if (!IntersectBox(ro, rd, boxMin, boxMax, tnear, tfar)) {
            discard;
        }

        // Korekcja punktu startowego, jeśli kamera jest w środku płynu
        if (tnear < 0.0) tnear = 0.0; 

        // Parametry Raymarchingu
        const int MAX_STEPS = 128;
        float stepLength = (tfar - tnear) / float(MAX_STEPS);
        vec3 stepDir = rd * stepLength;
        vec3 currentPos = ro + rd * tnear;

        float accumulatedDensity = 0.0;
        vec3 accumulatedColor = vec3(0.0);
        float absorption = 1.2; // Gęstość optyczna cieczy

        for (int i = 0; i < MAX_STEPS; i++)
        {
            // Mapowanie pozycji świata do współrzędnych tekstury 3D [0, 1]
            vec3 uvw = (currentPos - boxMin) / (boxMax - boxMin);

            // Pobranie gęstości z tekstury 3D
            float densitySample = texture(volumeTex, uvw).r;

            if (densitySample > 0.01) 
            {
                // --- KLUCZOWA ZMIANA: MAPOWANIE KOLORU (OD BIELI DO BŁĘKITU) ---
                vec3 foamWhite = vec3(0.95, 0.95, 1.0);  // Kolor dla niskiej gęstości (piana/rozproszenie)
                vec3 deepBlue  = vec3(0.02, 0.12, 0.45); // Kolor dla wysokiej gęstości (głęboka ciecz)
                
                // Kontrola przejścia: 
                // Mnożnik (np. 0.5) decyduje, jak szybko piana przechodzi w głęboki błękit.
                // Im mniejszy mnożnik, tym więcej obszarów pozostanie białych.
                float transitionFactor = clamp(densitySample * 0.5, 0.0, 1.0);
                vec3 liquidColor = mix(foamWhite, deepBlue, transitionFactor);
                
                // Wyliczenie alfy (gęstości optycznej) dla tego konkretnego kroku
                float alpha = densitySample * stepLength * absorption;
                
                // Efekt pochłaniania światła w głębi (ciecz z tyłu ciemnieje)
                vec3 attenuatedColor = liquidColor * alpha * (1.0 - accumulatedDensity);
                
                accumulatedColor += attenuatedColor;
                accumulatedDensity += (1.0 - accumulatedDensity) * alpha;

                // Ograniczenie nieprzezroczystości, aby zachować szklany charakter
                if (accumulatedDensity >= 0.85) { 
                    accumulatedDensity = 0.85;
                    break;
                }
            }

            currentPos += stepDir;
        }

        if (accumulatedDensity == 0.0) discard;

        fragColor = vec4(accumulatedColor, accumulatedDensity);
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