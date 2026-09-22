using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// VR 舞蹈播放器：
/// - 内置舞蹈：isDancing + DanceIndex(0..4)，与桌面版动画控制器共用状态
/// - 自定义舞蹈：从 persistentDataPath/Dances/*.bundle 加载 AnimationClip（可选 AudioClip），
///   通过 AnimatorOverrideController 替换 CUSTOM_DANCE 片段，isCustomDancing 驱动
///   （把含 AnimationClip 的 bundle 用 adb push 到 /sdcard/Android/data/com.mateengine.pico/files/Dances/ 即可）
/// </summary>
public class VRDancePlayer : MonoBehaviour
{
    [Header("引用")]
    public VRModelSwitcher switcher;
    public VRWorldMenu menu;

    [Header("舞蹈拖尾（原版 Trail 对象，构建时导入；跟随双手）")]
    public List<GameObject> danceTrails = new List<GameObject>();
    private float nextParticleLog;
    private bool dancingActive;
    private int activeTrailCount;

    [Header("内置舞蹈")]
    public int builtinDanceCount = 5;

    private readonly List<AnimationClip> customClips = new List<AnimationClip>();
    private readonly List<AudioClip> customSongs = new List<AudioClip>();
    private int builtinIndex = -1;
    private int customIndex = -1;
    private bool customMode;
    private AnimatorOverrideController overrideCtl;
    private AudioSource music;
    private GameObject trackedAvatar;

    private static readonly int isDancingParam = Animator.StringToHash("isDancing");
    private static readonly int isCustomDancingParam = Animator.StringToHash("isCustomDancing");
    private static readonly int danceIndexParam = Animator.StringToHash("DanceIndex");

    private void Start()
    {
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        LoadCustomDances();
    }

    private void Update()
    {
        // 换模型后停止舞蹈，避免 override 控制器串到新模型
        var cur = switcher != null ? switcher.CurrentAvatar : null;
        if (!ReferenceEquals(cur, trackedAvatar))
        {
            trackedAvatar = cur;
            if (customMode || builtinIndex >= 0) DanceStop();
            overrideCtl = null;
        }
    }

    private void LoadCustomDances()
    {
        string dir = Path.Combine(Application.persistentDataPath, "Dances");
        if (!Directory.Exists(dir)) return;
        var bundles = Directory.GetFiles(dir, "*.bundle");
        foreach (var f in bundles)
        {
            AssetBundle ab = null;
            try { ab = AssetBundle.LoadFromFile(f); } catch { }
            if (ab == null) continue;
            var clips = ab.LoadAllAssets<AnimationClip>();
            var songs = ab.LoadAllAssets<AudioClip>();
            if (clips != null) customClips.AddRange(clips);
            if (songs != null) customSongs.AddRange(songs);
        }
        if (customClips.Count > 0)
            Debug.Log("[VRDancePlayer] loaded " + customClips.Count + " custom dances from " + dir);
    }

    public void DanceNext()
    {
        int total = builtinDanceCount + customClips.Count;
        if (total == 0) { if (menu != null) menu.SetStatus("没有可用舞蹈"); return; }

        int next;
        if (customMode) next = customIndex + 1 < customClips.Count ? customIndex + 1 : -1;
        else next = builtinIndex + 1;

        if (next < builtinDanceCount)
            PlayBuiltin(next);
        else
            PlayCustom(next - builtinDanceCount);
    }

    private void PlayBuiltin(int index)
    {
        var anim = CurrentAnimator();
        if (anim == null) { if (menu != null) menu.SetStatus("模型没有 Animator"); return; }
        customMode = false;
        builtinIndex = Mathf.Clamp(index, 0, builtinDanceCount - 1);
        anim.SetBool(isCustomDancingParam, false);
        anim.SetBool(isDancingParam, true);
        anim.SetFloat(danceIndexParam, builtinIndex);
        if (music != null) music.Stop();
        SetParticles(true);
        if (menu != null) menu.SetStatus("内置舞蹈 " + (builtinIndex + 1) + "/" + builtinDanceCount);
        Debug.Log("[VRDancePlayer] builtin dance " + builtinIndex);
    }

    private void PlayCustom(int index)
    {
        var anim = CurrentAnimator();
        if (anim == null || customClips.Count == 0) { DanceNextFallback(); return; }
        customMode = true;
        customIndex = Mathf.Clamp(index, 0, customClips.Count - 1);

        var baseCtl = anim.runtimeAnimatorController;
        if (baseCtl == null) { if (menu != null) menu.SetStatus("模型没有动画控制器"); return; }

        if (overrideCtl == null || overrideCtl.runtimeAnimatorController != baseCtl)
        {
            overrideCtl = new AnimatorOverrideController(baseCtl);
            anim.runtimeAnimatorController = overrideCtl;
        }

        var overrides = overrideCtl.clips;
        bool replaced = false;
        for (int k = 0; k < overrides.Length; k++)
        {
            if (overrides[k].originalClip != null && overrides[k].originalClip.name == "CUSTOM_DANCE")
            {
                overrides[k].overrideClip = customClips[customIndex];
                replaced = true;
                break;
            }
        }
        if (replaced) overrideCtl.clips = overrides;

        anim.SetBool(isDancingParam, false);
        anim.SetBool(isCustomDancingParam, true);
        anim.SetFloat(danceIndexParam, 0f);

        if (music != null)
        {
            music.Stop();
            music.clip = customIndex < customSongs.Count ? customSongs[customIndex] : null;
            if (music.clip != null) music.Play();
        }
        SetParticles(true);
        if (menu != null) menu.SetStatus("自定义舞蹈 " + (customIndex + 1) + "/" + customClips.Count);
        Debug.Log("[VRDancePlayer] custom dance " + customIndex + " replaced=" + replaced);
    }

    private void DanceNextFallback()
    {
        // 没有自定义舞蹈时循环内置
        customMode = false;
        PlayBuiltin(0);
    }

    public void DanceStop()
    {
        var anim = CurrentAnimator();
        if (anim != null)
        {
            anim.SetBool(isDancingParam, false);
            anim.SetBool(isCustomDancingParam, false);
        }
        customMode = false;
        builtinIndex = -1;
        customIndex = -1;
        if (music != null) music.Stop();
        SetParticles(false);
    }

    /// <summary>是否正在跳舞（供抚摸拖尾等模块判断，避免争抢同一个拖尾对象）。</summary>
    public bool IsDancing { get { return dancingActive; } }

    /// <summary>
    /// 舞蹈拖尾：把原版 Trail 对象挂到**双手骨骼**上并激活（用户要的就是"跟着手指的音符拖尾"），
    /// 材质已在构建时换成内置加法混合（保留原贴图），这里再做一次运行时兜底。
    /// </summary>
    private void SetParticles(bool on)
    {
        if (danceTrails == null || danceTrails.Count == 0)
        {
            Debug.LogWarning("[VRDancePlayer] 舞蹈拖尾未注入");
            return;
        }

        var avatar = switcher != null ? switcher.CurrentAvatar : null;
        var anim = CurrentAnimator();
        Transform left = anim != null ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        Transform right = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;

        int active = 0, totalParticles = 0;
        for (int i = 0; i < danceTrails.Count; i++)
        {
            var t = danceTrails[i];
            if (t == null) continue;

            if (on)
            {
                if (avatar == null) continue;

                // 关键：不作为模型骨骼的子物体 —— 换模型时旧模型会被销毁，
                // 子物体会被一起销毁（这就是"换一次模型后拖尾永久消失"的原因）。
                // 这里只把它们收拢到本组件下，位置由 LateUpdate 每帧跟随手部骨骼。
                if (t.transform.parent != transform)
                    t.transform.SetParent(transform, true);

                t.SetActive(true);

                var systems = t.GetComponentsInChildren<ParticleSystem>(true);
                for (int k = 0; k < systems.Length; k++)
                {
                    var em = systems[k].emission;
                    em.enabled = true;

                    var rend = systems[k].GetComponent<ParticleSystemRenderer>();
                    if (rend != null)
                    {
                        rend.enabled = true;
                        // 兜底：确保用内置着色器（原版自定义粒子着色器在 Android 上不渲染）。
                        // 必须覆盖所有材质槽（可能有多个）。
                        var sh = Shader.Find("Sprites/Default");
                        if (sh != null)
                        {
                            var src = rend.sharedMaterial;
                            var mat = new Material(sh) { color = new Color(1f, 1f, 1f, 0.9f) };
                            if (src != null && src.mainTexture != null) mat.mainTexture = src.mainTexture;

                            int slots = Mathf.Max(1, rend.sharedMaterials.Length);
                            var mats = new Material[slots];
                            for (int s = 0; s < slots; s++) mats[s] = mat;
                            rend.sharedMaterials = mats;
                        }
                    }

                    systems[k].Clear();
                    systems[k].Play();
                    totalParticles += systems[k].particleCount;
                }
                active++;
            }
            else
            {
                var systems = t.GetComponentsInChildren<ParticleSystem>(true);
                for (int k = 0; k < systems.Length; k++) systems[k].Stop();
                t.SetActive(false);
            }
        }

        if (on)
        {
            dancingActive = true;
            activeTrailCount = active;
            Debug.Log("[VRDancePlayer] 舞蹈拖尾已开启: " + active + " 个（挂双手），当前粒子 " + totalParticles);
        }
        else
        {
            dancingActive = false;
            activeTrailCount = 0;
        }
    }

    /// <summary>
    /// 每帧让拖尾跟随双手骨骼（位置 + 朝向），并输出诊断信息。
    /// 用"跟随"而不是"挂为子物体"，是为了避免换模型时被连带销毁。
    /// </summary>
    private void LateUpdate()
    {
        if (!dancingActive) return;

        var anim = CurrentAnimator();
        Transform left = anim != null ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        Transform right = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;

        int sys = 0, total = 0, alive = 0;
        string pos = "-";
        for (int i = 0; i < danceTrails.Count; i++)
        {
            var t = danceTrails[i];
            if (t == null) continue;
            if (!t.activeSelf) continue;
            alive++;

            Transform src = (i % 2 == 0) ? left : right;
            if (src != null)
            {
                t.transform.position = src.position;
                t.transform.rotation = src.rotation;
            }

            foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
            {
                sys++;
                total += ps.particleCount;
            }
            if (pos == "-") pos = t.transform.position.ToString("F2");
        }

        if (Time.time >= nextParticleLog)
        {
            nextParticleLog = Time.time + 2f;
            Debug.Log("[VRDancePlayer] 拖尾状态: 激活=" + alive + "/" + danceTrails.Count +
                      " 系统数=" + sys + " 粒子数=" + total +
                      " 左手=" + (left != null ? left.position.ToString("F2") : "无") +
                      " 首个位置=" + pos);
        }
    }

    private Animator CurrentAnimator()
    {
        return switcher != null ? switcher.CurrentAnimator : null;
    }
}
