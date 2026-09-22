using UnityEngine;

/// <summary>
/// 脚下软阴影（blob shadow）。开启透视后场景里没有虚拟地面，
/// 桌宠即使正确站在真实地板上也会显得"悬空"——接触阴影能把它钉在地面上。
/// 贴在桌宠正下方的地板高度，随离地高度缩放/淡化。
/// </summary>
public class VRBlobShadow : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;

    [Header("外观")]
    public float radius = 0.42f;
    [Tooltip("贴地高度偏移，避免与地面 z-fighting")]
    public float groundOffset = 0.006f;
    [Tooltip("阴影最大不透明度")]
    [Range(0f, 1f)] public float maxAlpha = 0.45f;
    [Tooltip("完全淡出的离地高度（米）")]
    public float fadeOutHeight = 1.2f;

    private Transform quad;
    private Material mat;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Start()
    {
        BuildQuad();
    }

    private void BuildQuad()
    {
        var go = new GameObject("BlobShadow");
        go.transform.SetParent(transform, false);

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = BuildDiscMesh(24);

        var mr = go.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        mat = new Material(sh) { mainTexture = BuildRadialTexture(), color = new Color(0f, 0f, 0f, maxAlpha) };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        quad = go.transform;
        quad.rotation = Quaternion.Euler(90f, 0f, 0f);   // 平铺于地面
    }

    private void LateUpdate()
    {
        if (quad == null) return;

        var avatar = switcher != null ? switcher.CurrentAvatar : null;
        if (avatar == null) { quad.gameObject.SetActive(false); return; }
        quad.gameObject.SetActive(true);

        Bounds b = ComputeBounds(avatar);
        float floorY = 0f;                       // Stage/Floor 追踪模式下世界 y=0 即真实地板
        float petScale = switcher != null ? Mathf.Max(0.05f, switcher.CurrentScale) : 1f;
        float height = Mathf.Max(0f, b.min.y - floorY);

        quad.position = new Vector3(b.center.x, floorY + groundOffset, b.center.z);
        float footprint = Mathf.Max(0.05f, radius * 2f * petScale);
        quad.localScale = new Vector3(footprint, footprint, 1f);

        if (mat != null)
        {
            float k = Mathf.Clamp01(1f - height / Mathf.Max(0.01f, fadeOutHeight * petScale));
            mat.SetColor(ColorId, new Color(0f, 0f, 0f, maxAlpha * k));
        }
    }

    private GameObject cacheOwner;
    private Renderer[] cacheRenderers;

    private Bounds ComputeBounds(GameObject go)
    {
        if (cacheOwner != go || cacheRenderers == null)
        {
            cacheOwner = go;
            cacheRenderers = go.GetComponentsInChildren<Renderer>();
        }

        bool first = true;
        Bounds b = default;
        for (int i = 0; i < cacheRenderers.Length; i++)
        {
            var r = cacheRenderers[i];
            if (r == null) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static Mesh BuildDiscMesh(int segments)
    {
        var mesh = new Mesh { name = "BlobShadowDisc" };
        var verts = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            verts[i + 1] = new Vector3(cos * 0.5f, sin * 0.5f, 0f);
            uvs[i + 1] = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);
        }
        for (int i = 0; i < segments; i++)
        {
            int t = i * 3;
            tris[t] = 0;
            tris[t + 1] = (i + 1) % segments + 1;
            tris[t + 2] = i + 1;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D BuildRadialTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                                     // 中心浓、边缘柔和
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
