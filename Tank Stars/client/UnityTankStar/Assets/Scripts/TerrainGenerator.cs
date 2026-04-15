// Genera el terreny procedural i gestiona la destrucció per impacte
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Configuració del terreny")]
    public int   columns      = 120;
    public float width        = 22f;
    public float baseHeight   = -5f;
    public float maxHeight    = 5.5f;  // tall mountain peaks
    public float noiseScale   = 0.35f;

    [Header("Multi-Octave FBM (muntanyes)")]
    public int   octaves      = 5;
    public float persistence  = 0.55f;
    public float lacunarity   = 2.1f;

    // ── Flat colour per map type ──────────────────────────────────────────
    // Using solid colours instead of textures: texture UV warps on a height-varying
    // mesh always look off; flat colours are clean, instant, and match the game style.
    private static readonly Dictionary<string, Color> MapColors =
        new Dictionary<string, Color>
    {
        { "desert",    new Color(0.80f, 0.62f, 0.28f) },  // warm sandy tan
        { "snow",      new Color(0.78f, 0.87f, 0.95f) },  // pale ice-blue
        { "grassland", new Color(0.40f, 0.28f, 0.11f) },  // dark earth brown
        { "canyon",    new Color(0.65f, 0.26f, 0.10f) },  // red sandstone
        { "volcanic",  new Color(0.16f, 0.13f, 0.13f) },  // near-black basalt
    };

    // ── Server MAP_PRESETS — used only by LoadServerHeights (multiplayer) ──
    private static readonly Dictionary<string, int[]> MapPresets =
        new Dictionary<string, int[]>
    {
        { "desert",    new[] { 34, 36, 39, 44, 49, 53, 56, 58, 57, 54, 48, 43, 39, 37, 36, 38, 43, 50, 58, 63, 66, 65, 61, 54 } },
        { "snow",      new[] { 52, 55, 60, 65, 69, 71, 68, 63, 57, 52, 50, 49, 51, 55, 61, 68, 74, 78, 76, 71, 64, 58, 54, 51 } },
        { "grassland", new[] { 41, 43, 45, 48, 52, 55, 58, 56, 51, 46, 42, 40, 42, 46, 51, 57, 62, 64, 61, 56, 50, 46, 43, 41 } },
        { "canyon",    new[] { 46, 50, 55, 60, 62, 58, 49, 38, 28, 22, 20, 23, 31, 42, 55, 66, 72, 74, 69, 60, 52, 47, 45, 44 } },
        { "volcanic",  new[] { 39, 42, 46, 52, 60, 70, 78, 82, 74, 61, 48, 39, 35, 37, 45, 57, 68, 76, 73, 64, 53, 46, 41, 38 } },
    };

    // ── Per-biome Perlin FBM parameters for VsAI procedural generation ────
    // Each entry: { heightScale, noiseScale, octaves, persistence, lacunarity }
    // Different seeds produce genuinely different terrain; these params give each
    // biome its own character (flat dunes vs. jagged peaks vs. gentle hills, etc.)
    private static readonly Dictionary<string, float[]> MapNoiseParams =
        new Dictionary<string, float[]>
    {
        { "desert",    new float[] { 3.5f, 0.28f, 4, 0.45f, 2.0f } }, // gentle rolling dunes
        { "snow",      new float[] { 5.5f, 0.38f, 5, 0.55f, 2.2f } }, // tall smooth mountains
        { "grassland", new float[] { 2.5f, 0.20f, 3, 0.40f, 1.8f } }, // low gentle hills
        { "canyon",    new float[] { 5.5f, 0.45f, 5, 0.50f, 2.1f } }, // dramatic deep terrain
        { "volcanic",  new float[] { 6.0f, 0.55f, 6, 0.60f, 2.4f } }, // sharp jagged peaks
    };

    private float[]           heights;
    private MeshFilter        meshFilter;
    private PolygonCollider2D polyCollider;
    private MeshRenderer      meshRenderer;

    // Cached so ApplyMapTypeTexture can be re-called after LoadServerHeights
    private string _currentMapType = "desert";

    void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (meshFilter   == null) meshFilter   = GetComponent<MeshFilter>();
        if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
    }

    // ─── Public API ───────────────────────────────────────────────────────

    // Generates terrain procedurally — every seed produces a genuinely different
    // layout. Map-type-specific FBM params give each biome its own character
    // (gentle desert dunes vs. sharp volcanic peaks, etc.).
    public void GenerateTerrain(int seed, string mapType)
    {
        EnsureInit();

        _currentMapType = NormalizeMapType(mapType);
        Random.InitState(seed);
        float offset = Random.Range(0f, 10000f);

        // Pick FBM params for this biome (fall back to inspector values if unknown)
        float hScale, ns, pers, lac;
        int   oct;
        if (MapNoiseParams.TryGetValue(_currentMapType, out float[] p))
        {
            hScale = p[0]; ns = p[1]; oct = (int)p[2]; pers = p[3]; lac = p[4];
        }
        else
        {
            hScale = maxHeight; ns = noiseScale; oct = octaves; pers = persistence; lac = lacunarity;
        }

        heights = new float[columns];
        for (int i = 0; i < columns; i++)
        {
            float xNorm  = i / (float)(columns - 1);
            float fbm    = FBM(xNorm * ns * 10f, offset, oct, pers, lac);
            heights[i]   = baseHeight + fbm * hScale;
        }

        // Clamp so terrain stays within the visible camera area
        for (int i = 0; i < columns; i++)
            heights[i] = Mathf.Clamp(heights[i], baseHeight + 0.3f, baseHeight + maxHeight);

        BuildMesh();
        BuildCollider();
        ApplyMapTypeTexture(_currentMapType);
    }

    // Builds terrain from server-sent heights (0-100 integer scale, any column count).
    // Interpolates to the local column count and maps values to world Y coordinates.
    // Pass mapType to also update the terrain texture (omit to keep the current one).
    public void LoadServerHeights(int[] serverHeights, string mapType = null)
    {
        if (serverHeights == null || serverHeights.Length == 0) return;
        EnsureInit();

        if (mapType != null)
            _currentMapType = NormalizeMapType(mapType);

        heights = new float[columns];
        for (int i = 0; i < columns; i++)
        {
            float t      = (columns > 1) ? i / (float)(columns - 1) : 0f;
            float srcIdx = t * (serverHeights.Length - 1);
            int   lo     = Mathf.FloorToInt(srcIdx);
            int   hi     = Mathf.Min(lo + 1, serverHeights.Length - 1);
            float h      = Mathf.Lerp(serverHeights[lo], serverHeights[hi], srcIdx - lo);
            heights[i]   = baseHeight + (h / 100f) * maxHeight;
        }

        BuildMesh();
        BuildCollider();

        // Re-apply texture whenever the map type changes or on first load
        if (mapType != null)
            ApplyMapTypeTexture(_currentMapType);
    }

    // Destrueix el terreny en un radi circular al punt d'impacte
    public void DestroyTerrain(Vector2 impactWorld, float radius)
    {
        if (heights == null) return;

        // impactWorld is in world space; convert to terrain local X
        float localImpactX = impactWorld.x - transform.position.x;

        float stepX  = width / (columns - 1);
        float startX = -width / 2f;

        float localImpactY = impactWorld.y - transform.position.y;

        for (int i = 0; i < columns; i++)
        {
            float colX = startX + i * stepX;
            float dx   = Mathf.Abs(colX - localImpactX);
            if (dx > radius) continue;

            // Cosine falloff → smooth rounded bowl instead of sharp V-shape
            float depth  = Mathf.Cos((dx / radius) * Mathf.PI * 0.5f);
            float carved = localImpactY - radius * 0.7f * depth;
            heights[i] = Mathf.Min(heights[i], Mathf.Max(baseHeight + 0.2f, carved));
        }

        BuildMesh();
        BuildCollider();
    }

    // Retorna l'alçada de la superfície del terreny en coordenades del MÓN
    public float GetHeightAtX(float worldX)
    {
        if (heights == null) return transform.position.y + baseHeight;

        // Convert world X to local terrain X
        float localX = worldX - transform.position.x;
        float stepX  = width / (columns - 1);
        float startX = -width / 2f;
        float relX   = localX - startX;
        int   idx    = Mathf.RoundToInt(relX / stepX);
        idx = Mathf.Clamp(idx, 0, columns - 1);

        // Return WORLD Y = terrain world position + local height
        return transform.position.y + heights[idx];
    }

    // ─── Colour ───────────────────────────────────────────────────────────

    // Applies a flat solid colour for the map type.
    // Textures on a height-varying terrain mesh always warp; flat colours are clean.
    public void ApplyMapTypeTexture(string mapType)
    {
        if (meshRenderer == null) return;

        string norm = NormalizeMapType(mapType);
        Color  col  = MapColors.TryGetValue(norm, out Color c) ? c : new Color(0.80f, 0.62f, 0.28f);

        // .material auto-creates a per-instance copy so we don't touch the shared asset
        var mat = meshRenderer.material;
        mat.mainTexture = null;   // clear any previously assigned texture

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     col);
        // Universal fallback (built-in renderer)
        mat.color = col;
    }

    // ─── Internal helpers ─────────────────────────────────────────────────

    private static string NormalizeMapType(string mapType)
    {
        if (string.IsNullOrEmpty(mapType)) return "desert";
        string lower = mapType.ToLower();
        return MapPresets.ContainsKey(lower) ? lower : "desert";
    }

    // Fractional Brownian Motion — stacks multiple noise octaves for mountain look
    private float FBM(float x, float offset, int oct, float pers, float lac)
    {
        float value     = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxVal    = 0f;

        for (int i = 0; i < oct; i++)
        {
            value   += Mathf.PerlinNoise(x * frequency + offset, i * 1.7f) * amplitude;
            maxVal  += amplitude;
            amplitude *= pers;
            frequency *= lac;
        }

        return value / maxVal; // normalize 0..1
    }

    // Construeix el mesh del terreny a partir dels heights
    void BuildMesh()
    {
        var mesh   = new Mesh();
        var verts  = new Vector3[columns * 2];
        var tris   = new int[(columns - 1) * 6];
        var uvs    = new Vector2[columns * 2];

        float stepX  = width / (columns - 1);
        float startX = -width / 2f;
        float bottom = baseHeight - 3f;

        for (int i = 0; i < columns; i++)
        {
            float x          = startX + i * stepX;
            verts[i * 2]     = new Vector3(x, heights[i], 0f);
            verts[i * 2 + 1] = new Vector3(x, bottom,     0f);
            uvs[i * 2]       = new Vector2((float)i / (columns - 1), 1f);
            uvs[i * 2 + 1]   = new Vector2((float)i / (columns - 1), 0f);
        }

        int t = 0;
        for (int i = 0; i < columns - 1; i++)
        {
            int bl = i * 2 + 1, br = (i + 1) * 2 + 1;
            int tl = i * 2,     tr = (i + 1) * 2;
            tris[t++] = bl; tris[t++] = tl; tris[t++] = tr;
            tris[t++] = bl; tris[t++] = tr; tris[t++] = br;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.uv        = uvs;
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
    }

    // Construeix el col·lisionador poligonal
    void BuildCollider()
    {
        float stepX  = width / (columns - 1);
        float startX = -width / 2f;
        float bottom = baseHeight - 3f;

        var points = new Vector2[columns + 2];
        for (int i = 0; i < columns; i++)
            points[i] = new Vector2(startX + i * stepX, heights[i]);
        points[columns]     = new Vector2(startX + width, bottom);
        points[columns + 1] = new Vector2(startX,         bottom);
        polyCollider.SetPath(0, points);
    }
}
