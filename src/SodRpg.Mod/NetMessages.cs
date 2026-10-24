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
        /// <summary>ホスト→参加者：近接・協力の4設定（<see cref="MeleeCoopSettings"/>）。旧版・未受信は0で、参加者は自分の設定で表示する。</summary>
        public int meleeCoop;
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
        // Optional JSON field: absent (0) from older hosts. Party gear multiplier for the secure-panel nightmare odds (#572).
        public float nightmareGearMultiplier;
        // Uncapped pre-Infinity pressure for nightmare odds; 0 from older hosts.
        public float nightmarePressureHealthMultiplier;
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

    /// <summary>Optional counted reports, sent only after the recipient advertises bountyCounts.</summary>
    [Serializable]
    public class DreamforgeBountyCountsMsg
    {
        public int version = 1;
        public uint heroNetId;
        public string runId;
        public int shields, allyHeals;
        public long sequence;
        // Index is the original four-element mask, preserving overlapping elements.
        public int[] elementalKills;
    }

    [Serializable]
    public sealed class DreamforgeBountyReturnMsg
    {
        public int version = 1;
        public string runId;
        public uint heroNetId;
        public long revision, graphEpoch, segmentEpoch;
        public bool received;
    }

    [Serializable]
    public sealed class BossShareDisplay
    {
        public string participantId;
        public int playerSlot;
        // Quantized to 0..1000 so fractional HP noise does not flood the classification channel.
        public int remaining;
    }

    [Serializable]
    public sealed class DynamicDifficultyDisplay
    {
        public string participantId;
        // Hundredths: the UI shows the same ×1.32 value for every enemy in this room.
        public int factor;
    }

    /// <summary>ホスト → 全員：動的難易度と担当外の減衰の現在の状況（メニューの表示用、#700）。</summary>
    [Serializable]
    public sealed class DynamicDifficultyStatus
    {
        internal const int Off = 1, SoloOff = 2, On = 3;
        // 0 means not sent: older hosts omit the object (or a serializer fills its defaults).
        public long revision;
        public int state;
        // The run's 0-based world order (never restarts within a run); -1 outside a run.
        public int world;
        public int decayPercent;
        // Stage table from coop.json; the last entry applies to every later world.
        public int[] decayByWorld;
        // Thousandths of the host's damageExponent and rewardShare.
        public int damageExponent, rewardShare;

        internal bool Valid()
        {
            if (revision <= 0 || state < Off || state > On || world < -1 || decayPercent < 0 || decayPercent > 100
                || damageExponent < 0 || damageExponent > 1000 || rewardShare < 0 || rewardShare > 1000
                || decayByWorld == null || decayByWorld.Length < 1 || decayByWorld.Length > 32) return false;
            foreach (int percent in decayByWorld) if (percent < 0 || percent > 100) return false;
            return true;
        }
    }

    internal readonly struct MonsterOwnership
    {
        internal readonly string ParticipantId;
        internal readonly long Revision;
        internal readonly bool Protected;
        internal readonly int PlayerSlot;
        internal readonly BossShareDisplay[] BossShares;
        internal readonly DynamicDifficultyDisplay[] DynamicDifficulty;
        internal readonly long DynamicRevision;

        internal MonsterOwnership(string participantId, long revision, bool protectedState, int playerSlot,
            BossShareDisplay[] bossShares = null, DynamicDifficultyDisplay[] dynamicDifficulty = null, long dynamicRevision = 0)
        {
            ParticipantId = participantId;
            Revision = revision;
            Protected = protectedState;
            PlayerSlot = playerSlot;
            BossShares = bossShares;
            DynamicDifficulty = dynamicDifficulty;
            DynamicRevision = dynamicRevision;
        }

        internal static bool SameShares(BossShareDisplay[] left, BossShareDisplay[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i].participantId != right[i].participantId || left[i].playerSlot != right[i].playerSlot
                    || left[i].remaining != right[i].remaining) return false;
            return true;
        }

        internal bool SameState(MonsterOwnership other) => ParticipantId == other.ParticipantId
            && Revision == other.Revision && Protected == other.Protected && PlayerSlot == other.PlayerSlot
            && SameShares(BossShares, other.BossShares) && DynamicRevision == other.DynamicRevision;
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
        // Additive CustomRpc JSON fields; older clients ignore ownership.
        public string ownerParticipantId;
        public long ownershipRevision;
        public bool ownershipProtected;
        public int ownerPlayerSlot;
        public BossShareDisplay[] bossShares;
        public DynamicDifficultyDisplay[] dynamicDifficulty;
        public long dynamicRevision;
        // Additive (#700): only the netId 0 roster message carries it.
        public DynamicDifficultyStatus dynamicStatus;
    }

    /// <summary>ホスト → 全員：この敵が夢の変種になった（Core の変種ID）。</summary>
    [Serializable]
    public class DreamforgeVariantMsg
    {
        public int protocol;
        public uint netId;
        public string variantId;
        public ulong authorityGeneration;
        public string ownerParticipantId;
        public long ownershipRevision;
        public bool ownershipProtected;
        public int ownerPlayerSlot;
        public BossShareDisplay[] bossShares;
        public DynamicDifficultyDisplay[] dynamicDifficulty;
        public long dynamicRevision;
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
    public class DreamforgeAutocastPressureCapabilityMsg
    {
        public string capability = "autocast-pressure";
        public int version = 1;
    }

    [Serializable]
    public class DreamforgeBuildInputCapabilityMsg
    {
        public int version = 1;
        // Optional JSON field: absent on older peers. Hello's fixed schema stays intact.
        public int netLite;
        public int roomOmens;
        /// <summary>この参加者が工房の「禁忌の職人」を解放済みか（#415）。誰か1人でも解放していればホストは宿敵を出す。</summary>
        public int forbiddenSmith;
        /// <summary>この参加者と宿敵「深淵の追跡者」との因縁の深さ（#415）。ホストはパーティで最も深い値で宿敵を出す。旧版は 0。</summary>
        public int nemesisGrudge;
        /// <summary>ホストが禁忌装備入り（15欄形式）のビルド入力を読めるか（#415）。旧ホストは返さないので 0 扱いになり、参加者は禁忌装備を送らない。</summary>
        public int forbiddenInput;
        /// <summary>融合段階を含む16欄形式のビルド入力を読めるか。旧版は0。</summary>
        public int fusionInput;
        // Optional JSON field; absent/zero peers keep receiving single-event reports.
        public int bountyCounts;
        public int stateSnapshots;
        public int dynamicRoster;
        public int helloSchedule;
        public int killReceiptDelta;
        public int autocastPressure;
    }

    // Optional extension: Hello and every existing packet remain unchanged (Protocol 24).
    [Serializable]
    public class DreamforgeOverflowBonusMsg
    {
        public int version = 1;
        public bool enabled;
        public string runId;
        public long ledgerId, total;
        public int blockedWorldAck;
    }

    [Serializable]
    public class DreamforgeOverflowBonusResultMsg
    {
        public int version = 1;
        public bool available;
        public string runId;
        public long ledgerId, paid;
        public bool worldBlocked;
        public int blockedWorld;
    }

    [Serializable]
    public class DreamforgeDreamEventStartedMsg
    {
        public int protocol;
        public string runId;
        public int generation;
        public int dreamEvent;
    }

    /// <summary>
    /// クライアント → ホスト：自分のキャラのスキルが、手元では長く使えないままになっているので、
    /// 権威のある値（チャージ・クールダウン・最低遅延）の再送を頼む（docs/specs/guest-skill-lock.md）。
    /// この型を送るのは同じ MOD を積んだ参加者だけで、受け取らないホストは単に無視する。
    /// </summary>
    [Serializable]
    public class DreamforgeSkillResyncMsg
    {
        public int version = 1;
        public uint heroNetId;
    }

    [Serializable]
    public class DreamforgeSkillResyncCapabilityMsg
    {
        public int version = 2;
        public uint heroNetId;
        public DreamforgeSkillStateReplyMsg[] snapshots;
    }

    // Used only after the host advertises version 2. Observations never grant cast permission.
    [Serializable]
    public class DreamforgeSkillStateRequestMsg
    {
        public int version = 2;
        public uint heroNetId;
        public uint requestId;
        public int slot;
        public bool diagnose;
        public string reason;
        public DreamforgeSkillState state;
    }

    [Serializable]
    public class DreamforgeSkillStateReplyMsg
    {
        public int version = 2;
        public uint heroNetId;
        public uint requestId;
        public int slot;
        public bool compared, different;
        public DreamforgeSkillState state;
        public string diagnostic;
    }

    [Serializable]
    public class DreamforgeSkillState
    {
        public uint skillNetId, ownerNetId;
        public int config, abilityIndex, maxCharges;
        public bool active, configActive, casting, locksKnown, castLock, cooldownLock;
        public bool abilityLock, validator, reservable;
        public float mana, manaCost;
        public int status, blockAbility, blockAbilityCancelable, blockDodge, blockDodgeCancelable;
        public float[] cooldowns, delays;
        public int[] charges;
    }
}
