"""Profiles and stat-based ratings of the ~2,600 real players in build/player-stats/europe-*.csv (RealGM per-game
career tables supplied by the author, compacted by build/generate-real-leagues-dataset.py --source).

Same method as build/player_ratings.py (docs/PLAYER_DATABASE.md, "Stat-based ratings"), applied to a whole population
without hand estimates:
- Production (Game Score per 40 x competition strength, shrunk toward replacement level) and role (minutes x strength)
  give a composite z-score over all rated players.
- The scale is calibrated on the Serie A players who also have curated roster ratings: the level of those players
  keeps the mean and spread of their curated levels, so the new leagues sit on the scale of the existing data.
- Small samples are blended toward the mean level of the player's current division (free agents: the free-agent mean)
  by minutes / (minutes + 400).
- Box-score attributes move from the position default as in player_ratings; strength, speed and agility also move a
  little with height and weight against the position's typical build.
- Reputation: 20 at level 42, 72 at level 70, plus up to 8 each for NBA and EuroLeague games. Potential: +2.5 a year
  below 27.
"""
import csv
from pathlib import Path
from statistics import fmean, pstdev

import player_ratings as pr

FOLDER = Path(__file__).resolve().parent / "player-stats"
PROFILES_FILE, STATS_FILE = FOLDER / "europe-profiles.csv", FOLDER / "europe-stats.csv"
PROFILE_COLUMNS = ["Player", "Position", "Birth Date", "Age", "Height (cm)", "Weight (kg)", "Current Team", "Nationality", "RealGM URL"]
STAT_COLUMNS = ["GP", "GS", "MIN", "PTS", "FGM", "FGA", "FG%", "3PM", "3PA", "3P%", "FTM", "FTA", "FT%",
                "OFF", "DEF", "TRB", "AST", "STL", "BLK", "TOV", "PF"]
# Leagues of the new file missing from player_ratings.STRENGTH (relative to Lega A).
STRENGTH = {**pr.STRENGTH, "Winner League": 0.9, "BiH Liga": 0.6, "LNB Espoirs": 0.35, "Liga U": 0.35, "KML": 0.6,
            "NBL Blitz": 0.55, "LEB Oro": 0.7, "Super League": 0.6, "Alpe Adria Cup": 0.55, "ABA 2": 0.6, "ENBL": 0.55,
            "SLO 1. SKL": 0.7, "Liga 1": 0.55, "Slovak Extraliga": 0.6, "HUN NB I/A": 0.65, "NB I/A": 0.65, "ESAKE A2": 0.55,
            "HEBA A2": 0.55, "BUL NBL": 0.55, "NKL": 0.5, "SLB": 0.6, "KBL": 0.8, "NBB": 0.6, "Basketligaen": 0.55}


def compact(source):
    """Rewrites the two compact files from the raw RealGM export (one row per player season; profile repeated)."""
    profiles, stats = {}, []
    with open(source, encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            url = r["RealGM URL"]
            profiles.setdefault(url, {k: r[k] for k in PROFILE_COLUMNS})
            international = r["Category"] == "International Season Stats"
            if r["GP"] not in ("", "-") and (international or r["Category"] in pr.CATEGORY_LEAGUE):
                stats.append([url, r["Category"], r["Season"][:7], r["Age (season)"], r["Team"], r["League"]] + [r[k] for k in STAT_COLUMNS])
    with PROFILES_FILE.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, PROFILE_COLUMNS, lineterminator="\n")
        w.writeheader()
        w.writerows(profiles.values())
    with STATS_FILE.open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["url", "category", "season", "age", "team", "league"] + STAT_COLUMNS)
        w.writerows(stats)


def read_profiles():
    with PROFILES_FILE.open(encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def read_seasons():
    """{url: [(season start year, team, league, games)]} of every international season row, for spells and tenure."""
    seasons = {}
    for row in _rows():
        if row["category"] == "International Season Stats" and row["team"] not in ("", "All Teams"):
            seasons.setdefault(row["url"], []).append((int(row["season"][:4]), row["team"], row["league"], _num(row["GP"])))
    return seasons


def read_ages():
    """{url: (season start year, age)} of the latest season with a known age (birth year when the profile has none)."""
    ages = {}
    for row in _rows():
        if row["age"].isdigit():
            year = int(row["season"][:4])
            if year >= ages.get(row["url"], (0, 0))[0]:
                ages[row["url"]] = (year, int(row["age"]))
    return ages


def _rows():
    with STATS_FILE.open(encoding="utf-8", newline="") as f:
        yield from csv.DictReader(f)


def _num(text):
    try:
        return float(text)
    except ValueError:
        return 0.0


def load_careers():
    careers = {}
    for row in _rows():
        league = row["league"] if row["category"] == "International Season Stats" else pr.CATEGORY_LEAGUE.get(row["category"])
        if league not in STRENGTH or league == "All Leagues" or row["team"] == "All Teams":
            continue
        s = dict(zip(pr.COLUMNS, (_num(row[k]) for k in STAT_COLUMNS)))
        if s["GP"] <= 0 or s["MIN"] <= 0:
            continue
        age = int(row["age"]) if row["age"].isdigit() else None
        pr.STRENGTH.setdefault(league, STRENGTH[league])  # Career.add reads player_ratings.STRENGTH
        careers.setdefault(row["url"], pr.Career()).add(int(row["season"][:4]), age, league, s)
    return careers


def rate(players, calibration, attributes, profiles, season_year=2026):
    """players: [{"url", "position", "dateOfBirth", "heightCm", "weightKg", "group"}] (group = division name or
    "free agents"). calibration: {url: curated level} of players with curated ratings. Returns {url: rating}."""
    careers = load_careers()
    rated = [(p, careers[p["url"]]) for p in players if p["url"] in careers and careers[p["url"]].minutes > 0]
    population = [u for u in {p["url"] for p, _ in rated} | set(calibration) if u in careers and careers[u].minutes > 0]
    production = {u: careers[u].production / careers[u].minutes for u in population}
    replacement = sorted(production.values())[len(production) // 4]
    shrunk = {u: (r * careers[u].minutes + replacement * 250) / (careers[u].minutes + 250) for u, r in production.items()}
    role = {u: careers[u].role / careers[u].games_weight for u in population}

    def moments(values, cap):
        values = list(values)
        m, sd = fmean(values), pstdev(values) or 1
        return lambda v: max(-cap, min(cap, (v - m) / sd))

    zs, zr = moments(shrunk.values(), 3.5), moments(role.values(), 3.5)
    composite = {u: 0.65 * zs(shrunk[u]) + 0.35 * zr(role[u]) for u in population}
    pairs = [(composite[u], lv) for u, lv in calibration.items() if u in composite]
    cm, cs = fmean(x for x, _ in pairs), pstdev([x for x, _ in pairs]) or 1
    lm, ls = fmean(y for _, y in pairs), pstdev([y for _, y in pairs])
    stat_level = {u: lm + (v - cm) / cs * ls for u, v in composite.items()}
    confidence = {p["url"]: c.minutes / (c.minutes + 400) for p, c in rated}

    groups = {}
    for p, _ in rated:
        if confidence[p["url"]] >= 0.5:
            groups.setdefault(p["group"], []).append(stat_level[p["url"]])
    fallback = sorted(stat_level.values())[len(stat_level) // 4]
    prior = {g: fmean(v) for g, v in groups.items()}

    indicators = {p["url"]: pr._indicators(c) for p, c in rated}
    pools = {}
    for p, c in rated:
        if c.minutes >= pr.MIN_SAMPLE:
            for key, value in indicators[p["url"]].items():
                pools.setdefault((pr.GROUP[p["position"]], key), []).append(value)
    pools = {key: moments(values, 2.5) for key, values in pools.items()}
    category = dict(attributes)

    result = {}
    for p in players:
        url, position = p["url"], p["position"]
        career = careers.get(url)
        w = confidence.get(url, 0.0)
        base = prior.get(p["group"], fallback)
        level = round(min(90, max(30, w * stat_level.get(url, base) + (1 - w) * base)))
        age = season_year - int(p["dateOfBirth"][:4])
        height, weight, offsets = profiles[position]
        ratings = {name: min(95, max(20, level + offsets.get(cat, 0))) for name, cat in attributes}
        if url in indicators:
            for attribute, (scale, weights) in pr.STAT_ATTRIBUTES.items():
                column, needed = pr.MIN_ATTEMPTS.get(attribute, ("MIN", 0))
                if career.totals[column] < needed:
                    continue
                zz = sum(k * pools[(pr.GROUP[position], key)](indicators[url][key]) for key, k in weights.items()
                         if (pr.GROUP[position], key) in pools)
                ratings[attribute] = min(95, max(20, round(level + offsets.get(category[attribute], 0) + w * scale * zz)))
        build = {"strength": max(-6, min(6, (p["weightKg"] - weight) / 2)),
                 "speed": max(-5, min(5, (height - p["heightCm"]) / 2)), "agility": max(-5, min(5, (height - p["heightCm"]) / 2))}
        for attribute, delta in build.items():
            ratings[attribute] = min(95, max(20, round(ratings[attribute] + delta)))
        prestige = (min(8, career.nba_games / 15) + min(8, career.euroleague_games / 12)) if career else 0
        result[url] = {"level": level, "potential": min(95, max(level, round(level + 2.5 * max(0, 27 - age)))),
                       "reputation": round(min(99, max(1, 20 + 1.8 * (level - 42) + prestige))), "ratings": ratings,
                       "minutes": round(career.minutes) if career else 0}
    return result
