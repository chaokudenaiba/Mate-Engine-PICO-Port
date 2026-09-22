using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

/// <summary>
/// VR 模型切换器：A/主键 切换下一个模型，B/次键 切换上一个模型。
/// 同时监听 Input System XRController 与旧输入 JoystickButton（双通道，保证 PICO 手柄可触发）。
/// </summary>
public class VRModelSwitcher : MonoBehaviour
{
    [Tooltip("可切换的模型 prefab 列表（顺序即切换顺序）")]
    public List<GameObject> modelPrefabs = new List<GameObject>();

    [Tooltip("模型实例化后挂载的 AnimatorController（Idle 等状态）")]
    public RuntimeAnimatorController animatorController;

    [Tooltip("模型实例的父节点（自动创建）")]
    public Transform spawnParent;

    [Tooltip("模型出生位置（相对父节点）")]
    public Vector3 spawnPosition = new Vector3(0f, 0f, 2.2f);

    [Tooltip("模型缩放（1 = 真人比例，可调小做桌宠比例）")]
    public float spawnScale = 1f;

    [Tooltip("脚底与地面的间隙（自动落地用）")]
    public float floorClearance = 0.01f;
    [Tooltip("脚骨到脚底的高度差（米，按模型缩放换算）")]
    public float soleOffset = 0.06f;

    [Header("缩放（右手柄侧握键 Grip + 摇杆前后）")]
    [Tooltip("最小缩放（真人比例 1 为基准）")]
    public float minScale = 0.15f;
    [Tooltip("最大缩放")]
    public float maxScale = 3.0f;
    [Tooltip("摇杆满推时每秒的缩放倍率（乘性，1.2 ≈ 每秒 ±20%）")]
    public float scaleSpeed = 1.2f;

    private GameObject current;
    private int index = 0;

    void Start()
    {
        if (spawnParent == null)
        {
            var go = new GameObject("PetSpawn");
            go.transform.SetParent(transform, false);
            spawnParent = go.transform;
        }
        if (modelPrefabs == null || modelPrefabs.Count == 0)
        {
            Debug.LogWarning("[VRModelSwitcher] no model prefabs assigned");
            enabled = false;
            return;
        }
        Spawn(index);
    }

    void Update()
    {
        if (modelPrefabs == null || modelPrefabs.Count == 0) return;

        bool next = false, prev = false;

        // Channel 1: Input System XR controllers (PICO controllers register as XRController layouts)
        try
        {
            foreach (var dev in InputSystem.devices)
            {
                if (dev is XRController ctrl)
                {
                    var pb = ctrl.TryGetChildControl<ButtonControl>("primaryButton");
                    var sb = ctrl.TryGetChildControl<ButtonControl>("secondaryButton");
                    if (pb != null && pb.wasPressedThisFrame) next = true;   // A / X
                    if (sb != null && sb.wasPressedThisFrame) prev = true;   // B / Y
                }
            }
        }
        catch { }

        // Channel 2: legacy Input fallback (activeInputHandler = Both)
        if (!next && Input.GetKeyDown(KeyCode.JoystickButton0)) next = true;
        if (!prev && Input.GetKeyDown(KeyCode.JoystickButton1)) prev = true;

        if (next) Switch((index + 1) % modelPrefabs.Count);
        else if (prev) Switch((index - 1 + modelPrefabs.Count) % modelPrefabs.Count);
    }

    void Spawn(int i)
    {
        if (modelPrefabs == null || i < 0 || i >= modelPrefabs.Count) return;
        var prefab = modelPrefabs[i];
        if (prefab == null) return;

        // 记住上一个模型的位置、朝向与**脚底高度**：
        // 切换模型时新模型出现在同一位置；悬空时也保持高度（不能吸到地面）
        bool hadCurrent = current != null;
        Vector3 keepPos = hadCurrent ? current.transform.position : Vector3.zero;
        float keepYaw = hadCurrent ? current.transform.eulerAngles.y : 180f;
        float keepFeetY = hadCurrent ? FeetWorldY : 0f;

        if (current != null)
        {
            current.SetActive(false);   // 先隐藏，避免销毁延迟造成的一帧重影
            Destroy(current);
        }

        current = Instantiate(prefab, spawnParent);
        current.name = prefab.name;
        current.transform.localScale = Vector3.one * spawnScale;

        if (hadCurrent)
        {
            current.transform.position = keepPos;
            current.transform.rotation = Quaternion.Euler(0f, keepYaw, 0f);
            // 保持脚底高度：悬空则继续悬空，站地上则继续贴地
            float feetNow = FeetWorldY;
            current.transform.position += Vector3.up * (keepFeetY - feetNow);
        }
        else
        {
            current.transform.localPosition = spawnPosition;
            current.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the player
            PlaceOnGround();
        }

        var anim = current.GetComponent<Animator>();
        if (anim != null && animatorController != null)
        {
            anim.runtimeAnimatorController = animatorController;
        }

        // 注意：这里**不能**再无条件调用 PlaceOnGround()——
        // 那会把上面刚保持住的悬浮高度重新按到地面（用户反馈"切模型总是贴地"的原因）。

        index = i;
        Debug.Log("[VRModelSwitcher] switched to model: " + prefab.name + " (" + (index + 1) + "/" + modelPrefabs.Count + ")");
    }

    /// <summary>
    /// 自动落地：桌宠预制体原点常在髋部/中心（桌面版挂在窗口用），不在脚底。
    /// 按全部渲染器世界包围盒最低点对齐地面，保证任何模型脚底贴地。
    /// </summary>
    public void PlaceOnGround()
    {
        if (current == null) return;
        float dy = floorClearance - FeetWorldY;
        if (Mathf.Abs(dy) > 0.0005f)
            current.transform.position += Vector3.up * dy;
    }

    /// <summary>乘性缩放：amount &gt; 0 放大，&lt; 0 缩小（配合摇杆前后推）。</summary>
    public void ScaleBy(float amount)
    {
        float factor = Mathf.Exp(amount * scaleSpeed * Time.deltaTime);
        ApplyScale(Mathf.Clamp(spawnScale * factor, minScale, maxScale));
    }

    /// <summary>
    /// 应用缩放：**以脚底为锚点**缩放——悬空时保持离地高度、站在地面时保持贴地。
    /// 之前这里调用 PlaceOnGround 会把悬空的桌宠强行吸到地面（用户反馈的"缩放就跳回地面"）。
    /// </summary>
    public void ApplyScale(float s)
    {
        spawnScale = Mathf.Clamp(s, minScale, maxScale);
        if (current == null) return;

        float feetBefore = FeetWorldY;
        current.transform.localScale = Vector3.one * spawnScale;
        float feetAfter = FeetWorldY;
        current.transform.position += Vector3.up * (feetBefore - feetAfter);
    }

    public float CurrentScale { get { return spawnScale; } }

    /// <summary>当前模型的渲染包围盒（世界空间）。</summary>
    public Bounds CurrentAvatarBounds
    {
        get { return current != null ? ComputeWorldBounds(current) : new Bounds(); }
    }

    /// <summary>
    /// 当前模型脚底的世界高度。**优先用左右脚骨骼**（比渲染包围盒准确得多）——
    /// 包围盒会把裙摆、翅膀这类低垂部件算进去，导致桌宠看起来悬在半空/陷进地面。
    /// 骨骼取不到时回退到包围盒。
    /// </summary>
    public float FeetWorldY
    {
        get
        {
            if (current == null) return 0f;

            var anim = current.GetComponent<Animator>();
            if (anim != null)
            {
                var l = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var r = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (l != null && r != null)
                    return Mathf.Min(l.position.y, r.position.y) - soleOffset * spawnScale;
            }

            Bounds b = ComputeWorldBounds(current);
            return b.min.y;
        }
    }

    /// <summary>模型原点高于脚底的距离（米）。用于防穿地钳制。</summary>
    public float OriginAboveFeet
    {
        get
        {
            if (current == null) return 0f;
            return current.transform.position.y - FeetWorldY;
        }
    }

    // 渲染器缓存：缩放/落地每帧调用，避免 GetComponentsInChildren 反复分配
    private GameObject cacheOwner;
    private Renderer[] cacheRenderers;

    private Bounds ComputeWorldBounds(GameObject go)
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

    void Switch(int i) { Spawn(i); }

    public int CurrentIndex { get { return index; } }

    /// <summary>当前实例化的模型（供交互/聊天/舞蹈模块访问）。</summary>
    public GameObject CurrentAvatar { get { return current; } }

    public Animator CurrentAnimator
    {
        get { return current != null ? current.GetComponent<Animator>() : null; }
    }

    /// <summary>模型的出生父节点（抓取释放后回到这里）。</summary>
    public Transform SpawnParentOrSelf()
    {
        return spawnParent != null ? spawnParent : transform;
    }

    /// <summary>按方向切换模型（+1 下一个 / -1 上一个）。</summary>
    public void Next(int direction)
    {
        if (modelPrefabs == null || modelPrefabs.Count == 0) return;
        Switch((index + direction + modelPrefabs.Count) % modelPrefabs.Count);
    }

    public void SwitchTo(int target)
    {
        if (modelPrefabs == null || modelPrefabs.Count == 0) return;
        if (target < 0 || target >= modelPrefabs.Count) return;
        Switch(target);
    }

}
