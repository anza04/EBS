"""Stat-based level, potential, reputation and attributes for the real squads (build/real-rosters).

Source: build/player-stats/player-stats-per-game.csv, per-game career tables (RealGM) of the Serie A players: every
season, team and competition (domestic leagues, European cups, NBA, G League, NCAA). The rows come in several column
layouts, so each row is read by locating its block of 21 statistics (GP ... PF); the team and competition are the text
fields around it.

Method (docs/PLAYER_DATABASE.md, "Stat-based ratings"):
- Each competition has a strength relative to Lega A (STRENGTH); cups, pre-season tournaments, summer leagues and
  youth events are left out. Seasons count by recency (SEASON_WEIGHT) and by minutes played.
- Production is Hollinger's Game Score per 40 minutes times the competition strength, shrunk toward a replacement
  level for small samples; role is minutes per game times the strength. Level = 65% production + 35% role, scaled to
  the mean and spread of the hand estimates in the roster files (so Serie A keeps its place against the generated
  leagues), then blended with the hand estimate by sample size (minutes / (minutes + 400)).
- Attributes measurable from a box score (shooting, passing, steals, blocks, rebounds, stamina) are the position
  default (level + position offset) moved by up to 2.5 standard deviations against players of the same position group.
  The others keep the hand estimate or the position default.
- Potential grows 2.5 a year below 27; reputation blends the hand estimate with the level (on the scale of the hand
  estimates: 20 at level 42, 72 at level 70) plus up to 8 each for NBA and EuroLeague games.
Players without statistics in the file keep the hand estimates of their roster file.
"""
import csv
import re
import unicodedata
from pathlib import Path
from statistics import mean, pstdev

STATS_FILE = Path(__file__).resolve().parent / "player-stats" / "player-stats-per-game.csv"
COLUMNS = "GP GS MIN PTS FGM FGA FGP TPM TPA TPP FTM FTA FTP OFF DEF TRB AST STL BLK TOV PF".split()
NUMBER = re.compile(r"^-?\d*\.?\d+$")

# Strength relative to Lega A. Domestic leagues and European cups only; anything else is ignored.
STRENGTH = {
    "NBA": 1.5, "Euroleague": 1.35, "ACB": 1.1, "Eurocup": 1.1, "VTB United": 1.0, "T-BSL": 1.0, "Lega A": 1.0,
    "HEBA A1": 0.95, "Liga ABA": 0.95, "Jeep Elite": 0.95, "G-BBL": 0.95, "BCL-Eu": 0.95, "AUS NBL": 0.95,
    "LKL": 0.9, "I-BSL": 0.9, "CBA": 0.9, "G League": 0.9, "B.League": 0.85, "P-OBL": 0.8, "BNXT": 0.8, "Europe Cup": 0.8,
    "NCAA": 0.75, "A-1 Liga": 0.75, "KLS": 0.75, "BSN": 0.75, "LBL": 0.75, "ELBL": 0.75, "CZ NBL": 0.75, "A Bundeslig": 0.75,
    "Legadue Gold": 0.7, "SKL": 0.7, "Primera FEB": 0.7, "Super Liga": 0.7, "LNA": 0.7, "BSL": 0.7, "Eurochallenge": 0.8,
    "G Pro A": 0.65, "LNB Pro B": 0.65, "T-TBL": 0.65, "DBL": 0.65, "Korisliiga": 0.65, "NZ NBL": 0.65, "BBL Elite Division": 0.65,
    "CYP DivA": 0.6, "CEBL": 0.6, "CAN NBL": 0.6, "LNBP": 0.6, "ROM DivA": 0.6, "B-BBL": 0.6, "BBL": 0.6, "Uk Superleague": 0.6,
    "Extraliga": 0.6, "NBIA": 0.55, "Ma Superleague": 0.55, "Total League": 0.6, "Liga A": 0.55, "BAL": 0.6, "Basketligan": 0.6,
    "G Pro B": 0.5, "NM1": 0.5, "Segunda FEB": 0.5, "LEB DivA": 0.5, "P-LPB": 0.55, "Mo Prva A": 0.5, "ULM": 0.55, "V-SLB": 0.55,
}
CATEGORY_LEAGUE = {"NBA Regular Season Stats": "NBA", "G League Regular Season Stats": "G League", "NCAA Season Stats": "NCAA"}
SEASON_WEIGHT = {2026: 1.0, 2025: 1.0, 2024: 0.55, 2023: 0.3, 2022: 0.15}  # season start year; older seasons 0.05
# Roster name -> name in the statistics file (both normalised).
ALIASES = {"zan mark sisko": "zan sisko", "aljami durham": "aj durham", "jordan bayehe": "jordan philippe bayehe",
           "niccolo mannion": "nico mannion", "michael iuzzolino": "mike iuzzolino", "andres forray": "toto forray",
           "iris ikangi": "nongo ikangi", "mouhamet diouf": "momo diouf"}
GROUP = {"PG": "G", "SG": "G", "SF": "W", "PF": "B", "C": "B"}
# attribute -> (scale per standard deviation, {indicator: weight}); indicators are computed in _indicators.
STAT_ATTRIBUTES = {
    "threePoint": (9, {"p3": 0.7, "vol3": 0.3}), "freeThrow": (9, {"pft": 1}), "midRange": (6, {"pft": 0.5, "p3": 0.5}),
    "closeShot": (7, {"p2": 1}), "layup": (6, {"p2": 0.6, "ftr": 0.4}), "passing": (9, {"ast": 1}),
    "courtVision": (7, {"ast": 0.7, "astto": 0.3}), "decisionMaking": (7, {"astto": 1}), "ballHandling": (6, {"ast": 0.6, "astto": 0.4}),
    "steal": (9, {"stl": 1}), "block": (9, {"blk": 1}), "offensiveRebound": (9, {"oreb": 1}), "defensiveRebound": (9, {"dreb": 1}),
    "stamina": (6, {"mpg": 1}),
}
# Shooting attributes need this many (unweighted-by-strength) attempts; below it a hand estimate in the roster file stays.
MIN_ATTEMPTS = {"threePoint": ("TPA", 25), "midRange": ("TPA", 25), "freeThrow": ("FTA", 20)}
MIN_SAMPLE = 150  # weighted minutes below which a player is not used for the position-group averages


def normalise(name):
    name = "".join(c for c in unicodedata.normalize("NFKD", name) if not unicodedata.combining(c)).lower()
    name = re.sub(r"[.,']", "", name).replace("-", " ")
    return re.sub(r"\s+(jr|ii|iii)$", "", " ".join(name.split()))


def read_rows(path=STATS_FILE):
    """(player, season start year, age, competition, per-game statistics) for the rows of a rated competition."""
    with path.open(encoding="utf-8-sig", newline="") as f:
        lines = list(csv.reader(f))[1:]
    for fields in lines:
        player, _, category, season, age, rest = fields[0], fields[1], fields[2], fields[3], fields[4], fields[5:]
        start = next((i for i in range(len(rest) - 20) if all(NUMBER.match(x) for x in rest[i:i + 21])), None)
        if start is None:
            continue  # NCAA seasons without statistics
        stats = dict(zip(COLUMNS, map(float, rest[start:start + 21])))
        texts = [x for x in rest[:start] + rest[start + 21:] if x and not NUMBER.match(x)]
        if category == "International Season Stats":
            league = texts[-1] if texts else ""
            if "All Teams" in texts or league == "All Leagues":
                continue
        else:
            league = CATEGORY_LEAGUE.get(category)
        if league in STRENGTH and stats["GP"] > 0 and stats["MIN"] > 0:
            yield normalise(player), int(season[:4]), int(age) if age.isdigit() else None, league, stats


def _game_score(s):
    return (s["PTS"] + 0.4 * s["FGM"] - 0.7 * s["FGA"] - 0.4 * (s["FTA"] - s["FTM"]) + 0.7 * s["OFF"] + 0.3 * s["DEF"]
            + s["STL"] + 0.7 * s["AST"] + 0.7 * s["BLK"] - 0.4 * s["PF"] - s["TOV"])


class Career:
    """Season-weighted totals of one player."""

    def __init__(self):
        self.minutes = 0.0  # season-weighted minutes
        self.totals = {k: 0.0 for k in COLUMNS}
        self.production = self.role = self.games_weight = 0.0
        self.nba_games = self.euroleague_games = 0.0
        self.ages = {}

    def add(self, year, age, league, s):
        w = SEASON_WEIGHT.get(year, 0.05)
        strength = STRENGTH[league]
        minutes = s["GP"] * s["MIN"] * w
        self.minutes += minutes
        for k in COLUMNS:
            if k not in ("GP", "GS", "MIN", "FGP", "TPP", "FTP"):
                self.totals[k] += s[k] * s["GP"] * w * (strength ** 0.5 if k in ("OFF", "DEF", "AST", "STL", "BLK") else 1)
        self.totals["MIN"] += minutes
        self.production += _game_score(s) / s["MIN"] * 40 * strength * minutes
        self.role += s["MIN"] * strength * s["GP"] * w
        self.games_weight += s["GP"] * w
        if league == "NBA":
            self.nba_games += s["GP"]
        if league == "Euroleague":
            self.euroleague_games += s["GP"]
        if age:
            self.ages[year] = age


def load_careers(path=STATS_FILE):
    careers = {}
    for player, year, age, league, stats in read_rows(path):
        careers.setdefault(player, Career()).add(year, age, league, stats)
    return careers


def _indicators(c):
    t, per36 = c.totals, 36 / max(c.totals["MIN"], 1)
    return {"p3": (t["TPM"] + 0.33 * 60) / (t["TPA"] + 60), "vol3": t["TPA"] * per36,
            "pft": (t["FTM"] + 0.74 * 40) / (t["FTA"] + 40),
            "p2": (t["FGM"] - t["TPM"] + 0.50 * 80) / (t["FGA"] - t["TPA"] + 80), "ftr": t["FTA"] / max(t["FGA"], 1),
            "ast": t["AST"] * per36, "astto": (t["AST"] + 2) / (t["TOV"] + 2), "stl": t["STL"] * per36, "blk": t["BLK"] * per36,
            "oreb": t["OFF"] * per36, "dreb": t["DEF"] * per36, "mpg": c.role / max(c.games_weight, 1)}


def _z(value, values):
    sd = pstdev(values) or 1
    return max(-2.5, min(2.5, (value - mean(values)) / sd))


def rate_rosters(rosters, attributes, profiles, season_year=2026):
    """{(first, last, dateOfBirth): {"level", "reputation", "potential", "ratings", "minutes"}} for players with statistics."""
    careers = load_careers()
    matched = []
    for roster in rosters:
        for real in roster["players"]:
            name = normalise(f"{real['firstName']} {real['lastName']}")
            career = careers.get(ALIASES.get(name, name))
            if career is None or career.minutes == 0:
                continue
            birth = int(real["dateOfBirth"][:4])
            if any(abs(year - birth - age) > 2 for year, age in career.ages.items()):
                continue  # same name, different player
            matched.append((real, career))
    if not matched:
        return {}

    production = [c.production / c.minutes for _, c in matched]
    replacement = sorted(production)[len(production) // 4]
    scores = []
    for (_, c), rate in zip(matched, production):
        shrunk = (rate * c.minutes + replacement * 250) / (c.minutes + 250)
        scores.append((shrunk, c.role / c.games_weight))
    zp = [_z(s[0], [x[0] for x in scores]) for s in scores]
    zr = [_z(s[1], [x[1] for x in scores]) for s in scores]
    composite = [0.65 * a + 0.35 * b for a, b in zip(zp, zr)]
    hand = [r["level"] for r, _ in matched]
    centre, spread, csd = mean(hand), pstdev(hand), pstdev(composite) or 1

    indicators = [_indicators(c) for _, c in matched]
    pools = {}
    for (real, c), ind in zip(matched, indicators):
        if c.minutes >= MIN_SAMPLE:
            for key, value in ind.items():
                pools.setdefault((GROUP[real["position"]], key), []).append(value)

    result = {}
    for (real, c), comp, ind in zip(matched, composite, indicators):
        confidence = c.minutes / (c.minutes + 400)
        stat_level = centre + (comp - mean(composite)) / csd * spread
        level = round(min(85, max(30, confidence * stat_level + (1 - confidence) * real["level"])))
        age = season_year - int(real["dateOfBirth"][:4])
        potential = min(95, max(level, round(level + 2.5 * max(0, 27 - age))))
        prestige = min(8, c.nba_games / 15) + min(8, c.euroleague_games / 12)
        reputation = round(min(95, max(1, 0.5 * real["reputation"] + 0.5 * (20 + 1.8 * (level - 42) + prestige))))
        offsets = profiles[real["position"]][2]
        category = dict(attributes)
        ratings = dict(real["ratings"])
        for attribute, (scale, weights) in STAT_ATTRIBUTES.items():
            column, needed = MIN_ATTEMPTS.get(attribute, ("MIN", 0))
            if attribute in real["ratings"] and c.totals[column] < needed:
                continue
            group = GROUP[real["position"]]
            z = sum(w * _z(ind[k], pools.get((group, k), [ind[k]])) for k, w in weights.items())
            default = level + offsets.get(category[attribute], 0)
            ratings[attribute] = min(95, max(20, round(default + confidence * scale * z)))
        result[(real["firstName"], real["lastName"], real["dateOfBirth"])] = {
            "level": level, "reputation": reputation, "potential": potential, "ratings": ratings, "minutes": round(c.minutes)}
    return result
