using System;
namespace SodRpg.Core.Game
{
    /// <summary>Localized v1.29 gear behavior. Values remain display units and never depend on item level.</summary>
    public static class NewPowersV129
    {
        public static bool IsPower(Power p) => p >= Power.ShieldbreakBurst && p <= Power.SpilloverStrike;
        public static bool IsConditionalAttribute(Power p)
        {
            switch (p)
            {
                case Power.Vigor: case Power.Overload: case Power.Resonance: case Power.UltimateSurge:
                case Power.Devotion: case Power.CrystalResonance: case Power.LucidBoon: case Power.PreyPride:
                case Power.Retaliation: case Power.GleamingWard: case Power.Medley: return true;
                default: return false;
            }
        }
        private static readonly Txt[] Names =
        {
            new Txt("割れ盾の衝撃", "Shieldbreak Burst"),
            new Txt("分かち合う護り", "Shared Ward"),
            new Txt("盾打ち", "Shield Bash"),
            new Txt("障壁の輝き", "Gleaming Ward"),
            new Txt("共演", "Co-star"),
            new Txt("凱歌", "Triumph Song"),
            new Txt("見守りの手", "Watchful Hand"),
            new Txt("情けの返礼", "Kindness Returns"),
            new Txt("渡り鳥", "Wanderer's Edge"),
            new Txt("一点集中", "Focus Fire"),
            new Txt("包囲突破", "Breakout"),
            new Txt("決闘者の流儀", "Duelist's Way"),
            new Txt("不動の構え", "Immovable Stance"),
            new Txt("助走", "Run-Up"),
            new Txt("歩き撃ち", "Strafe Shot"),
            new Txt("継ぎの手", "Relay Hand"),
            new Txt("星屑の巡り", "Stardust Cycle"),
            new Txt("影の継承", "Umbral Heritage"),
            new Txt("脆き氷", "Brittle Ice"),
            new Txt("元素の収穫", "Elemental Harvest"),
            new Txt("七色の切替", "Prism Shift"),
            new Txt("群れの糧", "Pack Feast"),
            new Txt("前衛の誓い", "Vanguard's Oath"),
            new Txt("死に花", "Death Bloom"),
            new Txt("連奏", "Medley"),
            new Txt("詠唱の薙ぎ", "Spellsweep"),
            new Txt("素手の矜持", "Bare-Handed Pride"),
            new Txt("口火", "Opening Salvo"),
            new Txt("畳みかけ", "Pile-On"),
            new Txt("切り札を握る", "Ace in Hand"),
            new Txt("結晶の循環", "Crystal Circuit"),
            new Txt("欠片の恵み", "Shard Boon"),
            new Txt("継ぎの命", "Lifeline"),
            new Txt("夢見の前兆", "Dream Omen"),
            new Txt("後衛の流儀", "Rearguard's Way"),
            new Txt("怨嗟の鐘", "Toll of Grudge"),
            new Txt("不撓の心", "Unbowed Mind"),
            new Txt("積もる運", "Piling Luck"),
            new Txt("急所の傷", "Weak-Point Wound"),
            new Txt("会心の飛沫", "Crit Splash"),
            new Txt("戻り刃", "Returning Blade"),
            new Txt("備えの盾", "Ready Guard"),
            new Txt("薬師の手際", "Apothecary"),
            new Txt("溢れる一撃", "Spillover Strike"),
        };
        public static string Name(Power p) => IsPower(p) ? Names[(int)p - (int)Power.ShieldbreakBurst].ToString() : "-";
        public static Txt Epithet(Power p) => IsPower(p)
            ? new Txt(Names[(int)p - (int)Power.ShieldbreakBurst].Ja + "の", Names[(int)p - (int)Power.ShieldbreakBurst].En) : null;
        public static string Describe(Power p, int v)
        {
            string body;
            switch (p)
            {
                case Power.ShieldbreakBurst: body = Loc.T($"範囲ダメージ +割れる直前の障壁残量か自分の最大HPの高い方の{v}%／敵のダメージで自分の障壁が完全に割れたとき、自分の周囲5m以内の敵それぞれに与える（6秒に1回。時間切れでは発動しない）", $"Area damage +{v}% of the higher of your shield remaining before the hit or your maximum health per enemy within 5m of you when enemy damage fully breaks your shield (once per 6s; expiry does not trigger it)"); break;
                case Power.SharedWard: body = Loc.T($"味方への障壁 +自分が得た障壁量の{v}%／障壁を得たとき、自分の周囲10m以内の味方旅人それぞれへ4秒間付与（分けた障壁からは連鎖しない。重ならず大きい方を適用）", $"Shield granted to allies +{v}% of the shield you gained, for 4s per allied traveler within 10m of you (shared shields do not chain; highest shield only, without stacking)"); break;
                case Power.ShieldBash: body = Loc.T($"通常攻撃の追加無属性魔法ダメージ +現在の障壁量の{v}%／障壁がある間、命中した敵へ与える。障壁は消費しない", $"Basic attack bonus non-elemental magic damage +{v}% of your current shield amount on hit while shielded; does not consume the shield"); break;
                case Power.GleamingWard: body = Loc.T($"攻撃力・魔力 +{v}%／自分に障壁がある間", $"Attack damage and ability power +{v}% while you are shielded"); break;
                case Power.CoStar: body = Loc.T($"通常記憶の残りクールダウン -{v}%／味方が奥義の記憶を使ったとき、自分の通常記憶すべてが対象。自分の奥義使用時は-{v / 2f:0.#}%", $"All your ordinary memories' remaining cooldowns -{v}% when an ally uses an Ultimate memory; -{v / 2f:0.#}% when you use your own Ultimate"); break;
                case Power.TriumphSong: body = Loc.T($"HP回復 +自分の最大HPの{v}%／奥義の記憶を使ったとき、自分と周囲10m以内の味方旅人それぞれを回復（基準は受け手ではなく自分の最大HP）", $"Health restored +{v}% of your maximum health to yourself and each allied traveler within 10m when using an Ultimate memory (based on your maximum health, not the recipient's)"); break;
                case Power.WatchfulHand: body = Loc.T($"味方への障壁 +自分の最大HPの{v}%／生存中の味方旅人のHPが30%未満のとき、その味方へ6秒間付与（同じ味方に45秒に1回。距離制限なし）", $"Shield granted to an ally +{v}% of your maximum health for 6s while that living allied traveler is below 30% health (once per 45s per ally; no distance limit)"); break;
                case Power.KindnessReturns: body = Loc.T($"自分のHP回復 +味方への実回復量の{v}%／自分の記憶で別の味方旅人や召喚獣を回復したとき。最大HPを超えた回復分は数えない", $"Your health restored +{v}% of actual healing granted when your memory heals another allied traveler or summon; excludes overhealing"); break;
                case Power.WanderersEdge: body = Loc.T($"通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／直前の通常攻撃とは別の敵に命中したとき（攻撃1回の主対象に1回。同じ敵に1秒に1回）", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power when hitting a different enemy from your previous basic attack (once on the primary target per attack; once per 1s per enemy)"); break;
                case Power.FocusFire: body = Loc.T($"通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／同じ敵へ通常攻撃を5回続けて当てるたび（別の敵に当てるか4秒以上空くと数え直し）", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power every 5 consecutive hits on the same enemy (resets on a different target or a gap of at least 4s)"); break;
                case Power.Breakout: body = Loc.T($"障壁 +自分の最大HPの{v}%、敵の移動速度 -30%／自分の周囲6m以内の敵が3体以下から4体以上になったとき。自分への障壁は5秒間、6m以内の敵へのスロウは2秒間（10秒に1回）", $"Shield +{v}% of your maximum health for 5s and enemy movement speed -30% for 2s when the enemy count within 6m of you rises from 3 or fewer to 4 or more; slows all enemies within 6m (once per 10s)"); break;
                case Power.DuelistsWay: body = Loc.T($"通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／自分の周囲8m以内に敵が1体だけいる間、その敵へ命中したとき", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power against the sole enemy within 8m of you"); break;
                case Power.ImmovableStance: body = Loc.T($"与ダメージ +{v}%、被ダメージ -{v / 2}%／1秒以上動かずにいる間。動くと解除。軽減は同じ効果の合計で10%まで", $"Damage dealt +{v}%, damage taken -{v / 2}% after standing still for 1s, until you move; combined reduction from this effect capped at 10%"); break;
                case Power.RunUp: body = Loc.T($"次の通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／歩いて合計12m動いた後、次の命中時に1回消費。回避・ダッシュ・瞬間移動は数えず、世界を移ると距離を0に戻す。次の通常攻撃への追加効果は最大の1つだけを消費し、残りは保持", $"Next basic attack bonus damage +{v}% of the higher of your attack damage or ability power after walking 12m; consumed on the next hit. Dodges, dashes and teleports do not count; walking progress resets between worlds. Only the largest next-basic bonus is consumed; others remain"); break;
                case Power.StrafeShot: body = Loc.T($"敵の移動速度 -{v}%／移動しながら通常攻撃を当てたとき、当てた敵を1秒間遅くする（重ならず、再命中で時間を延長）", $"Enemy movement speed -{v}% for 1s when hit by your basic attack while moving (refreshes without stacking)"); break;
                case Power.RelayHand: body = Loc.T($"味方の通常記憶の残りクールダウン -{v}%／敵を倒したとき、最も近い味方旅人の通常記憶のうち残り時間が最長の1つが対象（同じ味方に2秒に1回）", $"Ally's ordinary-memory remaining cooldown -{v}% on kill; affects the nearest allied traveler's ordinary memory with the longest remaining cooldown (once per 2s per ally)"); break;
                case Power.StardustCycle: body = Loc.T($"通常記憶の残りクールダウン -{v}%／自分の光付与で敵の光が5つに達したとき、自分の通常記憶のうち残り時間が最長の1つが対象（自分全体で1秒に1回、同じ敵に5秒に1回）", $"Ordinary-memory remaining cooldown -{v}% when your Light application brings an enemy to 5 Light stacks; affects your ordinary memory with the longest remaining cooldown (once per 1s overall; once per 5s per enemy)"); break;
                case Power.UmbralHeritage: body = Loc.T($"闇付与確率 +{v}パーセントポイント／闇が2つ以上ある敵を倒したとき、倒した敵の周囲6m以内の別の敵最大5体へ、各敵に個別判定で闇を1つ付ける（闇は5つまで）", $"Dark application chance +{v} percentage points per target when killing an enemy with at least 2 Dark stacks; independently rolls to apply 1 Dark stack to up to 5 other enemies within 6m of the killed enemy (Dark capped at 5 stacks)"); break;
                case Power.BrittleIce: body = Loc.T($"追加ダメージ +攻撃力か魔力の高い方の{v}%／冷気を受けている敵に会心でダメージを与えたとき。通常攻撃・記憶のどちらも対象", $"Bonus damage +{v}% of the higher of your attack damage or ability power on a critical hit against a chilled enemy; applies to basic attacks and memories"); break;
                case Power.ElementalHarvest: body = Loc.T($"範囲ダメージ +攻撃力か魔力の高い方の{v}%×属性の種類数／2種類以上の属性がある敵を倒したとき、倒した敵の周囲4m以内の敵それぞれに与える（最大4種類分。連鎖しない）", $"Area damage +{v}% of the higher of your attack damage or ability power per element type on the killed enemy, dealt to each enemy within 4m; requires at least 2 element types (counts up to 4 types; does not chain)"); break;
                case Power.PrismShift: body = Loc.T($"障壁 +自分の最大HPの{v}%／直前とは別の属性を敵に付けたとき、自分へ3秒間付与（1秒に1回。重ならず大きい方を適用）", $"Shield +{v}% of your maximum health for 3s when applying a different element from your previous application (once per 1s; highest shield only, without stacking)"); break;
                case Power.PackFeast: body = Loc.T($"障壁 +自分の最大HPの{v}%／自分の召喚獣が敵を倒したとき、自分へ4秒間付与（重ならず大きい方を適用）", $"Shield +{v}% of your maximum health for 4s when your summon kills an enemy (highest shield only, without stacking)"); break;
                case Power.VanguardsOath: body = Loc.T($"得る障壁量 +{v}%（ソロでは+{v / 2f:0.#}%）／敵までの最短距離がパーティで最も短い旅人である間。ソロでは敵がいる間。敵がいないと無効", $"Shield amount received +{v}% (+{v / 2f:0.#}% when solo) while your distance to your nearest enemy is the shortest in the party; active in solo while enemies exist, inactive without enemies"); break;
                case Power.DeathBloom: body = Loc.T($"範囲ダメージ +攻撃力か魔力の高い方の{v}%／自分の召喚獣が敵に倒されたとき、その召喚獣の周囲4m以内の敵それぞれに与える（時間切れでは発動しない。連鎖しない）", $"Area damage +{v}% of the higher of your attack damage or ability power per enemy within 4m of your summon when an enemy kills it (expiry does not trigger this; does not chain)"); break;
                case Power.Medley: body = Loc.T($"攻撃力・魔力 +{v}%／使った通常記憶の種類1つにつき、最大3種類分まで。新しい種類を使うと全体の持続時間を12秒に延長。同じ記憶の再使用では種類数も期限も変わらない", $"Attack damage and ability power +{v}% per distinct ordinary memory used, up to 3 types; a new type refreshes the whole duration to 12s. Reusing the same memory changes neither the count nor the expiry"); break;
                case Power.Spellsweep: body = Loc.T($"範囲追加ダメージ +通常攻撃の与ダメージの{v}%／通常記憶使用後5秒以内の次の通常攻撃命中時、当てた敵の周囲3m以内の別の敵それぞれに与える（1回で消費。記憶の再使用で時間を延長）", $"Area bonus damage +{v}% of your basic attack's damage per other enemy within 3m of its target, on the next basic hit within 5s of using an ordinary memory (consumed once; memory use refreshes the window)"); break;
                case Power.BareHandedPride: body = Loc.T($"通常記憶すべての残りクールダウン -{v / 10f:0.#}秒／通常記憶がすべてクールダウン中で使える回数がない間、通常攻撃が命中したとき（0.3秒に1回）", $"All ordinary memories' remaining cooldowns -{v / 10f:0.#}s on a basic attack hit while every ordinary memory is cooling down with no available charges (once per 0.3s)"); break;
                case Power.OpeningSalvo: body = Loc.T($"使用した通常記憶の残りクールダウン -{v}%／通常記憶がすべて使える状態から、最初の通常記憶を使ったとき。すべて使える状態に戻ると再び準備", $"Used ordinary memory's remaining cooldown -{v}% on the first ordinary-memory use while all ordinary memories are ready; rearms when all are ready again"); break;
                case Power.PileOn: body = Loc.T($"使用した記憶の残りクールダウン -{v}%／ほかの記憶を挟まず同じ記憶を3回続けて使うたび（移動の記憶は回数に数えず、連続使用をリセット）", $"Used memory's remaining cooldown -{v}% every 3 consecutive uses of the same memory without another memory in between (Movement memory use does not count and resets the sequence)"); break;
                case Power.AceInHand: body = Loc.T($"通常記憶の与ダメージ +{v}%／装備中の奥義の記憶が使える間（星座で奥義になった記憶も対象。奥義がなければ無効）", $"Ordinary-memory damage +{v}% while your equipped Ultimate memory is ready (includes memories converted to Ultimate by constellations; inactive without an Ultimate)"); break;
                case Power.CrystalCircuit: body = Loc.T($"通常記憶すべての残りクールダウン -{v}%／自分のエッセンスが発動したとき", $"All ordinary memories' remaining cooldowns -{v}% when your Essence activates"); break;
                case Power.ShardBoon: body = Loc.T($"HP回復 +自分の最大HPの{v / 10f:0.#}%／黄金の欠片かドリームダストの欠片を壊したとき（0.3秒に1回）", $"Health restored +{v / 10f:0.#}% of your maximum health when breaking a Gold or Dreamdust fragment (once per 0.3s)"); break;
                case Power.Lifeline: body = Loc.T($"自分の召喚獣の与ダメージ +{v}%／自分に最大HPを超えた回復が出た後4秒間（重ならず、再発動で時間を延長）", $"Your summons' damage +{v}% for 4s after you receive overhealing (refreshes without stacking)"); break;
                case Power.DreamOmen: body = Loc.T($"通常記憶すべての残りクールダウン -{v}%、障壁 +自分の最大HPの{v / 2f:0.#}%／夢の出来事が始まったとき。自分への障壁は10秒間", $"All ordinary memories' remaining cooldowns -{v}% and shield +{v / 2f:0.#}% of your maximum health for 10s when a dream event begins"); break;
                case Power.RearguardsWay: body = Loc.T($"通常記憶の与ダメージ +{v}%／敵までの最短距離がパーティで最も長い旅人である間（ソロや敵がいない間は無効）", $"Ordinary-memory damage +{v}% while your distance to your nearest enemy is the longest in the party (inactive when solo or without enemies)"); break;
                case Power.TollOfGrudge: body = Loc.T($"範囲魔法ダメージ +自分の最大HPの{v}%／実際に失ったHPの合計が自分の最大HPの60%に達するたび、自分の周囲6m以内の敵それぞれに与える（障壁吸収は数えず、世界を移ると蓄積を0に戻す）", $"Area magic damage +{v}% of your maximum health per enemy within 6m of you whenever actual health lost totals 60% of your maximum health (shield absorption does not count; progress resets between worlds)"); break;
                case Power.UnbowedMind: body = Loc.T($"障壁 +自分の最大HPの{v}%、行動妨害無効 +4秒／敵から実際にスタンを受けたとき、自分へ4秒間付与（8秒に1回。障壁が壊れても行動妨害無効は持続）", $"Shield +{v}% of your maximum health and crowd-control immunity +4s when actually stunned by an enemy (once per 8s; immunity persists if the shield breaks)"); break;
                case Power.PilingLuck: body = Loc.T($"次の通常攻撃の会心率 +{v}パーセントポイント／通常攻撃が会心せずに命中するたび。最大10回分まで蓄積し、会心で0に戻す（確定会心の攻撃では蓄積もリセットもしない）", $"Next basic attack critical chance +{v} percentage points per non-critical basic hit; accumulates up to 10 times and resets on a critical hit (guaranteed critical hits neither add nor reset it)"); break;
                case Power.WeakPointWound: body = Loc.T($"追加ダメージ +攻撃力か魔力の高い方の{v}%／同じ敵に会心を3回当てるたび。通常攻撃・記憶のどちらも対象（敵ごとに数え、会心の間隔が6秒以上空くと数え直し）", $"Bonus damage +{v}% of the higher of your attack damage or ability power every 3 critical hits on the same enemy, from basic attacks or memories (tracked per enemy; resets after at least 6s between critical hits)"); break;
                case Power.CritSplash: body = Loc.T($"範囲追加ダメージ +通常攻撃の与ダメージの{v}%／通常攻撃が会心で命中したとき、当てた敵の周囲3m以内の別の敵それぞれに与える（追加分は会心せず、連鎖しない）", $"Area bonus damage +{v}% of your basic attack's damage per other enemy within 3m of its target on a critical basic hit (splash cannot critically hit or chain)"); break;
                case Power.ReturningBlade: body = Loc.T($"撃破した記憶の残りクールダウン -{v}%／自分の記憶のダメージで敵を倒したとき、その記憶が対象", $"Killing memory's remaining cooldown -{v}% when your memory's damage kills an enemy; affects that same memory"); break;
                case Power.ReadyGuard: body = Loc.T($"障壁 +自分の最大HPの{v}%／敵とのダメージのやり取りが5秒間なかった後、最初に与えるか受けたとき、自分へ8秒間付与（障壁による吸収もダメージのやり取りに数える）", $"Shield +{v}% of your maximum health for 8s on the first hostile damage dealt or received after 5s without either (shield absorption also counts as combat damage)"); break;
                case Power.Apothecary: body = Loc.T($"通常記憶すべての残りクールダウン -{v}%、味方のHP回復 +ポーション回復量の50%／HPポーションを使ったとき、自分の周囲10m以内の味方旅人それぞれを回復", $"All ordinary memories' remaining cooldowns -{v}% and allied health restored +50% of the potion's healing per allied traveler within 10m of you when using a health potion"); break;
                case Power.SpilloverStrike: body = Loc.T($"追加ダメージ +撃破した一撃の過剰ダメージの{v}%／敵を倒したとき、倒した敵の周囲6m以内の別の敵1体へ与える（連鎖しない）", $"Bonus damage +{v}% of the killing hit's excess damage to one other enemy within 6m of the killed enemy (does not chain)"); break;
                default: return "-";
            }
            string cap = p == Power.BareHandedPride
                ? Loc.T($"（装備・星の同じ効果による1回の短縮量は合計{Content.PowerCap(p) / 10f:0.#}秒まで）", $" (reduction per trigger from this effect across equipment and stars capped at {Content.PowerCap(p) / 10f:0.#}s)")
                : p == Power.ShardBoon
                    ? Loc.T($"（装備・星の同じ効果による1回の回復量は合計で最大HPの{Content.PowerCap(p) / 10f:0.#}%まで）", $" (healing per trigger from this effect across equipment and stars capped at {Content.PowerCap(p) / 10f:0.#}% of your maximum health)")
                    : Loc.T($"（装備・星の同じ効果の数値を合算：上限{Content.PowerCap(p)}%。敵の数や発動回数の合計ではない）", $" (combined value of this effect across equipment and stars capped at {Content.PowerCap(p)}%; not a total across targets or triggers)");
            if (p == Power.ElementalHarvest || p == Power.PilingLuck || p == Power.Medley)
                cap += p == Power.ElementalHarvest
                    ? Loc.T("。この上限は属性1種類あたりの割合に適用", ". This cap applies to the percentage per element type")
                    : p == Power.PilingLuck
                        ? Loc.T("。この上限は蓄積1回あたりの会心率増加に適用", ". This cap applies to the critical-chance increase per stack")
                        : Loc.T("。この上限は記憶1種類あたりの増加に適用", ". This cap applies to the bonus per memory type");
            if (p == Power.CoStar || p == Power.DreamOmen || p == Power.Apothecary)
                cap += Loc.T("。この上限はクールダウン短縮の割合に適用", ". This cap applies to the cooldown-reduction percentage");
            if (p == Power.DreamOmen)
                cap += Loc.T("（障壁の割合上限はその半分）", " (the shield percentage cap is half of this)");
            if (p == Power.GleamingWard || p == Power.Medley)
                cap += Loc.T("。条件つき攻撃力・魔力の増加は、覚醒前の数値で全効果合計120%まで",
                    ". Conditional attack damage and ability power bonuses together are capped at +120%, counted before awakening");
            return body + cap;
        }
    }
}
