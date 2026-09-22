using UnityEngine;

/// <summary>
/// 基础动画参数驱动 —— 还原原版 AvatarAnimatorController 中"驱动动画状态机基础参数"的那部分。
///
/// 背景：原版桌宠的 AvatarAnimatorController 在 OnEnable 里会设置 isMale/isFemale，
/// 并由协程维持 isIdle 与轮换 IdleIndex。移植版的 VR 场景里没有这个组件，导致：
/// - isMale/isFemale 恒为 0 → Gender 混合树在 0 位置混合，得到畸形姿势
/// - isIdle 从未被设置 → 待机状态进不去，跳舞/拖拽等状态也切不回来
/// 表现就是"人物卡在一个奇怪姿势、点什么都不动"。
/// </summary>
public class VRAvatarDriver : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public VRDancePlayer dance;
    public VRPetInteractor interactor;

    [Header("参数名（与原版控制器一致）")]
    public string isIdleParam = "isIdle";
    public string idleIndexParam = "IdleIndex";
    public string isMaleParam = "isMale";
    public string isFemaleParam = "isFemale";

    [Header("待机")]
    [Tooltip("待机动画总数（原版默认 10）")]
    public int totalIdleAnimations = 10;
    [Tooltip("待机切换间隔（秒，原版 IDLE_SWITCH_TIME = 12）")]
    public float idleSwitchTime = 12f;
    [Tooltip("默认使用女性骨骼/动画（对应原版 enableHusbandoMode = false）")]
    public bool husbandoMode = false;
    public bool logDiagnostics = true;

    private Animator anim;
    private GameObject avatar;
    private float nextIdleSwitch;
    private int idleIndex;
    private float nextLog;
    private int initHoldFrames;
    private RuntimeAnimatorController lastCtl;
    private static readonly int isIdleHash = Animator.StringToHash("isIdle");
    private static readonly int idleIndexHash = Animator.StringToHash("IdleIndex");
    private static readonly int isMaleHash = Animator.StringToHash("isMale");
    private static readonly int isFemaleHash = Animator.StringToHash("isFemale");
    private static readonly int isDraggingHash = Animator.StringToHash("isDragging");
    private static readonly int isDancingHash = Animator.StringToHash("isDancing");
    private static readonly int isCustomDancingHash = Animator.StringToHash("isCustomDancing");

    private void Update()
    {
        var cur = switcher != null ? switcher.CurrentAvatar : null;
        if (cur != avatar)
        {
            avatar = cur;
            anim = avatar != null ? avatar.GetComponent<Animator>() : null;
            if (anim != null)
            {
                InitAvatar(anim);
                initHoldFrames = 90;    // 约 1 秒内持续重设：新模型分配控制器会触发重绑定并清空参数
                Debug.Log("[VRAvatarDriver] 模型就绪：已设置 isFemale=" + (husbandoMode ? 0 : 1) +
                          " isIdle=true IdleIndex=" + idleIndex +
                          " 控制器=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null"));
            }
        }
        if (anim == null) return;

        // 控制器被切换（例如自动走动切到走路控制器）会重绑定并清空参数 —— 检测到就重新应用
        if (anim.runtimeAnimatorController != lastCtl)
        {
            lastCtl = anim.runtimeAnimatorController;
            initHoldFrames = 90;
        }

        // 跨过重绑定窗口：持续重设性别与待机索引
        if (initHoldFrames > 0)
        {
            initHoldFrames--;
            anim.SetFloat(isFemaleHash, husbandoMode ? 0f : 1f);
            anim.SetFloat(isMaleHash, husbandoMode ? 1f : 0f);
            anim.SetFloat(idleIndexHash, idleIndex);
        }

        // 被选中拖拽 / 跳舞时不算待机；否则维持 isIdle=true（原版行为）
        bool dragging = interactor != null && interactor.IsSelecting;
        bool dancing = anim.GetBool(isDancingHash) || anim.GetBool(isCustomDancingHash);

        if (dragging || dancing)
        {
            anim.SetBool(isIdleHash, false);
        }
        else
        {
            anim.SetBool(isIdleHash, true);

            if (Time.time >= nextIdleSwitch && totalIdleAnimations > 0)
            {
                nextIdleSwitch = Time.time + idleSwitchTime;
                idleIndex = (idleIndex + 1) % totalIdleAnimations;
                anim.SetFloat(idleIndexHash, idleIndex);
            }
        }

        if (logDiagnostics && Time.time >= nextLog)
        {
            nextLog = Time.time + 10f;
            Debug.Log("[VRAvatarDriver] isIdle=" + anim.GetBool(isIdleHash) +
                      " isDragging=" + anim.GetBool(isDraggingHash) +
                      " isDancing=" + anim.GetBool(isDancingHash) +
                      " isCustomDancing=" + anim.GetBool(isCustomDancingHash) +
                      " IdleIndex=" + anim.GetFloat(idleIndexHash) +
                      " 控制器=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null") +
                      " 位置=" + avatar.transform.position.ToString("F2"));
        }
    }

    private void InitAvatar(Animator a)
    {
        // 保险：确保动画组件启用、且挂在基础动画控制器上（走路控制器没有这些参数，写进去会全部无效）
        a.enabled = true;
        if (switcher != null && switcher.animatorController != null &&
            a.runtimeAnimatorController != switcher.animatorController)
        {
            Debug.LogWarning("[VRAvatarDriver] 发现模型不在基础控制器上，已切回");
            a.runtimeAnimatorController = switcher.animatorController;
        }

        a.SetFloat(isFemaleHash, husbandoMode ? 0f : 1f);
        a.SetFloat(isMaleHash, husbandoMode ? 1f : 0f);
        a.SetBool(isIdleHash, true);

        idleIndex = totalIdleAnimations > 0 ? Random.Range(0, totalIdleAnimations) : 0;
        a.SetFloat(idleIndexHash, idleIndex);
        nextIdleSwitch = Time.time + idleSwitchTime;

        // 自检：确认参数确实存在于该动画组件（哈希不匹配时 Set/Get 都是空操作）
        var has = new System.Text.StringBuilder();
        var pars = a.parameters;
        for (int i = 0; i < pars.Length && i < 8; i++)
            has.Append(pars[i].name).Append('(').Append(pars[i].type.ToString("d")).Append(") ");
        Debug.Log("[VRAvatarDriver] 自检 enabled=" + a.enabled + " avatar=" + (a.avatar != null ? a.avatar.name : "null") +
                  " 参数数=" + pars.Length + " 控制器=" + (a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null") +
                  " | " + has);
    }
}
