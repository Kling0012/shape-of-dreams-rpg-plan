using System;

namespace SodRpg.Mod
{
    /// <summary>
    /// クライアント → ホスト：自分のキャラの「今回の強さ」（Build.Encode の文字列）。
    /// ゲームの CustomRpc は型名で照合するため、他MODと衝突しない名前にする。
    /// </summary>
    [Serializable]
    public class DreamforgeBuildMsg
    {
        public string build;
        public int protocol;
    }

    /// <summary>ホスト → クライアント：ホストがMODを導入済みで、能力を反映したことの通知。</summary>
    [Serializable]
    public class DreamforgeAppliedMsg
    {
        public uint heroNetId;
        public string summary;
    }

    /// <summary>ホストが求めた夢の圧。途中参加向けにも定期送信する。</summary>
    [Serializable]
    public class DreamforgePressureMsg
    {
        public int protocol;
        public float healthMultiplier;
        public float damageMultiplier;
        public string runChoices;
    }

    /// <summary>Host-only shared depth and waypoint choices; also carried by pressure catch-up messages.</summary>
    [Serializable]
    public class DreamforgeRunChoicesMsg
    {
        public int protocol;
        public string choices;
    }

    internal enum BountyReportKind
    {
        ElementalKill,
        ShieldGranted,
        AllyHealed,
        MemoryUsed,
        LinksSatisfied,
        GimmicksTriggered,
    }

    /// <summary>ホスト → 対象の遊び手：確定した行動か、成立条件の最高値。</summary>
    [Serializable]
    public class DreamforgeBountyReportMsg
    {
        public int protocol;
        public uint heroNetId;
        public string runId;
        public int report;
        public int value;
    }

    /// <summary>ホスト → 全員：この敵が悪夢化した（接頭効果のビット集合）。</summary>
    [Serializable]
    public class DreamforgeNightmareMsg
    {
        public uint netId;
        public int affixes;
    }

    /// <summary>ホスト → 全員：この敵が夢の変種になった（Core の変種ID）。</summary>
    [Serializable]
    public class DreamforgeVariantMsg
    {
        public uint netId;
        public string variantId;
    }

    /// <summary>ホスト → 全員：現在の敵側の予告。0なし、1障壁予告、2守り、3隙/減速、4回復。</summary>
    [Serializable]
    public class DreamforgeMonsterCueMsg
    {
        public uint netId;
        public int cue;
    }

    /// <summary>クライアント → ホスト：悪夢の契約の代償として、自分のキャラへ本体の呪いを付けてほしい。</summary>
    [Serializable]
    public class DreamforgeCurseMsg
    {
        public int strength;
        public int protocol;
    }

    /// <summary>クライアント → ホスト：契約が解けたので、潜行で付いた呪いを消してほしい。</summary>
    [Serializable]
    public class DreamforgeCurseClearMsg
    {
        public int protocol;
    }

    /// <summary>クライアント → ホスト：本体の通貨での取引（支払いと受け取り）を依頼する。</summary>
    [Serializable]
    public class DreamforgeTradeMsg
    {
        public long token;
        public int spendGold;
        public int spendDust;
        public int earnDust;
        public int protocol;
    }

    /// <summary>ホスト → クライアント：取引の結果。</summary>
    [Serializable]
    public class DreamforgeTradeResultMsg
    {
        public long token;
        public bool ok;
        public string reason;
    }

    internal static class Protocol
    {
        // Version 11 adds cluster duration, radius, target-count and chance modifiers to gimmick build entries.
        public const int Version = 11;
    }

    [Serializable]
    public class DreamforgeDreamEventStartedMsg
    {
        public int protocol;
        public string runId;
        public int generation;
        public int dreamEvent;
    }
}
