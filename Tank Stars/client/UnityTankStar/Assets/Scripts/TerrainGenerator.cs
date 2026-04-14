// Genera el terreny procedural i gestiona la destrucció per impacte
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
    public int   octaves      = 5;    // more octaves = more mountain detail
    public float persistence  = 0.55f; // how much each octave contributes
    public float lacunarity   = 2.1f;  // frequency multiplier per octave

    private float[]           heights;
    private Color             currentGroundColor = Color.white;
    private MeshFilter        meshFilter;
    private PolygonCollider2D polyCollider;

    void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (meshFilter   == null) meshFilter   = GetComponent<MeshFilter>();
        if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();
    }

    // Genera el terreny amb una llavor donada
    public void GenerateTerrain(int seed, string mapType)
    {
        EnsureInit();

        Random.InitState(seed);
        float offset = Random.Range(0f, 10000f);

        // Configure biome settings based on project requirements
        switch (mapType.ToLower())
        {
            case "snow":
                currentGroundColor = new Color(0.9f, 0.95f, 1f);
                maxHeight = 6.5f; noiseScale = 0.45f; break;
            case "grassland":
                currentGroundColor = new Color(0.2f, 0.8f, 0.3f);
                maxHeight = 4.0f; noiseScale = 0.25f; break;
            case "canyon":
                currentGroundColor = new Color(0.8f, 0.4f, 0.2f);
                maxHeight = 7.0f; noiseScale = 0.55f; break;
            case "volcanic":
                currentGroundColor = new Color(0.3f, 0.1f, 0.1f);
                maxHeight = 5.0f; noiseScale = 0.4f; break;
            case "desert":
            default:
                currentGroundColor = new Color(0.9f, 0.7f, 0.3f);
                maxHeight = 5.5f; noiseScale = 0.35f; break;
        }

        heights = new float[columns];
        for (int i = 0; i < columns; i++)
        {
            float xNorm = i / (float)(columns - 1);
            float fbm   = FBM(xNorm * noiseScale * 10f, offset, octaves, persistence, lacunarity);
            heights[i]  = baseHeight + fbm * maxHeight;
        }

        // Clamp so terrain never goes above visible camera area
        for (int i = 0; i < columns; i++)
            heights[i] = Mathf.Clamp(heights[i], baseHeight + 0.3f, baseHeight + maxHeight);

        BuildMesh(currentGroundColor);
        BuildCollider();

        // Assegurar que el material utilitza vertex colors
        var rend = GetComponent<MeshRenderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            rend.material.EnableKeyword("_VERTEX_COLORS");
        }
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
    void BuildMesh(Color col)
    {
        var mesh   = new Mesh();
        var verts  = new Vector3[columns * 2];
        var tris   = new int[(columns - 1) * 6];
        var uvs    = new Vector2[columns * 2];
        var colors = new Color[columns * 2];

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
            
            // Bottom is darker for better depth
            colors[i * 2]     = col;
            colors[i * 2 + 1] = col * 0.4f;
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
        mesh.colors    = colors; // Assign the color array
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

    // Destrueix el terreny en un radi circular al punt d'impacte
    public void DestroyTerrain(Vector2 impactWorld, float radius)
    {
        if (heights == null) return;

        // impactWorld is in world space; convert to terrain local X
        float localImpactX = impactWorld.x - transform.position.x;

        float stepX  = width / (columns - 1);
        float startX = -width / 2f;

        for (int i = 0; i < columns; i++)
        {
            float colX = startX + i * stepX;
            float dx   = Mathf.Abs(colX - localImpactX);
            if (dx > radius) continue;

            float depth  = 1f - (dx / radius);
            // Convert impactWorld.y to local space
            float localImpactY = impactWorld.y - transform.position.y;
            float carved = localImpactY - radius * 1.4f * depth;
            heights[i] = Mathf.Min(heights[i], Mathf.Max(baseHeight + 0.2f, carved));
        }

        BuildMesh(currentGroundColor);
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
}
