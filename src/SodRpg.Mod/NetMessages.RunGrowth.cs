using System;

namespace SodRpg.Mod
{
    /// <summary>
    /// ホスト → 旅人の持ち主：遠征の鍛錬（RunGrowth）の現在のスタック。表示（HUD・取得した効果の一覧）専用で、
    /// 効果の計算にはクライアントの値を使わない。stacks は「星ID=スタック数」をコンマで連ねたもの。
    /// </summary>
    [Serializable]
    public class DreamforgeRunGrowthMsg
    {
        public int protocol;
        public uint heroNetId;
        public string runId;
        public string stacks;
    }
}
