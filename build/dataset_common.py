"""Shared dataset building blocks for build/generate-real-leagues-dataset.py: stable IDs (UUID v5), season dates,
competition formats (divisions, playoffs, domestic cups, international competitions), the real Serie A squads of
build/real-rosters, attribute profiles and generated top-up players. Formats are 2025-26 bases simplified to the
engine (docs/COUNTRY_REPORT.md, docs/CUP_FORMATS.md, docs/INTERNATIONAL_COMPETITIONS.md). Not runnable on its own.
"""
import csv
import json
import uuid
from datetime import date, timedelta
from pathlib import Path

import player_ratings

NS = uuid.UUID("5b0f3c8e-6d1a-4f44-9a57-0e5d2f7c1b15")
SEASON_START, SEASON_END = date(2026, 9, 19), date(2027, 6, 30)
REGULAR_END = date(2027, 5, 16)  # leaves about six weeks for playoffs
# Registration deadlines ("MM-dd", docs/TRANSFERS.md §9). Unverified simplification: top divisions and international
# competitions close at the end of February, second divisions a month later.
DEADLINE_TOP = "02-28"
DEADLINE_LOWER = "03-31"
TIE = ["HeadToHeadWins", "HeadToHeadPointDifference", "PointDifference", "PointsFor"]

ITA_SQUAD = {"maxForeignPlayers": 6, "minDomesticPlayers": 5}


def playoffs(teams, best_of, start=1, play_in=False):
    rules = {"teams": teams, "bestOf": best_of, "daysBetweenGames": 2}
    if start != 1:
        rules["fromPosition"] = start
    if play_in:
        rules["playIn"] = True
    return rules


def division(name, short, clubs, *, legs=2, po=None, promo=None, releg=0, squad=None, second=None, countries=None):
    return dict(name=name, short=short, clubs=clubs, legs=legs, po=po, promo=promo or {}, releg=releg,
                squad=squad, second=second, countries=countries)


def cup_round(name, legs=1, after=None):
    r = {"name": name}
    if legs != 1:
        r["legs"] = legs
    if after:
        r["daysAfter"] = after
    return r


QF, SF, F = cup_round("Quarter-finals"), cup_round("Semi-finals"), cup_round("Final")
FINAL_EIGHT = [cup_round("Quarter-finals"), cup_round("Semi-finals", after=2), cup_round("Final", after=1)]
FINAL_FOUR = [cup_round("Semi-finals", after=35), cup_round("Final", after=2)]

# Domestic cups (Step 17), 2025-26 formats simplified to the engine (docs/CUP_FORMATS.md §4, unverified):
# entry ("table", positions, after_round) = the top of the country's top division at that round (a Final Eight at
# the halfway point); ("clubs", division) = every club of the division from the cup's country. Final Eights and
# Final Fours are played on consecutive days at the better seed (no neutral venues). Sweden has no domestic cup.
CUPS = [
    ("ITA", "Coppa Italia Frecciarossa", "COPPA", ("table", 8, 15), dict(draw="Bracket", rounds=FINAL_EIGHT, offset=145)),
    ("ESP", "Copa del Rey", "COPA", ("table", 8, 17), dict(draw="Bracket", rounds=FINAL_EIGHT, offset=145)),
    ("GER", "BBL-Pokal", "POKAL", ("table", 16, 4), dict(rounds=[cup_round("Round of 16"), QF] + FINAL_FOUR, offset=35, days=42)),
    ("FRA", "Coupe de France", "CDF", ("clubs", "Betclic ÉLITE"), dict(draw="Open", host="LowerSeed", rounds=[cup_round("Round of 16"), QF] + FINAL_FOUR, offset=70, days=35)),
    ("GRE", "Greek Cup", "CUP", ("clubs", "Stoiximan GBL"), dict(rounds=[cup_round("Round of 16"), QF] + FINAL_FOUR, offset=60, days=35)),
    ("TUR", "Turkish Cup", "CUP", ("clubs", "Basketbol Süper Ligi"),
     dict(draw="Bracket", groups={"groups": 4, "legs": 1, "advance": 2, "daysBetweenRounds": 14},
          rounds=[cup_round("Quarter-finals", after=60), cup_round("Semi-finals", after=2), cup_round("Final", after=1)], offset=40)),
    ("GBR", "SLB Cup", "CUP", ("clubs", "Super League Basketball"),
     dict(draw="Bracket", groups={"groups": 2, "legs": 1, "advance": 2, "daysBetweenRounds": 7},
          rounds=[cup_round("Semi-finals", legs=2, after=21), cup_round("Final", after=28)], offset=30)),
    ("LTU", "Karaliaus Mindaugo taurė", "KMT", ("clubs", "Betsafe-LKL"), dict(rounds=[cup_round("First round"), cup_round("Quarter-finals", legs=2)] + FINAL_FOUR, offset=50, days=28)),
    ("SRB", "Kup Radivoja Koraća", "KRK", ("table", 8, 15), dict(draw="Bracket", rounds=FINAL_EIGHT, offset=145)),
    ("CRO", "Kup Krešimira Ćosića", "KKC", ("clubs", "Premijer liga"), dict(rounds=[cup_round("First round"), QF] + FINAL_FOUR, offset=50, days=35)),
    ("SLO", "Pokal Spar", "POKAL", ("table", 8, 9), dict(draw="Bracket", rounds=FINAL_EIGHT, offset=145)),
    ("BEL", "Beker van België", "CUP", ("clubs", "BNXT League"), dict(rounds=[cup_round("First round"), QF, cup_round("Semi-finals", legs=2), cup_round("Final", after=35)], offset=50, days=35)),
    ("NED", "NBB Beker", "CUP", ("clubs", "BNXT League"), dict(rounds=[cup_round("First round"), QF] + FINAL_FOUR, offset=50, days=35)),
    ("POR", "Taça de Portugal", "TAÇA", ("clubs", "Liga Betclic"), dict(draw="Open", host="LowerSeed", rounds=[cup_round("First round"), QF] + FINAL_FOUR, offset=50, days=35)),
    ("POL", "Puchar Polski", "PP", ("table", 8, 15), dict(draw="Bracket", rounds=FINAL_EIGHT, offset=145)),
    ("CZE", "Český pohár", "CP", ("clubs", "Kooperativa NBL"), dict(rounds=[cup_round("First round"), QF] + FINAL_FOUR, offset=50, days=35)),
    ("FIN", "Suomen cup", "CUP", ("clubs", "Korisliiga"), dict(rounds=[cup_round("First round"), QF] + FINAL_FOUR, offset=50, days=35)),
]


def add_cups(data, season, leagues, division_clubs):
    """Adds the domestic cups whose source division is in the dataset; leagues: (name, short name, country, divisions top
    first); division_clubs: division name -> club records."""
    top = {country: divs[0]["name"] for _, _, country, divs in leagues if country}
    for country, name, short, entry, fmt in CUPS:
        source = top.get(country) if entry[0] == "table" else entry[1]
        if source not in division_clubs:
            continue
        cup = {"draw": fmt.get("draw", "Seeded"), "startOffsetDays": fmt["offset"], "daysBetweenRounds": fmt.get("days", 1), "rounds": fmt["rounds"]}
        if fmt.get("host"):
            cup["host"] = fmt["host"]
        if fmt.get("groups"):
            cup["groupStage"] = fmt["groups"]
        clubs = [c for c in division_clubs[source] if c["country"] == country]
        teams = entry[1] if entry[0] == "table" else len(clubs)
        if entry[0] == "table":
            cup["qualification"] = {"level": 1, "positions": entry[1], "afterRound": entry[2]}
        competition = {"id": uid("competition", "cup", country, name), "name": name, "shortName": short, "type": "Cup", "country": country,
                       "rules": {"registration": {"minTeams": teams, "maxTeams": teams}, "cup": cup}}
        data["competitions"].append(competition)
        if entry[0] == "clubs":
            for club in clubs:
                data["participants"].append({"id": uid("entry", competition["id"], club["id"]), "competitionId": competition["id"],
                                             "seasonId": season["id"], "clubId": club["id"]})

def neutral(r):
    return {**r, "neutral": True}


def league(country, clubs):
    return {"type": "League", "countries": [country], "clubs": clubs}


FILL = {"type": "Reputation", "clubs": 0}  # wildcards by reputation until the competition is full

# International club competitions (Step 18, docs/INTERNATIONAL_COMPETITIONS.md), 2025-26 formats simplified to the
# engine, unverified: (name, short name, tier, (min, max) clubs, entry slots, group stage, knockout rounds,
# start offset, days between group rounds, rewards). Clubs qualify every season from the previous season's
# tables (the first season: club reputation); a club plays one of them only (the lowest tier).
INTERNATIONAL = [
    ("Turkish Airlines EuroLeague", "EL", 1, (16, 20),
     [{"type": "Holder"}, {"type": "Winner", "competition": "EC"}, league("ESP", 3), league("TUR", 2), league("GRE", 2),
      league("ITA", 2), league("FRA", 2), league("SRB", 2), league("GER", 1), league("LTU", 1), FILL],
     {"groups": 1, "legs": 2, "advance": 8, "daysBetweenRounds": 6},
     [cup_round("Playoffs", legs=2, after=7), neutral(cup_round("Semi-finals", after=10)), neutral(cup_round("Final", after=2))], 11, 6,
     {"entry": 1_000_000, "win": 50_000, "knockout": 300_000, "winner": 2_000_000, "runnerUp": 1_000_000,
      "reputationKnockout": 1, "reputationWinner": 3, "reputationRunnerUp": 2}),
    ("BKT EuroCup", "EC", 2, (16, 20),
     [{"type": "Holder"}, league("ESP", 2), league("ITA", 2), league("GER", 2), league("FRA", 2), league("TUR", 2),
      league("GRE", 1), league("LTU", 1), league("SRB", 1), league("SLO", 1), league("POL", 1), FILL],
     {"groups": 2, "legs": 2, "advance": 6, "daysBetweenRounds": 7},
     [cup_round("Eighth-finals", after=14), cup_round("Quarter-finals", after=7), cup_round("Semi-finals", after=7), cup_round("Final", legs=2, after=7)], 12, 7,
     {"entry": 400_000, "win": 20_000, "knockout": 100_000, "winner": 800_000, "runnerUp": 400_000,
      "reputationKnockout": 1, "reputationWinner": 2, "reputationRunnerUp": 1}),
    ("Basketball Champions League", "BCL", 3, (24, 32),
     [{"type": "Holder"}] + [league(c, 2) for c in ("ESP", "ITA", "GER", "FRA", "TUR", "GRE")]
     + [league(c, 1) for c in ("LTU", "SRB", "CRO", "SLO", "BEL", "NED", "POR", "POL", "CZE", "FIN", "SWE", "GBR")] + [FILL],
     {"groups": 8, "legs": 2, "advance": 2, "daysBetweenRounds": 7},
     [cup_round("Round of 16", legs=2, after=28), cup_round("Quarter-finals", legs=2, after=14),
      neutral(cup_round("Semi-finals", after=21)), neutral(cup_round("Final", after=2))], 10, 7,
     {"entry": 150_000, "win": 10_000, "knockout": 50_000, "winner": 1_000_000, "runnerUp": 500_000,
      "reputationKnockout": 1, "reputationWinner": 2, "reputationRunnerUp": 1}),
    ("FIBA Europe Cup", "FEC", 4, (24, 32),
     [{"type": "Holder"}] + [league(c, 1) for c in ("ESP", "ITA", "GER", "FRA", "TUR", "GRE", "LTU", "SRB", "CRO", "SLO",
                                                    "BEL", "NED", "POR", "POL", "CZE", "FIN", "SWE", "GBR")] + [FILL],
     {"groups": 8, "legs": 2, "advance": 2, "daysBetweenRounds": 7},
     [cup_round("Round of 16", legs=2, after=21), cup_round("Quarter-finals", legs=2, after=14), cup_round("Semi-finals", legs=2, after=14),
      cup_round("Final", legs=2, after=14)], 9, 7,
     {"entry": 50_000, "win": 5_000, "knockout": 20_000, "winner": 300_000, "runnerUp": 150_000, "reputationWinner": 1}),
]


def add_international(data):
    """Adds the international competitions; their clubs are qualified by the game every season (no entries here)."""
    for name, short, tier, (low, high), slots, groups, ko, offset, days, rewards in INTERNATIONAL:
        cup = {"entry": {"tier": tier, "slots": slots}, "groupStage": groups, "draw": "Bracket", "startOffsetDays": offset,
               "daysBetweenRounds": days, "daysBetweenLegs": 7, "rounds": ko, "rewards": rewards}
        data["competitions"].append({"id": uid("competition", "international", name), "name": name, "shortName": short, "type": "Cup",
                                     "rules": {"pointsForWin": 2, "pointsForLoss": 1, "registration": {"minTeams": low, "maxTeams": high, "deadline": DEADLINE_TOP},
                                               "tieBreakers": TIE, "cup": cup}})


def uid(*parts):
    return str(uuid.uuid5(NS, "|".join(parts)))


def rounds(clubs, legs):
    return (clubs if clubs % 2 else clubs - 1) * legs


def second_phase_rounds(second, clubs, countries):
    if not second:
        return 0
    per_country = len(countries) if second.get("byCountry") else 1
    sizes = [min(g["to"], clubs) - g["from"] + 1 for g in second["groups"]]
    return max(rounds(max(s * per_country, 2), 2) for s in sizes)


def rules(div, offset, level=1):
    total = rounds(div["clubs"], div["legs"]) + second_phase_rounds(div["second"], div["clubs"], div["countries"] or [])
    first = SEASON_START.toordinal() + offset
    days = min(7, (REGULAR_END.toordinal() - first) // (total - 1))
    result = {
        "pointsForWin": 2, "pointsForLoss": 1,
        "registration": {"minTeams": div["clubs"], "maxTeams": div["clubs"], "deadline": DEADLINE_TOP if level == 1 else DEADLINE_LOWER},
        "regularSeason": {"legs": div["legs"], "daysBetweenRounds": days, "startOffsetDays": offset},
        "tieBreakers": TIE,
    }
    if div["countries"]:
        result["registration"].update(sameCountry=False, countries=div["countries"])
    if div["squad"]:
        result["registration"]["squad"] = div["squad"]
    if div["second"]:
        result["secondPhase"] = {**div["second"], "legs": 2, "daysBetweenRounds": days}
    if div["po"]:
        result["playoffs"] = div["po"]
    if div["promo"]:
        result["promotion"] = div["promo"]
    if div["releg"]:
        result["relegation"] = {"direct": div["releg"]}
    return result


def board(level, rank, size, div):
    if level == 1:
        if rank < 2:
            return "WinTitle"
        if div["po"] and rank < div["po"]["teams"]:
            return "ReachPlayoffs"
        return "AvoidRelegation" if rank >= size - max(div["releg"], 1) - 1 else "MidTable"
    return "WinPromotion" if rank < 3 else ("ReachPlayoffs" if div["po"] and rank < div["po"]["teams"] else "MidTable")


# ---------------------------------------------------------------------------------------------------------------------
# Players: curated real squads and generated top-up players.
# ---------------------------------------------------------------------------------------------------------------------

# Real squads (build/real-rosters/*.json, curated from cited sources) (Serie A). Profiles are
# Verified when the file cites a source, attributes are always Estimated, contract terms Estimated (docs/PLAYER_DATABASE.md).
REAL_ROSTERS = {r["club"]: r for r in (json.loads(f.read_text(encoding="utf-8"))
                                       for f in sorted((Path(__file__).resolve().parent / "real-rosters").glob("*.json")))}

# Name pools for generated players ("first names; last names"; '_' joins a multi-word name). Common names only.
NAMES = {
    "ITA": "Marco Luca Matteo Andrea Alessandro Davide Simone Federico Lorenzo Riccardo Nicola Stefano Giacomo Tommaso Francesco Gabriele Leonardo Pietro Diego Michele; Rossi Bianchi Ferrari Russo Romano Colombo Ricci Marino Greco Bruno Gallo Conti De_Luca Mancini Costa Giordano Rizzo Lombardi Moretti Barbieri Fontana Santoro Mariani Rinaldi Caruso Ferri Galli Martini Leone Longo",
    "ESP": "Alejandro Pablo Sergio Javier Daniel Adrián Álvaro Carlos Diego Jorge Marc Pau Iván Rubén Mario Hugo Guillermo Raúl Víctor Alberto; García Fernández González Rodríguez López Martínez Sánchez Pérez Gómez Martín Jiménez Ruiz Hernández Díaz Moreno Muñoz Álvarez Romero Navarro Torres Domínguez Vázquez Ramos Gil Serrano Blanco Molina Morales Suárez Ortega",
    "GER": "Lukas Jonas Leon Finn Niklas Paul Felix Jan Tim Moritz Maximilian Julian Tobias David Philipp Johannes Simon Fabian Florian Benedikt; Müller Schmidt Schneider Fischer Weber Meyer Wagner Becker Schulz Hoffmann Koch Richter Klein Wolf Schröder Neumann Schwarz Zimmermann Braun Krüger Hofmann Hartmann Lange Schmitt Werner Krause Meier Lehmann Köhler Kaiser",
    "FRA": "Hugo Lucas Théo Nathan Louis Mathis Enzo Tom Maxime Antoine Thomas Clément Alexandre Baptiste Paul Quentin Romain Julien Yanis Bastien; Martin Bernard Dubois Thomas Robert Richard Petit Durand Leroy Moreau Simon Laurent Lefebvre Michel Garcia David Bertrand Roux Vincent Fournier Morel Girard André Mercier Dupont Lambert Bonnet François Legrand Faure",
    "GRE": "Georgios Dimitrios Konstantinos Ioannis Nikolaos Panagiotis Vasileios Christos Athanasios Michail Evangelos Spyridon Antonios Ilias Stavros Theodoros Alexandros Emmanouil Charalampos Petros; Papadopoulos Papadakis Nikolaidis Georgiou Karagiannis Vasileiou Dimitriou Konstantinidis Ioannou Pappas Oikonomou Makris Christodoulou Antoniou Anagnostou Theodorou Alexiou Kyriakou Michailidis Panagiotopoulos Stavropoulos Athanasiou Mavridis Karalis Lazaridis Sotiriou Petridis Zervas Raptis Galanis",
    "TUR": "Mehmet Mustafa Ahmet Ali Hüseyin Hasan İbrahim Emre Burak Can Kerem Yusuf Ömer Furkan Berk Arda Onur Doğukan Sertaç Tolga; Yılmaz Kaya Demir Şahin Çelik Yıldız Yıldırım Öztürk Aydın Özdemir Arslan Doğan Kılıç Aslan Çetin Kara Koç Kurt Özkan Şimşek Polat Erdem Korkmaz Güneş Aksoy Bulut Turan Karaca Ünal Tekin",
    "GBR": "Oliver Harry George Jack Jacob Charlie Thomas Oscar William James Joshua Alfie Callum Ryan Kyle Jordan Liam Daniel Connor Ethan; Smith Jones Williams Taylor Brown Davies Evans Wilson Thomas Johnson Roberts Robinson Thompson Wright Walker White Edwards Hughes Green Hall Lewis Harris Clarke Patel Jackson Wood Turner Martin Cooper Hill",
    "LTU": "Lukas Matas Jonas Mantas Tomas Dovydas Paulius Rokas Martynas Arnas Ignas Marius Deividas Justinas Domantas Gytis Karolis Vytautas Laurynas Eimantas; Kazlauskas Jankauskas Petrauskas Stankevičius Vasiliauskas Žukauskas Butkus Paulauskas Urbonas Kavaliauskas Navickas Ramanauskas Savickas Rimkus Baranauskas Sakalauskas Žemaitis Valančius Mockus Lukoševičius Bieliauskas Grigas Adomaitis Jonaitis Kairys Gudaitis Mažeika Norkus Bružas Šimkus",
    "SRB": "Nikola Stefan Marko Luka Aleksa Lazar Filip Nemanja Miloš Đorđe Vuk Ognjen Dušan Uroš Bogdan Vladimir Strahinja Mihajlo Andrija Petar; Jovanović Petrović Nikolić Marković Đorđević Stojanović Ilić Stanković Pavlović Milošević Popović Kostić Simić Lukić Mitrović Todorović Živković Ristić Kovačević Savić Tomić Đukić Vasić Milovanović Marinković Babić Lazić Radović Janković Stevanović",
    "CRO": "Luka Ivan Marko Josip Mateo Petar Filip Karlo Ante Dario Mario Tomislav Domagoj Nikola Antonio Matej Roko Lovro Fran Bruno; Horvat Kovačević Babić Marić Jurić Novak Kovačić Knežević Vuković Marković Petrović Matić Tomić Pavlović Božić Blažević Grgić Perić Šarić Radić Lovrić Vidović Perković Bošnjak Pavić Barišić Rukavina Jukić Brkić Šimić",
    "SLO": "Luka Jan Žiga Nejc Matej Tilen Jaka Anže Gašper Rok Miha Blaž Klemen Aljaž Žan Urban Tim Martin Jure David; Novak Horvat Kovačič Krajnc Zupančič Potočnik Kovač Mlakar Kos Vidmar Golob Turk Kralj Zupan Bizjak Hribar Kozole Korošec Rozman Kotnik Oblak Petek Žagar Kolar Hočevar Košir Koren Klemenčič Zajc Medved",
    "BEL": "Lucas Noah Arthur Louis Liam Adam Jules Victor Mathis Nathan Thomas Lars Wout Senne Milan Arne Robbe Jens Bram Kobe; Peeters Janssens Maes Jacobs Mertens Willems Claes Goossens Wouters Dubois Lambert Dupont Martens Vermeulen Hermans Leclercq Pauwels Michiels Desmet Smets Cools Verstraeten Aerts Bogaert Lemaire Declercq Wauters Renard Hendrickx Lemmens",
    "NED": "Daan Sem Lucas Levi Finn Milan Bram Thijs Jesse Ruben Lars Stijn Sven Jasper Tim Koen Niels Luuk Joris Thomas; De_Jong Jansen De_Vries Van_den_Berg Bakker Visser Smit Meijer De_Boer Mulder De_Groot Bos Vos Peters Hendriks Van_Leeuwen Dekker Brouwer De_Wit Dijkstra Smits De_Graaf Van_der_Meer Kok Jacobs Vermeulen Van_den_Broek Willems Schouten Janssen",
    "POR": "João Francisco Martim Rodrigo Santiago Tomás Afonso Duarte Miguel Gonçalo Diogo Pedro Rafael Tiago Gabriel Bernardo Rúben André Bruno Nuno; Silva Santos Ferreira Pereira Oliveira Costa Rodrigues Martins Jesus Sousa Fernandes Gonçalves Gomes Lopes Marques Alves Almeida Ribeiro Pinto Carvalho Teixeira Moreira Correia Mendes Nunes Soares Vieira Monteiro Cardoso Rocha",
    "POL": "Jakub Kacper Szymon Filip Jan Antoni Mateusz Bartosz Michał Wojciech Kamil Mikołaj Adam Piotr Łukasz Paweł Tomasz Dawid Krzysztof Igor; Nowak Kowalski Wiśniewski Wójcik Kowalczyk Kamiński Lewandowski Zieliński Szymański Woźniak Dąbrowski Kozłowski Jankowski Mazur Kwiatkowski Krawczyk Piotrowski Grabowski Nowakowski Pawłowski Michalski Nowicki Adamczyk Dudek Zając Wieczorek Jabłoński Król Majewski Olszewski",
    "CZE": "Jakub Jan Tomáš Matěj Adam Ondřej Vojtěch Lukáš Filip David Martin Petr Michal Daniel Šimon Dominik Patrik Marek Josef Václav; Novák Svoboda Novotný Dvořák Černý Procházka Kučera Veselý Horák Němec Marek Pokorný Pospíšil Hájek Jelínek Král Růžička Beneš Fiala Sedláček Doležal Zeman Kolář Navrátil Čermák Vaněk Urban Blažek Kříž Kovář",
    "FIN": "Juho Eetu Elias Onni Aleksi Veeti Lauri Joona Niko Mikko Antti Ville Joonas Sami Teemu Jani Oskari Valtteri Tuomas Arttu; Korhonen Virtanen Mäkinen Nieminen Mäkelä Hämäläinen Laine Heikkinen Koskinen Järvinen Lehtonen Lehtinen Saarinen Salminen Heinonen Niemi Heikkilä Kinnunen Salonen Turunen Salo Laitinen Tuominen Rantanen Karjalainen Jokinen Mattila Savolainen Lahtinen Ahonen",
    "SWE": "William Lucas Liam Elias Hugo Oliver Alexander Noah Adam Filip Oscar Viktor Erik Isak Anton Axel Emil Gustav Albin Linus; Andersson Johansson Karlsson Nilsson Eriksson Larsson Olsson Persson Svensson Gustafsson Pettersson Jonsson Jansson Hansson Bengtsson Jönsson Lindberg Jakobsson Magnusson Olofsson Lindström Lindqvist Lindgren Berg Axelsson Bergström Lundberg Lundgren Mattsson Berglund",
    "USA": "James Michael Chris Brandon Justin Tyler Jalen Marcus Darius Isaiah Jordan Kevin Malik Trey Andre Devin Jaylen Cameron Terrence Xavier Jamal Elijah Caleb Jarrett Kendall Quentin Myles Damon Reggie Corey; Johnson Williams Brown Jones Davis Miller Wilson Moore Taylor Anderson Thomas Jackson White Harris Martin Thompson Robinson Clark Lewis Walker Allen Young King Wright Scott Green Baker Adams Nelson Carter Mitchell Roberts Turner Phillips Campbell Parker Evans Edwards Collins Stewart Morris Reed Cook Bell Murphy Bailey Cooper Richardson Cox Howard",
}

# Mirrors Ebm.Domain.Players.PositionProfile and PlayerCatalog (attribute order and categories).
ATTRIBUTES = [("closeShot", "S"), ("midRange", "S"), ("threePoint", "S"), ("freeThrow", "S"), ("layup", "F"), ("dunk", "F"),
              ("postMoves", "F"), ("passing", "P"), ("courtVision", "P"), ("ballHandling", "P"), ("decisionMaking", "P"),
              ("perimeterDefense", "D"), ("interiorDefense", "D"), ("steal", "D"), ("block", "D"), ("defensivePositioning", "D"),
              ("offensiveRebound", "R"), ("defensiveRebound", "R"), ("speed", "X"), ("strength", "X"), ("vertical", "X"),
              ("agility", "X"), ("stamina", "X"), ("concentration", "M"), ("leadership", "M"), ("consistency", "M"), ("workRate", "M")]
PROFILES = {"PG": (186, 84, {"P": 10, "R": -10, "F": -4}), "SG": (194, 90, {"S": 8, "R": -6}), "SF": (200, 96, {}),
            "PF": (205, 104, {"R": 8, "F": 4, "P": -6}), "C": (211, 112, {"R": 10, "F": 6, "S": -10, "P": -10})}
SQUAD_POSITIONS = ["PG", "SG", "SF", "PF", "C", "PG", "SG", "SF", "PF", "C", "SG", "PF"]  # starters first
CONTRACT_SEASON = date(2026, 7, 1)  # contracts start on 1 July and end on 30 June
# Level, potential, reputation and box-score attributes of real players with statistics (build/player_ratings.py);
# players without statistics keep the hand estimates of their roster file.
STAT_RATINGS = player_ratings.rate_rosters(REAL_ROSTERS.values(), ATTRIBUTES, PROFILES)


class PlayerFactory:
    def __init__(self, rnd, countries):
        self.rnd = rnd
        self.eu = [c for c, (_, eu) in countries.items() if eu and c in NAMES]
        self.names = {c: ([n.replace("_", " ") for n in v.split(";")[0].split()], [n.replace("_", " ") for n in v.split(";")[1].split()])
                      for c, v in NAMES.items()}
        self.born = {}  # (first, last) -> birth dates: no two players share a name with birth dates within 400 days

    def name(self, nationality):
        firsts, lasts = self.names[nationality]
        while True:
            first, last = self.rnd.choice(firsts), self.rnd.choice(lasts)
            age = min(35, max(19, int(self.rnd.triangular(19, 35, 26))))
            birth = date(CONTRACT_SEASON.year - age, 1, 1) + timedelta(days=self.rnd.randint(0, 364))
            dates = self.born.setdefault((first, last), [])
            if all(abs((birth - d).days) > 400 for d in dates):
                dates.append(birth)
                return first, last, birth

    def create(self, nationality, position, quality, second=None):
        rnd = self.rnd
        first, last, birth = self.name(nationality)
        height, weight, offsets = PROFILES[position]
        age = CONTRACT_SEASON.year - birth.year
        player = {"id": uid("player", first, last, birth.isoformat()), "firstName": first, "lastName": last,
                  "dateOfBirth": birth.isoformat(), "nationality": nationality, "secondNationality": second or "",
                  "heightCm": height + rnd.randint(-5, 5), "weightKg": weight + rnd.randint(-6, 6), "position": position}
        player["wingspanCm"] = player["heightCm"] + rnd.randint(0, 9)
        for name, category in ATTRIBUTES:
            player[name] = min(95, max(20, quality + offsets.get(category, 0) + rnd.randint(-6, 6)))
        player["experience"] = min(100, max(1, 5 + (age - 18) * 4 + quality // 5))
        player["reputation"] = min(100, max(1, quality - 25 + rnd.randint(-4, 4)))
        player["potential"] = min(95, max(quality, quality + (27 - age) * 2) + rnd.randint(0, 4))
        player.update(dataStatus="Generated", attributesStatus="", source="", secondaryPositions="")
        return player


def real_squad(roster, club, clubs_by_name, players, contracts, history):
    """A curated squad: sourced profiles, attributes estimated from level and statistics, estimated contract terms."""
    rated = {id(x): STAT_RATINGS.get((x["firstName"], x["lastName"], x["dateOfBirth"]), x) for x in roster["players"]}
    ranked = sorted(roster["players"], key=lambda x: -rated[id(x)]["level"])
    weights = {id(x): max(0.3, 1.6 - i * 0.1) for i, x in enumerate(ranked)}
    wage_pool = club["wageBudget"] * 0.8
    for real in roster["players"]:
        position, level, estimate = real["position"], rated[id(real)]["level"], rated[id(real)]
        birth = date.fromisoformat(real["dateOfBirth"])
        age = CONTRACT_SEASON.year - birth.year
        player = {"id": uid("player", real["firstName"], real["lastName"], real["dateOfBirth"][:4]), "firstName": real["firstName"],
                  "lastName": real["lastName"], "dateOfBirth": real["dateOfBirth"], "nationality": real["nationality"],
                  "secondNationality": real.get("secondNationality", ""), "heightCm": real["heightCm"], "weightKg": real["weightKg"],
                  "position": position, "wingspanCm": ""}
        offsets = PROFILES[position][2]
        for name, category in ATTRIBUTES:
            player[name] = estimate["ratings"].get(name, min(95, max(20, level + offsets.get(category, 0))))
        player["experience"] = min(100, max(1, 5 + (age - 18) * 4 + level // 5))
        player["reputation"], player["potential"] = estimate["reputation"], estimate["potential"]
        player.update(dataStatus=real.get("dataStatus", "Verified"), attributesStatus="Estimated",
                      source=real.get("source", roster["profileSource"]), secondaryPositions=real.get("secondaryPositions", ""))
        players.append(player)
        start = real["joined"] or CONTRACT_SEASON.isoformat()
        contracts.append({"id": uid("contract", player["id"], start), "clubId": club["id"], "playerId": player["id"], "startDate": start,
                          "endDate": real.get("left", date(CONTRACT_SEASON.year + 1, 6, 30).isoformat()),
                          "annualSalary": int(round(wage_pool * weights[id(real)] / sum(weights.values()), -3)),
                          "dataStatus": "Estimated", "source": roster["contractSource"]})
        for spell in real["history"]:
            previous = clubs_by_name[spell["club"]]
            history.append({"id": uid("spell", player["id"], previous["id"], spell["startDate"]), "playerId": player["id"],
                            "clubId": previous["id"], "startDate": spell["startDate"], "endDate": spell["endDate"]})


def write_csv(path, rows):
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
