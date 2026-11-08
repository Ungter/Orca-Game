using UnityEngine;

// Plain URP materials for things the game builds at runtime (the placeholder boat, Al Cachud's
// dark doorway). Shader.Find and GameObject.CreatePrimitive's default material work in the
// editor but not in a player build: a shader no scene material uses is left out of the build
// (Shader.Find returns null), and primitives get the built-in Standard material, which URP
// draws bright pink. These templates live in Resources, so they and the variants they use
// always ship. Each call returns a fresh copy to tint.
public static class PlaceholderMaterials
{
    private const string LitPath = "Placeholders/PlaceholderLit";
    private const string UnlitPath = "Placeholders/PlaceholderUnlit";

    public static Material Lit(Color color) => Create(LitPath, "Universal Render Pipeline/Lit", color);

    // Double-sided.
    public static Material Unlit(Color color) => Create(UnlitPath, "Universal Render Pipeline/Unlit", color);

    private static Material Create(string path, string shaderName, Color color)
    {
        Material template = Resources.Load<Material>(path);
        Material material;
        if (template != null)
        {
            material = new Material(template);
        }
        else
        {
            Debug.LogWarning("[PlaceholderMaterials] Missing Resources/" + path + ".mat; falling back to Shader.Find.");
            Shader shader = Shader.Find(shaderName);
            material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
        material.color = color;
        return material;
    }
}
