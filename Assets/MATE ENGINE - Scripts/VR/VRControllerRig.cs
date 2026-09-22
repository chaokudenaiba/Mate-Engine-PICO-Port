using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 双手柄追踪与激光射线。
///
/// 位姿与按键一律取自 Unity 传统 XR 输入（InputDevices + CommonUsages）——
/// PICO 的 PXR_Loader 创建了名为 "PICO Input" 的 XRInputSubsystem，官方控制器组件
/// （PXR_ControllerAnimator）也走这条路径；Input System 的 XRController 设备在
/// PICO 非 OpenXR 模式下没有位姿数据，会造成手柄悬在原点不动。
/// </summary>
public class VRControllerRig : MonoBehaviour
{
    [Header("射线")]
    public float rayLength = 4f;
    public Color rayColor = new Color(0.30f, 0.80f, 1.00f, 0.90f);
    public Color rayHitColor = new Color(1.00f, 0.75f, 0.20f, 0.95f);

    [Tooltip("射线相对手柄 grip 轴的俯仰偏移（度）。固定值 0 = 直接用手柄朝向（运行时校准功能已移除）")]
    public float aimPitchDegrees = 0f;

    [Header("诊断")]
    [Tooltip("每隔几秒打印一次手柄位姿，便于实机排查")]
    public bool logDiagnostics = true;
    public float logInterval = 5f;

    public class Hand
    {
        public GameObject visual;
        public LineRenderer laser;
        public Transform dot;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector2 stick;          // 摇杆原始值（前推 y=+1，右推 x=+1）
        public bool isRight;           // 是否右手柄
        public bool tracked;

        // 边沿状态
        public bool triggerDown;
        public bool triggerHeld;
        public bool gripDown;
        public bool gripHeld;
        public bool primaryDown;
        public bool primaryHeld;
        public bool secondaryDown;
        public bool secondaryHeld;

        internal Vector3 lastPos;
        internal bool hadLastPos;
        internal bool lastTrigger, lastGrip, lastPrimary, lastSecondary;
        internal bool lastTracked;

        /// <summary>用指定方向构造射线（配合 VRControllerRig.AimDirection 做俯仰校准）。</summary>
        public Ray RayWith(Vector3 direction)
        {
            return new Ray(position, direction);
        }
    }

    public readonly List<Hand> hands = new List<Hand>();
    public int HandCount { get { return hands.Count; } }

    private Material rayMat, hitMat, dotMat;
    private float nextLogTime;
    private static readonly XRNode[] NodeOrder = { XRNode.LeftHand, XRNode.RightHand };

    private void Start()
    {
        rayMat = new Material(Shader.Find("Unlit/Color")) { color = rayColor };
        hitMat = new Material(Shader.Find("Unlit/Color")) { color = rayHitColor };
        dotMat = new Material(Shader.Find("Unlit/Color")) { color = rayHitColor };
        Debug.Log("[VRControllerRig] ready. aim pitch = " + aimPitchDegrees + "°（固定值，无运行时校准）");
    }

    private void Update()
    {
        for (int i = 0; i < NodeOrder.Length; i++)
        {
            if (i >= hands.Count) hands.Add(CreateHand(i));
            Hand h = hands[i];

            InputDevice dev = InputDevices.GetDeviceAtXRNode(NodeOrder[i]);

            bool tracked = false;
            Vector3 pos = h.position;
            Quaternion rot = h.rotation;

            if (dev.isValid)
            {
                dev.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
                Vector3 p;
                Quaternion r;
                if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out p)) pos = p;
                if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out r)) rot = r;
            }

            h.velocity = h.hadLastPos ? (pos - h.lastPos) / Mathf.Max(Time.deltaTime, 0.0001f) : Vector3.zero;
            h.lastPos = pos;
            h.hadLastPos = true;

            // 扳机/侧握用模拟量判定按住（PICO 上比布尔键更稳），主/次键用布尔量取边沿
            float trigger = ReadFloat(dev, CommonUsages.trigger);
            float grip = ReadFloat(dev, CommonUsages.grip);

            Vector2 stick = Vector2.zero;
            if (dev.isValid) dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
            h.stick = stick;

            // 迟滞阈值：按住后要松到很低才算松开。避免模拟量在阈值附近抖动，
            // 导致"按住"状态瞬间中断（表现为拖拽中途突然不动了）。
            bool triggerHeld = ReadBool(dev, CommonUsages.triggerButton)
                               || trigger > (h.lastTrigger ? 0.15f : 0.35f);
            bool gripHeld = ReadBool(dev, CommonUsages.gripButton)
                            || grip > (h.lastGrip ? 0.15f : 0.35f);
            bool primaryHeld = ReadBool(dev, CommonUsages.primaryButton);
            bool secondaryHeld = ReadBool(dev, CommonUsages.secondaryButton);

            h.triggerDown = triggerHeld && !h.lastTrigger;
            h.gripDown = gripHeld && !h.lastGrip;
            h.primaryDown = primaryHeld && !h.lastPrimary;
            h.secondaryDown = secondaryHeld && !h.lastSecondary;
            h.triggerHeld = triggerHeld;
            h.gripHeld = gripHeld;
            h.primaryHeld = primaryHeld;
            h.secondaryHeld = secondaryHeld;
            h.lastTrigger = triggerHeld;
            h.lastGrip = gripHeld;
            h.lastPrimary = primaryHeld;
            h.lastSecondary = secondaryHeld;

            h.tracked = tracked || dev.isValid;
            if (h.tracked)
            {
                h.position = pos;
                h.rotation = rot;
            }

            if (h.visual != null)
            {
                h.visual.SetActive(h.tracked);
                if (h.tracked)
                    h.visual.transform.SetPositionAndRotation(pos, rot);
            }

            if (logDiagnostics && Time.time >= nextLogTime)
                Debug.Log("[VRControllerRig] " + NodeOrder[i] + " valid=" + dev.isValid + " tracked=" + tracked +
                          " pos=" + pos.ToString("F3") + " rot=" + rot.eulerAngles.ToString("F1") +
                          " stick=" + stick.ToString("F2") + " trig=" + trigger.ToString("F2") +
                          " 扳机按住=" + triggerHeld + " 侧键按住=" + gripHeld);
        }

        if (logDiagnostics && Time.time >= nextLogTime)
            nextLogTime = Time.time + logInterval;
    }

    private static bool ReadBool(InputDevice dev, InputFeatureUsage<bool> usage)
    {
        bool v = false;
        if (dev.isValid) dev.TryGetFeatureValue(usage, out v);
        return v;
    }

    private static float ReadFloat(InputDevice dev, InputFeatureUsage<float> usage)
    {
        float v = 0f;
        if (dev.isValid) dev.TryGetFeatureValue(usage, out v);
        return v;
    }

    /// <summary>校准后的指向方向：绕手柄本地 X 轴俯仰 aimPitchDegrees 度（正值为向下压）。</summary>
    public Vector3 AimDirection(Hand h)
    {
        // 注意：Euler(+θ,0,0) * forward 得到向下偏转的向量（Y 分量为负），故用正值表示下压
        return h.rotation * Quaternion.Euler(aimPitchDegrees, 0f, 0f) * Vector3.forward;
    }

    /// <summary>
    /// 回写激光终点。线色保持恒定——命中反馈由被命中的 UI 自身高亮承担，
    /// 整条线变色 + 命中点大球会严重遮挡视线。
    /// </summary>
    public void SetLaserEnd(Hand hand, Vector3 worldPos, bool hitSomething)
    {
        if (hand == null || hand.laser == null) return;
        hand.laser.SetPosition(0, hand.position);
        hand.laser.SetPosition(1, worldPos);
    }

    public void SetLaserEndDefault(Hand hand)
    {
        if (hand == null || !hand.tracked) return;
        SetLaserEnd(hand, hand.position + AimDirection(hand) * rayLength, false);
    }

    private Hand CreateHand(int index)
    {
        var h = new Hand();
        h.isRight = NodeOrder[index] == XRNode.RightHand;

        h.visual = new GameObject("Controller" + index);
        h.visual.transform.SetParent(transform, false);

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(ball.GetComponent<Collider>());
        ball.transform.SetParent(h.visual.transform, false);
        ball.transform.localScale = Vector3.one * 0.035f;
        ball.GetComponent<MeshRenderer>().sharedMaterial =
            new Material(Shader.Find("Unlit/Color")) { color = new Color(0.85f, 0.88f, 0.92f) };

        h.laser = h.visual.AddComponent<LineRenderer>();
        h.laser.positionCount = 2;
        h.laser.startWidth = 0.006f;
        h.laser.endWidth = 0.002f;
        h.laser.useWorldSpace = true;
        h.laser.material = rayMat;
        h.laser.SetPosition(0, Vector3.zero);
        h.laser.SetPosition(1, Vector3.forward * rayLength);

        var dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(dotGo.GetComponent<Collider>());
        dotGo.transform.SetParent(h.visual.transform, false);
        dotGo.transform.localScale = Vector3.one * 0.012f;
        dotGo.GetComponent<MeshRenderer>().sharedMaterial = dotMat;
        dotGo.SetActive(false);
        h.dot = dotGo.transform;

        return h;
    }
}
