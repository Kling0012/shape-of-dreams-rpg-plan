# -*- coding: utf-8 -*-
"""Generates base-families.json and new-bases.json for v1.32 low-rarity volume (spec 3.1/3.3).

Reads the 360 existing BaseDefs from src/SodRpg.Core/Game/Content.cs, applies the
hand-authored family tags below, adds the hand-authored 240 new bases, validates
every constraint, and writes the two JSON files.

Run:  python tools/lowrarity/gen_lrdata.py
"""
import collections
import json
import re
import sys

SRC = "src/SodRpg.Core/Game/Content.cs"
FAM = ["Plain", "Frost", "Flame", "Light", "Dark", "Guard", "Gale", "Mend", "Summon", "Memory"]

# ---------------------------------------------------------------------------
# Family tags for the 360 existing bases (hand-authored; ids/names/stats unchanged).
# ---------------------------------------------------------------------------
TAGS = {
    # --- Weapon ---
    "weapon.chain_sword": "Plain", "weapon.war_axe": "Plain", "weapon.hooked_falchion": "Plain",
    "weapon.bone_cleaver": "Plain", "weapon.hunting_bow": "Plain", "weapon.chanting_wand": "Plain",
    "weapon.frost_spear": "Frost", "weapon.glacier_spear": "Frost", "weapon.northwind_axe": "Frost",
    "weapon.maelstrom_sword": "Frost", "weapon.thaw_hammer": "Frost", "weapon.tide_cutter": "Frost",
    "weapon.blaze_greatsword": "Flame", "weapon.ember_whip": "Flame", "weapon.whiteheat_estoc": "Flame",
    "weapon.rockbreaker": "Flame", "weapon.severing_axe": "Flame", "weapon.longspike_bow": "Flame",
    "weapon.lantern_rod": "Light", "weapon.sunlit_blade": "Light", "weapon.dawn_scepter": "Light",
    "weapon.starlit_knives": "Light", "weapon.starsinger_bow": "Light", "weapon.evilbreaker_lance": "Light",
    "weapon.dusk_scythe": "Dark", "weapon.gloaming_dagger": "Dark", "weapon.moon_sickle": "Dark",
    "weapon.astral_staff": "Dark", "weapon.ember_katar": "Dark", "weapon.twin_rapier": "Dark",
    "weapon.tower_lance": "Guard", "weapon.iron_halberd": "Guard", "weapon.shield_maul": "Guard",
    "weapon.oath_mace": "Guard", "weapon.tortoise_bokken": "Guard", "weapon.anchor_cleaver": "Guard",
    "weapon.swallow_kodachi": "Gale", "weapon.lightning_pair": "Gale", "weapon.thunder_hammer": "Gale",
    "weapon.windhowl_staff": "Gale", "weapon.evening_fan": "Gale", "weapon.twin_fang": "Gale",
    "weapon.dewclear_wand": "Mend", "weapon.pilgrim_staff": "Mend", "weapon.calming_staff": "Mend",
    "weapon.stillwater_blade": "Mend", "weapon.lighthouse_cudgel": "Mend", "weapon.vow_mace": "Mend",
    "weapon.bone_flute": "Summon", "weapon.tide_trident": "Summon", "weapon.gatehouse_maul": "Summon",
    "weapon.storm_glaive": "Summon", "weapon.granite_cosh": "Summon", "weapon.star_harp": "Summon",
    "weapon.dream_tome": "Memory", "weapon.starchart_scroll": "Memory", "weapon.dream_wand": "Memory",
    "weapon.moonlit_cane": "Memory", "weapon.firefly_staff": "Memory", "weapon.conch_scepter": "Memory",
    # --- Armor ---
    "armor.hunter_leather": "Plain", "armor.skirmisher_coat": "Plain", "armor.chain_hauberk": "Plain",
    "armor.spiked_plate": "Plain", "armor.citadel_plate": "Plain", "armor.ember_jacket": "Plain",
    "armor.frost_coat": "Frost", "armor.frost_robe": "Frost", "armor.glacier_harness": "Frost",
    "armor.tidebound_mail": "Frost", "armor.winterwool_coat": "Frost", "armor.aurora_wrap": "Frost",
    "armor.ember_plate": "Flame", "armor.ember_cuirass": "Flame", "armor.volcanic_coat": "Flame",
    "armor.scale_coat": "Flame", "armor.basalt_cuirass": "Flame", "armor.dancer_garb": "Flame",
    "armor.sun_plate": "Light", "armor.dawnlight_corset": "Light", "armor.mirrorsilk_robe": "Light",
    "armor.wardsigil_vest": "Light", "armor.prayer_shawl": "Light", "armor.morningdew_robe": "Light",
    "armor.shadow_cloak": "Dark", "armor.duskweave_jacket": "Dark", "armor.ambush_vest": "Dark",
    "armor.ink_robe": "Dark", "armor.hunter_vest": "Dark", "armor.twistedchain_ply": "Dark",
    "armor.guardian_plate": "Guard", "armor.thorn_mail": "Guard", "armor.bastion_shell": "Guard",
    "armor.rampart_jacket": "Guard", "armor.ironbark_vest": "Guard", "armor.counter_gauntlets": "Guard",
    "armor.flowing_cloak": "Gale", "armor.swiftstep_coat": "Gale", "armor.zephyr_robe": "Gale",
    "armor.stormfront_vest": "Gale", "armor.quicksilver_jacket": "Gale", "armor.traveler_coat": "Gale",
    "armor.healing_sash": "Mend", "armor.lampkeeper_mantle": "Mend", "armor.candlelight_robe": "Mend",
    "armor.monk_garb": "Mend", "armor.deeproot_vest": "Mend", "armor.nightloom_robe": "Mend",
    "armor.summoners_vest": "Summon", "armor.seafoam_gown": "Summon", "armor.oathplate_cuirass": "Summon",
    "armor.star_mantle": "Summon", "armor.emberweave_shawl": "Summon", "armor.buckler_vest": "Summon",
    "armor.star_cloak": "Memory", "armor.resonant_robe": "Memory", "armor.mist_robe": "Memory",
    "armor.moon_silk": "Memory", "armor.root_mail": "Memory", "armor.bark_mail": "Memory",
    # --- Head ---
    "head.hunter_hood": "Plain", "head.sage_hat": "Plain", "head.berserker_mask": "Plain",
    "head.ash_hood": "Plain", "head.iron_coif": "Plain", "head.longshot_cap": "Plain",
    "head.moon_hood": "Frost", "head.frost_helm": "Frost", "head.rimebloom_hood": "Frost",
    "head.icewall_helm": "Frost", "head.sunkenbell_helm": "Frost", "head.moonlace_hood": "Frost",
    "head.iron_helm": "Flame", "head.sun_mask": "Flame", "head.antler_crown": "Flame",
    "head.magma_band": "Flame", "head.crimsonlotus_hood": "Flame", "head.emberbloom_crown": "Flame",
    "head.ember_crown": "Light", "head.radiant_halo": "Light", "head.star_diadem": "Light",
    "head.noonlight_circlet": "Light", "head.oathring_circlet": "Light", "head.wardband": "Light",
    "head.plague_mask": "Dark", "head.eye_patch": "Dark", "head.raven_mask": "Dark",
    "head.talon_crown": "Dark", "head.eclipse_mask": "Dark", "head.vulture_hood": "Dark",
    "head.warden_visor": "Guard", "head.thorn_circlet": "Guard", "head.knight_helm": "Guard",
    "head.sentry_visor": "Guard", "head.boulder_helm": "Guard", "head.fortress_coif": "Guard",
    "head.mist_veil": "Gale", "head.scout_goggles": "Gale", "head.gale_hood": "Gale",
    "head.hawkeye_band": "Gale", "head.thunderveil_hood": "Gale", "head.whisper_veil": "Gale",
    "head.healer_band": "Mend", "head.dream_veil": "Mend", "head.moss_crown": "Mend",
    "head.sunbeam_hood": "Mend", "head.meditation_band": "Mend", "head.kindly_circlet": "Mend",
    "head.horned_helm": "Summon", "head.wolf_pelt": "Summon", "head.coral_crown": "Summon",
    "head.leaf_wreath": "Summon", "head.void_helm": "Summon", "head.beastcaller_antlers": "Summon",
    "head.dream_circlet": "Memory", "head.lantern_hat": "Memory", "head.pillar_crown": "Memory",
    "head.comet_diadem": "Memory", "head.tidal_circlet": "Memory", "head.starless_veil": "Memory",
    "charm.hunters_seal": "Plain", "charm.war_horn": "Plain", "charm.skirmish_ring": "Plain",
    "charm.longshot_charm": "Plain", "charm.fang_necklace": "Plain", "charm.iron_seal": "Plain",
    "charm.frost_pendant": "Frost", "charm.glacier_charm": "Frost", "charm.snowbloom_charm": "Frost",
    "charm.winteroak_charm": "Frost", "charm.tidewarden_brooch": "Frost", "charm.moon_bell": "Frost",
    "charm.ember_locket": "Flame", "charm.blaze_brooch": "Flame", "charm.cindercore_locket": "Flame",
    "charm.garnet_ring": "Flame", "charm.comet_pendant": "Flame", "charm.war_drum": "Flame",
    "charm.sun_brooch": "Light", "charm.sunspoke_pin": "Light", "charm.dawnsilk_band": "Light",
    "charm.stardust_pendant": "Light", "charm.keeneye_charm": "Light", "charm.resonance_amulet": "Light",
    "charm.shadow_mask": "Dark", "charm.shadow_ring": "Dark", "charm.duskbead_necklace": "Dark",
    "charm.raven_feather": "Dark", "charm.ink_stone": "Dark", "charm.storm_bell": "Dark",
    "charm.pulsing_core": "Guard", "charm.chain_necklace": "Guard", "charm.guardian_seal": "Guard",
    "charm.stone_heart": "Guard", "charm.iron_feather": "Guard", "charm.bulwark_seal": "Guard",
    "charm.tailwind_ring": "Gale", "charm.zephyr_ring": "Gale", "charm.stormcloud_locket": "Gale",
    "charm.windchime_charm": "Gale", "charm.pursuit_badge": "Gale", "charm.ironknot_ring": "Gale",
    "charm.mender_locket": "Mend", "charm.lotus_seal": "Mend", "charm.hearthside_ring": "Mend",
    "charm.hearthstone": "Mend", "charm.halo_charm": "Mend", "charm.oak_amulet": "Mend",
    "charm.beasttongue_charm": "Summon", "charm.seafoam_ring": "Summon", "charm.talon_pendant": "Summon",
    "charm.hunter_tooth": "Summon", "charm.deeproot_charm": "Summon", "charm.boulder_pendant": "Summon",
    "charm.dream_lens": "Memory", "charm.old_clock": "Memory", "charm.eclipse_ring": "Memory",
    "charm.clockwork_charm": "Memory", "charm.feather_token": "Memory", "charm.ice_heart": "Memory",
    "hands.leather_gloves": "Plain", "hands.iron_gauntlets": "Plain", "hands.archer_bracers": "Plain",
    "hands.reach_bracers": "Plain", "hands.duelist_gloves": "Plain", "hands.bowmaster_bracers": "Plain",
    "hands.frost_mitts": "Frost", "hands.radiant_wraps": "Frost", "hands.vigor_grips": "Frost",
    "hands.frostbite_knuckles": "Frost", "hands.reef_gauntlets": "Frost", "hands.snowmelt_mitts": "Frost",
    "hands.ember_gauntlets": "Flame", "hands.chain_wraps": "Flame", "hands.ripgrip_gloves": "Flame",
    "hands.blazeknit_gloves": "Flame", "hands.sunfire_grips": "Flame", "hands.lantern_fingerless": "Flame",
    "hands.oath_gauntlets": "Light", "hands.tide_gloves": "Light", "hands.sun_gauntlets": "Light",
    "hands.precise_fingerless": "Light", "hands.oathpalm_gloves": "Light", "hands.duskstitch_gloves": "Light",
    "hands.shadow_gloves": "Dark", "hands.flame_grips": "Dark", "hands.thief_gloves": "Dark",
    "hands.nightpalm_gloves": "Dark", "hands.riven_fists": "Dark", "hands.duelist_bracers": "Dark",
    "hands.stone_fists": "Guard", "hands.thorn_wraps": "Guard", "hands.wardens_grips": "Guard",
    "hands.ironvein_gauntlets": "Guard", "hands.heavypalm_gloves": "Guard", "hands.bulwark_wraps": "Guard",
    "hands.quick_fingers": "Gale", "hands.sling_bracers": "Gale", "hands.monk_wraps": "Gale",
    "hands.swiftpalm_gloves": "Gale", "hands.storm_knuckles": "Gale", "hands.chime_bracers": "Gale",
    "hands.healer_hands": "Mend", "hands.alchemist_gloves": "Mend", "hands.prayer_beads": "Mend",
    "hands.ice_bracers": "Mend", "hands.bark_knuckles": "Mend", "hands.mender_palms": "Mend",
    "hands.claw_gauntlets": "Summon", "hands.bone_knuckles": "Summon", "hands.hunting_sinew": "Summon",
    "hands.viperfang_claws": "Summon", "hands.tidecaller_wraps": "Summon", "hands.summoner_bands": "Summon",
    "hands.spell_gloves": "Memory", "hands.star_rings": "Memory", "hands.void_claws": "Memory",
    "hands.rootgrip_gloves": "Memory", "hands.manuscript_gloves": "Memory", "hands.cinderthread_wraps": "Memory",
    # --- Feet ---
    "feet.travel_boots": "Plain", "feet.stone_boots": "Plain", "feet.spiked_boots": "Plain",
    "feet.ash_boots": "Plain", "feet.hunter_boots": "Plain", "feet.iron_clogs": "Plain",
    "feet.rooted_boots": "Frost", "feet.frost_boots": "Frost", "feet.froststride_shoes": "Frost",
    "feet.ironwave_greaves": "Frost", "feet.winterhide_boots": "Frost", "feet.dawnmist_shoes": "Frost",
    "feet.shadow_slippers": "Flame", "feet.tide_sandals": "Flame", "feet.dawn_steps": "Flame",
    "feet.ember_slippers": "Flame", "feet.emberdash_boots": "Flame", "feet.mistral_sandals": "Flame",
    "feet.sage_slippers": "Light", "feet.knight_sabatons": "Light", "feet.sun_sandals": "Light",
    "feet.sunspur_boots": "Light", "feet.wardstep_sandals": "Light", "feet.cometstride_shoes": "Light",
    "feet.stalker_boots": "Dark", "feet.ember_treads": "Dark", "feet.mist_shoes": "Dark",
    "feet.duskstep_boots": "Dark", "feet.deadeye_leggings": "Dark", "feet.nightveil_slippers": "Dark",
    "feet.iron_greaves": "Guard", "feet.chain_greaves": "Guard", "feet.bastion_sabatons": "Guard",
    "feet.tortoiseshell_greaves": "Guard", "feet.ballast_boots": "Guard", "feet.rampart_greaves": "Guard",
    "feet.dancer_shoes": "Gale", "feet.wind_sandals": "Gale", "feet.storm_boots": "Gale",
    "feet.blitz_treads": "Gale", "feet.hunter_striders": "Gale", "feet.gale_greaves": "Gale",
    "feet.pilgrim_boots": "Mend", "feet.guard_sabatons": "Mend", "feet.deeproot_boots": "Mend",
    "feet.stoneguard_sabatons": "Mend", "feet.firebloom_slippers": "Mend", "feet.mender_shoes": "Mend",
    "feet.wolf_boots": "Summon", "feet.quicksilver_greaves": "Summon", "feet.wolfstride_boots": "Summon",
    "feet.thorntread_sabatons": "Summon", "feet.tidepool_sandals": "Summon", "feet.beastpaw_boots": "Summon",
    "feet.star_steps": "Memory", "feet.root_sandals": "Memory", "feet.star_slippers": "Memory",
    "feet.raven_boots": "Memory", "feet.starlit_moccasins": "Memory", "feet.whisperweave_shoes": "Memory",
    # --- Feet ---
}

# ---------------------------------------------------------------------------
# New bases (240 = 40/slot, 4 per slot x family). Hand-authored names.
# (id, slot, line, family, nameJa, nameEn, stat, value)
# ---------------------------------------------------------------------------
NEW = [
    # --- Weapon (40) ---
    ("weapon.plain_edge", "Weapon", "Offense", "Plain", "無垢の直刀", "Unblemished Blade", "AttackFlat", 5),
    ("weapon.soldier_saber", "Weapon", "Offense", "Plain", "兵の軍刀", "Soldier's Saber", "AttackFlat", 6),
    ("weapon.rough_cleaver", "Weapon", "Offense", "Plain", "無骨な鉈", "Unrefined Cleaver", "CritDamagePct", 12),
    ("weapon.apprentice_wand", "Weapon", "Resonance", "Plain", "見習いの短杖", "Apprentice's Wand", "PowerFlat", 5),
    ("weapon.icevein_blade", "Weapon", "Resonance", "Frost", "氷脈の刀", "Icevein Blade", "ColdAmp", 10),
    ("weapon.hoarfrost_dirk", "Weapon", "Offense", "Frost", "霧氷の短剣", "Hoarfrost Dirk", "CritChancePct", 3),
    ("weapon.winterbrand_mace", "Weapon", "Guard", "Frost", "冬の戦鎚", "Winterbrand Mace", "MaxHealthFlat", 20),
    ("weapon.frozen_oath_blade", "Weapon", "Guard", "Frost", "凍てつく誓いの剣", "Frozen-Oath Blade", "Tenacity", 8),
    ("weapon.inferno_lash", "Weapon", "Offense", "Flame", "業火の鞭", "Inferno Lash", "FireAmp", 10),
    ("weapon.firedance_saber", "Weapon", "Offense", "Flame", "火舞の細剣", "Firedance Saber", "AttackSpeedPct", 5),
    ("weapon.phoenix_plume_sword", "Weapon", "Offense", "Flame", "鳳羽の剣", "Phoenix-Plume Sword", "AttackRangePct", 5),
    ("weapon.cauterizing_estoc", "Weapon", "Offense", "Flame", "焼灼の刺突剣", "Cauterizing Estoc", "CritDamagePct", 14),
    ("weapon.daybreak_blade", "Weapon", "Resonance", "Light", "夜明けの刃", "Daybreak Blade", "PowerFlat", 5),
    ("weapon.halo_blade", "Weapon", "Offense", "Light", "光環の剣", "Halo Blade", "CritChancePct", 3),
    ("weapon.seraph_rod", "Weapon", "Resonance", "Light", "煌めく天使の杖", "Seraph's Rod", "HealPower", 5),
    ("weapon.lamplighter_sword", "Weapon", "Guard", "Light", "灯守の剣", "Lamplighter Sword", "ShieldPower", 5),
    ("weapon.shadowfang_knife", "Weapon", "Offense", "Dark", "影牙の短刀", "Shadowfang Knife", "DarkAmp", 10),
    ("weapon.nightreaver_axe", "Weapon", "Offense", "Dark", "夜刈りの斧", "Nightreaver Axe", "AttackFlat", 5),
    ("weapon.eclipsed_rapier", "Weapon", "Offense", "Dark", "蝕刻の突剣", "Eclipsed Rapier", "CritDamagePct", 12),
    ("weapon.shadowstitch_kodachi", "Weapon", "Offense", "Dark", "影縫いの小太刀", "Shadowstitch Kodachi", "AttackSpeedPct", 3),
    ("weapon.sentry_halberd", "Weapon", "Guard", "Guard", "哨兵の斧槍", "Sentry's Halberd", "Armor", 7),
    ("weapon.bulwark_greatsword", "Weapon", "Guard", "Guard", "壁衛の大剣", "Bulwark Greatsword", "Armor", 9),
    ("weapon.oakheart_club", "Weapon", "Guard", "Guard", "樫心の棍", "Oakheart Club", "HealthRegen", 2),
    ("weapon.vanguard_hammer", "Weapon", "Guard", "Guard", "先陣の戦鎚", "Vanguard Hammer", "MaxHealthPct", 6),
    ("weapon.whirlwind_naginata", "Weapon", "Offense", "Gale", "旋風の薙刀", "Whirlwind Naginata", "AttackRangePct", 6),
    ("weapon.gale_tossed_blades", "Weapon", "Offense", "Gale", "疾風の投刃", "Gale-Tossed Blades", "AttackSpeedPct", 3),
    ("weapon.stormstring_bow", "Weapon", "Offense", "Gale", "風鳴りの弓", "Stormstring Bow", "AttackFlat", 5),
    ("weapon.tailwind_saber", "Weapon", "Resonance", "Gale", "追風の細剣", "Tailwind Saber", "Haste", 4),
    ("weapon.mercy_staff", "Weapon", "Resonance", "Mend", "慈悲の杖", "Staff of Mercy", "HealthRegen", 3),
    ("weapon.herbalist_sickle", "Weapon", "Resonance", "Mend", "薬草鎌", "Herbalist's Sickle", "PowerFlat", 5),
    ("weapon.soothing_chime_staff", "Weapon", "Guard", "Mend", "癒し鈴の杖", "Soothing-Chime Staff", "MaxHealthFlat", 20),
    ("weapon.dewdrop_wand", "Weapon", "Resonance", "Mend", "露滴の細杖", "Dewdrop Wand", "Haste", 4),
    ("weapon.callers_greatsword", "Weapon", "Resonance", "Summon", "喚起の大剣", "Caller's Greatsword", "SummonPower", 8),
    ("weapon.beast_tamer_whip", "Weapon", "Offense", "Summon", "獣使いの鞭", "Beast-Tamer's Whip", "AttackFlat", 7),
    ("weapon.pact_dagger", "Weapon", "Offense", "Summon", "契約の短剣", "Pact Dagger", "CritDamagePct", 10),
    ("weapon.spirit_call_lance", "Weapon", "Offense", "Summon", "霊呼びの槍", "Spirit-Call Lance", "AttackRangePct", 5),
    ("weapon.remembrance_blade", "Weapon", "Resonance", "Memory", "追憶の剣", "Remembrance Blade", "PowerFlat", 6),
    ("weapon.yesteryear_staff", "Weapon", "Resonance", "Memory", "在りし日の杖", "Yesteryear Staff", "PowerFlat", 7),
    ("weapon.dusk_memory_scythe", "Weapon", "Resonance", "Memory", "夕闇の追憶鎌", "Dusk-Memory Scythe", "DarkAmp", 8),
    ("weapon.relic_hunter_blade", "Weapon", "Offense", "Memory", "遺跡巡りの刀", "Relic-Hunter Blade", "MoveSpeedPct", 3),
    # --- Armor (40) ---
    ("armor.sturdy_jerkin", "Armor", "Guard", "Plain", "頑丈な革胴", "Sturdy Jerkin", "Armor", 6),
    ("armor.militia_cuirass", "Armor", "Guard", "Plain", "郷勇の胸当て", "Militia Cuirass", "MaxHealthPct", 5),
    ("armor.roadworn_jacket", "Armor", "Offense", "Plain", "旅路の上着", "Roadworn Jacket", "AttackSpeedPct", 3),
    ("armor.padded_vest", "Armor", "Guard", "Plain", "詰め物の胴着", "Padded Vest", "HealthRegen", 2),
    ("armor.hoarfrost_mail", "Armor", "Resonance", "Frost", "霧氷の鎖帷子", "Hoarfrost Mail", "ColdAmp", 10),
    ("armor.frozen_rampart", "Armor", "Guard", "Frost", "氷塁の胸当て", "Frozen Rampart", "Armor", 7),
    ("armor.snowdrift_coat", "Armor", "Guard", "Frost", "雪どけの上衣", "Snowdrift Coat", "HealthRegen", 3),
    ("armor.iceskimmer_wrap", "Armor", "Resonance", "Frost", "氷滑りの帯衣", "Iceskimmer Wrap", "MoveSpeedPct", 3),
    ("armor.cinderplate", "Armor", "Offense", "Flame", "燃殻の板金鎧", "Cinderplate", "AttackFlat", 6),
    ("armor.furnace_guard", "Armor", "Guard", "Flame", "火床の胸当て", "Furnace Guard", "MaxHealthPct", 6),
    ("armor.ember_flare_jacket", "Armor", "Offense", "Flame", "火焔閃の上着", "Ember-Flare Jacket", "CritDamagePct", 12),
    ("armor.slowfire_bandolier", "Armor", "Guard", "Flame", "燠りの帯衣", "Slowfire Bandolier", "HealthRegen", 2),
    ("armor.dawnweave_harness", "Armor", "Resonance", "Light", "暁織りの胸綱", "Dawnweave Harness", "LightAmp", 10),
    ("armor.sanctum_vest", "Armor", "Resonance", "Light", "聖域の胴衣", "Sanctum Vest", "HealPower", 5),
    ("armor.clearsky_corset", "Armor", "Offense", "Light", "晴空の胴衣", "Clear-Sky Corset", "CritChancePct", 3),
    ("armor.lamplight_sash", "Armor", "Resonance", "Light", "灯火の飾り帯", "Lamplight Sash", "Haste", 4),
    ("armor.midnight_hauberk", "Armor", "Offense", "Dark", "真夜中の鎖帷子", "Midnight Hauberk", "DarkAmp", 10),
    ("armor.wraithweave_coat", "Armor", "Offense", "Dark", "幽織りの外套", "Wraithweave Coat", "CritDamagePct", 14),
    ("armor.nightprowler_jacket", "Armor", "Offense", "Dark", "夜盗の上着", "Nightprowler's Jacket", "AttackFlat", 7),
    ("armor.shadowstep_cloak", "Armor", "Resonance", "Dark", "影歩みの外套", "Shadowstep Cloak", "MoveSpeedPct", 4),
    ("armor.keepers_plate", "Armor", "Guard", "Guard", "預かり人の板金鎧", "Keeper's Plate", "Armor", 9),
    ("armor.deepwell_cuirass", "Armor", "Guard", "Guard", "深井戸の胴鎧", "Deepwell Cuirass", "MaxHealthFlat", 30),
    ("armor.unyielding_ply", "Armor", "Guard", "Guard", "屈しぬ腹当て", "Unyielding Ply", "Tenacity", 12),
    ("armor.warden_sigil_vest", "Armor", "Guard", "Guard", "番印の胴衣", "Warden-Sigil Vest", "ShieldPower", 5),
    ("armor.windcutter_vest", "Armor", "Offense", "Gale", "風切りの胸当て", "Windcutter Vest", "AttackSpeedPct", 5),
    ("armor.skyclad_sash", "Armor", "Resonance", "Gale", "天衣の帯", "Skyclad Sash", "Haste", 4),
    ("armor.darting_wrap", "Armor", "Offense", "Gale", "疾走の帯衣", "Darting Wrap", "AttackFlat", 5),
    ("armor.cyclone_cloak", "Armor", "Offense", "Gale", "竜巻の外套", "Cyclone Cloak", "CritDamagePct", 10),
    ("armor.herb_pouch_vest", "Armor", "Resonance", "Mend", "薬袋の胴着", "Herb-Pouch Vest", "PowerFlat", 5),
    ("armor.gentle_rain_jacket", "Armor", "Resonance", "Mend", "慈雨の上着", "Gentle-Rain Jacket", "Haste", 4),
    ("armor.bandage_wrap", "Armor", "Guard", "Mend", "包帯の巻衣", "Bandage Wrap", "ShieldPower", 5),
    ("armor.longevity_coat", "Armor", "Resonance", "Mend", "長命の上衣", "Longevity Coat", "MoveSpeedPct", 3),
    ("armor.whistle_call_vest", "Armor", "Offense", "Summon", "呼笛の胴衣", "Whistle-Call Vest", "AttackFlat", 5),
    ("armor.lair_woven_mail", "Armor", "Guard", "Summon", "巣織りの帷子", "Lair-Woven Mail", "MaxHealthFlat", 25),
    ("armor.familiars_shawl", "Armor", "Resonance", "Summon", "使い魔の肩掛け", "Familiar's Shawl", "Haste", 5),
    ("armor.pack_leader_wrap", "Armor", "Offense", "Summon", "群れ率びの腹巻", "Pack-Leader Wrap", "CritChancePct", 3),
    ("armor.annals_robe", "Armor", "Resonance", "Memory", "年代記の法衣", "Annals Robe", "PowerFlat", 6),
    ("armor.afterglow_vest", "Armor", "Resonance", "Memory", "名残りの胴衣", "Afterglow Vest", "DarkAmp", 8),
    ("armor.keepsake_cloak", "Armor", "Resonance", "Memory", "形見の外套", "Keepsake Cloak", "Haste", 6),
    ("armor.lingering_image_coat", "Armor", "Guard", "Memory", "面影の上衣", "Lingering-Image Coat", "MaxHealthFlat", 20),
    # --- Charm (40) ---
    ("charm.copper_band", "Charm", "Offense", "Plain", "銅の腕輪", "Copper Band", "AttackFlat", 7),
    ("charm.rough_hewn_charm", "Charm", "Offense", "Plain", "荒削りの御守り", "Rough-Hewn Charm", "CritDamagePct", 12),
    ("charm.simple_sigil", "Charm", "Resonance", "Plain", "素朴な印章", "Simple Sigil", "PowerFlat", 5),
    ("charm.journeythread_ring", "Charm", "Resonance", "Plain", "旅緒の指輪", "Journeythread Ring", "MoveSpeedPct", 3),
    ("charm.glacier_locket", "Charm", "Guard", "Frost", "氷晶のロケット", "Glacier Locket", "MaxHealthPct", 5),
    ("charm.frostlock_ring", "Charm", "Guard", "Frost", "霜鎖の指輪", "Frostlock Ring", "Tenacity", 12),
    ("charm.frost_breath_charm", "Charm", "Guard", "Frost", "白息の御守り", "Frost-Breath Charm", "HealthRegen", 2),
    ("charm.north_sky_charm", "Charm", "Resonance", "Frost", "北空の飾り", "North-Sky Charm", "Haste", 4),
    ("charm.sparkcase_pendant", "Charm", "Offense", "Flame", "火種の垂飾り", "Sparkcase Pendant", "AttackFlat", 5),
    ("charm.melt_ore_locket", "Charm", "Offense", "Flame", "熔鉱のロケット", "Melt-Ore Locket", "CritDamagePct", 14),
    ("charm.flarewheel_brooch", "Charm", "Offense", "Flame", "火輪のブローチ", "Flarewheel Brooch", "AttackRangePct", 5),
    ("charm.tinder_band", "Charm", "Offense", "Flame", "火口の腕輪", "Tinder Band", "AttackSpeedPct", 5),
    ("charm.sunrise_locket", "Charm", "Resonance", "Light", "朝焼けのロケット", "Sunrise Locket", "LightAmp", 10),
    ("charm.kindness_beads", "Charm", "Resonance", "Light", "慈愛の数珠", "Beads of Kindness", "HealPower", 5),
    ("charm.sanctuary_seal", "Charm", "Guard", "Light", "聖域の印章", "Sanctuary Seal", "ShieldPower", 5),
    ("charm.broad_day_charm", "Charm", "Resonance", "Light", "白昼の御守り", "Broad-Daylight Charm", "PowerFlat", 6),
    ("charm.night_owl_charm", "Charm", "Offense", "Dark", "梟の御守り", "Night-Owl Charm", "CritChancePct", 3),
    ("charm.grave_bell_ring", "Charm", "Offense", "Dark", "墓鈴の指輪", "Grave-Bell Ring", "CritDamagePct", 12),
    ("charm.funeral_band", "Charm", "Offense", "Dark", "葬列の腕輪", "Funeral Band", "AttackFlat", 6),
    ("charm.dusk_mist_band", "Charm", "Resonance", "Dark", "宵霧の腕輪", "Dusk-Mist Band", "MoveSpeedPct", 4),
    ("charm.heavy_chain_charm", "Charm", "Guard", "Guard", "重鎖の飾り", "Heavy-Chain Charm", "Armor", 8),
    ("charm.immovable_ring", "Charm", "Guard", "Guard", "不動の指輪", "Immovable Ring", "MaxHealthFlat", 25),
    ("charm.wellspring_amulet", "Charm", "Guard", "Guard", "湧き泉の護符", "Wellspring Amulet", "HealthRegen", 3),
    ("charm.watchpost_badge", "Charm", "Guard", "Guard", "見張り番の徽章", "Watchpost Badge", "ShieldPower", 5),
    ("charm.tailwind_plume", "Charm", "Offense", "Gale", "追風の羽飾り", "Tailwind Plume", "AttackSpeedPct", 4),
    ("charm.skylark_bell", "Charm", "Resonance", "Gale", "雲雀の鈴", "Skylark Bell", "Haste", 6),
    ("charm.far_flight_charm", "Charm", "Offense", "Gale", "遠翔の御守り", "Far-Flight Charm", "AttackRangePct", 6),
    ("charm.gust_band", "Charm", "Offense", "Gale", "疾風の腕輪", "Gust Band", "AttackFlat", 5),
    ("charm.healing_sachet", "Charm", "Resonance", "Mend", "癒しの香袋", "Healing Sachet", "PowerFlat", 5),
    ("charm.restful_heart_ring", "Charm", "Resonance", "Mend", "心休まる指輪", "Restful-Heart Ring", "Haste", 5),
    ("charm.lotus_bloom_seal", "Charm", "Guard", "Mend", "蓮華の印", "Lotus-Bloom Seal", "MaxHealthFlat", 20),
    ("charm.salve_case_charm", "Charm", "Guard", "Mend", "薬盒の飾り", "Salve-Case Charm", "ShieldPower", 5),
    ("charm.packfang_pendant", "Charm", "Offense", "Summon", "群牙の垂飾り", "Packfang Pendant", "AttackFlat", 6),
    ("charm.bone_whistle_charm", "Charm", "Resonance", "Summon", "骨笛の飾り", "Bone-Whistle Charm", "Haste", 4),
    ("charm.wildheart_locket", "Charm", "Offense", "Summon", "野性のロケット", "Wildheart Locket", "CritChancePct", 3),
    ("charm.rein_ring", "Charm", "Offense", "Summon", "手綱の指輪", "Rein Ring", "AttackSpeedPct", 3),
    ("charm.unaddressed_letter", "Charm", "Resonance", "Memory", "宛先なき手紙", "Unaddressed Letter", "DarkAmp", 8),
    ("charm.old_coin_charm", "Charm", "Resonance", "Memory", "古銭の御守り", "Old-Coin Charm", "DarkAmp", 10),
    ("charm.forgotten_name_charm", "Charm", "Resonance", "Memory", "忘れ名の飾り", "Forgotten-Name Charm", "PowerFlat", 6),
    ("charm.memory_bookmark", "Charm", "Resonance", "Memory", "思い出の栞", "Bookmark of Memories", "PowerFlat", 7),
    # --- Head (40) ---
    ("head.broad_brim_hat", "Head", "Offense", "Plain", "つばの広い帽子", "Broad-Brim Hat", "AttackFlat", 5),
    ("head.cotton_hood", "Head", "Offense", "Plain", "木綿の頭巾", "Cotton Hood", "CritDamagePct", 10),
    ("head.straw_hat", "Head", "Offense", "Plain", "麦わら帽子", "Straw Hat", "AttackSpeedPct", 3),
    ("head.sweatband", "Head", "Guard", "Plain", "汗の鉢巻", "Sweatband", "HealthRegen", 2),
    ("head.aurora_circlet", "Head", "Resonance", "Frost", "極光の額冠", "Aurora Circlet", "ColdAmp", 10),
    ("head.snowlight_hood", "Head", "Guard", "Frost", "雪明りの頭巾", "Snowlight Hood", "MaxHealthPct", 5),
    ("head.everfrost_hood", "Head", "Guard", "Frost", "不凍の頭巾", "Everfrost Hood", "HealthRegen", 3),
    ("head.icicle_crown", "Head", "Guard", "Frost", "氷柱の冠", "Icicle Crown", "Armor", 6),
    ("head.brazier_crown", "Head", "Offense", "Flame", "火鉢の冠", "Brazier Crown", "FireAmp", 10),
    ("head.flashfire_band", "Head", "Offense", "Flame", "火閃の鉢巻", "Flashfire Band", "AttackSpeedPct", 5),
    ("head.scorched_mask", "Head", "Offense", "Flame", "焦土の面", "Scorched-Earth Mask", "CritChancePct", 3),
    ("head.forge_hood", "Head", "Guard", "Flame", "鍛冶場の頭巾", "Forge-Hood", "MaxHealthPct", 5),
    ("head.hope_circlet", "Head", "Resonance", "Light", "望みの額冠", "Circlet of Hope", "LightAmp", 10),
    ("head.nurses_veil", "Head", "Resonance", "Light", "看護のヴェール", "Nurse's Veil", "HealPower", 5),
    ("head.first_light_band", "Head", "Resonance", "Light", "夜明けの鉢巻", "First-Light Band", "Haste", 5),
    ("head.clear_gaze_glasses", "Head", "Offense", "Light", "澄眼の眼鏡", "Clear-Gaze Glasses", "CritChancePct", 3),
    ("head.nightmare_visage", "Head", "Offense", "Dark", "悪夢の面", "Nightmare Visage", "DarkAmp", 10),
    ("head.funeral_crown", "Head", "Offense", "Dark", "葬列の冠", "Funeral Crown", "CritDamagePct", 14),
    ("head.shroud_hood", "Head", "Offense", "Dark", "死装束の頭巾", "Shroud Hood", "AttackSpeedPct", 4),
    ("head.moth_wing_mask", "Head", "Resonance", "Dark", "蛾翅の仮面", "Moth-Wing Mask", "MoveSpeedPct", 3),
    ("head.siege_helm", "Head", "Guard", "Guard", "攻城戦の兜", "Siege Helm", "Armor", 8),
    ("head.locked_visor", "Head", "Guard", "Guard", "閉扉の面頬", "Locked Visor", "Tenacity", 12),
    ("head.patrol_hood", "Head", "Guard", "Guard", "巡視の頭巾", "Patrol Hood", "HealthRegen", 2),
    ("head.heartwood_circlet", "Head", "Guard", "Guard", "心材の冠", "Heartwood Circlet", "MaxHealthFlat", 20),
    ("head.sprinting_veil", "Head", "Resonance", "Gale", "疾走のヴェール", "Sprinting Veil", "Haste", 6),
    ("head.kite_mask", "Head", "Offense", "Gale", "凧の面", "Kite Mask", "AttackSpeedPct", 3),
    ("head.wind_crest_band", "Head", "Offense", "Gale", "風紋の鉢巻", "Wind-Crest Band", "AttackFlat", 6),
    ("head.far_horizon_cap", "Head", "Offense", "Gale", "遠地平の帽子", "Far-Horizon Cap", "CritDamagePct", 10),
    ("head.pilgrim_doctor_hat", "Head", "Guard", "Mend", "巡礼医の帽子", "Pilgrim-Doctor Hat", "MaxHealthPct", 5),
    ("head.balm_band", "Head", "Resonance", "Mend", "香膏の鉢巻", "Balm Band", "PowerFlat", 5),
    ("head.soothing_wreath", "Head", "Guard", "Mend", "癒やしの花冠", "Soothing Wreath", "ShieldPower", 5),
    ("head.quiet_prayer_veil", "Head", "Guard", "Mend", "静祷のヴェール", "Quiet-Prayer Veil", "MaxHealthFlat", 20),
    ("head.beast_ear_hood", "Head", "Offense", "Summon", "獣耳の頭巾", "Beast-Ear Hood", "AttackFlat", 6),
    ("head.falconers_hood", "Head", "Offense", "Summon", "鷹狩りの頭巾", "Falconer's Hood", "CritDamagePct", 10),
    ("head.den_mother_circlet", "Head", "Guard", "Summon", "巣守りの額冠", "Den-Mother Circlet", "MaxHealthPct", 5),
    ("head.calling_whistle_band", "Head", "Resonance", "Summon", "呼び笛の鉢巻", "Calling-Whistle Band", "Haste", 5),
    ("head.nostalgia_band", "Head", "Resonance", "Memory", "追憶の鉢巻", "Nostalgia Band", "PowerFlat", 6),
    ("head.old_tales_hood", "Head", "Resonance", "Memory", "昔話の頭巾", "Old-Tales Hood", "DarkAmp", 8),
    ("head.star_calendar_cap", "Head", "Resonance", "Memory", "星暦の帽子", "Star-Calendar Cap", "Haste", 6),
    ("head.relic_eye_monocle", "Head", "Offense", "Memory", "遺物眼の単眼鏡", "Relic-Eye Monocle", "CritChancePct", 3),
    # --- Hands (40) ---
    ("hands.work_gloves", "Hands", "Offense", "Plain", "作業手袋", "Work Gloves", "AttackFlat", 5),
    ("hands.tumblers_gloves", "Hands", "Offense", "Plain", "軽業の手袋", "Tumbler's Gloves", "AttackSpeedPct", 5),
    ("hands.iron_thumb_gauntlet", "Hands", "Offense", "Plain", "鉄親指の籠手", "Iron-Thumb Gauntlet", "CritDamagePct", 12),
    ("hands.cotton_wraps", "Hands", "Resonance", "Plain", "木綿の手巻き", "Cotton Wraps", "PowerFlat", 5),
    ("hands.rimeguard_gauntlets", "Hands", "Resonance", "Frost", "霧氷の籠手", "Rimeguard Gauntlets", "ColdAmp", 10),
    ("hands.frozen_grip", "Hands", "Guard", "Frost", "氷結の握り", "Frozen Grip", "Tenacity", 10),
    ("hands.snow_weight_gloves", "Hands", "Offense", "Frost", "雪重りの手袋", "Snow-Weight Gloves", "AttackFlat", 6),
    ("hands.winter_ready_gloves", "Hands", "Guard", "Frost", "冬支度の手袋", "Winter-Ready Gloves", "HealthRegen", 3),
    ("hands.volcanic_grips", "Hands", "Offense", "Flame", "火山岩の握り", "Volcanic Grips", "FireAmp", 10),
    ("hands.fire_tong_mitts", "Hands", "Offense", "Flame", "火箸の指なし", "Fire-Tong Mitts", "AttackSpeedPct", 4),
    ("hands.slag_knuckles", "Hands", "Offense", "Flame", "鉱滓の拳当て", "Slag Knuckles", "CritDamagePct", 14),
    ("hands.brand_iron_fingerless", "Hands", "Offense", "Flame", "焼印の指抜き", "Brand-Iron Fingerless", "CritChancePct", 3),
    ("hands.morninglight_gloves", "Hands", "Resonance", "Light", "朝光の手袋", "Morninglight Gloves", "LightAmp", 8),
    ("hands.first_aid_mitts", "Hands", "Resonance", "Light", "手当ての指なし", "First-Aid Mitts", "HealPower", 5),
    ("hands.gospel_gloves", "Hands", "Resonance", "Light", "福音の手袋", "Gospel Gloves", "PowerFlat", 5),
    ("hands.sunhold_bands", "Hands", "Resonance", "Light", "日向の腕輪", "Sunhold Bands", "Haste", 5),
    ("hands.gravediggers_claws", "Hands", "Offense", "Dark", "墓掘りの爪", "Gravedigger's Claws", "DarkAmp", 10),
    ("hands.assassins_fingerless", "Hands", "Offense", "Dark", "暗殺者の指抜き", "Assassin's Fingerless", "CritDamagePct", 12),
    ("hands.gloomfang_knuckles", "Hands", "Offense", "Dark", "闇牙の拳当て", "Gloomfang Knuckles", "AttackSpeedPct", 5),
    ("hands.night_rose_gloves", "Hands", "Resonance", "Dark", "夜薔薇の手袋", "Night-Rose Gloves", "MoveSpeedPct", 3),
    ("hands.bastion_mitts", "Hands", "Guard", "Guard", "砦の指なし", "Bastion Mitts", "Armor", 8),
    ("hands.anvil_gauntlets", "Hands", "Guard", "Guard", "金床の籠手", "Anvil Gauntlets", "Armor", 10),
    ("hands.steadfast_grips", "Hands", "Guard", "Guard", "不動の握り", "Steadfast Grips", "MaxHealthFlat", 25),
    ("hands.iron_will_wraps", "Hands", "Guard", "Guard", "鉄意志の手巻き", "Iron-Will Wraps", "Tenacity", 15),
    ("hands.quicksilver_bands", "Hands", "Offense", "Gale", "迅銀の腕輪", "Quicksilver Bands", "AttackSpeedPct", 3),
    ("hands.galefinger_gloves", "Hands", "Offense", "Gale", "風指の手袋", "Galefinger Gloves", "AttackFlat", 6),
    ("hands.hawking_gloves", "Hands", "Offense", "Gale", "鷹寄せの手袋", "Hawking Gloves", "CritChancePct", 3),
    ("hands.windstep_wraps", "Hands", "Resonance", "Gale", "風歩きの手巻き", "Windstep Wraps", "MoveSpeedPct", 4),
    ("hands.repair_gloves", "Hands", "Resonance", "Mend", "繕いの手袋", "Repair Gloves", "PowerFlat", 6),
    ("hands.supporting_gloves", "Hands", "Guard", "Mend", "支えの手袋", "Supporting Gloves", "ShieldPower", 5),
    ("hands.splint_wraps", "Hands", "Guard", "Mend", "副木の手巻き", "Splint Wraps", "MaxHealthFlat", 20),
    ("hands.spring_breeze_grips", "Hands", "Resonance", "Mend", "春風の握り", "Spring-Breeze Grips", "Haste", 4),
    ("hands.beasthide_gloves", "Hands", "Offense", "Summon", "獣皮の手袋", "Beasthide Gloves", "AttackFlat", 6),
    ("hands.tamers_mitts", "Hands", "Offense", "Summon", "調教の指なし", "Tamer's Mitts", "AttackSpeedPct", 4),
    ("hands.alpha_grips", "Hands", "Offense", "Summon", "頭領の握り", "Alpha Grips", "CritDamagePct", 14),
    ("hands.kennel_keeper_gloves", "Hands", "Guard", "Summon", "番犬飼いの手袋", "Kennel-Keeper Gloves", "MaxHealthPct", 5),
    ("hands.palimpsest_gloves", "Hands", "Resonance", "Memory", "重写本の手袋", "Palimpsest Gloves", "PowerFlat", 7),
    ("hands.keepsake_bands", "Hands", "Resonance", "Memory", "形見の腕輪", "Keepsake Bands", "Haste", 5),
    ("hands.tale_teller_gloves", "Hands", "Offense", "Memory", "昔語りの手袋", "Tale-Teller Gloves", "CritChancePct", 3),
    ("hands.duskthread_wraps", "Hands", "Resonance", "Memory", "夕糸の手巻き", "Duskthread Wraps", "DarkAmp", 8),
    # --- Feet (40) ---
    ("feet.hobnail_boots", "Feet", "Guard", "Plain", "鉄釘の長靴", "Hobnail Boots", "Armor", 6),
    ("feet.everyday_shoes", "Feet", "Offense", "Plain", "日常の靴", "Everyday Shoes", "AttackFlat", 6),
    ("feet.work_boots", "Feet", "Offense", "Plain", "仕事靴", "Work Boots", "CritDamagePct", 10),
    ("feet.earth_stained_sandals", "Feet", "Resonance", "Plain", "土の付いた草鞋", "Earth-Stained Sandals", "Haste", 4),
    ("feet.blizzard_gaiters", "Feet", "Resonance", "Frost", "吹雪の脚絆", "Blizzard Gaiters", "ColdAmp", 10),
    ("feet.floe_skimmer_shoes", "Feet", "Guard", "Frost", "流氷滑りの靴", "Floe-Skimmer Shoes", "MaxHealthPct", 5),
    ("feet.ice_road_boots", "Feet", "Guard", "Frost", "氷道の長靴", "Ice-Road Boots", "HealthRegen", 2),
    ("feet.frostwind_slippers", "Feet", "Resonance", "Frost", "霜風の上履き", "Frostwind Slippers", "MoveSpeedPct", 3),
    ("feet.scorching_footwear", "Feet", "Offense", "Flame", "灼熱の履き物", "Scorching Footwear", "FireAmp", 10),
    ("feet.firewalk_sandals", "Feet", "Offense", "Flame", "火渡りの草鞋", "Firewalk Sandals", "AttackSpeedPct", 4),
    ("feet.cinder_trail_shoes", "Feet", "Offense", "Flame", "燼跡の靴", "Cinder-Trail Shoes", "AttackFlat", 5),
    ("feet.smelter_boots", "Feet", "Offense", "Flame", "溶鉱の長靴", "Smelter Boots", "CritDamagePct", 12),
    ("feet.sunpath_sandals", "Feet", "Resonance", "Light", "日の道の草鞋", "Sunpath Sandals", "LightAmp", 10),
    ("feet.pilgrim_light_shoes", "Feet", "Resonance", "Light", "巡礼灯の靴", "Pilgrim-Light Shoes", "HealPower", 5),
    ("feet.morningstar_shoes", "Feet", "Resonance", "Light", "暁星の靴", "Morningstar Shoes", "Haste", 5),
    ("feet.beacon_steps", "Feet", "Resonance", "Light", "灯台の足取り", "Beacon Steps", "MoveSpeedPct", 3),
    ("feet.graveshade_boots", "Feet", "Offense", "Dark", "墓影の長靴", "Graveshade Boots", "DarkAmp", 10),
    ("feet.soundless_shoes", "Feet", "Offense", "Dark", "無音の靴", "Soundless Shoes", "AttackFlat", 6),
    ("feet.shadowstrider_boots", "Feet", "Offense", "Dark", "影渡りの靴", "Shadowstrider Boots", "CritDamagePct", 14),
    ("feet.torchless_shoes", "Feet", "Guard", "Dark", "灯無しの靴", "Torchless Shoes", "Tenacity", 12),
    ("feet.gatehouse_sabatons", "Feet", "Guard", "Guard", "城門の鉄鞋", "Gatehouse Sabatons", "Armor", 9),
    ("feet.mountain_boots", "Feet", "Guard", "Guard", "山岳の重靴", "Mountain Boots", "Armor", 10),
    ("feet.night_watch_boots", "Feet", "Guard", "Guard", "夜警の長靴", "Night-Watch Boots", "MaxHealthPct", 5),
    ("feet.patrol_sandals", "Feet", "Guard", "Guard", "巡回兵の草鞋", "Patrol-Soldier Sandals", "HealthRegen", 3),
    ("feet.updraft_greaves", "Feet", "Resonance", "Gale", "上昇風の脛当て", "Updraft Greaves", "Haste", 5),
    ("feet.breeze_runners", "Feet", "Resonance", "Gale", "微風の走り靴", "Breeze Runners", "Haste", 6),
    ("feet.swallow_flight_boots", "Feet", "Offense", "Gale", "燕飛びの靴", "Swallow-Flight Boots", "AttackFlat", 5),
    ("feet.long_stride_boots", "Feet", "Offense", "Gale", "大股の長靴", "Long-Stride Boots", "AttackRangePct", 5),
    ("feet.hospice_slippers", "Feet", "Resonance", "Mend", "病院の上履き", "Hospice Slippers", "PowerFlat", 5),
    ("feet.comfort_sandals", "Feet", "Guard", "Mend", "安らぎの草鞋", "Comfort Sandals", "ShieldPower", 5),
    ("feet.herb_garden_shoes", "Feet", "Resonance", "Mend", "薬草園の靴", "Herb-Garden Shoes", "Haste", 4),
    ("feet.night_nurse_boots", "Feet", "Guard", "Mend", "夜看の長靴", "Night-Nurse Boots", "MaxHealthFlat", 20),
    ("feet.pack_trot_boots", "Feet", "Offense", "Summon", "群れ駆けの長靴", "Pack-Trot Boots", "AttackSpeedPct", 4),
    ("feet.beast_path_shoes", "Feet", "Offense", "Summon", "獣径の靴", "Beast-Path Shoes", "AttackFlat", 6),
    ("feet.howling_greaves", "Feet", "Offense", "Summon", "遠吠えの脛当て", "Howling Greaves", "CritDamagePct", 10),
    ("feet.den_warden_sabatons", "Feet", "Guard", "Summon", "巣番の鉄鞋", "Den-Warden Sabatons", "MaxHealthPct", 5),
    ("feet.old_road_boots", "Feet", "Resonance", "Memory", "旧道の長靴", "Old-Road Boots", "PowerFlat", 6),
    ("feet.twilight_stroll_shoes", "Feet", "Resonance", "Memory", "黄昏散策の靴", "Twilight-Stroll Shoes", "DarkAmp", 8),
    ("feet.remembered_steps", "Feet", "Resonance", "Memory", "思い出の足取り", "Remembered Steps", "Haste", 6),
    ("feet.once_worn_sandals", "Feet", "Resonance", "Memory", "昔履きの草鞋", "Once-Worn Sandals", "MoveSpeedPct", 3),
]


def main():
    src = open(SRC, encoding="utf-8").read()
    bases = re.findall(
        r'new BaseDef\("([^"]+)", Slot\.(\w+), Line\.(\w+), new Txt\("([^"]*)", "([^"]*)"\), '
        r"Stat\.(\w+), (-?\d+)\)", src)
    assert len(bases) == 360, len(bases)
    errs = []
    ids = {b[0] for b in bases}
    for i in ids:
        if i not in TAGS:
            errs.append("untagged: " + i)
    for k in TAGS:
        if k not in ids:
            errs.append("tag for unknown id: " + k)
    slots = sorted(set(b[1] for b in bases))
    lines = sorted(set(b[2] for b in bases))
    per = collections.Counter()
    combo = collections.defaultdict(set)
    vals = collections.defaultdict(list)
    ja = collections.Counter(b[3] for b in bases)
    en = collections.Counter(b[4] for b in bases)
    for b in bases:
        f = TAGS.get(b[0])
        if not f:
            continue
        per[(b[1], f)] += 1
        combo[(b[1], f)].add((b[5], int(b[6])))
        vals[b[5]].append(int(b[6]))
    for s in slots:
        for f in FAM:
            if per[(s, f)] != 6:
                errs.append("slot %s family %s: %d existing (want 6)" % (s, f, per[(s, f)]))
    for b in bases:
        f = TAGS.get(b[0])
        if not f:
            continue
        if sum(1 for x in bases if x[1] == b[1] and TAGS.get(x[0]) == f
               and x[5] == b[5] and int(x[6]) == int(b[6])) > 1:
            key = "dup existing %s/%s (%s,%s)" % (b[1], f, b[5], b[6])
            if key not in errs:
                errs.append(key)
    # --- new bases ---
    if len(NEW) != 240:
        errs.append("NEW has %d entries (want 240)" % len(NEW))
    newper = collections.Counter()
    seen_ids = set()
    for e in NEW:
        eid, slot, line, fam, ja_n, en_n, stat, val = e
        if not re.fullmatch(r"[a-z]+\.[a-z0-9_]+", eid):
            errs.append("bad id: " + eid)
        if eid in ids or eid in seen_ids:
            errs.append("duplicate id: " + eid)
        seen_ids.add(eid)
        if slot not in slots:
            errs.append("bad slot: " + eid)
        if not eid.startswith(slot.lower() + "."):
            errs.append("id prefix mismatch: " + eid)
        if line not in lines:
            errs.append("bad line: " + eid)
        if fam not in FAM:
            errs.append("bad family: " + eid)
        if stat not in vals:
            errs.append("stat not used by existing bases: " + eid)
        elif not (min(vals[stat]) <= val <= max(vals[stat])):
            errs.append("value %d outside %s range %d-%d: %s" % (val, stat, min(vals[stat]), max(vals[stat]), eid))
        if (stat, val) in combo[(slot, fam)]:
            errs.append("implicit (stat,value) clash in %s/%s: %s" % (slot, fam, eid))
        combo[(slot, fam)].add((stat, val))
        newper[slot] += 1
        if not ja_n or not en_n or re.search(r"\d", ja_n + en_n):
            errs.append("empty name or digits: " + eid)
        if len(ja_n) > 12:
            errs.append("nameJa too long (%d): %s" % (len(ja_n), eid))
        ja[ja_n] += 1
        en[en_n] += 1
    for s in slots:
        if newper[s] != 40:
            errs.append("slot %s: %d new (want 40)" % (s, newper[s]))
    for d in ([n for n, c in ja.items() if c > 1], [n for n, c in en.items() if c > 1]):
        if d:
            errs.append("duplicate names: %s" % d[:10])
    famslot = collections.Counter()
    for b in bases:
        famslot[(b[1], TAGS.get(b[0]))] += 1
    for e in NEW:
        famslot[(e[1], e[3])] += 1
    bad = [(k, v) for k, v in famslot.items() if v != 10]
    if bad:
        errs.append("slot x family != 10: %s" % bad[:8])
    if errs:
        print("ERRORS (%d):" % len(errs))
        for e in errs[:80]:
            print(" ", e)
        sys.exit(1)
    json.dump({b[0]: TAGS[b[0]] for b in bases},
              open("tools/lowrarity/base-families.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1, sort_keys=True)
    keys = ["id", "slot", "line", "family", "nameJa", "nameEn", "stat", "value"]
    out = [{k: v for k, v in zip(keys, e)} for e in NEW]
    json.dump(out, open("tools/lowrarity/new-bases.json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("OK: base-families.json (360 tags), new-bases.json (%d bases)" % len(out))


if __name__ == "__main__":
    main()
