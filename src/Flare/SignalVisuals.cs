using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Flare;

// A camera-relative proxy preserves angular size and direction beyond farClipPlane.
// Rendered scene depth preserves openings in foliage. There
// is no map pin, GUI marker, minimum pixel size, or dependency on a loaded ZDO.
internal sealed class SignalVisuals : IDisposable
{
    private readonly SignalNetwork network;
    private readonly SignalSmoke smoke = new();
    private Mesh? quad;
    private Material? material;
    private Texture2D? glow;
    private readonly MaterialPropertyBlock properties = new();
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private bool failed;
    private readonly Dictionary<string, Light> lights = new();
    internal SignalVisuals(SignalNetwork network)
    {
        this.network = network;
        Camera.onPreCull += Render;
    }
    // Real illumination lives at world coordinates, independently of the glow's
    // visibility and camera-relative drawing proxy. Never run on headless servers.
    internal void Update()
    {
        double now = network.Now;
        bool graphical = Player.m_localPlayer && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        foreach (var id in lights.Keys.ToArray())
        {
            if (!graphical || !network.Views.TryGetValue(id, out var view) || !view.Signal.Alive(now) || view.CollisionReported)
            { if (lights[id]) UnityEngine.Object.Destroy(lights[id].gameObject); lights.Remove(id); }
        }
        if (!graphical) return;
        var settings = network.Lighting;
        foreach (var pair in network.Views)
        {
            var view = pair.Value;
            if (!view.Signal.Alive(now) || view.CollisionReported) continue;
            if (!lights.TryGetValue(pair.Key, out var light))
            {
                var go = new GameObject("norskit_flare_light");
                light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, .63f, .28f);
                light.renderMode = LightRenderMode.ForcePixel;
                light.shadowStrength = 1;
                lights.Add(pair.Key, light);
            }
            var signal = view.Signal;
            light.transform.position = SignalNetwork.Position(signal, now);
            light.enabled = settings.Radius > 0 && settings.Intensity > 0;
            light.range = Mathf.Max(.01f, settings.Radius);
            float flicker = .96f + .04f * Mathf.Sin((float)(now - signal.Start) * 19 + signal.X);
            light.intensity = settings.Intensity * LightingSettings.Fade(signal.End - now) * flicker;
            light.shadows = Plugin.Instance.LightShadows.Value ? LightShadows.Soft : LightShadows.None;
        }
    }
    private bool Initialize()
    {
        if (material) return true;
        if (failed || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
        var shader = Shader.Find("Sprites/Default");
        if (!shader)
        {
            failed = true;
            Plugin.Instance.Log("Flare rendering unavailable: Sprites/Default shader missing.");
            return false;
        }
        glow = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var pixels = new Color[64 * 64];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float r = new Vector2((x - 31.5f) / 31.5f, (y - 31.5f) / 31.5f).magnitude;
            float alpha = r < 1 ? Mathf.Pow(1 - r * r, 3) : 0;
            pixels[y * 64 + x] = new Color(1, 1, 1, alpha);
        }
        glow.SetPixels(pixels); glow.Apply(true, true);
        material = new Material(shader) { mainTexture = glow, renderQueue = 3000 };
        quad = new Mesh { name = "Flare billboard" };
        quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; quad.RecalculateBounds();
        return true;
    }

    private void Render(Camera camera)
    {
        if (camera != Utils.GetMainCamera()) return;
        if (!Player.m_localPlayer || network.Views.Count == 0 || !Initialize()) return;
        smoke.Render(camera, network, material!, glow!);
        double now = network.Now;
        foreach (var view in network.Views.Values)
        {
            var signal = view.Signal;
            if (!signal.Alive(now) || view.CollisionReported) continue;
            var position = SignalNetwork.Position(signal, now);
            var delta = position - camera.transform.position;
            float distance = delta.magnitude;
            var viewport = camera.WorldToViewportPoint(position);
            if (distance < .2f || viewport.z <= 0 || viewport.x < -.05f || viewport.x > 1.05f || viewport.y < -.05f || viewport.y > 1.05f) continue;
            float age = (float)(now - signal.Start);
            bool rising = age < signal.Apex;
            float fade = Mathf.Clamp01((float)(signal.End - now) / 3);
            float atmosphere = Atmosphere(distance);
            float attenuation = 1 / (1 + distance / 6000f);
            float flicker = .92f + .08f * Mathf.Sin(age * 19 + signal.X);
            float alpha = fade * atmosphere * attenuation * network.Lighting.Glow;
            if (alpha < .001f) continue;
            Draw(camera, position, rising ? 2.5f : 7f, new Color(1, .34f, .035f, .6f * alpha * flicker));
            Draw(camera, position, rising ? .5f : 1.8f, new Color(1, .87f, .34f, alpha));

            float detail = Mathf.Clamp01(1 - distance / 500);
            if (detail <= 0) continue;
            if (rising)
            {
                for (int i = 1; i <= 7; i++)
                {
                    double past = Math.Max(signal.Start, now - i * .055);
                    Draw(camera, SignalNetwork.Position(signal, past), .2f + .15f * i,
                        new Color(1, .5f, .08f, alpha * detail * (1 - i / 8f)));
                }
            }
            else
            {
                // Nearby embers complement the persistent smoke trail.
                for (int i = 0; i < 8; ++i)
                {
                    float spark = Mathf.Repeat(age * .9f + i / 8f, 1);
                    Draw(camera, position + new Vector3(Mathf.Sin(i * 8) * spark, -spark * 2, Mathf.Cos(i * 8) * spark), .12f,
                        new Color(1, .6f, .08f, alpha * detail * (1 - spark)));
                }
            }
        }
    }

    internal static float Atmosphere(float distance)
    {
        if (!RenderSettings.fog) return 1;
        // Emissive light penetrates farther than unlit scenery, but fog still
        // attenuates it using the observer's current weather and actual distance.
        float optical = RenderSettings.fogDensity * distance * .12f;
        switch (RenderSettings.fogMode)
        {
            case FogMode.Linear:
                return Mathf.Clamp01((RenderSettings.fogEndDistance - distance) /
                    Mathf.Max(1, RenderSettings.fogEndDistance - RenderSettings.fogStartDistance));
            case FogMode.ExponentialSquared: return Mathf.Exp(-optical * optical);
            default: return Mathf.Exp(-optical);
        }
    }

    private void Draw(Camera camera, Vector3 actual, float size, Color color)
    {
        Vector3 delta = actual - camera.transform.position;
        // Scaling displacement AND size by the same factor preserves perspective
        // and parallax at every range; the camera's far plane is never changed.
        float depth = Vector3.Dot(delta, camera.transform.forward);
        if (depth <= camera.nearClipPlane) return;
        float ratio = Mathf.Min(1, camera.farClipPlane * .8f / depth);
        var proxy = camera.transform.position + delta * ratio;
        color.a = Mathf.Clamp01(color.a);
        properties.SetColor(ColorId, color);
        Graphics.DrawMesh(quad, Matrix4x4.TRS(proxy, camera.transform.rotation, Vector3.one * (size * ratio)), material,
            0, camera, 0, properties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
    }

    public void Dispose()
    {
        Camera.onPreCull -= Render;
        smoke.Dispose();
        foreach (var light in lights.Values) if (light) UnityEngine.Object.Destroy(light.gameObject);
        lights.Clear();
        if (quad) UnityEngine.Object.Destroy(quad);
        if (material) UnityEngine.Object.Destroy(material);
        if (glow) UnityEngine.Object.Destroy(glow);
    }
}
