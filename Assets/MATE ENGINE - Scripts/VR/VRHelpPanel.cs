using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 操作说明面板（世界空间 uGUI，运行时自建，无需 EventSystem）。
///
/// 分页展示全部操作：手柄按键、移动摆放桌宠、互动（抚摸/跳舞）、菜单各项说明。
/// 配图是"程序化绘制"的示意图（手柄轮廓 + 高亮按键 + 摇杆箭头 + 抚摸示意），
/// 直接画进 Texture2D，不依赖任何美术资源，因此换模型/换场景都不会丢。
///
/// 与主菜单/模型列表互相排斥（同一位置会互相遮挡导致点不到按钮）。
/// </summary>
public class VRHelpPanel : MonoBehaviour
{
    [Header("引用")]
    public VRWorldMenu menu;
    public VRModelMenu modelMenu;

    [Header("字体（构建脚本注入，可空）")]
    public Font fontOverride;

    [Header("布局")]
    public Vector3 panelPosition = new Vector3(1.10f, 1.45f, 0.95f);
    public float canvasScale = 0.0012f;

    [Header("显示位置（每次打开都按当前视线重新摆放，保证随时能找到）")]
    public float frontDistance = 1.55f;   // 正前方距离（米，比主菜单稍远一点，避免贴着）
    public float sideOffset = -0.70f;     // 左右偏移：正数=视线左前方，负数=右前方（与主菜单并排，避免重叠）
    public float heightOffset = 1.32f;    // 高度

    private const float PanelW = 860f;
    private const float PanelH = 660f;

    private Canvas canvas;
    private Text titleText;
    private Text bodyText;
    private Text pageText;
    private Image diagram;
    private Text diagramCaption;

    private int page;                       // 0-based
    private readonly List<Page> pages = new List<Page>();
    private bool visible;

    private readonly Dictionary<Collider, Action> buttons = new Dictionary<Collider, Action>();
    private readonly Dictionary<Collider, Image> buttonImages = new Dictionary<Collider, Image>();
    private readonly Dictionary<Collider, Color> buttonBaseColors = new Dictionary<Collider, Color>();
    private Collider lastHover;

    private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.92f);
    private static readonly Color BtnColor = new Color(0.16f, 0.30f, 0.52f, 0.95f);
    private static readonly Color CloseColor = new Color(0.42f, 0.22f, 0.24f, 0.95f);
    private static readonly Color HoverColor = new Color(0.95f, 0.62f, 0.18f, 0.98f);
    private static readonly Color AccentColor = new Color(0.55f, 0.85f, 1f);

    private class Page
    {
        public string title;
        public string body;
        public Bmp art;
        public string caption;
        public Sprite sprite;       // 懒生成并缓存，避免每次翻页都新建贴图
    }

    private void Start()
    {
        BuildPages();
        BuildUI();
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (canvas == null || !visible) return;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = transform.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    // ---------------- 内容 ----------------

    private void BuildPages()
    {
        pages.Add(new Page
        {
            title = "① 左右手柄分工",
            body = "【左手柄】侧键 Grip：按一下 → 显示 / 隐藏菜单\n" +
                   "【右手柄】侧键 Grip 按住 + 摇杆前后 → 缩小 / 放大\n\n" +
                   "【任一只手】扳机（食指扣的那个键）：\n" +
                   "   指着按钮扣 = 点击按钮\n" +
                   "   指着桌宠扣 = 选中它\n\n" +
                   "射线指到哪个按钮，哪个按钮变橙色 —— 表示\"扣扳机会按到它\"。",
            art = ArtTwoControllers(),
            caption = "橙色=扳机（两只手都能用）　绿色=侧键（左手管菜单，右手管缩放）"
        });

        pages.Add(new Page
        {
            title = "② 搬动桌宠",
            body = "1. 用任一只手指着桌宠，扣一下【扳机】选中它\n" +
                   "2. 保持【同一只手的扳机按住】，转动手柄 → 它跟着你走\n" +
                   "3. 用【同一只手的摇杆】前推 / 后拉 → 推远 / 拉近\n" +
                   "4. 松开【扳机】→ 放下\n\n" +
                   "换手要重新扣一次扳机选中。\n" +
                   "找不到了？点菜单里的【送到我面前】。",
            art = ArtJoystick(),
            caption = "选中后：摇杆前后 = 推远 / 拉近"
        });

        pages.Add(new Page
        {
            title = "③ 放大缩小（右手柄）",
            body = "1. 右手柄：按住【侧键 Grip】不要松开\n" +
                   "2. 同时推右手柄的【摇杆】\n" +
                   "   前推 → 缩小\n" +
                   "   后拉 → 放大\n\n" +
                   "缩放以脚底为基准，不会突然跳到地上或浮到空中。\n" +
                   "左手柄的侧键是开关菜单，不要用它来缩放。",
            art = ArtScale(),
            caption = "右手柄：Grip 按住 + 摇杆前后 = 缩放"
        });

        pages.Add(new Page
        {
            title = "④ 抚摸互动",
            body = "用【任一只手】慢慢靠近它的【头部】或【腰部】，轻轻上下动几下：\n\n" +
                   "   头部 → 摸头动画 + 爱心 / 星辰特效\n" +
                   "   腰部 → 撒娇反应 + 特效\n\n" +
                   "离得越近越容易触发（约 25 厘米内），抚摸时手边会拖出星光。",
            art = ArtPat(),
            caption = "任一只手靠近头部并轻拍"
        });

        pages.Add(new Page
        {
            title = "⑤ 跳舞特效",
            body = "点菜单里的【跳舞 ▶】→ 它开始跳舞，双手拖出音符 / 星光拖尾\n" +
                   "点【停止跳舞】→ 停下并收起特效\n\n" +
                   "切换模型后特效依然可用；\n" +
                   "如果先跳舞再切模型，重新点一次【跳舞 ▶】即可。",
            art = ArtMenuMap(),
            caption = "菜单按钮位置"
        });

        pages.Add(new Page
        {
            title = "⑥ 菜单各项说明",
            body = "【切换模型】打开模型列表，选中立刻换装（位置、朝向、大小保留）\n" +
                   "【跳舞 ▶ / 停止跳舞】开始 / 停止跳舞特效\n" +
                   "【AI 聊天】正在开发中（后续版本开放）\n" +
                   "【送到我面前】把桌宠挪回你正前方\n" +
                   "【隐藏菜单】收起菜单\n" +
                   "【操作说明】就是这一页",
            art = null,
            caption = ""
        });

        pages.Add(new Page
        {
            title = "⑦ 小技巧",
            body = "• 菜单找不到了？按一下【左手柄侧键】，它会重新出现在你视线左前方\n\n" +
                   "• 按钮点不动？先把射线指向它、等它变橙色，再扣扳机\n\n" +
                   "• 面板挡住桌宠时不用挪 —— 射线会优先选中按钮\n\n" +
                   "• 大小调到合适后再搬动，摆放更顺手",
            art = null,
            caption = ""
        });
    }

    // ---------------- 示意图（程序化绘制） ----------------

    private class Bmp
    {
        public int w, h;
        public Color32[] px;

        public Bmp(int width, int height)
        {
            w = width; h = height;
            px = new Color32[width * height];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
        }

        public void Blend(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || c.a <= 0f) return;
            int i = y * w + x;
            Color32 u32 = px[i];
            Color under = u32;
            float a = c.a + under.a * (1f - c.a);
            if (a <= 0.0001f) { px[i] = new Color32(0, 0, 0, 0); return; }
            Color outC = (c * c.a + under * under.a * (1f - c.a)) / a;
            px[i] = new Color(outC.r, outC.g, outC.b, a);
        }

        public Texture2D ToTexture()
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }
    }

    private static Bmp NewTex(int w, int h) { return new Bmp(w, h); }

    private static void FillRect(Bmp t, float x0, float y0, float x1, float y1, Color c)
    {
        int ix0 = Mathf.RoundToInt(Mathf.Min(x0, x1)), ix1 = Mathf.RoundToInt(Mathf.Max(x0, x1));
        int iy0 = Mathf.RoundToInt(Mathf.Min(y0, y1)), iy1 = Mathf.RoundToInt(Mathf.Max(y0, y1));
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
                t.Blend(x, y, c);
    }

    private static void RoundRect(Bmp t, float x0, float y0, float x1, float y1, float r, Color c, bool filled)
    {
        float cx0 = x0 + r, cx1 = x1 - r, cy0 = y0 + r, cy1 = y1 - r;
        for (int y = Mathf.RoundToInt(y0); y <= Mathf.RoundToInt(y1); y++)
        {
            for (int x = Mathf.RoundToInt(x0); x <= Mathf.RoundToInt(x1); x++)
            {
                float dx = x < cx0 ? cx0 - x : (x > cx1 ? x - cx1 : 0f);
                float dy = y < cy0 ? cy0 - y : (y > cy1 ? y - cy1 : 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (filled) { if (d <= r) t.Blend(x, y, c); }
                else if (Mathf.Abs(d - r) <= 1.2f) t.Blend(x, y, c);
            }
        }
        if (filled)
        {
            FillRect(t, cx0, y0, cx1, y1, c);
        }
        else
        {
            FillRect(t, cx0, y0, cx1, y0 + 1f, c);
            FillRect(t, cx0, y1 - 1f, cx1, y1, c);
            FillRect(t, x0, cy0, x0 + 1f, cy1, c);
            FillRect(t, x1 - 1f, cy0, x1, cy1, c);
        }
    }

    private static void Line(Bmp t, float ax, float ay, float bx, float by, float thickness, Color c)
    {
        float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        int steps = Mathf.Max(2, Mathf.CeilToInt(len * 2f));
        for (int i = 0; i <= steps; i++)
        {
            float f = (float)i / steps;
            float x = Mathf.Lerp(ax, bx, f), y = Mathf.Lerp(ay, by, f);
            FillRect(t, x - thickness * 0.5f, y - thickness * 0.5f, x + thickness * 0.5f, y + thickness * 0.5f, c);
        }
    }

    private static void Circle(Bmp t, float cx, float cy, float r, float thickness, Color c, bool filled)
    {
        int ir = Mathf.CeilToInt(r + thickness + 2f);
        for (int y = -ir; y <= ir; y++)
        {
            for (int x = -ir; x <= ir; x++)
            {
                float d = Mathf.Sqrt(x * x + y * y);
                bool hit = filled ? d <= r : Mathf.Abs(d - r) <= thickness;
                if (hit) t.Blend(Mathf.RoundToInt(cx) + x, Mathf.RoundToInt(cy) + y, c);
            }
        }
    }

    private static void Arrow(Bmp t, float ax, float ay, float bx, float by, float thickness, Color c)
    {
        Line(t, ax, ay, bx, by, thickness, c);
        float ang = Mathf.Atan2(by - ay, bx - ax);
        float head = thickness * 4.5f;
        float a1 = ang + Mathf.PI * 0.82f, a2 = ang - Mathf.PI * 0.82f;
        Line(t, bx, by, bx + Mathf.Cos(a1) * head, by + Mathf.Sin(a1) * head, thickness, c);
        Line(t, bx, by, bx + Mathf.Cos(a2) * head, by + Mathf.Sin(a2) * head, thickness, c);
    }

    private static Sprite ToSprite(Bmp b)
    {
        var tex = b.ToTexture();
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Color Body() { return new Color(0.30f, 0.34f, 0.42f, 1f); }
    private static Color Highlight() { return new Color(1.0f, 0.62f, 0.16f, 1f); }
    private static Color Dim() { return new Color(0.55f, 0.60f, 0.70f, 0.85f); }

    /// <summary>两只手柄并排：橙色=扳机（都可用），绿色=侧键（左手=菜单，右手=缩放）。</summary>
    private Bmp ArtTwoControllers()
    {
        var t = NewTex(360, 240);
        var green = new Color(0.35f, 0.85f, 0.55f, 1f);
        var orange = Highlight();

        // ---- 左手柄（左边）：侧键 = 菜单 ----
        RoundRect(t, 40, 55, 110, 195, 32, Body(), true);          // 手柄主体
        Circle(t, 75, 160, 22, 0f, Dim(), false);                  // 摇杆
        RoundRect(t, 55, 118, 95, 145, 11, orange, true);          // 扳机
        RoundRect(t, 18, 78, 42, 112, 9, green, true);             // 侧键
        // 侧键 → 菜单面板（示意"按它开/关菜单"）
        Line(t, 30, 112, 30, 205, 3f, green);
        RoundRect(t, 10, 205, 100, 230, 6, new Color(0.20f, 0.34f, 0.55f, 1f), true);

        // ---- 右手柄（右边）：侧键 + 摇杆 = 缩放 ----
        RoundRect(t, 250, 55, 320, 195, 32, Body(), true);         // 手柄主体
        Circle(t, 285, 160, 22, 2f, Dim(), false);                 // 摇杆
        Arrow(t, 285, 172, 285, 205, 3f, green);
        Arrow(t, 285, 148, 285, 122, 3f, green);
        RoundRect(t, 265, 118, 305, 145, 11, orange, true);        // 扳机
        RoundRect(t, 318, 78, 342, 112, 9, green, true);           // 侧键

        // 分隔线
        Line(t, 180, 40, 180, 225, 1.5f, new Color(1f, 1f, 1f, 0.18f));
        return t;
    }

    /// <summary>摇杆四向箭头：前后 = 推远/拉近。</summary>
    private Bmp ArtJoystick()
    {
        var t = NewTex(360, 240);
        float cx = 180, cy = 120;
        Circle(t, cx, cy, 62, 3f, Dim(), false);
        Circle(t, cx, cy, 14, 0f, Body(), true);
        Arrow(t, cx, cy + 14, cx, cy + 70, 4f, Highlight());
        Arrow(t, cx, cy - 14, cx, cy - 70, 4f, Highlight());
        Arrow(t, cx - 14, cy, cx - 70, cy, 3f, Dim());
        Arrow(t, cx + 14, cy, cx + 70, cy, 3f, Dim());
        return t;
    }

    /// <summary>缩放示意：Grip 按住 + 摇杆前后 = 小/大。</summary>
    private Bmp ArtScale()
    {
        var t = NewTex(360, 240);
        // 左：小
        RoundRect(t, 40, 90, 92, 180, 20, Body(), true);
        // 右：大
        RoundRect(t, 250, 40, 330, 200, 26, Body(), true);
        // 中间箭头
        Arrow(t, 110, 130, 235, 130, 4f, Highlight());
        // Grip 提示（小方块）
        RoundRect(t, 40, 40, 66, 66, 8, new Color(0.35f, 0.85f, 0.55f, 1f), true);
        Line(t, 66, 53, 110, 53, 3f, new Color(0.35f, 0.85f, 0.55f, 1f));
        return t;
    }

    /// <summary>抚摸示意：手柄靠近头部并轻拍。</summary>
    private Bmp ArtPat()
    {
        var t = NewTex(360, 240);
        // 人物：头 + 身体
        Circle(t, 170, 175, 34, 0f, Body(), true);
        RoundRect(t, 140, 40, 200, 145, 26, new Color(0.22f, 0.26f, 0.34f, 1f), true);
        // 抚摸范围（虚线圈）
        Circle(t, 170, 175, 56, 2f, new Color(1f, 0.72f, 0.35f, 0.8f), false);
        // 手柄从右上来
        RoundRect(t, 268, 150, 320, 230, 22, Body(), true);
        Arrow(t, 300, 190, 226, 172, 4f, Highlight());
        // 星光点
        Circle(t, 200, 205, 4, 0f, new Color(1f, 0.85f, 0.4f, 0.9f), true);
        Circle(t, 140, 198, 3, 0f, new Color(1f, 0.85f, 0.4f, 0.9f), true);
        return t;
    }

    /// <summary>菜单按钮位置示意（6 个格子 + 高亮当前页）。</summary>
    private Bmp ArtMenuMap()
    {
        var t = NewTex(360, 240);
        RoundRect(t, 30, 20, 330, 220, 16, new Color(0.12f, 0.14f, 0.19f, 1f), true);
        float bw = 130, bh = 54, gx = 14, gy = 12;
        string[] names = { "切换模型", "跳舞 ▶", "停止跳舞", "AI 聊天", "送到我面前", "隐藏菜单" };
        for (int i = 0; i < 6; i++)
        {
            int col = i % 2, row = i / 2;
            float x0 = 45 + col * (bw + gx);
            float y0 = 150 - row * (bh + gy);
            bool special = (i == 3);
            RoundRect(t, x0, y0, x0 + bw, y0 + bh, 10,
                      special ? new Color(0.45f, 0.35f, 0.55f, 1f) : Body(), true);
        }
        return t;
    }

    // ---------------- UI ----------------

    private void BuildUI()
    {
        var go = new GameObject("VRHelpCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)canvas.transform;
        rt.sizeDelta = new Vector2(PanelW, PanelH);
        rt.localScale = Vector3.one * canvasScale;

        AddPanel(rt, Vector2.zero, new Vector2(PanelW, PanelH), PanelColor);

        titleText = AddLabel(rt, "", new Vector2(0f, 290f), new Vector2(PanelW - 60f, 52f), 32, AccentColor);

        // 示意图（上方居中）
        var artGo = new GameObject("Diagram", typeof(RectTransform), typeof(Image));
        var artRt = (RectTransform)artGo.transform;
        artRt.SetParent(rt, false);
        artRt.sizeDelta = new Vector2(330f, 200f);
        artRt.anchoredPosition = new Vector2(0f, 150f);
        diagram = artGo.GetComponent<Image>();
        diagram.color = Color.white;
        diagram.preserveAspect = true;
        diagram.raycastTarget = false;

        diagramCaption = AddLabel(rt, "", new Vector2(0f, 28f), new Vector2(PanelW - 80f, 36f), 19, new Color(0.75f, 0.85f, 0.75f));

        // 文字区（下方整幅宽度，避免长句折行后被裁掉/溢出面板）
        bodyText = AddLabel(rt, "", new Vector2(0f, -150f), new Vector2(PanelW - 70f, 330f), 22, Color.white);
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;

        // 底部按钮
        float by = -286f;
        AddButton(rt, "◀ 上一页", new Vector2(-300f, by), new Vector2(190, 56), () => { page--; Refresh(); }, BtnColor);
        pageText = AddLabel(rt, "1 / 1", new Vector2(-75f, by), new Vector2(140, 48), 26, Color.white);
        AddButton(rt, "下一页 ▶", new Vector2(130f, by), new Vector2(190, 56), () => { page++; Refresh(); }, BtnColor);
        AddButton(rt, "关闭", new Vector2(330f, by), new Vector2(160, 56), Hide, CloseColor);

        transform.position = panelPosition;
        Refresh();
    }

    private void Refresh()
    {
        if (pages.Count == 0) return;
        page = Mathf.Clamp(page, 0, pages.Count - 1);
        var p = pages[page];
        if (titleText != null) titleText.text = p.title;
        if (bodyText != null) bodyText.text = p.body;
        if (pageText != null) pageText.text = (page + 1) + " / " + pages.Count;
        if (diagram != null)
        {
            if (p.art != null)
            {
                if (p.sprite == null) p.sprite = ToSprite(p.art);
                diagram.gameObject.SetActive(true);
                diagram.sprite = p.sprite;
            }
            else diagram.gameObject.SetActive(false);
        }
        if (diagramCaption != null)
            diagramCaption.text = p.caption ?? "";
    }

    private RectTransform AddPanel(RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    private Text AddLabel(RectTransform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = ResolveFont();
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private void AddButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick, Color color)
    {
        var rt = AddPanel(parent, pos, size, color);
        var labelT = AddLabel(rt, label, Vector2.zero, size, 26, Color.white);
        labelT.gameObject.transform.SetParent(rt, false);

        var col = rt.gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 2f);
        col.isTrigger = true;

        buttons[col] = onClick;
        buttonImages[col] = rt.GetComponent<Image>();
        buttonBaseColors[col] = color;
    }

    private Font ResolveFont()
    {
        if (fontOverride != null) return fontOverride;
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }

    // ---------------- 运行时接口（与 VRWorldMenu 一致） ----------------

    public bool IsVisible { get { return visible; } }

    public bool Owns(Collider col)
    {
        return col != null && buttons.ContainsKey(col);
    }

    public void SetHover(Collider col)
    {
        if (lastHover == col) return;
        Image img;
        if (lastHover != null && buttonImages.TryGetValue(lastHover, out img)) img.color = BaseColor(lastHover);
        lastHover = col;
        if (lastHover != null && buttonImages.TryGetValue(lastHover, out img)) img.color = HoverColor;
    }

    private Color BaseColor(Collider col)
    {
        Color c;
        return col != null && buttonBaseColors.TryGetValue(col, out c) ? c : BtnColor;
    }

    public void Click(Collider col)
    {
        Action a;
        if (buttons.TryGetValue(col, out a) && a != null) a();
    }

    public void Show()
    {
        visible = true;
        page = 0;
        PlaceInFrontOfUser();     // 每次打开都摆到当前视线的正前方，转头后也能立刻看到
        ApplyVisibility();
        Refresh();
    }

    /// <summary>把面板摆到用户"当前视线"的前方（水平方向按头显朝向，不跟随俯仰）。</summary>
    public void PlaceInFrontOfUser()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        else fwd.Normalize();
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
        Vector3 user = new Vector3(cam.transform.position.x, 0f, cam.transform.position.z);

        SetAnchorPosition(user + fwd * frontDistance - right * sideOffset + Vector3.up * heightOffset);
    }

    public void Hide()
    {
        visible = false;
        ApplyVisibility();
    }

    public void Toggle()
    {
        if (visible) Hide(); else Show();
    }

    /// <summary>运行时重新摆放面板（启动时置于用户面前，与聊天面板同位置）。</summary>
    public void SetAnchorPosition(Vector3 pos)
    {
        panelPosition = pos;
        transform.position = pos;
    }

    private void ApplyVisibility()
    {
        if (canvas != null) canvas.gameObject.SetActive(visible);
        if (!visible) SetHover(null);
    }
}
