# 5D’s RPG 월드 에셋

`Assets/StreamingAssets/TagForce/UI/`에 배치한 PNG의 원본과 사용 조건입니다.

| 프로젝트 파일 | 받은 패키지와 원본 파일 | 사용 조건 |
| --- | --- | --- |
| `icon_quest_main.png` | [Kenney Emotes Pack](https://kenney.nl/assets/emotes-pack) · `PNG/Vector/Style 1/emote_alert.png` | CC0. 금색으로 조정하고 128×128 투명 캔버스에 배치 |
| `icon_quest_sub.png` | [Kenney Emotes Pack](https://kenney.nl/assets/emotes-pack) · `PNG/Vector/Style 1/emote_dots3.png` | CC0. 하늘색으로 조정하고 128×128 투명 캔버스에 배치 |
| `icon_duel_ready.png` | [Crossed swords](https://game-icons.net/1x1/lorc/crossed-swords.html) · 투명 PNG | Lorc, CC BY 3.0. 색과 크기 조정 |
| `icon_map_lock.png` | [Padlock](https://game-icons.net/1x1/lorc/padlock.html) · 투명 PNG | Lorc, CC BY 3.0. 색과 크기 조정 |
| `hud_target.png` | [Lucide Target 0.468.0](https://github.com/lucide-icons/lucide/blob/0.468.0/icons/target.svg) | 96×96 투명 PNG로 변환. Lucide ISC 및 Feather MIT 고지는 같은 폴더의 `Lucide-LICENSE.txt` |
| `panel_dialogue_frame.png` | [Kenney UI Pack – Sci-Fi](https://kenney.nl/assets/ui-pack-sci-fi) · `PNG/Extra/Double/panel_glass_notches_top.png` | CC0. 1024×256으로 9-Slice 확장, 네 변에 24픽셀 여백 |

직접 다운로드는 [Kenney Emotes ZIP](https://kenney.nl/media/pages/assets/emotes-pack/d00a3dcb06-1677578798/kenney_emotes-pack.zip), [Kenney Sci-Fi ZIP](https://kenney.nl/media/pages/assets/ui-pack-sci-fi/b67c2acd31-1724181109/kenney_ui-pack-space-expansion.zip), [Crossed swords PNG](https://game-icons.net/icons/ffffff/transparent/1x1/lorc/crossed-swords.png), [Padlock PNG](https://game-icons.net/icons/ffffff/transparent/1x1/lorc/padlock.png)에서 제공됩니다. 프로젝트에 넣은 Kenney 원본은 제작자가 OpenGameArt에 올린 [Emotes ZIP](https://opengameart.org/sites/default/files/kenney_emotesPack.zip)과 [Sci-Fi ZIP](https://opengameart.org/sites/default/files/kenney_ui-pack-scifi.zip)에서 추출했습니다. Game-icons.net의 [CC BY 3.0 사용 조건](https://creativecommons.org/licenses/by/3.0/)을 함께 보존합니다.

폰트 원본은 `Assets/Fonts/Resources/StoryFonts/`에 있습니다. [Pretendard v1.3.9 ZIP](https://github.com/orioncactus/pretendard/releases/download/v1.3.9/Pretendard-1.3.9.zip)의 `public/static/alternative/Pretendard-Medium.ttf`, `Pretendard-SemiBold.ttf`와 [Paperlogy 1.001 ZIP](https://github.com/Freesentation/paperlogy/raw/refs/heads/main/Paperlogy-1.001.zip)의 `Paperlogy-7Bold.ttf`입니다. SIL OFL 1.1 원문을 `Assets/Fonts/`에 넣었습니다. RPG 대화 본문과 로그에는 Pretendard Medium을, 화자 이름과 제목에는 Pretendard SemiBold를 사용합니다. 동일한 TTF로 TMP 텍스트를 만들 때는 Unity의 **Window → TextMeshPro → Font Asset Creator**에서 TTF를 고르고 한글 입력이 필요한 경우 Atlas Population Mode를 Dynamic으로 설정해 저장하면 됩니다.

## 새 필드 이미지

`5DS/WalkCycles/sd_*_side_idle.png`는 프로젝트의 기존 SD 원화를 참고해 새로 생성한 40명의 우측 대기 포즈입니다. PNG는 투명 배경으로 정리했습니다. 기존 방향별 시트가 있는 인물은 해당 시트의 옆모습을 사용합니다.

OpenAI 내장 이미지 생성 도구로 여섯 장을 각각 생성한 뒤 1920×1080 PNG로 정리했습니다. 공통 프롬프트는 고정된 3/4 시점의 2.5D 사이버펑크 애니메이션 RPG 배경, 하단 약 46%의 연속된 보행 공간, 인물·텍스트·UI 제외입니다.

| 파일명 | 장면 프롬프트 |
| --- | --- |
| `spot_kai_garage.png` | 노을이 들어오는 D-휠 정비소. 뒤쪽의 오토바이, 푸른 진단 모니터와 부품, 넓은 콘크리트 바닥 |
| `spot_facility_prison.png` | 강철 벽과 닫힌 보안문, 먼 쪽의 붉은 레이저 게이트, 넓은 금속 바닥 |
| `spot_fortune_stadium.png` | 밤의 빈 관중석, 시안과 골드 스포트라이트, 경기장 바닥 |
| `spot_old_moment.png` | 지하 발전로 폐허, 보라색 기하학 문양의 제단과 균열이 있는 석재 바닥 |
| `spot_wrgp_circuit.png` | 해질녘 고가 서킷과 피트 레인, 도시와 홀로그램 가드레일, 아스팔트 바닥 |
| `spot_ark_cradle.png` | 구름과 금빛 오로라 위의 백금빛 요새, 큰 톱니바퀴와 금속·유리 통로 |

## 11개 지역과 대화 에셋

- `5DS/Maps/spot_duel_academia*.png`와 `spot_crash_town*.png`는 OpenAI 내장 이미지 생성 도구로 제작했습니다. 16:9의 2.5D 시점에서 교정은 맑은 아침의 옅은 석재와 유리 건물, 크래시 타운은 푸른 하늘 아래 서부 광산 마을을 요청했습니다. 두 이미지 모두 하단을 보행 공간으로 비웠습니다.
- `5DS/Characters_Standing/full_*_{smile,blush,sad,determined,shock,hurt}.png`는 프로젝트 스탠딩을 참고해 제작한 대화 표정입니다. 같은 인물의 얼굴과 구도, 시선, 허리 높이를 맞추고 실제 투명 배경으로 저장했습니다. 아키는 일상 대화를 위해 크림 블라우스와 버건디 재킷을 입은 포즈를 사용합니다.
- `5DS/WalkCycles/sd_*_field.png`의 추가 시트는 기존 프로젝트 SD를 참고했습니다. 위부터 옆·앞·뒤, 각 행은 대기·왼발·오른발을 배치한 투명 3×3 시트입니다.
- `5DS/Cutscenes/cg_dark_signer_altar.jpg`, `cg_aki_garage_training.jpg`, `cg_wrgp_riding_clash.jpg`, `cg_ark_cradle_final.jpg`는 각각 보라색 제단, 아침 정비소의 실습, 서킷의 라이딩 대결, 공중 요새의 마지막 장면을 새 구도로 제작했습니다.
- `icon_shop.png`, `icon_treasure.png`, `icon_talk.png`는 프로젝트용 카드, 반짝임, 대화 아이콘을 128×128 투명 PNG로 직접 그린 파일입니다.

## 장면별 의상

프로젝트 스탠딩과 SD 원화를 참고해 OpenAI 내장 이미지 생성 도구로 제작했습니다. 투명 상반신은 미소, 홍조, 슬픔, 결의, 놀람, 아픔 6종으로 만들었습니다. 같은 옷의 SD는 옆·앞·뒤 대기와 왼발·오른발 보행을 담은 투명 3×3 시트입니다.

| 인물과 장면 | 사용한 의상 프롬프트 | 파일명 |
| --- | --- | --- |
| 아키 · 아카데미아 | 짙은 남색 블레이저, 흰 셔츠, 와인색 리본, 회색 주름치마 | `full_1_school_*`, `sd_1_school_field.png` |
| 아키 · 일상 | 크림색 블라우스, 버건디 조끼와 리본, 버건디 치마 | 기존 `full_1_*`, `sd_1_everyday_field.png` |
| 아키 · 정비소 훈련 | 크림색 몸판과 와인색 소매가 있는 정비 재킷, 어두운 작업 바지 | `full_1_workshop_*`, `sd_1_workshop_field.png` |
| 아키 · 결전 | 버건디와 검정의 결전복, 크림색 퍼프 소매, 녹색 펜던트 | `full_1_duel_*`, 기존 `sd_1_field.png` |
| 아키 · WRGP | 아이보리 어깨 보호대, 적색 몸판, 차콜 보호대와 승차용 바지 | `full_1_riding_*`, `sd_1_riding_field.png` |
| 칼리 · 시티 취재 | 짙은 청록 트렌치코트, 복숭아색 스카프, 작은 카메라 가방 | `full_220_press_*`, `sd_220_press_field.png` |
| 셰리 · 시내 만남 | 남색 롱 블레이저, 엷은 세이지색 스카프, 아이보리 블라우스 | `full_32_city_*`, `sd_32_city_field.png` |
| 칼리 · 다크 시그너 | 기존 검정과 보라색 의상의 앞·옆·뒤 걸음 | `sd_221_field.png` |

아키의 교복·일상복·훈련복·라이딩복은 동일한 내장 이미지 생성 도구에서 머리 장식만 편집했습니다. 머리핀을 빼고 머리 위의 두 뾰족한 부분을 붉은 앞머리 결에 이어지게 다듬었습니다. 이 설정을 해당 상반신 표정과 SD 보행 시트, 정비소 노을과 다이달로스 브리지 엔딩 CG에도 적용했습니다. 결전복은 장식이 있는 모습을 사용합니다.

아키의 다섯 가지 의상에서 홍조 표정 PNG의 볼 부분을 같은 도구로 보정했습니다. 기존 선과 눈매를 기준으로 붉은 빗금을 지우고 주변 피부색에 스며드는 옅은 색을 요청했습니다. 볼에 해당하는 보정만 원본에 반영했습니다.
