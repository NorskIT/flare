using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Flare;

internal sealed class SignalSmoke : IDisposable
{
    private Mesh? mesh;
    private Material? material;
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color> colors = new();
    private readonly List<int> triangles = new();
    private readonly List<(Vector3 position, float size, Color color, float depth)> puffs = new();

    internal void Render(Camera camera, SignalNetwork network, Material source, Texture2D texture)
    {
        if (network.Lighting.SmokeStrength <= 0) return;
        if (!mesh) { mesh = new Mesh { name = "Flare smoke trail" }; mesh.MarkDynamic(); }
        if (!material) material = new Material(source) { mainTexture = texture, renderQueue = 2999 };
        puffs.Clear(); vertices.Clear(); uvs.Clear(); colors.Clear(); triangles.Clear();
        var origin = camera.transform.position;
        foreach (var view in network.Views.Values.OrderBy(v => (SignalNetwork.Position(v.Signal, network.Now) - origin).sqrMagnitude))
        {
            float distance = Vector3.Distance(origin, SignalNetwork.Position(view.Signal, network.Now));
            foreach (var puff in SmokeTrail.Sample(view.Signal, network.Now, network.Lighting.SmokeLifetime, distance))
            {
                var actual = new Vector3(puff.X, puff.Y, puff.Z);
                var viewport = camera.WorldToViewportPoint(actual);
                if (viewport.z <= camera.nearClipPlane || viewport.x < -.2f || viewport.x > 1.2f || viewport.y < -.2f || viewport.y > 1.2f) continue;
                float ratio = Mathf.Min(1, camera.farClipPlane * .8f / viewport.z);
                var color = Color.Lerp(new Color(.16f, .15f, .14f), new Color(.9f, .35f, .1f), puff.Warmth);
                color.a = Mathf.Clamp01(puff.Opacity * network.Lighting.SmokeStrength * SignalVisuals.Atmosphere(Vector3.Distance(origin, actual)));
                puffs.Add((origin + (actual - origin) * ratio, puff.Size * ratio, color, viewport.z));
                if (puffs.Count >= 4096) break;
            }
            if (puffs.Count >= 4096) break;
        }
        foreach (var puff in puffs.OrderByDescending(p => p.depth))
        {
            int n = vertices.Count;
            var right = camera.transform.right * puff.size * .5f;
            var up = camera.transform.up * puff.size * .5f;
            vertices.Add(puff.position - right - up); vertices.Add(puff.position + right - up);
            vertices.Add(puff.position + right + up); vertices.Add(puff.position - right + up);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
            for (int i = 0; i < 4; i++) colors.Add(puff.color);
            triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 1);
            triangles.Add(n); triangles.Add(n + 3); triangles.Add(n + 2);
        }
        mesh!.Clear();
        if (vertices.Count == 0) return;
        mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, camera, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
    }
    public void Dispose()
    {
        if (mesh) UnityEngine.Object.Destroy(mesh);
        if (material) UnityEngine.Object.Destroy(material);
    }
}
