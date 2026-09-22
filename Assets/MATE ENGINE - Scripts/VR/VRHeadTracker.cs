using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 头显位姿驱动：把 HMD 位姿写入主相机。
///
/// 背景：本项目场景是自建的裸相机（未使用 Unity "XR Origin (VR)" 预制体，
/// 因而没有 XROrigin / TrackedPoseDriver）。实测相机位姿不会被 XR 运行时驱动，
/// 视点固定在原点（地板高度），导致所有虚拟内容相对真实视线整体上移约一个身高
/// —— 表现为桌宠在视线斜上方约 40°、手柄射线在头顶上方。
///
/// 这里用与手柄同一条已验证可用的输入路径（InputDevices + CommonUsages）自行驱动，
/// 在 onBeforeRender 写入（该回调紧邻渲染，等价于 TrackedPoseDriver 的做法）。
/// </summary>
[DefaultExecutionOrder(10000)]
public class VRHeadTracker : MonoBehaviour
{
    [Tooltip("被驱动的主相机（默认 Camera.main）")]
    public Camera targetCamera;

    [Header("诊断")]
    public bool logDiagnostics = true;
    public float logInterval = 5f;

    private InputDevice head;
    private float nextLog;
    private Vector3 lastLoggedPos;
    private bool hasLogged;

    private void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
    }

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        Debug.Log("[VRHeadTracker] start, camera=" + (targetCamera != null ? targetCamera.name : "null"));
    }

    private void Update()
    {
        if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

        if (logDiagnostics && Time.time >= nextLog)
        {
            nextLog = Time.time + logInterval;
            Vector3 p; Quaternion r;
            ReadPose(out p, out r);
            float moved = hasLogged ? Vector3.Distance(p, lastLoggedPos) : 0f;
            Debug.Log("[VRHeadTracker] head valid=" + head.isValid + " pos=" + p.ToString("F2") +
                      " rot=" + r.eulerAngles.ToString("F1") + " 位移=" + moved.ToString("F3") + "m");
            lastLoggedPos = p;
            hasLogged = true;
        }
    }

    private bool ReadPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        if (!head.isValid) return false;

        bool okP = head.TryGetFeatureValue(CommonUsages.centerEyePosition, out pos);
        if (!okP) okP = head.TryGetFeatureValue(CommonUsages.devicePosition, out pos);

        bool okR = head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rot);
        if (!okR) okR = head.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);

        return okP && okR;
    }

    private void OnBeforeRender()
    {
        if (targetCamera == null) return;
        if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

        Vector3 pos; Quaternion rot;
        if (!ReadPose(out pos, out rot)) return;

        // 相机父节点即追踪空间原点，故用 local 写入
        var t = targetCamera.transform;
        t.localPosition = pos;
        t.localRotation = rot;
    }
}
