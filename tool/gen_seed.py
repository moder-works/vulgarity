#!/usr/bin/env python3
"""Build data/seed.json, the base English term list.

Every term here is already folded to profile fold-v1: lowercase, ASCII letters
and digits only, no separators, repeated letters left intact.

The `w` column requests a word-boundary check. Set it on any term that also
appears inside an innocent word. The matcher's boundary test is gap-aware, so
`w` still lets "the rapist" match while "therapist" does not.

Leave `w` off for a long, distinctive term. The matcher then finds it inside a
compound, so "clusterfuck" hits without a separate entry.

Run: python3 tool/gen_seed.py
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

P, X, H, V, D = "profanity", "sexual", "hate", "violence", "drug"

# (term, category, severity, word-boundary-required)
TERMS = [
    # ---- profanity -------------------------------------------------------
    ("fuck", P, 4, False),
    ("fuk", P, 4, False),
    ("fuq", P, 4, True),
    ("phuck", P, 4, False),
    ("fck", P, 4, True),
    ("fcuk", P, 4, True),
    ("shit", P, 3, False),
    ("shyt", P, 3, True),
    ("bitch", P, 3, False),
    ("biatch", P, 3, False),
    ("cunt", P, 5, False),
    ("kunt", P, 5, True),  # boundary: eduskunta
    ("ass", P, 2, True),
    ("arse", P, 2, True),
    ("asshole", P, 4, False),
    ("arsehole", P, 4, False),
    ("dumbass", P, 3, False),
    ("jackass", P, 3, False),
    ("badass", P, 2, False),
    ("smartass", P, 2, False),
    ("asshat", P, 3, False),
    ("asswipe", P, 3, False),
    ("bastard", P, 3, False),
    ("damn", P, 1, True),
    ("goddamn", P, 2, False),
    ("damnit", P, 2, False),
    ("dammit", P, 2, False),
    ("hell", P, 1, True),
    ("crap", P, 1, True),
    ("crappy", P, 1, True),  # boundary: scrappy
    ("piss", P, 2, True),
    ("pissed", P, 2, True),
    ("pissing", P, 2, True),
    ("pisser", P, 2, True),
    ("prick", P, 2, True),
    ("twat", P, 4, True),
    ("wank", P, 3, True),
    ("wanker", P, 3, True),  # boundary: swanker, twanker
    ("bollock", P, 2, True),
    ("bollocks", P, 2, True),
    ("bugger", P, 2, True),  # boundary: humbugger
    ("bloody", P, 1, True),
    ("douche", P, 2, False),
    ("douchebag", P, 3, False),
    ("dick", P, 3, True),
    ("dickhead", P, 4, False),
    ("dickwad", P, 4, False),
    ("dickface", P, 4, False),
    ("cock", P, 3, True),
    ("cocksucker", P, 5, False),
    ("cocksuck", P, 5, False),
    ("knob", P, 1, True),
    ("knobhead", P, 3, False),
    ("knobend", P, 3, False),
    ("tosser", P, 2, True),
    ("bellend", P, 3, False),
    ("numbnuts", P, 2, False),
    ("nutsack", P, 3, False),
    ("jerkoff", P, 3, False),
    ("jackoff", P, 3, False),
    ("shithead", P, 4, False),
    ("shitface", P, 4, False),
    ("shitbag", P, 4, False),
    ("shitshow", P, 2, False),
    ("bullshit", P, 3, False),
    ("horseshit", P, 3, False),
    ("dogshit", P, 3, False),
    ("batshit", P, 2, False),
    ("fuckwit", P, 4, False),
    ("fuckface", P, 4, False),
    ("fuckhead", P, 4, False),
    ("fuckboy", P, 3, False),
    ("clusterfuck", P, 3, False),
    ("mindfuck", P, 3, False),
    ("whore", P, 4, False),
    ("hoe", P, 2, True),
    ("hoes", P, 2, True),
    ("slut", P, 4, False),
    ("slag", P, 3, True),
    ("slags", P, 3, True),
    ("skank", P, 3, False),
    ("hussy", P, 2, True),
    ("bimbo", P, 2, True),
    ("fanny", P, 1, True),
    ("minge", P, 3, True),
    ("gash", P, 2, True),
    ("muff", P, 2, True),
    ("poon", P, 3, True),
    ("poontang", P, 4, False),
    ("punani", P, 3, False),
    ("vag", P, 2, True),
    ("pussy", P, 3, True),
    ("titty", P, 3, False),
    ("tits", P, 3, True),
    ("tit", P, 2, True),
    ("boob", P, 1, True),
    ("boobs", P, 1, True),
    ("boobies", P, 2, False),
    ("bumhole", P, 3, False),
    ("butthole", P, 3, False),
    ("butt", P, 1, True),
    ("poop", P, 1, True),
    ("poo", P, 1, True),
    ("turd", P, 2, True),
    ("fart", P, 1, True),
    ("farts", P, 1, True),
    ("farted", P, 1, True),
    ("farting", P, 1, True),
    ("pee", P, 1, True),
    ("moron", P, 2, True),
    ("idiot", P, 1, True),
    ("imbecile", P, 2, False),
    ("stupid", P, 1, True),
    ("dumb", P, 1, True),
    ("jerk", P, 1, True),
    ("loser", P, 1, True),
    ("scum", P, 1, True),
    ("scumbag", P, 2, False),
    ("sleazebag", P, 2, False),

    # ---- sexual ----------------------------------------------------------
    # Clinical anatomy sits at severity 1 so a caller can raise MinSeverity
    # to 2 and keep medical writing clean.
    ("sex", X, 1, True),
    ("porn", X, 3, False),
    ("hentai", X, 3, False),
    ("xxx", X, 2, True),
    ("nude", X, 1, True),
    ("nudes", X, 1, True),
    ("naked", X, 1, True),
    ("boner", X, 2, True),
    ("erection", X, 1, True),  # boundary: piloerection
    ("hardon", X, 2, False),
    ("horny", X, 2, True),  # boundary: thorny, hawthorny
    ("milf", X, 3, True),  # boundary: milfoil
    ("dilf", X, 3, False),
    ("bdsm", X, 2, False),
    ("bondage", X, 1, True),
    ("fetish", X, 1, True),
    ("orgy", X, 3, True),
    ("orgasm", X, 2, False),
    ("cum", X, 3, True),
    ("cumming", X, 3, True),  # boundary: scumming
    ("cumshot", X, 4, False),
    ("jizz", X, 3, False),
    ("spunk", X, 2, True),
    ("semen", X, 1, True),
    ("sperm", X, 1, True),
    ("ejaculat", X, 2, False),
    ("masturbat", X, 2, False),
    ("fornicat", X, 2, False),
    ("fellatio", X, 3, False),
    ("cunnilingus", X, 3, False),
    ("analingus", X, 3, False),
    ("blowjob", X, 4, False),
    ("handjob", X, 4, False),
    ("rimjob", X, 4, False),
    ("rimming", X, 3, True),  # boundary: trimming, brimming
    ("deepthroat", X, 4, False),
    ("gangbang", X, 4, False),
    ("bukkake", X, 4, False),
    ("creampie", X, 4, False),
    ("threesome", X, 2, False),
    ("dildo", X, 3, False),
    ("buttplug", X, 4, False),
    ("fleshlight", X, 3, False),
    ("strapon", X, 3, False),
    ("vibrator", X, 2, True),  # boundary: multivibrator
    ("anal", X, 3, True),
    ("anus", X, 1, True),
    ("rectum", X, 1, True),
    ("penis", X, 1, True),
    ("vagina", X, 1, True),
    ("vulva", X, 1, True),
    ("clitoris", X, 1, False),
    ("clit", X, 3, True),
    ("scrotum", X, 1, True),
    ("testicle", X, 1, False),
    ("ballsack", X, 3, False),
    ("prostitute", X, 2, False),
    ("prostitution", X, 2, False),
    ("hooker", X, 2, True),
    ("pimp", X, 2, True),
    ("brothel", X, 1, True),
    ("stripper", X, 1, True),
    ("incest", X, 4, False),
    ("pedophile", X, 5, False),
    ("paedophile", X, 5, False),
    ("pedo", X, 5, True),
    ("molest", X, 4, False),
    ("bestiality", X, 5, False),
    ("zoophilia", X, 5, False),
    ("upskirt", X, 4, False),
    ("voyeur", X, 2, False),
    ("camgirl", X, 2, False),
    ("sodomy", X, 3, False),
    ("sodomiz", X, 3, False),
    ("sodomis", X, 3, False),
    ("buggery", X, 3, True),  # boundary: humbuggery
    ("nympho", X, 3, False),
    ("titfuck", X, 5, False),

    # ---- hate ------------------------------------------------------------
    ("nigger", H, 5, True),  # boundary: sniggering
    ("nigga", H, 5, True),  # boundary: niggardly, niggard
    ("negro", H, 3, True),
    ("coon", H, 4, True),
    ("jigaboo", H, 5, False),
    ("porchmonkey", H, 5, False),
    ("spearchucker", H, 5, False),
    ("darkie", H, 4, True),
    ("darky", H, 4, True),
    ("sambo", H, 4, True),
    ("kike", H, 5, False),
    ("heeb", H, 4, True),
    ("hymie", H, 4, True),
    ("yid", H, 4, True),
    ("spic", H, 5, True),
    ("spick", H, 5, True),  # boundary: spicket, mispickel
    ("wetback", H, 5, False),
    ("beaner", H, 5, True),  # boundary: beanery
    ("chink", H, 5, True),
    ("gook", H, 5, True),
    ("jap", H, 4, True),
    ("slopehead", H, 5, False),
    ("zipperhead", H, 5, False),
    ("paki", H, 5, True),
    ("currymuncher", H, 5, False),
    ("towelhead", H, 5, False),
    ("raghead", H, 5, False),
    ("sandnigger", H, 5, False),
    ("cameljockey", H, 5, False),
    ("gyp", H, 3, True),
    ("gippo", H, 4, True),
    ("pikey", H, 4, True),
    ("kraut", H, 3, True),
    ("polack", H, 4, True),
    ("wop", H, 4, True),
    ("dago", H, 4, True),
    ("limey", H, 2, True),
    ("whitetrash", H, 3, False),
    ("honky", H, 3, True),
    ("gringo", H, 2, True),
    ("injun", H, 4, True),
    ("redskin", H, 4, False),
    ("squaw", H, 4, True),
    ("boong", H, 5, True),
    ("faggot", H, 5, False),
    ("fag", H, 5, True),
    ("fags", H, 5, True),
    ("faggy", H, 5, False),
    ("fudgepacker", H, 5, False),
    ("homo", H, 4, True),
    ("queer", H, 2, True),
    ("dyke", H, 3, True),
    ("lesbo", H, 4, True),
    ("lezzie", H, 4, True),
    ("poof", H, 4, True),
    ("poofter", H, 5, False),
    ("battyboy", H, 5, False),
    ("nancyboy", H, 4, False),
    ("pansy", H, 1, True),
    ("sissy", H, 2, True),
    ("tranny", H, 5, False),
    ("shemale", H, 5, False),
    ("heshe", H, 4, False),
    ("ladyboy", H, 4, False),
    ("retard", H, 4, True),
    ("retarded", H, 4, True),
    ("tard", H, 4, True),
    ("mongoloid", H, 5, False),
    ("spastic", H, 4, True),  # boundary: spasticity, vasospastic
    ("spaz", H, 4, True),
    ("cripple", H, 3, True),
    ("gimp", H, 3, True),
    ("midget", H, 3, True),
    ("windowlicker", H, 4, False),
    ("psycho", H, 2, True),
    ("nazi", H, 3, True),
    ("nazis", H, 3, True),
    ("kkk", H, 4, True),
    ("whitepower", H, 4, False),
    ("heilhitler", H, 4, False),

    # ---- violence --------------------------------------------------------
    ("killyourself", V, 5, False),
    ("kys", V, 4, True),
    ("rape", V, 5, True),
    ("rapes", V, 5, True),
    ("raped", V, 5, True),
    ("raping", V, 5, True),
    ("rapist", V, 5, True),
    ("lynch", V, 3, True),
    ("genocide", V, 3, False),
    ("behead", V, 3, False),
    ("murder", V, 2, True),
    ("terrorist", V, 2, True),
    ("shootup", V, 3, False),
    ("strangle", V, 2, True),
    ("torture", V, 2, True),
    ("suicide", V, 2, False),
    ("selfharm", V, 3, False),
    ("massacre", V, 2, False),
    ("slaughter", V, 1, True),
    ("gaschamber", V, 4, False),

    # ---- drug ------------------------------------------------------------
    ("cocaine", D, 2, False),
    ("heroin", D, 2, True),  # boundary: heroine
    ("meth", D, 2, True),
    ("methamphetamine", D, 2, False),
    ("marijuana", D, 1, False),
    ("cannabis", D, 1, False),
    ("lsd", D, 2, True),
    ("mdma", D, 2, False),
    ("ketamine", D, 2, False),
    ("fentanyl", D, 3, False),
    ("crackhead", D, 3, False),
    ("junkie", D, 2, True),
    ("stoner", D, 1, True),
    ("bong", D, 2, True),
    ("shrooms", D, 2, True),  # boundary: mushrooms
    ("roofie", D, 4, True),
    ("roofies", D, 4, True),
    ("angeldust", D, 2, False),
    ("speedball", D, 2, False),
]

# ---------------------------------------------------------------------------
# Hand-vetted additions, corroborated against the safe_text corpus
# (github.com/master-wayne7/safe_text, lib/data/en.dart).
#
# The corpus is community-sourced and unvetted, so nothing enters this list
# automatically. I read the folded misses and kept only the terms that are
# genuinely vulgar and that do not collide with ordinary English. Terms such as
# "acct", "cigs", "lmao", "noob", "aunt" and "cool" appear in the corpus and
# stay out on purpose.
# ---------------------------------------------------------------------------
ADDITIONS = [
    # ---- profanity: variant spellings and compounds ----------------------
    ("mofo", P, 4, True),  # boundary: bromoform
    ("fook", P, 4, True),
    ("fooking", P, 4, False),
    ("feck", P, 2, True),
    ("fecking", P, 2, False),
    ("shite", P, 3, False),
    ("gobshite", P, 3, False),
    ("dipshit", P, 3, False),
    ("apeshit", P, 2, False),
    ("jackshit", P, 2, False),
    ("chickenshit", P, 2, False),
    ("shitstain", P, 4, False),
    ("shitcunt", P, 5, False),
    ("shitfaced", P, 2, False),
    ("fuckery", P, 3, False),
    ("fuckwad", P, 4, False),
    ("fucknut", P, 4, False),
    ("fuckstick", P, 4, False),
    ("fuckup", P, 2, False),
    ("cuntface", P, 5, False),
    ("cuntbag", P, 5, False),
    ("twatwaffle", P, 4, False),
    ("twatface", P, 4, False),
    ("wanking", P, 3, True),  # boundary: swanking, twanking
    ("arsehat", P, 3, False),
    ("arsewipe", P, 3, False),
    ("arseclown", P, 3, False),
    ("assclown", P, 3, False),
    ("assface", P, 4, False),
    ("asslicker", P, 4, False),
    ("asskisser", P, 3, False),
    ("assmunch", P, 3, False),
    ("bitchass", P, 4, False),
    ("cockwomble", P, 3, False),
    ("dickbag", P, 4, False),
    ("dickweed", P, 4, False),
    ("dickcheese", P, 4, False),
    ("dickless", P, 3, False),
    ("buttmunch", P, 3, False),
    ("smeghead", P, 2, False),
    ("pissflaps", P, 3, False),
    ("pisstake", P, 1, False),
    ("tosspot", P, 2, False),
    ("bawbag", P, 2, False),
    ("bollix", P, 2, False),
    ("titties", P, 3, False),
    ("plonker", P, 1, True),
    ("pillock", P, 2, True),
    ("prat", P, 1, True),
    ("berk", P, 1, True),
    ("wazzock", P, 1, True),
    ("numpty", P, 1, True),
    ("muppet", P, 1, True),
    ("minger", P, 2, True),
    ("munter", P, 2, True),
    ("slapper", P, 2, True),
    ("sket", P, 3, True),
    ("thot", P, 3, True),
    ("scrote", P, 2, True),
    ("choad", P, 2, True),
    ("chode", P, 2, True),
    ("dong", P, 1, True),
    ("schlong", P, 2, False),
    ("todger", P, 2, True),
    ("willy", P, 1, True),
    ("poxy", P, 1, True),
    ("yobbo", P, 1, True),
    ("chav", P, 2, True),

    # ---- sexual ----------------------------------------------------------
    ("anilingus", X, 3, False),
    ("analsex", X, 3, False),
    ("beastiality", X, 5, False),
    ("cumslut", X, 4, False),
    ("cumbucket", X, 4, False),
    ("cumdumpster", X, 4, False),
    ("tittyfuck", X, 5, False),
    ("titwank", X, 4, False),
    ("titjob", X, 4, False),
    ("boobjob", X, 3, False),
    ("footjob", X, 3, False),
    ("buttfuck", X, 4, False),
    ("cocktease", X, 3, False),
    ("doggystyle", X, 2, False),
    ("facesitting", X, 3, False),
    ("fingerbang", X, 3, False),
    ("fisting", X, 4, False),
    ("sixtynine", X, 3, False),
    ("wetdream", X, 2, False),
    ("queef", X, 2, False),
    ("smegma", X, 3, False),
    ("pubes", X, 1, True),
    ("pubic", X, 1, True),
    ("nudity", X, 1, False),
    ("pegging", X, 2, True),
    ("bareback", X, 1, False),
    ("gspot", X, 2, False),
    ("xrated", X, 2, False),
    ("pron", X, 3, True),
    ("lolita", X, 3, False),
    ("prostie", X, 2, True),
    ("raunchy", X, 1, True),
    ("kinky", X, 1, True),
    ("swinger", X, 1, True),
    ("scat", X, 2, True),
    ("sadomasochism", X, 2, False),
    ("tubgirl", X, 4, False),

    # ---- hate ------------------------------------------------------------
    ("abbo", H, 5, True),
    ("ofay", H, 4, True),
    ("hebe", H, 4, True),
    ("dego", H, 4, True),
    ("guido", H, 3, True),
    ("koon", H, 4, True),
    ("niga", H, 5, True),
    ("nigglet", H, 5, False),
    ("junglebunny", H, 5, False),
    ("tarbaby", H, 5, False),
    ("uncletom", H, 4, False),
    ("oreo", H, 2, True),
    ("wigger", H, 4, True),  # boundary: swigger, twigger
    ("chinaman", H, 4, False),
    ("chingchong", H, 5, False),
    ("chinky", H, 5, False),
    ("sandmonkey", H, 5, False),
    ("muzzie", H, 4, True),
    ("mudslime", H, 5, False),
    ("kafir", H, 3, True),
    ("kuffar", H, 3, True),
    ("gypo", H, 4, True),
    ("gyppo", H, 4, True),
    ("bogtrotter", H, 4, False),
    ("taig", H, 4, True),
    ("fenian", H, 3, True),
    ("sheboon", H, 5, False),
    ("groid", H, 5, True),
    ("mudshark", H, 4, False),
    ("halfbreed", H, 4, False),
    ("mulatto", H, 3, True),
    ("quadroon", H, 3, True),
    ("octoroon", H, 3, True),
    ("squarehead", H, 3, False),
    ("spigger", H, 5, False),
    ("tacohead", H, 5, False),
    ("dothead", H, 5, False),
    ("paleface", H, 2, True),
    ("trailertrash", H, 3, False),
    ("inbred", H, 2, True),
    ("fucktard", H, 5, False),
    ("libtard", H, 3, False),
    ("mongo", H, 4, True),
    ("mong", H, 4, True),
    ("spacker", H, 4, True),
    ("spakker", H, 4, True),
    ("downie", H, 4, True),
    ("autist", H, 4, True),
    ("sperg", H, 4, True),
    ("trannie", H, 5, False),
    ("faggit", H, 5, False),
    ("fagit", H, 5, False),
    ("fagot", H, 5, True),  # boundary: fagoting, contrafagotto
    ("phaggot", H, 5, False),
    ("bumboy", H, 4, False),
    ("carpetmuncher", H, 5, False),
    ("muffdiver", H, 4, False),
    ("rugmuncher", H, 5, False),
    ("gaylord", H, 4, False),
    ("gayboy", H, 4, False),
    ("queerbait", H, 4, False),

    # ---- violence --------------------------------------------------------
    ("killurself", V, 5, False),
    ("hangyourself", V, 4, False),
    ("neckrope", V, 4, False),
    ("diaf", V, 4, True),
    ("foad", V, 4, True),
    ("esad", V, 4, True),
    ("schoolshooter", V, 4, False),
    ("bombthreat", V, 3, False),
    ("decapitate", V, 3, False),
    ("disembowel", V, 3, False),
    ("femicide", V, 3, False),
    ("infanticide", V, 3, False),
    ("pogrom", V, 3, False),
    ("ethniccleansing", V, 4, False),
    ("snufffilm", V, 4, False),

    # ---- drug ------------------------------------------------------------
    ("oxycontin", D, 2, False),
    ("oxycodone", D, 2, False),
    ("percocet", D, 2, False),
    ("adderall", D, 1, False),
    ("xanax", D, 1, False),
    ("codeine", D, 1, False),
    ("morphine", D, 1, False),
    ("opium", D, 2, True),  # boundary: microscopium
    ("hashish", D, 1, False),
    ("kush", D, 1, True),
    ("reefer", D, 1, True),
    ("ganja", D, 1, True),
    ("psilocybin", D, 2, False),
    ("peyote", D, 2, False),
    ("mescaline", D, 2, False),
    ("crystalmeth", D, 3, False),
    ("tweaker", D, 2, True),
]

TERMS = TERMS + ADDITIONS

# Plurals and variants that a word-boundary check would otherwise miss.
PLURALS = [
    ("niggers", H, 5, True),
    ("niggas", H, 5, True),
    ("niggaz", H, 5, True),
    ("wankers", P, 3, True),
    ("mofos", P, 4, True),
    ("milfs", X, 3, True),
    ("spicks", H, 5, True),
    ("beaners", H, 5, True),
    ("vibrators", X, 2, True),
    ("erections", X, 1, True),
    ("crappier", P, 1, True),
    ("crappiest", P, 1, True),
]

TERMS = TERMS + PLURALS

# Innocent words that contain a flagged term. The matcher drops any match that
# sits inside one of these. The word-boundary flag already covers most cases,
# so this list stays short and targets the terms that match inside a word.
ALLOW = [
    # Words that hold a term but carry no vulgar meaning. Keep this list short.
    # The gap-aware word-boundary test already covers the classic cases on its
    # own: "assassin", "class", "analysis", "grapefruit", "raccoon", "cockpit",
    # "niggardly" and "sauerkraut" all stay clean with no entry here.
    #
    # An entry that is not needed is worse than useless. "therapist" sat here
    # once, and it suppressed the correct match on "the rapist". A test fails on
    # any entry the matcher does not need.
    #
    # -- matched inside a word --
    "scunthorpe",   # cunt
    "placuntitis",  # cunt, a placental inflammation
    "placuntoma",   # cunt, a placental tumour
    "tubehead",     # behead
    "washita",      # shit, a whetstone and a river
    "bereshith",    # shit, the opening word of Genesis
    "cushitic",     # shit, a branch of Afro-Asiatic
    "brushite",     # shite, a mineral
    "marshite",     # shite, a mineral
    "girgashite",   # shite, a Canaanite people
    "agapornis",    # porn, the lovebird genus
    "epornitic",    # porn, an epidemic among birds
    "nymphosis",    # nympho, insect pupation
    "nymphoides",   # nympho, the floatingheart genus
    "tittymouse",   # titty, a titmouse variant
    #
    # -- exposed only after repeated letters collapse (the squeeze pass) --
    # tool/gen_seed.py cannot find these. The test suite sweeps the system
    # dictionary and fails on any new one. Note that "kaffir" and "bastaard"
    # are also squeeze artifacts, and they stay flagged on purpose.
    "shiitake",     # shit
    "shitake",      # shit
    "shiite",       # shite, and a religious denomination
    "shiitic",      # shit
    "annal",        # anal, and it covers "annals" and "annalist"
    "beehead",      # behead
    "inbreed",      # inbred
    "looser",       # loser
    "pollack",      # polack, and a fish
    "poorness",     # porn
    "pratt",        # prat, a surname
    "rappe",        # rape, and it covers "rapper" and "rappers"
    "rapping",      # raping, as in rap music
    "rappist",      # rapist
    "skeet",        # sket, as in skeet shooting
]

CATEGORIES = [P, X, H, V, D]
TERM_RE = re.compile(r"^[a-z0-9]+$")


def squeeze(text):
    """Collapse every run of one character down to a single character."""
    out = []
    for ch in text:
        if not out or out[-1] != ch:
            out.append(ch)
    return "".join(out)


def validate(entries, allow):
    problems = []
    seen = {}
    for term, cat, sev, w in entries:
        if not TERM_RE.match(term):
            problems.append("term is not fold-v1 normalized: %r" % term)
        if cat not in CATEGORIES:
            problems.append("unknown category %r on %r" % (cat, term))
        if not 1 <= sev <= 5:
            problems.append("severity out of range on %r: %d" % (term, sev))
        if term in seen:
            problems.append("duplicate term: %r" % term)
        seen[term] = True

    for word in allow:
        if not TERM_RE.match(word):
            problems.append("allow entry is not fold-v1 normalized: %r" % word)

    # An allow entry must actually contain a term, in its plain form or after
    # the matcher's repeated-letter squeeze. Otherwise it is dead weight.
    for word in allow:
        squeezed = squeeze(word)
        if not any(t in word or t in squeezed for t, _, _, _ in entries):
            problems.append("allow entry contains no term, so it does nothing: %r" % word)

    return problems


def main():
    problems = validate(TERMS, ALLOW)
    if problems:
        for p in problems:
            print("ERROR: " + p, file=sys.stderr)
        return 1

    entries = []
    for term, cat, sev, w in sorted(TERMS):
        entry = {"t": term, "cat": cat, "sev": sev}
        if w:
            entry["w"] = True
        entries.append(entry)

    doc = {
        "schema": 1,
        "profile": "fold-v1",
        "note": (
            "Base English term list for the vulgarity matcher. Every term is "
            "already folded to profile fold-v1. Set \"w\" to require a word "
            "boundary. Severity runs 1 to 5; clinical anatomy sits at 1."
        ),
        "entries": entries,
        "allow": sorted(set(ALLOW)),
    }

    path = os.path.join(ROOT, "data", "seed.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    by_cat = {}
    for _, cat, _, _ in TERMS:
        by_cat[cat] = by_cat.get(cat, 0) + 1
    print("terms: %d" % len(TERMS))
    for cat in CATEGORIES:
        print("  %-10s %d" % (cat, by_cat.get(cat, 0)))
    print("allow: %d" % len(set(ALLOW)))
    print("wrote data/seed.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
