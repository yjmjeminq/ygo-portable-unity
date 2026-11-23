# -*- coding: utf-8 -*-
import os
import json
from PIL import Image, ImageDraw, ImageFont

BASE_DIR = r"Assets/StreamingAssets/TagForce/GX"
SD_DIR = os.path.join(BASE_DIR, "Characters_SD")
ST_DIR = os.path.join(BASE_DIR, "Characters_Standing")

os.makedirs(SD_DIR, exist_ok=True)
os.makedirs(ST_DIR, exist_ok=True)

# 46종 캐릭터별 고유 비주얼 테마 정의 (원작 애니메이션 고증)
CHAR_SPECS = [
    ("player_heroine", "여주인공", "HEROINE", (211, 47, 47), (255, 255, 255), "RED CAP & PONYTAIL"),
    ("119", "유우키 쥬다이", "JUDAI", (211, 47, 47), (245, 176, 65), "SLIFER RED HERO"),
    ("209", "유우키 쥬다이 (유벨)", "JUDAI-YUBEL", (136, 14, 79), (46, 204, 113), "ODD-EYES FUSION"),
    ("214", "패왕 쥬다이", "SUPREME KING", (26, 26, 26), (255, 215, 0), "DARK SUPREME ARMOR"),
    ("137", "마루후지 료", "KAISER RYO", (13, 71, 161), (255, 255, 255), "OBELISK BLUE WHITE"),
    ("207", "헬 카이저 료", "HELL KAISER", (33, 33, 33), (142, 36, 170), "UNDERGROUND CHOKER"),
    ("117", "만죠메 쥰", "CHAZZ THUNDER", (17, 17, 17), (255, 235, 59), "BLACK COAT THUNDER"),
    ("3100", "만죠메 쥰 (빛)", "WHITE CHAZZ", (255, 255, 255), (176, 190, 197), "SOCIETY OF LIGHT"),
    ("107", "텐죠인 아스카", "ALEXIS", (25, 118, 210), (255, 160, 0), "OBELISK BLUE QUEEN"),
    ("199", "텐죠인 아스카 (빛)", "WHITE ALEXIS", (224, 247, 250), (128, 222, 234), "WHITE ICE PRINCESS"),
    ("111", "텐죠인 후부키", "ATTICUS", (25, 118, 210), (255, 112, 67), "HAWAIIAN UKULELE"),
    ("110", "다크니스 후부키", "DARKNESS", (26, 26, 26), (229, 57, 53), "SKULL DRAGON MASK"),
    ("128", "마루후지 쇼", "SYRUS", (211, 47, 47), (129, 212, 250), "SLIFER RED GLASSES"),
    ("118", "마에다 하야토", "CHUMLEY", (211, 47, 47), (141, 110, 99), "KOALA BANDANA"),
    ("113", "미사와 다이치", "BASTION", (245, 127, 23), (46, 125, 50), "RA YELLOW 6-DECKS"),
    ("132", "티라노 켄잔", "HASSLEBERRY", (245, 127, 23), (198, 40, 40), "DINO FOSSIL VEST"),
    ("109", "에드 피닉스", "ASTER PHOENIX", (74, 20, 140), (224, 224, 224), "DESTINY HERO SUIT"),
    ("120", "요한 안데르센", "JESSE", (0, 150, 136), (233, 30, 99), "CRYSTAL BEAST TEAL"),
    ("121", "짐 크로커다일", "JIM COOK", (109, 76, 65), (76, 175, 80), "FOSSIL CROCODILE"),
    ("112", "오브라이언", "AXEL BRODIE", (38, 50, 56), (255, 87, 34), "VOLCANIC GUN DISK"),
    ("106", "아몬 가람", "ADRIAN", (27, 94, 32), (255, 215, 0), "CLOUD WEST SUIT"),
    ("114", "사오토메 레이", "BLAIR", (211, 47, 47), (255, 183, 77), "RED MAIDEN TWINTAIL"),
    ("133", "크로노스", "CROWLER", (142, 36, 170), (255, 215, 0), "ANTIQUE GEAR COAT"),
    ("125", "다이토쿠지", "BANNER", (121, 85, 72), (255, 236, 179), "CAT PHARAOH LOWPONY"),
    ("108", "암나엘", "AMNAEL", (26, 26, 26), (255, 215, 0), "ALCHEMY MASK HOOD"),
    ("127", "사메지마 교장", "SHEPPARD", (13, 71, 161), (255, 215, 0), "HEADMASTER MUSTACHE"),
    ("115", "나폴레옹 교감", "BONAPARTE", (27, 94, 32), (229, 57, 53), "BICORNE HAT DEPUTY"),
    ("123", "마르탄 (유벨)", "MARCEL", (25, 118, 210), (255, 235, 59), "YUBEL POSSESSED BOY"),
    ("136", "유벨", "YUBEL", (74, 20, 140), (0, 230, 118), "DEMON DRAGON WINGS"),
    ("131", "타이탄", "TITAN", (62, 39, 35), (255, 215, 0), "SHADOW DUEL BIKER"),
    ("3101", "어둠의 타이탄", "DARK TITAN", (26, 26, 26), (183, 28, 28), "MATADOR DARK CAPE"),
    ("122", "카게마루 (젊음)", "KAGEMARU", (26, 26, 26), (229, 57, 53), "SACRED BEAST MUSCLE"),
    ("3102", "D.D.", "THE D", (49, 27, 146), (255, 215, 0), "HEAVYWEIGHT CHAMPION"),
    ("126", "사이오 타쿠마", "SARTORIUS", (237, 231, 246), (123, 31, 162), "ARCANA TAROT SUIT"),
    ("213", "사이오 미즈치", "SARINA", (183, 28, 28), (255, 255, 255), "SHRINE MAIDEN MIKO"),
    ("116", "카뮬라", "CAMULA", (74, 20, 140), (229, 57, 53), "VAMPIRE BAT GOTHIC"),
    ("129", "타니아", "TANIA", (230, 81, 0), (255, 235, 59), "AMAZONESS LEOPARD"),
    ("202", "돈 잘우그", "DON ZALOOG", (74, 20, 140), (255, 215, 0), "SCORPION REVOLVER"),
    ("201", "아비도스 3세", "ABYDOS", (255, 215, 0), (13, 71, 161), "PHARAOH NEMES GOLD"),
    ("130", "프로페서 코브라", "VIPER", (55, 71, 79), (229, 57, 53), "DEATHBELT MILITARY"),
    ("225", "후지와라 유스케", "YUSUKE", (25, 118, 210), (179, 229, 252), "CLEAR PRISM DISK"),
    ("3103", "트루맨", "MR. T", (17, 17, 17), (255, 255, 255), "FEDORA BLACK SUIT"),
    ("124", "다크니스", "NIGHTSHROUD", (10, 10, 10), (213, 0, 0), "ZERO INFINITY CORE"),
    ("215", "아유카와 에미", "FONDA", (255, 255, 255), (142, 36, 170), "DOCTOR CLINIC COAT"),
    ("205", "하라다 준코", "JASMINE", (25, 118, 210), (171, 71, 188), "BLUE HIGH PONYTAIL"),
    ("206", "하마구치 모모에", "MINDY", (25, 118, 210), (141, 110, 99), "BLUE BOB SHORT")
]

def make_sd(cid, name, eng, primary, accent, tag):
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 배경 원형 네온 글로우
    d.ellipse([36, 36, 476, 476], fill=(primary[0], primary[1], primary[2], 220), outline=accent, width=12)
    d.ellipse([64, 64, 448, 448], outline=(255, 255, 255, 180), width=4)
    # 캐릭터 중앙 심볼 박스
    d.rounded_rectangle([116, 120, 396, 330], radius=24, fill=(18, 22, 30, 240), outline=accent, width=6)
    d.text((256, 180), eng[:10], fill=accent, anchor="mm")
    d.text((256, 240), name, fill=(255, 255, 255), anchor="mm")
    d.text((256, 290), tag, fill=(180, 200, 220), anchor="mm")
    # 하단 명패
    d.rectangle([96, 370, 416, 430], fill=(10, 14, 20, 250), outline=accent, width=4)
    d.text((256, 400), f"ID: {cid}", fill=(255, 255, 255), anchor="mm")
    path = os.path.join(SD_DIR, f"sd_{cid}.png")
    img.save(path)

def make_standing(cid, name, eng, primary, accent, tag):
    img = Image.new("RGBA", (1024, 1536), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 마스터 듀얼 스타일 카드 스탠딩 베이스
    d.rounded_rectangle([100, 100, 924, 1436], radius=40, fill=(15, 20, 28, 235), outline=accent, width=16)
    d.rounded_rectangle([130, 130, 894, 1406], radius=30, fill=(primary[0], primary[1], primary[2], 120), outline=(255, 255, 255, 160), width=4)
    # 상단 배너
    d.rounded_rectangle([180, 220, 844, 420], radius=20, fill=(10, 12, 16, 240), outline=accent, width=8)
    d.text((512, 290), eng, fill=accent, anchor="mm")
    d.text((512, 360), name, fill=(255, 255, 255), anchor="mm")
    # 중앙 엠블럼 및 디테일 박스
    d.ellipse([262, 540, 762, 1040], fill=(primary[0], primary[1], primary[2], 180), outline=accent, width=14)
    d.text((512, 790), eng[:4], fill=(255, 255, 255), anchor="mm")
    # 하단 특징 명패
    d.rounded_rectangle([200, 1160, 824, 1320], radius=16, fill=(10, 14, 22, 240), outline=accent, width=6)
    d.text((512, 1220), tag, fill=accent, anchor="mm")
    d.text((512, 1270), f"FORM ID: {cid}", fill=(255, 255, 255), anchor="mm")
    path = os.path.join(ST_DIR, f"full_{cid}.png")
    img.save(path)

for spec in CHAR_SPECS:
    make_sd(*spec)
    make_standing(*spec)

print(f"[완료] 총 {len(CHAR_SPECS)*2}개 캐릭터 에셋 자동 생성 완료!")
