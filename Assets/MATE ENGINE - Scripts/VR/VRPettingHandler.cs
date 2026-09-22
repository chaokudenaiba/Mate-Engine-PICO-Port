using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VR 版部位抚摸反应（还原原版 PetVoiceReactionHandler）：
/// - 部位数据直接复用原版的 <see cref="PetVoiceReactionHandler.VoiceRegion"/> 结构，
///   构建时从原版场景把配置整份复制过来（语音片段、动画状态、画圈参数一并还原）
/// - 判定方式由"鼠标屏幕坐标"改为"手柄世界坐标"：手到部位中心的距离 ≤ 半径即算抚摸中
/// - 画圈抚摸（patMode）：把手的运动投影到面向用户的平面上累计角度，达阈值才触发
/// </summary>
public class VRPettingHandler : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public VRControllerRig rig;
    [Tooltip("部位判定用的相机（一般是主相机）")]
    public Camera hmdCamera;

    [Header("部位配置（构建时从原版场景复制）")]
    public List<PetVoiceReactionHandler.VoiceRegion> regions = new List<PetVoiceReactionHandler.VoiceRegion>();

    [Header("参数")]
    [Tooltip("触发反应后的冷却时间（秒）")]
    public float cooldown = 1.4f;
    [Tooltip("画圈触发所需角度。原版 540° 需要转很多圈，实际很难触发，这里放宽（设 0 则用原版值）")]
    public float patCircleDegreesOverride = 270f;
    [Tooltip("手停留在部位内多久即触发一次（秒）——保证简单触碰也有反应")]
    public float proximityTriggerTime = 0.45f;
    [Tooltip("画圈判定的最小半径（米，对应原版的像素阈值）")]
    public float patMinRadiusWorld = 0.035f;
    [Tooltip("部位半径的整体缩放（原版数值按真人比例配置）")]
    public float radiusScale = 1f;
    public bool logDiagnostics = true;

    // 运行时
    private Animator anim;
    private GameObject avatar;
    private AudioSource voice;
    private ParticleSystem hearts;
    private readonly Dictionary<PetVoiceReactionHandler.VoiceRegion, float> cooldownUntil
        = new Dictionary<PetVoiceReactionHandler.VoiceRegion, float>();
    private readonly Dictionary<PetVoiceReactionHandler.VoiceRegion, float> patAccum
        = new Dictionary<PetVoiceReactionHandler.VoiceRegion, float>();
    private readonly Dictionary<PetVoiceReactionHandler.VoiceRegion, float> patPrevAngle
        = new Dictionary<PetVoiceReactionHandler.VoiceRegion, float>();
    private readonly Dictionary<PetVoiceReactionHandler.VoiceRegion, bool> wasHovering
        = new Dictionary<PetVoiceReactionHandler.VoiceRegion, bool>();
    private readonly Dictionary<PetVoiceReactionHandler.VoiceRegion, float> insideSince
        = new Dictionary<PetVoiceReactionHandler.VoiceRegion, float>();
    private float nextLog;

    /// <summary>部位名 → 骨骼。原版配置里的枚举值语义不直观，这里按名字显式映射，保证部位正确。</summary>
    private static HumanBodyBones ResolveBone(PetVoiceReactionHandler.VoiceRegion r)
    {
        string n = (r.name ?? "").ToLowerInvariant();
        if (n.Contains("head")) return HumanBodyBones.Head;
        if (n.Contains("intimate") || n.Contains("waist") || n.Contains("hip")) return HumanBodyBones.Hips;
        if (n.Contains("chest")) return HumanBodyBones.Chest;
        if (n.Contains("hand") && n.Contains("left")) return HumanBodyBones.LeftHand;
        if (n.Contains("hand") && n.Contains("right")) return HumanBodyBones.RightHand;
        return r.targetBone;   // 未知名字则沿用原配置值
    }

    private void Start()
    {
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;      // 人声用 2D，避免距离衰减导致听不清
        voice.volume = 1f;

        CreateHeartsParticle();
    }

    private void CreateHeartsParticle()
    {
        var go = new GameObject("PetHearts");
        go.transform.SetParent(transform, false);
        hearts = go.AddComponent<ParticleSystem>();
        var main = hearts.main;
        main.startLifetime = 1.1f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
        main.startColor = new Color(1f, 0.45f, 0.6f, 0.95f);
        main.maxParticles = 40;
        main.gravityModifier = -0.12f;
        main.playOnAwake = false;
        var em = hearts.emission;
        em.rateOverTime = 0f;
        var shape = hearts.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.07f;
        var r = hearts.GetComponent<ParticleSystemRenderer>();
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) r.sharedMaterial = new Material(sh);
        hearts.Stop();
    }

    private void Update()
    {
        var cur = switcher != null ? switcher.CurrentAvatar : null;
        if (cur != avatar)
        {
            avatar = cur;
            anim = avatar != null ? avatar.GetComponent<Animator>() : null;
            cooldownUntil.Clear();
            patAccum.Clear();
            patPrevAngle.Clear();
            wasHovering.Clear();
            if (logDiagnostics && avatar != null)
                Debug.Log("[VRPetting] 切换模型，部位数=" + regions.Count);
        }
        if (avatar == null || rig == null) return;

        var cam = hmdCamera != null ? hmdCamera : Camera.main;
        if (cam == null) return;

        float now = Time.time;

        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region == null) continue;
            if ((region.name ?? "").ToLowerInvariant().Contains("sit")) continue;   // VR 无坐姿状态

            Transform bone = anim != null ? anim.GetBoneTransform(ResolveBone(region)) : null;
            if (bone == null) continue;

            Vector3 center = bone.position + bone.TransformVector(region.offset) + region.worldOffset;
            float radius = Mathf.Max(0.03f, region.hoverRadius * radiusScale * bone.lossyScale.magnitude);

            // 取离该部位最近的手柄
            Vector3 handPos = Vector3.zero;
            float best = float.MaxValue;
            bool any = false;
            for (int h = 0; h < rig.HandCount; h++)
            {
                var hand = rig.hands[h];
                if (hand == null || !hand.tracked) continue;
                float d = Vector3.Distance(hand.position, center);
                if (d < best) { best = d; handPos = hand.position; any = true; }
            }
            if (!any) continue;

            bool inside = best <= radius;

            // 停留计时：简单触碰（不画圈）也能触发一次反应
            if (inside)
            {
                if (!insideSince.ContainsKey(region)) insideSince[region] = now;
            }
            else insideSince.Remove(region);

            bool held = inside && insideSince.TryGetValue(region, out var t0) && (now - t0) >= proximityTriggerTime;

            // 画圈抚摸：在"面向用户"的平面上累计手的环绕角度
            bool patOk = true;
            if (region.patMode)
                patOk = ProcessPat(region, center, handPos, cam, now);

            bool allowed = inside && (patOk || held);
            bool isHovering = wasHovering.TryGetValue(region, out var w) && w;

            if (allowed && !isHovering && now >= GetCooldown(region))
            {
                cooldownUntil[region] = now + cooldown;
                wasHovering[region] = true;

                TriggerAnimation(region);
                PlayVoice(region);
                EmitHearts(center);

                if (logDiagnostics)
                    Debug.Log("[VRPetting] 触发部位「" + region.name + "」距离=" + best.ToString("F3") +
                              " 半径=" + radius.ToString("F3"));
            }
            else if (!allowed && isHovering)
            {
                wasHovering[region] = false;
                ClearAnimation(region);
            }
        }

        // 触碰特效到时隐藏（原版也是临时显示后收起）
        if (hoverEffect != null && hoverEffect.activeSelf && Time.time >= hoverEffectHideAt)
            hoverEffect.SetActive(false);

        // 诊断：特效显示期间每 2 秒打印粒子数量与位置
        if (hoverEffect != null && hoverEffect.activeSelf && Time.time >= nextFxLog)
        {
            nextFxLog = Time.time + 2f;
            int sys = 0, total = 0;
            foreach (var ps in hoverEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                sys++;
                total += ps.particleCount;
            }
            Debug.Log("[VRPetting] 触碰特效状态: 系统数=" + sys + " 粒子数=" + total +
                      " 位置=" + hoverEffect.transform.position.ToString("F2"));
        }

        if (logDiagnostics && Time.time >= nextLog && regions.Count > 0)
        {
            nextLog = Time.time + 8f;
            var r0 = regions[0];
            if (r0 != null && anim != null)
            {
                Transform b0 = anim.GetBoneTransform(ResolveBone(r0));
                Debug.Log("[VRPetting] 部位「" + r0.name + "」→ 骨骼 " + (b0 != null ? b0.name : "未找到") +
                          "，共 " + regions.Count + " 个部位");
            }
        }
    }

    private float GetCooldown(PetVoiceReactionHandler.VoiceRegion r)
    {
        return cooldownUntil.TryGetValue(r, out var t) ? t : 0f;
    }

    /// <summary>画圈抚摸检测（世界空间版）：投影到面向用户的平面并累计环绕角度。</summary>
    private bool ProcessPat(PetVoiceReactionHandler.VoiceRegion region, Vector3 center, Vector3 handPos, Camera cam, float now)
    {
        Vector3 facing = center - cam.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
        facing.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
        Vector3 up = Vector3.Cross(facing, right).normalized;

        Vector3 d = handPos - center;
        Vector2 local = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up));
        float rad = local.magnitude;
        float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;

        float accum = patAccum.TryGetValue(region, out var a) ? a : 0f;
        float prev = patPrevAngle.TryGetValue(region, out var p) ? p : ang;

        if (rad >= patMinRadiusWorld)
        {
            float dAng = Mathf.DeltaAngle(prev, ang);
            accum += Mathf.Abs(dAng);

            // 手停下来一段时间就重置累计
            if (Mathf.Abs(dAng) < 0.05f) accum *= 0.98f;
        }
        patPrevAngle[region] = ang;

        float need = patCircleDegreesOverride > 0f ? patCircleDegreesOverride : region.patCircleDegrees;
        bool ok = accum >= need;
        if (ok)
        {
            accum = 0f;                 // 触发后重新累计
            patAccum[region] = accum;
            return true;
        }

        patAccum[region] = accum;
        return false;
    }

    private void TriggerAnimation(PetVoiceReactionHandler.VoiceRegion region)
    {
        if (anim == null) return;

        if (!string.IsNullOrEmpty(region.hoverAnimationParameter))
            anim.SetBool(region.hoverAnimationParameter, true);
        if (!string.IsNullOrEmpty(region.faceAnimationParameter))
            anim.SetBool(region.faceAnimationParameter, true);

        if (!string.IsNullOrEmpty(region.hoverAnimationState) && string.IsNullOrEmpty(region.hoverAnimationParameter))
        {
            int layer = anim.GetLayerIndex(region.hoverAnimationLayer);
            if (layer >= 0) anim.Play(region.hoverAnimationState, layer, 0f);
        }
    }

    private void ClearAnimation(PetVoiceReactionHandler.VoiceRegion region)
    {
        if (anim == null) return;
        if (!string.IsNullOrEmpty(region.hoverAnimationParameter))
            anim.SetBool(region.hoverAnimationParameter, false);
        if (!string.IsNullOrEmpty(region.faceAnimationParameter))
            anim.SetBool(region.faceAnimationParameter, false);
    }

    private void PlayVoice(PetVoiceReactionHandler.VoiceRegion region)
    {
        if (voice == null) return;
        var clips = region.voiceClips;
        if (clips == null || clips.Count == 0) return;

        var clip = clips[Random.Range(0, clips.Count)];
        if (clip == null) return;
        voice.PlayOneShot(clip);
    }

    [Header("触碰特效（原版场景对象 Particle Group，构建时导入）")]
    public GameObject hoverEffect;
    [Tooltip("特效显示时长（秒）")]
    public float hoverEffectDuration = 2.6f;

    [Header("抚摸时的星辰拖尾（构建时注入，跟随触摸的手）")]
    public GameObject pettingTrail;
    [Tooltip("拖尾持续时间（秒）")]
    public float pettingTrailDuration = 1.8f;
    public VRDancePlayer dance;

    private float hoverEffectHideAt;
    private float nextFxLog;
    private float pettingTrailHideAt;
    private Vector3 lastPetPoint;

    /// <summary>点亮抚摸拖尾（星辰/星座粒子跟随那只手）。</summary>
    private void StartPettingTrail(Vector3 worldPos)
    {
        if (pettingTrail == null) return;

        // 跳舞时拖尾归舞蹈系统使用，避免两边争抢同一个对象
        if (dance != null && dance.IsDancing) return;

        lastPetPoint = worldPos;
        pettingTrailHideAt = Time.time + pettingTrailDuration;

        if (pettingTrail.transform.parent != transform)
            pettingTrail.transform.SetParent(transform, true);
        pettingTrail.transform.position = worldPos;
        pettingTrail.SetActive(true);

        var systems = pettingTrail.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var em = systems[i].emission;
            em.enabled = true;
            systems[i].Clear();
            systems[i].Play();
        }
        Debug.Log("[VRPetting] 抚摸拖尾已点亮（粒子系统 " + systems.Length + " 个）");
    }

    private void LateUpdate()
    {
        if (pettingTrail == null || !pettingTrail.activeSelf) return;

        if (Time.time >= pettingTrailHideAt)
        {
            foreach (var ps in pettingTrail.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop();
            pettingTrail.SetActive(false);
            return;
        }

        // 跟随离触发点最近的那只手
        if (rig == null) return;
        float best = float.MaxValue;
        Vector3 target = lastPetPoint;
        for (int i = 0; i < rig.HandCount; i++)
        {
            var h = rig.hands[i];
            if (h == null || !h.tracked) continue;
            float d = Vector3.Distance(h.position, lastPetPoint);
            if (d < best) { best = d; target = h.position; }
        }
        pettingTrail.transform.position = target;
    }

    private void EmitHearts(Vector3 worldPos)
    {
        // 优先用原版特效对象（参数与材质与原版完全一致）
        if (hoverEffect != null)
        {
            hoverEffect.transform.position = worldPos;
            hoverEffect.transform.rotation = Quaternion.identity;
            hoverEffect.SetActive(true);

            var systems = hoverEffect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var em = systems[i].emission;
                em.enabled = true;

                var rend = systems[i].GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    rend.enabled = true;
                    // 兜底：确保用内置着色器（自定义粒子着色器在 Android 上显示为方块）。
                    // 必须覆盖**所有材质槽**（粒子渲染器可能有多槽）。
                    var sh = Shader.Find("Sprites/Default");
                    if (sh != null)
                    {
                        var src = rend.sharedMaterial;
                        var mat = new Material(sh) { color = new Color(1f, 0.5f, 0.72f, 1f) };
                        if (src != null && src.mainTexture != null) mat.mainTexture = src.mainTexture;

                        int slots = Mathf.Max(1, rend.sharedMaterials.Length);
                        var mats = new Material[slots];
                        for (int k = 0; k < slots; k++) mats[k] = mat;
                        rend.sharedMaterials = mats;
                    }
                }

                systems[i].Clear();
                systems[i].Play();
            }

            // 其它渲染器类型（拖尾/网格/线）同样兜底换内置着色器，保留各自贴图
            var shader2 = Shader.Find("Sprites/Default");
            if (shader2 != null)
            {
                foreach (var r in hoverEffect.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (r is TrailRenderer || r is LineRenderer || r is MeshRenderer)
                    {
                        var src = r.sharedMaterial;
                        var mat = new Material(shader2) { color = Color.white };
                        if (src != null && src.mainTexture != null) mat.mainTexture = src.mainTexture;
                        r.sharedMaterial = mat;
                    }
                }
            }
            hoverEffectHideAt = Time.time + hoverEffectDuration;

            // 同时点亮星辰拖尾：跟着你抚摸的那只手走（跳舞期间由舞蹈系统接管，避免争抢）
            StartPettingTrail(worldPos);
            return;
        }

        // 回退：自建爱心粒子
        if (hearts != null)
        {
            hearts.transform.position = worldPos;
            hearts.Emit(8);
        }
    }
}
