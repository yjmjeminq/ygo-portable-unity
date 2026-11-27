# GX UI 검수

## 수정 전후

| 화면 | 캡처에서 확인한 변경 |
| --- | --- |
| 메인 메뉴 | GX 진입 버튼을 강조하고 섬 미리보기와 카드 상점의 위치를 나눴다. |
| GX 필드 | 장소와 목표를 한곳에 모으고 미니맵과 소지 정보를 줄였다. 본관의 지역 안내와 줍기 버튼 사이 간격을 늘렸다. |
| NPC 대화 | 이름표에 캐릭터 색상을 적용하고 본문을 키웠다. 대화가 열려도 화면 크기와 카메라 구도가 유지된다. |
| 스토리 대화 | 대화 도구와 시간 자막의 위치를 분리하고 반투명 대화창을 적용했다. 오토 기본값과 CG 감상 모드는 기존 흐름을 유지한다. |
| 월드맵 | 잠김, 현재 위치, 사건 표식을 구분하고 지역 정보와 이동 버튼에 공통 스킨을 적용했다. |

전후 비교는 `qa/screenshots/gx-before-after.jpg`, 가로형 노치 캡처는 `qa/screenshots/gx-release-touch/04-mobile-safe-area.png`, 보행 연속 프레임은 `qa/recordings/gx-harbor-walk.jpg`에 저장했다.

## 검증 범위

- Unity 백그라운드 렌더에서 16:9와 19.5:9 화면을 확인했다. GX 53시퀀스, 승리 경로 34개, 패배와 재도전 경로 34개, 입력을 잠그는 컷신 19개, 12개 필드 및 시점 전환 3개를 자동 검증했다.
- 항구에서 60Hz 간격으로 입력을 시뮬레이션했다. 보행 중 프레임 변화, 보행 경계, 이동을 멈춘 뒤 방향 유지가 통과했다.
- 에디터에서 가로형 노치 여백, 가상 스틱의 입력 문턱, 다른 손가락 입력 격리, 이동과 손을 뗀 뒤 정지를 확인했다.
- 캠페인 무결성 검사에서 53시퀀스, 필드 12개, 덱 37개와 CG 39개를 확인했다. 고정 듀얼 회귀는 8건 통과했다.
- 기존 AI와 캠페인 데이터를 사용하는 별도 Git 체크아웃에서 `VerifyAllHeadless`, `TestStoryCampaign.Run`, `TestDuelAiTactics.RunHeadless`, `TestStoryTactics.Run`을 통과했다. 초기 전체 검사에서 실패한 링크 전개 두 건은 최종 전체 재실행에서 통과했다.
- 실제 iPhone의 60FPS, 프레임별 메모리와 발열은 측정하지 않았다.

## 아카데미아 자유시간

시범 경기와 아스카의 초반 에피소드를 마치면 필드의 시간 표시와 메뉴에서 일정을 열 수 있다. 일정에서 아침과 방과 후, 저녁을 바꾸고 저녁에는 오시리스 레드 기숙사 앞에서 다음 날까지 쉴 수 있다. 시간 변경은 메인 사건 진행도를 바꾸거나 획득한 일회성 아이템을 다시 생성하지 않는다.

본관 매점에서 멜론빵을 사면 DP와 소지 수량이 함께 저장된다. 아스카와 쥬다이의 대화와 선물은 친밀도에 반영된다. 이미 선물한 날에는 빵을 소모하지 않으며, 휴식 뒤 저장을 다시 열어도 지난 선물에 대한 대사가 나온다.

GX 자동 검증에 일정 변경과 NPC 이동, 구매와 중복 선물 방지, 기숙사 휴식, 저장 재로드, 이전 태그 포스 일정 데이터, 화면 전환 후 주민 불투명도 검사를 추가했다. 16:9과 19.5:9 화면을 숨김 모드에서 렌더링했다. 최신 시각 자료는 `qa/screenshots/gx-campus-release/`에 있다.

새 에셋은 내장 이미지 생성으로 제작했다. 멜론빵은 RGBA 투명 이미지를 356px로 줄였다. 저녁 기숙사에 기존 보행 구역과 카메라를 적용하고 원본 낮 배경도 사용한다.

- `Assets/StreamingAssets/TagForce/GX/Items/campus_melon_bread.png` 프롬프트
  > Create an original inventory sprite for a polished Korean illustrated academy RPG. One fresh round Japanese melon-pan bun, golden honey-brown domed crust with the distinctive shallow diamond cookie scoring, a soft pale base, lightly dusted fine sugar, viewed from a slightly elevated three-quarter angle. Painterly cel-rendered food illustration with clean soft edges and subtle warm highlights, strong readable silhouette at 100 px. Center the entire single bun with a small clear margin. Genuinely transparent alpha background. No plate, no wrapper, no backdrop, no lettering, no branding, no floating particles.
- `Assets/StreamingAssets/TagForce/GX/Maps/spot_red_dorm_evening.png` 프롬프트
  > Lighting and time-of-day edit ONLY for the supplied original 2.5D RPG map. The source is the exact required playable geometry. Preserve the full 16:9 framing, camera, line art, coordinate layout, silhouettes, and pixel positions of every porch beam, step, deck rail, shoreline, tree, courtyard paving and red circular crest. No zoom, crop, camera movement, layout changes, people or new objects. Render the exact scene as coastal blue hour shortly after sundown. Dim amber light should glow from the existing windows and the two existing porch lamps, gently reflecting near the front door. Change the sky and sea to deep blue with a faint soft warm strip on the distant horizon. Keep courtyard walkable surfaces and step edges readable, and preserve the cel-illustrated rendering and details.
