"""Generates the European dataset in data/europe: europe-2026-27.json (Step 15: countries, leagues, competitions,
clubs, entries) and the Step 16 player database (players, contracts and club history as CSV files).

Competition names, division hierarchy, formats, promotion/relegation and registration limits follow the official
competitions (docs/COUNTRY_REPORT.md lists the verification status). Top-division clubs are the real clubs, marked
Unverified; second-division clubs are fictional placeholders named after real cities (Generated). Players are
fictional (Generated): docs/PLAYER_DATABASE.md. Deterministic: the same script always writes the same files
(UUID v5 IDs, seeded numbers). Run: python build/generate-europe-dataset.py
"""
import csv
import json
import random
import uuid
from datetime import date, timedelta
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


# ---------------------------------------------------------------------------------------------------------------------
# Step 16: real clubs (top divisions, Unverified) and generated players, contracts and club history (Generated).
# ---------------------------------------------------------------------------------------------------------------------

# Real clubs of each top division, strongest first ("name|short name|city"); names without sponsors where the club is
# commonly known without one. Unverified: compiled from 2025-26 knowledge, not checked against 2026-27 registrations.
REAL_CLUBS = {
    "Lega Basket Serie A": "Olimpia Milano|MIL|Milano; Virtus Bologna|VBO|Bologna; Reyer Venezia|VEN|Venezia; Aquila Basket Trento|TRE|Trento; Basket Brescia Leonessa|BRE|Brescia; Trapani Shark|TRA|Trapani; Dinamo Sassari|SAS|Sassari; Pallacanestro Reggiana|REG|Reggio Emilia; Derthona Basket|DER|Tortona; Pallacanestro Trieste|TRI|Trieste; Napoli Basket|NAP|Napoli; Pallacanestro Varese|VAR|Varese; Vanoli Basket Cremona|CRE|Cremona; Universo Treviso Basket|TRV|Treviso; Pallacanestro Cantù|CAN|Cantù; APU Udine|UDI|Udine",
    "Liga Endesa": "Real Madrid|RMB|Madrid; FC Barcelona|FCB|Barcelona; Valencia Basket|VAL|Valencia; Unicaja|UNI|Málaga; Saski Baskonia|BKN|Vitoria-Gasteiz; CB Canarias|CAN|San Cristóbal de La Laguna; CB Gran Canaria|GCA|Las Palmas de Gran Canaria; Club Joventut Badalona|JOV|Badalona; UCAM Murcia CB|MUR|Murcia; CB Breogán|BRE|Lugo; Bàsquet Girona|GIR|Girona; Bàsquet Manresa|MAN|Manresa; Basket Zaragoza|ZAR|Zaragoza; Bilbao Basket|BIL|Bilbao; BC Andorra|AND|Andorra la Vella; Força Lleida|LLE|Lleida; Fundación CB Granada|GRA|Granada; CB San Pablo Burgos|BUR|Burgos",
    "easyCredit BBL": "FC Bayern München|FCB|München; ALBA Berlin|ALB|Berlin; Ratiopharm Ulm|ULM|Ulm; Telekom Baskets Bonn|BON|Bonn; Würzburg Baskets|WUE|Würzburg; Niners Chemnitz|CHE|Chemnitz; Riesen Ludwigsburg|LUD|Ludwigsburg; Bamberg Baskets|BAM|Bamberg; Rostock Seawolves|ROS|Rostock; Hamburg Towers|HAM|Hamburg; Baskets Oldenburg|OLD|Oldenburg; Academics Heidelberg|HEI|Heidelberg; Mitteldeutscher BC|MBC|Weißenfels; Basketball Löwen Braunschweig|BRA|Braunschweig; Science City Jena|JEN|Jena; Skyliners Frankfurt|FRA|Frankfurt; Rasta Vechta|VEC|Vechta; Gladiators Trier|TRI|Trier",
    "Betclic ÉLITE": "Paris Basketball|PAR|Paris; AS Monaco Basket|MON|Monaco; ASVEL Lyon-Villeurbanne|ASV|Villeurbanne; JL Bourg|JLB|Bourg-en-Bresse; Nanterre 92|NAN|Nanterre; SIG Strasbourg|SIG|Strasbourg; JDA Dijon|DIJ|Dijon; Cholet Basket|CHO|Cholet; Le Mans Sarthe Basket|MSB|Le Mans; Limoges CSP|CSP|Limoges; BCM Gravelines-Dunkerque|BCM|Gravelines; ESSM Le Portel|ESS|Le Portel; Saint-Quentin Basket-Ball|SQB|Saint-Quentin; Élan Béarnais|EBP|Pau; Boulazac Basket Dordogne|BBD|Boulazac; SLUC Nancy|SLU|Nancy",
    "Stoiximan GBL": "Olympiacos|OLY|Peiraiás; Panathinaikos|PAO|Athína; AEK Athens|AEK|Athína; PAOK|PAOK|Thessaloníki; Aris Thessaloniki|ARI|Thessaloníki; Peristeri|PER|Peristéri; Promitheas Patras|PRO|Pátra; Kolossos Rhodes|KOL|Rhódos; Maroussi|MAR|Maroússi; Karditsa|KAR|Kardítsa; Panionios|PAN|Néa Smýrni; Lavrio|LAV|Lávrio",
    "Basketbol Süper Ligi": "Fenerbahçe|FEN|İstanbul; Anadolu Efes|EFS|İstanbul; Beşiktaş|BJK|İstanbul; Galatasaray|GS|İstanbul; Türk Telekom|TTK|Ankara; Bahçeşehir Koleji|BAH|İstanbul; Karşıyaka|KSK|İzmir; Tofaş|TOF|Bursa; Manisa Basket|MAN|Manisa; Aliağa Petkimspor|PET|İzmir; Trabzonspor|TS|Trabzon; Mersinspor|MER|Mersin; Bursaspor|BUR|Bursa; Büyükçekmece Basketbol|BCB|İstanbul; Esenler Erokspor|ERO|İstanbul; Merkezefendi Belediyesi|MRK|Denizli",
    "Super League Basketball": "London Lions|LON|London; Leicester Riders|LEI|Leicester; Sheffield Sharks|SHE|Sheffield; Cheshire Phoenix|CHE|Ellesmere Port; Bristol Flyers|BRI|Bristol; Caledonia Gladiators|CAL|East Kilbride; Newcastle Eagles|NEW|Newcastle; Manchester Basketball|MAN|Manchester; Surrey Scorchers|SUR|Guildford; Plymouth City Patriots|PLY|Plymouth",
    "Betsafe-LKL": "Žalgiris Kaunas|ZAL|Kaunas; Rytas Vilnius|RYT|Vilnius; Lietkabelis Panevėžys|LIE|Panevėžys; Neptūnas Klaipėda|NEP|Klaipėda; BC Šiauliai|SIA|Šiauliai; Juventus Utena|JUV|Utena; Wolves Vilnius|WOL|Vilnius; CBet Jonava|JON|Jonava; Gargždai|GAR|Gargždai; Nevėžis Kėdainiai|NEV|Kėdainiai",
    "Košarkaška liga Srbije": "Crvena zvezda|CZV|Beograd; Partizan|PAR|Beograd; Mega Basket|MEG|Beograd; FMP|FMP|Beograd; Borac Čačak|BOR|Čačak; Spartak Subotica|SPA|Subotica; Zlatibor|ZLA|Čajetina; Sloga Kraljevo|SLO|Kraljevo; Vojvodina|VOJ|Novi Sad; Radnički Kragujevac|RAD|Kragujevac; Metalac Valjevo|MET|Valjevo; Novi Pazar|NPZ|Novi Pazar; Dynamic Beograd|DYN|Beograd; Mladost Zemun|MLA|Beograd; Tamiš Pančevo|TAM|Pančevo; OKK Beograd|OKK|Beograd",
    "Premijer liga": "Cibona|CIB|Zagreb; KK Zadar|ZAD|Zadar; KK Split|SPL|Split; Cedevita Junior|CED|Zagreb; GKK Šibenik|SIB|Šibenik; Alkar Sinj|ALK|Sinj; Gorica|GOR|Velika Gorica; Dubrava|DUB|Zagreb; KK Osijek|OSI|Osijek; Zabok|ZAB|Zabok; Sonik Puntamika|SON|Zadar; Kvarner Rijeka|KVA|Rijeka",
    "Liga Nova KBM": "Cedevita Olimpija|OLI|Ljubljana; Krka Novo mesto|KRK|Novo Mesto; Helios Suns|HEL|Domžale; Šentjur|SEN|Šentjur; Ilirija|ILI|Ljubljana; Rogaška|ROG|Rogaška Slatina; Zlatorog Laško|ZLA|Laško; Hopsi Polzela|HOP|Polzela; Triglav Kranj|TRI|Kranj; Terme Olimia Podčetrtek|POD|Podčetrtek",
    "BNXT League": "Filou Oostende|OOS|Oostende; Antwerp Giants|ANT|Antwerpen; Mons-Hainaut|MON|Mons; Spirou Charleroi|SPI|Charleroi; Liège Basket|LIE|Liège; Kangoeroes Mechelen|KAN|Mechelen; Okapi Aalst|OKA|Aalst; Limburg United|LIM|Hasselt; Brussels Basketball|BRU|Bruxelles; Leuven Bears|LEU|Leuven; ZZ Leiden|ZZL|Leiden; Heroes Den Bosch|HER|Den Bosch; Landstede Hammers|LAN|Zwolle; Donar Groningen|DON|Groningen; Feyenoord Basketball|FEY|Rotterdam; Yoast United|YOA|Bemmel; BAL Weert|BAL|Weert; Den Helder Suns|DHS|Den Helder; Dutch Windmills|DWM|Dordrecht; Aris Leeuwarden|ARL|Leeuwarden",
    "Liga Betclic": "SL Benfica|SLB|Lisboa; FC Porto|FCP|Porto; Sporting CP|SCP|Lisboa; Imortal Basket|IMO|Albufeira; UD Oliveirense|OLI|Oliveira de Azeméis; Ovarense|OVA|Ovar; Vitória SC|VSC|Guimarães; Esgueira|ESG|Aveiro; Galitos Barreiro|GAL|Barreiro; CD Póvoa|POV|Póvoa de Varzim; Illiabum Clube|ILL|Ílhavo; CA Queluz|QUE|Queluz; SC Braga|BRA|Braga; Lusitânia|LUS|Angra do Heroísmo",
    "Orlen Basket Liga": "Trefl Sopot|TRE|Sopot; King Szczecin|KIN|Szczecin; Anwil Włocławek|ANW|Włocławek; Legia Warszawa|LEG|Warszawa; Śląsk Wrocław|SLA|Wrocław; Start Lublin|STA|Lublin; Stal Ostrów Wielkopolski|STL|Ostrów Wielkopolski; Arka Gdynia|ARK|Gdynia; Czarni Słupsk|CZA|Słupsk; MKS Dąbrowa Górnicza|MKS|Dąbrowa Górnicza; Spójnia Stargard|SPO|Stargard; Górnik Wałbrzych|GOR|Wałbrzych; Dziki Warszawa|DZI|Warszawa; Zastal Zielona Góra|ZAS|Zielona Góra; Twarde Pierniki Toruń|TOR|Toruń; Kotwica Kołobrzeg|KOT|Kołobrzeg",
    "Kooperativa NBL": "ERA Nymburk|NYM|Nymburk; BK Opava|OPA|Opava; USK Praha|USK|Praha; BK Pardubice|PAR|Pardubice; BK Děčín|DEC|Děčín; Basket Brno|BRN|Brno; NH Ostrava|OST|Ostrava; BC Kolín|KOL|Kolín; Sluneta Ústí nad Labem|UST|Ústí nad Labem; Hradec Králové|HKR|Hradec Králové; BK Olomoucko|OLO|Prostějov; Svitavy|SVI|Svitavy",
    "Korisliiga": "Karhu Kauhajoki|KAR|Kauhajoki; Kataja Basket|KAT|Joensuu; Salon Vilpas|VIL|Salo; Helsinki Seagulls|SEA|Helsinki; Tampereen Pyrintö|PYR|Tampere; Kobrat|KOB|Lapua; Bisons Loimaa|BIS|Loimaa; KTP-Basket|KTP|Kotka; Kouvot|KOU|Kouvola; Lahti Basketball|LAH|Lahti; Ura Basket|URA|Kaarina; Tapiolan Honka|HON|Espoo",
    "Basketligan": "Norrköping Dolphins|NOR|Norrköping; BC Luleå|LUL|Luleå; Borås Basket|BOR|Borås; Södertälje Kings|SOD|Södertälje; Uppsala Basket|UPP|Uppsala; Köping Stars|KOP|Köping; Nässjö Basket|NAS|Nässjö; Jämtland Basket|JAM|Östersund; Umeå BSKT|UME|Umeå; Malbas|MAL|Malmö; Djurgårdens IF|DIF|Stockholm; Alvik Basket|ALV|Stockholm",
}

# Nationalities without a league in the dataset (foreign players). Code: (name, EU/EEA/CH).
EXTRA_COUNTRIES = {"USA": ("United States", False), "LVA": ("Latvia", True), "NGA": ("Nigeria", False)}

# Real squads (build/real-rosters/*.json, curated from cited sources) replace a club's generated players. Profiles are
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
SQUAD_SIZE = len(SQUAD_POSITIONS)
CONTRACT_SEASON = date(2026, 7, 1)  # contracts start on 1 July and end on 30 June
PLAYERS_FILE, CONTRACTS_FILE, HISTORY_FILE = "players-europe-2026-27.csv", "contracts-europe-2026-27.csv", "playerHistory-europe-2026-27.csv"


def composition(div, level):
    """Domestic, EU-foreign and non-EU players of a 12-man squad that satisfies the division's registration limits."""
    squad = div["squad"] or {}
    domestic = max(6 if level == 1 else 9, squad.get("minDomesticPlayers", 0))
    if "maxForeignPlayers" in squad:
        domestic = max(domestic, SQUAD_SIZE - squad["maxForeignPlayers"])
    foreign = SQUAD_SIZE - domestic
    non_eu = min(foreign, squad.get("maxNonEuPlayers", 4 if level == 1 else 2))
    return domestic, foreign - non_eu, non_eu


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


def add_players(data, clubs_by_country, rnd):
    """Twelve generated players per club with a contract, and a past club spell for some of them."""
    countries = {c["code"]: (c["name"], c["euEea"]) for c in data["countries"]}
    factory = PlayerFactory(rnd, countries)
    for roster in REAL_ROSTERS.values():  # generated players never share a name and close birth date with a real one
        for real in roster["players"]:
            factory.born.setdefault((real["firstName"], real["lastName"]), []).append(date.fromisoformat(real["dateOfBirth"]))
    clubs_by_name = {c["name"]: c for c in data["clubs"]}
    players, contracts, history = [], [], []
    for club, div, level in data["_club_divisions"]:
        if club["name"] in REAL_ROSTERS:
            # The generated squad is still drawn (and discarded) so every other club's players keep their IDs.
            generate_squad(club, div, level, factory, clubs_by_country, rnd, [], [], [])
            real_squad(REAL_ROSTERS[club["name"]], club, clubs_by_name, players, contracts, history)
        else:
            generate_squad(club, div, level, factory, clubs_by_country, rnd, players, contracts, history)
    return players, contracts, history


def generate_squad(club, div, level, factory, clubs_by_country, rnd, players, contracts, history):
    """Twelve generated players with contracts for one club, and a past club spell for some of them."""
    home = club["country"]
    domestic, eu_foreign, non_eu = composition(div, level)
    eu_pool = [c for c in factory.eu if c != home]
    # Foreign players take the best squad places, as in most European clubs.
    nationalities = [("SRB" if home != "SRB" and rnd.random() < 0.15 else "USA", None) for _ in range(non_eu)]
    nationalities += [("USA", rnd.choice(eu_pool)) if rnd.random() < 0.3 else (rnd.choice(eu_pool), None) for _ in range(eu_foreign)]
    nationalities += [(home, None)] * domestic
    base = 30 + club["reputation"] * 0.42
    wage_pool = club["wageBudget"] * 0.8
    weights = [1.6 - i * 0.1 for i in range(SQUAD_SIZE)]
    for slot, (position, (nationality, second)) in enumerate(zip(SQUAD_POSITIONS, nationalities)):
        quality = round(base + 8 - slot * 1.4 + rnd.randint(-3, 3))
        player = factory.create(nationality, position, quality, second)
        players.append(player)
        start = date(CONTRACT_SEASON.year - rnd.choice([0, 0, 1, 2]), 7, 1)
        end = date(max(CONTRACT_SEASON.year + 1, start.year + rnd.randint(1, 3)), 6, 30)
        contracts.append({"id": uid("contract", player["id"], start.isoformat()), "clubId": club["id"], "playerId": player["id"],
                          "startDate": start.isoformat(), "endDate": end.isoformat(),
                          "annualSalary": int(round(wage_pool * weights[slot] / sum(weights), -3)), "dataStatus": "Generated", "source": ""})
        age = CONTRACT_SEASON.year - int(player["dateOfBirth"][:4])
        former = [c for c in clubs_by_country.get(nationality, []) if c["id"] != club["id"]]
        if age >= 22 and former and rnd.random() < 0.35:
            previous = rnd.choice(former)
            spell_end = start - timedelta(days=1)
            spell_start = date(spell_end.year - rnd.randint(1, 3), 7, 1)
            history.append({"id": uid("spell", player["id"], previous["id"], spell_start.isoformat()), "playerId": player["id"],
                            "clubId": previous["id"], "startDate": spell_start.isoformat(), "endDate": spell_end.isoformat()})


def real_squad(roster, club, clubs_by_name, players, contracts, history):
    """A curated squad: sourced profiles, attributes estimated from level and statistics, estimated contract terms."""
    ranked = sorted(roster["players"], key=lambda x: -x["level"])
    weights = {id(x): 1.6 - i * 0.1 for i, x in enumerate(ranked)}
    wage_pool = club["wageBudget"] * 0.8
    for real in roster["players"]:
        position, level = real["position"], real["level"]
        birth = date.fromisoformat(real["dateOfBirth"])
        age = CONTRACT_SEASON.year - birth.year
        player = {"id": uid("player", real["firstName"], real["lastName"], real["dateOfBirth"]), "firstName": real["firstName"],
                  "lastName": real["lastName"], "dateOfBirth": real["dateOfBirth"], "nationality": real["nationality"],
                  "secondNationality": real.get("secondNationality", ""), "heightCm": real["heightCm"], "weightKg": real["weightKg"],
                  "position": position, "wingspanCm": ""}
        offsets = PROFILES[position][2]
        for name, category in ATTRIBUTES:
            player[name] = real["ratings"].get(name, min(95, max(20, level + offsets.get(category, 0))))
        player["experience"] = min(100, max(1, 5 + (age - 18) * 4 + level // 5))
        player["reputation"], player["potential"] = real["reputation"], real["potential"]
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


def main():
    rnd = random.Random(15)
    season = {"id": uid("season", "2026-27"), "name": "2026-27", "startDate": SEASON_START.isoformat(), "endDate": SEASON_END.isoformat()}
    data = {"format": "ebm-dataset", "version": 1, "countries": [], "clubs": [], "leagues": [], "divisions": [],
            "competitions": [], "seasons": [season], "participants": []}
    for code, (name, eu, _, _) in COUNTRIES.items():
        data["countries"].append({"code": code, "name": name, "euEea": eu})
    for code, (name, eu) in EXTRA_COUNTRIES.items():
        data["countries"].append({"code": code, "name": name, "euEea": eu})
    club_divisions = []

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
            real = [entry.split("|") for entry in REAL_CLUBS[div["name"]].split("; ")] if level == 1 and div["name"] in REAL_CLUBS else None
            assert real is None or len(real) == div["clubs"], f"{div['name']}: {len(real)} real clubs for {div['clubs']} places"
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
                if real:
                    name, short, city = real[rank]
                    identity = {"id": uid("club", home, name), "name": name, "shortName": short, "country": home, "city": city, "dataStatus": "Unverified"}
                else:
                    identity = {"id": uid("club", home, city, nickname), "name": f"{city} {nickname}",
                                "shortName": (city[:3] + nickname[:1]).upper(), "country": home, "city": city, "dataStatus": "Generated"}
                club = {**identity,
                        "foundedYear": rnd.randint(1920, 2010), "reputation": reputation,
                        "balance": round(wage * rnd.uniform(0.2, 0.6), -3), "wageBudget": wage,
                        "boardExpectation": board(level, within if not div["countries"] else rank, div["clubs"], div)}
                data["clubs"].append(club)
                club_divisions.append((club, div, level))
                data["participants"].append({"id": uid("entry", competition["id"], club["id"]), "competitionId": competition["id"],
                                             "seasonId": season["id"], "clubId": club["id"]})

    assert len({c["id"] for c in data["clubs"]}) == len(data["clubs"]), "duplicate club"
    for club in data["clubs"]:
        club["balance"] = int(club["balance"])
    clubs_by_country = {}
    for club in data["clubs"]:
        clubs_by_country.setdefault(club["country"], []).append(club)
    data["_club_divisions"] = club_divisions
    players, contracts, history = add_players(data, clubs_by_country, random.Random(16))
    del data["_club_divisions"]
    assert len({p["id"] for p in players}) == len(players), "duplicate player"

    folder = Path(__file__).resolve().parent.parent / "data" / "europe"
    folder.mkdir(exist_ok=True)
    (folder / "europe-2026-27.json").write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    write_csv(folder / PLAYERS_FILE, players)
    write_csv(folder / CONTRACTS_FILE, contracts)
    write_csv(folder / HISTORY_FILE, history)
    real = sum(1 for c in data["clubs"] if c["dataStatus"] == "Unverified")
    print(f"{folder}: {len(data['countries'])} countries, {len(data['leagues'])} leagues, {len(data['competitions'])} competitions, "
          f"{len(data['clubs'])} clubs ({real} real), {len(players)} players, {len(contracts)} contracts, {len(history)} past club spells")


if __name__ == "__main__":
    main()
