# -*- coding: utf-8 -*-
"""Generates named.json and minisets.json for v1.32 low-rarity volume (spec 3.2 / 8).

Reads the 360 existing BaseDefs and their families (tools/lowrarity/base-families.json),
assigns each named item (銘品) an existing base of its family, picks powers that are
droppable and allowed for the rarity (Content.PowerAllowedForRarity), preferring the
base family's preferred powers (Families.cs FamilyPrefs), and combines them with the
hand-authored names/lore below. Also emits the 30 mini sets (小セット).

Slot pools alone cannot supply 48 distinct single-power sets per slot (24 Uncommon +
24 Rare, max usable pool = 36), so every power is a droppable rarity-allowed power,
with family preference first and slot-pool membership second.

Run:  python tools/lowrarity/gen_named.py
"""
import collections
import json
import re
import sys

SRC = "src/SodRpg.Core/Game/Content.cs"
NEWP = "src/SodRpg.Core/Game/NewPowersV129.cs"
FAMS = "src/SodRpg.Core/Game/Families.cs"
IDS = "src/SodRpg.Core/Game/Ids.cs"
BASEFAM = "tools/lowrarity/base-families.json"
OUT_NAMED = "tools/lowrarity/named.json"
OUT_SETS = "tools/lowrarity/minisets.json"

FAM = ["Plain", "Frost", "Flame", "Light", "Dark", "Guard", "Gale", "Mend", "Summon", "Memory"]
SLOTS = ["Weapon", "Armor", "Charm", "Head", "Hands", "Feet"]
SLOT_LOW = {"Weapon": "weapon", "Armor": "armor", "Charm": "charm", "Head": "head", "Hands": "hands", "Feet": "feet"}

# Rarity split per family within one slot (must total 24 U / 24 R / 12 E per slot).
SPLIT = {
    "Plain":  ["U", "U", "R", "U", "R", "E"],
    "Frost":  ["U", "R", "U", "R", "R", "E"],
    "Flame":  ["U", "U", "R", "U", "R", "E"],
    "Light":  ["U", "R", "U", "R", "R", "E"],
    "Dark":   ["U", "U", "R", "U", "R", "E"],
    "Guard":  ["U", "R", "U", "R", "R", "E"],
    "Gale":   ["U", "U", "R", "U", "R", "E"],
    "Mend":   ["U", "R", "U", "R", "R", "E"],
    "Summon": ["U", "R", "U", "R", "E", "E"],
    "Memory": ["U", "R", "U", "R", "E", "E"],
}

# Neutral "craft" powers for the Plain family (it has no FamilyPrefs entry).
PLAINPREF = ["Momentum", "Lifesteal", "Thorns", "Barrier", "Tailwind", "Shatter",
             "Aegis", "OpeningStrike", "SoulSiphon", "SecondWind", "Bulwark",
             "Executioner", "ChainLightning", "WatchfulHand", "StillWater"]


def load_reference():
    src = open(SRC, encoding="utf-8").read()
    ids = open(IDS, encoding="utf-8").read()
    fams = open(FAMS, encoding="utf-8").read()
    newp = open(NEWP, encoding="utf-8").read()

    from pathlib import Path
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "balance"))
    from equipment_items_values import load_bases
    canonical_bases = load_bases()
    original_ids = set(json.load(open(BASEFAM, encoding="utf-8")))
    base_pat = re.compile(
        r'new BaseDef\("([^"]+)", Slot\.(\w+), Line\.(\w+), new Txt\("([^"]*)", "([^"]*)"\), '
        r"Stat\.(\w+), EquipmentItemsBalanceValues\.(\w+)(?:, Family\.(\w+))?\)")
    bases = collections.defaultdict(list)
    for m in base_pat.finditer(src):
        if m.group(1) not in original_ids:
            continue
        bases[m.group(2)].append(dict(id=m.group(1), line=m.group(3), ja=m.group(4),
                                      en=m.group(5), stat=m.group(6), val=canonical_bases[m.group(1)]["implicitValue"],
                                      family=m.group(8) or "Plain"))
    assert sum(len(v) for v in bases.values()) == 360

    from equipment_pools_values import load_affixes, load_power_pools
    pools = {slot: {power: (row["min"], row["max"]) for power, row in rows.items()}
             for slot, rows in load_power_pools()["pools"].items()}

    affixes = collections.defaultdict(list)
    for rows in load_affixes()["pools"].values():
        for stat, row in rows.items():
            affixes[stat].append((row["min"], row["max"]))

    enum_order = {name: int(val) for name, val in re.findall(r"^\s*(\w+)\s*=\s*(\d+),", ids, re.M)}
    enum_names = set(enum_order)
    # NewPowersV129.IsConditionalAttribute (NewPowersV129.cs:8-17).
    conditional = set(re.findall(r"case Power\.(\w+):", re.search(
        r"IsConditionalAttribute\(Power p\)\s*\{\s*switch \(p\)\s*\{\s*(.*?)default:", newp, re.S).group(1)))
    # Content.IsPowerDroppable (Content.cs): None / ShadowStep / currency powers are not droppable.
    currency = {"KillGoldPct", "EliteKillGoldPct", "DreamDustPct", "DreamDustDelvePct"}
    droppable = set()
    for ps in pools.values():
        droppable |= set(ps)
    droppable -= {"None", "ShadowStep"} | currency
    assert droppable <= enum_names, droppable - enum_names

    prefs = dict(re.findall(r"\[Family\.(\w+)\] = new\[\] \{ (.*?) \}", fams))
    fampref = {f: re.findall(r"Power\.(\w+)", v) for f, v in prefs.items()}
    famtag = json.load(open(BASEFAM, encoding="utf-8"))
    return bases, pools, affixes, enum_order, conditional, droppable, fampref, famtag


def build_skeleton(bases, pools, enum_order, conditional, droppable, fampref):
    """(slot, family, ordinal) -> {rarity, baseId, powers:[(power, band)]}."""
    universe_nc = droppable - conditional
    skel = {}
    for slot in SLOTS:
        pool = pools[slot]
        fambases = {f: sorted([b for b in bases[slot] if b["family"] == f], key=lambda b: b["id"]) for f in FAM}
        for f in FAM:
            assert len(fambases[f]) == 6, (slot, f, len(fambases[f]))

        def score(power, fam):
            prefs = fampref.get(fam) or PLAINPREF
            s = 0
            if power in prefs:
                s += 4
            if power in pool:
                s += 2
            if pool.get(power, (0, 1))[0] == pool.get(power, (1, 0))[1]:
                s -= 1  # single-value range: band is meaningless, deprioritise
            return s

        used_singles = set()
        used_pairs = set()
        used_p1 = set()
        used_p2 = set()

        def pick(cands, used, fam, extra=()):
            best_seen = set(extra)
            for p in sorted(cands, key=lambda p: (-score(p, fam), enum_order[p])):
                if p not in used and p not in best_seen:
                    return p
            return None

        for fam in [f for f in FAM if f != "Plain"] + ["Plain"]:
            for ordinal, rar in enumerate(SPLIT[fam]):
                base = fambases[fam][ordinal]
                key = (slot, fam, ordinal)
                if rar in ("U", "R"):
                    power = pick(universe_nc, used_singles, fam)
                    used_singles.add(power)
                    band = "low" if rar == "U" or ordinal % 2 == 0 else "mid"
                    skel[key] = dict(rarity=rar, baseId=base["id"], powers=[(power, band)])
                else:
                    prefs = fampref.get(fam) or PLAINPREF
                    p1 = pick(prefs, used_p1, fam) or pick(droppable, used_p1, fam)
                    used_p1.add(p1)
                    p2 = None
                    for in_pool in (True, False):
                        cands = [p for p in droppable - conditional if p != p1 and (p in pool) == in_pool]
                        for cand in sorted(cands, key=lambda p: (-score(p, fam), enum_order[p])):
                            if cand not in used_p2 and frozenset({p1, cand}) not in used_pairs:
                                p2 = cand
                                break
                        if p2 is not None:
                            break
                    assert p2 is not None, (slot, fam, p1)
                    used_p2.add(p2)
                    used_pairs.add(frozenset({p1, p2}))
                    b1 = "high" if (ordinal + SLOTS.index(slot)) % 2 == 0 else "mid"
                    b2 = "mid" if ordinal % 2 == 0 else "low"
                    skel[key] = dict(rarity=rar, baseId=base["id"], powers=[(p1, b1), (p2, b2)])
        assert len(used_singles) == 48, (slot, len(used_singles))
    return skel


def assign_ids(skel):
    """named.<slot>.NNN: Uncommon 001-024, Rare 025-048, Epic 049-060."""
    ids = {}
    for slot in SLOTS:
        n = {"U": 0, "R": 24, "E": 48}
        for fam in FAM:
            for ordinal in range(6):
                key = (slot, fam, ordinal)
                rar = skel[key]["rarity"]
                ids[key] = "named.%s.%03d" % (SLOT_LOW[slot], n[rar] + 1)
                n[rar] += 1
        assert n == {"U": 24, "R": 48, "E": 60}, (slot, n)
    return ids


# ---------------------------------------------------------------------------
# Hand-authored names and lore. Keyed by named id (see skeleton above).
# nameJa <= 14 chars, no digits; loreJa one line <= 30 chars.
# ---------------------------------------------------------------------------
PROSE = {
    # ---- Weapon / Plain ----
    "named.weapon.001": ("先んじる骨断ち", "First-Cut Cleaver",
                         "獲物が息を呑む前に、刃は落ちる。", "The blade falls before the prey draws breath."),
    "named.weapon.002": ("断ち砕く連鎖", "Chainbreaking Edge",
                          "倒れた者の跡で、次が砕ける。", "Where one falls, the next one shatters."),
    "named.weapon.025": ("詠唱の静水", "Still-Chant Wand",
                         "詠い止まれば、水は盾となる。", "When the chant stills, the water becomes a shield."),
    "named.weapon.003": ("鉤爪の渇き", "Thirsting Hook",
                         "血を知った爪は、深く食い込む。", "Having tasted blood, the hook bites deeper."),
    "named.weapon.026": ("射止める盾音", "Shield-Crack Shot",
                         "矢鳴りは、盾の音に似ている。", "An arrow's song can ring like a struck shield."),
    "named.weapon.049": ("戦斧の踊り", "Dancing War Axe",
                         "最初の一振りが、舞の拍子を決める。", "The first swing sets the rhythm of the dance."),
    # ---- Weapon / Frost ----
    "named.weapon.004": ("霜の足枷", "Frost Fetters",
                         "霜は足を掴み、槍は息を止める。", "The frost grips the feet; the spear stops the breath."),
    "named.weapon.027": ("凍て穂の脆さ", "Brittle Rimespike",
                         "凍てついた装甲は、花のように割れる。", "Frozen armor splits like a blossom."),
    "named.weapon.005": ("渦潮の霜刃", "Maelstrom Frostblade",
                         "潮の渦が、刃から冷気を巻く。", "The tide's whirl drags cold from the blade."),
    "named.weapon.028": ("北風の白息", "Northwind Steam",
                         "吹き荒ぶ風が、白い霧を残す。", "The howling wind leaves a white mist behind."),
    "named.weapon.029": ("雪解けの晶", "Thawlight Crystal",
                         "解けかけの氷が、光を鎧う。", "Half-melted ice wears the light as armor."),
    "named.weapon.050": ("波切りの冬牢", "Tidecutter Wintercell",
                         "斬った水が凍り、敵を閉じ込める。", "The cut water freezes and seals the foe."),
    # ---- Weapon / Flame ----
    "named.weapon.006": ("燃えさかる刃", "Seething Blade",
                         "四つ目の振りが、火を呼ぶ。", "Every fourth swing calls the fire."),
    "named.weapon.007": ("火の粉を撒く鞭", "Spark-Scattering Whip",
                         "しなる先から、火の粉が散る。", "Sparks scatter from the flicking tip."),
    "named.weapon.030": ("飛び火の長弓", "Wildfire Longbow",
                         "火はひとつにとどまらない。", "Fire never stays with just one."),
    "named.weapon.008": ("砕岩の黒種", "Cinder Rockbreak",
                         "砕いた岩の奥に、黒い種を残す。", "In the broken rock it leaves a black seed."),
    "named.weapon.031": ("断ち切りの舞", "Severing Dance",
                         "斬り落とすたび、足が軽くなる。", "Each cut taken makes the feet lighter."),
    "named.weapon.051": ("白熱の突き", "Whiteheat Thrust",
                         "白い刃が貫き、火の粒を撒く。", "The white blade pierces and sows sparks."),
    # ---- Weapon / Light ----
    "named.weapon.009": ("暁の輝き笏", "Dawning Scepter",
                         "振れば、夜の名残が剥がれる。", "A wave of it peels the last of night away."),
    "named.weapon.032": ("破邪の灯", "Wardlight Lance",
                         "邪を破る槍先に、灯がともる。", "A flame lights where the lance breaks evil."),
    "named.weapon.010": ("星除けの灯杖", "Starward Lantern Rod",
                         "灯は、技の果てに星を描く。", "The lantern draws a star at the art's end."),
    "named.weapon.033": ("星剣の情け", "Starlit Kindness",
                         "投じた星が、恩を返して戻る。", "The thrown star returns to repay a kindness."),
    "named.weapon.034": ("奏星の継ぎ糸", "Star-Song Lifeline",
                         "弦の音が、命をつなぐ糸になる。", "The string's note becomes a thread of life."),
    "named.weapon.052": ("陽射しの二閃", "Sunlit Double Flash",
                         "光を纏い、日向のように舞う。", "Wrapped in light, it dances like open day."),
    # ---- Weapon / Dark ----
    "named.weapon.011": ("星霜の断罪", "Astral Verdict",
                         "星の巡りが、終わりを告げる。", "The stars' slow turn pronounces the end."),
    "named.weapon.012": ("黄昏の吸魂", "Dusk Soulreaper",
                         "鎌が過ぎた後、魂の抜け殻が残る。", "After the scythe passes, only husks remain."),
    "named.weapon.035": ("熾火の影刃", "Ember-Shadow Katar",
                         "熾きた火の周りに、影が濃くなる。", "Shadows thicken around the glowing coal."),
    "named.weapon.013": ("宵闇の蝕刃", "Gloaming Eclipse",
                         "光と影が刃で交わる瞬間。", "The instant light and shadow cross on the blade."),
    "named.weapon.036": ("月鎌の相続", "Moonsickle Heritage",
                         "三日月が、影の名を譲る。", "The crescent moon bequeaths a shadowed name."),
    "named.weapon.053": ("双子の刈り入れ", "Twin Harvest",
                         "一本が刈り、もう一本が飲む。", "One reaps; the other drinks."),
    # ---- Weapon / Guard ----
    "named.weapon.014": ("錨の輪", "Anchor Ring",
                         "囲まれてこそ、錨は光る。", "Surrounded, the anchor shines."),
    "named.weapon.037": ("鉄の棘", "Iron Thorns",
                         "受けた痛みは、そのまま返す。", "Whatever pain it takes, it gives straight back."),
    "named.weapon.015": ("誓いの守護霊", "Oathbound Guardian",
                         "大波を食らっても、誓いは砕けない。", "Even a great blow cannot break the oath."),
    "named.weapon.038": ("不動の盾打ち", "Immovable Shieldbash",
                         "構えを解かなければ、城は落ちない。", "While the stance holds, the castle stands."),
    "named.weapon.039": ("亀甲の備え", "Tortoise Readiness",
                         "遅い甲羅こそ、最後まで残る。", "The slow shell is what remains at the end."),
    "named.weapon.054": ("城壁の血肉", "Rampart and Blood",
                         "壁は厚く、槍は生き血を知る。", "The wall is thick; the spear knows blood."),
    # ---- Weapon / Gale ----
    "named.weapon.016": ("夕薫の乱舞", "Evening Whirl",
                         "扇が混むほど、風は荒れる。", "The more crowded, the wilder the wind."),
    "named.weapon.017": ("渡り鳥の双閃", "Wanderer's Twin Flash",
                         "旅の刃は、帰り道も知っている。", "A traveler's blade knows the way home too."),
    "named.weapon.040": ("燕返しの助走", "Swallow Run-Up",
                         "低く飛べば、刃はさらに伸びる。", "Fly low, and the blade stretches further."),
    "named.weapon.018": ("雷鳴の追い風", "Thunder Tailwind",
                         "鳴り止んだ雷が、背を押す。", "The fading thunder pushes at your back."),
    "named.weapon.041": ("双牙の旋風", "Twin-Fang Whirl",
                         "身をひねれば、牙が渦を描く。", "A twist of the body, and the fangs draw a spiral."),
    "named.weapon.055": ("風唸りの極", "Windhowl Peak",
                         "唸る風の中で、杖も獣になる。", "In the howling wind, the staff turns beast."),
    # ---- Weapon / Mend ----
    "named.weapon.019": ("鎮めの吸命", "Calming Leech",
                         "敵の騒ぎを、自分の静けさに変える。", "It turns the foe's fury into your own calm."),
    "named.weapon.042": ("露払いの灯守", "Dewclear Warden",
                         "露が晴れる頃、もう一度立ち上がる。", "When the dew clears, you rise once more."),
    "named.weapon.020": ("灯台の溢れ", "Lighthouse Overflow",
                         "満ちた光は、余すことなく盾になる。", "Light that overflows becomes a shield."),
    "named.weapon.043": ("巡礼の薬缶", "Pilgrim's Medicine Pot",
                         "歩いた道のりだけ、薬は効く。", "The medicine works for every mile walked."),
    "named.weapon.044": ("静水の雷", "Stillwater Lightning",
                         "水面を走る稲妻のように、静かに繋がる。", "Like lightning over still water, it links in silence."),
    "named.weapon.056": ("誓約の刈り", "Vowbound Harvest",
                         "誓いは血を分け、終わりを断つ。", "The vow shares blood and cuts the ending short."),
    # ---- Weapon / Summon ----
    "named.weapon.021": ("骨笛の分け前", "Bone-Flute Share",
                         "笛の音は、味方の盾を半分持つ。", "The flute's note carries half a comrade's shield."),
    "named.weapon.045": ("城門の共演", "Gatehouse Co-Star",
                         "門が開く時、誰かが並んで立つ。", "When the gate opens, someone stands beside you."),
    "named.weapon.022": ("御影の見守り", "Granite Watch",
                         "石は動かず、それでも見ている。", "The stone does not move, yet it watches."),
    "named.weapon.046": ("竪琴の継ぎ手", "Harp Relay",
                         "一つめの音は、次の者へ渡される。", "The first note is handed to the next player."),
    "named.weapon.057": ("嵐の分かち合い", "Storm-Shared Ward",
                         "嵐の夜は、傘を差し合う仲間がいる。", "On stormy nights, someone shares your umbrella."),
    "named.weapon.058": ("潮騒の共演", "Tidal Co-Star",
                         "波も、二つ重なれば音になる。", "Even waves, when two overlap, become sound."),
    # ---- Weapon / Memory ----
    "named.weapon.023": ("法螺の余韻", "Conch Echo",
                         "法螺の音が、技の間を縮める。", "The conch's drone shortens the wait between arts."),
    "named.weapon.047": ("夢綴じの切り札", "Dreambound Trump",
                         "読み終わった頁に、一枚だけ残る。", "One card stays behind on the final page."),
    "named.weapon.024": ("夢見の終曲", "Dreamer's Finale",
                         "三つの夢が、最後の幕を早める。", "Three dreams hurry the final curtain."),
    "named.weapon.048": ("蛍火の巡り", "Firefly Cycle",
                         "蛍の光は、星屑の通り道。", "Firefly light rides the stardust road."),
    "named.weapon.059": ("月明の切り拓き", "Moonlit Trump Card",
                         "月に照らされて、会心が鋭くなる。", "By moonlight, the critical touch sharpens."),
    "named.weapon.060": ("星図の伏せ札", "Star-Chart Hidden Card",
                         "星の並びに、まだ見ぬ一手がある。", "In the stars' arrangement hides an unplayed move."),
    # ---- Armor / Plain ----
    "named.armor.001": ("鎖の輪舞", "Chain Ronde",
                        "鎖が鳴るたび、歩みが速くなる。", "Each clink of chain quickens the step."),
    "named.armor.002": ("城砦の雷紋", "Citadel Lightning Crest",
                        "石壁を伝う稲妻は、味方の合図。", "Lightning along stone is a signal to allies."),
    "named.armor.025": ("火の粉の砕音", "Cinder Crack",
                        "灰を払えば、下から音が砕ける。", "Brush the ash away and something cracks beneath."),
    "named.armor.003": ("革の先手", "Leather First Move",
                        "革の軋みは、敵より先に鳴る。", "The leather creaks before the enemy moves."),
    "named.armor.026": ("遊撃の割れ盾", "Skirmisher's Shieldbreak",
                        "小さな盾も、割れれば音になる。", "Even a small shield makes a sound when it breaks."),
    "named.armor.049": ("棘付きの静水", "Spineplate Stillwater",
                        "棘は返し、水は受け止める。", "The spikes give back; the water receives."),
    # ---- Armor / Frost ----
    "named.armor.004": ("極光の足止め", "Aurora Snare",
                        "揺れる光に、追われる足が止まる。", "Under the wavering light, fleeing feet slow."),
    "named.armor.027": ("霜織りの衣", "Frostweave Mantle",
                        "織り込んだ霜が、触れた者を凍らせる。", "The woven frost freezes whoever touches it."),
    "named.armor.005": ("霜法衣の湯気", "Frost-Robe Steam",
                        "冷たい衣の内側で、白い霧が育つ。", "Inside the cold robe, white mist gathers."),
    "named.armor.028": ("氷河の晶鎧", "Glacial Crystal Harness",
                        "削られた氷が、光を鎧として纏う。", "Carved ice wears light as its armor."),
    "named.armor.029": ("潮縛の脆氷", "Tidebound Brittle Ice",
                        "潮を吸った氷は、諸共に割れる。", "Tide-soaked ice shatters all together."),
    "named.armor.050": ("冬毛の囲い", "Winterwool Enclosure",
                        "厚い毛は冷えと敵を、同時に防ぐ。", "Thick wool keeps out cold and foes alike."),
    # ---- Armor / Flame ----
    "named.armor.006": ("玄武の火脈", "Basalt Firevein",
                        "岩の割れ目から、四つ目の火が噴く。", "From the rock's cracks, the fourth fire spouts."),
    "named.armor.007": ("舞手の火の粉", "Dancer's Sparks",
                        "衣の裾が、火の粉を撒き散らす。", "The hem scatters sparks as it turns."),
    "named.armor.030": ("残り火の飛び路", "Ember's Leaping Path",
                        "一つの火が、もう一つの火を呼ぶ。", "One flame calls another."),
    "named.armor.008": ("燠火の黒種", "Emberforged Seed",
                        "焼けた鎧の縫い目に、黒い種がある。", "In the seams of the fired plate, a black seed waits."),
    "named.armor.031": ("竜鱗の輪", "Dragon Scale Ring",
                        "鱗は重なり、囲まれても毀れない。", "Scales overlap; a ring that will not chip."),
    "named.armor.051": ("火山の返し火", "Volcanic Backfire",
                        "火を吐けば、灰が棘になって戻る。", "Spit fire, and the ash comes back as thorns."),
    # ---- Armor / Light ----
    "named.armor.009": ("暁光の灯", "Dawnlight Lamp",
                        "暁の光が、ひと息ごとに盾を織る。", "Dawnlight weaves a shield with every breath."),
    "named.armor.032": ("鏡絹の星盾", "Mirrorsilk Starshield",
                        "絹が星を映せば、盾の形になる。", "When silk mirrors a star, it takes a shield's shape."),
    "named.armor.010": ("朝露の輝き", "Morning-Dew Radiance",
                        "露の粒が、小さな光を重ねる。", "Each dewdrop stacks a little light."),
    "named.armor.033": ("祈りの返礼", "Prayer's Kind Return",
                        "捧げた祈りは、誰かの親切になる。", "A given prayer becomes someone's kindness."),
    "named.armor.034": ("陽光の継ぎ糸", "Sunlit Lifeline",
                        "日だまりの中で、命が縫い合わされる。", "In the sunpatch, life is stitched back together."),
    "named.armor.052": ("加護の二重織", "Wardsigil Double Weave",
                        "二つの護りを、一枚の布に織り込んだ。", "Two wards woven into a single cloth."),
    # ---- Armor / Dark ----
    "named.armor.011": ("奇襲の断末", "Ambush Finale",
                        "息の根を止めるのは、最初の一手。", "The first move is what stops the breath."),
    "named.armor.012": ("黄昏織りの影", "Duskweave Shadow",
                        "織り上がるのは、夕暮れそのもの。", "What it weaves is dusk itself."),
    "named.armor.035": ("狩りの吸魂", "Hunter's Siphon",
                        "獲物の最後の息を、着ていく。", "It wears the prey's final breath home."),
    "named.armor.013": ("墨染めの蝕", "Ink-Dyed Eclipse",
                        "墨が滲む処で、光と影が交わる。", "Where ink bleeds, light and shadow meet."),
    "named.armor.036": ("影織りの跡目", "Shadowweave Heirloom",
                        "外套を継いだ者に、影が従う。", "The shadow obeys whoever inherits the cloak."),
    "named.armor.053": ("ねじれ鎖の処罰", "Twisted-Chain Verdict",
                        "歪んだ鎖は、悪を締め上げ灯をともす。", "The twisted chain strangles evil and lights a lamp."),
    # ---- Armor / Guard ----
    "named.armor.014": ("甲羅の返し棘", "Shell's Returning Spine",
                        "叩いた手が、痛い。", "The hand that strikes it aches."),
    "named.armor.037": ("反撃の守護霊", "Counter Guardian",
                        "痛い一撃には、霊が蓋をする。", "Against the worst blows, a spirit throws the lid."),
    "named.armor.015": ("護りの不動", "Guardian Immovability",
                        "構えを守れば、守られる。", "Hold the stance, and be held."),
    "named.armor.038": ("鉄樹の備え", "Ironbark Readiness",
                        "硬い樹は、嵐の前に身を固める。", "The hard tree braces before the storm."),
    "named.armor.039": ("土塁の残身", "Rampart Echo",
                        "かわした体が、次の一手を覚える。", "The dodging body remembers the next move."),
    "named.armor.054": ("棘鎧の壁", "Thorn Wall Mail",
                        "棘の壁は、巨浪も霊も弾く。", "The thorn wall turns aside waves and spirits."),
    # ---- Armor / Gale ----
    "named.armor.016": ("流れの旋風", "Flowing Whirlwind",
                        "身をひねれば、外套が渦になる。", "Twist away, and the cloak becomes a whirl."),
    "named.armor.017": ("水銀の乱戦", "Quicksilver Melee",
                        "混むほど、水銀は沸き立つ。", "The more crowded, the more the mercury seethes."),
    "named.armor.040": ("嵐前の疾駆", "Stormfront Dash",
                        "嵐の前の風は、足に乗る。", "The wind before the storm rides your feet."),
    "named.armor.018": ("疾歩の追風", "Swiftstep Tailwind",
                        "倒した敵の分だけ、風が追う。", "For every foe felled, the wind follows harder."),
    "named.armor.041": ("旅人の渡り路", "Traveler's Crossing",
                        "馴れた道ほど、刃が冴える。", "On well-worn roads, the edge shines."),
    "named.armor.055": ("西風の舞い衣", "Zephyr Dancing Robe",
                        "袖が膨らめば、渦と喧騒が来る。", "When the sleeves fill, whirl and clamor follow."),
    # ---- Armor / Mend ----
    "named.armor.019": ("燭光の満ち", "Candlelight Fullness",
                        "溢れた灯が、蝋の盾になる。", "The overflowing flame becomes a wax shield."),
    "named.armor.042": ("深根の薬師", "Deeproot Apothecary",
                        "根が届く範囲まで、手当てが届く。", "Care reaches as far as the roots."),
    "named.armor.020": ("癒し帯の吸命", "Healing Sash Leech",
                        "締めた帯が、痛みを吸い取る。", "The bound sash draws the pain out."),
    "named.armor.043": ("灯守の再起", "Lampkeeper's Second Wind",
                        "灯が消えかけても、芯は残る。", "Even as the flame gutters, the wick remains."),
    "named.armor.044": ("行者の止水", "Ascetic Stillwater",
                        "動じなければ、水面が盾になる。", "Unshaken, the water's surface becomes a shield."),
    "named.armor.056": ("夜織りの溢れ", "Nightloom Overflow",
                        "夜に織った分だけ、命が余る。", "For all woven by night, life is left over."),
    # ---- Armor / Summon ----
    "named.armor.021": ("小盾の分け前", "Buckler Share",
                        "小さな盾も、二つなら大きい。", "A small shield, doubled, is large."),
    "named.armor.045": ("織り火の見守り", "Emberweave Watch",
                        "肩に掛けた火が、仲間を見ている。", "The fire on your shoulder watches your friends."),
    "named.armor.022": ("誓板の共演", "Oathplate Co-Star",
                        "誓いを並べれば、舞台ができる。", "Line up the oaths, and a stage appears."),
    "named.armor.046": ("海沫の継ぎ手", "Seafoam Relay",
                        "泡が弾ければ、次の波に渡す。", "When foam pops, it hands off to the next wave."),
    "named.armor.057": ("星屑の双護", "Stardust Twin Ward",
                        "降る星屑が、護りを分け合う。", "Falling stardust shares out its warding."),
    "named.armor.058": ("喚び獣の目付", "Summoner's Watchpost",
                        "呼んだ獣が、死角を見張る。", "The called beast guards your blind side."),
    # ---- Armor / Memory ----
    "named.armor.023": ("樹皮の終曲", "Barkbound Finale",
                        "年輪の果てに、幕引きの音がある。", "At the rings' end sounds the closing note."),
    "named.armor.047": ("霧衣の余韻", "Mist-Robe Echo",
                        "霧が晴れても、音は残る。", "After the mist clears, the sound remains."),
    "named.armor.024": ("月絹の巡り", "Moonsilk Cycle",
                        "絹が月光を巻き戻す。", "The silk winds the moonlight back."),
    "named.armor.048": ("共鳴の切札", "Resonant Trump",
                        "袖の中に、一枚だけ残してある。", "One card is kept back in the sleeve."),
    "named.armor.059": ("根鎖の終章", "Rootbound Finale",
                        "根が絡めば、幕は早く下りる。", "When the roots entangle, the curtain falls sooner."),
    "named.armor.060": ("星読みの余韻", "Stargazer's Echo",
                        "読んだ星の分だけ、間が詰まる。", "Each star read closes the gap a little."),
    # ---- Charm / Plain ----
    "named.charm.001": ("牙の連鳴", "Fang Chaincall",
                        "牙が鳴るたび、拍子が上がる。", "Each click of fang lifts the rhythm."),
    "named.charm.002": ("狩印の雷鎖", "Hunter's Lightning Seal",
                        "印を押した獲物へ、雷が渡る。", "Lightning crosses to whatever the seal marks."),
    "named.charm.025": ("鉄印の先手", "Iron First Seal",
                        "押した瞬間に、勝負は半ば決まる。", "The instant it presses, half the contest is won."),
    "named.charm.003": ("遠当ての静水", "Longshot Stillwater",
                        "遠くからでも、水は静かに効く。", "Even from afar, the water works in stillness."),
    "named.charm.026": ("四元の巡り輪", "Ring of Four Colors",
                        "火霜光影が揃う瞬間、輪が光る。", "When fire, frost, light and shadow align, the ring glows."),
    "named.charm.049": ("戦角の凱歌", "War-Horn Victory Note",
                        "角笛が鳴り、砕けた後に息が戻る。", "The horn sounds; after the shattering, breath returns."),
    # ---- Charm / Frost ----
    "named.charm.004": ("霜息の垂飾", "Frostbreath Pendant",
                        "吐く息ごとに、冷気が深まる。", "With every breath, the cold deepens."),
    "named.charm.027": ("氷晶の湯気", "Glacier Steam",
                        "氷と火が触れ、白い幕を張る。", "Ice meets fire and hangs a white curtain."),
    "named.charm.005": ("月鈴の霜晶", "Moon Bell Frostcrystal",
                        "鈴の音に、冷たい光が応える。", "Cold light answers the bell's ring."),
    "named.charm.028": ("雪華の枷", "Snowbloom Shackles",
                        "咲いた花の数だけ、足が重い。", "For every blossom, a foot grows heavy."),
    "named.charm.029": ("潮衛の脆氷", "Tidewarden Brittle Ice",
                        "守った潮が、敵の装甲を弱らせる。", "The guarded tide softens the foe's armor."),
    "named.charm.050": ("冬樫の白息", "Winter Oak White Breath",
                        "樫の葉が落ちる頃、霧が味方する。", "When oak leaves fall, the mist takes your side."),
    # ---- Charm / Flame ----
    "named.charm.006": ("焔の黒種", "Blaze Black Seed",
                        "燃え残りが、影と手を結ぶ。", "The charred remnant joins hands with shadow."),
    "named.charm.007": ("火芯の四連", "Cindercore Quadruple",
                        "蓋を開けるたび、火が覚える。", "Each time it opens, the fire remembers."),
    "named.charm.030": ("彗星の火種", "Comet Spark",
                        "尾を引けば、火が残る。", "Draw the tail, and fire remains."),
    "named.charm.008": ("残り火の飛び火", "Ember Locket Wildfire",
                        "胸の火が、隣へ移る。", "The fire at the chest leaps to the next."),
    "named.charm.031": ("紅玉の追風", "Garnet Tailwind",
                        "深紅の輝きが、背を押す。", "The deep red gleam pushes at your back."),
    "named.charm.051": ("戦鼓の白煙", "War-Drum White Smoke",
                        "打ち鳴らせば、煙と種が散る。", "Every beat scatters smoke and seeds."),
    # ---- Charm / Light ----
    "named.charm.009": ("暁糸の星傘", "Dawnsilk Starshade",
                        "編んだ糸が、星の傘になる。", "The woven thread becomes a star's parasol."),
    "named.charm.032": ("鋭眼の返し", "Keen-Eye Kindness",
                        "見逃した者に、情けが戻る。", "Kindness returns for those spared."),
    "named.charm.010": ("共鳴の継ぎ糸", "Resonant Lifeline",
                        "波長が合えば、命が繋がる。", "Where wavelengths meet, life is joined."),
    "named.charm.033": ("星屑の灯", "Stardust Lamp",
                        "集めた星屑が、定めに灯る。", "Gathered stardust lights a steady lamp."),
    "named.charm.034": ("陽の重ね輝き", "Sunlight Stacked Glow",
                        "陽が当たるたび、光が重なる。", "Each touch of sun stacks another glow."),
    "named.charm.052": ("陽輪の二重奏", "Sunspoke Duet",
                        "傘と情けを、同時に差し出す。", "It offers a parasol and a kindness at once."),
    # ---- Charm / Dark ----
    "named.charm.011": ("宵珠の影重ね", "Duskbead Shadow Stack",
                        "一珠ごとに、影が濃くなる。", "Each bead darkens the shadow a shade."),
    "named.charm.012": ("墨硯の吸魂", "Inkstone Siphon",
                        "すった墨が、誰かの最後を知る。", "The ground ink knows someone's last moment."),
    "named.charm.035": ("鴉羽の蝕", "Raven-Quill Eclipse",
                        "羽で書けば、昼と夜が交わる。", "Write with the quill, and day crosses night."),
    "named.charm.013": ("仮面の跡目", "Mask's Shadow Heir",
                        "被った者から、次へ影が渡る。", "The shadow passes from wearer to wearer."),
    "named.charm.036": ("影環の断罪", "Shadow Ring Verdict",
                        "怯えた影から、順に刈られる。", "The frightened shadows are reaped in turn."),
    "named.charm.053": ("嵐鈴の夜噛み", "Storm Bell Nightbite",
                        "鐘が鳴る夜は、影が魂を舐める。", "On nights the bell rings, shadow licks at souls."),
    # ---- Charm / Guard ----
    "named.charm.014": ("壁印の備え", "Bulwark Readiness",
                        "印は盾ではなく、心構え。", "The seal is not a shield but a stance."),
    "named.charm.037": ("鎖首の輪", "Chain Necklace Ring",
                        "囲まれて、鎖は固く鳴る。", "Surrounded, the chain rings hard."),
    "named.charm.015": ("守護の棘", "Guardian Spines",
                        "護る心に、棘が生える。", "A guarding heart grows spines."),
    "named.charm.038": ("鉄羽の守護霊", "Iron-Feather Guardian",
                        "大波を受け、羽が盾になる。", "It takes the great wave; the feather turns shield."),
    "named.charm.039": ("脈打つ不動", "Pulsing Immovability",
                        "鼓動は速くても、地に根が張る。", "The beat may race, yet roots hold the ground."),
    "named.charm.054": ("石心の備え風", "Stone Heart Readywind",
                        "石は構え、風だけが動く。", "The stone holds its guard; only the wind moves."),
    # ---- Charm / Gale ----
    "named.charm.016": ("鉄結びの旋風", "Ironknot Whirl",
                        "結び目がほどける時、渦が起きる。", "When the knot slips loose, a whirl begins."),
    "named.charm.017": ("追撃の疾駆", "Pursuit Dash",
                        "かわすことが、追うことになる。", "To dodge is already to pursue."),
    "named.charm.040": ("雷雲の乱気", "Stormcloud Turbulence",
                        "雲が低いほど、中は荒れる。", "The lower the cloud, the wilder inside."),
    "named.charm.018": ("追風の渡り路", "Tailwind Crossing",
                        "風に乗れば、帰りも知っている。", "Ride the wind, and you know the way home."),
    "named.charm.041": ("風鈴の助走", "Wind-Chime Run-Up",
                        "鳴った分だけ、距離が縮む。", "Every chime shortens the distance."),
    "named.charm.055": ("西風の二つ輪", "Zephyr Twin Rings",
                        "追風と旋風を、一本の指で。", "Tailwind and whirlwind on a single finger."),
    # ---- Charm / Mend ----
    "named.charm.019": ("光環の再燃", "Halo Rekindling",
                        "輪が細っても、芯は燃え直す。", "Though the ring thins, the core relights."),
    "named.charm.042": ("囲炉裏の薬環", "Hearthside Remedy Ring",
                        "火を囲んだ手は、薬を覚える。", "Hands that circle a fire remember medicine."),
    "named.charm.020": ("炉端の吸命", "Hearthstone Leech",
                        "温もりの分を、どこかから貰う。", "Its warmth is borrowed from somewhere."),
    "named.charm.043": ("蓮印の溢れ", "Lotus Overflow",
                        "満ちた水は、葉を盾に変える。", "Water that overfills turns leaves to shields."),
    "named.charm.044": ("癒し匣の砕音", "Mender's Crack",
                        "開けた匣の中で、何かが弾ける。", "Inside the opened locket, something bursts."),
    "named.charm.056": ("樫守の二の息", "Oak Second Breath",
                        "倒れても、根から息が戻る。", "Even felled, breath returns through the roots."),
    # ---- Charm / Summon ----
    "named.charm.021": ("獣語の分け合い", "Beasttongue Share",
                        "言葉が通じれば、盾も分け合う。", "Where words reach, shields are shared."),
    "named.charm.045": ("岩塊の共演", "Boulder Co-Star",
                        "岩と並べば、道は広がる。", "Stand beside the boulder and the road widens."),
    "named.charm.022": ("深根の見守り", "Deeproot Watch",
                        "根の張った先まで、目は届く。", "Sight reaches wherever roots have spread."),
    "named.charm.046": ("牙飾りの継ぎ", "Fang Relay",
                        "一本の牙が、次の牙へ繋ぐ。", "One fang passes the charge to the next."),
    "named.charm.057": ("海沫の分け輪", "Seafoam Shared Ring",
                        "泡の輪は、嵐を半分にする。", "A ring of foam halves the storm."),
    "named.charm.058": ("爪飾りの二役", "Talon's Two Roles",
                        "並び立ち、そして見守る。", "It stands with you, and it watches."),
    # ---- Charm / Memory ----
    "named.charm.023": ("ぜんまいの切札", "Clockwork Trump",
                        "捲くたびに、一枚増える。", "Every wind adds a card."),
    "named.charm.047": ("夢鏡の循環", "Dreaming-Lens Circuit",
                        "覗くたび、光が環を描く。", "Each look makes the light loop once more."),
    "named.charm.024": ("蝕環の欠片", "Eclipse Shard",
                        "欠けた分だけ、恵みが積もる。", "For every missing piece, a boon accrues."),
    "named.charm.048": ("風羽の終曲", "Windfeather Finale",
                        "羽が揃えば、幕が早まる。", "When the feathers gather, the curtain hurries."),
    "named.charm.059": ("氷心の切り札", "Frozen-Heart Trump",
                        "凍えた胸に、最後の一枚がある。", "In the frozen chest waits the final card."),
    "named.charm.060": ("古時計の循環", "Old-Clock Circuit",
                        "針が一周すれば、恵みが落ちる。", "Each full turn of the hands drops a boon."),
    # ---- Head / Plain ----
    "named.head.001": ("灰かぶりの拍子", "Ashen Rhythm",
                       "灰を払う手が、次の拍子を刻む。", "The hand that brushes ash beats the next bar."),
    "named.head.002": ("狂面の雷", "Berserker's Lightning",
                       "叫びが、敵から敵へ渡る。", "The shout passes from foe to foe."),
    "named.head.025": ("狩巾の砕け道", "Hunter's Crack",
                       "狙った獲物の後ろで、音が弾ける。", "Behind the marked prey, something bursts."),
    "named.head.003": ("鎖帽の先手", "Coif's First Move",
                       "目立ちたくない者ほど、先に動く。", "Those who shun notice move first."),
    "named.head.026": ("遠帽の止水", "Long-Cap Stillwater",
                       "息を止めた分、狙いは静か。", "For every held breath, the aim grows quiet."),
    "named.head.049": ("賢帽の二つの拍", "Sage's Two Beats",
                       "歩む拍子と、幕引きの拍子。", "One beat for walking, one for the curtain."),
    # ---- Head / Frost ----
    "named.head.004": ("霜兜の晶", "Frosthelm Crystal",
                       "兜の霜が、光と結ぶ。", "The helm's frost binds with light."),
    "named.head.027": ("氷壁の白霜", "Icewall Rime",
                       "壁の霜が、触れた敵を凍らす。", "The wall's rime freezes whoever touches it."),
    "named.head.005": ("月影の足留め", "Moonshadow Snare",
                       "影が伸びる先で、足が止まる。", "Where the shadow stretches, feet stop."),
    "named.head.028": ("月紗の白霧", "Moonlace White Mist",
                       "冷たい紗が、温かい息と混ざる。", "Cold lace mixes with warm breath."),
    "named.head.029": ("霜華の砕氷", "Rimebloom Brittle Ice",
                       "咲いた霜の花は、踏めば割れる。", "The rime blossoms shatter underfoot."),
    "named.head.050": ("沈鐘の晶灯", "Sunken-Bell Crystal Lamp",
                       "沈んだ鐘が、光の殻を鳴らす。", "The sunken bell rings a shell of light."),
    # ---- Head / Flame ----
    "named.head.006": ("大角の四連火", "Antler Quadruple Fire",
                       "角が四つ数えれば、火が噴く。", "Count four on the antlers and fire spouts."),
    "named.head.007": ("紅蓮の火種", "Crimson Spark",
                       "蓮の弁から、火の粉が落ちる。", "Sparks drop from the lotus petals."),
    "named.head.030": ("火焔花の飛び火", "Emberbloom Wildfire",
                       "花の火は、隣の枝へ渡る。", "The blossom's fire crosses to the next branch."),
    "named.head.008": ("鉄兜の黒種", "Iron-Helm Black Seed",
                       "鉄の匂いに、焼けた種が混じる。", "A burnt seed taints the iron smell."),
    "named.head.031": ("熔岩の灯", "Magma Lamp",
                       "冷えかけた岩が、定めに灯る。", "The cooling rock keeps a steady flame."),
    "named.head.051": ("日輪面の再燃", "Sunwheel Rekindling",
                       "日が沈んでも、面は火を覚えている。", "The mask remembers fire past sundown."),
    # ---- Head / Light ----
    "named.head.009": ("残り火冠の輝き", "Ember-Crown Glow",
                       "火の残りが、光の種になる。", "What fire leaves behind seeds the light."),
    "named.head.032": ("真昼の星傘", "Noonlight Starshade",
                       "真昼でも、星は傘を差す。", "Even at noon, a star holds its parasol."),
    "named.head.010": ("誓環の継ぎ糸", "Oathring Lifeline",
                       "輪は切れても、糸が残る。", "Though the ring breaks, a thread remains."),
    "named.head.033": ("光輪の返し", "Halo's Kind Return",
                       "注いだ光が、誰かの情けで戻る。", "Poured light returns as someone's kindness."),
    "named.head.034": ("星冠の灯守", "Stardiadem Warden",
                       "星が一つ消えても、灯は残る。", "Though one star goes out, the lamp remains."),
    "named.head.052": ("護符帯の二光", "Ward Band Twin Lights",
                       "灯る盾と、重なる光。", "A lamp of a shield, and a stacking glow."),
    # ---- Head / Dark ----
    "named.head.011": ("蝕面の影重ね", "Eclipse-Mask Shadow Stack",
                       "面の影が、一つずつ深まる。", "The mask's shadow deepens by ones."),
    "named.head.012": ("片目の蝕", "Eyepatch Eclipse",
                       "隠した目で、昼と夜を見る。", "The hidden eye sees day and night as one."),
    "named.head.035": ("鳥嘴の断罪", "Beaked Verdict",
                       "嘴が向く先は、もう終わり。", "Where the beak points, it is already over."),
    "named.head.013": ("鴉面の吸魂", "Raven-Mask Siphon",
                       "鴉が啄むのは、最後の息。", "What the raven pecks is the final breath."),
    "named.head.036": ("爪冠の跡目", "Talon-Crown Heirloom",
                       "冠を受けた頭に、影が巣を作る。", "On the crowned head, shadow builds its nest."),
    "named.head.053": ("兀鷲の蝕日", "Vulture Eclipse",
                       "羽の陰で、昼が蝕まれる。", "In the wings' shade, noon is eclipsed."),
    # ---- Head / Guard ----
    "named.head.014": ("岩兜の不動", "Boulder Immovability",
                       "岩の上では、風も考え直す。", "Even the wind rethinks on the boulder."),
    "named.head.037": ("城塞の輪", "Fortress Ring",
                       "寄ってきた数だけ、固くなる。", "It hardens by the number that gather."),
    "named.head.015": ("騎盔の棘", "Knight's Spines",
                       "兜の縁に、返しの棘。", "Along the helm's rim, returning spines."),
    "named.head.038": ("歩哨の守護霊", "Sentry Guardian",
                       "一撃を見極める目に、霊が宿る。", "A spirit dwells in the eye that reads the blow."),
    "named.head.039": ("茨冠の備え", "Thorn Readiness",
                       "茨は、触れる前に構える。", "The briars brace before you touch them."),
    "named.head.054": ("番人の血醒め", "Warden's Bloodthirst",
                       "構えは解けず、渇きだけが増す。", "The stance never breaks; only the thirst grows."),
    # ---- Head / Gale ----
    "named.head.016": ("疾風巾の追風", "Gale-Hood Tailwind",
                       "脱いだ後も、風は追いかける。", "Even off your head, the wind keeps chase."),
    "named.head.017": ("鷹目の旋風", "Hawkeye Whirl",
                       "見切りつけた瞬間、渦が起きる。", "The instant the eye commits, a whirl rises."),
    "named.head.040": ("霧帳の乱気", "Mist-Veil Turbulence",
                       "霧の密度が、荒さを決める。", "The mist's thickness decides the temper."),
    "named.head.018": ("斥候鏡の疾駆", "Scout's Dash",
                       "見えた抜け道を、体が先に知る。", "The body knows the gap before the mind."),
    "named.head.041": ("雷帷の渡り", "Thunderveil Crossing",
                       "雷の簾をくぐって、刃が冴える。", "Through the curtain of thunder, the edge sharpens."),
    "named.head.055": ("囁きの追い風", "Whisper Tailwind",
                       "耳元の囁きは、影と風の声。", "The whisper at your ear is shadow and wind."),
    # ---- Head / Mend ----
    "named.head.019": ("夢帷の吸命", "Dreamveil Leech",
                       "夢の中で、少しずつ元気を貰う。", "In dreams it sips a little strength."),
    "named.head.042": ("癒手の溢れ", "Healer's Overflow",
                       "癒し過ぎた分が、盾に変わる。", "What heals beyond need turns to shield."),
    "named.head.020": ("慈手の薬包", "Kindly Remedy",
                       "額の温かさが、薬になる。", "The warmth at the brow becomes medicine."),
    "named.head.043": ("瞑想の渇き", "Meditative Thirst",
                       "静かに座るほど、血が騒ぐ。", "The quieter the sitting, the louder the blood."),
    "named.head.044": ("苔冠の終曲", "Moss-Crown Finale",
                       "苔が這う速さで、幕が近づく。", "At the moss's creeping pace, the curtain nears."),
    "named.head.056": ("光条の灯守", "Sunbeam Warden",
                       "光の筋が、切れた命を繋ぐ。", "The shaft of light rejoins the severed life."),
    # ---- Head / Summon ----
    "named.head.021": ("呼角の共演", "Beastcaller Co-Star",
                       "角が鳴れば、誰かが並ぶ。", "When the antlers sound, someone falls in."),
    "named.head.045": ("珊瑚の群れ糧", "Coral Pack Feast",
                       "枝の数だけ、口がある。", "For every branch, a mouth."),
    "named.head.022": ("双角の分け合い", "Horned Share",
                       "二本の角は、盾を二つに分ける。", "Two horns split one shield in two."),
    "named.head.046": ("若葉の見守り", "Leaf-Wreath Watch",
                       "若葉の揺れは、合図になる。", "Each rustle of new leaves is a signal."),
    "named.head.057": ("虚兜の二役", "Hollow Two Roles",
                       "空っぽの兜が、群れと並ぶ。", "The empty helm lines up with the pack."),
    "named.head.058": ("狼皮の星傘", "Wolf-Pelt Starshade",
                       "毛皮の上に、星が傘を差す。", "Over the pelt, a star holds its parasol."),
    # ---- Head / Memory ----
    "named.head.023": ("彗星髪の巡り", "Comet Cycle",
                       "尾を引いた星が、元の場所へ。", "The tailed star returns whence it came."),
    "named.head.047": ("夢冠の切札", "Dreamer's Trump",
                       "額の飾りに、一枚隠してある。", "A single card hides in the ornament."),
    "named.head.024": ("灯笠の環", "Lantern-Hat Loop",
                       "笠の灯が、小さな環を回す。", "The hat's lamp turns a little loop."),
    "named.head.048": ("柱冠の余韻", "Pillar-Crown Echo",
                       "石に響いた音が、速さを残す。", "The note off the stone leaves speed behind."),
    "named.head.059": ("無星帳の二巡", "Starless Double Round",
                       "星のない空も、巡りは続ける。", "Even a starless sky keeps its rounds."),
    "named.head.060": ("潮冠の星巡り", "Tidal Star Cycle",
                       "潮が引けば、星屑が戻ってくる。", "When the tide recedes, stardust comes back."),
    # ---- Hands / Plain ----
    "named.hands.001": ("射手の砕け腕", "Archer's Crack Bracers",
                        "放った矢の先で、何かが弾ける。", "Where the arrow lands, something bursts."),
    "named.hands.002": ("弓張りの先手", "Bowmaster's First Move",
                        "弦を引く手が、勝負を先取る。", "The drawing hand takes the lead."),
    "named.hands.025": ("決闘手の止水", "Duelist's Stillwater",
                        "受けを決めた手が、水面を作る。", "A hand that parries well makes still water."),
    "named.hands.003": ("鉄篭手の盾打", "Iron Shieldbash",
                        "籠手そのものが、盾になる。", "The gauntlet itself becomes the shield."),
    "named.hands.026": ("革手の一点", "Leather Focus",
                        "狙う場所を、指が覚えている。", "The fingers remember where to aim."),
    "named.hands.049": ("遠腕の二の拍", "Reach Bracers' Second Beat",
                        "届く先で、拍子が繋がる。", "Where the arm reaches, the beat goes on."),
    # ---- Hands / Frost ----
    "named.hands.004": ("霜手袋の白息", "Frost-Mitt Breath",
                        "握ったものから、熱を奪う。", "It steals heat from whatever it holds."),
    "named.hands.027": ("凍拳の足止め", "Frostbite Snare",
                        "凍えた拳は、逃げ道も凍らす。", "The frozen fist freezes the escape too."),
    "named.hands.005": ("光巻きの湯気", "Radiant Steam Wraps",
                        "光と冷気が、白い幕を作る。", "Light and cold weave a white curtain."),
    "named.hands.028": ("礁篭手の脆氷", "Reef Brittle Ice",
                        "掴んだ鎧ごと、手の中で割る。", "It cracks the grasped armor in its grip."),
    "named.hands.029": ("雪解の晶", "Snowmelt Crystal",
                        "解けかけの手袋が、光を纏う。", "The half-thawed mitt wears light."),
    "named.hands.050": ("活力握りの霜", "Vigorgrip Frost",
                        "強い握りほど、冷たさを増す。", "The stronger the grip, the deeper the cold."),
    # ---- Hands / Flame ----
    "named.hands.006": ("火織りの四連", "Blazeknit Quadruple",
                        "編み目が四つ数え、火を噴く。", "Four stitches counted, and fire spouts."),
    "named.hands.007": ("鎖拳の火の粉", "Chain-Fist Sparks",
                        "巻いた鎖から、火の粉が散る。", "Sparks fly off the wrapped chain."),
    "named.hands.030": ("残火手甲の飛び火", "Ember Gauntlet Wildfire",
                        "触れた火が、隣へ渡る。", "Fire touched passes to the next."),
    "named.hands.008": ("灯指抜きの種", "Lantern Black Seed",
                        "灯の下で、黒い種が熟す。", "Beneath the lamp, a black seed ripens."),
    "named.hands.031": ("裂握の拍子", "Ripgrip Rhythm",
                        "引き裂くたび、手が軽くなる。", "Each tear makes the hands lighter."),
    "named.hands.051": ("陽炎握りの種火", "Sunfire Seedgrip",
                        "握れば熱く、離せば火の粉。", "Hot in the grip; sparks when released."),
    # ---- Hands / Light ----
    "named.hands.009": ("黄昏縫いの返し", "Duskstitch Kind Return",
                        "縫い込んだ情けが、ほどけて戻る。", "The kindness stitched in unpicks itself to return."),
    "named.hands.032": ("誓籠手の灯", "Oath-Gauntlet Lamp",
                        "誓った手の甲に、灯がともる。", "On the sworn hand, a flame is lit."),
    "named.hands.010": ("誓掌の重ね光", "Oathpalm Stacked Glow",
                        "手を重ねるたび、光が増す。", "Each palm laid adds another glow."),
    "named.hands.033": ("的確の星傘", "Precise Starshade",
                        "正確な指先に、星が傘を差す。", "To precise fingertips, a star lends shade."),
    "named.hands.034": ("陽籠手の継ぎ糸", "Sunlit-Gauntlet Lifeline",
                        "日を当てた手が、命を縫う。", "Sun-warmed hands stitch life together."),
    "named.hands.052": ("潮手の二重奏", "Tide-Glove Duet",
                        "返す波と、重なる拍子。", "The returning wave and the stacking beat."),
    # ---- Hands / Dark ----
    "named.hands.011": ("決闘腕の断罪", "Duelist's Verdict",
                        "挑んだ相手の息は、短い。", "The challenged one's breath runs short."),
    "named.hands.012": ("炎握りの跡目", "Flame-Grip Heirloom",
                        "火を受け継いだ手に、影が張り付く。", "Shadow clings to hands that inherited fire."),
    "named.hands.035": ("夜掌の影", "Nightpalm Shadow",
                        "開いた掌から、夜が零れる。", "Night spills from the opened palm."),
    "named.hands.013": ("裂拳の吸魂", "Riven-Fist Siphon",
                        "裂け目から、最後の息を吸う。", "It draws the last breath through the crack."),
    "named.hands.036": ("影縫いの蝕", "Shadowstitch Eclipse",
                        "縫い合わせた影と光が、蝕になる。", "Shadow and light sewn together eclipse."),
    "named.hands.053": ("盗手の刈り入れ", "Thief's Harvest",
                        "盗んだ影ごと、命を刈る。", "It reaps the life along with the stolen shadow."),
    # ---- Hands / Guard ----
    "named.hands.014": ("壁巻きの輪", "Bulwark Wraps Ring",
                        "巻いた手が、輪の内側を作る。", "The wrapped hands make the inside of a ring."),
    "named.hands.037": ("重掌の返し棘", "Heavypalm Spines",
                        "重い手の平ほど、痛い。", "The heavier the palm, the worse it stings."),
    "named.hands.015": ("鉄脈の守護霊", "Ironvein Guardian",
                        "鉱脈の記憶が、大波を防ぐ。", "The vein's memory turns the great wave."),
    "named.hands.038": ("岩拳の不動", "Stone-Fist Immovability",
                        "握りは岩、構えは山。", "A grip of rock, a stance of mountain."),
    "named.hands.039": ("茨巻きの備え", "Thorn-Wrap Readiness",
                        "巻き直す暇もなく、構える。", "No time to rewrap; already braced."),
    "named.hands.054": ("番人握りの輪", "Warden's Ring Grip",
                        "囲まれても、握りは血を知る。", "Though surrounded, the grip knows blood."),
    # ---- Hands / Gale ----
    "named.hands.016": ("鈴腕の乱気", "Chime Turbulence",
                        "鈴の密度が、荒さになる。", "The density of chimes sets the temper."),
    "named.hands.017": ("行者巻きの渡り", "Ascetic's Crossing",
                        "細い巻きでも、道は渡れる。", "Even thin wraps can cross the road."),
    "named.hands.040": ("早業の助走", "Quickfinger Run-Up",
                        "指が走れば、刃も伸びる。", "When fingers run, the blade extends."),
    "named.hands.018": ("投げ腕の追風", "Thrower's Tailwind",
                        "投げた物の後を、風が追う。", "The wind chases whatever you throw."),
    "named.hands.041": ("嵐拳の旋風", "Storm-Knuckle Whirl",
                        "かわせば、拳が渦を描く。", "Dodge, and the fist draws a whirl."),
    "named.hands.055": ("風掌の二段", "Swift-Palm Second Stage",
                        "混んで、渡って、なお速い。", "Crowded, crossing, and still faster."),
    # ---- Hands / Mend ----
    "named.hands.019": ("錬金手の吸命", "Alchemist's Leech",
                        "混ぜるたびに、少し元気になる。", "Every stirring returns a little vigor."),
    "named.hands.042": ("樹皮拳の薬", "Bark-Knuckle Remedy",
                        "樹の薬箱を、拳で開ける。", "It opens the tree's medicine chest with a fist."),
    "named.hands.020": ("癒手の再びの息", "Healer's Second Breath",
                        "手を貸した側こそ、息を戻す。", "The lending hand is the one that breathes again."),
    "named.hands.043": ("氷腕の溢れ", "Ice-Bracer Overflow",
                        "冷やし過ぎた分が、盾になる。", "What is over-chilled becomes a shield."),
    "named.hands.044": ("癒掌の静雷", "Mender's Quiet Lightning",
                        "静かに走る光が、痛みを繋ぐ。", "Quietly running light links the pains."),
    "named.hands.056": ("数珠の二つの徳", "Prayer-Bead Twin Virtues",
                        "数えた珠が、命と情けを返す。", "The counted beads give back life and kindness."),
    # ---- Hands / Summon ----
    "named.hands.021": ("骨拳の継ぎ", "Bone-Knuckle Relay",
                        "骨が骨へ、音を渡す。", "Bone passes the note to bone."),
    "named.hands.045": ("獣爪の分け合い", "Beastclaw Share",
                        "爪を振れば、盾が半分こになる。", "A sweep of the claw halves the shield."),
    "named.hands.022": ("筋帯の共演", "Sinew Co-Star",
                        "弓の腱が、仲間と鳴る。", "The bow's sinew sounds with a partner."),
    "named.hands.046": ("喚び環の見守り", "Summoner's Watch",
                        "指輪の数だけ、目がある。", "For every band, an eye."),
    "named.hands.057": ("潮呼びの継ぎ討ち", "Tidecaller Relay Hunt",
                        "渡した波が、終わりを断つ。", "The handed-over wave cuts the ending short."),
    "named.hands.058": ("毒牙の二役", "Viper-Fang Two Roles",
                        "毒を分け、役を継ぐ。", "It shares its venom and takes its turn."),
    # ---- Hands / Memory ----
    "named.hands.023": ("火糸の終曲", "Cinderthread Finale",
                        "焼けた糸が、幕を引く音をする。", "The burnt thread makes the curtain-pull sound."),
    "named.hands.047": ("写本手の余韻", "Manuscript Echo",
                        "写した文字が、鋭く響く。", "The copied characters ring sharp."),
    "named.hands.024": ("根握りの星巡り", "Rootgrip Star Cycle",
                        "握った根から、星屑が巡る。", "Stardust circles through the gripped root."),
    "named.hands.048": ("呪文手の切札", "Spellweave Trump",
                        "編んだ呪文の端に、一枚。", "At the woven spell's end, one card."),
    "named.hands.059": ("星環の速い幕", "Star-Ring Swift Curtain",
                        "会心のたび、幕が近づく。", "Every critical brings the curtain nearer."),
    "named.hands.060": ("虚爪の巡り響き", "Hollow-Claw Echo Cycle",
                        "空っぽの爪に、音がこだまする。", "In the hollow claw, the sound echoes on."),
    # ---- Feet / Plain ----
    "named.feet.001": ("灰踏みの静雷", "Ashwalker Quiet Lightning",
                       "灰の上を、光が這う。", "Light crawls across the ash."),
    "named.feet.002": ("追跡の砕け音", "Tracker's Crack",
                       "踏み抜いた先で、音が弾ける。", "Where the boot breaks through, sound bursts."),
    "named.feet.025": ("鉄下駄の先手", "Iron-Clog First Move",
                       "重い一歩が、勝負を決める。", "One heavy step settles the contest."),
    "named.feet.003": ("棘靴の止水", "Spiked Stillwater",
                       "踏み込んだ足元が、水面になる。", "Where the boot plants, still water forms."),
    "named.feet.026": ("岩靴の見切り", "Stone-Boot Read",
                       "動かない足が、次を読む。", "Feet that do not move read what comes."),
    "named.feet.049": ("旅靴の二の拍", "Traveler's Second Beat",
                       "歩いた分だけ、息も余る。", "For every mile walked, breath left over."),
    # ---- Feet / Frost ----
    "named.feet.004": ("暁霧の白霜", "Dawnmist Rime",
                       "霧の靴跡が、霜になる。", "The misty footprints turn to rime."),
    "named.feet.027": ("氷上の足留め", "Ice-Skimmer Snare",
                       "滑った跡で、追う足が止まる。", "Along the glide, pursuing feet slow."),
    "named.feet.005": ("霜踏みの湯気", "Froststride Steam",
                       "冷たい歩みの後ろに、白い幕。", "Behind the cold stride, a white curtain."),
    "named.feet.028": ("鉄波の晶", "Iron-Wave Crystal",
                       "寄せる鉄の波が、光を鎧う。", "The advancing iron wave arms itself with light."),
    "named.feet.029": ("根靴の脆氷", "Rooted Brittle Ice",
                       "張った根の周りで、氷が割れる。", "Around the spread roots, ice cracks."),
    "named.feet.050": ("冬毛の白い舞", "Winterhide White Dance",
                       "雪の上を、舞いながら渡る。", "Across the snow it goes, dancing."),
    # ---- Feet / Flame ----
    "named.feet.006": ("暁足の四連火", "Dawnstep Quadruple",
                       "四歩目ごとに、火が灯る。", "Every fourth step kindles a flame."),
    "named.feet.007": ("燠火履きの粉", "Embered-Slipper Sparks",
                       "歩くたび、火の粉が残る。", "Each step leaves sparks behind."),
    "named.feet.030": ("火駆けの飛び火", "Emberdash Wildfire",
                       "駆けた跡で、火が増える。", "Along the dash, the fires multiply."),
    "named.feet.008": ("突風履きの種", "Mistral Black Seed",
                       "風が運んだ火の種を、履いて歩く。", "It walks wearing fire's seed the wind brought."),
    "named.feet.031": ("影履きの拍子", "Shadow-Slipper Rhythm",
                       "影を踏むたび、足が軽い。", "Each shadow stepped on lightens the feet."),
    "named.feet.051": ("潮履きの焔壁", "Tide-Sandal Fire Wall",
                       "潮風が火を運び、輪を固める。", "The sea wind bears fire and closes the ring."),
    # ---- Feet / Light ----
    "named.feet.009": ("彗星履きの灯", "Comet-Stride Lamp",
                       "流れた跡に、灯が残る。", "Along the comet's wake, a lamp remains."),
    "named.feet.032": ("騎靴の重ね光", "Knight's Stacked Glow",
                       "鉄の靴が、光を弾き返す。", "The iron shoe rebounds the light."),
    "named.feet.010": ("賢履きの星傘", "Sage's Starshade",
                       "歩みの果てに、星が差しかかる。", "At the walk's end, a star shades you."),
    "named.feet.033": ("陽だまりの返し", "Sunwarm Kind Return",
                       "温めた分が、誰かの情けで戻る。", "The warmth given returns as kindness."),
    "named.feet.034": ("陽蹴りの継ぎ糸", "Sunspur Lifeline",
                       "蹴った光が、命に繋がる。", "The kicked light links to a life."),
    "named.feet.052": ("護り草鞋の棘", "Wardstep Thorns",
                       "結んだ縄が、灯と棘になる。", "The knotted rope becomes lamp and thorn."),
    # ---- Feet / Dark ----
    "named.feet.011": ("必中の吸魂", "Deadeye Siphon",
                       "外さない脚が、息を拾う。", "Feet that never miss gather breath."),
    "named.feet.012": ("宵踏みの断罪", "Duskstep Verdict",
                       "黄昏の中で、終わりを踏む。", "In the dusk it treads on endings."),
    "named.feet.035": ("残り火の影踏み", "Ember-Tread Shadow",
                       "火の周りの影を、歩いて濃くする。", "It walks the shadows thicker round the fire."),
    "named.feet.013": ("霧履きの蝕", "Mist-Shoe Eclipse",
                       "霧の中で、昼と夜が入れ替わる。", "Inside the mist, day and night trade places."),
    "named.feet.036": ("夜帳の跡目", "Nightveil Heirloom",
                       "夜を継いだ足に、影が従う。", "Shadow follows feet that inherited night."),
    "named.feet.053": ("忍び足の収穫", "Stalker's Harvest",
                       "音もなく歩き、息を集める。", "It walks soundless and gathers breath."),
    # ---- Feet / Guard ----
    "named.feet.014": ("沈錘の輪", "Ballast Ring",
                       "重い靴ほど、輪は固い。", "The heavier the boot, the harder the ring."),
    "named.feet.037": ("城塞鉄鞋の棘", "Bastion Spines",
                       "城門の前で、棘が返る。", "Before the gate, the spines give back."),
    "named.feet.015": ("鎖脛の守護霊", "Chain-Greave Guardian",
                       "鎖の重みが、大波を止める。", "The chain's weight halts the great wave."),
    "named.feet.038": ("鉄脛の不動", "Iron-Greave Immovability",
                       "踏ん張った鉄は、動かない。", "Iron set against the ground does not move."),
    "named.feet.039": ("土塁脛の備え", "Rampart Readiness",
                       "土の盛りは、いつも構えている。", "The earthwork is always braced."),
    "named.feet.054": ("亀甲の二重殻", "Tortoise Twin Shell",
                       "二枚の甲羅が、輪と霊を担ぐ。", "Two shells carry the ring and the spirit."),
    # ---- Feet / Gale ----
    "named.feet.016": ("電光足の追風", "Blitz Tailwind",
                       "閃いた後を、風が追う。", "After the flash, the wind gives chase."),
    "named.feet.017": ("舞鞋の旋風", "Dancer's Whirl",
                       "回れば、靴先が渦になる。", "Every turn, the toe draws a whirl."),
    "named.feet.040": ("烈風脛の疾駆", "Gale-Greave Dash",
                       "かわせば、風が足に乗る。", "Dodge, and the wind mounts your feet."),
    "named.feet.018": ("追撃脛の助走", "Hunter's Run-Up",
                       "距離こそ、爪の長さ。", "Distance is the claw's true length."),
    "named.feet.041": ("嵐駆けの乱気", "Stormrunner Turbulence",
                       "嵐の中は、歩くほど速い。", "Inside the storm, the more feet, the faster."),
    "named.feet.055": ("風履きの二重渦", "Wind-Sandal Twin Whirl",
                       "追風を纏い、渦を残す。", "Wrapped in tailwind, it leaves a whirl."),
    # ---- Feet / Mend ----
    "named.feet.019": ("深根靴の溢れ", "Deeproot Overflow",
                       "根が吸い過ぎて、盾が実る。", "Roots that drink too much bear shields."),
    "named.feet.042": ("火華履きの吸命", "Firebloom Leech",
                       "咲いた火から、少しずつ貰う。", "It sips a little from each blossom of fire."),
    "named.feet.020": ("守靴の灯守", "Guardian Warden",
                       "倒れかけた時、靴が灯る。", "Just before falling, the boots light up."),
    "named.feet.043": ("癒し靴の薬", "Mender's Remedy Shoes",
                       "歩いた道が、薬の処方。", "The road walked writes the prescription."),
    "named.feet.044": ("巡礼靴の残身", "Pilgrim's Echo",
                       "かわした道が、次の一歩を教える。", "The path dodged teaches the next step."),
    "named.feet.056": ("石衛の溢れ残り", "Stoneguard Overflow",
                       "石の懐に、余った命を隠す。", "The stone's folds hide the life left over."),
    # ---- Feet / Summon ----
    "named.feet.021": ("獣趾靴の見守り", "Beastpaw Watch",
                       "獣の足跡が、後ろを見る。", "The paw prints watch behind you."),
    "named.feet.045": ("水銀脛の分け合い", "Quicksilver Share",
                       "流れた分を、味方に回す。", "Whatever flows off is routed to allies."),
    "named.feet.022": ("棘踏みの共演", "Thorntread Co-Star",
                       "棘の上で、誰かと並んで立つ。", "On the thorns, it stands beside someone."),
    "named.feet.046": ("潮溜まりの継ぎ", "Tidepool Relay",
                       "波が引けば、次の溜まりへ渡す。", "As the wave recedes, it hands to the next pool."),
    "named.feet.057": ("狼皮の見張り狩り", "Wolfskin Watch-Hunt",
                       "狼は見張り、息を集める。", "The wolf keeps watch and gathers breath."),
    "named.feet.058": ("狼歩きの二役", "Wolfstride Two Roles",
                       "盾を分け、後ろを見る。", "It splits its shield and watches behind."),
    # ---- Feet / Memory ----
    "named.feet.023": ("鴉羽靴の欠片", "Raven-Boot Shard",
                       "落とした羽の数だけ、恵み。", "For every feather shed, a boon."),
    "named.feet.047": ("根草履の終曲", "Root-Sandal Finale",
                       "編み終わった縄が、幕を引く。", "The finished braid draws the curtain."),
    "named.feet.024": ("星座履きの余韻", "Constellation Echo",
                       "星の並びが、鋭く響く。", "The stars' arrangement rings sharp."),
    "named.feet.048": ("星渡りの巡り", "Starstep Cycle",
                       "渡った星路が、巡って戻る。", "The star-road crossed circles back."),
    "named.feet.059": ("星履きの駆け恵み", "Starlit Dash-Boon",
                       "駆けた分だけ、恵みが積もる。", "The farther the dash, the deeper the boons."),
    "named.feet.060": ("囁き織りの幕", "Whisperweave Curtain",
                       "織り上げた囁きが、幕を早める。", "The woven whispers hurry the curtain."),
}

# ---------------------------------------------------------------------------
# Mini sets (spec 3.2): 18 two-piece + 12 three-piece, pieces in distinct slots.
# twoPiece stat values are at or below the typical affix median; never Attack% /
# Power%. threePiece is a weak (low band) non-conditional power.
# ---------------------------------------------------------------------------
SETS = [
    ("miniset.rime_lantern", "霜灯の二つ星", "Twin Rime Lanterns",
     ["named.weapon.004", "named.charm.004"], ("ColdAmp", 8), None,
     "霜路の灯守", "Keeper of the Frost Path"),
    ("miniset.hearth_ember", "炉端の残り火", "Hearthside Embers",
     ["named.armor.007", "named.feet.007"], ("FireAmp", 8), None,
     "火種の運び人", "Ember Carrier"),
    ("miniset.noon_blessing", "真昼の祝傘", "Noonday Blessed Shade",
     ["named.head.032", "named.hands.033"], ("ShieldPower", 4), None,
     "陽傘の祝人", "Blessed Shade-Bearer"),
    ("miniset.dusk_pact", "宵闇の密約", "Twilight Pact",
     ["named.weapon.035", "named.armor.012"], ("DarkAmp", 8), None,
     "宵の約束者", "Dusk Pactkeeper"),
    ("miniset.iron_vow", "鉄の誓約", "Iron Vow",
     ["named.head.037", "named.feet.038"], ("Armor", 6), None,
     "鉄の誓い人", "Iron-Sworn"),
    ("miniset.tailwind_pair", "追い風二つ", "Two Tailwinds",
     ["named.charm.041", "named.feet.016"], ("MoveSpeedPct", 3), None,
     "風の道連れ", "Wind's Companion"),
    ("miniset.sprout_care", "若芽の看病", "Nursing the Sprout",
     ["named.weapon.042", "named.charm.019"], ("HealPower", 4), None,
     "苗床の看病人", "Seedling Nurse"),
    ("miniset.pack_call", "群れの呼び声", "Call of the Pack",
     ["named.weapon.045", "named.head.021"], ("AttackSpeedPct", 4), None,
     "群れの笛吹き", "Pack Caller"),
    ("miniset.star_ledger", "星の帳面", "The Star Ledger",
     ["named.weapon.047", "named.charm.023"], ("Haste", 4), None,
     "星目録の記手", "Star-Ledger Keeper"),
    ("miniset.plain_craft", "道具の二人", "Two of Tools",
     ["named.weapon.001", "named.hands.002"], ("AttackFlat", 3), None,
     "道具の相棒", "Partners in Craft"),
    ("miniset.winter_watch", "冬の見張り", "Winter Watch",
     ["named.armor.029", "named.head.029"], ("Tenacity", 8), None,
     "冬営の見張り番", "Winter Camp Watch"),
    ("miniset.kindled_step", "火種の足取り", "Kindlefoot",
     ["named.hands.030", "named.charm.008"], ("CritChancePct", 3), None,
     "飛び火の目撃者", "Wildfire Witness"),
    ("miniset.dawn_pair", "暁の二灯", "Twin Dawn Lamps",
     ["named.armor.009", "named.feet.009"], ("MaxHealthPct", 4), None,
     "暁を運ぶ者", "Bringer of Dawn"),
    ("miniset.shadow_reverie", "影の空想", "Shadow Reverie",
     ["named.charm.036", "named.feet.012"], ("CritDamagePct", 8), None,
     "影の夢想家", "Shadow Dreamer"),
    ("miniset.shieldmates", "盾の戦友", "Shieldmates",
     ["named.weapon.039", "named.charm.014"], ("MaxHealthFlat", 20), None,
     "背中を預けし者", "Back-to-Back Bonded"),
    ("miniset.swift_vow", "疾風の二閃", "Twin Gale Flashes",
     ["named.weapon.041", "named.armor.016"], ("Haste", 4), None,
     "風と誓う者", "Windsworn"),
    ("miniset.warm_hands", "温もりの二つ手", "Two Warm Hands",
     ["named.armor.042", "named.feet.043"], ("HealthRegen", 2), None,
     "手を温める者", "Hand-Warmer"),
    ("miniset.old_friends", "古びた二つ星", "Two Old Friends",
     ["named.head.048", "named.feet.024"], ("PowerFlat", 3), None,
     "旧友の再会", "Old Friends Reunited"),
    ("miniset.winter_caravan", "冬の隊商", "Winter Caravan",
     ["named.weapon.028", "named.hands.005", "named.feet.005"], ("ColdAmp", 8), ("Frost", "low"),
     "霜道の先導者", "Frostroad Outrider"),
    ("miniset.triple_forge", "三つ火の鍛冶", "Three-Fire Forge",
     ["named.weapon.006", "named.head.006", "named.hands.006"], ("FireAmp", 8), ("Ember", "low"),
     "三炎の鍛冶職人", "Smith of Three Fires"),
    ("miniset.triple_light", "光の三唱", "Triple Light Chant",
     ["named.weapon.009", "named.charm.034", "named.armor.010"], ("LightAmp", 8), ("Radiance", "low"),
     "陽の運び手", "Sunbearer"),
    ("miniset.umbral_trio", "闇の三重唱", "Umbral Trio",
     ["named.head.011", "named.hands.035", "named.feet.035"], ("DarkAmp", 8), ("Umbra", "low"),
     "闇に歌う者", "Singer in the Dark"),
    ("miniset.shield_wall", "三枚の盾壁", "Wall of Three Shields",
     ["named.armor.014", "named.hands.037", "named.head.015"], ("Armor", 6), ("Bulwark", "low"),
     "動かぬ壁", "The Unmoving Wall"),
    ("miniset.gale_trio", "疾風の三脚", "Gale Tripod",
     ["named.head.016", "named.hands.018", "named.feet.040"], ("MoveSpeedPct", 3), ("Sprint", "low"),
     "風の三本足", "Three Legs of Wind"),
    ("miniset.healing_trio", "癒しの三手", "Three Healing Hands",
     ["named.head.020", "named.hands.019", "named.weapon.019"], ("HealPower", 4), ("Lifesteal", "low"),
     "三手の癒し手", "Healer of Three Hands"),
    ("miniset.pack_trio", "三匹の群れ", "A Pack of Three",
     ["named.armor.022", "named.charm.045", "named.feet.022"], ("HealthRegen", 2), ("PackFeast", "low"),
     "群れの同志", "Comrade of the Pack"),
    ("miniset.memory_trio", "記憶の三鍵", "Three Keys of Memory",
     ["named.armor.024", "named.hands.024", "named.weapon.048"], ("Haste", 4), ("CriticalEcho", "low"),
     "星霜の記録者", "Annalist of Ages"),
    ("miniset.plain_trio", "素朴な三つ道具", "Three Plain Tools",
     ["named.armor.025", "named.head.025", "named.feet.002"], ("AttackFlat", 3), ("Momentum", "low"),
     "道具箱の達人", "Master of the Toolbox"),
    ("miniset.tide_and_flame", "潮と焔", "Tide and Flame",
     ["named.charm.027", "named.head.028", "named.feet.030"], ("FireAmp", 8), ("Steam", "low"),
     "湯気の幻想家", "Dreamer in Steam"),
    ("miniset.dusk_and_dawn", "暁と宵", "Dusk and Dawn",
     ["named.head.012", "named.feet.013", "named.charm.009"], ("LightAmp", 8), ("Eclipse", "low"),
     "蝕を見る者", "Eclipse Watcher"),
]

RARITY = {"U": "Uncommon", "R": "Rare", "E": "Epic"}


def median(vals):
    v = sorted(vals)
    return v[len(v) // 2] if len(v) % 2 else (v[len(v) // 2 - 1] + v[len(v) // 2]) / 2.0


def main():
    bases, pools, affixes, enum_order, conditional, droppable, fampref, famtag = load_reference()
    skel = build_skeleton(bases, pools, enum_order, conditional, droppable, fampref)
    ids = assign_ids(skel)
    errs = []

    # Existing names (bases + uniques) that new names must not duplicate.
    src = open(SRC, encoding="utf-8").read()
    uniques = re.findall(r'new UniqueDef\("[^"]+", "[^"]+", new Txt\("([^"]+)", "([^"]+)"\)', src)
    taken_ja = {b["ja"] for bs in bases.values() for b in bs} | {u[0] for u in uniques}
    taken_en = {b["en"] for bs in bases.values() for b in bs} | {u[1] for u in uniques}

    if set(PROSE) != set(ids.values()):
        errs.append("PROSE keys != skeleton ids: %s" % sorted(set(PROSE) ^ set(ids.values()))[:8])

    membership = {p for s in SETS for p in s[3]}
    named = []
    combo = collections.defaultdict(set)
    names_ja, names_en = collections.Counter(), collections.Counter()
    cnt = collections.Counter()
    prefstat = collections.Counter()
    for slot in SLOTS:
        for fam in FAM:
            for ordinal in range(6):
                key = (slot, fam, ordinal)
                nid = ids[key]
                ent = skel[key]
                ja, en, lja, len_ = PROSE[nid]
                base = next(b for b in bases[slot] if b["id"] == ent["baseId"])
                if famtag[base["id"]] != fam:
                    errs.append("base family mismatch %s" % nid)
                cnt[(slot, ent["rarity"])] += 1
                names_ja[ja] += 1
                names_en[en] += 1
                if ja in taken_ja:
                    errs.append("nameJa collides with existing item: %s %s" % (nid, ja))
                if en in taken_en:
                    errs.append("nameEn collides with existing item: %s %s" % (nid, en))
                if not ja or len(ja) > 14 or re.search(r"\d", ja + en):
                    errs.append("name rule %s" % nid)
                if not lja or len(lja) > 30:
                    errs.append("loreJa rule %s" % nid)
                if not len_:
                    errs.append("loreEn required %s" % nid)
                powers = []
                for i, (p, band) in enumerate(ent["powers"]):
                    if p not in enum_order or p not in droppable:
                        errs.append("power not droppable %s %s" % (nid, p))
                    if ent["rarity"] != "E" and p in conditional:
                        errs.append("conditional power below Epic %s %s" % (nid, p))
                    if ent["rarity"] == "U" and band != "low":
                        errs.append("uncommon band %s" % nid)
                    if ent["rarity"] == "R" and band not in ("low", "mid"):
                        errs.append("rare band %s" % nid)
                    if ent["rarity"] == "E":
                        if i == 0 and band not in ("mid", "high"):
                            errs.append("epic first band %s" % nid)
                        if i == 1 and band not in ("low", "mid"):
                            errs.append("epic second band %s" % nid)
                        if i == 1 and p in conditional:
                            errs.append("epic second power conditional %s" % nid)
                    power_entry = {"power": p}
                    if p not in pools[slot]:
                        power_entry["rangeSlot"] = next(source for source in SLOTS if p in pools[source])
                    power_entry["band"] = band
                    powers.append(power_entry)
                    combo[slot].add(tuple(sorted([q["power"] for q in powers])))
                    prefs = fampref.get(fam) or PLAINPREF
                    prefstat[(fam, p in prefs)] += 1
                if len({q["power"] for q in powers}) != len(powers):
                    errs.append("duplicate power within item %s" % nid)
                named.append({
                    "id": nid, "slot": slot, "rarity": RARITY[ent["rarity"]],
                    "baseId": ent["baseId"], "family": fam,
                    "nameJa": ja, "nameEn": en, "loreJa": lja, "loreEn": len_,
                    "powers": powers, "miniSetId": None,
                })
    for slot in SLOTS:
        keys = collections.Counter(tuple(sorted(q["power"] for q in e["powers"])) for e in named if e["slot"] == slot)
        if any(c > 1 for c in keys.values()):
            errs.append("duplicate power set within slot %s: %s" % (slot, [k for k, c in keys.items() if c > 1]))
    for slot in SLOTS:
        for r, want in (("U", 24), ("R", 24), ("E", 12)):
            if cnt[(slot, r)] != want:
                errs.append("slot %s rarity %s: %d (want %d)" % (slot, r, cnt[(slot, r)], want))
    for n, c in names_ja.items():
        if c > 1:
            errs.append("duplicate nameJa: %s" % n)
    for n, c in names_en.items():
        if c > 1:
            errs.append("duplicate nameEn: %s" % n)

    byid = {e["id"]: e for e in named}
    used = collections.Counter()
    sets_out = []
    n2 = n3 = 0
    for sid, ja, en, pieces, two, three, tja, ten in SETS:
        if len(pieces) == 2:
            n2 += 1
        else:
            n3 += 1
        if len(pieces) not in (2, 3):
            errs.append("bad piece count %s" % sid)
        slots = [byid[p]["slot"] for p in pieces if p in byid]
        if len(slots) != len(pieces) or len(set(slots)) != len(slots):
            errs.append("pieces missing or same slot %s" % sid)
        for p in pieces:
            used[p] += 1
            byid[p]["miniSetId"] = sid
        stat, val = two
        med = median([(mn + mx) / 2.0 for mn, mx in affixes[stat]])
        if stat in ("AttackPct", "PowerPct") or val > med:
            errs.append("twoPiece rule %s %s %s>%s" % (sid, stat, val, med))
        if len(pieces) == 2 and three is not None:
            errs.append("two-piece set has threePiece %s" % sid)
        if len(pieces) == 3:
            if not three or three[1] != "low" or three[0] in conditional or three[0] not in droppable:
                errs.append("threePiece rule %s" % sid)
        sets_out.append({
            "id": sid, "nameJa": ja, "nameEn": en, "pieces": pieces,
            "twoPiece": {"stat": stat, "value": val},
            "threePiece": {"power": three[0],
                           "rangeSlot": next(source for source in SLOTS if three[0] in pools[source]),
                           "band": three[1]} if three else None,
            "titleJa": tja, "titleEn": ten,
        })
    if (n2, n3) != (18, 12):
        errs.append("need 18 two-piece and 12 three-piece sets (got %d, %d)" % (n2, n3))
    if len(sets_out) != 30:
        errs.append("need 30 sets (got %d)" % len(sets_out))
    if any(c > 1 for c in used.values()):
        errs.append("piece in two sets: %s" % [p for p, c in used.items() if c > 1])
    if len(used) != 72 or sum(1 for e in named if e["miniSetId"]) != 72:
        errs.append("mini-set membership must be exactly 72 pieces (got %d used / %d tagged)"
                    % (len(used), sum(1 for e in named if e["miniSetId"])))
    titles = collections.Counter(s[6] for s in SETS) + collections.Counter(s[7] for s in SETS)
    if any(c > 1 for c in titles.values()):
        errs.append("duplicate title: %s" % [t for t, c in titles.items() if c > 1])

    if errs:
        print("ERRORS (%d):" % len(errs))
        for e in errs[:30]:
            print(" -", e)
        sys.exit(1)

    named.sort(key=lambda e: e["id"])
    with open(OUT_NAMED, "w", encoding="utf-8", newline="\n") as f:
        json.dump(named, f, ensure_ascii=False, indent=2)
        f.write("\n")
    with open(OUT_SETS, "w", encoding="utf-8", newline="\n") as f:
        json.dump(sets_out, f, ensure_ascii=False, indent=2)
        f.write("\n")

    for slot in SLOTS:
        inpool = sum(1 for e in named if e["slot"] == slot for q in e["powers"] if q["power"] in pools[slot])
        total = sum(1 for e in named if e["slot"] == slot for _ in e["powers"])
        print("%s: %d/%d power picks in slot pool" % (slot, inpool, total))
    for fam in FAM:
        pref = prefstat[(fam, True)]
        tot = pref + prefstat[(fam, False)]
        print("%s: %d/%d picks are family-preferred" % (fam, pref, tot))
    print("OK: named.json (%d), minisets.json (%d)" % (len(named), len(sets_out)))


if __name__ == "__main__":
    main()
