// 可复现的随机数（xorshift32）——逐位与 JS rng.js 一致。
// 移植注意：
//   Math.imul(seed>>>0, 2654435761) = 32 位截断乘法（返回 int32 再 >>>0 变 uint32）
//   JS xorshift 步骤先变 int32 再 >>>0；C# 用 uint 自然截断等效。

namespace Starstrung.Logic
{
    public class Rng
    {
        private uint s;

        public Rng(int seed = 1)
        {
            // JS: (Math.imul(seed >>> 0, 2654435761) + 12345) >>> 0
            unchecked
            {
                s = (uint)seed * 2654435761u + 12345u;
            }
            if (s == 0u) s = 1u;
        }

        public uint Next()
        {
            unchecked
            {
                s ^= s << 13;   // JS: s ^= s << 13; s >>>= 0;  → uint 自然截断
                s ^= s >> 17;   // JS: s ^= s >>> 17;           → uint >> 是无符号右移
                s ^= s << 5;    // JS: s ^= s << 5; s >>>= 0;
            }
            return s;
        }

        /// [a, b) 整数，对应 JS range(a, b) = a + (next() % (b-a))
        public int Range(int a, int b)
        {
            return a + (int)(Next() % (uint)(b - a));
        }

        /// [0, 1) 浮点，对应 JS value() = next() / 4294967296
        public double Value()
        {
            return Next() / 4294967296.0;
        }
    }
}
