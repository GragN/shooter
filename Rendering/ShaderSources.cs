namespace Sniper3D;

internal static class ShaderSources
{
    public const string Vertex = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec3 aColor;

        uniform mat4 model;
        uniform mat4 view;
        uniform mat4 projection;

        out vec3 Normal;
        out vec3 WorldPosition;
        out vec3 SurfaceColor;

        void main()
        {
            mat3 normalMatrix = transpose(inverse(mat3(model)));
            Normal = normalMatrix * aNormal;
            WorldPosition = vec3(model * vec4(aPosition, 1.0));
            SurfaceColor = aColor;
            gl_Position = projection * view * model * vec4(aPosition, 1.0);
        }
        """;

    public const string Fragment = """
        #version 330 core
        out vec4 FragColor;

        in vec3 Normal;
        in vec3 WorldPosition;
        in vec3 SurfaceColor;

        uniform vec3 objectColor;
        uniform int useLighting;
        uniform vec3 cameraPosition;

        void main()
        {
            if (useLighting == 0)
            {
                FragColor = vec4(objectColor, 1.0);
                return;
            }

            vec3 n = normalize(Normal);
            vec3 lightDir = normalize(vec3(-0.45, 0.85, 0.35));
            float diffuse = max(dot(n, lightDir), 0.0);
            float brightness = 0.40 + diffuse * 0.60;
            if (useLighting == 2)
            {
                vec3 viewDirection = normalize(cameraPosition - WorldPosition);
                vec3 halfDirection = normalize(lightDir + viewDirection);
                float specular = pow(max(dot(n, halfDirection), 0.0), 40.0);
                FragColor = vec4(objectColor * brightness + vec3(0.32) * specular, 1.0);
                return;
            }
            vec3 baseColor = useLighting == 3 ? SurfaceColor : objectColor;
            FragColor = vec4(baseColor * brightness, 1.0);
        }
        """;
}
