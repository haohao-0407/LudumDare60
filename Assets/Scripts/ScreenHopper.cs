using System.Collections.Generic;
using LudumDare60.TerminalDialogue;
using UnityEngine;

namespace LudumDare60.Pet
{
    /// <summary>
    /// 把 2D 精灵（桌宠 / 终端 AI 的小人化身）贴在船舱那几块物理显示屏的屏面上活动。
    ///
    /// 行为：
    ///   · 默认不出场；玩家在终端里选定「发送求救信号」那个选项后才出现，终端一关就消失；
    ///   · 出场期间在单块屏内踱步 / 待机，永远被夹在屏幕矩形里，不会跑到屏外；
    ///   · 隔一阵子串门：先走到屏幕边缘，然后挤压 + 淡出「钻进边框」，
    ///     在邻屏对应的那条边挤压 + 淡入「冒出来」—— 全程没有任何一帧出现在非屏幕区域；
    ///   · AI 在终端打字说话时停住不说话，只做小幅抖动（无气泡框）。
    ///
    /// 全部在世界空间计算：每块屏的平面基向量、可用矩形都从它自己的 MeshFilter 网格算出来，
    /// 所以屏幕被挪动 / 旋转 / 重新摆位后仍然正确。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ScreenHopper : MonoBehaviour
    {
        private enum State
        {
            Perch = 0,
            ToEdge = 1,
            Crossing = 2,
        }

        [Header("屏幕（留空则按下面的名字自动找）")]
        [Tooltip("要串门的屏幕显示面。建议直接拖 SCREEN_xxx 这几个网格。")]
        [SerializeField] private Transform[] displays = new Transform[0];

        [Tooltip("displays 为空时按这些物体名去场景里找。")]
        [SerializeField] private string[] displayNames =
        {
            "SCREEN_ATTITUDE", "SCREEN_RADAR", "SCREEN_NAVIGATION",
            "SCREEN_SYSTEMS", "SCREEN_COMMUNICATIONS",
        };

        [Tooltip("距屏幕四边的留白（米）。防止小人贴边或半个身子出画。")]
        [SerializeField] private Vector2 padding = new Vector2(0.02f, 0.02f);

        [Tooltip("离屏幕表面的距离（米）。太小会和屏面深度冲突闪烁，太大像浮在空中。")]
        [SerializeField] private float surfaceOffset = 0.012f;

        [Header("体型")]
        [Tooltip("小人屏幕上的高度（米）。显示屏高约 0.46m，0.07 约等于屏高的 15%。")]
        [SerializeField] private float petHeight = 0.07f;

        [Tooltip("素材默认朝左？本工程的小人朝左，所以默认关闭。")]
        [SerializeField] private bool artFacesRight = false;

        [Header("出场条件")]
        [Tooltip("勾上：只有满足条件的终端对话才出场；关掉：进游戏就在屏上（方便单独调试桌宠）。")]
        [SerializeField] private bool gateByStory = true;

        [Tooltip("留空则运行时自动找场景里的终端对话控制器。")]
        [SerializeField] private TerminalDialogueController controller;

        [Tooltip("玩家选中这些选项后出场。填 TerminalChoice 的 Id（不是显示文字，显示文字会被翻译）。")]
        [SerializeField] private string[] appearChoiceIds = { "briefing_reply_01" };

        [Tooltip("可选兜底：该 Flag 为真时视为已经出场过（应对中途读档 / 对话被跳过）。留空则不用。")]
        [SerializeField] private string appearFlag = "pioneer60.briefing_complete";

        [Tooltip("终端关闭（玩家离开终端）时消失。")]
        [SerializeField] private bool hideWhenTerminalCloses = true;

        [Tooltip("出场时固定出现在哪块屏。默认落在终端那块（SCREEN_NAVIGATION）。")]
        [SerializeField] private string appearScreenName = "SCREEN_NAVIGATION";

        [Header("游走")]
        [Tooltip("屏面内爬行速度（米/秒）。0.06 大约 5~8 秒横穿一块大屏。")]
        [SerializeField] private float walkSpeed = 0.06f;

        [Tooltip("走到目标点后站着发呆多久（秒，随机区间）。")]
        [SerializeField] private Vector2 idleDuration = new Vector2(1.6f, 4.2f);

        [Tooltip("在同一块屏上住多久后去隔壁（秒，随机区间）。")]
        [SerializeField] private Vector2 dwellDuration = new Vector2(7f, 15f);

        [Tooltip("单次走动的跨度（占可活动宽度的比例）。")]
        [Range(0.1f, 1f)] [SerializeField] private float wanderRange = 0.55f;

        [Tooltip("待机呼吸幅度（米）。")]
        [SerializeField] private float idleBobAmplitude = 0.0015f;

        [Tooltip("待机呼吸周期（秒）。")]
        [SerializeField] private float idleBobPeriod = 2.6f;

        [Header("串门（钻屏幕边框，不飞到空中）")]
        [Tooltip("走向屏幕边缘的速度（米/秒）。比闲逛快一点，显得有目的。")]
        [SerializeField] private float edgeApproachSpeed = 0.1f;

        [Tooltip("钻进边框：淡出 + 挤压的时长（秒）。太长会显得黏。")]
        [SerializeField, Min(0.02f)] private float crossOutDuration = 0.1f;

        [Tooltip("从邻屏冒出来：淡入 + 回弹的时长（秒）。")]
        [SerializeField, Min(0.02f)] private float crossInDuration = 0.14f;

        [Tooltip("过渡最扁时的横向压缩比。1 = 不压缩。")]
        [Range(0.05f, 1f)] [SerializeField] private float crossSquash = 0.3f;

        [Tooltip("勾上则允许连着两次去同一块屏。默认关闭，避免来回横跳。")]
        [SerializeField] private bool allowBackToBack = false;

        [Header("AI 说话")]
        [Tooltip("AI 打字时停下来（只做细微抖动）。关掉就一直在逛。")]
        [SerializeField] private bool reactToAI = true;

        [Tooltip("说话抖动幅度（米）。")]
        [SerializeField] private float talkJitterAmplitude = 0.0022f;

        [Tooltip("说话抖动频率（Hz 左右，越大越急）。")]
        [SerializeField] private float talkJitterSpeed = 19f;

        [Header("调试")]
        [SerializeField] private bool logStateChanges = false;

        private struct ScreenPlane
        {
            public Transform transform;
            public Vector3 localCenter;
            public float localDepth;
            public Vector3 center;
            public Vector3 right;
            public Vector3 up;
            public Vector3 normal;
            public float halfWidth;
            public float halfHeight;
            public float usableHalfWidth;
            public float usableHalfHeight;
        }

        private ScreenPlane[] _screens;
        private SpriteRenderer _sr;
        private Animator _anim;
        private Camera _camera;

        private State _state = State.Perch;
        private int _index;
        private int _previousIndex = -1;
        private int _appearIndex;
        private float _u;
        private float _v;
        private Vector2 _targetUV;
        private float _idleTimer;
        private float _dwellTimer;
        private bool _idle;
        private float _bobTime;
        private float _bob;
        private float _facing = 1f;
        private Vector3 _baseScale = Vector3.one;
        private bool _ready;
        private Vector3 _lastPosition;

        private Vector2 _edgeUV;
        private int _crossIndex;
        private float _crossDestU;
        private float _crossDestV;
        private float _crossTime;
        private bool _crossSwapped;

        private bool _talking;
        private bool _summoned;
        private bool _visible;
        private TerminalDialogueSession _watchedSession;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _anim = GetComponent<Animator>();
            _camera = Camera.main;

            ResolveDisplays();
            if (_screens == null || _screens.Length == 0)
            {
                Debug.LogError("[ScreenHopper] 一块屏幕都没找到，检查 displayNames 或手动拖 displays。", this);
                enabled = false;
                return;
            }

            ApplyScale();

            _index = IndexOfScreen(appearScreenName);
            if (_index < 0) _index = IndexOfScreen("SCREEN_NAVIGATION");
            if (_index < 0) _index = 0;
            _appearIndex = _index;

            _u = 0f;
            _v = -_screens[_index].usableHalfHeight * 0.4f;
            _targetUV = new Vector2(_u, _v);
            _idleTimer = Random.Range(idleDuration.x, idleDuration.y);
            _dwellTimer = Random.Range(dwellDuration.x, dwellDuration.y);
            _ready = true;

            SnapToCurrentScreen();
            _lastPosition = transform.position;
            UpdateAnimator(false);

            // 直接落状态，不走 SetVisible：初始值两侧都是 false 会被提前 return 掉。
            _visible = !gateByStory;
            if (_sr != null) _sr.enabled = _visible;
            if (_anim != null) _anim.enabled = _visible;
            if (!_visible) _state = State.Perch;
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (controller == null)
                controller = Object.FindFirstObjectByType<TerminalDialogueController>(FindObjectsInactive.Include);
            if (controller == null) return;
            controller.ChoiceAccepted -= HandleChoiceAccepted;
            controller.ChoiceAccepted += HandleChoiceAccepted;
        }

        private void Unsubscribe()
        {
            if (controller != null) controller.ChoiceAccepted -= HandleChoiceAccepted;
        }

        private void HandleChoiceAccepted(string nodeId, string choiceId)
        {
            if (!gateByStory || _summoned) return;
            if (appearChoiceIds == null) return;

            for (var i = 0; i < appearChoiceIds.Length; i++)
            {
                if (!string.Equals(appearChoiceIds[i], choiceId)) continue;
                _summoned = true;
                if (logStateChanges)
                    Debug.Log("[ScreenHopper] 剧情触发出场：" + (string.IsNullOrEmpty(nodeId) ? "?" : nodeId)
                              + " / " + choiceId, this);
                return;
            }
        }

        private void Update()
        {
            if (!_ready) return;

            var dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            RefreshPlanes();
            UpdatePresence();
            if (!_visible) return;

            _talking = DetectAITalking();

            switch (_state)
            {
                case State.Crossing:
                    StepCross(dt);
                    break;

                case State.ToEdge:
                    if (_talking) { _state = State.Perch; _idle = true; }
                    else StepToEdge(dt);
                    break;

                default:
                    if (_talking) _idle = true;
                    else StepPerch(dt);
                    break;
            }

            if (_state != State.Crossing)
            {
                UpdateBob(dt);
                ApplyOnSurface();

                var velocity = (transform.position - _lastPosition) / dt;
                UpdateFacing(velocity);
            }

            _lastPosition = transform.position;
            UpdateAnimator(!_idle);
        }

        // ------------------------------------------------------------------ 出场 / 退场

        private void UpdatePresence()
        {
            if (!gateByStory) return;

            if (controller == null)
            {
                controller = Object.FindFirstObjectByType<TerminalDialogueController>(FindObjectsInactive.Include);
                if (controller == null) return;
                Subscribe();
            }

            var session = controller.Session;
            if (!ReferenceEquals(session, _watchedSession))
            {
                // 换了会话 = 重开了一局，出场状态一并重置。
                _watchedSession = session;
                _summoned = false;
                if (session != null && session.IsOpen) _summoned = HasSummonFlag(session);
            }

            if (session == null) { SetVisible(false); return; }

            if (!_summoned && session.IsOpen && HasSummonFlag(session)) _summoned = true;

            var shouldShow = session.IsOpen;
            if (_summoned && !session.IsOpen && hideWhenTerminalCloses) shouldShow = false;
            SetVisible(_summoned && shouldShow);
        }

        private bool HasSummonFlag(TerminalDialogueSession session)
        {
            if (string.IsNullOrEmpty(appearFlag)) return false;
            return session.GetFlag(appearFlag);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;

            if (_sr != null) _sr.enabled = visible;
            if (_anim != null) _anim.enabled = visible;

            if (visible)
            {
                WarpToScreen(_appearIndex);
                _lastPosition = transform.position;
                if (logStateChanges) Debug.Log("[ScreenHopper] 出场", this);
            }
            else
            {
                _talking = false;
                _state = State.Perch;
                ResetVisual();
                transform.localScale = _baseScale;
                if (logStateChanges) Debug.Log("[ScreenHopper] 退场", this);
            }
        }

        // ------------------------------------------------------------------ 屏幕采样

        /// <summary>重新扫描屏幕（屏幕被挪动 / 重新摆位后可在 Inspector 里点一下）。</summary>
        [ContextMenu("重新扫描屏幕")]
        public void ResolveDisplays()
        {
            var sources = new List<Transform>();

            if (displays != null)
                for (var i = 0; i < displays.Length; i++)
                    if (displays[i] != null) sources.Add(displays[i]);

            if (sources.Count == 0 && displayNames != null)
            {
                for (var i = 0; i < displayNames.Length; i++)
                {
                    if (string.IsNullOrEmpty(displayNames[i])) continue;
                    var go = GameObject.Find(displayNames[i]);
                    if (go != null) sources.Add(go.transform);
                    else Debug.LogWarning("[ScreenHopper] 场景里找不到名为 " + displayNames[i] + " 的物体。", this);
                }
            }

            var list = new List<ScreenPlane>(sources.Count);
            for (var i = 0; i < sources.Count; i++)
            {
                var plane = BuildPlane(sources[i]);
                if (plane.halfWidth > 0.001f && plane.halfHeight > 0.001f) list.Add(plane);
            }

            _screens = list.ToArray();
        }

        /// <summary>
        /// 每帧把缓存的屏幕世界坐标刷一遍。
        /// 船舱 / 飞船整体会被移走（着陆、撞击、震动），缓存的旧世界坐标会让人偶留在原地不动。
        /// </summary>
        private void RefreshPlanes()
        {
            if (_screens == null) return;

            for (var i = 0; i < _screens.Length; i++)
            {
                var t = _screens[i].transform;
                if (t == null) continue;
                _screens[i].center = t.TransformPoint(_screens[i].localCenter);
                _screens[i].right = t.right;
                _screens[i].up = t.up;
                _screens[i].normal = t.forward;
            }
        }

        private ScreenPlane BuildPlane(Transform t)
        {
            var plane = new ScreenPlane
            {
                transform = t,
                right = t.right,
                up = t.up,
                normal = t.forward,
                halfWidth = 0f,
                halfHeight = 0f,
            };

            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var bounds = mf.sharedMesh.bounds;
                plane.localCenter = bounds.center;
                plane.localDepth = bounds.center.z + Mathf.Abs(bounds.extents.z);
                plane.center = t.TransformPoint(bounds.center);
                var lossy = t.lossyScale;
                plane.halfWidth = Mathf.Abs(bounds.extents.x * lossy.x);
                plane.halfHeight = Mathf.Abs(bounds.extents.y * lossy.y);
            }
            else
            {
                var r = t.GetComponent<Renderer>();
                if (r == null) return plane;
                var b = r.bounds;
                plane.center = b.center;
                plane.localCenter = t.InverseTransformPoint(b.center);
                plane.halfWidth = Vector3.Dot(b.extents, Abs(plane.right));
                plane.halfHeight = Vector3.Dot(b.extents, Abs(plane.up));
            }

            // 注意：这里要扣的是"缩放之后"的实际占地，不是 1 倍缩放下的精灵尺寸。
            plane.usableHalfWidth = Mathf.Max(0.005f, plane.halfWidth - padding.x - PetWorldWidth() * 0.5f);
            plane.usableHalfHeight = Mathf.Max(0.005f, plane.halfHeight - padding.y - petHeight * 0.5f);
            return plane;
        }

        /// <summary>小人缩放之后在世界上占的宽度（米）。</summary>
        private float PetWorldWidth()
        {
            if (_sr == null || _sr.sprite == null) return petHeight;
            var rect = _sr.sprite.rect;
            if (rect.height <= 0.001f) return petHeight;
            return petHeight * (rect.width / rect.height);
        }

        private float SpriteWorldHeight()
        {
            if (_sr == null || _sr.sprite == null) return 1f;
            return _sr.sprite.rect.height / Mathf.Max(1f, _sr.sprite.pixelsPerUnit);
        }

        private void ApplyScale()
        {
            var h = SpriteWorldHeight();
            if (h <= 0.0001f) return;
            _baseScale = Vector3.one * (petHeight / h);
            transform.localScale = _baseScale;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private int IndexOfScreen(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return -1;
            for (var i = 0; i < _screens.Length; i++)
                if (_screens[i].transform.name == objectName) return i;
            return -1;
        }

        private Vector3 WorldOn(ScreenPlane plane, float u, float v, float extra)
        {
            return plane.center + plane.right * u + plane.up * v
                 + plane.normal * (surfaceOffset + extra);
        }

        private static Quaternion RotationOn(ScreenPlane plane)
        {
            return Quaternion.LookRotation(plane.normal, plane.up);
        }

        private void SnapToCurrentScreen()
        {
            var plane = _screens[_index];
            AttachTo(plane);
            transform.localPosition = new Vector3(_u, _v, plane.localDepth + surfaceOffset);
            transform.localScale = _baseScale;
            ResetVisual();
            _state = State.Perch;
            _idle = true;
            _bob = 0f;
        }

        // ------------------------------------------------------------------ 屏上巡游

        private void StepPerch(float dt)
        {
            var plane = _screens[_index];

            _dwellTimer -= dt;
            if (_dwellTimer <= 0f)
            {
                PlanCross();
                return;
            }

            if (_idleTimer > 0f)
            {
                _idleTimer -= dt;
                _idle = true;
                return;
            }

            var to = _targetUV - new Vector2(_u, _v);
            var distance = to.magnitude;
            var step = walkSpeed * dt;

            if (distance <= step)
            {
                _u = _targetUV.x;
                _v = _targetUV.y;
                _idle = true;
                _idleTimer = Random.Range(idleDuration.x, idleDuration.y);
                PickNewTarget(plane);
            }
            else
            {
                _idle = false;
                var delta = to / distance * step;
                _u += delta.x;
                _v += delta.y;
            }
        }

        private void PickNewTarget(ScreenPlane plane)
        {
            for (var i = 0; i < 10; i++)
            {
                var u = _u + Random.Range(-wanderRange, wanderRange) * plane.usableHalfWidth;
                var v = _v + Random.Range(-wanderRange, wanderRange) * plane.usableHalfHeight * 0.7f;
                var candidate = new Vector2(
                    Mathf.Clamp(u, -plane.usableHalfWidth, plane.usableHalfWidth),
                    Mathf.Clamp(v, -plane.usableHalfHeight, plane.usableHalfHeight));

                if (Vector2.Distance(candidate, new Vector2(_u, _v)) > plane.usableHalfWidth * 0.15f)
                {
                    _targetUV = candidate;
                    return;
                }
            }

            _targetUV = new Vector2(
                Random.Range(-plane.usableHalfWidth, plane.usableHalfWidth),
                Random.Range(-plane.usableHalfHeight, plane.usableHalfHeight));
        }

        private void ApplyOnSurface()
        {
            var plane = _screens[_index];
            AttachTo(plane);
            // 只写屏幕的局部坐标：贴在屏面上，不跟世界坐标 / 相机视角产生位移或视差
            // 呼吸/说话抖动走屏面内的上下方向，不再往镜头方向推（那样会又变成视差）
            transform.localPosition = new Vector3(_u, _v + _bob, plane.localDepth + surfaceOffset);
            transform.localScale = _baseScale;
            ResetVisual();
        }

        /// <summary>把精灵挂到屏幕物体下。挂上之后位置只相对屏面算，船怎么晃、屏怎么摆都不影响。</summary>
        private void AttachTo(ScreenPlane plane)
        {
            if (plane.transform == null) return;
            if (transform.parent != plane.transform) transform.SetParent(plane.transform, false);
            transform.localRotation = Quaternion.identity;   // 屏面朝 +Z，精灵默认也朝 +Z
        }

        private void ResetVisual()
        {
            if (_sr == null) return;
            var c = _sr.color;
            if (!Mathf.Approximately(c.a, 1f))
            {
                c.a = 1f;
                _sr.color = c;
            }
        }

        private void UpdateBob(float dt)
        {
            float target;
            if (_talking)
            {
                // 说话时快速小幅抖动，像在絮絮叨叨。
                _bobTime += dt;
                target = talkJitterAmplitude * 0.8f
                         + Mathf.Sin(_bobTime * talkJitterSpeed) * talkJitterAmplitude;
            }
            else if (_idle && idleBobAmplitude > 0f)
            {
                _bobTime += dt;
                target = Mathf.Sin(_bobTime / Mathf.Max(0.05f, idleBobPeriod) * Mathf.PI * 2f) * idleBobAmplitude;
            }
            else
            {
                _bobTime = 0f;
                target = 0f;
            }

            _bob = Mathf.Lerp(_bob, target, 1f - Mathf.Exp(-16f * dt));
        }

        // ------------------------------------------------------------------ 串门：钻边框

        private int PickNextScreen()
        {
            if (_screens.Length <= 1) return _index;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var pick = Random.Range(0, _screens.Length);
                if (pick == _index) continue;
                if (!allowBackToBack && pick == _previousIndex) continue;
                return pick;
            }

            return (_index + 1) % _screens.Length;
        }

        /// <summary>选好下一块屏，算好「从哪条边钻出去 / 从哪条边冒出来」，然后开始走过去。</summary>
        private void PlanCross()
        {
            if (_screens.Length <= 1) return;

            var from = _screens[_index];
            var next = PickNextScreen();
            var to = _screens[next];

            // 目标屏在源屏自身坐标系里的方位，决定走哪条边（横/纵取主轴向）。
            var relative = to.center - from.center;
            var du = Vector3.Dot(relative, from.right);
            var dv = Vector3.Dot(relative, from.up);

            float exitU, exitV, destU, destV;

            if (Mathf.Abs(du) >= Mathf.Abs(dv))
            {
                var sign = du >= 0f ? 1f : -1f;
                exitU = sign * from.usableHalfWidth;
                exitV = Mathf.Clamp(_v, -from.usableHalfHeight, from.usableHalfHeight);

                var ratio = from.usableHalfHeight > 1e-4f ? exitV / from.usableHalfHeight : 0f;
                destU = -sign * to.usableHalfWidth;
                destV = Mathf.Clamp(ratio * to.usableHalfHeight, -to.usableHalfHeight, to.usableHalfHeight);
            }
            else
            {
                var sign = dv >= 0f ? 1f : -1f;
                exitV = sign * from.usableHalfHeight;
                exitU = Mathf.Clamp(_u, -from.usableHalfWidth, from.usableHalfWidth);

                var ratio = from.usableHalfWidth > 1e-4f ? exitU / from.usableHalfWidth : 0f;
                destV = -sign * to.usableHalfHeight;
                destU = Mathf.Clamp(ratio * to.usableHalfWidth, -to.usableHalfWidth, to.usableHalfWidth);
            }

            _edgeUV = new Vector2(exitU, exitV);
            _crossIndex = next;
            _crossDestU = destU;
            _crossDestV = destV;
            _state = State.ToEdge;
            _idle = false;

            if (logStateChanges)
                Debug.Log("[ScreenHopper] 去隔壁：" + from.transform.name + " -> " + to.transform.name, this);
        }

        private void StepToEdge(float dt)
        {
            var to = _edgeUV - new Vector2(_u, _v);
            var distance = to.magnitude;
            var step = edgeApproachSpeed * dt;

            if (distance <= step)
            {
                _u = _edgeUV.x;
                _v = _edgeUV.y;
                BeginCross();
                return;
            }

            var delta = to / distance * step;
            _u += delta.x;
            _v += delta.y;
            _idle = false;
        }

        private void BeginCross()
        {
            _state = State.Crossing;
            _crossTime = 0f;
            _crossSwapped = false;
            _idle = false;
        }

        private void StepCross(float dt)
        {
            var outDuration = Mathf.Max(0.02f, crossOutDuration);
            var inDuration = Mathf.Max(0.02f, crossInDuration);
            _crossTime += dt;

            if (!_crossSwapped && _crossTime >= outDuration)
            {
                // 此刻完全透明（alpha = 0），换屏不会有任何可见的跳变。
                _previousIndex = _index;
                _index = _crossIndex;
                _u = _crossDestU;
                _v = _crossDestV;
                _targetUV = new Vector2(_u, _v);

                var plane = _screens[_index];
                AttachTo(plane);
                transform.localPosition = new Vector3(_u, _v, plane.localDepth + surfaceOffset);
                transform.localScale = _baseScale;
                _crossSwapped = true;
            }

            float alpha;
            float squash;
            if (_crossTime < outDuration)
            {
                var k = _crossTime / outDuration;
                alpha = 1f - k;
                squash = Mathf.Lerp(1f, crossSquash, k * k);
            }
            else
            {
                var k = Mathf.Clamp01((_crossTime - outDuration) / inDuration);
                alpha = k;
                squash = Mathf.Lerp(crossSquash, 1f, 1f - (1f - k) * (1f - k));
            }

            ApplyCrossVisual(alpha, squash);

            if (_crossTime < outDuration + inDuration) return;

            _state = State.Perch;
            _idle = true;
            _bob = 0f;
            _bobTime = 0f;
            _idleTimer = Random.Range(idleDuration.x, idleDuration.y) * 0.6f;
            _dwellTimer = Random.Range(dwellDuration.x, dwellDuration.y);
            ApplyOnSurface();
        }

        private void ApplyCrossVisual(float alpha, float squash)
        {
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = Mathf.Clamp01(alpha);
                _sr.color = c;
            }

            // 横向压扁 + 纵向微微拉长 = 被边框挤了一下。
            transform.localScale = new Vector3(
                _baseScale.x * squash,
                _baseScale.y * (1f + (1f - squash) * 0.16f),
                _baseScale.z);
        }

        // ------------------------------------------------------------------ 表现

        private void UpdateFacing(Vector3 worldVelocity)
        {
            if (_sr == null) return;

            var lateral = Vector3.Dot(worldVelocity, transform.right);
            if (Mathf.Abs(lateral) > 0.0015f) _facing = Mathf.Sign(lateral);
            _sr.flipX = artFacesRight ? _facing < 0f : _facing > 0f;
        }

        private void UpdateAnimator(bool moving)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return;
            _anim.SetBool("IsMoving", moving);
        }

        // ------------------------------------------------------------------ AI 说话

        private bool DetectAITalking()
        {
            if (!reactToAI) return false;
            if (controller == null) return false;

            var session = controller.Session;
            if (session == null || !session.IsOpen) return false;
            if (session.Phase != TerminalPhase.Typing && session.Phase != TerminalPhase.Holding) return false;

            var messages = session.Messages;
            if (messages == null || messages.Count == 0) return false;

            var last = messages[messages.Count - 1];
            if (last == null || last.Speaker != TerminalSpeaker.AI || string.IsNullOrEmpty(last.Text)) return false;

            return session.VisibleCodeUnits > 0;
        }

        // ------------------------------------------------------------------ 外部接口

        /// <summary>立刻把小人放到第 N 块屏（0 起，按 displays 顺序）。</summary>
        public void WarpToScreen(int screenIndex)
        {
            if (!_ready || _screens == null || _screens.Length == 0) return;
            _index = Mathf.Clamp(screenIndex, 0, _screens.Length - 1);
            _previousIndex = -1;
            _u = 0f;
            _v = -_screens[_index].usableHalfHeight * 0.4f;
            _targetUV = new Vector2(_u, _v);
            _dwellTimer = Random.Range(dwellDuration.x, dwellDuration.y);
            _idleTimer = Random.Range(idleDuration.x, idleDuration.y);
            SnapToCurrentScreen();
            _lastPosition = transform.position;
        }

        /// <summary>立刻开始一次串门（不用等 dwell 计时）。</summary>
        public void RequestCross()
        {
            if (!_ready || !_visible || _state != State.Perch) return;
            PlanCross();
        }

        /// <summary>手动让它出场（给 UnityEvent 或别处脚本用）。</summary>
        public void Summon()
        {
            _summoned = true;
            SetVisible(true);
        }

        /// <summary>手动让它退场。</summary>
        public void Dismiss()
        {
            _summoned = false;
            SetVisible(false);
        }

        private void OnDrawGizmosSelected()
        {
            if (_screens == null) return;

            for (var i = 0; i < _screens.Length; i++)
            {
                var p = _screens[i];
                if (p.transform == null) continue;

                Gizmos.matrix = Matrix4x4.TRS(p.center, RotationOn(p), Vector3.one);
                Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.85f);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(p.halfWidth * 2f, p.halfHeight * 2f, 0.001f));
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(Vector3.zero,
                    new Vector3(p.usableHalfWidth * 2f, p.usableHalfHeight * 2f, 0.002f));
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
