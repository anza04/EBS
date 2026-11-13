"""Generates data/europe/europe-2026-27.json: the Step 15 European league dataset.

Competition names, division hierarchy, formats, promotion/relegation and registration limits follow the official
competitions (docs/COUNTRY_REPORT.md lists the verification status). Clubs are fictional placeholders named after
real cities, until the real-world database of Step 16. Deterministic: the same script always writes the same file
(UUID v5 IDs, seeded numbers). Run: python build/generate-europe-dataset.py
"""
import json
import random
import uuid
from datetime import date
from pathlib import Path

NS = uuid.UUID("5b0f3c8e-6d1a-4f44-9a57-0e5d2f7c1b15")
SEASON_START, SEASON_END = date(2026, 9, 19), date(2027, 6, 30)
REGULAR_END = date(2027, 5, 16)  # leaves about six weeks for playoffs
TIE = ["HeadToHeadWins", "HeadToHeadPointDifference", "PointDifference", "PointsFor"]

NICKNAMES = ["Comets", "Meteors", "Zenith", "Nova", "Orbit", "Polaris", "Quasar", "Equinox", "Vertex", "Summit",
             "Horizon", "Tempest", "Cyclones", "Glacier", "Granite", "Cobalt", "Ember", "Aurora", "Solstice", "Lynx",
             "Herons", "Bisons", "Ravens", "Stallions", "Mustangs", "Condors", "Pumas", "Jaguars", "Cougars", "Orcas",
             "Ibis", "Kestrels", "Ospreys", "Gryphons", "Sentinels", "Pilots", "Voyagers", "Navigators", "Rockets", "Pulsar"]

# code, name, EU/EEA/CH, strength (reputation of the best club), cities.
COUNTRIES = {
    "ITA": ("Italy", True, 84, ["Milano", "Bologna", "Roma", "Napoli", "Torino", "Venezia", "Firenze", "Genova", "Bari", "Palermo", "Verona", "Padova", "Trieste", "Brescia", "Trento", "Sassari", "Pesaro", "Varese", "Cremona", "Reggio Emilia", "Udine", "Pistoia", "Cantù", "Treviso", "Forlì", "Rimini", "Cagliari", "Livorno", "Ferrara", "Bergamo"]),
    "ESP": ("Spain", True, 88, ["Madrid", "Barcelona", "Valencia", "Sevilla", "Málaga", "Bilbao", "Zaragoza", "Vitoria", "Murcia", "Granada", "Badalona", "Manresa", "Girona", "Tenerife", "Gran Canaria", "Andorra", "Burgos", "Valladolid", "León", "Oviedo", "Palencia", "Cáceres", "Huesca", "Lleida", "Ourense", "Castellón", "Alicante", "Almería", "Córdoba", "Santander"]),
    "GER": ("Germany", True, 82, ["München", "Berlin", "Hamburg", "Frankfurt", "Köln", "Ulm", "Bamberg", "Oldenburg", "Bonn", "Ludwigsburg", "Würzburg", "Braunschweig", "Göttingen", "Vechta", "Chemnitz", "Heidelberg", "Bayreuth", "Gießen", "Jena", "Rostock", "Trier", "Karlsruhe", "Kiel", "Leipzig", "Dresden", "Hagen", "Bremen", "Münster", "Nürnberg", "Tübingen"]),
    "FRA": ("France", True, 82, ["Paris", "Lyon", "Monaco", "Strasbourg", "Nanterre", "Le Mans", "Dijon", "Limoges", "Cholet", "Pau", "Bourg-en-Bresse", "Nancy", "Gravelines", "Le Portel", "Saint-Quentin", "Roanne", "Orléans", "Boulogne", "Rouen", "Nantes", "Antibes", "Évreux", "Fos-sur-Mer", "Blois", "Chalon", "Vichy", "Denain", "Poitiers", "Quimper", "Caen"]),
    "GRE": ("Greece", True, 86, ["Athína", "Peiraiás", "Thessaloníki", "Pátra", "Irákleio", "Lárisa", "Vólos", "Ioánnina", "Kavála", "Kalamáta", "Rhódos", "Chalkída", "Tríkala", "Kozáni", "Sérres", "Alexandroúpoli", "Lamía", "Kérkyra", "Chaniá", "Xánthi", "Kastoriá", "Véroia", "Agrínio", "Kórinthos", "Dráma", "Kardítsa", "Náfplio", "Préveza"]),
    "TUR": ("Turkey", False, 88, ["İstanbul", "Ankara", "İzmir", "Bursa", "Antalya", "Konya", "Gaziantep", "Mersin", "Kayseri", "Eskişehir", "Samsun", "Trabzon", "Denizli", "Sakarya", "Manisa", "Bandırma", "Afyon", "Bahçeşehir", "Karşıyaka", "Adana", "Malatya", "Erzurum", "Tekirdağ", "Balıkesir", "Aydın", "Muğla", "Kocaeli", "Edirne", "Bolu", "Sivas", "Ordu", "Rize"]),
    "GBR": ("United Kingdom", False, 62, ["London", "Manchester", "Leicester", "Sheffield", "Newcastle", "Glasgow", "Bristol", "Cardiff", "Birmingham", "Leeds", "Nottingham", "Liverpool", "Edinburgh", "Belfast", "Southampton", "Oxford", "Cambridge", "Reading", "Derby", "Coventry", "Plymouth", "Brighton", "Norwich", "Swansea"]),
    "LTU": ("Lithuania", True, 80, ["Vilnius", "Kaunas", "Klaipėda", "Šiauliai", "Panevėžys", "Alytus", "Marijampolė", "Utena", "Jonava", "Kėdainiai", "Telšiai", "Tauragė", "Ukmergė", "Visaginas", "Mažeikiai", "Plungė", "Kretinga", "Radviliškis", "Druskininkai", "Palanga", "Elektrėnai", "Biržai", "Rokiškis", "Raseiniai"]),
    "SRB": ("Serbia", False, 80, ["Beograd", "Novi Sad", "Niš", "Kragujevac", "Subotica", "Čačak", "Kraljevo", "Smederevo", "Zrenjanin", "Pančevo", "Novi Pazar", "Šabac", "Valjevo", "Leskovac", "Vršac", "Sombor", "Užice", "Kruševac", "Jagodina", "Požarevac", "Vranje", "Zaječar", "Loznica", "Bor", "Pirot", "Kikinda", "Sremska Mitrovica", "Inđija", "Obrenovac", "Ćuprija", "Aranđelovac", "Paraćin"]),
    "CRO": ("Croatia", True, 74, ["Zagreb", "Split", "Rijeka", "Zadar", "Osijek", "Šibenik", "Dubrovnik", "Pula", "Karlovac", "Varaždin", "Sisak", "Slavonski Brod", "Vinkovci", "Vukovar", "Čakovec", "Koprivnica", "Bjelovar", "Makarska", "Samobor", "Velika Gorica", "Kutina", "Požega", "Poreč", "Zabok"]),
    "SLO": ("Slovenia", True, 74, ["Ljubljana", "Maribor", "Celje", "Kranj", "Koper", "Novo Mesto", "Velenje", "Ptuj", "Nova Gorica", "Murska Sobota", "Domžale", "Škofja Loka", "Kamnik", "Postojna", "Trbovlje", "Jesenice", "Zagorje", "Šentjur", "Rogaška Slatina", "Slovenska Bistrica", "Sevnica", "Litija"]),
    "BEL": ("Belgium", True, 72, ["Antwerpen", "Oostende", "Bruxelles", "Liège", "Mons", "Charleroi", "Leuven", "Gent", "Brugge", "Mechelen", "Hasselt", "Namur", "Kortrijk", "Aalst", "Limburg", "Spa", "Waregem", "Ieper", "Tournai", "Verviers", "Genk", "Lier", "Roeselare", "Wavre"]),
    "NED": ("Netherlands", True, 68, ["Amsterdam", "Rotterdam", "Den Haag", "Utrecht", "Groningen", "Leiden", "Den Bosch", "Zwolle", "Almere", "Nijmegen", "Apeldoorn", "Haarlem", "Eindhoven", "Tilburg", "Breda", "Arnhem", "Enschede", "Amersfoort", "Maastricht", "Dordrecht", "Delft", "Lelystad"]),
    "POR": ("Portugal", True, 70, ["Lisboa", "Porto", "Braga", "Coimbra", "Ovar", "Oliveira de Azeméis", "Barreiro", "Setúbal", "Aveiro", "Viseu", "Guimarães", "Leiria", "Faro", "Funchal", "Ponta Delgada", "Évora", "Vila Real", "Lagos", "Tomar", "Portimão", "Queluz", "Sintra", "Almada", "Amadora", "Matosinhos", "Gaia", "Póvoa de Varzim", "Famalicão", "Santarém", "Castelo Branco"]),
    "POL": ("Poland", True, 74, ["Warszawa", "Kraków", "Wrocław", "Gdańsk", "Gdynia", "Sopot", "Poznań", "Łódź", "Lublin", "Szczecin", "Bydgoszcz", "Toruń", "Włocławek", "Zielona Góra", "Ostrów Wielkopolski", "Słupsk", "Dąbrowa Górnicza", "Stargard", "Kalisz", "Katowice", "Rzeszów", "Opole", "Białystok", "Olsztyn", "Kielce", "Legnica", "Koszalin", "Tarnów", "Gliwice", "Częstochowa", "Elbląg", "Radom", "Płock", "Przemyśl"]),
    "CZE": ("Czech Republic", True, 70, ["Praha", "Brno", "Ostrava", "Plzeň", "Nymburk", "Pardubice", "Opava", "Děčín", "Ústí nad Labem", "Kolín", "Hradec Králové", "Olomouc", "Liberec", "Jihlava", "Zlín", "Svitavy", "Písek", "Kroměříž", "Karviná", "Prostějov", "Teplice", "Most", "Chomutov", "Tábor", "Kladno", "Třinec"]),
    "FIN": ("Finland", True, 64, ["Helsinki", "Espoo", "Vantaa", "Tampere", "Turku", "Oulu", "Jyväskylä", "Lahti", "Kouvola", "Kotka", "Joensuu", "Kuopio", "Pori", "Vaasa", "Seinäjoki", "Lappeenranta", "Hämeenlinna", "Salo", "Rauma", "Kajaani", "Mikkeli", "Porvoo", "Raahe", "Kokkola"]),
    "SWE": ("Sweden", True, 64, ["Stockholm", "Göteborg", "Malmö", "Uppsala", "Norrköping", "Södertälje", "Borås", "Luleå", "Umeå", "Jämtland", "Nässjö", "Köping", "Lund", "Halmstad", "Västerås", "Örebro", "Linköping", "Jönköping", "Sundsvall", "Gävle", "Karlstad", "Helsingborg", "Kristianstad", "Växjö"]),
}

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


# League name, short name, country (None = cross-border), divisions top first. Promotion places of a division equal
# the relegation places of the one above so division sizes stay constant.
LEAGUES = [
    ("Lega Basket", "LBA", "ITA", [
        division("Lega Basket Serie A", "LBA", 16, po=playoffs(8, [5, 5, 7]), releg=1, squad=ITA_SQUAD),
        division("Serie A2", "A2", 20, po=playoffs(8, [5, 5, 5]), promo={"playoffWinner": True}, squad={"maxForeignPlayers": 2}),
    ]),
    ("Asociación de Clubs de Baloncesto", "ACB", "ESP", [
        division("Liga Endesa", "ACB", 18, po=playoffs(8, [3, 5, 5]), releg=2, squad={"maxNonEuPlayers": 2, "minDomesticPlayers": 4}),
        division("Primera FEB", "1FEB", 18, po=playoffs(8, [3, 3, 3], start=2), promo={"direct": 1, "playoffWinner": True}, squad={"maxNonEuPlayers": 2}),
    ]),
    ("Basketball Bundesliga", "BBL", "GER", [
        division("easyCredit BBL", "BBL", 18, po=playoffs(8, [5, 5, 5], play_in=True), releg=2, squad={"minDomesticPlayers": 6}),
        division("ProA", "ProA", 18, po=playoffs(8, [5, 5, 5]), promo={"playoffPlaces": 2}, squad={"minDomesticPlayers": 6}),
    ]),
    ("Ligue Nationale de Basket", "LNB", "FRA", [
        division("Betclic ÉLITE", "ELITE", 16, po=playoffs(8, [3, 5, 5], play_in=True), releg=2, squad={"minDomesticPlayers": 4}),
        division("ÉLITE 2", "ELITE2", 18, po=playoffs(8, [3, 3, 3], start=2), promo={"direct": 1, "playoffWinner": True}, squad={"minDomesticPlayers": 4}),
    ]),
    ("Hellenic Basketball Clubs Association", "ESAKE", "GRE", [
        division("Stoiximan GBL", "GBL", 12, po=playoffs(8, [3, 5, 5]), releg=1, squad={"maxForeignPlayers": 6}),
        division("Elite League", "ELITE", 16, po=playoffs(4, [3, 3]), promo={"playoffWinner": True}, squad={"maxForeignPlayers": 2}),
    ]),
    ("Turkish Basketball Federation", "TBF", "TUR", [
        division("Basketbol Süper Ligi", "BSL", 16, po=playoffs(8, [3, 5, 7]), releg=2, squad={"maxForeignPlayers": 6}),
        division("Türkiye Basketbol Ligi", "TBL", 16, po=playoffs(8, [3, 3, 5], start=2), promo={"direct": 1, "playoffWinner": True}, squad={"maxForeignPlayers": 2}),
    ]),
    ("Super League Basketball", "SLB", "GBR", [
        division("Super League Basketball", "SLB", 10, legs=4, po=playoffs(8, [3, 3, 1])),
    ]),
    ("National Basketball League", "NBL", "GBR", [
        division("NBL Division 1", "NBL1", 14, po=playoffs(8, [1, 1, 1])),
    ]),
    ("Lietuvos krepšinio lyga", "LKL", "LTU", [
        division("Betsafe-LKL", "LKL", 10, legs=4, po=playoffs(8, [3, 5, 5]), releg=1),
        division("Nacionalinė krepšinio lyga", "NKL", 14, po=playoffs(8, [3, 3, 5]), promo={"playoffWinner": True}),
    ]),
    ("Košarkaški savez Srbije", "KSS", "SRB", [
        division("Košarkaška liga Srbije", "KLS", 16, po=playoffs(8, [3, 3, 5]), releg=2),
        division("Prva liga Srbije", "PLS", 16, promo={"direct": 2}),
    ]),
    ("Hrvatski košarkaški savez", "HKS", "CRO", [
        division("Premijer liga", "HTPL", 12, po=playoffs(8, [3, 3, 5]), releg=1),
        division("Prva muška liga", "PML", 12, promo={"direct": 1}),
    ]),
    ("Košarkarska zveza Slovenije", "KZS", "SLO", [
        division("Liga Nova KBM", "1SKL", 10, po=playoffs(4, [3, 3]), releg=1,
                 second={"groups": [{"name": "Championship round", "from": 1, "to": 6}, {"name": "Relegation round", "from": 7, "to": 10}], "carryOver": "All"}),
        division("2. SKL", "2SKL", 12, promo={"direct": 1}),
    ]),
    ("BNXT League", "BNXT", None, [
        division("BNXT League", "BNXT", 20, po=playoffs(8, [3, 3, 3]), countries=["BEL", "NED"],
                 second={"groups": [{"name": "Elite Gold", "from": 1, "to": 5}, {"name": "Elite Silver", "from": 6, "to": 10}], "byCountry": True, "carryOver": "None"}),
    ]),
    ("Basketball Belgium", "BB", "BEL", [
        division("Top Division Men 1", "TDM1", 14, po=playoffs(4, [3, 3])),
    ]),
    ("Basketball Nederland", "BNL", "NED", [
        division("Promotiedivisie", "PROMO", 12, po=playoffs(4, [3, 3])),
    ]),
    ("Federação Portuguesa de Basquetebol", "FPB", "POR", [
        division("Liga Betclic", "LPB", 14, po=playoffs(8, [3, 5, 5]), releg=2),
        division("Proliga", "PRO", 16, po=playoffs(4, [3, 3], start=2), promo={"direct": 1, "playoffWinner": True}),
    ]),
    ("Polska Liga Koszykówki", "PLK", "POL", [
        division("Orlen Basket Liga", "OBL", 16, po=playoffs(8, [5, 5, 7]), releg=2),
        division("Bank Pekao S.A. 1 Liga", "1LM", 18, po=playoffs(8, [3, 3, 5]), promo={"playoffPlaces": 2}),
    ]),
    ("Česká basketbalová federace", "CBF", "CZE", [
        division("Kooperativa NBL", "NBL", 12, po=playoffs(8, [3, 5, 5]), releg=1,
                 second={"groups": [{"name": "Group A1", "from": 1, "to": 6}, {"name": "Group A2", "from": 7, "to": 12}], "carryOver": "All"}),
        division("1. liga", "1LIGA", 14, promo={"direct": 1}),
    ]),
    ("Koripallon Suomen Liiga", "KSL", "FIN", [
        division("Korisliiga", "KL", 12, legs=3, po=playoffs(8, [3, 5, 5]), releg=1),
        division("I-divisioona A", "1DA", 12, promo={"direct": 1}),
    ]),
    ("Svenska Basketbollförbundet", "SBBF", "SWE", [
        division("Basketligan", "BL", 12, legs=3, po=playoffs(8, [5, 5, 5]), releg=1),
        division("Superettan", "SE", 12, promo={"direct": 1}),
    ]),
]


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


def rules(div, offset):
    total = rounds(div["clubs"], div["legs"]) + second_phase_rounds(div["second"], div["clubs"], div["countries"] or [])
    first = SEASON_START.toordinal() + offset
    days = min(7, (REGULAR_END.toordinal() - first) // (total - 1))
    result = {
        "pointsForWin": 2, "pointsForLoss": 1,
        "registration": {"minTeams": div["clubs"], "maxTeams": div["clubs"]},
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


def main():
    rnd = random.Random(15)
    season = {"id": uid("season", "2026-27"), "name": "2026-27", "startDate": SEASON_START.isoformat(), "endDate": SEASON_END.isoformat()}
    data = {"format": "ebm-dataset", "version": 1, "countries": [], "clubs": [], "leagues": [], "divisions": [],
            "competitions": [], "seasons": [season], "participants": []}
    for code, (name, eu, _, _) in COUNTRIES.items():
        data["countries"].append({"code": code, "name": name, "euEea": eu})

    used_cities = {code: 0 for code in COUNTRIES}
    nick_index = 0
    for league_name, league_short, country, divisions in LEAGUES:
        league = {"id": uid("league", league_name), "name": league_name, "shortName": league_short}
        if country:
            league["country"] = country
        data["leagues"].append(league)
        for level, div in enumerate(divisions, start=1):
            division_id = uid("division", league_name, str(level))
            data["divisions"].append({"id": division_id, "leagueId": league["id"], "name": div["name"], "level": level})
            competition = {"id": uid("competition", league_name, div["name"]), "name": div["name"], "shortName": div["short"],
                           "type": "League", "divisionId": division_id, "rules": rules(div, 7 if level == 1 else 8)}
            if country:
                competition["country"] = country
            data["competitions"].append(competition)

            homes = [c for c in (div["countries"] or [country]) for _ in range(div["clubs"] // len(div["countries"] or [country]))]
            for rank, home in enumerate(homes):
                cities = COUNTRIES[home][3]
                city = cities[used_cities[home] % len(cities)]
                used_cities[home] += 1
                nickname = NICKNAMES[nick_index % len(NICKNAMES)]
                nick_index += 7  # coprime with the pool size: neighbouring clubs get different nicknames
                strength = COUNTRIES[home][2] - (level - 1) * 22
                within = rank if not div["countries"] else rank // len(div["countries"])
                reputation = max(10, strength - within * 3 + rnd.randint(-2, 2))
                wage = round(reputation ** 3 * 12 / 1000) * 1000
                club = {"id": uid("club", home, city, nickname), "name": f"{city} {nickname}",
                        "shortName": (city[:3] + nickname[:1]).upper(), "country": home, "city": city,
                        "foundedYear": rnd.randint(1920, 2010), "reputation": reputation,
                        "balance": round(wage * rnd.uniform(0.2, 0.6), -3), "wageBudget": wage,
                        "boardExpectation": board(level, within if not div["countries"] else rank, div["clubs"], div)}
                data["clubs"].append(club)
                data["participants"].append({"id": uid("entry", competition["id"], club["id"]), "competitionId": competition["id"],
                                             "seasonId": season["id"], "clubId": club["id"]})

    assert len({c["id"] for c in data["clubs"]}) == len(data["clubs"]), "duplicate club"
    for club in data["clubs"]:
        club["balance"] = int(club["balance"])
    out = Path(__file__).resolve().parent.parent / "data" / "europe" / "europe-2026-27.json"
    out.parent.mkdir(exist_ok=True)
    out.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"{out}: {len(data['countries'])} countries, {len(data['leagues'])} leagues, {len(data['competitions'])} competitions, {len(data['clubs'])} clubs")


if __name__ == "__main__":
    main()
