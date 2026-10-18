using UnityEngine;

/// <summary>
/// 让 2D 精灵在一块 UI 面板（World Space Canvas 也算）的矩形范围内自由走动，
/// 并自动朝向移动方向（flipX 翻转）。
///
/// 位移在父级 RectTransform 的局部坐标系（UI 像素）里计算，
/// 因此小人被严格限制在面板内，面板怎么缩放 / 摆放都不影响。
///
/// 前提：动画 Clip 不要驱动 transform 的 position / localScale / flipX，否则会互相覆盖。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class ScreenWanderer : MonoBehaviour
{
    public enum Mode
    {
        RandomWander = 0,
        ExternalInput = 1,
    }

    [Header("活动范围")]
    [Tooltip("小人在哪个矩形范围内活动。留空 = 自动用父级 RectTransform。")]
    public RectTransform area;

    [Tooltip("四周留白（UI 像素）。想让他在某块区域内活动就调大。")]
    public Vector2 padding = new Vector2(40f, 30f);

    [Header("运动")]
    public Mode mode = Mode.RandomWander;

    [Tooltip("移动速度（UI 像素 / 秒）。110 约等于 9 秒横穿 1000 宽的面板。")]
    public float speed = 110f;

    [Tooltip("到达目标点后待机多久（秒），随机取区间值。数值越大，走动频率越低。")]
    public Vector2 idleDuration = new Vector2(2.2f, 5.5f);

    [Tooltip("单次移动的横向跨度（占可活动宽度的比例）。越小越像原地踱步。")]
    [Range(0.05f, 1f)] public float wanderRange = 0.4f;

    [Tooltip("单次移动的纵向跨度（占可活动高度的比例）。")]
    [Range(0.05f, 1f)] public float verticalRange = 0.3f;

    [Header("待机")]
    [Tooltip("待机时上下呼吸的幅度（UI 像素）。0 = 完全静止。")]
    public float idleBobAmplitude = 2f;

    [Tooltip("待机呼吸的周期（秒）。")]
    public float idleBobPeriod = 2.4f;

    [Header("朝向")]
    [Tooltip("美术素材默认朝右？本工程的小人素材朝左，所以默认关闭。素材换成朝右的图时再勾上。")]
    public bool artFacesRight = false;

    [Header("动画")]
    [Tooltip("Animator 里控制「走 / 站」切换的 Bool 参数名。")]
    public string movingParam = "IsMoving";

    [Tooltip("停住时额外冻结 Animator 播放速度（已经有 idle 状态时一般不用勾）。")]
    public bool pauseAnimationWhenIdle = false;

    RectTransform _rt;
    SpriteRenderer _sr;
    Animator _anim;
    RectTransform _area;

    Vector2 _pos;
    Vector2 _target;
    float _idleTimer;
    float _facing = 1f;
    Vector2 _externalInput;
    Sprite _boundsSprite;
    bool _isIdle;
    bool _lastMoving = false;
    float _bobTime;
    float _bobCurrent;

    float _limitX;
    float _limitY;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _sr = GetComponent<SpriteRenderer>();
        _anim = GetComponent<Animator>();

        if (_rt == null)
        {
            Debug.LogError("[ScreenWanderer] 物体上需要 RectTransform，才能被限制在 UI 面板内。", this);
            enabled = false;
            return;
        }

        if (area == null) area = _rt.parent as RectTransform;
        if (area == null)
        {
            Debug.LogError("[ScreenWanderer] 找不到活动范围 area，请手动指定一个 RectTransform。", this);
            enabled = false;
            return;
        }

        _area = area;

        _rt.anchorMin = new Vector2(0.5f, 0.5f);
        _rt.anchorMax = new Vector2(0.5f, 0.5f);

        if (_anim != null && !string.IsNullOrEmpty(movingParam))
        {
            bool found = false;
            for (int i = 0; i < _anim.parameters.Length; i++)
            {
                if (_anim.parameters[i].name == movingParam) { found = true; break; }
            }
            if (!found)
                Debug.LogWarning("[ScreenWanderer] Animator 里没有 Bool 参数 '" + movingParam + "'，走 / 站动画不会切换。", this);
        }

        RefreshLimits();

        _pos = new Vector2(0f, -_limitY * 0.3f);
        _rt.anchoredPosition = _pos;
        PickNewTarget(true);
    }

    void Update()
    {
        if (_sr.sprite != _boundsSprite)
        {
            _boundsSprite = _sr.sprite;
            RefreshLimits();
        }

        float dt = Time.deltaTime;
        float prevX = _pos.x;

        if (mode == Mode.ExternalInput) StepExternal(dt);
        else StepWander(dt);

        _pos.x = Mathf.Clamp(_pos.x, -_limitX, _limitX);
        _pos.y = Mathf.Clamp(_pos.y, -_limitY, _limitY);

        float bobTarget = 0f;
        if (_isIdle && idleBobAmplitude > 0f)
        {
            _bobTime += dt;
            bobTarget = Mathf.Sin(_bobTime / Mathf.Max(0.01f, idleBobPeriod) * Mathf.PI * 2f) * idleBobAmplitude;
        }
        else
        {
            _bobTime = 0f;
        }
        _bobCurrent = Mathf.Lerp(_bobCurrent, bobTarget, 1f - Mathf.Exp(-14f * dt));

        _rt.anchoredPosition = _pos + new Vector2(0f, _bobCurrent);
        UpdateFacing(_pos.x - prevX);
        UpdateAnimatorState();
    }

    void UpdateAnimatorState()
    {
        if (_anim == null) return;

        bool moving = !_isIdle;
        if (moving != _lastMoving)
        {
            _lastMoving = moving;
            if (!string.IsNullOrEmpty(movingParam)) _anim.SetBool(movingParam, moving);
        }

        _anim.speed = (pauseAnimationWhenIdle && _isIdle) ? 0f : 1f;
    }

    void StepWander(float dt)
    {
        if (_idleTimer > 0f)
        {
            _idleTimer -= dt;
            _isIdle = true;
            return;
        }

        _isIdle = false;

        Vector2 to = _target - _pos;
        float dist = to.magnitude;
        float step = speed * dt;

        if (dist <= step)
        {
            _pos = _target;
            _idleTimer = Random.Range(idleDuration.x, idleDuration.y);
            _isIdle = true;
            PickNewTarget(false);
        }
        else
        {
            _pos += to / dist * step;
        }
    }

    void StepExternal(float dt)
    {
        if (_externalInput.sqrMagnitude < 0.0001f)
        {
            _isIdle = true;
            return;
        }

        _isIdle = false;
        _pos += _externalInput.normalized * (speed * dt);
    }

    void PickNewTarget(bool initial)
    {
        for (int i = 0; i < 10; i++)
        {
            float x = _pos.x + Random.Range(-wanderRange, wanderRange) * _limitX;
            float y = _pos.y + Random.Range(-verticalRange, verticalRange) * _limitY;
            Vector2 c = new Vector2(Mathf.Clamp(x, -_limitX, _limitX), Mathf.Clamp(y, -_limitY, _limitY));

            if (initial || Vector2.Distance(c, _pos) > _limitX * 0.15f)
            {
                _target = c;
                return;
            }
        }

        _target = new Vector2(Random.Range(-_limitX, _limitX), Random.Range(-_limitY, _limitY));
    }

    void UpdateFacing(float dx)
    {
        if (Mathf.Abs(dx) > 0.01f) _facing = Mathf.Sign(dx);
        _sr.flipX = artFacesRight ? _facing < 0f : _facing > 0f;
    }

    /// <summary>
    /// 重新计算可活动边界。面板尺寸变了、或者想手动刷新时调一下。
    /// </summary>
    public void RefreshLimits()
    {
        if (_area == null) return;

        float spriteW = 0f;
        float spriteH = 0f;

        if (_sr != null && _sr.sprite != null)
        {
            spriteW = _sr.sprite.rect.width / Mathf.Max(1f, _sr.sprite.pixelsPerUnit);
            spriteH = _sr.sprite.rect.height / Mathf.Max(1f, _sr.sprite.pixelsPerUnit);
        }

        Vector3 areaScale = _area.lossyScale;
        float sx = Mathf.Max(1e-6f, Mathf.Abs(areaScale.x));
        float sy = Mathf.Max(1e-6f, Mathf.Abs(areaScale.y));

        float uiW = spriteW * Mathf.Abs(_rt.lossyScale.x) / sx;
        float uiH = spriteH * Mathf.Abs(_rt.lossyScale.y) / sy;

        Rect r = _area.rect;
        _limitX = Mathf.Max(0f, r.width * 0.5f - padding.x - uiW * 0.5f);
        _limitY = Mathf.Max(0f, r.height * 0.5f - padding.y - uiH * 0.5f);
    }

    /// <summary>由外部脚本喂方向（不必归一化），配合 mode = ExternalInput 使用。</summary>
    public void SetMoveInput(Vector2 direction)
    {
        _externalInput = Vector2.ClampMagnitude(direction, 1f);
    }

    /// <summary>瞬移到面板内的某个位置，参数是 0~1 的归一化坐标（左下角为原点）。</summary>
    public void WarpToNormalized(Vector2 normalized)
    {
        _pos = new Vector2(
            Mathf.Lerp(-_limitX, _limitX, Mathf.Clamp01(normalized.x)),
            Mathf.Lerp(-_limitY, _limitY, Mathf.Clamp01(normalized.y)));
        _target = _pos;
        _idleTimer = 0f;
        _rt.anchoredPosition = _pos;
    }

    /// <summary>让小人朝某个方向（1 = 右，-1 = 左）。</summary>
    public void FaceDirection(float dir)
    {
        if (Mathf.Abs(dir) > 0.001f) _facing = Mathf.Sign(dir);
        if (_sr != null) _sr.flipX = artFacesRight ? _facing < 0f : _facing > 0f;
    }
}
