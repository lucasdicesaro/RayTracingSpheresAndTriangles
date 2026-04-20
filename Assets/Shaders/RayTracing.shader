Shader "Custom/RayTracing"
{
    Properties {}

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Parameters set from RayTracingManager
            float4 ViewParams;              // (planeWidth, planeHeight, nearClip, 0)
            float4x4 CamLocalToWorldMatrix; // camera transform

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            struct Ray {
                float3 origin;
                float3 dir;
            };

            struct RayTracingMaterial {
                float4 color;
                float3 emissionColor;
                float emissionStrength;
                float smoothness;
                float specularProbability;
                float3 specularColor;
            };

            struct HitInfo {
                bool didHit;
                float dst;
                float3 hitPoint;
                float3 normal;
                RayTracingMaterial material;
            };

            struct Sphere {
                float3 position;
                float radius;
                RayTracingMaterial material;
            };

            struct Triangle {
                float3 posA, posB, posC;
                float3 normalA, normalB, normalC;
            };

            struct MeshInfo {
                uint firstTriangleIndex;
                uint numTriangles;
                float3 boundsMin;
                float3 boundsMax;
                RayTracingMaterial material;
            };

            struct Environment {
                float4 color;
                float3 emissionColor;
                float emissionStrength;
            };

            StructuredBuffer<Sphere> Spheres;
            int NumSpheres;
            StructuredBuffer<Triangle> Triangles;
            StructuredBuffer<MeshInfo> AllMeshInfo;
            int NumMeshes;
            int MaxBounceCount;
            int NumRaysPerPixel;
            float DefocusStrength;
            float DivergeStrength;
            float FocusDistance;
            uint Frame; // current frame/sample index
            float3 SkyColorZenith;
            float3 SkyColorHorizon;
            float3 GroundColor;
            float3 SunLightDirection;
            float SunIntensity;
            float SunFocus;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Calculate the intersection between a ray and a sphere
            HitInfo RaySphere(Ray ray, float3 sphereCenter, float sphereRadius)
            {
                HitInfo hitInfo = (HitInfo)0;

                float3 offsetRayOrigin = ray.origin - sphereCenter;
                // From the equation: sqrLength(rayOrigin + rayDir * dst) = radius^2
                // Solving for dst results in a quadratic equation with coefficients:
                float a = dot(ray.dir, ray.dir); // a = 1 (assuming unit vector)
                float b = 2.0 * dot(offsetRayOrigin, ray.dir);
                float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;
                // Quadratic discriminant
                float discriminant = b * b - 4 * a * c;

                // No solution when d < 0 (ray misses sphere)
                if (discriminant >= 0)
                {
                    // Distance to nearest intersection point (from quadratic formula)
                    float dst = (-b - sqrt(discriminant)) / (2.0 * a);

                    // Ignore intersections behind the ray origin
                    if (dst > 0)
                    {
                        hitInfo.didHit = true;
                        hitInfo.dst = dst;
                        hitInfo.hitPoint = ray.origin + dst * ray.dir;
                        hitInfo.normal = normalize(hitInfo.hitPoint - sphereCenter);
                    }
                }

                return hitInfo;
            }

            // Calculate the inresetion of a ray with a triangle usung Möller-Trumbore algorithm
            // Thanks to https://stackoverflow.com/a/42752998
            HitInfo RayTriangle(Ray ray, Triangle tri) {
                float3 edgeAB = tri.posB - tri.posA;
                float3 edgeAC = tri.posC - tri.posA;
                float3 normalVector = cross(edgeAB, edgeAC);
                float3 ao = ray.origin - tri.posA;
                float3 dao = cross(ao, ray.dir);
                
                float determinant = -dot(ray.dir, normalVector);
                HitInfo hitInfo = (HitInfo)0;
                if (determinant < 1E-6) return hitInfo;
                float invDet = 1 / determinant;

                // Calculate dst to triangle & barycentric coordinates of intersection point
                float dst = dot(ao, normalVector) * invDet;
                float u = dot(edgeAC, dao) * invDet;
                float v = -dot(edgeAB, dao) * invDet;
                float w = 1 - u - v;

                hitInfo.didHit = dst >= 0 && u >= 0 && v >= 0 && w >= 0;
                hitInfo.hitPoint = ray.origin + ray.dir * dst;
                hitInfo.normal = normalize(tri.normalA * w + tri.normalB * u + tri.normalC * v);
                hitInfo.dst = dst;
                return hitInfo;
            }

            bool RayBoundingBox(Ray ray, float3 boundsMin, float3 boundsMax) {
                float3 invDir = 1.0 / ray.dir;
                float3 t0 = (boundsMin - ray.origin) * invDir;
                float3 t1 = (boundsMax - ray.origin) * invDir;
                float3 tmin3 = min(t0, t1);
                float3 tmax3 = max(t0, t1);
                float tEnter = max(max(tmin3.x, tmin3.y), tmin3.z);
                float tExit = min(min(tmax3.x, tmax3.y), tmax3.z);
                return tExit >= max(tEnter, 0.0);
            }

            // Find the first point that the given ray collides with, and return hit info
            HitInfo CalculateRayCollision(Ray ray) {
                HitInfo closestHit = (HitInfo)0;
                // We haven't hit anything yet, so 'closest' hit is infitely far away
                closestHit.dst = 1.#INF;

                // Raycast against triangle meshes (with bounds culling)
                for (int meshIndex = 0; meshIndex < NumMeshes; meshIndex++) {
                    MeshInfo meshInfo = AllMeshInfo[meshIndex];
                    // Skip meshes if ray doesn't intersect its bounding box
                    if (!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax)) {
                       continue;
                    }

                    for (int bounceIndex = 0; bounceIndex < meshInfo.numTriangles; bounceIndex++) {
                        int triIndex = meshInfo.firstTriangleIndex + bounceIndex;
                        Triangle tri = Triangles[triIndex];
                        HitInfo hitInfo = RayTriangle(ray, tri);
                        if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
                            closestHit = hitInfo;
                            closestHit.material = meshInfo.material;
                        }
                    }
                }

                // Raycast against spheres and keep info about the closest hit
                for (int bounceIndex = 0; bounceIndex < NumSpheres; bounceIndex++) {
                    Sphere sphere = Spheres[bounceIndex];
                    HitInfo hitInfo = RaySphere(ray, sphere.position, sphere.radius);
                    if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
                        closestHit = hitInfo;
                        closestHit.material = sphere.material;
                    }
                }
                return closestHit;
            }

            // PCG (permuted congruential generator). Thanks to:
            // www.pcg-random.org and www.shadertoy.com/view/XlGcRh
            float RandomValue(inout uint state)
            {
                state = state * 747796405u + 2891336453u;
                uint result = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                result = (result >> 22u) ^ result;
                return result * (1.0 / 4294967296.0);
            }

            // Random value in normal distribution (with mean=0 and sd=1)
            float RandomValueNormalDistribution(inout uint state)
            {
                // Thanks to https://stackoverflow.com/a/6178290
                float theta = 2 * 3.14159265 * RandomValue(state);
                float rho = sqrt(-2.0 * log(max(RandomValue(state), 1e-10)));
                return rho * cos(theta);
            }

            // Calculate random direction.
            // Note: there are many alternative methods for computing this,
            // with varying trade-offs between speed and accuracy.
            float3 RandomDirection(inout uint state) {
                // Thanks to https://math.stackexchange.com/a/1585996
                float x = RandomValueNormalDistribution(state);
                float y = RandomValueNormalDistribution(state);
                float z = RandomValueNormalDistribution(state);
                return normalize(float3(x, y, z));
            }

            // Random direction in the hemisphere oriented around the given normal vector
            float3 RandomHemisphereDirection(float3 normal, inout uint rngState) {
                float3 dir = RandomDirection(rngState);
                return dir * sign(dot(normal, dir));
            }

            // Simple background environment lighting
            float3 GetEnvironmentLight(Ray ray)
            {
                float skyGradientT = pow(smoothstep(0, 0.4, ray.dir.y), 0.35);
                float3 skyGradient = lerp(SkyColorHorizon, SkyColorZenith, skyGradientT);
                float sun = pow(max(0, dot(ray.dir, -SunLightDirection)), SunFocus) * SunIntensity;

                // Combine ground, sky, and sun
                float groundToSkyT = smoothstep(-0.01, 0, ray.dir.y);
                float sunMask = groundToSkyT >= 1;
                return lerp(GroundColor, skyGradient, groundToSkyT) + sun * sunMask;
            }

            // Trace the path of a ray of light (in reverse) as it travels from the camera,
            // reflects off objects in the scene, and ends up (hopefully) at a light source.
            float3 Trace(Ray ray, inout uint rngState) {
                float3 incomingLight = 0;
                float3 rayColor = 1;
                
                for (int bounceIndex = 0; bounceIndex <= MaxBounceCount; bounceIndex++){
                    HitInfo hitInfo = CalculateRayCollision(ray);
                    if (hitInfo.didHit) {
                        ray.origin = hitInfo.hitPoint;
                        RayTracingMaterial material = hitInfo.material;
                        bool isSpecularBounce = material.specularProbability >= RandomValue(rngState);
                        //float3 diffuseDir = RandomHemisphereDirection(hitInfo.normal, rngState);
                        float3 diffuseSum = hitInfo.normal + RandomDirection(rngState);
                        float3 diffuseDir = dot(diffuseSum, diffuseSum) < 1e-6 ? hitInfo.normal : normalize(diffuseSum);
                        float3 specularDir = reflect(ray.dir, hitInfo.normal);
                        ray.dir = normalize(lerp(diffuseDir, specularDir, material.smoothness * isSpecularBounce));

                        float3 emittedLight = material.emissionColor * material.emissionStrength;
                        incomingLight += emittedLight * rayColor;
                        rayColor *= lerp(material.color, material.specularColor, isSpecularBounce);
                        if (dot(rayColor, 1) < 0.0001) break; // Early exit if cannot contribute further
                    }
                    else {
                        incomingLight += GetEnvironmentLight(ray) * rayColor;
                        break;
                    }
                }

                return incomingLight;
            }
        
            static const float PI = 3.14159265;

            float2 RandomPointInCircle(inout uint state) {
                float angle = RandomValue(state) * 2.0 * PI;
                float2 pointOnCircle = float2(cos(angle), sin(angle));
                return pointOnCircle * sqrt(RandomValue(state)); // uniform distribution
            }

            // Run for every pixel in the display
            float4 frag(v2f bounceIndex) : SV_Target
            {
                // Create a seed for random number generator
                uint2 numPixels = _ScreenParams.xy;
                uint2 pixelCoord = bounceIndex.uv * numPixels;
                uint pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
                uint rngState = pixelIndex + Frame * 719393u;

                // Create ray
                float3 viewPointLocal = float3(bounceIndex.uv - 0.5, 1) * ViewParams;
                float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1));
                float3 camRight = CamLocalToWorldMatrix._m00_m10_m20;
                float3 camUp = CamLocalToWorldMatrix._m01_m11_m21;

                // Add: compute focal point on the plane at FocusDistance (per pixel)
                float3 camFwd = CamLocalToWorldMatrix._m02_m12_m22;
                float3 camPos = _WorldSpaceCameraPos;
                float3 baseDir = normalize(viewPoint - camPos);
                float denomFocus = max(1e-6, dot(camFwd, baseDir));
                float tFocus = FocusDistance / denomFocus;
                float3 focalPoint = camPos + baseDir * tFocus;

                // Calculate pixel color
                float3 totalIncomingLight = 0;
                for (int rayIndex = 0; rayIndex < NumRaysPerPixel; rayIndex++) {
                    Ray ray;
                    float2 defocusJitter = RandomPointInCircle(rngState) * DefocusStrength / numPixels.x;
                    ray.origin = _WorldSpaceCameraPos + camRight * defocusJitter.x + camUp * defocusJitter.y;
                    float2 jitter = RandomPointInCircle(rngState) * DivergeStrength / numPixels.x;

                    // Aim ray to focal plane with small focal jitter (focus control)
                    float3 focalJitter = camRight * jitter.x + camUp * jitter.y;
                    //float3 jitteredViewPoint = viewPoint + focalJitter;
                    float3 jitteredViewPoint = focalPoint + focalJitter;
                    ray.dir = normalize(jitteredViewPoint - ray.origin);

                    float3 incomingLight = Trace(ray, rngState);
                    totalIncomingLight += incomingLight;
                }
                float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
                return float4(pixelCol, 1);
            }
            ENDCG
        }
    }
}
