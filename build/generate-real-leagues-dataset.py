"""Generates data/real-leagues: eight real top divisions of 2026-27 (ABA League, BBL, Betclic ÉLITE, GBL, Lega Basket
Serie A, Serie A2, Liga Endesa, BSL) with their 138 real clubs and the real players of
build/player-stats/europe-*.csv (RealGM, supplied by the author). Serie A uses the curated squads of
build/real-rosters; players at clubs outside these divisions are free agents. Ratings: build/realgm_ratings.py.
docs/PLAYER_DATABASE.md §7. Deterministic (UUID v5 IDs shared with data/europe, seeded numbers).

Run: python build/generate-real-leagues-dataset.py [--source <raw RealGM export .csv>]
(--source first refreshes the compact statistics files.)
"""
import importlib.util
import json
import random
import sys
from datetime import date
from pathlib import Path

import player_ratings
import realgm_ratings

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("europe", HERE / "generate-europe-dataset.py")
eu = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(eu)

SLUG = "real-leagues"
SEASON_START = eu.SEASON_START
MIN_SQUAD = 12
FREE_AGENTS = "free agents"

# name: (code, EU/EEA/CH). Existing data/europe codes are kept (GER, GRE, CRO, SLO, NED, POR …); others ISO alpha-3.
NATIONS = {
    "Albania": ("ALB", False), "Algeria": ("DZA", False), "Angola": ("AGO", False), "Argentina": ("ARG", False),
    "Armenia": ("ARM", False), "Australia": ("AUS", False), "Austria": ("AUT", True), "Azerbaijan": ("AZE", False),
    "Bahamas": ("BHS", False), "Barbados": ("BRB", False), "Belarus": ("BLR", False), "Belgium": ("BEL", True),
    "Benin": ("BEN", False), "Bosnia and Herzegovina": ("BIH", False), "Brazil": ("BRA", False), "Bulgaria": ("BGR", True),
    "Cameroon": ("CMR", False), "Canada": ("CAN", False), "Cape Verde": ("CPV", False), "Central African Republic": ("CAF", False),
    "Chile": ("CHL", False), "Colombia": ("COL", False), "Comoros": ("COM", False), "Croatia": ("CRO", True), "Cuba": ("CUB", False),
    "Cyprus": ("CYP", True), "Czechia": ("CZE", True), "Democratic Republic of the Congo": ("COD", False), "Denmark": ("DNK", True),
    "Dominica": ("DMA", False), "Dominican Republic": ("DOM", False), "England": ("GBR", False), "Scotland": ("GBR", False),
    "Eritrea": ("ERI", False), "Estonia": ("EST", True), "Finland": ("FIN", True), "France": ("FRA", True), "Gabon": ("GAB", False),
    "Gambia": ("GMB", False), "Georgia": ("GEO", False), "Germany": ("GER", True), "Ghana": ("GHA", False), "Greece": ("GRE", True),
    "Guinea": ("GIN", False), "Haiti": ("HTI", False), "Hungary": ("HUN", True), "Iceland": ("ISL", True), "Iran": ("IRN", False),
    "Ireland": ("IRL", True), "Israel": ("ISR", False), "Italy": ("ITA", True), "Ivory Coast": ("CIV", False), "Jamaica": ("JAM", False),
    "Japan": ("JPN", False), "Kosovo": ("XKX", False), "Latvia": ("LVA", True), "Lebanon": ("LBN", False), "Lithuania": ("LTU", True),
    "Luxembourg": ("LUX", True), "Madagascar": ("MDG", False), "Mali": ("MLI", False), "Mexico": ("MEX", False), "Moldova": ("MDA", False),
    "Montenegro": ("MNE", False), "Netherlands": ("NED", True), "New Zealand": ("NZL", False), "Nigeria": ("NGA", False),
    "North Macedonia": ("MKD", False), "Norway": ("NOR", True), "Panama": ("PAN", False), "Paraguay": ("PRY", False),
    "Philippines": ("PHL", False), "Poland": ("POL", True), "Portugal": ("POR", True), "Puerto Rico": ("PUR", False),
    "Republic of the Congo": ("COG", False), "Romania": ("ROU", True), "Russia": ("RUS", False), "Rwanda": ("RWA", False),
    "Senegal": ("SEN", False), "Serbia": ("SRB", False), "Slovakia": ("SVK", True), "Slovenia": ("SLO", True),
    "South Africa": ("ZAF", False), "South Sudan": ("SSD", False), "Spain": ("ESP", True), "Sudan": ("SDN", False),
    "Sweden": ("SWE", True), "Switzerland": ("CHE", True), "Tahiti": ("PYF", True), "Thailand": ("THA", False),
    "Turkiye": ("TUR", False), "Uganda": ("UGA", False), "Ukraine": ("UKR", False), "United States": ("USA", False),
    "Uruguay": ("URY", False), "United Arab Emirates": ("ARE", False),
}
COUNTRY_NAMES = {"GBR": "United Kingdom", "CZE": "Czech Republic", "TUR": "Turkey", "CIV": "Côte d'Ivoire", "COD": "DR Congo",
                 "PYF": "French Polynesia", "USA": "United States"}

ABA_COUNTRIES = ["SRB", "CRO", "SLO", "MNE", "BIH", "AUT", "SVK", "ROU", "ARE"]
D = eu.division
LEAGUES = [
    ("Lega Basket", "LBA", "ITA", [
        D("Lega Basket Serie A", "LBA", 16, po=eu.playoffs(8, [5, 5, 7]), releg=1, squad=eu.ITA_SQUAD),
        D("Serie A2", "A2", 20, po=eu.playoffs(8, [5, 5, 5]), promo={"playoffWinner": True}, squad={"maxForeignPlayers": 2}),
    ]),
    ("Asociación de Clubs de Baloncesto", "ACB", "ESP", [
        D("Liga Endesa", "ACB", 18, po=eu.playoffs(8, [3, 5, 5]), squad={"maxNonEuPlayers": 2, "minDomesticPlayers": 4})]),
    ("Basketball Bundesliga", "BBL", "GER", [
        D("easyCredit BBL", "BBL", 18, po=eu.playoffs(8, [5, 5, 5], play_in=True), squad={"minDomesticPlayers": 6})]),
    ("Ligue Nationale de Basket", "LNB", "FRA", [
        D("Betclic ÉLITE", "ELITE", 16, po=eu.playoffs(8, [3, 5, 5], play_in=True), squad={"minDomesticPlayers": 4})]),
    ("Hellenic Basketball Clubs Association", "ESAKE", "GRE", [
        D("Stoiximan GBL", "GBL", 14, po=eu.playoffs(8, [3, 5, 5]), squad={"maxForeignPlayers": 6})]),
    ("Turkish Basketball Federation", "TBF", "TUR", [
        D("Basketbol Süper Ligi", "BSL", 16, po=eu.playoffs(8, [3, 5, 7]), squad={"maxForeignPlayers": 6})]),
    ("ABA League", "ABA", None, [
        D("ABA League", "ABA", 20, po=eu.playoffs(8, [3, 3, 5], play_in=True), countries=ABA_COUNTRIES)]),
]

# RealGM team (2026-27) -> "club name|short|city|country". Names of data/europe clubs are kept (same IDs).
CLUBS = {
    "Lega Basket Serie A": {
        "Virtus Bologna": "Virtus Bologna", "AX Armani Exchange Milan": "Olimpia Milano", "Umana Venezia": "Reyer Venezia",
        "Dolomiti Energia Trento": "Aquila Basket Trento", "Bertram Tortona": "Derthona Basket", "Grissin Bon Reggio Emilia": "Pallacanestro Reggiana",
        "Napoli Basket": "Napoli Basket", "Pallacanestro Trieste 2004": "Pallacanestro Trieste", "Maxima Roma": "Maxima Roma",
        "Old Wild West Udine": "APU Udine", "OpenJobMetis Varese": "Pallacanestro Varese", "Roma SPQR": "BC Roma",
        "Universo De'Longhi Treviso": "Universo Treviso Basket", "Acqua San Bernardo Cantu": "Pallacanestro Cantù",
        "Givova Scafati": "Scafati Basket", "Tezenis Scaligera Verona": "Scaligera Basket Verona"},
    "Serie A2": {
        "Fortituto Kontatto Bologna": "Fortitudo Bologna|FOR|Bologna", "Banco di Sardegna Sassari": "Dinamo Sassari|SAS|Sassari",
        "Carpegna Prosciutto Basket Pesaro": "Victoria Libertas Pesaro|PES|Pesaro", "Ferraroni Juvi Cremona": "Juvi Cremona|CRE|Cremona",
        "UEB Gesteco Cividale": "UEB Cividale|CIV|Cividale del Friuli", "Zeus Energy Group Rieti": "Real Sebastiani Rieti|RIE|Rieti",
        "Gemini Mestre": "Basket Mestre|MES|Venezia", "Unieuro Forli": "Pallacanestro Forlì|FRL|Forlì", "Sella Cento": "Benedetto XIV Cento|CEN|Cento",
        "Reale Mutua Torino": "Basket Torino|TOR|Torino", "Urania Milano": "Urania Milano|URA|Milano", "RivieraBanca Rimini": "Basket Rimini|RIM|Rimini",
        "Pasta Reggia JuveCaserta": "Juvecaserta|CAS|Caserta", "Elachem Vigevano": "Vigevano 1955|VIG|Vigevano",
        "Enel Brindisi": "New Basket Brindisi|BRI|Brindisi", "Sidigas Avellino": "Scandone Avellino|AVE|Avellino",
        "Georgio Tesi Group Pistoia": "Pistoia Basket|PIS|Pistoia", "Montecatini": "Montecatini Basket|MON|Montecatini Terme",
        "Akern Libertas Livorno": "Libertas Livorno|LIV|Livorno", "Crifo Wines Ruvo di Puglia": "Talos Ruvo di Puglia|RUV|Ruvo di Puglia"},
    "Liga Endesa": {
        "Real Madrid": "Real Madrid", "Barca": "FC Barcelona", "Valencia Basket": "Valencia Basket", "Unicaja": "Unicaja",
        "Baskonia": "Saski Baskonia", "Lenovo Tenerife": "CB Canarias", "Joventut Badalona": "Club Joventut Badalona",
        "UCAM Murcia CB": "UCAM Murcia CB", "Rio Breogan": "CB Breogán", "CB Girona": "Bàsquet Girona", "BAXI Manresa": "Bàsquet Manresa",
        "Casademont Zaragoza": "Basket Zaragoza", "Bilbao Basket": "Bilbao Basket", "MoraBanc Andorra": "BC Andorra",
        "Forca Lleida CE": "Força Lleida", "Siblo San Pablo Burgos": "CB San Pablo Burgos",
        "CB Coruna": "Básquet Coruña|COR|A Coruña|ESP", "Monbus Obradoiro": "Obradoiro CAB|OBR|Santiago de Compostela|ESP"},
    "easyCredit BBL": {
        "Bayern Munich": "FC Bayern München", "ALBA Berlin": "ALBA Berlin", "Ratiopharm Ulm": "Ratiopharm Ulm",
        "Telekom Baskets Bonn": "Telekom Baskets Bonn", "s.Oliver Baskets": "Würzburg Baskets", "BV Chemnitz 99": "Niners Chemnitz",
        "MHP Riesen": "Riesen Ludwigsburg", "Brose Baskets Bamberg": "Bamberg Baskets", "Rostock Seawolves": "Rostock Seawolves",
        "Veolia Towers Hamburg": "Hamburg Towers", "EWE Baskets Oldenburg": "Baskets Oldenburg", "Mitteldeutscher BC": "Mitteldeutscher BC",
        "Basketball Lowen Braunschweig": "Basketball Löwen Braunschweig", "Science City Jena": "Science City Jena",
        "Fraport Skyliners": "Skyliners Frankfurt", "SC Rasta Vechta": "Rasta Vechta", "Gladiators Trier": "Gladiators Trier",
        "Phoenix Hagen": "Phoenix Hagen|HAG|Hagen|GER"},
    "Betclic ÉLITE": {
        "Paris Basketball": "Paris Basketball", "ASVEL Basket": "ASVEL Lyon-Villeurbanne", "JL Bourg-en-Bresse": "JL Bourg",
        "Nanterre 92": "Nanterre 92", "Strasbourg IG": "SIG Strasbourg", "JDA Dijon Basket": "JDA Dijon", "Cholet Basket": "Cholet Basket",
        "Le Mans Sarthe Basket": "Le Mans Sarthe Basket", "CSP Limoges": "Limoges CSP", "BCM Gravelines": "BCM Gravelines-Dunkerque",
        "Saint-Quentin Basketball": "Saint-Quentin Basket-Ball", "Pau-Orthez": "Élan Béarnais", "Boulazac Basket Dordogne": "Boulazac Basket Dordogne",
        "Nancy Basket": "SLUC Nancy", "Chalon-Sur-Saone": "Élan Chalon|CHA|Chalon-sur-Saône|FRA",
        "Chorale Roanne Basket": "Chorale Roanne|ROA|Roanne|FRA"},
    "Stoiximan GBL": {
        "Olympiacos": "Olympiacos", "Panathinaikos": "Panathinaikos", "AEK Athens": "AEK Athens", "PAOK BC": "PAOK",
        "Aris Midea Thessaloniki": "Aris Thessaloniki", "Peristeri Betsson": "Peristeri", "ASP Promitheas Patras": "Promitheas Patras",
        "Kolossos Rodou BC": "Kolossos Rhodes", "Maroussi BC": "Maroussi", "AS Karditsas": "Karditsa",
        "Iraklis BC": "Iraklis|IRA|Thessaloníki|GRE", "Vikos Ioanninon": "Vikos Ioannina|VIK|Ioánnina|GRE",
        "AE Doxa Lefkadas": "Doxa Lefkadas|DOX|Lefkáda|GRE", "Mykonos": "Mykonos Basket|MYK|Mýkonos|GRE"},
    "Basketbol Süper Ligi": {
        "Fenerbahce Beko": "Fenerbahçe", "Anadolu Efes": "Anadolu Efes", "Besiktas Icrypex": "Beşiktaş", "Galatasaray": "Galatasaray",
        "Turk Telekom": "Türk Telekom", "Bahcesehir Koleji": "Bahçeşehir Koleji", "Pinar Karsiyaka": "Karşıyaka", "Tofas SC": "Tofaş",
        "Socar Petkimspor": "Aliağa Petkimspor", "Trabzonspor": "Trabzonspor", "Bursaspor": "Bursaspor", "Esenler Erokspor": "Esenler Erokspor",
        "Merkezefendi Belediyesi Denizli Basket": "Merkezefendi Belediyesi",
        "Bordo Sportif Balikesir": "Balıkesir Basketbol|BAL|Balıkesir|TUR", "Cayirova": "Çayırova Belediyesi|CAY|Çayırova|TUR",
        "Korfez Mansia": "Körfez Spor|KOR|Körfez|TUR"},
    "ABA League": {
        "KK Partizan": "Partizan|PAR|Beograd|SRB", "KK Crvena Zvezda": "Crvena zvezda|CZV|Beograd|SRB", "Dubai": "Dubai Basketball|DUB|Dubai|ARE",
        "Buducnost Voli Podgorica": "Budućnost|BUD|Podgorica|MNE", "KK Cedevita Olimpija Ljubljana": "Cedevita Olimpija|OLI|Ljubljana|SLO",
        "BC Igokea": "Igokea|IGO|Aleksandrovac|BIH", "KK Bosna": "Bosna|BOS|Sarajevo|BIH", "KK Mega Bemax": "Mega Basket|MEG|Beograd|SRB",
        "FMP Beograd": "FMP|FMP|Beograd|SRB", "KK Borac Cacak": "Borac Čačak|BOR|Čačak|SRB", "Krka Novo Mesto": "Krka Novo mesto|KRK|Novo Mesto|SLO",
        "KK Spartak Subotica": "Spartak Subotica|SPA|Subotica|SRB", "KK Zadar": "KK Zadar|ZAD|Zadar|CRO", "Ilirija": "Ilirija|ILI|Ljubljana|SLO",
        "Studentski Centar Podgorica": "SC Derby|SCD|Podgorica|MNE", "KK Cibona": "Cibona|CIB|Zagreb|CRO",
        "BC Hallmann Vienna": "BC Vienna|VIE|Wien|AUT", "BC Siroki": "HKK Široki|SIR|Široki Brijeg|BIH",
        "Slovan Bratislava": "Slovan Bratislava|SLV|Bratislava|SVK", "U-Banca Transilvania Cluj Napoca": "U-BT Cluj-Napoca|CLU|Cluj-Napoca|ROU"},
}
GENERATED_NAMES = {"MNE": "SRB", "BIH": "CRO", "AUT": "GER", "SVK": "CZE", "ROU": "SRB", "ARE": "USA"}  # name pools for top-ups


def europe_clubs():
    return {c["name"]: c for c in json.loads((HERE.parent / "data" / "europe" / "europe-2026-27.json").read_text(encoding="utf-8"))["clubs"]}


def club_identity(entry, known):
    if "|" not in entry:
        c = known[entry]
        return {"id": c["id"], "name": c["name"], "shortName": c["shortName"], "country": c["country"], "city": c["city"]}
    name, short, city, *country = entry.split("|")
    country = country[0] if country else "ITA"
    return {"id": eu.uid("club", country, name), "name": name, "shortName": short, "country": country, "city": city}


POSITIONS = {"G": ("PG", "SG", 190), "F": ("SF", "PF", 204), "GF": ("SG", "SF", 196), "FC": ("PF", "C", 208)}


def position(code, height):
    """Primary and secondary position from RealGM's code; generic codes split by height."""
    if code in eu.PROFILES:
        return code, ""
    if code in POSITIONS:
        small, big, cut = POSITIONS[code]
        return (small, big) if height < cut else (big, small)
    return min(eu.PROFILES, key=lambda p: abs(eu.PROFILES[p][0] - height)), ""


def profile(raw, season_age):
    """A player record from a RealGM profile, or None when nationality or age is unknown. season_age: (season start
    year, age) of the latest season with an age, for profiles without birth date or age."""
    nations = [n.strip() for n in raw["Nationality"].split("/") if n.strip()]
    if raw["Birth Date"]:
        birth, estimated = raw["Birth Date"], []
    elif raw["Age"].isdigit():
        birth, estimated = f"{2026 - int(raw['Age'])}-07-01", ["birth date from age"]
    elif season_age:
        birth, estimated = f"{season_age[0] - season_age[1]}-07-01", ["birth date from season age"]
    else:
        return None
    if not nations or (2026 - int(birth[:4]) > 40 and (season_age or (0,))[0] < 2024):
        return None  # no nationality, or a coach / staff member listed at the club (over 40, no recent season)
    first, _, last = raw["Player"].strip().partition(" ")
    if not last:
        first, last = "", first
    height = round(float(raw["Height (cm)"])) if raw["Height (cm)"] else None
    pos, second = position(raw["Position"], height or 200)
    if height is None:
        height = eu.PROFILES[pos][0]
        estimated.append("height by position")
    if raw["Weight (kg)"]:
        weight = round(float(raw["Weight (kg)"]))
    else:
        weight = eu.PROFILES[pos][1] + round((height - eu.PROFILES[pos][0]) * 0.9)
        estimated.append("weight from height")
    codes = [NATIONS[n][0] for n in nations]
    source = f"RealGM {raw['RealGM URL']}" + (f" ({', '.join(estimated)})" if estimated else "")
    return {"url": raw["RealGM URL"], "team": raw["Current Team"], "firstName": first, "lastName": last, "dateOfBirth": birth,
            "nationality": codes[0], "secondNationality": next((c for c in codes[1:] if c != codes[0]), ""),
            "heightCm": max(150, min(250, height)), "weightKg": max(50, min(200, weight)), "position": pos, "secondaryPositions": second,
            "dataStatus": "Estimated" if estimated else "Verified", "source": source}


def tenure(seasons, team):
    """First season of the current run at the RealGM team (consecutive seasons up to the latest)."""
    years = sorted({y for y, t, _, _ in seasons if t == team}, reverse=True)
    start = years[0] if years else 2026
    for y in years[1:]:
        if y != start - 1:
            break
        start = y
    return start


def spells(seasons, team_clubs, before):
    """Past club spells at dataset clubs: one club per season (most games), consecutive seasons merged."""
    per_season = {}
    for year, team, _, games in seasons:
        if year < before and team in team_clubs and games > per_season.get(year, ("", -1))[1]:
            per_season[year] = (team_clubs[team], games)
    result = []
    for year in sorted(per_season):
        club = per_season[year][0]
        if result and result[-1][0] == club and result[-1][2] == year - 1:
            result[-1][2] = year
        else:
            result.append([club, year, year])
    return [(club, date(a, 7, 1).isoformat(), date(b + 1, 6, 30).isoformat()) for club, a, b in result]


def player_row(p, rating):
    row = {k: p[k] for k in ("id", "firstName", "lastName", "dateOfBirth", "nationality", "secondNationality", "heightCm", "weightKg", "position")}
    row["wingspanCm"] = ""
    row.update({name: rating["ratings"][name] for name, _ in eu.ATTRIBUTES})
    age = 2026 - int(p["dateOfBirth"][:4])
    row["experience"] = min(100, max(1, 5 + (age - 18) * 4 + rating["level"] // 5))
    row.update(reputation=rating["reputation"], potential=rating["potential"], dataStatus=p["dataStatus"],
               attributesStatus="Estimated", source=p["source"], secondaryPositions=p["secondaryPositions"])
    return row


def main():
    if "--source" in sys.argv:
        realgm_ratings.compact(sys.argv[sys.argv.index("--source") + 1])
    rnd = random.Random(26)
    known = europe_clubs()
    season = {"id": eu.uid("season", "2026-27"), "name": "2026-27", "startDate": SEASON_START.isoformat(), "endDate": eu.SEASON_END.isoformat()}
    data = {"format": "ebm-dataset", "version": 1, "countries": [], "clubs": [], "leagues": [], "divisions": [],
            "competitions": [], "seasons": [season], "participants": []}

    # Clubs and the RealGM team names that point at them (first: current team; europe names: curated history).
    divisions, team_clubs = [], {}
    for league_name, short, country, divs in LEAGUES:
        league = {"id": eu.uid("league", league_name), "name": league_name, "shortName": short, **({"country": country} if country else {})}
        data["leagues"].append(league)
        for level, div in enumerate(divs, start=1):
            division_id = eu.uid("division", league_name, str(level))
            data["divisions"].append({"id": division_id, "leagueId": league["id"], "name": div["name"], "level": level})
            competition = {"id": eu.uid("competition", league_name, div["name"]), "name": div["name"], "shortName": div["short"],
                           "type": "League", "divisionId": division_id, "rules": eu.rules(div, 7 if level == 1 else 8, level),
                           **({"country": country} if country else {})}
            data["competitions"].append(competition)
            clubs = [club_identity(e, known) for e in CLUBS[div["name"]].values()]
            assert len(clubs) == div["clubs"], div["name"]
            for team, club in zip(CLUBS[div["name"]], clubs):
                team_clubs[team] = club
            divisions.append((div, level, competition, clubs))

    # Players: real profiles; curated Serie A squads replace the RealGM Serie A players.
    seasons, ages = realgm_ratings.read_seasons(), realgm_ratings.read_ages()
    curated = {}
    for roster in eu.REAL_ROSTERS.values():
        for real in roster["players"]:
            curated[(player_ratings.normalise(f"{real['firstName']} {real['lastName']}"), int(real["dateOfBirth"][:4]))] = real
    lba_teams = set(CLUBS["Lega Basket Serie A"])
    profiles, skipped, calibration = [], [], {}
    for raw in realgm_ratings.read_profiles():
        p = profile(raw, ages.get(raw["RealGM URL"]))
        if p is None:
            skipped.append(raw["Player"])
            continue
        name = player_ratings.normalise(raw["Player"])
        name = next((k for k, v in player_ratings.ALIASES.items() if v == name), name)
        year = int(p["dateOfBirth"][:4])
        match = next((curated[(name, y)] for y in (year, year - 1, year + 1) if (name, y) in curated), None)
        if match is not None:
            key = (match["firstName"], match["lastName"], match["dateOfBirth"])
            calibration[p["url"]] = eu.STAT_RATINGS.get(key, match)["level"]
            continue  # the curated record is used
        club = team_clubs.get(p["team"]) if p["team"] not in lba_teams else None
        p["club"] = club
        p["group"] = next((d["name"] for d, _, _, cs in divisions if club in cs), FREE_AGENTS) if club else FREE_AGENTS
        profiles.append(p)
    ratings = realgm_ratings.rate(profiles, calibration, eu.ATTRIBUTES, eu.PROFILES)

    # Club strength from the squads: mean level of the best eight, on the scale of data/europe Serie A reputations.
    squads = {c["id"]: [] for _, _, _, cs in divisions for c in cs}
    for p in profiles:
        if p["club"]:
            squads[p["club"]["id"]].append(ratings[p["url"]]["level"])
    for roster in eu.REAL_ROSTERS.values():
        cid = known[roster["club"]]["id"]
        squads[cid] += [eu.STAT_RATINGS.get((x["firstName"], x["lastName"], x["dateOfBirth"]), x)["level"] for x in roster["players"]]
    quality = {cid: sum(sorted(v, reverse=True)[:8]) / max(1, min(8, len(v))) for cid, v in squads.items()}
    lba = [quality[c["id"]] for c in divisions[0][3]]
    reps = [known[n]["reputation"] for n in CLUBS["Lega Basket Serie A"].values()]
    slope = (max(reps) - min(reps)) / (max(lba) - min(lba))

    def club_reputation(q):  # half the slope below the weakest Serie A club, so second-division budgets stay workable
        q = q if q >= min(lba) else min(lba) - (min(lba) - q) / 2
        return round(max(10, min(99, max(reps) + (q - max(lba)) * slope)))

    players, contracts, history, rows_by_club = [], [], [], {}
    for div, level, competition, clubs in divisions:
        for rank, club in enumerate(sorted(clubs, key=lambda c: -quality[c["id"]])):
            reputation = club_reputation(quality[club["id"]])
            wage = round(reputation ** 3 * 12 / 1000) * 1000
            data["clubs"].append({**club, "foundedYear": known.get(club["name"], {}).get("foundedYear") or rnd.randint(1920, 2010),
                                  "reputation": reputation, "balance": int(round(wage * rnd.uniform(0.2, 0.6), -3)), "wageBudget": wage,
                                  "boardExpectation": eu.board(level, rank, div["clubs"], div), "dataStatus": "Unverified"})
            data["participants"].append({"id": eu.uid("entry", competition["id"], club["id"]), "competitionId": competition["id"],
                                         "seasonId": season["id"], "clubId": club["id"]})
    eu.add_cups(data, season, {div["name"]: clubs for div, _, _, clubs in divisions})
    eu.add_international(data)
    clubs_by_id = {c["id"]: c for c in data["clubs"]}
    club_ids = set(clubs_by_id)

    # Curated Serie A squads (same records as data/europe); spells outside the dataset are dropped.
    names = {**{n: {"id": c["id"]} for n, c in known.items()}, **{c["name"]: c for c in data["clubs"]}}
    level_of = {}
    for roster in eu.REAL_ROSTERS.values():
        for x in roster["players"]:
            level_of[eu.uid("player", x["firstName"], x["lastName"], x["dateOfBirth"][:4])] =                 eu.STAT_RATINGS.get((x["firstName"], x["lastName"], x["dateOfBirth"]), x)["level"]
        club = clubs_by_id[known[roster["club"]]["id"]]
        spells_before = len(history)
        eu.real_squad(roster, club, names, players, contracts, history)
        history[spells_before:] = [h for h in history[spells_before:] if h["clubId"] in club_ids]

    # RealGM players: contract from the first season of the current run at the club, end estimated; free agents none.
    by_club, team_ids = {}, {t: c["id"] for t, c in team_clubs.items()}
    for p in profiles:
        p["id"] = eu.uid("player", p["firstName"], p["lastName"], p["dateOfBirth"][:4])
        players.append(player_row(p, ratings[p["url"]]))
        own = seasons.get(p["url"], [])
        # Contracts run at most ten years (Contract.MaxLengthYears): earlier seasons at the club become a past spell.
        start_year = max(2020, tenure(own, p["team"])) if p["club"] else 2026
        for club_id, start, end in spells(own, team_ids, start_year if p["club"] else 2026):
            history.append({"id": eu.uid("spell", p["id"], club_id, start), "playerId": p["id"], "clubId": club_id, "startDate": start, "endDate": end})
        if p["club"]:
            by_club.setdefault(p["club"]["id"], []).append((p, start_year))
    for club_id, members in by_club.items():
        club = clubs_by_id[club_id]
        members.sort(key=lambda m: -ratings[m[0]["url"]]["level"])
        weights = [max(0.3, 1.6 - i * 0.1) for i in range(len(members))]
        for (p, start_year), weight in zip(members, weights):
            start = date(start_year, 7, 1)
            age = 2026 - int(p["dateOfBirth"][:4])
            end = date(max(2027, start_year + (1 if age >= 32 else rnd.randint(1, 3))), 6, 30)
            contracts.append({"id": eu.uid("contract", p["id"], start.isoformat()), "clubId": club_id, "playerId": p["id"],
                              "startDate": start.isoformat(), "endDate": end.isoformat(),
                              "annualSalary": int(round(club["wageBudget"] * 0.8 * weight / sum(weights), -3)),
                              "dataStatus": "Estimated", "source": "Club and first season from RealGM; salary and length estimated"})

    # Squads below twelve at the season start get generated players at the level of their weakest player.
    factory = eu.PlayerFactory(random.Random(27), {c: (n, e) for n, (c, e) in NATIONS.items()})
    for p in players:
        factory.born.setdefault((p["firstName"], p["lastName"]), []).append(date.fromisoformat(p["dateOfBirth"]))
    level_of.update({p["id"]: ratings[p["url"]]["level"] for p in profiles})
    generated = 0
    for club in data["clubs"]:
        active = [c for c in contracts if c["clubId"] == club["id"] and c["startDate"] <= SEASON_START.isoformat() <= c["endDate"]]
        missing = MIN_SQUAD - len(active)
        if missing <= 0:
            continue
        floor = min(level_of.get(c["playerId"], 50) for c in active) - 2
        positions = [eu.SQUAD_POSITIONS[(len(active) + i) % len(eu.SQUAD_POSITIONS)] for i in range(missing)]
        for pos in positions:
            player = factory.create(GENERATED_NAMES.get(club["country"], club["country"]), pos, floor)
            players.append(player)
            contracts.append({"id": eu.uid("contract", player["id"], "2026-07-01"), "clubId": club["id"], "playerId": player["id"],
                              "startDate": "2026-07-01", "endDate": "2027-06-30", "annualSalary": int(round(club["wageBudget"] * 0.01, -3)),
                              "dataStatus": "Generated", "source": ""})
            generated += 1

    assert len({p["id"] for p in players}) == len(players), "duplicate player"
    codes = {c["country"] for c in data["clubs"]} | {p["nationality"] for p in players} | {p["secondNationality"] for p in players} - {""}
    data["countries"] = [{"code": code, "name": COUNTRY_NAMES.get(code) or next(n for n, (c, _) in NATIONS.items() if c == code),
                          "euEea": next(e for c, e in NATIONS.values() if c == code)} for code in sorted(codes - {""})]
    folder = HERE.parent / "data" / SLUG
    folder.mkdir(exist_ok=True)
    (folder / f"{SLUG}-2026-27.json").write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    eu.write_csv(folder / f"players-{SLUG}-2026-27.csv", players)
    eu.write_csv(folder / f"contracts-{SLUG}-2026-27.csv", contracts)
    eu.write_csv(folder / f"playerHistory-{SLUG}-2026-27.csv", history)
    free = len(players) - len({c["playerId"] for c in contracts})
    print(f"{folder}: {len(data['countries'])} countries, {len(data['competitions'])} competitions, {len(data['clubs'])} clubs, "
          f"{len(players)} players ({free} free agents, {generated} generated top-ups), {len(contracts)} contracts, "
          f"{len(history)} past club spells; skipped (no nationality or age, or staff): {skipped}")


if __name__ == "__main__":
    main()
