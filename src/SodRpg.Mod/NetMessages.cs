using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

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
        public string transferId;
        public int index;
        public int count;
        public int totalLength;

        public static DreamforgeBuildMsg FromPart(BuildTransferPart part) => new DreamforgeBuildMsg
        {
            build = part.Data, protocol = Protocol.Version, transferId = part.TransferId,
            index = part.Index, count = part.Count, totalLength = part.TotalLength,
        };
        public BuildTransferPart ToPart() => new BuildTransferPart
        {
            Data = build, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength,
        };
    }

    /// <summary>ホスト → クライアント：ホストがMODを導入済みで、能力を反映したことの通知。</summary>
    [Serializable]
    /// <summary>
    /// 版のあいさつ。参加者→ホスト、ホスト→参加者。**この型の名前と欄は今後も変えない**（どの版どうしでも読めて、版違いを利用者に知らせるため）。
    /// </summary>
    public class DreamforgeHelloMsg
    {
        public int protocol;
        public string modVer;
        public string content;
        public string killObservationSessionId;
        public ulong authorityGeneration;
        public string continueRunId, continueCheckpointId, continueResumeSession;
        public bool continueCheckpoints;
        public bool infinityAvailable;
    }

    public class DreamforgeAppliedMsg
    {
        public uint heroNetId;
        public string summary;
        public int protocol;
        public string transferId;
        public int index;
        public int count;
        public int totalLength;

        public static DreamforgeAppliedMsg FromPart(BuildTransferPart part, uint heroNetId) => new DreamforgeAppliedMsg
        {
            summary = part.Data, protocol = Protocol.Version, transferId = part.TransferId,
            index = part.Index, count = part.Count, totalLength = part.TotalLength, heroNetId = heroNetId,
        };
        public BuildTransferPart ToPart() => new BuildTransferPart
        {
            Data = summary, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength,
        };
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
        public bool terminal;
        public bool victory;
        public string lobbyReturnRunId;
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
        public int protocol;
        public uint netId;
        public int affixes;
        public bool removed;
        public ulong authorityGeneration;
    }

    /// <summary>ホスト → 全員：この敵が夢の変種になった（Core の変種ID）。</summary>
    [Serializable]
    public class DreamforgeVariantMsg
    {
        public int protocol;
        public uint netId;
        public string variantId;
        public ulong authorityGeneration;
    }

    /// <summary>ホスト → 全員：現在の敵側の予告。0なし、1障壁予告、2守り、3隙/減速、4回復。</summary>
    [Serializable]
    public class DreamforgeMonsterCueMsg
    {
        public int protocol;
        public uint netId;
        public int cue;
        public ulong authorityGeneration;
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
        public string runId;
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
        // Version 13 requires canonical allocation/loadout inputs for host reconstruction.
        // Version 13 requires replayable authoritative death facts; version 12 cannot settle unknown dead classifications.
        // Version 13 adds authored mechanisms, scoped keystones and registry negotiation.
        // Version 14 carries every selected keystone (up to the traveler's slots) in the build envelope.
        // Version 15 removes keystone drawbacks: the keystone wire grammar drops the downside transform list and conditions.
        // Version 16 adds sequenced kill facts and persisted receipt ACKs for compact, recoverable checkpoints.
        // Version 17 requires each Pure White participant to resolve their own choice before kill settlement.
        // Version 18 adds boss move/reward build sections, epoch-scoped visual snapshots and frozen boss kill facts.
        // Version 18 adds elemental geometry, shrinking domains and bounded reward counters to boss visuals.
        // Version 19 binds MOD checkpoint barriers and resume handshakes to native continue saves.
// Version 20 carries run-bound return-to-lobby defeats and blocks their native resumes.
        // Version 19 adds fixed-zone infinity epochs, shared boss choices and continue-save agreement.
        // Version 20 carries persistent Infinity reward caps.
        // Version 21 keeps the return-to-lobby defeat + Infinity reward-cap protocol and adds owner-bound satchel overflow dust trades with their expedition identity.
        // Version 22 distinguishes frozen continue barriers from disk-confirmed cleanup notices.
        // Version 23 requires one-room Infinity reveal and matching travel/vote restrictions.
        // Version 24 requires random Infinity zone transitions and nonterminal Primus choices.
        public const int Version = 24;
        public const string LobbyReturnedResumeSession = "lobby-returned";

        // Compatibility is diagnostic, never permission to run a feature.
        // Bound warnings by the locally defined message names, not remote values.
        private static readonly HashSet<string> ProtocolWarnings = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> ContentWarnings = new HashSet<string>(StringComparer.Ordinal);

        internal static void WarnMismatch(int received, string message)
        {
            if (received != Version && ProtocolWarnings.Add(message))
                Log.Warn($"{message}: protocol {received} differs from local {Version}; continuing with readable fields.");
        }

        internal static void WarnContentMismatch(int received, string content, string message)
        {
            WarnMismatch(received, message);
            if (!string.Equals(content, ContentFingerprint.Value, StringComparison.Ordinal) && ContentWarnings.Add(message))
                Log.Warn($"{message}: MOD content differs; continuing with readable fields.");
        }
    }

    [Serializable]
    public class DreamforgeBuildInputCapabilityMsg
    {
        public int version = 1;
        // Optional JSON field: absent on older peers. Hello's fixed schema stays intact.
        public int netLite;
    }

    // Optional extension: Hello and every existing packet remain unchanged (Protocol 24).
    [Serializable]
    public class DreamforgeOverflowBonusMsg
    {
        public int version = 1;
        public bool enabled;
        public string runId;
        public long ledgerId, total;
    }

    [Serializable]
    public class DreamforgeOverflowBonusResultMsg
    {
        public int version = 1;
        public bool available;
        public string runId;
        public long ledgerId, paid;
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
