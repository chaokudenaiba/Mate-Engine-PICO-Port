using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 自动走动（还原原版 AvatarLocomotionController 的"自己溜达"体验）：
/// 桌面版是沿 Windows 窗口/任务栏走动，VR 里改为在真实房间里漫步——
/// 在"家"附近随机选点 → 走过去 → 停一会儿 → 再选下一个点。
/// 走路动画用构建时注入的 VRWalk 图层（原版 PET_WALK_LEFT/RIGHT 原地循环）；
/// 被选中拖拽时自动暂停，避免和手的控制打架。
/// </summary>
[DefaultExecutionOrder(50)]
public class VRAutoWalk : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public VRPetInteractor interactor;

    [Header("开关")]
    [Tooltip("默认关闭，可在菜单里开启「自动走动」")]
    public bool enabled_ = false;

    [Header("漫步")]
    [Tooltip("以出发点为中心的活动半径（米）")]
    public float wanderRadius = 1.6f;
    [Tooltip("行走速度（米/秒）")]
    public float walkSpeed = 0.32f;
    [Tooltip("到达后停留时间范围（秒）")]
    public Vector2 idleTimeRange = new Vector2(3f, 9f);
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 120f;
    [Tooltip("朝向修正：0 = 用骨骼推算结果（本项目模型已验证为正确）")]
    public float facingOffsetDegrees = 0f;
    [Tooltip("走路片段：左横移（原版 PET_WALK_LEFT，构建时去根位移）")]
    public AnimationClip walkLeftClip;
    [Tooltip("走路片段：右横移（原版 PET_WALK_RIGHT）")]
    public AnimationClip walkRightClip;
    [Tooltip("若左右侧步动画用反了，勾选此项")]
    public bool invertWalkSide = false;
    [Tooltip("离用户太近/太远都不去（米）")]
    public Vector2 keepDistanceFromUser = new Vector2(1.0f, 4.0f);

    private Animator anim;
    private GameObject avatar;

    // 走路用 Playable 图层叠加：不切换控制器，避免重绑定造成的姿态跳变/闪烁
    private PlayableGraph walkGraph;
    private AnimationLayerMixerPlayable walkMixer;
    private bool walkLayerReady;
    private int activeWalkPort;   // 0=不走路 1=WalkLeft 2=WalkRight
    private float nextWalkLog;

    private Vector3 homePos;
    private bool hasHome;
    private Vector3 targetPos;
    private bool walking;
    private float idleUntil;
    private float walkStartTime;
    private float stepTimer;

    private void Update()
    {
        var cur = switcher != null ? switcher.CurrentAvatar : null;
        if (cur != avatar)
        {
            avatar = cur;
            anim = avatar != null ? avatar.GetComponent<Animator>() : null;
            hasHome = false;
            walking = false;
            SetupWalkPlayable();
        }
        if (avatar == null) return;

        bool blocked = !enabled_ || (interactor != null && interactor.IsSelecting);

        if (!hasHome)
        {
            homePos = avatar.transform.position;
            hasHome = true;
            idleUntil = Time.time + Random.Range(idleTimeRange.x, idleTimeRange.y);
        }

        if (blocked)
        {
            StopWalk();
            return;
        }

        if (walking)
        {
            StepWalk();
        }
        else if (Time.time >= idleUntil)
        {
            PickTarget();
        }

        // 换模型/复位后家位置缓慢跟随（避免越走越偏）
        if (!walking && Vector3.Distance(new Vector3(avatar.transform.position.x, 0, avatar.transform.position.z),
                                          new Vector3(homePos.x, 0, homePos.z)) > wanderRadius * 1.6f)
        {
            homePos = avatar.transform.position;
        }
    }

    private void PickTarget()
    {
        // 以用户为中心"环绕"选点：距离基本不变、只改变角度。
        // 原版 PET_WALK_LEFT/RIGHT 是左右侧步动画，让桌宠面向用户做横向移动，
        // 动画方向才对得上（若朝行进方向走，永远会看起来在倒退）。
        var cam = Camera.main;
        Vector3 user = cam != null ? cam.transform.position : homePos;

        Vector3 fromUser = new Vector3(homePos.x - user.x, 0f, homePos.z - user.z);
        float dist = fromUser.magnitude;
        if (dist < 0.2f) dist = 1.8f;
        dist = Mathf.Clamp(dist, keepDistanceFromUser.x, keepDistanceFromUser.y);

        float a0 = Mathf.Atan2(fromUser.z, fromUser.x);
        float delta = Random.Range(0.35f, 1.15f) * (Random.value < 0.5f ? -1f : 1f);
        float a1 = a0 + delta;

        targetPos = new Vector3(user.x + Mathf.Cos(a1) * dist, homePos.y, user.z + Mathf.Sin(a1) * dist);
        walking = true;
        walkStartTime = Time.time;
        stepTimer = 0f;

        // 走路片段在"这一段路开始时"就定好，全程不再切换 ——
        // 若每帧按转向符号切换，会导致左右动画频繁互换、姿态来回跳（看起来就是高低闪动）。
        Vector3 myPos = avatar != null ? avatar.transform.position : targetPos;
        Vector3 toTarget2 = new Vector3(targetPos.x - myPos.x, 0f, targetPos.z - myPos.z);
        Vector3 fwd = avatar != null ? avatar.transform.forward : Vector3.forward;
        Vector3 tmp;
        if (anim != null && VRPetInteractor.TryGetModelForward(anim, out tmp)) fwd = tmp;
        if (toTarget2.sqrMagnitude > 0.0001f)
        {
            float turnSign = Vector3.SignedAngle(fwd, toTarget2.normalized, Vector3.up);
            if (invertWalkSide) turnSign = -turnSign;
            SetWalkPort(turnSign >= 0f ? 2 : 1);
        }
    }

    private void StepWalk()
    {
        Vector3 cur = avatar.transform.position;
        Vector3 flatCur = new Vector3(cur.x, 0f, cur.z);
        Vector3 flatTarget = new Vector3(targetPos.x, 0f, targetPos.z);
        Vector3 toTarget = flatTarget - flatCur;
        float dist = toTarget.magnitude;

        // 到达或超时 → 停下休息
        if (dist < 0.12f || Time.time - walkStartTime > 25f)
        {
            StopWalk();
            idleUntil = Time.time + Random.Range(idleTimeRange.x, idleTimeRange.y);
            return;
        }

        Vector3 dir = toTarget / dist;

        // 朝向：面向行进方向（原版走路是"朝前走"的动画；根位移已在构建时剥离，
        // 所以现在不会再有整体偏移/倒着走的问题）
        FaceDirection(dir);

        // 位移
        Vector3 next = flatCur + dir * walkSpeed * Time.deltaTime;
        avatar.transform.position = new Vector3(next.x, cur.y, next.z);

        // 诊断：行走期间每 2 秒打印高度、通道与目标，便于排查"高低跳/方向不对"
        if (Time.time >= nextWalkLog)
        {
            nextWalkLog = Time.time + 2f;
            Debug.Log("[VRAutoWalk] 行走中 y=" + avatar.transform.position.y.ToString("F3") +
                      " 通道=" + activeWalkPort +
                      " 朝向=" + avatar.transform.eulerAngles.y.ToString("F0") +
                      " 目标=" + targetPos.ToString("F2") +
                      " 剩余=" + dist.ToString("F2"));
        }

        // 注：走路片段已在 PickTarget 里选定，这里不再逐帧切换（避免姿态来回跳）
    }

    /// <summary>切换走路图层中的激活片段（1=左 2=右，0=不走路）。</summary>
    private void SetWalkPort(int port)
    {
        if (!walkLayerReady || !walkMixer.IsValid()) return;
        if (activeWalkPort == port) return;
        activeWalkPort = port;
        walkMixer.SetInputWeight(0, port == 0 ? 1f : 0f);
        walkMixer.SetInputWeight(1, port == 1 ? 1f : 0f);
        walkMixer.SetInputWeight(2, port == 2 ? 1f : 0f);
    }

    /// <summary>
    /// 走路用 Playable 图层叠加：输入0 = 原动画控制器，输入1/2 = 左右侧步片段，
    /// 靠权重切换当前步态。完全不切换控制器 —— 切换会触发重绑定，表现为姿态跳变/闪烁/瞬移。
    /// </summary>
    private void SetupWalkPlayable()
    {
        walkLayerReady = false;
        activeWalkPort = 0;
        DestroyWalkGraph();

        if (anim == null || walkLeftClip == null || walkRightClip == null)
        {
            Debug.Log("[VRAutoWalk] 走路片段未配置，自动走动将没有动画");
            return;
        }

        try
        {
            walkGraph = PlayableGraph.Create("VRPetWalk");
            var output = AnimationPlayableOutput.Create(walkGraph, "VRPetWalkOutput", anim);
            walkMixer = AnimationLayerMixerPlayable.Create(walkGraph, 3);

            if (anim.runtimeAnimatorController != null)
            {
                var ctrlPlayable = AnimatorControllerPlayable.Create(walkGraph, anim.runtimeAnimatorController);
                walkGraph.Connect(ctrlPlayable, 0, walkMixer, 0);
            }

            var leftPlayable = AnimationClipPlayable.Create(walkGraph, walkLeftClip);
            leftPlayable.SetApplyFootIK(false);
            walkGraph.Connect(leftPlayable, 0, walkMixer, 1);

            var rightPlayable = AnimationClipPlayable.Create(walkGraph, walkRightClip);
            rightPlayable.SetApplyFootIK(false);
            walkGraph.Connect(rightPlayable, 0, walkMixer, 2);

            walkMixer.SetInputWeight(0, 1f);
            walkMixer.SetInputWeight(1, 0f);
            walkMixer.SetInputWeight(2, 0f);

            output.SetSourcePlayable(walkMixer);
            walkGraph.Play();

            walkLayerReady = true;
            Debug.Log("[VRAutoWalk] 走路 Playable 图层就绪（左右侧步，3 输入）");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VRAutoWalk] Playable 图层创建失败，走路将没有动画: " + e.Message);
            walkLayerReady = false;
            DestroyWalkGraph();
        }
    }

    private void SetWalkWeight(float w)
    {
        SetWalkPort(w > 0.5f ? 1 : 0);
    }

    private void DestroyWalkGraph()
    {
        if (walkGraph.IsValid()) walkGraph.Destroy();
    }

    private void OnDisable() { DestroyWalkGraph(); }
    private void OnDestroy() { DestroyWalkGraph(); }

    /// <summary>按模型真实正面转向目标方向（不假设模型正面是 +Z）。</summary>
    private void FaceDirection(Vector3 dir)
    {
        if (avatar == null || dir.sqrMagnitude < 0.0001f) return;

        Vector3 fwd;
        if (!VRPetInteractor.TryGetModelForward(anim, out fwd))
        {
            fwd = avatar.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;
            fwd.Normalize();
        }

        // 朝向修正：把"模型真实正面"换算成"游戏意义上的正面"（骨骼左右与视觉正面相反时为 180°）
        if (Mathf.Abs(facingOffsetDegrees) > 0.01f)
            fwd = Quaternion.AngleAxis(facingOffsetDegrees, Vector3.up) * fwd;

        float angle = Vector3.SignedAngle(fwd, dir.normalized, Vector3.up);
        float step = Mathf.Clamp(angle, -turnSpeed * Time.deltaTime, turnSpeed * Time.deltaTime);
        avatar.transform.Rotate(0f, step, 0f, Space.World);
    }

    private void StopWalk()
    {
        walking = false;
        SetWalkWeight(0f);
    }

    public void SetEnabled(bool on)
    {
        enabled_ = on;
        if (!on) StopWalk();
    }

    public bool IsEnabled { get { return enabled_; } }

    /// <summary>把"家"重新锚定到当前位置（复位/送到我面前后调用）。</summary>
    public void Rehome()
    {
        if (avatar != null)
        {
            homePos = avatar.transform.position;
            hasHome = true;
        }
    }
}
