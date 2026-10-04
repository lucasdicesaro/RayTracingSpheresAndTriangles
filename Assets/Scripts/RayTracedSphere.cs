using UnityEngine;

public class RayTracedSphere : MonoBehaviour
{
    [Header("Material")]
    public Color color = Color.white;
    [ColorUsage(true, true)] public Color emissionColor = Color.black;
    [Range(0f, 50f)] public float emissionStrength = 0f;

    [Range(0f, 1f)] public float smoothness = 0f;

    // New specular controls
    [Range(0f, 1f)] public float specularProbability = 0f;
    [ColorUsage(false)] public Color specularColor = Color.white;
    public RayTracedMesh.MaterialFlag flag;

    // Accessors used by the manager
    public Color Color => color;
    public Color EmissionColor => emissionColor;
    public float EmissionStrength => emissionStrength;
    public float Smoothness => smoothness;
    public float SpecularProbability => specularProbability;
    public Color SpecularColor => specularColor;
    public RayTracedMesh.MaterialFlag Flag => flag;
}
