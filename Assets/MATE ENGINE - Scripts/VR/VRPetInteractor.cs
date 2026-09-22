using UnityEngine;

/// <summary>
/// 桌宠 VR 交互核心：
/// - 选中移动：扳机指向桌宠按下 = 选中；选中期间桌宠跟随手柄移动（左右/上下），
///   摇杆前推 = 推远、后拉 = 拉近；松开扳机即放下（就地停在原处，不自动落地）
/// - 摸头：手柄靠近头部并下压 → Headpat 动画 + 爱心粒子
/// - 菜单：扳机指向按钮 → 转发给 VRWorldMenu / VRHelpPanel / VRModelMenu（被指向的按钮高亮）
/// - 左手侧握键(Grip)开关菜单；右手侧握键(Grip) + 摇杆 = 缩放
/// </summary>
public class VRPetInteractor : MonoBehaviour
{
    [Header("引用")]
    public VRControllerRig rig;
    public VRModelSwitcher switcher;
    public VRWorldMenu menu;
    public VRHelpPanel help;
    public VRPetPhysics physics;
    public VRModelMenu modelMenu;
    public Camera hmdCamera;

    [Header("拖拽音效（还原原版 AvatarDragSoundHandler：随机音高）")]
    public AudioClip dragStartClip;
    public AudioClip dragStopClip;
    [Range(0f, 100f)] public float maxHighPitchPercent = 20f;
    [Range(0f, 100f)] public float maxLowPitchPercent = 40f;
    public float dragSoundVolume = 0.35f;

    private AudioSource dragAudio;

    [Header("选中判定")]
    [Tooltip("选中判定盒在身体包围盒基础上的外扩（米）。调小可减少误选")]
    public float grabPadding = 0f;
    [Tooltip("近身兜底：桌宠离手柄这么近时，即使射线没命中（起点已在它内部）也能按扳机选中")]
    public float closeSelectRadius = 0.28f;

    // 射线检测缓冲（避免每帧分配）
    private readonly RaycastHit[] hitBuf = new RaycastHit[16];

    [Header("移动（选中后：桌宠落在射线方向上，摇杆调距离）")]
    [Tooltip("摇杆推远/拉近的速度（米/秒，满摇杆）")]
    public float moveSpeed = 2.2f;
    [Tooltip("移动摇杆死区")]
    public float moveDeadZone = 0.25f;
    [Tooltip("若摇杆前推变成拉近（方向相反），勾选此项")]
    public bool invertMoveStick = false;
    [Tooltip("与用户的最小水平距离（防止推到自己脸上；调小才能把桌宠拉到身边）")]
    public float minDistanceFromUser = 0.15f;
    [Tooltip("与用户的最大水平距离（防止推出视野）")]
    public float maxDistanceFromUser = 10f;

    [Header("选中朝向")]
    [Tooltip("选中后转向用户的速度（度/秒）。设很大值即为瞬间转向")]
    public float faceTurnSpeed = 540f;
    [Tooltip("模型朝向修正：0 = 用骨骼推算结果（本项目模型已验证为正确）；180 = 反转")]
    public float facingOffsetDegrees = 0f;
    public float patEnterRadius = 0.26f;
    public float patExitRadius = 0.38f;
    public float patMinStrokeSpeed = 0.25f;   // 手向下 cm/s 量级的轻拍判定
    public float heartInterval = 0.6f;

    [Header("缩放（右手柄侧握键 Grip + 摇杆前后）")]
    [Tooltip("摇杆前后推时忽略的死区")]
    public float scaleDeadZone = 0.3f;
    [Tooltip("若前推变成放大（方向相反），勾选此项")]
    public bool invertScaleStick = false;

    private GameObject avatar;
    private Animator avatarAnim;
    private Transform avatarHead;
    private Collider grabCollider;

    // 选中状态
    private bool selected;
    private VRControllerRig.Hand selectedHand;
    private float selectDistance = 2f;     // 手柄到"抓取点"的距离（射线方向上），摇杆可调
    private Vector3 grabOffset;            // 选中瞬间"命中点 → 模型原点"的世界偏移（保持悬挂关系不变）
    private float nextSelectLog;

    private static readonly int isDraggingParam = Animator.StringToHash("isDragging");
    private static readonly int headpatParam = Animator.StringToHash("Headpat");

    // 摸头状态机
    private bool patting;

    private void Start()
    {
        // 拖拽音效（原版音量 0.35）
        dragAudio = gameObject.AddComponent<AudioSource>();
        dragAudio.playOnAwake = false;
        dragAudio.spatialBlend = 0f;
        dragAudio.volume = dragSoundVolume;
    }

    /// <summary>按原版方式播放拖拽音效：随机音高（高 ±20%、低 ±40%）。</summary>
    private void PlayDragSound(AudioClip clip)
    {
        if (clip == null || dragAudio == null) return;
        float low = 1f - maxLowPitchPercent / 100f;
        float high = 1f + maxHighPitchPercent / 100f;
        dragAudio.pitch = Random.Range(low, high);
        dragAudio.PlayOneShot(clip);
        Debug.Log("[VRPetInteractor] 拖拽音效: " + clip.name + " pitch=" + dragAudio.pitch.ToString("F2"));
    }


    private void Update()
    {
        if (rig == null || switcher == null) return;

        var current = switcher.CurrentAvatar;
        if (current != avatar)
        {
            OnAvatarChanged(current);
        }

        if (avatar == null) return;

        // Grip 开关菜单
        if (rig.HandCount > 0 && rig.hands[0] != null && rig.hands[0].gripDown && menu != null)
            menu.ToggleMenu();

        Collider menuHover = null, modelHover = null, helpHover = null;
        for (int i = 0; i < rig.HandCount; i++)
        {
            var h = rig.hands[i];
            if (h == null || !h.tracked) continue;

            Ray aimRay = h.RayWith(rig.AimDirection(h));
            int n = Physics.RaycastNonAlloc(aimRay, hitBuf, rig.rayLength, ~0, QueryTriggerInteraction.Collide);

            // 面板按钮优先于其它物体：桌宠站在面板前面时，若不这样处理会挡住射线导致点不到按钮
            Collider uiCol = null; float uiDist = float.MaxValue;
            Collider anyCol = null; float anyDist = float.MaxValue;
            Vector3 anyPoint = aimRay.origin + aimRay.direction * rig.rayLength;
            for (int k = 0; k < n; k++)
            {
                var c = hitBuf[k].collider;
                if (c == null) continue;

                bool isUi = (menu != null && menu.Owns(c)) ||
                            (help != null && help.Owns(c)) ||
                            (modelMenu != null && modelMenu.Owns(c));

                if (isUi && hitBuf[k].distance < uiDist) { uiDist = hitBuf[k].distance; uiCol = c; }
                if (hitBuf[k].distance < anyDist) { anyDist = hitBuf[k].distance; anyCol = c; anyPoint = hitBuf[k].point; }
            }

            Collider chosen = uiCol != null ? uiCol : anyCol;
            Vector3 chosenPoint = uiCol != null ? aimRay.origin + aimRay.direction * uiDist : anyPoint;
            bool hasHit = chosen != null;

            rig.SetLaserEnd(h, chosenPoint, hasHit);

            if (hasHit)
            {
                if (menu != null && menu.Owns(chosen)) menuHover = chosen;
                else if (help != null && help.Owns(chosen)) helpHover = chosen;
                else if (modelMenu != null && modelMenu.Owns(chosen)) modelHover = chosen;
            }

            if (h.triggerDown && !selected)
            {
                if (hasHit && menu != null && menu.Owns(chosen))
                {
                    menu.Click(chosen);
                }
                else if (hasHit && help != null && help.Owns(chosen))
                {
                    help.Click(chosen);
                }
                else if (hasHit && modelMenu != null && modelMenu.Owns(chosen))
                {
                    modelMenu.Click(chosen);
                }
                else if (hasHit && chosen == grabCollider)
                {
                    BeginSelect(h, chosenPoint);
                }
                else if (avatar != null && switcher != null &&
                         switcher.CurrentAvatarBounds.SqrDistance(h.position) <= closeSelectRadius * closeSelectRadius)
                {
                    // 近身兜底：桌宠贴在手边时射线起点已在它内部，射线检测不到，
                    // 按"离手距离"直接判定选中（更贴近"捏住它"的手感）
                    Debug.Log("[VRPetInteractor] 近身兜底选中，离手 " +
                              Mathf.Sqrt(switcher.CurrentAvatarBounds.SqrDistance(h.position)).ToString("F2") + "m");
                    BeginSelect(h, h.position);
                }
            }
        }

        // 瞄准反馈：只有被指向的那个按钮高亮（线本身不变色）
        if (menu != null) menu.SetHover(menuHover);
        if (help != null) help.SetHover(helpHover);
        if (modelMenu != null) modelMenu.SetHover(modelHover);

        // 缩放手势：右手柄侧握键(Grip)按住 + 摇杆前推缩小 / 后拉放大
        for (int i = 0; i < rig.HandCount; i++)
        {
            var sh = rig.hands[i];
            if (sh == null || !sh.tracked) continue;
            if (!sh.isRight) continue;      // 仅右手柄
            if (!sh.gripHeld) continue;     // 需按住侧握键

            float y = invertScaleStick ? -sh.stick.y : sh.stick.y;
            if (Mathf.Abs(y) < scaleDeadZone) continue;

            switcher.ScaleBy(-y);           // 前推(+y) => 缩小
            ShowScaleStatus();
        }

        // 选中状态：桌宠跟随手柄移动（左右/上下），摇杆前后推远拉近；松开扳机即放下
        if (selected && selectedHand != null)
        {
            UpdateSelected();
            if (!selectedHand.triggerHeld) EndSelect();
        }

    }

    private float lastScaleShown = -1f;

    /// <summary>
    /// 选中状态（职责分离，用户确认的模型）：
    /// - 射线 = 只控制移动方向：桌宠的"抓取点"始终粘在激光线上（线指到哪，它跟到哪）
    /// - 摇杆 = 只控制远近：调整抓取点在激光线上的距离
    /// - 选中时记下"命中点 → 模型整体"的偏移，全程保持 → 选中瞬间不跳、不悬空
    /// - PlaceOnGround 仅作防穿地钳制：线朝下时桌宠站在地面上，抬高线时沿线提起
    /// </summary>
    private void UpdateSelected()
    {
        if (avatar == null || selectedHand == null || rig == null) return;

        var cam = hmdCamera != null ? hmdCamera : Camera.main;
        Vector3 head = cam != null ? cam.transform.position : Vector3.zero;

        // 摇杆 = 唯一的远近控制：调整抓取点在激光线上的距离
        float y = invertMoveStick ? -selectedHand.stick.y : selectedHand.stick.y;
        if (Mathf.Abs(y) > moveDeadZone)
            selectDistance = Mathf.Clamp(selectDistance + y * moveSpeed * Time.deltaTime,
                                         minDistanceFromUser, maxDistanceFromUser);

        // 射线 = 唯一的方向控制：抓取点粘在激光线上，模型带偏移跟随
        Vector3 origin = selectedHand.position;
        Vector3 dir = rig.AimDirection(selectedHand).normalized;
        Vector3 grabOnRay = origin + dir * selectDistance;
        Vector3 target = grabOnRay + grabOffset;

        // 与用户保持水平距离（防止推到自己脸上 / 推出视野）
        Vector2 flat = new Vector2(target.x - head.x, target.z - head.z);
        float d = flat.magnitude;
        if (d > 0.0001f)
        {
            float clamped = Mathf.Clamp(d, minDistanceFromUser, maxDistanceFromUser);
            if (!Mathf.Approximately(clamped, d))
            {
                flat = flat / d * clamped;
                target.x = head.x + flat.x;
                target.z = head.z + flat.y;   // flat.y 存的是 z 分量
            }
        }

        avatar.transform.position = target;

        // 视线对准用户：选中后人物转向相机（只转水平朝向，不歪头），拖动中持续保持
        FaceCamera();

        // 防穿地：脚底（= 原点下方 feetAboveOrigin 处）不低于地面。
        // 注意是"地面 + 原点高于脚底的距离"——写反会让桌宠往地里沉。
        float feetAboveOrigin = switcher != null ? switcher.OriginAboveFeet : 0.54f;
        float minOriginY = (switcher != null ? switcher.floorClearance : 0.01f) + feetAboveOrigin;
        if (target.y < minOriginY)
        {
            avatar.transform.position = new Vector3(target.x, minOriginY, target.z);
            target.y = minOriginY;
        }

        // 选中期间让激光连到抓取点上，视觉上"牵着"它
        rig.SetLaserEnd(selectedHand, grabOnRay, true);

        // 诊断：每秒打印方向、距离、桌宠位置、脚底高度与"朝向误差"（0° = 正对相机）
        if (Time.time >= nextSelectLog)
        {
            nextSelectLog = Time.time + 1f;

            float facingErr = -1f;
            Vector3 fwd;
            if (avatar != null && TryGetModelForward(out fwd))
            {
                Vector3 toCam = head - avatar.transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.0001f)
                    facingErr = Vector3.Angle(fwd, toCam.normalized);
            }

            Debug.Log("[VRPetInteractor] 选中中 方向=" + dir.ToString("F2") +
                      " 距离=" + selectDistance.ToString("F2") +
                      " 桌宠=" + target.ToString("F2") +
                      " 脚底=" + (target.y - feetAboveOrigin).ToString("F2") +
                      " 朝向误差=" + facingErr.ToString("F0") + "°" +
                      " 摇杆y=" + selectedHand.stick.y.ToString("F2"));
        }
    }

    /// <summary>
    /// 让人物转向相机（只调整水平朝向，保持直立）。
    /// 关键：不猜测模型正面是 +Z 还是 -Z，而是**从骨骼实际推算**——
    /// 由左右脚骨骼连线得到模型的"右方向"，再叉乘上方向得到真实正面朝向，
    /// 因此与 VRM 0.x/1.0、坐标系约定无关，必定转向用户。
    /// </summary>
    private void FaceCamera()
    {
        if (avatar == null) return;
        var cam = hmdCamera != null ? hmdCamera : Camera.main;
        if (cam == null) return;

        Vector3 toCam = cam.transform.position - avatar.transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.0001f) return;
        toCam.Normalize();

        Vector3 modelForward;
        if (!TryGetModelForward(out modelForward))
        {
            // 回退：按模型自身 +Z 当作正面
            modelForward = avatar.transform.forward;
            modelForward.y = 0f;
            if (modelForward.sqrMagnitude < 0.0001f) return;
            modelForward.Normalize();
        }

        // 朝向修正（骨骼推算结果与实际视觉正面相差 180° 时用得到）
        if (Mathf.Abs(facingOffsetDegrees) > 0.01f)
            modelForward = Quaternion.AngleAxis(facingOffsetDegrees, Vector3.up) * modelForward;

        // 按最短角度旋转（水平），带速度限制 → 平滑"转头"
        float angle = Vector3.SignedAngle(modelForward, toCam, Vector3.up);
        float step = Mathf.Clamp(angle, -faceTurnSpeed * Time.deltaTime, faceTurnSpeed * Time.deltaTime);
        avatar.transform.Rotate(0f, step, 0f, Space.World);
    }

    /// <summary>从左右脚（或小腿）骨骼推算模型的世界正面方向；失败返回 false。</summary>
    private bool TryGetModelForward(out Vector3 forward)
    {
        return TryGetModelForward(avatarAnim, out forward);
    }

    /// <summary>
    /// 静态版：不假设模型正面是 +Z 还是 -Z，由骨骼实际推算——
    /// 左右脚连线得到模型"右方向"，叉乘上方向得到真实正面。自动走动等模块复用此方法。
    /// </summary>
    public static bool TryGetModelForward(Animator anim, out Vector3 forward)
    {
        forward = Vector3.forward;
        if (anim == null) return false;

        var l = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var r = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        if (l == null || r == null)
        {
            l = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            r = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        }
        if (l == null || r == null) return false;

        Vector3 right = r.position - l.position;   // 模型自身的"右"
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f) return false;

        forward = Vector3.Cross(right.normalized, Vector3.up).normalized;
        return true;
    }

    private void ShowScaleStatus()
    {
        if (menu == null || switcher == null) return;
        float s = switcher.CurrentScale;
        if (Mathf.Abs(s - lastScaleShown) < 0.01f) return;
        lastScaleShown = s;
        menu.SetStatus("桌宠尺寸 " + Mathf.RoundToInt(s * 100f) + "%");
        Debug.Log("[VRPetInteractor] scale -> " + s.ToString("F2"));
    }

    private void OnAvatarChanged(GameObject newAvatar)
    {
        if (selected) EndSelect();
        if (physics != null) physics.ResetPhysics();
        avatar = newAvatar;
        avatarAnim = avatar != null ? avatar.GetComponent<Animator>() : null;
        avatarHead = null;
        grabCollider = null;
        patting = false;

        if (avatar == null) return;

        avatarHead = ResolveHead();
        BuildGrabCollider();
    }

    /// <summary>
    /// 选中判定用覆盖整个身体的包围盒（而不是只有头部的小球）——
    /// 这样射线指到腹部、腿部、头部任意位置都能选中，符合"点中哪个部位就捏住哪"的直觉。
    /// </summary>
    private void BuildGrabCollider()
    {
        var box = avatar.AddComponent<BoxCollider>();

        Bounds b = switcher != null
            ? switcher.CurrentAvatarBounds
            : new Bounds(avatar.transform.position + Vector3.up, Vector3.one);

        box.center = avatar.transform.InverseTransformPoint(b.center);

        Vector3 ls = avatar.transform.lossyScale;
        float s = Mathf.Max(0.0001f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        box.size = new Vector3(
            Mathf.Max(0.08f, b.size.x / s) + grabPadding * 2f,
            Mathf.Max(0.08f, b.size.y / s) + grabPadding * 2f,
            Mathf.Max(0.08f, b.size.z / s) + grabPadding * 2f);
        box.isTrigger = true;

        grabCollider = box;
    }

    private Transform ResolveHead()
    {
        if (avatarAnim != null)
        {
            var head = avatarAnim.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }
        // 回退：找名字带 Head 的骨骼
        return FindInChildren(avatar.transform, "Head");
    }

    private static Transform FindInChildren(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindInChildren(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>是否正被选中拖拽（供自动走动等功能判断）。</summary>
    public bool IsSelecting { get { return selected; } }

    private void BeginSelect(VRControllerRig.Hand h, Vector3 hitPoint)
    {
        selected = true;
        selectedHand = h;
        if (physics != null) physics.suspended = true;   // 拖拽期间暂停重力，位置完全由手控制

        Vector3 origin = h.position;
        Vector3 dir = rig != null ? rig.AimDirection(h).normalized : h.rotation * Vector3.forward;

        // 抓取参考点取"射线上离模型中心最近的点"（而不是碰撞盒表面的命中点）——
        // 命中点常在模型前方，用它做参考会让桌宠横向拖尾、看起来不贴线。
        Vector3 modelCenter = switcher != null ? switcher.CurrentAvatarBounds.center : avatar.transform.position;
        float t = Vector3.Dot(modelCenter - origin, dir);
        if (t < 0.05f) t = 0.05f;

        selectDistance = Mathf.Clamp(t, minDistanceFromUser, maxDistanceFromUser);

        // 偏移按"钳制后的抓取点"计算 → 保证选中瞬间桌宠一动不动（即使距离被钳制）
        Vector3 grabOnRay = origin + dir * selectDistance;
        grabOffset = avatar.transform.position - grabOnRay;

        if (avatarAnim != null)
        {
            avatarAnim.SetBool(isDraggingParam, true);
            avatarAnim.SetBool(headpatParam, false);
        }
        SetPatting(false);
        PlayDragSound(dragStartClip);   // 原版：抱起时播放抓取音效
        Debug.Log("[VRPetInteractor] select start 参考点=" + grabOnRay.ToString("F2") +
                  " 偏移=" + grabOffset.ToString("F2") + " 距离=" + selectDistance.ToString("F2"));
    }

    /// <summary>
    /// 松开扳机即放下。桌宠就地停在原处（含高度）——用户可能刻意把它放在桌面高度或举高，
    /// 所以不自动落回地面；需要贴地时把控制器放低即可（桌宠平行跟随）。
    /// </summary>
    private void EndSelect()
    {
        selected = false;
        selectedHand = null;

        if (avatarAnim != null)
            avatarAnim.SetBool(isDraggingParam, false);

        // 松手后交给重力：悬空时会自然下坠并轻微弹跳落地（物理组件检测到离地即开始下落）
        if (physics != null)
            physics.suspended = false;

        PlayDragSound(dragStopClip);   // 原版：放下时播放落地音效

        Debug.Log("[VRPetInteractor] select end at " +
                  (avatar != null ? avatar.transform.position.ToString("F2") : "?"));
    }


    private void SetPatting(bool on)
    {
        // 抚摸判定由 VRPettingHandler（原版部位配置 + 原版特效）统一负责，
        // 这里不再设置 Headpat，避免两套逻辑互相覆盖。
        patting = on;
    }

    /// <summary>把桌宠放回初始展示位。</summary>
    public void ResetAvatar()
    {
        if (selected) EndSelect();
        if (avatar == null) return;
        var parent = switcher.SpawnParentOrSelf();
        avatar.transform.SetParent(parent, false);
        avatar.transform.localPosition = switcher.spawnPosition;
        avatar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        avatar.transform.localScale = Vector3.one * switcher.spawnScale;
        switcher.PlaceOnGround();
        if (avatarAnim != null) avatarAnim.SetBool(isDraggingParam, false);
    }
}
