using UnityEngine;

public static class ShaderHelper
{
    // Initializes or updates the material with the provided shader.
    // - Creates the material once and reuses it across calls to avoid allocations.
    // - If the shader changes, reassigns a new material using the new shader.
    public static void InitMaterial(Shader shader, ref Material material)
    {
        if (shader == null)
        {
            // No shader provided; leave material as-is.
            return;
        }

        if (material == null)
        {
            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return;
        }

        if (material.shader != shader)
        {
            // Recreate material to ensure proper keyword/reset state for the new shader.
            var newMat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            // Optionally copy common properties if desired; for now use fresh material.
            material = newMat;
        }
    }
}
