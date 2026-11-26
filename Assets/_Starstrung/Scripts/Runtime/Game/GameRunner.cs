using System;
using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;
using UnityEngine.XR;

namespace Starstrung
{
    /// <summary>
    /// 一局游戏的运行器：每帧读输入（编辑器 DeskSim / 头显 PicoHandInput）→ 换算坐标 → 推进纯逻辑 Game → 分发事件给表现层。
    /// 坐标约定：逻辑层是 three.js 习惯（面朝 -Z），Unity 面朝 +Z —— 换算只是 z 取反（左右不镜像）。
    /// 逻辑原点挂在 rig 上：头显里开局按玩家的位置和朝向校准 rig，编辑器里 rig 就在世界原点。
    /// </summary>
    public class GameRunner : MonoBehaviour
    {
        public Camera cam;
        public Transform rig;
        public DeskSim desk;
        public PicoHandInput pico;
        public InputTuning tuning;
        public string levelId = "T";
        [Tooltip("开场直接进关卡（只在单独测试玩法时勾；正常由 AppFlow 控制流程）")] public bool autoStart;

        public Game Game { get; private set; }
        public event Action<GameEvent> OnEvent;
        public event Action OnLevelStart;
        /// <summary>这一帧送进逻辑层的输入（表现层画手、射线用）</summary>
        public FrameInput LastInput { get; private set; }
        public bool InXR => XRSettings.isDeviceActive;

        float calibrateIn = -1f;
        bool lastFloorSet;
        /// <summary>暂停：不推进逻辑（手还照常读，这样恢复时不会突然跳一下）</summary>
        [System.NonSerialized] public bool paused;
        /// <summary>顿帧（hit-stop）：连锁消除的那一下让整串星停住几十毫秒，“咔嗒”一下的打击感</summary>
        float hitStop;
        public void HitStop(float seconds) => hitStop = Mathf.Max(hitStop, seconds);

        // ───── 坐标换算 ─────
        public static Vector3 ToLocal(Vec3 v) => new((float)v.X, (float)v.Y, (float)-v.Z);
        public static Vec3 ToLogic(Vector3 v) => new(v.x, v.y, -v.z);
        public Vector3 ToWorld(Vec3 v) => rig ? rig.TransformPoint(ToLocal(v)) : ToLocal(v);
        public Vec3 FromWorld(Vector3 w) => ToLogic(rig ? rig.InverseTransformPoint(w) : w);
        public Vec3 DirFromWorld(Vector3 d) => ToLogic(rig ? rig.InverseTransformDirection(d) : d);

        void Start()
        {
            ApplyTuning();
            if (desk) desk.snapTargets = SnapTargets;
            if (autoStart) StartLevel(levelId);
            if (InXR) calibrateIn = 0.6f;
        }

        /// <summary>把 InputTuning 里的阈值写进逻辑层（真机装机后改资产即可生效）</summary>
        public void ApplyTuning()
        {
            if (!tuning) return;
            PICK.NearRadius = tuning.nearRadius; PICK.PullOut = tuning.pullOut; PICK.ConeDeg = tuning.coneDeg;
            PICK.HysteresisDeg = tuning.hysteresisDeg; PICK.Lookback = tuning.lookback; PICK.GazeDeg = tuning.gazeDeg;
            HAND.FreezeAfter = tuning.freezeAfter; HAND.RaiseHint = tuning.raiseHint;
        }

        public void StartLevel(string id)
        {
            ApplyTuning();
            var L = Levels.LevelById(id) ?? Levels.LEVELS[0];
            levelId = L.Id;
            Game = new Game(L);
            OnLevelStart?.Invoke();
        }

        /// <summary>退出关卡（回到标题 / 选关）：星串、路线都撤掉</summary>
        public void Unload()
        {
            Game = null;
            OnLevelStart?.Invoke();
        }

        /// <summary>当前两只手（头显 = PICO 手部追踪，编辑器 = 模拟手）</summary>
        public void ReadHands(out HandState l, out HandState r)
        {
            bool xr = InXR && pico && pico.Active;
            l = xr ? pico.Left : desk ? desk.Left : default;
            r = xr ? pico.Right : desk ? desk.Right : default;
        }

        /// <summary>头显：world 原点对齐玩家脚下、眼高对齐 1.6 m、朝向对齐当前面朝</summary>
        public void Calibrate()
        {
            if (!rig || !cam) return;
            TargetPose(out var p, out var q);
            rig.SetPositionAndRotation(p, q);
            recenterT = -1f;
        }

        void TargetPose(out Vector3 pos, out Quaternion rot)
        {
            var p = cam.transform.position;
            var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            pos = new Vector3(p.x, p.y - (float)LAYOUT.Head.Y, p.z); rot = Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        // ── 重新对准（11-25：开局只对准一次，玩家当时侧着身 / 坐着 / 在调头带，整局都会歪） ──
        // 平滑：整个星界 0.5 秒内转到玩家现在面朝的方向，不瞬间跳（瞬移会晕）
        float recenterT = -1f; Vector3 rcFromP, rcToP; Quaternion rcFromQ, rcToQ;
        /// <summary>重新对准（设置里的按钮、系统回中、进关前偏太多时自动调）</summary>
        public void Recenter()
        {
            if (!rig || !cam) return;
            rcFromP = rig.position; rcFromQ = rig.rotation;
            TargetPose(out rcToP, out rcToQ);
            recenterT = 0f;
        }
        /// <summary>玩家相对星界偏了多少：朝向超过 25° 或者走开 30 cm 以上算“歪了”</summary>
        public bool Drifted()
        {
            if (!rig || !cam) return false;
            TargetPose(out var p, out var q);
            return Quaternion.Angle(q, rig.rotation) > 25f || Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(rig.position.x, 0, rig.position.z)) > 0.3f;
        }
        void StepRecenter(float dt)
        {
            if (recenterT < 0f || !rig) return;
            recenterT = Mathf.Min(1f, recenterT + dt / 0.5f);
            float e = recenterT * recenterT * (3f - 2f * recenterT);
            rig.SetPositionAndRotation(Vector3.Lerp(rcFromP, rcToP, e), Quaternion.Slerp(rcFromQ, rcToQ, e));
            if (recenterT >= 1f) recenterT = -1f;
        }
        void OnEnable()
        {
            var subs = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated += OnOriginUpdated;
        }
        void OnDisable()
        {
            var subs = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated -= OnOriginUpdated;
        }
        // 玩家长按系统键“回中”：跟着重新对准（等一帧让相机位姿更新）
        void OnOriginUpdated(UnityEngine.XR.XRInputSubsystem s) => calibrateIn = 0.1f;

        void Update()
        {
            StepRecenter(Time.unscaledDeltaTime);
            // 追踪原点刚切到 Floor 时再对准一次，防止首帧仍在 Device 原点导致星界高度偏低
            if (InXR && MRRig.FloorSet && !lastFloorSet)
            {
                lastFloorSet = true;
                if (calibrateIn < 0f) calibrateIn = 0.15f;
            }
            if (calibrateIn > 0 && Game == null && (calibrateIn -= Time.deltaTime) <= 0) Calibrate();
            if (Game == null) return;
            if (calibrateIn > 0 && (calibrateIn -= Time.deltaTime) <= 0) Calibrate();
            if (tuning) ApplyTuning();
            if (hitStop > 0) hitStop -= Time.deltaTime;
            double dt = paused || hitStop > 0 ? 0 : Mathf.Min(Time.deltaTime, 0.05f);
            LastInput = BuildInput();
            Game.Update(dt, LastInput);
            foreach (var e in Game.DrainEvents()) OnEvent?.Invoke(e);
        }

        FrameInput BuildInput()
        {
            bool xr = InXR && pico && pico.Active;
            HandState l = xr ? pico.Left : desk ? desk.Left : default;
            HandState r = xr ? pico.Right : desk ? desk.Right : default;
            var input = new FrameInput { Hands = new List<HandInput> { Hand("left", l), Hand("right", r) } };
            if (xr && pico.EyeTracked) input.Gaze = new Ray3(FromWorld(pico.Gaze.origin), DirFromWorld(pico.Gaze.direction));
            return input;
        }

        HandInput Hand(string side, HandState h)
        {
            var hi = new HandInput { Side = side, Tracked = h.tracked, Pinching = h.pinch, Point = FromWorld(h.pinchPoint) };
            if (h.tracked && h.hasRay) hi.Ray = new Ray3(FromWorld(h.ray.origin), DirFromWorld(h.ray.direction.normalized));
            return hi;
        }

        /// <summary>编辑器模拟手“伸过去”的目标：空手时是星，拿着星时是缝隙；外加星盘</summary>
        IEnumerable<Vector3> SnapTargets()
        {
            // 界面上能摘的东西（选关星、按钮星）
            foreach (var t in PickTarget.All) if (t.interactable) yield return t.transform.position;
            if (Game == null) yield break;
            foreach (var s in LAYOUT.Stash) yield return ToWorld(s);
            int side = desk ? desk.activeSide : 1;
            bool holding = Game.Hands[side == 0 ? "left" : "right"].Holding != null;
            foreach (var c in Game.Chains)
            {
                if (holding)
                {
                    for (int g = 0; g <= c.Sim.Count; g++) { var gp = Game.GapPosAt(c.Ci, g); if (gp != null) yield return ToWorld(gp.Pos); }
                }
                else foreach (var s in c.Sim.Stars) if (s.S >= 0) yield return ToWorld(c.Track.PointAtU(s.S));
            }
        }
    }
}
