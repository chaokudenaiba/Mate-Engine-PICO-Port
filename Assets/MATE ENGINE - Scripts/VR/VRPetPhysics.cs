using UnityEngine;

/// <summary>
/// 还原原版桌宠的"重力 + 飘"手感（对应原版 AvatarGravityController + AvatarSwayController）：
/// - 飘摆：移动时身体按速度倾斜（惯性感），停下后平滑回正
/// - 重力：松手后若悬在空中，自由落体 + 轻微弹跳，最终落到地面
/// 桌面版的窗口坐标系重力在 VR 无意义，这里改为真实世界重力。
///
/// 执行顺序在交互器之后，这样倾斜是叠加在交互器设定的朝向之上。
/// </summary>
[DefaultExecutionOrder(100)]
public class VRPetPhysics : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;

    [Header("重力")]
    [Tooltip("开启后：松手时若悬在空中会自由落体到地面。\n用户希望桌宠停在自己摆放的高度时保持关闭——重力感由移动时的飘摆体现。")]
    public bool enableGravity = false;
    public float gravity = 9.8f;
    [Tooltip("落地弹跳系数（0 = 不弹）")]
    [Range(0f, 0.8f)] public float bounce = 0.28f;
    [Tooltip("停止弹跳的速度阈值")]
    public float restSpeed = 0.6f;

    [Header("飘摆（移动时倾斜）")]
    [Tooltip("水平速度 → 侧倾系数（度 / (米/秒)）")]
    public float leanPerSpeed = 5.0f;
    [Tooltip("垂直速度 → 前后俯仰系数（度 / (米/秒)）")]
    public float pitchPerSpeed = 3.0f;
    [Tooltip("最大倾斜角度")]
    public float maxTiltDegrees = 22f;
    [Tooltip("倾斜平滑速度")]
    public float tiltSmoothing = 7f;

    /// <summary>被选中拖拽期间由交互器置 true —— 暂停重力（位置完全由手/射线控制）。</summary>
    [HideInInspector] public bool suspended;

    private bool falling;
    private float vy;
    private Vector3 lastPos;
    private bool hasLastPos;
    private Vector3 smoothedVelocity;
    private Quaternion tilt = Quaternion.identity;
    private Quaternion appliedTilt = Quaternion.identity;   // 上一帧实际叠加的倾斜，用于增量还原

    private GameObject Avatar { get { return switcher != null ? switcher.CurrentAvatar : null; } }

    private void Update()
    {
        var avatar = Avatar;
        if (avatar == null)
        {
            falling = false;
            smoothedVelocity = Vector3.zero;
            hasLastPos = false;
            return;
        }

        // ---- 速度（供飘摆使用）----
        Vector3 vel = Vector3.zero;
        if (hasLastPos && Time.deltaTime > 0.0001f)
            vel = (avatar.transform.position - lastPos) / Time.deltaTime;
        lastPos = avatar.transform.position;
        hasLastPos = true;
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, vel, 1f - Mathf.Exp(-10f * Time.deltaTime));

        // ---- 重力（默认关闭：让桌宠停在你摆放的高度）----
        if (suspended || !enableGravity)
        {
            falling = false;
            vy = 0f;
        }
        else
        {
            float feet = avatar.transform.position.y - switcher.OriginAboveFeet;
            if (!falling && feet > switcher.floorClearance + 0.02f)
            {
                falling = true;
                vy = 0f;
            }
            if (falling) StepGravity(avatar);
        }

        // ---- 飘摆：按模型自身的前/右分解速度，得到俯仰与侧倾 ----
        Vector3 local = avatar.transform.InverseTransformDirection(smoothedVelocity);
        float targetRoll = Mathf.Clamp(-local.x * leanPerSpeed, -maxTiltDegrees, maxTiltDegrees);
        float targetPitch = Mathf.Clamp(local.z * pitchPerSpeed, -maxTiltDegrees, maxTiltDegrees);

        tilt = Quaternion.Slerp(tilt, Quaternion.Euler(targetPitch, 0f, targetRoll),
                                1f - Mathf.Exp(-tiltSmoothing * Time.deltaTime));

        // 以"增量"方式叠加倾斜：先撤掉上一帧的倾斜，再叠加新的。
        // 这样完全不碰 yaw —— 用 eulerAngles 重建旋转会在有倾斜时把 yaw 分解错，表现为抖动/闪烁。
        avatar.transform.rotation = avatar.transform.rotation * Quaternion.Inverse(appliedTilt) * tilt;
        appliedTilt = tilt;
    }

    private void StepGravity(GameObject avatar)
    {
        if (switcher.OriginAboveFeet <= 0.0001f) { falling = false; return; }

        vy -= gravity * Time.deltaTime;

        Vector3 p = avatar.transform.position;
        p.y += vy * Time.deltaTime;

        float floorOriginY = switcher.floorClearance + switcher.OriginAboveFeet;
        if (p.y <= floorOriginY)
        {
            p.y = floorOriginY;
            avatar.transform.position = p;

            if (Mathf.Abs(vy) > restSpeed && bounce > 0f)
                vy = -vy * bounce;      // 落地弹一下
            else
            {
                vy = 0f;
                falling = false;        // 停稳
            }
            return;
        }

        avatar.transform.position = p;
    }

    /// <summary>换模型 / 复位时清空状态。</summary>
    public void ResetPhysics()
    {
        falling = false;
        vy = 0f;
        tilt = Quaternion.identity;
        appliedTilt = Quaternion.identity;
        smoothedVelocity = Vector3.zero;
        hasLastPos = false;
    }
}
