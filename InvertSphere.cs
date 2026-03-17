// =============================================================================
// InvertSphere.cs
// Purpose: Flips sphere mesh normals so the texture is visible from INSIDE
// Attach to: VideoSphere GameObject
// Project: STAR-VP Quest Pro Data Collection
// Unity: 2022.3 LTS
// =============================================================================

using UnityEngine;

public class InvertSphere : MonoBehaviour
{
    void Start()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("[InvertSphere] No MeshFilter found on this GameObject!");
            return;
        }

        Mesh mesh = meshFilter.mesh;

        // Reverse all triangle winding order (flips normals)
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Swap second and third vertex of each triangle
            int temp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = temp;
        }
        mesh.triangles = triangles;

        // Flip normals
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = -normals[i];
        }
        mesh.normals = normals;

        Debug.Log("[InvertSphere] Sphere normals inverted successfully.");
    }
}
