using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 模型选择菜单：列出内置模型，点击即切换。
/// 与其它面板一样，按钮由手柄射线点击（Owns/Click），面板按钮优先于桌宠。
/// </summary>
public class VRModelMenu : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public Font fontOverride;

    [Header("布局")]
    public Vector3 menuPosition = new Vector3(1.05f, 1.45f, 0.95f);
    public float canvasScale = 0.0016f;
    public int maxRows = 6;

    [Header("显示位置（每次打开都按当前视线重新摆放，转头后也能立刻找到）")]
    public float frontDistance = 1.45f;   // 正前方距离（米）
    public float sideOffset = -0.70f;     // 左右偏移：正数=视线左前方，负数=右前方（与主菜单并排，避免重叠）
    public float heightOffset = 1.35f;    // 高度

    private Canvas canvas;
    private Text statusText;
    private RectTransform listRoot;
    private readonly Dictionary<Collider, Action> listButtons = new Dictionary<Collider, Action>();
    private readonly Dictionary<Collider, Image> listButtonImages = new Dictionary<Collider, Image>();
    // 常驻按钮（截图/刷新/关闭）单独存放：Refresh() 只清列表，不能连它们一起清掉
    private readonly Dictionary<Collider, Action> fixedButtons = new Dictionary<Collider, Action>();
    private readonly Dictionary<Collider, Image> fixedButtonImages = new Dictionary<Collider, Image>();
    private Collider lastHover;
    private static readonly Color NormalColor = new Color(0.16f, 0.30f, 0.52f, 0.95f);
    private static readonly Color HoverColor = new Color(0.95f, 0.62f, 0.18f, 0.98f);
    private static readonly Color CloseColor = new Color(0.42f, 0.22f, 0.24f, 0.95f);

    private void Start()
    {
        BuildUI();
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = transform.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    // ---------------- 构建 ----------------

    private void BuildUI()
    {
        var go = new GameObject("VRModelMenuCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // 高度 = 标题区 + 列表 + 底部按钮区
        float h = 160f + maxRows * 62f;
        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(560f, h);
        rt.localScale = Vector3.one * canvasScale;
        rt.anchoredPosition = Vector3.zero;

        AddPanel(rt, Vector2.zero, new Vector2(560f, h), new Color(0.07f, 0.08f, 0.11f, 0.90f));
        AddLabel(rt, "选择模型", new Vector2(0f, h * 0.5f - 42f), new Vector2(520, 44), 32, new Color(0.55f, 0.85f, 1f));
        statusText = AddLabel(rt, "", new Vector2(0f, h * 0.5f - 82f), new Vector2(520, 30), 20, new Color(0.8f, 0.9f, 0.7f));

        var listGo = new GameObject("List", typeof(RectTransform));
        listRoot = (RectTransform)listGo.transform;
        listRoot.SetParent(rt, false);
        listRoot.sizeDelta = new Vector2(520, maxRows * 62f);
        listRoot.anchoredPosition = new Vector2(0f, -10f);

        float bottom = -h * 0.5f + 36f;
        AddButton(rt, "关闭", new Vector2(0f, bottom), new Vector2(240, 56), Hide, CloseColor, true);

        transform.position = menuPosition;
    }



    /// <summary>重建列表内容（内置模型）。</summary>
    public void Refresh()
    {
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);
        listButtons.Clear();
        listButtonImages.Clear();
        lastHover = null;

        int row = 0;

        // 内置模型
        if (switcher != null && switcher.modelPrefabs != null)
        {
            for (int i = 0; i < switcher.modelPrefabs.Count && row < maxRows; i++)
            {
                var prefab = switcher.modelPrefabs[i];
                if (prefab == null) continue;
                int captured = i;
                bool isCurrent = switcher.CurrentAvatar != null && switcher.CurrentAvatar.name.StartsWith(prefab.name);
                AddRow(row++, (isCurrent ? "● " : "○ ") + prefab.name, () =>
                {
                    switcher.SwitchTo(captured);
                    SetStatus("已切换到：" + prefab.name);
                    Refresh();
                });
            }
        }

        if (row == 0)
            AddLabel(listRoot, "（没有可用模型）", Vector2.zero, new Vector2(500, 40), 24, Color.gray);
    }

    private void AddRow(int row, string label, Action onClick)
    {
        float y = listRoot.sizeDelta.y * 0.5f - 31f - row * 62f;
        AddButton(listRoot, label, new Vector2(0f, y), new Vector2(500, 56), onClick);
    }

    // ---------------- 运行时接口（手柄射线点击） ----------------

    public bool Owns(Collider col)
    {
        return col != null && (listButtons.ContainsKey(col) || fixedButtons.ContainsKey(col));
    }

    public void Click(Collider col)
    {
        Action a;
        if (fixedButtons.TryGetValue(col, out a)) { a(); return; }
        if (listButtons.TryGetValue(col, out a)) a();
    }

    public void SetHover(Collider col)
    {
        if (lastHover == col) return;

        Image img;
        if (lastHover != null)
        {
            if (fixedButtonImages.TryGetValue(lastHover, out img)) img.color = NormalColor;
            else if (listButtonImages.TryGetValue(lastHover, out img)) img.color = NormalColor;
        }

        lastHover = col;

        if (lastHover != null)
        {
            if (fixedButtonImages.TryGetValue(lastHover, out img)) img.color = HoverColor;
            else if (listButtonImages.TryGetValue(lastHover, out img)) img.color = HoverColor;
        }
    }

    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    public void Show()
    {
        PlaceInFrontOfUser();     // 每次打开都摆到当前视线前方
        gameObject.SetActive(true);
        if (canvas != null) canvas.gameObject.SetActive(true);
        Refresh();
    }

    /// <summary>把面板摆到用户"当前视线"的正前方（水平方向按头显朝向，不跟随俯仰）。</summary>
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

        menuPosition = user + fwd * frontDistance - right * sideOffset + Vector3.up * heightOffset;
        transform.position = menuPosition;
    }

    public void Hide()
    {
        if (canvas != null) canvas.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    public bool IsVisible { get { return gameObject.activeSelf; } }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    // ---------------- UI 基建 ----------------

    private GameObject AddPanel(RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = color;
        return go;
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
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private void AddButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick, Color? color = null, bool persistent = false)
    {
        var go = AddPanel(parent, pos, size, color ?? NormalColor);
        var rt = (RectTransform)go.transform;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var lrt = (RectTransform)labelGo.transform;
        lrt.SetParent(rt, false);
        lrt.sizeDelta = size;
        lrt.anchoredPosition = Vector2.zero;
        var t = labelGo.AddComponent<Text>();
        t.text = label;
        t.font = ResolveFont();
        t.fontSize = 26;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 2f);
        col.isTrigger = true;

        if (persistent)
        {
            fixedButtons[col] = onClick;
            fixedButtonImages[col] = go.GetComponent<Image>();
        }
        else
        {
            listButtons[col] = onClick;
            listButtonImages[col] = go.GetComponent<Image>();
        }
    }

    private Font ResolveFont()
    {
        if (fontOverride != null) return fontOverride;
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }
}
