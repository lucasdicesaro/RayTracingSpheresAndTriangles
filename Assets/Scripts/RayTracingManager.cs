using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    [SerializeField, Min(0)] int MaxBounceCount = 10;
    [SerializeField, Min(1)] int NumRaysPerPixel = 3;
    [SerializeField, Min(0)] float DefocusStrength = 100f;
    [SerializeField, Min(0)] float DivergeStrength = 0.2f;
    [SerializeField, Min(0), InspectorName("Focus Distance")] float FocusDistance = 3.5f;
    [SerializeField] bool Accumulate = true;
    [SerializeField, InspectorName("Update In Edit Mode")] bool updateInEditMode = false;
    [SerializeField, InspectorName("Stop Uploading")] bool stopUploading = false;
    [SerializeField, Min(0), InspectorName("Num Rendered Frames")] int frameIndex;

    [Header("View Settings")]
    [SerializeField, InspectorName("Use Shader in Scene View")] bool useShaderInSceneView;
    [SerializeField, InspectorName("Show Focus Plane")] bool showFocusPlane = true;

    [Header("References")]
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader comparisonShader; // assign Custom/Comparison

    Material rayTracingMaterial;
    Material comparisonMaterial;
    ComputeBuffer sphereBuffer;
    ComputeBuffer trianglesBuffer;
    ComputeBuffer meshInfoBuffer;

    // Accumulation ping-pong
    RenderTexture accumA; // write target
    RenderTexture accumB; // read source

    private bool wasStopUploading = false;
    private int savedFrameIndex = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct RayTracingMaterial
    {
        public Vector4 color;           // RGBA
        public Vector3 emissionColor;   // RGB
        public float emissionStrength;  // scalar
        public float smoothness;        // 0..1
        public float specularProbability; // 0..1
        public Vector3 specularColor;     // RGB
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Sphere
    {
        public Vector3 position;
        public float radius;
        public RayTracingMaterial material;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Triangle
    {
        public Vector3 posA;
        public Vector3 posB;
        public Vector3 posC;
        public Vector3 normalA;
        public Vector3 normalB;
        public Vector3 normalC;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MeshInfo
    {
        public uint firstTriangleIndex;
        public uint numTriangles;
        public Vector3 boundsMin;
        public Vector3 boundsMax;
        public RayTracingMaterial material;
    }

    List<Triangle> cachedTriangles = new List<Triangle>(1024);
    List<MeshInfo> cachedMeshInfos = new List<MeshInfo>(64);
    RayTracedSphere[] cachedSphereComponents;
    RayTracedMesh[] cachedMeshObjects;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Application.isPlaying || updateInEditMode)
        {
            if (Accumulate) { frameIndex++; } else { frameIndex = Time.frameCount; }
        }
        else
        {
            frameIndex = 0;
        }

        // Save frameIndex when pausing
        if (!wasStopUploading && stopUploading)
        {
            savedFrameIndex = frameIndex;
        }

        // Restore frameIndex when resuming
        if (wasStopUploading && !stopUploading)
        {
            frameIndex = savedFrameIndex;
            // Do NOT release accumA/accumB here, so accumulation continues smoothly
        }
        wasStopUploading = stopUploading;
    }

    // Called after each camera (e.g. game or scene camera) has finished rendering into the src texture
    void OnRenderImage(RenderTexture src, RenderTexture target)
    {
        // Use the camera this component is attached to (Main Camera in Game view, Scene Camera in Scene view clone)
        var cam = GetComponent<Camera>();
        bool isSceneView = cam != null && cam.cameraType == CameraType.SceneView;

        if (!isSceneView || useShaderInSceneView)
        {
            // --- ADD THIS BLOCK ---
            if (stopUploading && accumB != null)
            {
                // Just show the last accumulated image, do not update or render anything new
                Graphics.Blit(accumB, target);
                return;
            }
            // --- END BLOCK ---

            ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);
            if (rayTracingMaterial == null)
            {
                Graphics.Blit(src, target); // shader not set; bail out safely
                return;
            }

            // Only create/use comparison material when accumulating
            if (Accumulate && comparisonMaterial == null && comparisonShader != null)
            {
                ShaderHelper.InitMaterial(comparisonShader, ref comparisonMaterial);
            }

            UpdateCameraParams(cam);
            SendSphereParams();
            SendTriangleParams();
            SendEnvironmentParams();

            rayTracingMaterial.SetInt("MaxBounceCount", MaxBounceCount);
            rayTracingMaterial.SetInt("NumRaysPerPixel", NumRaysPerPixel);
            rayTracingMaterial.SetFloat("DefocusStrength", DefocusStrength);
            rayTracingMaterial.SetFloat("DivergeStrength", DivergeStrength);
            rayTracingMaterial.SetFloat("FocusDistance", FocusDistance);
            rayTracingMaterial.SetInt("Frame", frameIndex);

            if (!Accumulate)
            {
                Graphics.Blit(null, target, rayTracingMaterial);
                return;
            }

            // Ensure accumulation buffers
            var desc = src.descriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.sRGB = false;
            desc.colorFormat = RenderTextureFormat.ARGBFloat;
            if (accumA == null || accumA.width != src.width || accumA.height != src.height)
            {
                if (accumA) { accumA.Release(); accumA = null; }
                if (accumB) { accumB.Release(); accumB = null; }
                accumA = new RenderTexture(desc) { filterMode = FilterMode.Bilinear }; accumA.Create();
                accumB = new RenderTexture(desc) { filterMode = FilterMode.Bilinear }; accumB.Create();
                frameIndex = 0;
            }

            // Render current frame
            var currentRT = RenderTexture.GetTemporary(src.descriptor);
            Graphics.Blit(null, currentRT, rayTracingMaterial);

            // Set frame count and explicitly bind Comparison shader inputs
            int previousCount = Mathf.Clamp(frameIndex - 1, 0, 65535);
            comparisonMaterial.SetInt("NumRenderedFrames", previousCount);
            comparisonMaterial.SetTexture("_MainTex", currentRT);    // new render
            comparisonMaterial.SetTexture("_MainTexOld", accumB);

            // Blend into write target
            Graphics.Blit(null, accumA, comparisonMaterial);

            // Ping-pong and output
            var tmp = accumB; accumB = accumA; accumA = tmp;
            Graphics.Blit(accumB, target);
            RenderTexture.ReleaseTemporary(currentRT);
        }
        else
        {
            Graphics.Blit(src, target);
        }
    }

    void UpdateCameraParams(Camera cam)
    {
        float planeHeight = cam.nearClipPlane * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        rayTracingMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }

    void SendSphereParams()
    {
        if (cachedSphereComponents == null || !Application.isPlaying)
            cachedSphereComponents = Object.FindObjectsByType<RayTracedSphere>(FindObjectsSortMode.None);
        var sphereComponents = cachedSphereComponents;

        int count = sphereComponents.Length;
        var data = new Sphere[Mathf.Max(1, count)];
        for (int i = 0; i < count; i++)
        {
            var s = sphereComponents[i];
            var t = s.transform;

            // Use the largest axis as diameter
            float diameter = Mathf.Max(t.lossyScale.x, Mathf.Max(t.lossyScale.y, t.lossyScale.z));
            float radius = 0.5f * diameter;

            data[i] = new Sphere
            {
                position = t.position,
                radius = radius,
                material = new RayTracingMaterial
                {
                    color = new Vector4(s.Color.r, s.Color.g, s.Color.b, s.Color.a),
                    emissionColor = new Vector3(s.EmissionColor.r, s.EmissionColor.g, s.EmissionColor.b),
                    emissionStrength = s.EmissionStrength,
                    smoothness = s.Smoothness,
                    specularProbability = s.SpecularProbability,
                    specularColor = new Vector3(s.SpecularColor.r, s.SpecularColor.g, s.SpecularColor.b)
                }
            };
        }

        // Allocate/resize buffer
        int sphereStride = sizeof(float) * (3 + 1 + (4 + 3 + 1 + 1 + 1 + 3)); // pos(3)+radius(1)+material(13 floats)
        if (sphereBuffer == null || sphereBuffer.count != Mathf.Max(1, count))
        {
            sphereBuffer?.Release();
            sphereBuffer = new ComputeBuffer(Mathf.Max(1, count), sphereStride, ComputeBufferType.Structured);
        }
        if (count > 0)
        {
            sphereBuffer.SetData(data);
        }

        // Bind
        rayTracingMaterial.SetInt("NumSpheres", count);
        rayTracingMaterial.SetBuffer("Spheres", sphereBuffer);
    }

    void SendTriangleParams()
    {
        // Build full world-space geometry (positions + normals + bounds + materials)
        BuildSceneGeometry();

        int triCount = cachedTriangles.Count;
        int triStride = sizeof(float) * 18; // pos(3*3) + normal(3*3)
        if (trianglesBuffer == null || trianglesBuffer.count != Mathf.Max(1, triCount))
        {
            trianglesBuffer?.Release();
            trianglesBuffer = new ComputeBuffer(Mathf.Max(1, triCount), triStride, ComputeBufferType.Structured);
        }
        if (triCount > 0)
        {
            trianglesBuffer.SetData(cachedTriangles);
        }

        int meshCount = cachedMeshInfos.Count;
        // MeshInfo: uint*2 + bounds(3+3 floats) + material(4+3+1+1+1+3 floats)
        int meshStride = sizeof(uint) * 2 + sizeof(float) * (3 + 3) + sizeof(float) * (4 + 3 + 1 + 1 + 1 + 3);
        if (meshInfoBuffer == null || meshInfoBuffer.count != Mathf.Max(1, meshCount))
        {
            meshInfoBuffer?.Release();
            meshInfoBuffer = new ComputeBuffer(Mathf.Max(1, meshCount), meshStride, ComputeBufferType.Structured);
        }
        if (meshCount > 0)
        {
            meshInfoBuffer.SetData(cachedMeshInfos);
        }

        rayTracingMaterial.SetInt("NumMeshes", meshCount);
        rayTracingMaterial.SetBuffer("Triangles", trianglesBuffer);
        rayTracingMaterial.SetBuffer("AllMeshInfo", meshInfoBuffer);
    }

    void BuildSceneGeometry()
    {
        cachedTriangles.Clear();
        cachedMeshInfos.Clear();

        if (cachedMeshObjects == null || !Application.isPlaying)
            cachedMeshObjects = Object.FindObjectsByType<RayTracedMesh>(FindObjectsSortMode.None);
        var meshObjects = cachedMeshObjects;
        for (int i = 0; i < meshObjects.Length; i++)
        {
            var rt = meshObjects[i];
            var mf = rt.meshFilter;
            if (mf == null || mf.sharedMesh == null)
                continue;

            var mesh = mf.sharedMesh;
            var t = mf.transform;

            var verts = mesh.vertices;
            int subMeshCount = mesh.subMeshCount;
            for (int sm = 0; sm < subMeshCount; sm++)
            {
                if (mesh.GetTopology(sm) != MeshTopology.Triangles)
                    continue;

                int startTri = cachedTriangles.Count;
                int triCountForSubmesh = 0;
                Vector3 bmin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 bmax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                var indices = mesh.GetIndices(sm);
                for (int idx = 0; idx + 2 < indices.Length; idx += 3)
                {
                    int i0 = indices[idx];
                    int i1 = indices[idx + 1];
                    int i2 = indices[idx + 2];

                    Vector3 w0 = t.localToWorldMatrix.MultiplyPoint3x4(verts[i0]);
                    Vector3 w1 = t.localToWorldMatrix.MultiplyPoint3x4(verts[i1]);
                    Vector3 w2 = t.localToWorldMatrix.MultiplyPoint3x4(verts[i2]);

                    Vector3 faceN = Vector3.Normalize(Vector3.Cross(w1 - w0, w2 - w0));

                    cachedTriangles.Add(new Triangle
                    {
                        posA = w0, posB = w1, posC = w2,
                        normalA = faceN, normalB = faceN, normalC = faceN
                    });
                    triCountForSubmesh++;

                    bmin = Vector3.Min(bmin, Vector3.Min(w0, Vector3.Min(w1, w2)));
                    bmax = Vector3.Max(bmax, Vector3.Max(w0, Vector3.Max(w1, w2)));
                }

                if (triCountForSubmesh > 0)
                {
                    var elem = (rt.materials != null && rt.materials.Length > 0)
                        ? rt.materials[Mathf.Min(sm, rt.materials.Length - 1)]
                        : default;

                    var mat = new RayTracingMaterial
                    {
                        color = new Vector4(elem.color.r, elem.color.g, elem.color.b, elem.color.a),
                        emissionColor = new Vector3(elem.emissionColor.r, elem.emissionColor.g, elem.emissionColor.b),
                        emissionStrength = elem.emissionStrength,
                        smoothness = elem.smoothness,
                        specularProbability = elem.specularProbability, // now sent
                        specularColor = new Vector3(elem.specularColor.r, elem.specularColor.g, elem.specularColor.b)
                    };

                    cachedMeshInfos.Add(new MeshInfo
                    {
                        firstTriangleIndex = (uint)startTri,
                        numTriangles = (uint)triCountForSubmesh,
                        boundsMin = bmin,
                        boundsMax = bmax,
                        material = mat
                    });
                }
            }
        }
    }

    [Header("Environment Settings")]
    [SerializeField, InspectorName("Enabled")] bool enableEnvironmentLight = false;

    [SerializeField, Tooltip("Directional Light used for the sun. Leave empty to use RenderSettings.sun or the first Directional Light.")]
    Light sunLight;
    [SerializeField] float sunFocus = 256f;     // widen the disc to verify
    [SerializeField] float sunIntensityScale = 1f; // boost if needed

    void SendEnvironmentParams()
    {
        // Sky and ground colors (disabled -> black)
        var zenith = enableEnvironmentLight ? new Color(0.15f, 0.2f, 0.4f, 1f) : Color.black;
        var horizon = enableEnvironmentLight ? new Color(0.5f, 0.6f, 0.8f, 1f) : Color.black;
        var ground = enableEnvironmentLight ? new Color(0.2f, 0.18f, 0.15f, 1f) : Color.black;
        rayTracingMaterial.SetVector("SkyColorZenith", new Vector4(zenith.r, zenith.g, zenith.b, 0f));
        rayTracingMaterial.SetVector("SkyColorHorizon", new Vector4(horizon.r, horizon.g, horizon.b, 0f));
        rayTracingMaterial.SetVector("GroundColor", new Vector4(ground.r, ground.g, ground.b, 0f));

        // Sun direction and intensity
        Light dirLight = sunLight != null ? sunLight : RenderSettings.sun;
        if (dirLight == null)
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional) { dirLight = lights[i]; break; }
            }
        }

        // Use direction TOWARD the sun: -forward
        Vector3 sunDir = dirLight != null ? (-dirLight.transform.forward) : Vector3.down;
        float sunIntensity = enableEnvironmentLight ? ((dirLight != null ? dirLight.intensity : 0f) * sunIntensityScale) : 0f;
        rayTracingMaterial.SetVector("SunLightDirection", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        rayTracingMaterial.SetFloat("SunIntensity", sunIntensity);
        rayTracingMaterial.SetFloat("SunFocus", sunFocus);
    }

    void OnDisable()
    {
        // Safely release ping-pong RTs
        if (accumA != null)
        {
            if (accumA.IsCreated()) accumA.Release();
#if UNITY_EDITOR
            Object.DestroyImmediate(accumA);
#else
            Object.Destroy(accumA);
#endif
            accumA = null;
        }

        if (accumB != null)
        {
            if (accumB.IsCreated()) accumB.Release();
#if UNITY_EDITOR
            Object.DestroyImmediate(accumB);
#else
            Object.Destroy(accumB);
#endif
            accumB = null;
        }

        cachedSphereComponents = null;
        cachedMeshObjects = null;
        sphereBuffer?.Release();
        sphereBuffer = null;
        trianglesBuffer?.Release();
        trianglesBuffer = null;
        meshInfoBuffer?.Release();
        meshInfoBuffer = null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showFocusPlane) return;

        // Draw only when the Scene view is rendering gizmos
        var currentCam = Camera.current;
        if (currentCam == null || currentCam.cameraType != CameraType.SceneView) return;

        var cam = GetComponent<Camera>();
        if (cam == null) return;

        // Plane center and size at FocusDistance along the camera forward
        float halfHeight = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * FocusDistance;
        float halfWidth = halfHeight * cam.aspect;

        var t = cam.transform;
        Vector3 center = t.position + t.forward * FocusDistance;
        Vector3 right = t.right * halfWidth;
        Vector3 up = t.up * halfHeight;

        Vector3 p0 = center - right - up;
        Vector3 p1 = center + right - up;
        Vector3 p2 = center + right + up;
        Vector3 p3 = center - right + up;

        // Green transparent fill + outline
        Color fill = new Color(0f, 1f, 0f, 0.2f);
        Color outline = new Color(0f, 1f, 0f, 0.8f);

        // Add: draw plane only where it is behind nearer geometry
        var prev = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        Handles.DrawSolidRectangleWithOutline(new[] { p0, p1, p2, p3 }, fill, outline);
        Handles.zTest = prev;
    }
    #endif
}
