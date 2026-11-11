#!/usr/bin/env python3
"""Build and validate the unified Ally + Enemy Character Stats Database.

Reads authoritative definitions from ``data/master/*.json``, ``data/loc/*/strings.json``,
and ``presets/characters_dna.json`` without mutating MasterData, and generates:
- ``data/stats/character_stats_db.json`` (validated against Draft 2020-12 schema)
- ``data/stats/character_stats_db.csv`` (unified Ally + Enemy flat spreadsheet table)
- ``data/stats/character_stats_db.sqlite`` (normalized tables + ``v_all_combat_units`` view)
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import os
import sqlite3
import sys
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
from typing import Any

import jsonschema

SCHEMA_VERSION = "1.0.0"
DATABASE_NAME = "project_tower_character_stats_db"

SOURCE_REL_PATHS = (
    "data/master/characters.json",
    "data/master/enemies.json",
    "data/master/config.json",
    "data/master/portrait_stages.json",
    "data/master/stages.json",
    "data/loc/zh-TW/strings.json",
    "data/loc/en/strings.json",
    "presets/characters_dna.json",
)

ALLY_SNAPSHOT_TIERS: tuple[tuple[str, int, int], ...] = (
    ("lv01_aw0_base", 1, 0),
    ("lv20_aw0_cap", 20, 0),
    ("lv20_aw1_balanced", 20, 1),
    ("lv35_aw2_trained", 35, 2),
    ("lv50_aw4_awakened", 50, 4),
    ("lv70_aw5_max", 70, 5),
)


def sha256_file(path: Path) -> str:
    """Return lowercase hexadecimal SHA-256 digest of a file."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path) -> Any:
    """Load UTF-8 JSON file."""
    return json.loads(path.read_text(encoding="utf-8"))


def sec_to_ticks(sec: float, tps: int) -> int:
    """Match C# UnitTemplate.SecToTicks (MidpointRounding.AwayFromZero)."""
    val = Decimal(str(sec)) * Decimal(tps)
    return int(val.quantize(Decimal("1"), rounding=ROUND_HALF_UP))


def round2(value: float) -> float:
    """Round float cleanly to 2 decimal places for deterministic JSON/CSV output."""
    return round(value, 2)


def compute_combat_metrics(
    battle: dict[str, Any], hp: int, atk: int, tps: int
) -> dict[str, Any]:
    """Compute engine-aligned tick/milli combat stats shared by allies and enemies."""
    interval_sec = float(battle["attackIntervalSec"])
    interval_ticks = max(1, sec_to_ticks(interval_sec, tps))
    actual_interval_sec = interval_ticks / tps
    dps = round2(atk / actual_interval_sec) if actual_interval_sec > 0 else 0.0
    unit_range = int(battle["range"])
    move_speed = int(battle["moveSpeed"])
    return {
        "hp": int(hp),
        "atk": int(atk),
        "dps": dps,
        "range": unit_range,
        "range_milli": unit_range * 1000,
        "attack_interval_sec": interval_sec,
        "attack_interval_ticks": interval_ticks,
        "move_speed": move_speed,
        "speed_milli_per_tick": (move_speed * 1000) // tps,
        "knockbacks": max(0, int(battle.get("knockbacks", 0))),
        "target": str(battle.get("target", "single")),
    }


def is_healer_ally(char_def: dict[str, Any]) -> bool:
    """Match C# BattleSimulator.IsHealer classification."""
    cid = str(char_def["id"])
    battle = char_def["battle"]
    skill = battle.get("skill") or {}
    traits = [str(t) for t in (battle.get("traits") or [])]
    if str(skill.get("effectType", "")).lower() == "heal":
        return True
    if "healer" in traits:
        return True
    return "healer" in cid.lower()


def parse_unlock_requirements(unlock_node: dict[str, Any] | None) -> tuple[int, int]:
    """Extract (min_level, min_awaken) required to unlock a portrait stage."""
    if not unlock_node:
        return (1, 0)
    req_lv, req_aw = 1, 0
    nodes = unlock_node.get("of", []) if unlock_node.get("type") == "all" else [unlock_node]
    for cond in nodes:
        ctype = cond.get("type")
        val = int(cond.get("value", 0))
        if ctype == "char_level_gte":
            req_lv = max(req_lv, val)
        elif ctype == "char_awaken_gte":
            req_aw = max(req_aw, val)
    return (req_lv, req_aw)


def compute_ally_stats(
    base_hp: int,
    base_atk: int,
    per_level_pct: int,
    level: int,
    awaken_pct: int,
    stage_hp_pct: int,
    stage_atk_pct: int,
) -> tuple[int, int]:
    """Match C# GrowthRules.ComputeStats two-step integer truncation order."""
    level_pct = 100 + per_level_pct * (level - 1)
    hp = ((base_hp * level_pct) // 100) * (100 + awaken_pct + stage_hp_pct) // 100
    atk = ((base_atk * level_pct) // 100) * (100 + awaken_pct + stage_atk_pct) // 100
    return int(hp), int(atk)


def resolve_loc_name(raw_name: str, loc_map: dict[str, str], fallback: str) -> str:
    """Resolve ``loc:key`` entries via localization string maps."""
    if raw_name.startswith("loc:"):
        key = raw_name[4:]
        if key in loc_map:
            return loc_map[key]
    return fallback


def build_allies(
    repo_root: Path,
    characters: list[dict[str, Any]],
    portrait_stages: list[dict[str, Any]],
    config: dict[str, Any],
    loc_tw: dict[str, str],
    loc_en: dict[str, str],
    dna_by_id: dict[str, dict[str, Any]],
) -> list[dict[str, Any]]:
    """Build complete structured records for all active playable ally heroines."""
    growth = config["growth"]
    tps = int(config["battle"]["ticksPerSecond"])
    curve_map: dict[str, int] = growth["statCurvePerLevelPct"]
    awaken_bonuses: list[int] = growth["awakenStatBonusPct"]

    stages_by_char: dict[str, list[dict[str, Any]]] = {}
    for ps in portrait_stages:
        req_lv, req_aw = parse_unlock_requirements(ps.get("unlock"))
        bonus = ps.get("statBonus") or {}
        stages_by_char.setdefault(ps["character"], []).append(
            {
                "stage_id": str(ps["id"]),
                "order": int(ps["order"]),
                "req_level": req_lv,
                "req_awaken": req_aw,
                "atk_pct": int(bonus.get("atkPct", 0)),
                "hp_pct": int(bonus.get("hpPct", 0)),
            }
        )
    for stage_list in stages_by_char.values():
        stage_list.sort(key=lambda x: x["order"])

    allies: list[dict[str, Any]] = []
    for ch in characters:
        cid = str(ch["id"])
        battle = ch["battle"]
        dna_entry = dna_by_id.get(cid, {})
        curve_name = str(battle["growthCurve"])
        per_level = int(curve_map[curve_name])

        base_hp = int(battle["hp"])
        base_atk = int(battle["atk"])
        cost = int(battle["cost"])
        cd_sec = float(battle["cooldownSec"])

        common = compute_combat_metrics(battle, base_hp, base_atk, tps)
        base_stats = {
            "cost": cost,
            "cooldown_sec": cd_sec,
            "cooldown_ticks": sec_to_ticks(cd_sec, tps),
            **common,
            "hp_per_cost": round2(base_hp / cost),
            "dps_per_cost": round2(common["dps"] / cost),
            "traits": [str(t) for t in (battle.get("traits") or [])],
        }

        healer_flag = is_healer_ally(ch)
        interval_ticks = common["attack_interval_ticks"]
        actual_interval_sec = interval_ticks / tps
        aura_reach = common["range"] if common["range"] > 0 else 120
        healer_aura = {
            "is_healer": healer_flag,
            "aura_heal_base": base_atk if healer_flag else 0,
            "aura_heal_per_sec_base": round2(base_atk / actual_interval_sec) if healer_flag else 0.0,
            "aura_interval_ticks": interval_ticks if healer_flag else 0,
            "aura_reach_units": aura_reach if healer_flag else 0,
        }

        raw_skill = battle["skill"]
        skill_charge = float(raw_skill["chargeSec"])
        skill = {
            "id": str(raw_skill["id"]),
            "name": str(raw_skill["name"]),
            "charge_sec": skill_charge,
            "charge_ticks": max(1, sec_to_ticks(skill_charge, tps)),
            "effect_type": str(raw_skill["effectType"]),
            "value": int(raw_skill["value"]),
            "duration_sec": float(raw_skill.get("durationSec", 0.0)),
            "range": int(raw_skill.get("range", 0)),
        }

        char_stages = stages_by_char.get(cid, [])
        snapshots: dict[str, dict[str, Any]] = {}
        for snap_key, lv, aw in ALLY_SNAPSHOT_TIERS:
            awaken_pct = awaken_bonuses[aw] if aw < len(awaken_bonuses) else 0
            unlocked = [
                st for st in char_stages if lv >= st["req_level"] and aw >= st["req_awaken"]
            ]
            best_stage = (
                max(unlocked, key=lambda st: (st["atk_pct"] + st["hp_pct"], st["order"]))
                if unlocked
                else {"stage_id": f"ps_{cid}_s1", "order": 1, "atk_pct": 0, "hp_pct": 0}
            )
            snap_hp, snap_atk = compute_ally_stats(
                base_hp,
                base_atk,
                per_level,
                lv,
                awaken_pct,
                best_stage["hp_pct"],
                best_stage["atk_pct"],
            )
            snap_dps = round2(snap_atk / actual_interval_sec)
            snapshots[snap_key] = {
                "level": lv,
                "awaken": aw,
                "awaken_bonus_pct": awaken_pct,
                "portrait_stage_id": best_stage["stage_id"],
                "portrait_stage_order": best_stage["order"],
                "portrait_atk_pct": best_stage["atk_pct"],
                "portrait_hp_pct": best_stage["hp_pct"],
                "hp": snap_hp,
                "atk": snap_atk,
                "dps": snap_dps,
                "hp_per_cost": round2(snap_hp / cost),
                "dps_per_cost": round2(snap_dps / cost),
                "aura_heal_per_pulse": snap_atk if healer_flag else 0,
                "aura_heal_per_sec": round2(snap_atk / actual_interval_sec) if healer_flag else 0.0,
            }

        sprite_rel = f"client/Assets/StreamingAssets/Battle/Units/{cid}.png"
        allies.append(
            {
                "id": cid,
                "side": "ally",
                "status": "active",
                "names": {
                    "zh_TW": resolve_loc_name(str(ch["name"]), loc_tw, str(dna_entry.get("name_en", cid))),
                    "en": resolve_loc_name(str(ch["name"]), loc_en, str(dna_entry.get("name_en", cid))),
                    "archetype_zh": str(dna_entry.get("name_zh", "")),
                },
                "rarity": str(ch["rarity"]),
                "role": str(ch["role"]),
                "growth_curve": curve_name,
                "per_level_pct": per_level,
                "obtainable_by": [str(x) for x in ch.get("obtainableBy", [])],
                "base_stats": base_stats,
                "healer_aura": healer_aura,
                "skill": skill,
                "portrait_stage_bonuses": char_stages,
                "scaling_snapshots": snapshots,
                "assets": {
                    "battle_sprite_path": sprite_rel,
                    "battle_sprite_exists": (repo_root / sprite_rel).is_file(),
                    "default_costume": str(ch.get("defaultCostume", "")),
                    "story_route": str(ch.get("storyRoute", "")),
                    "default_seed": int(dna_entry.get("default_seed", 0)),
                },
            }
        )
    return allies


def build_enemies(
    repo_root: Path,
    enemies: list[dict[str, Any]],
    stages: list[dict[str, Any]],
    config: dict[str, Any],
    loc_tw: dict[str, str],
    loc_en: dict[str, str],
) -> list[dict[str, Any]]:
    """Build complete structured records for all enemy faction units from MasterData & Loc."""
    tps = int(config["battle"]["ticksPerSecond"])

    appearances_by_enemy: dict[str, list[dict[str, Any]]] = {}
    for st in stages:
        st_id = str(st["id"])
        chapter = int(st["chapter"])
        stat_pct = int(st["enemyStatPct"])
        st_name_zh = resolve_loc_name(str(st.get("name", st_id)), loc_tw, st_id)
        for wave in st.get("waves", []):
            eid = str(wave["enemy"])
            appearances_by_enemy.setdefault(eid, []).append(
                {
                    "stage_id": st_id,
                    "chapter": chapter,
                    "stage_name_zh": st_name_zh,
                    "enemy_stat_pct": stat_pct,
                    "wave_count": int(wave["count"]),
                    "wave_interval_sec": (
                        float(wave["intervalSec"]) if wave.get("intervalSec") is not None else None
                    ),
                }
            )

    out: list[dict[str, Any]] = []
    for en in enemies:
        eid = str(en["id"])
        faction = str(en.get("faction") or "")
        if faction not in ("dark_elf", "goblin", "monster"):
            raise ValueError(f"Enemy '{eid}' missing valid faction in enemies.json: {faction!r}")
        sub_category = str(en.get("subCategory") or "standard")
        scale_category = str(en.get("scaleCategory") or "normal")
        raw_name = str(en.get("name") or f"loc:enemy/{eid}.name")
        battle = en["battle"]
        base_hp = int(battle["hp"])
        base_atk = int(battle["atk"])
        defeat_gold = int(en["defeatGold"])
        unit_type = str(en["type"])

        common = compute_combat_metrics(battle, base_hp, base_atk, tps)
        actual_interval_sec = common["attack_interval_ticks"] / tps
        base_stats = {
            **common,
            "hp_per_gold": round2(base_hp / defeat_gold) if defeat_gold > 0 else 0.0,
        }

        stage_entries: list[dict[str, Any]] = []
        for app in appearances_by_enemy.get(eid, []):
            pct = app["enemy_stat_pct"]
            s_hp = (base_hp * pct) // 100
            s_atk = (base_atk * pct) // 100
            stage_entries.append(
                {
                    **app,
                    "scaled_hp": s_hp,
                    "scaled_atk": s_atk,
                    "scaled_dps": round2(s_atk / actual_interval_sec),
                }
            )

        sprite_rel = f"client/Assets/StreamingAssets/Battle/Units/{eid}.png"
        out.append(
            {
                "id": eid,
                "side": "enemy",
                "faction": faction,
                "sub_category": sub_category,
                "unit_type": unit_type,
                "scale_category": scale_category,
                "lane": "flying" if unit_type == "flying" else "ground",
                "is_boss_tier": scale_category == "boss" or unit_type == "armored",
                "names": {
                    "zh_TW": resolve_loc_name(raw_name, loc_tw, eid),
                    "en": resolve_loc_name(raw_name, loc_en, eid),
                },
                "defeat_gold": defeat_gold,
                "base_stats": base_stats,
                "stage_appearances": stage_entries,
                "assets": {
                    "battle_sprite_path": sprite_rel,
                    "battle_sprite_exists": (repo_root / sprite_rel).is_file(),
                },
            }
        )
    return out


def build_bases(
    repo_root: Path,
    config: dict[str, Any],
    stages: list[dict[str, Any]],
    loc_tw: dict[str, str],
) -> dict[str, Any]:
    """Build ally bastion upgrade table (Lv.1..max) and enemy core stats per stage."""
    growth = config["growth"]
    battle = config["battle"]
    default_hp = int(battle["defaultAllyBaseHp"])
    max_lv = int(growth.get("baseMaxLevel", 20))
    hp_bonus = int(growth.get("baseHpBonusPerLevel", 2500))
    cost_base = int(growth.get("baseUpgradeGoldBase", 500))
    cost_step = int(growth.get("baseUpgradeGoldStep", 500))

    levels = []
    for lv in range(1, max_lv + 1):
        max_hp = default_hp + (lv - 1) * hp_bonus
        next_cost = None if lv >= max_lv else cost_base + (lv - 1) * cost_step
        levels.append({"level": lv, "max_hp": max_hp, "upgrade_gold_cost_to_next": next_cost})

    ally_sprite = "client/Assets/StreamingAssets/Battle/Bases/ally_base.png"
    enemy_sprite = "client/Assets/StreamingAssets/Battle/Bases/enemy_base.png"

    by_stage = [
        {
            "stage_id": str(st["id"]),
            "chapter": int(st["chapter"]),
            "stage_name_zh": resolve_loc_name(str(st.get("name", st["id"])), loc_tw, str(st["id"])),
            "enemy_base_hp": int(st["enemyBaseHp"]),
            "enemy_stat_pct": int(st["enemyStatPct"]),
        }
        for st in stages
    ]

    return {
        "ally_bastion": {
            "default_hp": default_hp,
            "max_level": max_lv,
            "hp_bonus_per_level": hp_bonus,
            "sprite_path": ally_sprite,
            "sprite_exists": (repo_root / ally_sprite).is_file(),
            "levels": levels,
        },
        "enemy_core": {
            "sprite_path": enemy_sprite,
            "sprite_exists": (repo_root / enemy_sprite).is_file(),
            "by_stage": by_stage,
        },
    }


def build_database_dict(repo_root: Path) -> dict[str, Any]:
    """Assemble the complete unified Character Stats Database payload from source files."""
    characters = load_json(repo_root / "data/master/characters.json")
    enemies = load_json(repo_root / "data/master/enemies.json")
    config = load_json(repo_root / "data/master/config.json")
    portrait_stages = load_json(repo_root / "data/master/portrait_stages.json")
    stages = load_json(repo_root / "data/master/stages.json")
    loc_tw = load_json(repo_root / "data/loc/zh-TW/strings.json")
    loc_en = load_json(repo_root / "data/loc/en/strings.json")
    dna_list = load_json(repo_root / "presets/characters_dna.json")

    dna_by_id = {str(d["id"]): d for d in dna_list}
    allies = build_allies(repo_root, characters, portrait_stages, config, loc_tw, loc_en, dna_by_id)
    enemy_entries = build_enemies(repo_root, enemies, stages, config, loc_tw, loc_en)
    active_ids = {a["id"] for a in allies}

    unimplemented = [
        {
            "id": str(d["id"]),
            "side": "ally",
            "status": "planned_unimplemented",
            "name_en": str(d.get("name_en", "")),
            "name_zh": str(d.get("name_zh", "")),
            "default_seed": int(d.get("default_seed", 0)),
            "ref_fn": str(d.get("ref_fn", "")),
        }
        for d in dna_list
        if str(d["id"]) not in active_ids
    ]

    growth = config["growth"]
    battle = config["battle"]
    faction_counts = {"dark_elf": 0, "goblin": 0, "monster": 0}
    for e in enemy_entries:
        faction_counts[e["faction"]] += 1

    return {
        "schema_version": SCHEMA_VERSION,
        "database_name": DATABASE_NAME,
        "master_data_version": int(config.get("masterDataVersion", 1)),
        "source_master_hashes": {rel: sha256_file(repo_root / rel) for rel in SOURCE_REL_PATHS},
        "combat_rules_summary": {
            "ticks_per_second": int(battle["ticksPerSecond"]),
            "default_ally_base_hp": int(battle["defaultAllyBaseHp"]),
            "max_units_per_side": int(battle["maxUnitsPerSide"]),
            "max_battle_sec": int(battle["maxBattleSec"]),
            "knockback_distance": int(battle["knockbackDistance"]),
            "stat_curve_per_level_pct": dict(growth["statCurvePerLevelPct"]),
            "level_cap_by_awaken": [int(x) for x in growth["levelCapByAwaken"]],
            "awaken_stat_bonus_pct": [int(x) for x in growth["awakenStatBonusPct"]],
            "awaken_shard_cost": [int(x) for x in growth["awakenShardCost"]],
            "base_max_level": int(growth.get("baseMaxLevel", 20)),
            "base_hp_bonus_per_level": int(growth.get("baseHpBonusPerLevel", 2500)),
            "base_upgrade_gold_base": int(growth.get("baseUpgradeGoldBase", 500)),
            "base_upgrade_gold_step": int(growth.get("baseUpgradeGoldStep", 500)),
        },
        "summary": {
            "active_allies": len(allies),
            "healer_allies": sum(1 for a in allies if a["healer_aura"]["is_healer"]),
            "planned_unimplemented_allies": len(unimplemented),
            "total_dna_roster": len(dna_list),
            "enemies": len(enemy_entries),
            "enemies_by_faction": faction_counts,
            "stages": len(stages),
        },
        "allies": allies,
        "enemies": enemy_entries,
        "bases": build_bases(repo_root, config, stages, loc_tw),
        "unimplemented_dna_roster": unimplemented,
    }


def render_csv_text(db: dict[str, Any]) -> str:
    """Render unified Ally + Enemy comparison sheet with UTF-8 BOM and LF endings."""
    buf = io.StringIO()
    writer = csv.writer(buf, lineterminator="\n")
    writer.writerow(
        [
            "side",
            "id",
            "faction_or_rarity",
            "role_or_type",
            "lane",
            "name_zh",
            "name_en",
            "cost_or_defeat_gold",
            "hp_base",
            "atk_base",
            "dps_base",
            "range",
            "attack_interval_sec",
            "move_speed",
            "knockbacks",
            "target",
            "is_healer",
            "skill_or_stages",
            "hp_mid_tier",
            "atk_mid_tier",
            "hp_peak_tier",
            "atk_peak_tier",
            "sprite_path",
        ]
    )

    for a in db["allies"]:
        b = a["base_stats"]
        snaps = a["scaling_snapshots"]
        mid = snaps["lv20_aw1_balanced"]
        peak = snaps["lv70_aw5_max"]
        writer.writerow(
            [
                "ally",
                a["id"],
                a["rarity"],
                a["role"],
                "ground",
                a["names"]["zh_TW"],
                a["names"]["en"],
                b["cost"],
                b["hp"],
                b["atk"],
                b["dps"],
                b["range"],
                b["attack_interval_sec"],
                b["move_speed"],
                b["knockbacks"],
                b["target"],
                "true" if a["healer_aura"]["is_healer"] else "false",
                f"{a['skill']['effect_type']}:{a['skill']['name']}",
                mid["hp"],
                mid["atk"],
                peak["hp"],
                peak["atk"],
                a["assets"]["battle_sprite_path"],
            ]
        )

    for e in db["enemies"]:
        b = e["base_stats"]
        apps = e["stage_appearances"]
        stages_desc = ";".join(f"{x['stage_id']}({x['enemy_stat_pct']}%)" for x in apps)
        mid_hp = min(x["scaled_hp"] for x in apps) if apps else b["hp"]
        mid_atk = min(x["scaled_atk"] for x in apps) if apps else b["atk"]
        peak_hp = max(x["scaled_hp"] for x in apps) if apps else b["hp"]
        peak_atk = max(x["scaled_atk"] for x in apps) if apps else b["atk"]
        writer.writerow(
            [
                "enemy",
                e["id"],
                e["faction"],
                e["unit_type"],
                e["lane"],
                e["names"]["zh_TW"],
                e["names"]["en"],
                e["defeat_gold"],
                b["hp"],
                b["atk"],
                b["dps"],
                b["range"],
                b["attack_interval_sec"],
                b["move_speed"],
                b["knockbacks"],
                b["target"],
                "false",
                stages_desc,
                mid_hp,
                mid_atk,
                peak_hp,
                peak_atk,
                e["assets"]["battle_sprite_path"],
            ]
        )

    return "\ufeff" + buf.getvalue()


def write_sqlite_db(sqlite_path: Path, db: dict[str, Any]) -> None:
    """Write normalized SQLite tables plus unified ``v_all_combat_units`` query view."""
    if sqlite_path.exists():
        sqlite_path.unlink()

    conn = sqlite3.connect(str(sqlite_path))
    try:
        cur = conn.cursor()
        cur.executescript(
            """
            CREATE TABLE metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE allies (
                id TEXT PRIMARY KEY,
                name_zh TEXT NOT NULL,
                name_en TEXT NOT NULL,
                archetype_zh TEXT NOT NULL,
                rarity TEXT NOT NULL,
                role TEXT NOT NULL,
                growth_curve TEXT NOT NULL,
                per_level_pct INTEGER NOT NULL,
                cost INTEGER NOT NULL,
                cooldown_sec REAL NOT NULL,
                hp_base INTEGER NOT NULL,
                atk_base INTEGER NOT NULL,
                dps_base REAL NOT NULL,
                range INTEGER NOT NULL,
                attack_interval_sec REAL NOT NULL,
                move_speed INTEGER NOT NULL,
                knockbacks INTEGER NOT NULL,
                target TEXT NOT NULL,
                traits TEXT NOT NULL,
                is_healer INTEGER NOT NULL,
                aura_heal_base INTEGER NOT NULL,
                aura_heal_per_sec_base REAL NOT NULL,
                skill_id TEXT NOT NULL,
                skill_name TEXT NOT NULL,
                skill_effect_type TEXT NOT NULL,
                skill_value INTEGER NOT NULL,
                skill_charge_sec REAL NOT NULL,
                default_seed INTEGER NOT NULL,
                sprite_path TEXT NOT NULL
            );
            CREATE TABLE ally_scaling (
                ally_id TEXT NOT NULL,
                tier_key TEXT NOT NULL,
                level INTEGER NOT NULL,
                awaken INTEGER NOT NULL,
                portrait_stage_id TEXT NOT NULL,
                hp INTEGER NOT NULL,
                atk INTEGER NOT NULL,
                dps REAL NOT NULL,
                aura_heal_per_pulse INTEGER NOT NULL,
                aura_heal_per_sec REAL NOT NULL,
                PRIMARY KEY (ally_id, tier_key),
                FOREIGN KEY (ally_id) REFERENCES allies(id)
            );
            CREATE TABLE enemies (
                id TEXT PRIMARY KEY,
                faction TEXT NOT NULL,
                sub_category TEXT NOT NULL,
                unit_type TEXT NOT NULL,
                scale_category TEXT NOT NULL,
                lane TEXT NOT NULL,
                is_boss_tier INTEGER NOT NULL,
                name_zh TEXT NOT NULL,
                name_en TEXT NOT NULL,
                defeat_gold INTEGER NOT NULL,
                hp_base INTEGER NOT NULL,
                atk_base INTEGER NOT NULL,
                dps_base REAL NOT NULL,
                range INTEGER NOT NULL,
                attack_interval_sec REAL NOT NULL,
                move_speed INTEGER NOT NULL,
                knockbacks INTEGER NOT NULL,
                target TEXT NOT NULL,
                sprite_path TEXT NOT NULL
            );
            CREATE TABLE enemy_stage_scaling (
                enemy_id TEXT NOT NULL,
                stage_id TEXT NOT NULL,
                chapter INTEGER NOT NULL,
                stage_name_zh TEXT NOT NULL,
                enemy_stat_pct INTEGER NOT NULL,
                scaled_hp INTEGER NOT NULL,
                scaled_atk INTEGER NOT NULL,
                scaled_dps REAL NOT NULL,
                wave_count INTEGER NOT NULL,
                wave_interval_sec REAL,
                PRIMARY KEY (enemy_id, stage_id),
                FOREIGN KEY (enemy_id) REFERENCES enemies(id)
            );
            CREATE TABLE ally_bastion_levels (
                level INTEGER PRIMARY KEY,
                max_hp INTEGER NOT NULL,
                upgrade_gold_cost_to_next INTEGER
            );
            CREATE TABLE enemy_core_stages (
                stage_id TEXT PRIMARY KEY,
                chapter INTEGER NOT NULL,
                stage_name_zh TEXT NOT NULL,
                enemy_base_hp INTEGER NOT NULL,
                enemy_stat_pct INTEGER NOT NULL
            );
            CREATE TABLE planned_roster (
                id TEXT PRIMARY KEY,
                name_en TEXT NOT NULL,
                name_zh TEXT NOT NULL,
                default_seed INTEGER NOT NULL,
                ref_fn TEXT NOT NULL
            );
            CREATE VIEW v_all_combat_units AS
                SELECT
                    'ally' AS side,
                    id,
                    rarity AS faction_or_rarity,
                    role AS role_or_type,
                    'ground' AS lane,
                    name_zh,
                    name_en,
                    cost AS economy_value,
                    hp_base,
                    atk_base,
                    dps_base,
                    range,
                    attack_interval_sec,
                    move_speed,
                    knockbacks,
                    target,
                    is_healer,
                    sprite_path
                FROM allies
                UNION ALL
                SELECT
                    'enemy' AS side,
                    id,
                    faction AS faction_or_rarity,
                    unit_type AS role_or_type,
                    lane,
                    name_zh,
                    name_en,
                    defeat_gold AS economy_value,
                    hp_base,
                    atk_base,
                    dps_base,
                    range,
                    attack_interval_sec,
                    move_speed,
                    knockbacks,
                    target,
                    0 AS is_healer,
                    sprite_path
                FROM enemies;
            """
        )

        cur.executemany(
            "INSERT INTO metadata(key, value) VALUES (?, ?)",
            [
                ("schema_version", db["schema_version"]),
                ("database_name", db["database_name"]),
                ("master_data_version", str(db["master_data_version"])),
                ("source_master_hashes", json.dumps(db["source_master_hashes"], ensure_ascii=False)),
            ],
        )

        for a in db["allies"]:
            b = a["base_stats"]
            h = a["healer_aura"]
            sk = a["skill"]
            cur.execute(
                """INSERT INTO allies VALUES (
                    ?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?
                )""",
                (
                    a["id"],
                    a["names"]["zh_TW"],
                    a["names"]["en"],
                    a["names"]["archetype_zh"],
                    a["rarity"],
                    a["role"],
                    a["growth_curve"],
                    a["per_level_pct"],
                    b["cost"],
                    b["cooldown_sec"],
                    b["hp"],
                    b["atk"],
                    b["dps"],
                    b["range"],
                    b["attack_interval_sec"],
                    b["move_speed"],
                    b["knockbacks"],
                    b["target"],
                    json.dumps(b["traits"]),
                    1 if h["is_healer"] else 0,
                    h["aura_heal_base"],
                    h["aura_heal_per_sec_base"],
                    sk["id"],
                    sk["name"],
                    sk["effect_type"],
                    sk["value"],
                    sk["charge_sec"],
                    a["assets"]["default_seed"],
                    a["assets"]["battle_sprite_path"],
                ),
            )
            for tier_key, snap in a["scaling_snapshots"].items():
                cur.execute(
                    "INSERT INTO ally_scaling VALUES (?,?,?,?,?,?,?,?,?,?)",
                    (
                        a["id"],
                        tier_key,
                        snap["level"],
                        snap["awaken"],
                        snap["portrait_stage_id"],
                        snap["hp"],
                        snap["atk"],
                        snap["dps"],
                        snap["aura_heal_per_pulse"],
                        snap["aura_heal_per_sec"],
                    ),
                )

        for e in db["enemies"]:
            b = e["base_stats"]
            cur.execute(
                "INSERT INTO enemies VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                (
                    e["id"],
                    e["faction"],
                    e["sub_category"],
                    e["unit_type"],
                    e["scale_category"],
                    e["lane"],
                    1 if e["is_boss_tier"] else 0,
                    e["names"]["zh_TW"],
                    e["names"]["en"],
                    e["defeat_gold"],
                    b["hp"],
                    b["atk"],
                    b["dps"],
                    b["range"],
                    b["attack_interval_sec"],
                    b["move_speed"],
                    b["knockbacks"],
                    b["target"],
                    e["assets"]["battle_sprite_path"],
                ),
            )
            for app in e["stage_appearances"]:
                cur.execute(
                    "INSERT INTO enemy_stage_scaling VALUES (?,?,?,?,?,?,?,?,?,?)",
                    (
                        e["id"],
                        app["stage_id"],
                        app["chapter"],
                        app["stage_name_zh"],
                        app["enemy_stat_pct"],
                        app["scaled_hp"],
                        app["scaled_atk"],
                        app["scaled_dps"],
                        app["wave_count"],
                        app["wave_interval_sec"],
                    ),
                )

        cur.executemany(
            "INSERT INTO ally_bastion_levels VALUES (?,?,?)",
            [
                (lv["level"], lv["max_hp"], lv["upgrade_gold_cost_to_next"])
                for lv in db["bases"]["ally_bastion"]["levels"]
            ],
        )
        cur.executemany(
            "INSERT INTO enemy_core_stages VALUES (?,?,?,?,?)",
            [
                (
                    st["stage_id"],
                    st["chapter"],
                    st["stage_name_zh"],
                    st["enemy_base_hp"],
                    st["enemy_stat_pct"],
                )
                for st in db["bases"]["enemy_core"]["by_stage"]
            ],
        )
        cur.executemany(
            "INSERT INTO planned_roster VALUES (?,?,?,?,?)",
            [
                (p["id"], p["name_en"], p["name_zh"], p["default_seed"], p["ref_fn"])
                for p in db["unimplemented_dna_roster"]
            ],
        )
        conn.commit()
    finally:
        conn.close()


def validate_outputs(repo_root: Path, expected_db: dict[str, Any], expected_json_text: str, expected_csv_text: str) -> None:
    """Validate schema compliance and parity between disk artifacts and MasterData."""
    stats_dir = repo_root / "data/stats"
    schema = load_json(stats_dir / "character_stats_db.schema.json")
    json_path = stats_dir / "character_stats_db.json"
    csv_path = stats_dir / "character_stats_db.csv"
    sqlite_path = stats_dir / "character_stats_db.sqlite"

    actual_json_bytes = json_path.read_bytes()
    if b"\r" in actual_json_bytes:
        raise AssertionError("character_stats_db.json contains CR bytes (expected LF)")
    actual_db = json.loads(actual_json_bytes.decode("utf-8"))
    jsonschema.validate(instance=actual_db, schema=schema)
    if actual_json_bytes.decode("utf-8") != expected_json_text:
        raise AssertionError("character_stats_db.json is out of sync with MasterData")

    actual_csv_bytes = csv_path.read_bytes()
    if b"\r" in actual_csv_bytes:
        raise AssertionError("character_stats_db.csv contains CR bytes (expected LF)")
    if actual_csv_bytes.decode("utf-8") != expected_csv_text:
        raise AssertionError("character_stats_db.csv is out of sync with MasterData")

    if not sqlite_path.is_file() or sqlite_path.stat().st_size == 0:
        raise AssertionError("character_stats_db.sqlite is missing or empty")

    conn = sqlite3.connect(str(sqlite_path))
    try:
        ally_cnt, enemy_cnt = conn.execute(
            "SELECT (SELECT COUNT(*) FROM allies), (SELECT COUNT(*) FROM enemies)"
        ).fetchone()
        unified_cnt = conn.execute("SELECT COUNT(*) FROM v_all_combat_units").fetchone()[0]
    finally:
        conn.close()

    if ally_cnt != len(expected_db["allies"]) or enemy_cnt != len(expected_db["enemies"]):
        raise AssertionError(f"SQLite counts mismatch: allies={ally_cnt}, enemies={enemy_cnt}")
    if unified_cnt != ally_cnt + enemy_cnt:
        raise AssertionError(f"Unified view count mismatch: {unified_cnt}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build/verify Project Tower character stats DB.")
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verify existing data/stats/* files match MasterData and JSON Schema without writing.",
    )
    args = parser.parse_args(argv)

    repo_root = Path(__file__).resolve().parent.parent
    stats_dir = repo_root / "data/stats"
    schema = load_json(stats_dir / "character_stats_db.schema.json")

    db = build_database_dict(repo_root)
    jsonschema.validate(instance=db, schema=schema)

    json_text = json.dumps(db, ensure_ascii=False, indent=2) + "\n"
    csv_text = render_csv_text(db)

    if args.check:
        validate_outputs(repo_root, db, json_text, csv_text)
        print(
            f"[OK] Character stats DB verified: {db['summary']['active_allies']} allies + "
            f"{db['summary']['enemies']} enemies ({db['summary']['planned_unimplemented_allies']} planned DNA stubs)"
        )
        return 0

    stats_dir.mkdir(parents=True, exist_ok=True)
    with open(stats_dir / "character_stats_db.json", "w", encoding="utf-8", newline="\n") as fp:
        fp.write(json_text)
    with open(stats_dir / "character_stats_db.csv", "w", encoding="utf-8", newline="\n") as fp:
        fp.write(csv_text)
    write_sqlite_db(stats_dir / "character_stats_db.sqlite", db)

    validate_outputs(repo_root, db, json_text, csv_text)
    print(
        f"[BUILT] data/stats/character_stats_db.{{json,csv,sqlite}} "
        f"(allies={db['summary']['active_allies']}, enemies={db['summary']['enemies']}, "
        f"healers={db['summary']['healer_allies']}, stages={db['summary']['stages']})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
