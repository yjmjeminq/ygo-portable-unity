"""Engine assignments for story opponents with ten deck levels.

Each repeated name is a new list built around that engine. The final entries
preserve the character's signature plays while using later support cards.
A prefix family specifies cards to consider, not a textual deck label alone.
"""

# The order of the first three opponents' decks keeps the requested distribution
# while leaving their strongest character-specific strategies as the final decks.
EXACT_PLANS = {
    104: ("gaia", "dark-magician", "black-luster-soldier", "slifer-holactie", "gaia",
          "black-luster-soldier", "slifer-holactie", "dark-magician", "chimera", "dark-magician"),
    94: ("blue-eyes", "xyz-abc", "obelisk", "chaos-max", "xyz-abc", "obelisk",
         "chaos-max", "blue-eyes", "xyz-abc", "blue-eyes"),
    103: ("lava-golem-burn", "battle-city-dark", "revival-slime-lock", "ra",
           "lava-golem-burn", "revival-slime-lock", "lava-golem-burn", "ra",
           "revival-slime-lock", "ra"),
}

# (first, alternate, third, fourth, strongest). Early spins use the original
# archetype or a matching summoning/monster family visible in the character's
# other decks. Later spins assign a separate logical build and associated Extra.
FEATURED = {
    81: ("red-eyes", "gearfried", "flame-swordsman", "jinzo", "red-eyes"),
    105: ("shining-sarcophagus", "gadget", "buster-blader", "gaia", "shining-sarcophagus"),
    102: ("destiny-board", "skull-servant", "shaddoll", "zombie-world", "destiny-board"),
    84: ("toon", "relinquished", "illusionist", "chimera", "relinquished"),
    119: ("elemental-hero", "neos-contact", "evil-hero", "masked-hero", "elemental-hero"),
    137: ("cyber-dragon", "cyberdark", "ancient-gear", "xyz-abc", "cyber-dragon"),
    117: ("ojama", "armed-dragon", "xyz-abc", "light-darkness", "armed-dragon"),
    107: ("cyber-blader", "cyber-angel", "voiceless-voice", "drytron", "cyber-angel"),
    109: ("destiny-hero", "vision-hero", "elemental-hero", "masked-hero", "destiny-hero"),
    120: ("crystal-beast", "advanced-crystal", "malefic", "gem-knight", "crystal-beast"),
    136: ("yubel", "dark-world", "unchained", "fiendsmith", "yubel"),
    35: ("junk", "stardust", "synchron", "assault-mode", "synchron"),
    16: ("resonator", "assault-mode", "red-dragon", "savior", "resonator"),
    1: ("rose-dragon", "aroma", "sunavalon", "rikka", "rose-dragon"),
    3: ("tech-genus", "synchron", "f-a", "morphtronic", "tech-genus"),
    10: ("blackwing", "raidraptor", "harpie", "lyrilusc", "blackwing"),
    19: ("infernity", "earthbound", "infernoid", "zombie-world", "infernity"),
    36: ("timelord", "generaiders", "metaphys", "machina", "timelord"),
    174: ("gagaga", "gogogo", "zexal", "dododo", "zexal"),
    168: ("shark", "mermail", "paleozoic", "atlantean", "shark"),
    156: ("photon", "galaxy", "cipher", "tachyon", "galaxy"),
    146: ("zexal", "gagaga", "time-thief", "goblin-biker", "zexal"),
    158: ("tachyon", "galaxy", "hieratic", "dragunity", "tachyon"),
    160: ("abyss-shark", "mermail", "atlantean", "paleozoic", "abyss-shark"),
    172: ("umbral", "skull-servant", "dark-world", "shaddoll", "umbral"),
    72: ("performapal", "odd-eyes", "pendulum-magician", "supreme-king", "odd-eyes"),
    43: ("ddd", "abyss-actor", "valmonica", "metalfoes", "ddd"),
    61: ("raidraptor", "blackwing", "lyrilusc", "phantom-knights", "raidraptor"),
    68: ("speedroid", "windwitch", "gusto", "harpie", "speedroid"),
    69: ("predaplant", "despia", "shaddoll", "chimera", "predaplant"),
    71: ("phantom-knights", "raidraptor", "time-thief", "goblin-biker", "phantom-knights"),
    74: ("supreme-king", "odd-eyes", "pendulum-magician", "majespecter", "supreme-king"),
    143: ("code-talker", "firewall", "mathmech", "ignister", "firewall"),
    187: ("salamangreat", "volcanic", "battlin-boxer", "snake-eye", "salamangreat"),
    189: ("rokket", "borrel", "dragonmaid", "dragunity", "rokket"),
    140: ("trickstar", "marincess", "solfachord", "evil-twin", "trickstar"),
    177: ("ignister", "firewall", "code-talker", "mathmech", "ignister"),
}

FEATURED_RELATED = {
    28: ("one-shot", "machina", "one-shot"),
    97: ("slifer-holactie", "revival-slime-lock", "slifer-holactie"),
    80: ("ishizu-exchange", "gravekeeper", "vernusylph"),
    92: ("evol", "red-eyes", "dinosaur"),
    101: ("insect-queen", "inzektor", "beetrooper"),
    112: ("volcanic", "flamvell", "volcanic"),
    113: ("water-dragon", "zombie-world", "water-dragon"),
    118: ("koala", "nimble", "beast-rescue"),
    175: ("gagaga", "zexal", "onomat"),
    194: ("red-eyes", "gearfried", "flame-swordsman"),
    201: ("pharaoh", "zombie-world", "eldlich"),
    3304: ("insect-queen", "inzektor", "beetrooper"),
    176: ("zexal", "gagaga", "onomat"),
    32: ("fleur", "noble-knight", "baronne"),
}

# Prefixes are Korean card database names. Overlapping variants use narrower
# card families, such as Cyber Dragon/Cyberdark and the different HERO engines.
FAMILIES = {
    "abyss-actor": ("마계극단", "마계극단|마계대본", 0),
    "abyss-shark": ("칠황 · 어비스 샤크", "어비스 샤크|크리스탈 샤크|랜턴 샤크|커터 샤크|칠황|리몰라", 12744567),
    "advanced-crystal": ("어드밴스드 보옥수", "A[ (－].*보옥수|어드밴스드 보옥수|어드밴스드 다크|궁극보옥신", 79407975),
    "ancient-gear": ("앤틱 기어", "앤틱 기어", 0),
    "aroma": ("아로마", "아로마", 0),
    "armed-dragon": ("암드 드래곤", "암드 드래곤", 0),
    "assault-mode": ("버스터 모드", "버스터 모드|스타더스트 드래곤 / 버스터|레드 데몬즈 드래곤 / 버스터|사이킥 리플렉터|버스터 비스트|버스터 스나이퍼", 0),
    "atlantean": ("해황", "해황|넵트어비스", 0),
    "battlin-boxer": ("버닝나쿠라", "BK[ －(]", 0),
    "beast-rescue": ("야수족 구조대", "레스큐 캣|레스큐 래빗|레스큐 햄스터|순종적|메르피|칼란토사", 0),
    "beetrooper": ("비틀트루퍼", "비틀트루퍼", 38229962),
    "blackwing": ("BF", "^(A )?BF[ －T(]|검은 선풍|검은 날개의 선풍", 0),
    "blue-eyes": ("푸른 눈", "푸른 눈|파란 눈|하얀 영룡|태고의 백석|전설의 백석|카이바맨|궁극 융합|진정한 빛|드래곤의 독재자|백룡의 위광", 89631139),
    "borrel": ("바렐", "바렐(?!.*바렛)|더블 트리거|부트 섹터|퀵 리볼브", 0),
    "buster-blader": ("버스터 블레이더", "버스터 블레이더|파괴검|용파괴", 0),
    "cipher": ("사이퍼", "사이퍼|네오 갤럭시아이즈 사이퍼", 0),
    "code-talker": ("코드 토커", "코드 토커|마이크로 코더|코드 제너레이터|코드 엑스포터|코드 라디에이터|사이바넷", 0),
    "crystal-beast": ("보옥수", "보옥수|^보옥의|^레인보우|궁극보옥신", 0),
    "cyber-angel": ("사이버 엔젤", "사이버 엔젤|사이버 쁘띠 엔젤|기계천사|사이버 에그 엔젤", 0),
    "cyber-blader": ("사이버 블레이더", "사이버 블레이더|에뜨와르 사이버|블레이드 스케이터|사이버 튀튀|사이버 프리마|퓨전 웨폰", 10248389),
    "cyber-dragon": ("사이버 드래곤", "사이버 드래곤|^사이버 리페어|^사이버 로드|키메라테크|이멀전시 사이버|^사이버 레브", 0),
    "cyberdark": ("사이버 다크", "사이버 다크|사이버네틱 호라이즌", 37542782),
    "dark-world": ("암흑계", "암흑계", 0),
    "ddd": ("DD와 DDD", "^DD[ D(－]|계약서", 46593546),
    "despia": ("데스피아", "데스피아|낙인(?!.*(상실|추방))|알버스의 낙윤", 0),
    "destiny-board": ("위저 보드", "위저 보드|죽음의 메시지|다크 네크로피어|커스 네크로피어|죽음의 선고|다크 오컬티즘", 94212438),
    "destiny-hero": ("데스티니 히어로", "데스티니 히어로|퓨전 데스티니|^D－|데스티니 드로우", 60461804),
    "dinosaur": ("궁극의 전도 티라노", "티라노|영혼을 먹는 오비랍토르|베이비 케라사우루스|쁘띠라노돈|미세라사우루스|스크랩 랩터|화석조사|^초진화약|궁극진화약|^환창의 미세라", 18940556),
    "dododo": ("도도도", "^도도도|희망황 오노마토피아|오노마토", 0),
    "dragonmaid": ("드래곤메이드", "드래곤메이드", 0),
    "dragunity": ("드래그니티", "드래그니티|용의 계곡", 0),
    "drytron": ("드라이트론", "드라이트론|메테오니스", 0),
    "earthbound": ("지박신과 지박수", "지박신|지박수|지박계례|지박신의|지박감옥", 0),
    "elemental-hero": ("엘리멘틀 히어로", "엘리멘틀 히어로|E－이멀전시|에어맨|미라클 퓨전", 0),
    "evil-hero": ("이블 히어로", "이블 히어로|다크 퓨전|다크 콜링|다크 콘택트", 0),
    "evil-twin": ("이빌트윈", "(Evil★Twin|Live☆Twin|이빌트윈|라이브트윈)", 0),
    "evol": ("에볼드 · 에볼카이저", "에볼드|에볼더|에볼카이저|진화의", 0),
    "f-a": ("F.A.", "^F[. ]?A[. －]", 0),
    "fiendsmith": ("데먼스미스", "데먼스미스|마를 새기는", 0),
    "flamvell": ("플레임벨", "플레임벨", 0),
    "eldlich": ("황금경 엘드리치", "엘드리치|엘드릭시르|황금향", 95440946),
    "firewall": ("파이어월", "파이어월|프로텍트코드|링그리보|사이버스 디세브룸", 0),
    "flame-swordsman": ("화염의 검사", "화염의 검사|투화염의 검사|비룡의 화염|투기의 화염|불꽃의 검|살라만도라", 1047075),
    "fleur": ("플뢰르 기사", "플뢰르|시간꽃|성기사", 45037489),
    "baronne": ("시간꽃의 바로네스", "플뢰르|시간꽃|죽음의 꽃|네크로 싱크론|^꽃기사", 84815190),
    "gadget": ("가제트", "가제트|기동제독|^기동병사|기동요새|기동장치", 0),
    "gagaga": ("가가가", "^가가가|오노마토", 0),
    "galaxy": ("갤럭시아이즈", "^갤럭시|銀河|갤럭시아이즈", 0),
    "gearfried": ("기어프리드", "기어프리드|^철의 기사|메탈화|메탈 데블", 423705),
    "gem-knight": ("젬나이트", "젬나이트|브릴리언트 퓨전|브릴리언트 로즈", 0),
    "generaiders": ("제너레이드", "제너레이드", 0),
    "goblin-biker": ("고블린라이더", "고블린라이더|^고블린의", 0),
    "gogogo": ("고고고", "^고고고|오노마토|골렘", 0),
    "gravekeeper": ("묘지기와 네크로밸리", "묘지기|네크로밸리", 17393207),
    "gusto": ("가스타", "^가스타|가르도스", 0),
    "harpie": ("해피 레이디", "해피(?!니스|러버)|사이버 본디지", 0),
    "hieratic": ("성각", "성각", 0),
    "ignister": ("@이그니스터", "이그니스터|＠|^Ai[ .－'「]|^Ai랜드", 0),
    "infernoid": ("인페르노이드", "인페르노이드|연옥의", 0),
    "infernity": ("인페르니티", "인페르니티", 0),
    "insect-queen": ("곤충 여왕", "곤충 여왕|고치|대곤충|기생충|킬러 니들|블랙 아이언|공진충|응전의 G", 41456841),
    "inzektor": ("인잭터", "인잭터|인섹터", 0),
    "illusionist": ("천년의 눈", "천년의 눈|새크리파이스|새크리파이스 이터|디아벨스타|환조마|환상마", 0),
    "ishizu-exchange": ("현세와 명계의 역전", "현세와 명계|켈도|켈벡|켈베크|아기도|무도라|묘지기의 함정|고첨병", 17484499),
    "jinzo": ("인조인간", "인조인간|사이코 (쇼크|웨이브|기가사이버)|사이버 에너지 쇼크|사이코 로드", 0),
    "junk": ("정크", "^정크|^도플 워리어|^튜닝|^싱크론 익스플로러", 0),
    "light-darkness": ("라이트 앤드 다크니스", "라이트 앤드 다크니스|혼원룡|휘백룡|암흑룡|카오스 테리토리|혼돈마룡", 19652159),
    "koala": ("코알라와 캥거루", "코알라|코알랏코|캥거루|에어즈록|마스터 오브 OZ", 0),
    "lyrilusc": ("LL", "^LL[－ (]", 0),
    "machina": ("머시너즈", "^머시너즈|^기동병사|^기동제독", 0),
    "malefic": ("Sin", "^Sin[ －(]", 0),
    "majespecter": ("마제스펙터", "^마제스펙터", 0),
    "marincess": ("마린세스", "마린세스", 0),
    "masked-hero": ("마스크드 히어로", "마스크드 히어로|마스크 체인지|폼 체인지|새도우 미스트", 58481572),
    "mathmech": ("참기", "참기", 0),
    "mermail": ("머메일", "머메일|어비스피어|어비스콜|어비스케일|어비스팀", 0),
    "metalfoes": ("메탈포제", "메탈포제", 0),
    "metaphys": ("메타파이즈", "메타파이즈", 0),
    "morphtronic": ("디포머", "^D[ －]|디포머", 0),
    "neos-contact": ("네오스 콘택트", "네오스|네오 스페이시언|네오 스페이스|콘택트", 0),
    "nimble": ("재빠른 야수", "재빠른|에어즈록", 0),
    "noble-knight": ("성기사", "성기사|성검|샤를", 0),
    "odd-eyes": ("오드아이즈", "오드아이즈|나선의 스트라이크버스트|천공의 홍채", 0),
    "one-shot": ("원샷 캐논", "원샷 캐논|원샷 로켓|원샷 부스터|원샷 원드|제트 싱크론|아이언 콜|고철의 신호", 13574687),
    "onomat": ("오노마토", "오노마토|가가가|고고고|도도도|주바바|희망황 호프", 0),
    "ojama": ("방해꾼", "^방해꾼", 0),
    "paleozoic": ("버제스토마", "버제스토마|개구리|떡 깨구리", 0),
    "pendulum-magician": ("펜듈럼 마술사", "마술사$|펜듈럼 콜|듀얼리스트 어드벤트|조현의 마술사|아스트로그래프", 0),
    "performapal": ("EM", "^EM[ －(]", 0),
    "pharaoh": ("스피릿 오브 파라오", "스피릿 오브 파라오|제일의 관|제이의 관|제삼의 관|미라|파라오의 사도", 25343280),
    "phantom-knights": ("팬텀 나이츠", "팬텀 나이츠|팬텀 포그|팬텀 포스|팬텀 윙", 0),
    "photon": ("포톤", "^포톤|갤럭시아이즈 포톤", 0),
    "predaplant": ("프레데터 플랜츠", "프레데터 플랜츠|프레데터 (플래닝|프랙티스|플라스트|퓨전)|스타브 베놈", 0),
    "raidraptor": ("RR", "^RR[－ (]|^RUM－레이드", 0),
    "relinquished": ("새크리파이스", "새크리파이스(?!.*(로터스|스워드|데먼|스파이더))|검은 환상|천개의 눈", 64631466),
    "red-dragon": ("레드 데몬즈", "레드 데몬|염마룡|스카레드|붉은 연옥|데몬즈 체인", 0),
    "red-eyes": ("붉은 눈", "붉은 눈|흑강룡|흑염탄|흑룡의|전설의 흑석", 74677422),
    "resonator": ("레조네이터", "레조네이터|콜 레조네이터|레드 라이징|소울 레조네이터", 0),
    "rikka": ("육화", "^육화", 0),
    "rokket": ("바렛", "바렛|퀵 리볼브|부트 섹터|스트라이커 드래곤", 0),
    "rose-dragon": ("로즈 드래곤", "로즈 드래곤|피닉시안|블랙 가든|가든 로즈|장미의|로즈 프린세스", 73580471),
    "salamangreat": ("샐러맨그레이트", "샐러맨그레이트|전생염수", 0),
    "savior": ("세이비어", "세이비어|구세룡|광래하는 기적|집결한 소망|스타더스트 트레일", 0),
    "shaddoll": ("섀도르", "섀도르", 0),
    "shark": ("샤크", "(?<!어비스 )샤크|크래커 드래곤", 0),
    "shining-sarcophagus": ("빛의 황금궤", "빛의 황금궤|간드라|암흑 마술사|동료의 연|트리콜로르 가제트|사일런트 .* 제로|파괴룡의", 0),
    "skull-servant": ("저주받은 하인", "저주받은 하인|와이트|불사의 소생", 0),
    "snake-eye": ("스네이크아이", "스네이크아이|죄보|디아벨스타", 0),
    "solfachord": ("도레미코드", "도레미코드|솔파코드", 0),
    "speedroid": ("스피드로이드", "^SR[ －(]|^HSR|스피드로이드|클리어윙|크리스탈윙", 0),
    "stardust": ("스타더스트", "스타더스트|코즈믹 블레이저|슈팅 스타|슈팅 퀘이사", 44508094),
    "sunavalon": ("생아발론", "생아발론|생바인|생시드", 0),
    "supreme-king": ("패왕룡", "패왕권룡|패왕문|패왕룡|패왕의|패왕천룡", 13331639),
    "synchron": ("싱크론", "싱크론|^조율$|싱크로 오버테이크|액셀싱크로|액셀 싱크로", 0),
    "tachyon": ("타키온", "타키온|칠황승격|칠황의", 0),
    "tech-genus": ("TG", "^TG[－ (]", 0),
    "time-thief": ("크로노다이버", "크로노다이버", 0),
    "timelord": ("시계신", "시계신|아인 소프|허무계 아인|무한광", 0),
    "toon": ("툰 월드", "^툰[ －]|판타스틱 툰", 0),
    "train": ("초노급포탑열차", "열차|기관차|전회조차|긴급 다이어|기관중련|탄환특급", 26096328),
    "trickstar": ("트릭스터", "^트릭스터|^트릭스타", 0),
    "umbral": ("엄브럴", "엄브럴|마스커레이드|데스 키메라 드래곤|망롱룡|RUM－바리언즈 포스", 49456901),
    "unchained": ("언체인드", "언체인드", 0),
    "valmonica": ("바르모니카", "바르모니카", 76821171),
    "vernusylph": ("봄화정", "^봄화정|의 봄화정", 55125728),
    "vision-hero": ("비전 히어로", "비전 히어로|비전 퓨전|^환영 히어로", 0),
    "voiceless-voice": ("순성 의식", "순성|로가디언", 10774240),
    "volcanic": ("볼캐닉 엠페러", "^볼캐닉|브레이즈 캐논", 46412900),
    "water-dragon": ("워터 드래곤", "워터 드래곤|듀테리온|옥시게돈|하이드로게돈|본딩", 6022371),
    "windwitch": ("WW", "^WW[ －(]|윈드 위치", 0),
    "xyz-abc": ("XYZ와 ABC", "드래곤 (버스터|캐논|헤드|이어헤드)|하이퍼 드래곤|메탈 캐터필러|어썰트 코어|버스터 드레이크|크러시 와이반|유니온 (격납고|컨트롤러|액티베이션|드라이버)|AtoZ", 75748977),
    "yubel": ("유벨", "유벨|나이트메어 스론|나이트메어 페인|새크리파이스 로터스|그레이브 스쿼머|이터널 페이버릿", 0),
    "zexal": ("제알 · 유토피아", "제알|유토피아|희망황|^ZW[ －(]|^ZS[ －(]|오노마토", 0),
    "zombie-world": ("언데드 월드", "언데드 월드|유니좀비|시라누이|시체계의 밴시|그로우업 블룸|사령왕|좀비 마스터|좀비 캐리어|고블린 좀비", 0),
}

# Explicit packs where the engine is composed of cards with several different
# archetype flags, or matching a name prefix would include unrelated cards.
# (localized title, ace, count-by-code main, prioritized extra, solo starts).
CUSTOM = {
    "dark-magician": ("블랙 매지션", 46986414, {
        46986414: 2, 38033121: 1, 7084129: 3, 97631303: 3, 12266229: 3,
        23020408: 3, 47222536: 2, 59514116: 2, 41735184: 1, 1784686: 1,
        2501624: 3, 342673: 2, 71696014: 1, 48680970: 2,
    }, (50237654, 37818794, 5829717, 41721210, 43892408), (7084129, 97631303, 12266229, 2501624)),
    "slifer-holactie": ("오시리스와 호르아크티", 10000020, {
        10000020: 2, 10000000: 1, 10000010: 1, 10000040: 1, 79387392: 3,
        59094601: 3, 39913299: 2, 52112003: 3, 5253985: 2, 15735108: 2,
        26905245: 2, 10000080: 1, 15771991: 2, 269012: 1, 74875003: 2,
    }, (42166000,), (79387392, 74875003)),
    "black-luster-soldier": ("카오스 솔저", 70551291, {
        70551291: 2, 5405694: 1, 54484652: 1, 72989439: 1, 45948430: 3,
        14094090: 1, 79234734: 2, 73694478: 1, 29904964: 2, 7841921: 2,
        95492061: 3, 23401839: 3, 96729612: 3, 13048472: 2, 46052429: 2,
    }, (49202162,), (95492061, 23401839, 96729612, 13048472)),
    "gaia": ("마방 기사 가이아", 15989522, {
        2106266: 3, 34130561: 3, 61525276: 3, 38590361: 3, 7913375: 3,
        49328340: 1, 29477860: 1, 50354944: 2, 33318980: 1,
        8910971: 1, 6368038: 1, 73129314: 2,
    }, (15989522, 66889139), (2106266, 34130561, 7913375)),
    "chimera": ("환상수왕 키메라", 4796100, {
        5818798: 3, 23076639: 3, 28954097: 3, 92565383: 3, 63136489: 3,
        36637374: 2, 44362883: 2,
        68468459: 1, 82738008: 1,
    }, (4796100, 1769875, 38264974, 11321089, 69601012), (5818798, 28954097, 92565383, 44362883)),
    "chaos-max": ("푸른 눈의 카오스 MAX", 55410871, {
        55410871: 3, 20654247: 1, 21082832: 3, 46052429: 3, 89631139: 3,
        95492061: 3, 23401839: 3, 96729612: 2, 13048472: 1, 38517737: 1,
        66961194: 2, 71039903: 2, 67768675: 1,
    }, (59822133, 2129638, 89604813), (95492061, 23401839, 96729612, 66961194)),
    "obelisk": ("오벨리스크의 거신병", 10000000, {
        10000000: 3, 79868386: 2, 269012: 2, 5253985: 3, 7373632: 2,
        15735108: 2, 79387392: 3, 26905245: 2, 17444133: 2, 10000080: 1,
        79339613: 2, 85182315: 2, 15771991: 2,
    }, (42166000,), (79387392, 17444133)),
    "ra": ("라의 익신룡", 10000010, {
        10000010: 2, 10000080: 2, 10000090: 1, 15771991: 3, 79387392: 3,
        11587414: 3, 41044418: 2, 26905245: 2, 74875003: 2, 5253985: 2,
        15735108: 2, 269012: 1, 7373632: 1, 52112003: 2,
    }, (42166000,), (79387392, 11587414, 74875003)),
    "lava-golem-burn": ("라바 골렘 고문 번", 102380, {
        102380: 3, 54704216: 2, 85562745: 3, 3510565: 3, 99517131: 3,
        24068492: 3, 27053506: 3, 62279055: 2, 44656491: 2,
        58775978: 2, 12607053: 2, 91623717: 2, 39526584: 2,
    }, (), (3510565,)),
    "revival-slime-lock": ("재생 슬라임 락", 42166000, {
        31709826: 3, 21558682: 2, 21770260: 2, 26905245: 3, 46821314: 1,
        79387392: 3, 57953380: 2, 15771991: 2, 44656491: 2, 30606547: 2,
        12538374: 2, 19089195: 2, 9126351: 2, 98045062: 2,
    }, (42166000,), (79387392, 9126351)),
    "battle-city-dark": ("배틀시티 암흑", 4335645, {
        4335645: 3, 17597059: 2, 56043446: 2, 76052811: 2, 99050989: 3,
        81985784: 3, 83011277: 3, 66690411: 2, 15800838: 2, 58775978: 2,
        78010363: 2, 5758500: 1, 94192409: 2,
    }, (), (4335645, 81985784, 83011277, 78010363)),
}

# Starters with costs or an ignition effect that a description-only scan cannot
# recognize. These refer to the package-specific lines, such as a legal Union
# target in deck, or Spirit of White enabling the Blue-Eyes route.
KNOWN_STARTS = {
    "abyss-shark": (7150545,),
    "atlantean": (21565445, 78868119),
    "ddd": (11609969, 46372010),
    "mathmech": (36521307, 57160136, 16188701),
    "predaplant": (35272499,),
    "speedroid": (81275020,),
    "phantom-knights": (81275020, 10802915),
    "toon": (89997728,),
    "blue-eyes": (8240199, 80326401, 17947697, 67768675, 17725109, 66961194),
    "xyz-abc": (66399653, 38527680, 6355563, 73628505),
    "jinzo": (51916032, 39987731),
    "red-eyes": (92353449, 6172122),
    "assault-mode": (74644400, 39015),
    "beetrooper": (2656842,),
    "dinosaur": (44335251, 47325505),
    "volcanic": (22411609, 76459806),
    "water-dragon": (43017476,),
    "train": (25274141,),
    "destiny-hero": (52947044,),
    "flamvell": (23297235,),
    "infernity": (28985331, 43694650, 99745551),
    "nimble": (68353324,),
    "gravekeeper": (17393207,),
    "armed-dragon": (34611551,),
    "onomat": (85119159, 6595475),
    "vision-hero": (40044918, 27780618),
    "masked-hero": (40044918, 50720316),
    "elemental-hero": (40044918,),
    "buster-blader": (49823708,),
}

# Support packages that belong to the strategy even when cards have a different
# printed prefix. Each includes material or searchers rather than filler bosses.
RELATED_CARDS = {
    "assault-mode": (74644400, 3431737, 39015, 44508094, 70902743),
    "abyss-shark": (7150545, 61496006, 70156946, 98881700, 90315086, 20358953, 47840168, 11845050, 59479050, 440556, 12744567),
    "blue-eyes": (8240199, 80326401, 17947697, 67768675, 17725109, 66961194, 42097666, 71143015),
    "cyber-blader": (32807846, 24094653, 11460577, 97023549, 17194258, 48130397),
    "mathmech": (57160136, 16188701, 18789533, 63528891, 52698008, 88093706),
    "firewall": (57160136, 16188701, 18789533, 63528891, 52698008, 88093706),
    "flame-swordsman": (73714736, 35697544, 37531679, 32807846, 24094653),
    "atlantean": (21565445, 78868119, 17080584, 9453320),
    "mermail": (21565445, 78868119, 17080584, 9453320),
    "phantom-knights": (81275020, 53932291, 10802915, 62709239),
    "infernity": (28985331, 14536035, 13521194, 43694650, 99745551, 81439173, 93946239),
    "koala": (14878871, 423585, 68353324),
    "nimble": (14878871, 423585, 68353324),
    "jinzo": (81439173, 83764718, 58577036, 43040603),
    "light-darkness": (2295440, 99234526, 61901281, 66574418, 75500286),
    "lyrilusc": (87209160, 14816857, 50810455, 56196385),
    "masked-hero": (40044918, 50720316, 213326, 32807846, 22865492),
    "relinquished": (41426869, 13048472, 23401839, 95492061, 96729612),
    "vision-hero": (40044918, 50720316, 22865492, 32807846, 52947044),
    "water-dragon": (47325505, 7913375, 22587018),
    "one-shot": (6142213, 60187739, 36042825, 9742784, 36280194, 60303245),
    "windwitch": (81439173, 2295440),
    "xyz-abc": (62651957, 66399653, 38527680, 75748977, 1561110, 2133971, 91998119, 65172015),
}

# Compact packages selected by the deck's dominant monster type. Each summary
# lists the cards necessary for the starter to deliver its documented payoff.
# (supported race, main counts, required Extra, independent starter codes)
OPENING_PACKAGES = (
    # Connector summons Aqua Dolphin and supplies two Warriors for Isolde.
    (1, {85840608: 3, 32807846: 3, 17955766: 1}, (59934749,), (85840608, 32807846)),
    # Spellbook Magician -> Secrets -> Knowledge sets up spell resources.
    (2, {14824019: 3, 89739383: 3, 23314220: 2}, (), (14824019, 89739383)),
    # Ostinato opens without monsters; Refrain adds Couplet.
    (4, {9113513: 3, 64881644: 3, 90276649: 2, 62895219: 1},
     (56208713, 83793721), (9113513, 64881644)),
    # Engraver/Tractus finds Lurrie; Lurrie makes Requiem.
    (8, {60764609: 3, 98567237: 3, 97651498: 1}, (2463794,), (60764609, 98567237)),
    # Uni-Zombie or its normal summon recruiter places Banshee in the GY.
    (16, {49959355: 3, 94801854: 3, 66570171: 1, 4064256: 1}, (), (49959355, 94801854)),
    # Gearframe and Redeployment each get the Machina setup started.
    (32, {42940404: 3, 86852702: 3, 5556499: 2, 87074380: 1}, (), (42940404, 86852702)),
    # Revealer uses a discard to access the Ice Barrier Tuner.
    (64, {88494899: 3, 84206435: 3, 18482591: 1, 44308317: 1},
     (50321796,), (88494899, 84206435)),
    # Trooper and Bonfire start the Volcanic resource engine.
    (128, {22411609: 3, 85106525: 3, 76459806: 2, 85250352: 1, 46412900: 1},
     (), (22411609, 85106525, 76459806)),
    # Sunseed is a real one-card Link engine only with Dryas and Sowing.
    (1024, {27520594: 3, 911883: 3, 53286626: 1}, (93896655,), (27520594, 911883)),
    # Blackwing / Winged Beast variants use the Tri-Brigade GY engine.
    (512, {87209160: 3, 14816857: 2, 56196385: 2, 50810455: 1}, (), (87209160,)),
    # Scout Buggy deploys a second copy to make Armed Horn.
    (2048, {2656842: 3, 39041550: 2, 65430555: 1}, (2834264, 38229962), (2656842,)),
    # Seyfert converts itself to a matching Chaos extender; Shrine sets up
    # a Normal Dragon/Stone line only when the required cards are all present.
    (8192, {15381421: 3, 61901281: 1, 99234526: 1}, (), (15381421,)),
    # Rescue Cat deploys two low-level Beasts, and Priest gets to Cat.
    (16384, {14878871: 3, 423585: 3, 68353324: 3},
     (), (14878871, 68353324)),
    # Oviraptor and Fossil Dig access the Dinosaur GY/setup engine.
    (65536, {44335251: 3, 47325505: 3, 38572779: 2, 18940556: 1}, (), (44335251, 47325505)),
    # Cutter plus the WATER extender it summons makes a Rank 4 Shark.
    (131072, {7150545: 3, 98881700: 2, 70156946: 2}, (440556,), (7150545,)),
    # Diva or Neptabyss gets the Atlantean/WATER engine into motion.
    (262144, {78868119: 3, 21565445: 3, 17080584: 1, 74311226: 1},
     (9453320,), (78868119, 21565445)),
    # Ze Amin and Emergency Teleport reach Foxy Tune and Deer Note.
    (1048576, {19535693: 3, 67723438: 3, 55920742: 2, 6609736: 1},
     (28403802, 44708154), (19535693, 67723438, 55920742)),
    # The Cyberse cards can develop from one normal summon into a Link.
    (16777216, {16188701: 3, 57160136: 3, 18789533: 2, 63528891: 1},
     (52698008, 88093706), (16188701, 57160136)),
    # Millennium field bodies pay LP or reveal the cross. The cross is
    # included for resource play, so the support represents a real engine.
    (2097152, {1164211: 3, 74169516: 3, 38775407: 3, 37613663: 1},
     (83257450,), (1164211, 74169516, 38775407)),
)

CLASSIC_FIEND_PACKAGE = (
    {10802915: 3, 26202165: 3},
    (60303245, 62709239),
    (10802915, 26202165),
)
WIND_PACKAGE = (
    {43722862: 3, 70117860: 1, 71007216: 1},
    (82044279,),
    (43722862,),
)


# Secondary choices for high levels that already have the matching attribute.
# Use these only after preserving a substantial primary engine.
ATTRIBUTE_PACKAGES = (
    (0x20, {86120751: 3, 47679935: 3, 74063034: 2},
     (34755994, 75286621), (86120751, 47679935)),
    (0x10, {84941194: 3, 16528181: 3, 26984177: 2, 74725513: 1},
     (28400508,), (84941194, 16528181, 26984177)),
    (0x01, {3285551: 3, 30680659: 3, 39568067: 1, 2563463: 1, 38745520: 1},
     (), (3285551, 30680659)),
)


SPECIAL_SUPPORT = {
    "marincess": ({91953000: 3, 36492575: 3}, (43735670,), (91953000, 36492575)),
    "rokket-start": ({31443476: 3, 68464358: 2, 25554552: 2}, (73539069,), (31443476,)),
    "shark-cutter": ({7150545: 3, 70156946: 2, 98881700: 1}, (440556,), (7150545,)),
    "cyberse-normal": ({62706865: 3, 36211150: 2, 57160136: 3, 16188701: 3},
                      (32995276, 52698008), (62706865, 57160136, 16188701)),
    "insect-gokipole": ({17535764: 3, 35052053: 2, 81439173: 3},
                       (60303245,), (17535764, 81439173)),
    "hero-air": ({40044918: 3, 213326: 3}, (), (40044918, 213326)),
    "snake-eye-fire": ({9674034: 3, 85106525: 3, 89023486: 1, 90241276: 1},
                       (29301450,), (9674034, 85106525)),
    "ancient-warrior-link": ({87209160: 3, 57103969: 3, 14816857: 1, 56196385: 1, 50810455: 1},
                             (), (87209160, 57103969)),
    "rank-four": ({8633261: 3, 34022970: 3, 35844557: 2, 72238166: 1},
                  (7511613, 34909328), (8633261, 34022970, 35844557)),
    "link-twins": ({36326160: 3, 73810864: 3, 37582948: 3, 8083925: 1},
                   (9205573, 36609518, 93672138), (36326160, 73810864, 37582948, 8083925)),
    "bees": ({28388927: 3, 56146300: 3, 67441879: 2, 54772065: 2}, (27565379, 53950487), (28388927,)),
    "alchemy": ({30241314: 3, 54493213: 2, 81674782: 2}, (), (30241314,)),
    "ritual": ({95492061: 3, 23401839: 3, 13048472: 3}, (), (95492061, 23401839, 13048472)),
    "speed-machine": ({81275020: 3, 53932291: 2, 59640711: 1},
                      (31320433, 95992081), (81275020,)),
    "combat-machine": ({67922702: 3, 31533704: 2, 72291078: 2, 16943770: 1},
                       (44097050,), (67922702,)),
}
