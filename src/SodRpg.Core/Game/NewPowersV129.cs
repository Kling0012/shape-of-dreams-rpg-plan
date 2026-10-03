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
                case Power.ShieldbreakBurst: body = Loc.T($"敵の攻撃で障壁が割れ切ると、割れる直前の障壁残量か最大HPの高い方の{v}%分のダメージを周囲5mに与える（6秒に1回。時間切れでは発動しない）", $"When enemy damage breaks your shield, deal {v}% of the higher of its remaining value before the hit or your max health to enemies within 5m (once per 6s; expiry does not trigger it)"); break;
                case Power.SharedWard: body = Loc.T($"障壁を得るたび、10m以内の味方旅人にその量の{v}%の障壁を4秒間与える（分けた障壁からは発動しない。重ならず大きい方を適用）", $"When you gain a shield, give allied travelers within 10m a 4s shield worth {v}% of that amount (shared shields do not trigger this; highest shield only, no stacking)"); break;
                case Power.ShieldBash: body = Loc.T($"障壁がある間、通常攻撃に現在の障壁量の{v}%分の無属性魔法ダメージを追加する（障壁は減らない）", $"While shielded, basic attacks deal {v}% of your current shield as bonus non-elemental magic damage without consuming it"); break;
                case Power.GleamingWard: body = Loc.T($"障壁がある間、攻撃力・魔力が{v}%上がる（エピック以上限定。条件つき攻撃力・魔力の総上限は覚醒前120%）", $"While shielded, gain +{v}% AD/AP (Epic or higher only; combined conditional AD/AP is capped at 120% before awakening)"); break;
                case Power.CoStar: body = Loc.T($"味方がUltimateを使うたび、自分の通常記憶の残りクールダウンが{v}%縮む（自分のUltimateでは{v / 2f:0.#}%）", $"An ally using an Ultimate cuts your ordinary memories' remaining cooldowns by {v}% (your own Ultimate grants {v / 2f:0.#}%)"); break;
                case Power.TriumphSong: body = Loc.T($"Ultimateを使うと、自分と10m以内の味方旅人を自分の最大HPの{v}%回復する", $"Using an Ultimate heals you and allied travelers within 10m for {v}% of your max health"); break;
                case Power.WatchfulHand: body = Loc.T($"味方旅人のHPが30%を切ると、自分の最大HPの{v}%の障壁をその味方に6秒間張る（同じ味方へ45秒に1回）", $"When an allied traveler falls below 30% health, give them a 6s shield worth {v}% of your max health (once per 45s per ally)"); break;
                case Power.KindnessReturns: body = Loc.T($"自分の記憶で別の味方旅人や召喚獣を回復すると、実際に回復した量の{v}%を自分も回復する", $"When your memory heals another allied traveler or summon, heal yourself for {v}% of the health actually restored"); break;
                case Power.WanderersEdge: body = Loc.T($"通常攻撃を直前とは別の敵に当てると、攻撃力か魔力の高い方の{v}%分のダメージを追加する（主対象への1回につき1回。同じ敵には1秒に1回）", $"Hitting a different enemy with a basic attack adds {v}% of the higher of AD or AP as damage (once per primary attack; once per 1s per enemy)"); break;
                case Power.FocusFire: body = Loc.T($"同じ敵へ通常攻撃を5回続けて当てるたび、攻撃力か魔力の高い方の{v}%分のダメージを追加する（別の敵に当てるか4秒空くと数え直し）", $"Every 5 consecutive basic hits on the same enemy deal {v}% of the higher of AD or AP as bonus damage (resets on a different target or a 4s gap)"); break;
                case Power.Breakout: body = Loc.T($"6m以内の敵が3体以下から4体以上になると、最大HPの{v}%の障壁を5秒間張り、周囲6mの敵を2秒間30%遅くする（10秒に1回）", $"When enemies within 6m rise from 3 or fewer to 4 or more, gain a 5s shield worth {v}% max health and slow enemies within 6m by 30% for 2s (once per 10s)"); break;
                case Power.DuelistsWay: body = Loc.T($"8m以内の敵が1体だけの間、その敵への通常攻撃に攻撃力か魔力の高い方の{v}%分のダメージを追加する", $"While exactly one enemy is within 8m, basic attacks against it deal {v}% of the higher of AD or AP as bonus damage"); break;
                case Power.ImmovableStance: body = Loc.T($"1秒以上動かずにいる間、与えるダメージが{v}%上がり、受けるダメージが{v / 2}%下がる（軽減は10%まで。動くと解除）", $"After standing still for 1s, deal {v}% more damage and take {v / 2}% less damage until you move (damage reduction capped at 10%)"); break;
                case Power.RunUp: body = Loc.T($"歩いて合計12m動くと、次の通常攻撃に攻撃力か魔力の高い方の{v}%分のダメージを追加する（1回で消費。回避・ダッシュは数えず、世界を移ると0に戻る。次の通常攻撃への上乗せは最大の1つだけを使い、残りは保持）", $"After walking 12m, your next basic attack deals {v}% of the higher of AD or AP as bonus damage (consumed once; excludes dodges and dashes; resets between worlds; only the largest next-basic bonus is consumed, others remain)"); break;
                case Power.StrafeShot: body = Loc.T($"移動しながら通常攻撃を当てると、敵の移動速度を1秒間{v}%下げる（重ならず時間を延長）", $"Basic attacks while moving slow the enemy by {v}% for 1s (refreshes without stacking)"); break;
                case Power.RelayHand: body = Loc.T($"敵を倒すと、最も近い味方旅人の通常記憶のうち残りクールダウンが最長の1つを{v}%短縮する（同じ味方へ2秒に1回）", $"On kill, cut the nearest allied traveler's longest remaining ordinary-memory cooldown by {v}% (once per 2s per ally)"); break;
                case Power.StardustCycle: body = Loc.T($"敵の光が5つに達すると、通常記憶のうち残りクールダウンが最長の1つを{v}%短縮する（全体で1秒に1回、同じ敵へ5秒に1回）", $"When an enemy reaches 5 Light stacks, cut your longest remaining ordinary-memory cooldown by {v}% (once per 1s overall and once per 5s per enemy)"); break;
                case Power.UmbralHeritage: body = Loc.T($"闇が2つ以上ある敵を倒すと、6m以内の敵5体までにそれぞれ{v}%の確率で闇を1つ付ける（闇は5つまで）", $"Killing an enemy with 2 or more Dark stacks has a {v}% chance per enemy to apply 1 Dark stack to up to 5 enemies within 6m (Dark capped at 5)"); break;
                case Power.BrittleIce: body = Loc.T($"冷気の敵に会心を当てると、攻撃力か魔力の高い方の{v}%分のダメージを追加する", $"Critical hits on chilled enemies deal {v}% of the higher of AD or AP as bonus damage"); break;
                case Power.ElementalHarvest: body = Loc.T($"2種類以上の属性がある敵を倒すと、属性1種類につき攻撃力か魔力の高い方の{v}%分のダメージを周囲4mに与える（4種類まで。連鎖しない）", $"Killing an enemy with at least 2 element types deals {v}% of the higher of AD or AP per type to enemies within 4m (up to 4 types; does not chain)"); break;
                case Power.PrismShift: body = Loc.T($"直前とは別の属性を付けると、最大HPの{v}%の障壁を3秒間張る（1秒に1回。重ならず大きい方を適用）", $"Applying a different element from your last grants a 3s shield worth {v}% max health (once per 1s; highest shield only)"); break;
                case Power.PackFeast: body = Loc.T($"自分の召喚獣が敵を倒すと、最大HPの{v}%の障壁を4秒間張る（重ならず大きい方を適用）", $"Your summons killing an enemy grants a 4s shield worth {v}% max health (highest shield only; does not stack)"); break;
                case Power.VanguardsOath: body = Loc.T($"敵に最も近い旅人である間、得る障壁量が{v}%増える（ソロでは{v / 2f:0.#}%。敵がいない間は無効）", $"While you are the traveler nearest an enemy, gain {v}% more shield ({v / 2f:0.#}% when solo; inactive without enemies)"); break;
                case Power.DeathBloom: body = Loc.T($"自分の召喚獣が倒されると、周囲4mに攻撃力か魔力の高い方の{v}%分のダメージを与える（時間切れでは発動せず、連鎖しない）", $"When your summon is killed, deal {v}% of the higher of AD or AP to enemies within 4m of it (expiry does not trigger this; does not chain)"); break;
                case Power.Medley: body = Loc.T($"直近12秒に使った通常記憶の種類1つにつき、攻撃力・魔力が{v}%上がる（3種類まで。種類が増えると12秒に延長。同じ記憶では増えず消えない。エピック以上限定。条件つき攻撃力・魔力の総上限は覚醒前120%）", $"Gain +{v}% AD/AP per distinct ordinary memory used within 12s (up to 3; new types refresh the 12s window, repeats neither add nor remove stacks; Epic or higher only; combined conditional AD/AP capped at 120% before awakening)"); break;
                case Power.Spellsweep: body = Loc.T($"通常記憶を使った後5秒以内の次の通常攻撃が、そのダメージの{v}%を周囲3mの別の敵にも与える（1回で消費。記憶を使うたび時間を延長）", $"After using an ordinary memory, your next basic attack within 5s deals {v}% of its damage to other enemies within 3m (consumed once; each memory use refreshes the window)"); break;
                case Power.BareHandedPride: body = Loc.T($"通常記憶がすべてクールダウン中の間、通常攻撃を当てると通常記憶のクールダウンが{v / 10f:0.#}秒縮む（0.3秒に1回）", $"While every ordinary memory is cooling down, basic hits reduce their cooldowns by {v / 10f:0.#}s (once per 0.3s)"); break;
                case Power.OpeningSalvo: body = Loc.T($"通常記憶がすべて使える状態から最初に使う記憶の残りクールダウンを{v}%短縮する（すべて使える状態に戻ると再準備）", $"When all ordinary memories are ready, the first one used has its remaining cooldown reduced by {v}% (rearms when all are ready again)"); break;
                case Power.PileOn: body = Loc.T($"ほかの記憶を挟まず同じ記憶を3回続けて使うたび、その記憶の残りクールダウンが{v}%縮む", $"Every 3 consecutive uses of the same memory without another memory in between cut its remaining cooldown by {v}%"); break;
                case Power.AceInHand: body = Loc.T($"装備中のUltimateが使える間、通常記憶のダメージが{v}%上がる（星座によるUltimate化を含む。Ultimateがなければ無効）", $"While your equipped Ultimate is ready, ordinary memories deal {v}% more damage (includes constellation conversions to Ultimate; requires an Ultimate)"); break;
                case Power.CrystalCircuit: body = Loc.T($"エッセンスが発動するたび、通常記憶の残りクールダウンが{v}%縮む", $"Each Essence activation cuts your ordinary memories' remaining cooldowns by {v}%"); break;
                case Power.ShardBoon: body = Loc.T($"黄金の欠片かドリームダストの欠片を壊すと、最大HPの{v / 10f:0.#}%を回復する（0.3秒に1回）", $"Breaking a Gold or Dreamdust fragment heals {v / 10f:0.#}% max health (once per 0.3s)"); break;
                case Power.Lifeline: body = Loc.T($"最大HPを超えた回復が出ると、4秒間、自分の召喚獣のダメージが{v}%上がる（重ならず時間を延長）", $"Overhealing grants your summons {v}% more damage for 4s (refreshes without stacking)"); break;
                case Power.DreamOmen: body = Loc.T($"夢の出来事が始まると、通常記憶の残りクールダウンが{v}%縮み、最大HPの{v / 2f:0.#}%の障壁を10秒間張る", $"When a dream event begins, cut ordinary-memory remaining cooldowns by {v}% and gain a 10s shield worth {v / 2f:0.#}% max health"); break;
                case Power.RearguardsWay: body = Loc.T($"パーティで敵から最も遠い旅人である間、通常記憶のダメージが{v}%上がる（ソロや敵がいない間は無効）", $"While you are the traveler farthest from enemies in your party, ordinary memories deal {v}% more damage (inactive when solo or without enemies)"); break;
                case Power.TollOfGrudge: body = Loc.T($"実際に失ったHPの合計が最大HPの60%に達するたび、周囲6mに最大HPの{v}%分の魔法ダメージを与える（障壁で吸収した分は数えず、世界を移ると溜めは0）", $"Every time actual health lost totals 60% max health, deal {v}% max health as magic damage within 6m (shield absorption does not count; progress resets between worlds)"); break;
                case Power.UnbowedMind: body = Loc.T($"敵からスタン・スロウ・ノックバックを受けると、最大HPの{v}%の障壁を4秒間張る（8秒に1回。自分の記憶による移動は除く）", $"Enemy stuns, slows or knockbacks grant a 4s shield worth {v}% max health (once per 8s; excludes movement caused by your own memories)"); break;
                case Power.PilingLuck: body = Loc.T($"会心しなかった通常攻撃のたび、次の通常攻撃の会心率が{v}%上がる（10回まで。会心で0に戻る。確定会心では数えず、リセットもしない）", $"Each non-critical basic attack adds {v}% critical chance to your next basic attack (up to 10 stacks; a critical hit resets them; guaranteed critical hits neither add nor reset stacks)"); break;
                case Power.WeakPointWound: body = Loc.T($"同じ敵へ会心を3回当てるたび、攻撃力か魔力の高い方の{v}%分のダメージを追加する（敵ごとに数え、6秒空くと0）", $"Every 3 critical hits on the same enemy deal {v}% of the higher of AD or AP as bonus damage (tracked per enemy; resets after a 6s gap)"); break;
                case Power.CritSplash: body = Loc.T($"通常攻撃が会心すると、そのダメージの{v}%を周囲3mの別の敵にも与える（追加分は会心せず、連鎖しない）", $"Critical basic attacks deal {v}% of their damage to other enemies within 3m (the splash cannot critically hit or chain)"); break;
                case Power.ReturningBlade: body = Loc.T($"記憶のダメージで敵を倒すと、その記憶の残りクールダウンが{v}%縮む", $"Killing an enemy with a memory cuts that memory's remaining cooldown by {v}%"); break;
                case Power.ReadyGuard: body = Loc.T($"5秒間ダメージを与えず受けずに過ごした後、最初に与えるか受けた瞬間、最大HPの{v}%の障壁を8秒間張る", $"After 5s without dealing or taking damage, your first damage dealt or taken grants an 8s shield worth {v}% max health"); break;
                case Power.Apothecary: body = Loc.T($"HPポーションを使うと、通常記憶の残りクールダウンが{v}%縮み、10m以内の味方旅人をポーションの回復量の50%回復する", $"Using a health potion cuts ordinary-memory remaining cooldowns by {v}% and heals allied travelers within 10m for 50% of the potion's healing"); break;
                case Power.SpilloverStrike: body = Loc.T($"敵を倒した一撃の過剰ダメージの{v}%を、6m以内の別の敵1体に与える（連鎖しない）", $"On kill, deal {v}% of the killing hit's excess damage to one other enemy within 6m (does not chain)"); break;
                default: return "-";
            }
            string cap = p == Power.BareHandedPride
                ? Loc.T($"（短縮は合計{Content.PowerCap(p) / 10f:0.#}秒まで）", $" (total reduction capped at {Content.PowerCap(p) / 10f:0.#}s)")
                : p == Power.ShardBoon
                    ? Loc.T($"（合計上限{Content.PowerCap(p) / 10f:0.#}%）", $" (combined value capped at {Content.PowerCap(p) / 10f:0.#}%)")
                    : Loc.T($"（効果値の合計上限{Content.PowerCap(p)}%）", $" (combined value capped at {Content.PowerCap(p)}%)");
            return Loc.T($"【{Name(p)}】", $"[{Name(p)}] ") + body + cap;
        }
    }
}
