using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 手势和判定阈值（规范第 4 条：所有阈值放在一个资产里，同事装机后直接调）。
    /// 资产：Assets/_Starstrung/Settings/InputTuning.asset。运行时改了立刻生效。
    /// </summary>
    [CreateAssetMenu(menuName = "Starstrung/Input Tuning", fileName = "InputTuning")]
    public class InputTuning : ScriptableObject
    {
        [Header("捏合（拇指尖–食指尖距离，米）")]
        [Tooltip("小于它算捏住。规范写 2.0 cm；WarTable 真机日志里真捏读数 2.1–3.2 cm，所以默认 4.0 cm")]
        public float pinchOn = 0.04f;
        [Tooltip("大于它算松开（滞回防抖）。WarTable 真机：松开的手读数 5.1–6.6 cm")]
        public float pinchOff = 0.055f;
        [Tooltip("捏合时中指 / 无名指 / 小指至少一根伸展度大于它（排除握拳、手指自然蜷着被当成捏）")]
        public float otherFingerOpen = 1.3f;
        [Tooltip("捏着超过这么久且手几乎没动，当作已经松手（手指自然蜷着一直“捏”）")]
        public float staleAfter = 3f;

        [Header("跟丢")]
        [Tooltip("丢一两帧在这段时间内保持上一帧（不松开捏合）")]
        public float lostGrace = 0.15f;
        [Tooltip("腕 + 拇指尖 + 食指尖连续这么久完全不变 = 系统在报冻住的旧数据，按丢失处理")]
        public float frozenAfter = 0.3f;
        [Tooltip("跟丢超过这么久冻结这只手的状态（逻辑层也有同名参数）")]
        public float freezeAfter = 0.2f;
        [Tooltip("跟丢超过这么久提示“把手抬到眼前”")]
        public float raiseHint = 2.5f;

        [Header("手掌射线（肩膀 → 手掌）")]
        public float shoulderSide = 0.17f;
        public float shoulderDown = 0.2f;
        [Tooltip("舒适（11-25）：手比肩膀低时射线自动往上抬，手放在腰间也能指到眼睛高度的星，不用一直举着手臂。最多抬这么多度")]
        public float lowHandLiftDeg = 22f;
        [Tooltip("手比肩膀低多少（米）开始抬 / 抬满")]
        public float liftFrom = 0.1f, liftFull = 0.45f;

        [Header("选星（远摘 / 远放）")]
        [Tooltip("指尖离星中心多近算近摘（米）")] public float nearRadius = 0.06f;
        [Tooltip("近摘拉出多远算摘下（米）")] public float pullOut = 0.05f;
        [Tooltip("远摘射线锥角（度）")] public float coneDeg = 5f;
        [Tooltip("防跳：新候选要比当前再准这么多度才切换")] public float hysteresisDeg = 1.5f;
        [Tooltip("捏合时取多少秒之前亮着的目标")] public float lookback = 0.1f;

        [Header("眼动（只做加分）")]
        [Tooltip("眼动吸附角度（度）")] public float gazeDeg = 4f;
        [Tooltip("眼动平滑（秒）")] public float gazeSmoothing = 0.035f;
        [Tooltip("眼动启动失败后多久重试一次（第一次启动会弹授权框，授权前一直失败）")] public float eyeRetry = 2f;
    }
}
