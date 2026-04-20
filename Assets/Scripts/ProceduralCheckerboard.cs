using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class ProceduralCheckerboard : MonoBehaviour
{
    [Header("Texture Size")]
    [Min(2)] public int width = 512;
    [Min(2)] public int height = 512;

    [Header("Pattern")]
    [Min(1)] public int tileSize = 32;
    public Color colorA = Color.white;
    public Color colorB = Color.black;

    [Header("Material Target")]
    [Tooltip("Texture property name on the material")]
    public string textureProperty = "_MainTex";

    Texture2D tex;
    Renderer rend;

    void OnEnable()
    {
        rend = GetComponent<Renderer>();
        Regenerate();
    }

    void OnDisable()
    {
        if (tex != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(tex);
            else Destroy(tex);
#else
            Destroy(tex);
#endif
            tex = null;
        }
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        Regenerate();
    }

    void Regenerate()
    {
        if (width <= 0 || height <= 0 || tileSize <= 0) return;

        if (tex == null || tex.width != width || tex.height != height)
        {
            if (tex != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(tex);
                else Destroy(tex);
#else
                Destroy(tex);
#endif
            }
            tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = "ProceduralCheckerboard";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point; // crisp edges
            tex.hideFlags = HideFlags.DontSave;
        }

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            int ty = y / tileSize;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int tx = x / tileSize;
                bool a = ((tx + ty) & 1) == 0;
                pixels[row + x] = a ? (Color32)colorA : (Color32)colorB;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        if (rend != null && !string.IsNullOrEmpty(textureProperty))
        {
            // In play mode, use material (instanced). In edit mode, use sharedMaterial.
            var mat = Application.isPlaying ? rend.material : rend.sharedMaterial;
            if (mat != null && mat.HasProperty(textureProperty))
            {
                mat.SetTexture(textureProperty, tex);
            }
        }
    }
}