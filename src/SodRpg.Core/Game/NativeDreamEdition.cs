namespace SodRpg.Core.Game
{
    /// <summary>
    /// ビルド時の版（通常／特別）に依存する判定。特別版では夢の種を開発フラグなしで有効にし、
    /// 本体の抽選プールへ原型の品と同じ重みで加える。通常版は従来どおりフラグ時のみ・プールには入れない。
    /// </summary>
    public static class NativeDreamEdition
    {
        /// <summary>夢の種を有効にするか。特別版はフラグ不要、通常版は dev.flag と native-dream.flag の両方。</summary>
        public static bool EnableContent(bool specialEdition, bool devFlag, bool nativeDreamFlag)
            => specialEdition || (devFlag && nativeDreamFlag);

        /// <summary>その品を本体の抽選プールへ加えるか。特別版かつ登録済みの品だけ。</summary>
        public static bool IncludeInLootPool(bool specialEdition, bool itemReady)
            => specialEdition && itemReady;

        /// <summary>版表示に付ける接尾辞。通常版は空。</summary>
        public static string VersionSuffix(bool specialEdition)
            => specialEdition ? " 特別版 / Special Edition" : "";
    }
}
