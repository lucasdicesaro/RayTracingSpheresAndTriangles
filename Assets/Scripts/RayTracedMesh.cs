using UnityEngine;

public class RayTracedMesh : MonoBehaviour
{
    [System.Serializable]
    public struct MaterialElement
    {
        [ColorUsage(true, true)] public Color color;
        [ColorUsage(true, true)] public Color emissionColor;
        [Range(0f, 50f)] public float emissionStrength;

        [Range(0f, 1f)] public float smoothness;

        // New specular controls
        [Range(0f, 1f)] public float specularProbability; // set to 1 for mirror-like
        [ColorUsage(false, true)] public Color specularColor;
    }

    [Header("Materials")]
    [SerializeField]
    public MaterialElement[] materials;


    [Header("Info")]
    [SerializeField] public MeshRenderer meshRenderer;
    [SerializeField] public MeshFilter meshFilter;
    [SerializeField] public int triangleCount;

    void Awake()
    {
        RefreshRenderers();
    }

    void Start()
    {
    }

    void OnValidate()
    {
        // Ensure updates reflect in editor when values change
        RefreshRenderers();
    }

    void RefreshRenderers()
    {
        meshRenderer = GetComponentInChildren<MeshRenderer>(true);
        meshFilter = GetComponentInChildren<MeshFilter>(true);

        Mesh mesh = meshFilter ? meshFilter.sharedMesh : null;
        triangleCount = mesh ? GetTriangleCount(mesh) : 0;
    }

    int GetTriangleCount(Mesh mesh)
    {
        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var topology = mesh.GetTopology(i);
            if (topology == MeshTopology.Triangles)
            {
                count += (int)mesh.GetIndexCount(i) / 3;
            }
        }

        if (count == 0)
        {
            var tris = mesh.triangles;
            if (tris != null)
            {
                count = tris.Length / 3;
            }
        }

        return count;
    }

    
}
