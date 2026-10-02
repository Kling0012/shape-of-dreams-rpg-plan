using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>初めて触る人向けのヒント。場面ごとに一度だけ出す。</summary>
    public enum Hint
    {
        Welcome = 0,
        FirstDrop = 1,
        FirstSecurePoint = 2,
        FirstSecure = 3,
        FirstDelve = 4,
        FirstDefeat = 5,
        TalentPoints = 6,
        KeystoneReady = 7,
        FirstNightmare = 8,
        FirstBounty = 9,
        StarterGear = 10,
        ForgeReady = 11,
    }

    public sealed class HintDef
    {
        public Hint Id;
        public Txt Title;
        public Txt Body;
    }

    /// <summary>
    /// 初めての動線：初期装備と場面別ヒント。ヒントは Profile.SeenHints に記録し、一度見たものは出さない。
    /// </summary>
    public static class Onboarding
    {
        public static readonly IReadOnlyList<HintDef> All = new[]
        {
            new HintDef
            {
                Id = Hint.Welcome, Title = new Txt("ようこそ、Dreamforge へ", "Welcome to Dreamforge"),
                Body = new Txt(
                    "このMODでは、Shape of Dreams の遠征で「持ち帰れる装備（遺物）」が手に入るようになります。\n" +
                    "① 敵を倒すと遺物が落ちます。協力プレイでも一人ひとりに別々に落ちます。\n" +
                    "② 新しいゾーンに着くと「確保地点」が開きます。確保すれば拾った物を持ち帰れます。深く潜れば戦利品が増えますが、そのぶん危険も増します。\n" +
                    "③ 装備・鍛冶・星図は [F6] のメニューから使えます。最初の遺物を3つ用意したので、初めての遠征で自動的に装備されます。",
                    "This mod adds take-home gear (relics) to your Shape of Dreams runs.\n" +
                    "1) Enemies drop relics (personal loot for each player).\n" +
                    "2) Each new zone is a secure point: Secure to keep your loot, or Delve for more loot and more danger.\n" +
                    "3) Gear, forge and sigils live in the [F6] menu. You get 3 starter relics, auto-equipped on your first run."),
            },
            new HintDef
            {
                Id = Hint.StarterGear, Title = new Txt("初期の遺物を装備しました", "Starter relics equipped"),
                Body = new Txt("空いていた枠に初期の遺物を装備しました。左のパネルで状態を確認できます。強い遺物を拾ったら、確保地点で [F6] から付け替えましょう。",
                    "Your empty slots now hold starter relics. Check the Dreamforge panel on the left. When you find better ones, swap them at a secure point via [F6]."),
            },
            new HintDef
            {
                Id = Hint.FirstDrop, Title = new Txt("遺物を拾いました", "You found a relic!"),
                Body = new Txt("拾った遺物はまだ「未確保」です。次のゾーンに着いたときに「確保」すると保管庫に入り、装備できるようになります。全滅すると未確保の物は遺失物になります。",
                    "New relics start unsecured. Secure them when you reach the next zone to send them to your stash and equip them. If your party is wiped, unsecured loot becomes Lost & Found."),
            },
            new HintDef
            {
                Id = Hint.FirstSecurePoint, Title = new Txt("確保地点に着きました", "Secure point"),
                Body = new Txt("ここでは次のどちらかを選びます。\n・確保する [F7]：拾った遺物と欠片を持ち帰ります。この先で全滅しても失いません。\n・深く潜る [F8]：潜行が1段深くなり、遺物が多く良い物が出やすくなります。悪夢化した敵が増え、受けるダメージも増えます。\n迷ったら、まずは確保しましょう。遠征中に [F6] で装備を付け替えられるのは、この確保地点だけです。",
                    "Choose here.\n- Secure [F7]: keep your unsecured relics and shards (safe).\n- Delve [F8]: +1 delve level: more and better drops and nightmare elites, but more damage taken.\nWhen unsure, Secure. During a run, [F6] gear changes are only allowed here."),
            },
            new HintDef
            {
                Id = Hint.FirstSecure, Title = new Txt("遺物を持ち帰りました", "Secured"),
                Body = new Txt("保管庫に入った遺物は、もう失われることはありません。[F6] の「装備」タブで比較して付け替え、「鍛冶」タブで強化・分解ができます。",
                    "Stashed relics are safe forever. Compare and equip them in the [F6] Gear tab; enhance or salvage them in the Forge tab."),
            },
            new HintDef
            {
                Id = Hint.FirstDelve, Title = new Txt("さらに深く潜りました", "You delved deeper"),
                Body = new Txt("潜行が深いほど戦利品は増えますが、未確保の物を抱えたままです。次の確保地点で確保すると、潜行に応じて欠片のボーナスも付きます。",
                    "Deeper delves bring more loot, but you are still carrying it unsecured. Secure at the next point to bank it, with a shard bonus for your delve level."),
            },
            new HintDef
            {
                Id = Hint.FirstDefeat, Title = new Txt("夢から覚めました", "You awoke"),
                Body = new Txt("未確保の遺物は「遺失物」に移りました。次の遠征で戦闘部屋を突破すると、最も良いものを1つ取り戻せます。欠片の一部は残響として戻っています。",
                    "Your unsecured relics moved to Lost & Found. Clear combat rooms next run to recover the best one. Some shards came back as echoes."),
            },
            new HintDef
            {
                Id = Hint.TalentPoints, Title = new Txt("星図のポイントが増えました", "Star map points earned"),
                Body = new Txt("夢のレベルが上がり、ポイントを得ました。遠征の外で [F6] の「星図」タブを開き、使っている旅人の刻印に振りましょう。旅人ごとに、その旅人のスキルを伸ばす刻印があります。",
                    "Your Dream Level rose. Outside a run, open the [F6] Star Map tab and spend points on your Traveler's sigils, which boost that Traveler's own kit."),
            },
            new HintDef
            {
                Id = Hint.KeystoneReady, Title = new Txt("到達刻印を選べるようになりました", "Keystones unlocked"),
                Body = new Txt("この旅人の熟練度が上がり、到達刻印を選べるようになりました。選ぶには、この旅人のツリーに6ポイント以上振っておく必要があります。",
                    "This Traveler's mastery now allows a keystone (needs 6 points in the tree)."),
            },
            new HintDef
            {
                Id = Hint.FirstNightmare, Title = new Txt("悪夢化した敵", "Nightmare elite"),
                Body = new Txt("頭上にピンクの名札がある敵は「悪夢化」しています。普通より強いですが、倒すと1段上の格の戦利品を落とします。深く潜るほど増えます。",
                    "Enemies with a pink tag are nightmares: tougher, but they drop loot one tier higher. They appear more as you delve deeper."),
            },
            new HintDef
            {
                Id = Hint.FirstBounty, Title = new Txt("依頼を達成しました", "Bounty complete"),
                Body = new Txt("遠征ごとに依頼が3つあります（左のパネル）。祭壇・商人・強化など、本体での行動も依頼になります。",
                    "Each run has 3 bounties (left Dreamforge panel). Many are about using the game's own shrines, merchants and upgrades."),
            },
            new HintDef
            {
                Id = Hint.ForgeReady, Title = new Txt("鍛冶を使ってみましょう", "Forge ready"),
                Body = new Txt("欠片が貯まりました。[F6] の「鍛冶」タブで遺物を強化（+5まで）したり、同じレア度3つを合成したりできます。",
                    "You have enough shards. In the [F6] Forge tab you can enhance relics (+5 max) or transmute 3 of a rarity into a better one."),
            },
        };

        public static HintDef Get(Hint h)
        {
            foreach (var d in All)
                if (d.Id == h) return d;
            return null;
        }

        /// <summary>まだ見ていなければ既読にして true。</summary>
        public static bool Show(Profile p, Hint h)
        {
            if (p.HintsOff) return false;
            return p.SeenHints.Add((int)h);
        }

        public const int StarterShardsForForgeHint = 15;

        /// <summary>
        /// 初期装備：最初に一度だけ、各枠1つずつアンコモンの遺物を保管庫に入れる。
        /// </summary>
        public static List<Relic> GrantStarterKit(Profile p)
        {
            var given = new List<Relic>();
            if (p.StarterGranted) return given;
            p.StarterGranted = true;
            var rng = p.TakeRng();
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                var r = Loot.RollRelic(rng, Rarity.Uncommon, 1, slot);
                p.Stash.Add(r);
                p.StarterUids.Add(r.Uid);
                p.Codex.Add(r.BaseId);
                given.Add(r);
            }
            p.StoreRng(rng);
            return given;
        }

        /// <summary>この旅人に何も装備していなければ、初期の遺物を空いた枠に装備する。装備したら true。</summary>
        public static bool AutoEquipStarter(Profile p, string heroKey)
        {
            if (string.IsNullOrEmpty(heroKey) || p.StarterUids.Count == 0) return false;
            var h = p.Hero(heroKey);
            if (h.Equipped.Any(u => u != null)) return false;
            bool any = false;
            foreach (var uid in p.StarterUids)
            {
                var r = p.FindStash(uid);
                if (r == null || h.Equipped[(int)r.Slot] != null) continue;
                h.Equipped[(int)r.Slot] = uid;
                any = true;
            }
            return any;
        }
    }
}
