using System;
using Xunit;

namespace SodRpg.Core.Tests.Testing
{
    /// <summary>
    /// 数分かかる網羅版の試験。通常は Skip され、環境変数 SODRPG_SLOW=1 のときだけ走る（Speed=Slow 特性付き）。
    /// 実行例: SODRPG_SLOW=1 dotnet test --filter "Speed=Slow"
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SlowFactAttribute : FactAttribute
    {
        public SlowFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("SODRPG_SLOW") != "1")
                Skip = "Slow exhaustive variant. Set SODRPG_SLOW=1 to run.";
        }
    }
}
