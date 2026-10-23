using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

// Skoll バースト表示（#931）のための境界。NativePersistence.targets が
// HostAuthority.BossGlacialCore.cs と HostAuthority.BossVisuals.cs から抽出した
// 実ソース（SkollPublishBurst / CompleteSkollCoreHeal / CountSkollCoreShot /
// TickSkollCores / SkollReleaseCore / PublishBossVisual とその状態）が触る本体 API と、
// リンク外の本体側 HostAuthority の部位（SkollCoreRuntime の戦闘状態検証・BossReady の
// クールダウン帳簿・BossVisuals の JSON 直列化と配信）の形だけを用意する。
namespace SodRpg.Mod
{
    // 本体の Gem_U_GlacialCore。抽出した Skoll 系メソッドが触る分だけ。
    internal sealed class Gem_U_GlacialCore : Actor
    {
        public bool isValid = true;
        public float creationTime;
    }

    // 本体の Ai_Gem_U_GlacialCore_Projectile（AbilityInstance）。弾の発射元（parentActor）と
    // キャスト情報（info = CastInfo）は境界の Actor / AbilityInstance から継承する。
    internal sealed class Ai_Gem_U_GlacialCore_Projectile : AbilityInstance { }

    // 本体 HostAuthority.BossPools.cs（リンク外）の BossObjectPool。
    // 借りた物の識別と返却の冪等さだけを写し、事前確保は省く。
    internal sealed class BossObjectPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly List<T> _rented = new List<T>();
        public BossObjectPool(int capacity, Func<T> factory) => _factory = factory;
        public T Rent() { var item = _factory(); _rented.Add(item); return item; }
        public void Return(T item) => _rented.Remove(item);
    }

    internal sealed partial class HostAuthority
    {
        // 本体 HostAuthority.BossRuntime.cs の BossCombatState の形。バースト表示は NextId だけ、
        // 印の経路（CompileSkollMarks の抽出）は Ready（再発動間隔）・Ledger（命中の帳簿）・
        // Sequence（指令パルス）も使う。
        internal sealed class BossCombatState
        {
            public long Epoch, NextId, Revision, OwnerLife;
            public readonly Dictionary<string, float> Ready = new Dictionary<string, float>(128, StringComparer.Ordinal);
            public readonly BossProgressLedger Ledger = new BossProgressLedger();
            public readonly BossPulseBuffer Sequence = new BossPulseBuffer();
        }
        // 本体 HostAuthority.BossRuntime.cs の BossReady は鍵ごとの再発動間隔を払う。
        // ここでは常に発動可能とし、間隔の検証はバースト表示の対象外とする。
        private static bool BossReady(HeroRuntime rt, string key, float now, int cooldown) => true;
        // 本体 HostAuthority.BossNativeAdapters.cs の BossNativeActorLife は Actor の
        // 生存世代の刻印を返す。ここでは境界 Actor の識別子で代える。
        private long BossNativeActorLife(Actor actor) => actor == null ? 0 : actor.GetInstanceID();

        // 本体の SkollCorePending（回復の検証に使う private nested struct）。抽出タスクは
        // nested class しか抜けないためここに置く。抽出したメソッドは辞書の維持
        // （Clear / Remove）だけでフィールドを読み書きしない。抽出側の SkollCoreState が
        // internal で Pending を公開するため、ここも internal にする。
        internal struct SkollCorePending { }

        // SkollState と持ち主の台帳（_skollStates / _skollStatePool / SkollOwnerState）は
        // 本体 HostAuthority.BossSkoll.cs から CompileSkollMarks（NativePersistence.targets）
        // が抽出した実物を使う。以前はここに Cores / ExpiredCores だけの形を置いていた
        // （TickSkollCores の抽出が触る分）。印の経路の検証（SkollMarkTests）から同じ実物を通す。

        // 本体の SkollCoreRuntime は所有者の生存・報酬段階・装備の一致・部屋と run の同一性を
        // 検証して state を返す。ここでは登録台帳だけで同じ役を果たす（検証そのものは対象外）。
        private bool SkollCoreRuntime(Gem_U_GlacialCore core, out HeroRuntime rt, out SkollCoreState state, bool create = false)
        {
            rt = null; state = null;
            if (!NetworkServer.active || core == null || !core.isValid) return false;
            if (!SkollCoreOwners.TryGetValue(core, out rt)) return false;
            var owner = SkollOwnerState(rt);
            if (!owner.Cores.TryGetValue(core, out state) || !ReferenceEquals(state.Core, core) || !ReferenceEquals(state.Owner, rt.Hero)) { state = null; return false; }
            return true;
        }
        // 本体の SkollPendingHeal は BeginSkollCoreHeal が記録した回復の検証。承認台帳で代える。
        private bool SkollPendingHeal(object routine, Gem_U_GlacialCore core, EventInfoDamage info, out HeroRuntime rt, out SkollCoreState state)
        {
            rt = null; state = null;
            if (!SkollApprovedRoutines.Contains(routine)) return false;
            return SkollCoreRuntime(core, out rt, out state);
        }
        // 本体の SkollCoreReward は報酬段階を選ぶ。実データ（BossProfiles）の段階をそのまま使う。
        internal int SkollRewardStageForTest = 3;
        private BossRewardStage SkollCoreReward(HeroRuntime rt)
            => BossProfiles.TryGetReward(BossProfiles.SkollRewardId, out var profile)
                && SkollRewardStageForTest >= 1 && SkollRewardStageForTest <= profile.Stages.Count
                ? profile.Stages[SkollRewardStageForTest - 1] : null;

        // 本体 HostAuthority.BossVisuals.cs の SendBossVisualMessage は JSON 直列化と
        // 全人間プレイヤーへの配信。ここでは届いた差分の写しを記録するだけにする
        // （スナップショットは差分ではないので数えない）。
        internal readonly List<DreamforgeBossEffect[]> SentBossVisualDeltas = new List<DreamforgeBossEffect[]>();
        private void SendBossVisualMessage(BossVisualState state, DreamforgeBossEffect[] effects, bool snapshot)
        {
            var copy = new DreamforgeBossEffect[effects == null ? 0 : effects.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var source = effects[i];
                copy[i] = new DreamforgeBossEffect
                {
                    id = source.id, kind = source.kind, targetNetId = source.targetNetId,
                    center = source.center, end = source.end, radius = source.radius,
                    element = source.element, shape = source.shape, count = source.count,
                    range = source.range, width = source.width, angle = source.angle,
                    budget = source.budget, finalRadius = source.finalRadius,
                    due = source.due, expires = source.expires, removed = source.removed
                };
            }
            if (!snapshot) SentBossVisualDeltas.Add(copy);
        }

        // ---- テストからの準備フック（本体には存在しない） ----
        internal readonly Dictionary<Gem_U_GlacialCore, HeroRuntime> SkollCoreOwners = new Dictionary<Gem_U_GlacialCore, HeroRuntime>();
        internal readonly HashSet<object> SkollApprovedRoutines = new HashSet<object>();
        internal object SkollOwnerForTest(HeroRuntime rt) => SkollOwnerState(rt);
        internal SkollCoreState SkollCreateCoreForTest(HeroRuntime rt, Gem_U_GlacialCore core)
        {
            var owner = SkollOwnerState(rt);
            if (!owner.Cores.TryGetValue(core, out var state))
            {
                state = _skollCorePool.Rent();
                state.Core = core; state.Owner = rt.Hero; state.Creation = core.creationTime;
                owner.Cores.Add(core, state);
            }
            SkollCoreOwners[core] = rt;
            return state;
        }

        // 本体 HostAuthority.cs の HeroRuntime のうち BossVisuals / Skoll が触る分
        // （NativeBoundary.cs の HeroRuntime を partial にして足す）。
        internal sealed partial class HeroRuntime
        {
            public readonly BossCombatState Boss = new BossCombatState();
            public long ShieldEquipmentEpoch;
        }
    }
}
