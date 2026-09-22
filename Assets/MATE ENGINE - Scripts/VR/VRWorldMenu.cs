using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 世界空间菜单（运行时自建 uGUI，无需 EventSystem）：
/// 每个按钮挂 BoxCollider，由 VRPetInteractor 的手柄射线调用 Click()。
/// 中文字体由构建脚本注入（NotoSansSC），为空时回退系统字体。
/// </summary>
public class VRWorldMenu : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public VRDancePlayer dance;
    public VRHelpPanel help;
    public VRPetInteractor interactor;
    public VRBootstrap bootstrap;
    public VRAutoWalk autoWalk;
    public VRModelMenu modelMenu;

    [Header("字体（构建脚本注入，可空）")]
    public Font fontOverride;

    [Header("布局")]
    public Vector3 menuPosition = new Vector3(-1.05f, 1.45f, 0.95f);
    public float canvasScale = 0.0016f;

    [Header("显示位置（每次打开菜单都按当前视线重新摆放，转头后也能立刻找到）")]
    public float frontDistance = 1.35f;   // 正前方距离（米）
    public float sideOffset = 0.60f;      // 左右偏移：正数=视线左前方（主菜单固定在左边）
    public float heightOffset = 1.32f;    // 高度

    private Canvas canvas;
    private Text statusText;
    private readonly Dictionary<Collider, Action> buttons = new Dictionary<Collider, Action>();
    private readonly Dictionary<Collider, Image> buttonImages = new Dictionary<Collider, Image>();
    private static readonly Color NormalColor = new Color(0.16f, 0.30f, 0.52f, 0.95f);
    private static readonly Color HoverColor = new Color(0.95f, 0.62f, 0.18f, 0.98f);

    private bool visible = true;
    private Collider lastHover;

    private void Start()
    {
        BuildUI();
        PlaceInFrontOfUser();     // 初始就摆在视线左前方（不依赖宠物位置）
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        // 面板朝向用户（保持直立）
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = transform.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// 由交互器每帧回写：手柄射线指向哪个按钮就把哪个按钮高亮（null = 无命中）。
    /// 这是唯一的"瞄准"反馈——激光线本身保持常色，避免整条线变色遮挡视线。
    /// </summary>
    public void SetHover(Collider col)
    {
        if (lastHover == col) return;

        Image img;
        if (lastHover != null && buttonImages.TryGetValue(lastHover, out img))
            img.color = NormalColor;

        lastHover = col;

        if (lastHover != null && buttonImages.TryGetValue(lastHover, out img))
            img.color = HoverColor;
    }

    // ---------- 构建 ----------

    private void BuildUI()
    {
        var go = new GameObject("VRMenuCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(560, 480);
        rt.localScale = Vector3.one * canvasScale;
        rt.anchoredPosition = Vector3.zero;

        AddPanel(rt, Vector2.zero, new Vector2(560, 480), new Color(0.07f, 0.08f, 0.11f, 0.88f), 26);

        AddLabel(rt, "MATE ENGINE · PICO", new Vector2(0f, 200f), new Vector2(520, 48), 34, new Color(0.55f, 0.85f, 1f));

        AddButton(rt, "切换模型", new Vector2(-135f, 140f), new Vector2(240, 58), () =>
        {
            if (modelMenu != null)
            {
                // 与说明面板互斥（位置相同，同开会互相遮挡导致点不到）
                if (help != null) help.Hide();
                modelMenu.Toggle();      // 打开模型列表（内置模型）
                if (modelMenu.IsVisible) SetStatus("已打开模型列表");
            }
            else if (switcher != null) switcher.Next(1);
        });
        AddButton(rt, "跳舞 ▶", new Vector2(135f, 140f), new Vector2(240, 58), () =>
        {
            if (dance != null) dance.DanceNext();
        });
        AddButton(rt, "停止跳舞", new Vector2(-135f, 74f), new Vector2(240, 58), () =>
        {
            if (dance != null) dance.DanceStop();
        });
        // AI 聊天：功能已整体移除（代码/配置已删除，后续用新方案重做），菜单入口保留
        AddButton(rt, "AI 聊天", new Vector2(135f, 74f), new Vector2(240, 58), () =>
        {
            if (modelMenu != null) modelMenu.Hide();
            if (help != null) help.Hide();
            SetStatus("AI 聊天功能正在开发中，敬请期待～");
        });
        AddButton(rt, "送到我面前", new Vector2(-135f, 8f), new Vector2(240, 58), () =>
        {
            if (bootstrap != null) bootstrap.LayoutInFrontOfUser();
            else if (interactor != null) interactor.ResetAvatar();
            SetStatus("桌宠已送到你面前");
        });
        AddButton(rt, "隐藏菜单", new Vector2(135f, 8f), new Vector2(240, 58), HideMenu);
        AddButton(rt, "操作说明 ❓", new Vector2(0f, -58f), new Vector2(300, 58), () =>
        {
            if (modelMenu != null) modelMenu.Hide();
            if (help != null)
            {
                help.Toggle();
                SetStatus(help.IsVisible ? "已打开操作说明" : " ");
            }
            else SetStatus("操作说明不可用");
        });

        statusText = AddLabel(rt, "欢迎回来～", new Vector2(0f, -150f), new Vector2(520, 60), 26, new Color(0.9f, 0.9f, 0.9f));

        transform.position = menuPosition;
    }

    private RectTransform AddPanel(RectTransform parent, Vector2 pos, Vector2 size, Color color, float corner)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        img.color = color;
        return rt;
    }

    private Text AddLabel(RectTransform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject("Label_" + text, typeof(RectTransform));
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

    private void AddButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick)
    {
        var rt = AddPanel(parent, pos, size, NormalColor, 10);
        var labelT = AddLabel(rt, label, Vector2.zero, size, 30, Color.white);
        labelT.gameObject.transform.SetParent(rt, false);

        var col = rt.gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 2f);
        col.isTrigger = true;

        buttons[col] = onClick;
        buttonImages[col] = rt.GetComponent<Image>();
    }

    private Font ResolveFont()
    {
        if (fontOverride != null) return fontOverride;
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        return f;
    }

    // ---------- 运行时接口 ----------

    public bool Owns(Collider col)
    {
        return col != null && buttons.ContainsKey(col);
    }

    public void Click(Collider col)
    {
        Action a;
        if (buttons.TryGetValue(col, out a))
        {
            SetStatus(" ");
            a();
        }
    }

    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    /// <summary>运行时重新摆放菜单（启动时置于用户面前）。</summary>
    public void SetAnchorPosition(Vector3 pos)
    {
        menuPosition = pos;
        transform.position = pos;
    }

    public void ToggleMenu()
    {
        visible = !visible;
        if (visible) PlaceInFrontOfUser();     // 每次显示都回到视线左前方，转头后也能立刻找到
        ApplyVisibility();
    }

    /// <summary>把菜单摆到用户"当前视线"的左前方（水平方向按头显朝向，不跟随俯仰）。</summary>
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

    public void HideMenu()
    {
        visible = false;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (canvas != null) canvas.gameObject.SetActive(visible);
        if (!visible) SetHover(null);
    }
}
