// 所有可调参数（对应 JS config.js）。
// Vec3、Ray3 等数学基础类型也放这里——不依赖 UnityEngine。
// 坐标：玩家站原点、面朝 -Z，Y 向上，单位米。

namespace Starstrung.Logic
{
    // ──────────────────── 数学基础 ────────────────────

    /// 三维向量（double 精度），对应 JS 里的 {x,y,z} 普通对象
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"({X:F3},{Y:F3},{Z:F3})";
    }

    /// 射线（原点 + 方向单位向量），对应 JS 里的 ray:{o,d}
    public struct Ray3
    {
        public Vec3 O;   // origin
        public Vec3 D;   // direction（单位向量）
        public Ray3(Vec3 o, Vec3 d) { O = o; D = d; }
    }

    // ──────────────────── 输入结构 ────────────────────
    // 对应 game.js update(dt, input) 的 input 类型

    /// 单只手的输入帧数据
    public class HandInput
    {
        public string Side;      // "left" / "right"
        public bool Tracked;
        public bool Pinching;
        public Vec3 Point;       // 指尖世界坐标
        public Ray3? Ray;        // null = 没有射线
    }

    /// 一帧的完整输入，对应 JS { hands:[...], gaze:{o,d}|null }
    public class FrameInput
    {
        public System.Collections.Generic.List<HandInput> Hands;
        public Ray3? Gaze;
    }

    // ──────────────────── 配置常量 ────────────────────

    public static class LAYOUT
    {
        public static readonly Vec3 Head  = new Vec3(0, 1.6,   0);
        public static readonly Vec3 Core  = new Vec3(0, 1.22, -0.35);
        public const double CoreRadius    = 0.075;
        public static readonly Vec3[] Stash = {
            new Vec3(-0.27, 1.08, -0.3),
            new Vec3( 0.27, 1.08, -0.3),
        };
        public const double StashRadius = 0.085;

        // 星直径 = base + perMeter × 离头距离，限 [min, max]
        public static class Star
        {
            public const double Base     = 0.05;
            public const double PerMeter = 0.035;
            public const double Min      = 0.06;
            public const double Max      = 0.2;
        }
        public static class Shoulder
        {
            public const double Side = 0.17;
            public const double Down = 0.2;
            public const double Back = 0.02;
        }
    }

    public static class HAND
    {
        public static double PinchOn      = 0.02;
        public static double PinchOff     = 0.035;
        public static double FreezeAfter  = 0.2;
        public static double RaiseHint    = 2.5;
    }

    /// 选星阈值：运行时由 GameRunner 从 InputTuning.asset 覆盖（真机可调），默认值和 JS 一致
    public static class PICK
    {
        public static double NearRadius    = 0.06;
        public static double PullOut       = 0.05;
        public static double ConeDeg       = 5;
        public static double HysteresisDeg = 1.5;
        public static double NearTieDeg    = 0.6;
        public static double Lookback      = 0.1;
        public static double GazeDeg       = 4;
        public static double GazeBonusDeg  = 1.2;
        public static double FlyIn         = 0.4;
        public static double GapNearRadius = 0.07;
        public static double StashNear     = 0.09;
        public static double ReturnTime    = 0.9;
    }

    public static class GAME
    {
        public const int    MaxCracks          = 3;
        public const double KnockbackMeters    = 1.5;
        public const double ComboRetreatMeters = 0.25;
    }
}
