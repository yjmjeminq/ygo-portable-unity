# 듀얼 AI 아침 보고서

**목표 미달. 연속 다섯 후보 탈락으로 이번 검증을 종료했습니다. 현재 배포 AI는 `355e905`이며 새 채택 태그는 없습니다.** 2026년 11월 20일 16:24 KST에 갱신된 작업에서 전술 후보 세 개와 재학습 후보 두 개를 검증했습니다. 처음에는 `Logs/` 폴더 전체에 5GB 상한을 잘못 적용했습니다. 대전 원본 로그만 다시 집계해 재개했으며, 마지막 선별 시작 시 약 0.93GB였습니다. 원본은 보존했습니다.

## 단계별 진행

아래 원본 경로는 저장소 루트 기준입니다. 기존 시험 수치는 새 목표로 바뀌기 전에 얻은 참고 자료입니다.

| 단계 | 한 일 | 이전·이후 수치 | 판정과 원본 |
|---|---|---|---|
| 0. 기존 기준선 | `Baseline-v1` 커밋 `1da3213`, 기존 자동 집계, 리플레이 옵션과 상대 풀을 점검 | 기존 랜덤 상대 131승 1무/132판, 순승률 99.24%. 기존 배포판 고정 회귀 4/6, 현사·커넥터 미해결. 두 기록 모두 Retry/ScriptErrors 0 | 기준선 보존. 새 미러 상대 풀 전수 검증 전. [랜덤 대전](Logs/AiBenchmark/RandomLegalFloor/random-legal-floor-dae1492/summary.json) · [고정 회귀](Logs/AiBenchmark/FixedRegressions/deployed-ai-after-screen/summary.json) |
| 0. 미러와 단독 시간 | 고정 평가기를 변경 없이 호출하는 별도 도구 추가. 미러 두 덱·시드 3개에서 시작 패가 좌석 교대 전후 동일함을 검사. 선택용·홀드아웃을 덱 내용과 시드 양쪽에서 분리 | Baseline-v1 상대 2워커 9승 3패/12판. 별도 1워커 반복 7승 5패/12판. 단독 일반 평균 0.056ms, 최대 21.66ms, 전개 계획 최대 214.73ms. 두 시험 Retry/ScriptErrors 0 | 통신·입력·시간 측정 스모크 통과. 표본 부족. 홀드아웃 미사용. [미러 대전](Logs/AiBenchmark/MirrorStage0/verified-selection-smoke/summary.json) · [단독 시간](Logs/AiBenchmark/MirrorStage0/verified-standalone-timing/summary.json) |
| 0. 미러 상대 풀 | 고정 평가기와 두 AI의 정책을 분리. 동일한 배포 AI를 두 버전으로 넣고 14덱·시드 4개·양쪽 좌석으로 다섯 비교 실행 | 직접 대전 55승 57패/112판(p=.8125). 두 버전의 Baseline-v1 상대 79/112승(70.54%)과 75/112승(66.96%), 평균 종료 보드 2.996과 3.074. 랜덤 상대는 각각 112전 전승. 총 560판 Retry/ScriptErrors 0 | 입력과 대비 경로 확인. 동일 코드 대조이며 채택·승격 없음. [미러 상대 풀](Logs/AiBenchmark/MirrorStage0/stage0-league-112/summary.json) |
| 0. 단독 전 구간 시간 | 같은 14덱·시드·평가기로 한 프로세스에서 Baseline-v1과 112판 | 일반 평균 0.074ms, 최대 56.41ms, 전개 계획 최대 222.90ms. Retry/ScriptErrors 0 | 이 표본에서 100/300/1000ms 기준 통과. 병렬 결과와 시간 수치를 합치지 않음. [단독 시간](Logs/AiBenchmark/MirrorStage0/stage0-timing-112/summary.json) |
| 1. 새 미러 진단 | Baseline-v1 상대 112판에서 판단 유형을 분류. 히어로와 너스 번의 종료 결정을 네이티브 리플레이로 확인 | 초반 목표 전개 실패 46판. 목표 소환을 선택할 수 있던 1판과 그 선택이 관측되지 않은 45판. 아래 반복 패턴 5가지 참조 | 중복 가능한 관측이며 패배 원인 단정은 보류. [미러 원본](Logs/AiBenchmark/MirrorStage0/stage0-league-112/baseline/summary.json) |
| 1. 종료·발동 대조 | 히어로·의식·너스 번·서브테러 네 장면에서 같은 네이티브 응답까지 진행한 뒤 종료와 발동을 끝까지 비교 | 앞의 세 덱은 발동해도 패배. 서브테러는 강제 발동 갈래에서 승리했으나 새 AI가 스스로 같은 효과를 쓴 별도 재생에서는 T9 패배. Retry/ScriptErrors 0 | 선택지가 있다는 사실만으로 실책을 판단하기 어려움. [네 장면 대조](Logs/AiBenchmark/MirrorStage0/stage1-end-or-follow-up/unity.log) · [자동 선택](Logs/AiBenchmark/MirrorStage0/stage2-protective-flip-auto/unity.log) |
| 2. 수비 겸 리버스 준비 | 검증된 도사의 효과로 자신을 뒤집어 다음 리버스를 준비하고, 공개된 공격 몬스터를 동시에 눕히는 후보 | Baseline-v1 상대 기존 71/112승→후보 76/112승(63.39→67.86%), 종료 보드 3.022→3.175. 직접 기존판 상대 **54승 58패**, 단측 p=.890625. 랜덤 상대 양쪽 110/112승(98.21%). 총 560판 Retry/ScriptErrors 0 | 직접 승률 기준 미달로 탈락. 방법·성향별 모두 표본 부족. 후보는 작업용 커밋으로만 보존. [미러 선별](Logs/AiBenchmark/MirrorStage0/protective-flip-screen-112/summary.json) |
| 2. 상대 필드로 넘길 몬스터 | 너스 번에서 상대에게 핵심 몬스터를 넘기던 선택을 수정. 같은 추방 발동 이후 후보가 유코백을 자동 선택 | 재생 T8 패배→T10 승리. 직접 기존판 상대 **55승 57패/112판**, p=.7734375. Baseline-v1 상대 78→72/112승, 종료 보드 3.007→3.038. 랜덤 상대 112→111/112승. 560판 Retry/ScriptErrors 0 | 직접 승률과 공통 상대 성적이 떨어져 탈락. 전체 대전에서 기존판과 후보 모두 이 장면의 최초 추방 발동을 건너뜀. [자동 표적](Logs/AiBenchmark/MirrorStage0/stage2-opponent-summon-auto/unity.log#L1156) · [미러 선별](Logs/AiBenchmark/MirrorStage0/opponent-summon-screen-112/summary.json) |
| 2. 구조 | 공개 필드 제거 수에 탐색 시간을 배정한 직전 후보 | Baseline-v1 상대 **82/132승 → 79/132승**(62.12% → 59.85%), 평균 종료 보드 2.883 → 2.808, 단측 p=.82038. 일반 평균/최대 0.064/54.63 → 0.074/44.59ms, 계획 최대 243.05 → 233.12ms. Retry/ScriptErrors 0 | 탈락·원복. 성향·소환법 수치는 모두 표본 부족. 시간은 병렬 구동 중 참고값. [전후 비교](Logs/AiBenchmark/StableV1Screen/paired-recovery-removal-screen/summary.json) |
| 3. 학습 데이터 점검 | 실제 프리셋 1,380개와 승격용 덱 내용 14종을 대조 | 일반 학습 48개 중 10개, 일반 내부 시험 27개 중 3개, 링크 학습 12개 중 1개, 링크 상대 8개 중 3개가 승격용과 겹침. 점검에서 듀얼 0판. 비승격용 자원형 프리셋은 섬도희 `20687` | 새 학습에서 승격용 덱 내용과 시드를 제외. [데이터 점검](Logs/AiBenchmark/MirrorStage0/promotion-holdout-training-preflight/unity.log#L328) |
| 3. 일반 AI 재학습 | 내 턴 종료와 상대 턴 이후를 함께 보상. 승격용 14덱과 네이티브 시드 2,912개를 제외한 별도 워커에서 학습 1,140판, 내부 검증 768판 | 내부 검증 128판에서 기존 64승→후보 68승, 보상 기준의 평균 필드 안정성 0.2111→0.2259. 전개 29→32/62, 컨트롤 12→14/28, 락 23→22/38, 자원형 표본 0. Retry/ScriptErrors 0 | 학습 워커의 기존 콤보 수읽기를 비활성화. 기존 체인 가중치를 유지하고 메인 페이즈 선택 가중치만 시험. [학습 원본](Logs/AiBenchmark/MirrorStage0/stable-turn-learning/unity.log#L329) |
| 3. 일반 학습 가중치 검증 | 고정 덱 14개와 같은 초패에서 기존 정책과 후보를 교대 | 직접 **57승 55패/112판**(50.89%, p=.5), 보드 3.083→3.237. Baseline-v1 상대 75→73/112승, 보드 3.108→3.165. 랜덤 상대 112→111/112승. 총 560판 Retry/ScriptErrors 0 | 상대 풀 성적 저하 및 직접 대전 자멸 증가로 탈락. 단독 시간과 400판 미검증. [학습 후보 선별](Logs/AiBenchmark/MirrorStage0/stable-turn-learning-screen-112/summary.json) |
| 3. 링크 소재 보존 재학습 | 더 높은 링크를 낮은 링크 소환에 실제로 소비한 수를 보상에 반영. 승격용 데이터는 제외 | 학습 비교 88판에서 46→47승, 상위 링크 소비 24→20장. 독립 내부 검증 96판에서 31→32승, 소비 21장 동일, 종료 보드 1.933→1.924. 링크 4 이상 도달 선공 3회·후공 4회 동일. 총 808판 Retry/ScriptErrors 0 | 독립 검증에서 비용 절감이 재현되지 않고 종료 보드가 떨어져 탈락. [링크 학습 원본](Logs/AiBenchmark/MirrorStage0/link-downgrade-learning/unity.log#L332) |
| 2. 메인 1 계약서 유지비 | 실제 패배 3장면과 여유 LP 대조 1장면에서 동일 응답을 재생하고 유지비 기준을 두 메인 페이즈에 적용 | LP 2,000에서 두 번째 계약서를 보류해 T13 메인에 LP 600으로 진입. T14 패배. 나머지 두 사례는 예방 이후에도 각각 배틀과 기존 계약서로 패배. LP 3,200에 예정 비용 총 2,000인 대조는 발동 유지. 8갈래 Retry/ScriptErrors 0 | 한 재현에서 유지비를 견뎌 다음 메인에 도달. [기존·후보 자동 비교](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-candidate/unity.log#L332) |
| 4. 유지비 후보 선별 | 동일 초패·좌석을 교대한 고정 미러 다섯 비교 | 직접 **55승 57패/112판**(49.11%, p=.74609), 종료 보드 3.111→3.278. D/D 직접 5승 3패, 자멸 기존판 3건·후보 1건. Baseline-v1 상대 77/112승 유지, 종료 보드 3.014→3.068. 랜덤 양쪽 112전 전승. 총 560판 Retry/ScriptErrors 0 | 직접 비교와 실책·일부 소환법 기준 미달로 탈락. [마지막 선별](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-screen-112/summary.json) |
| 4. 확정 | 연속 5개 후보가 채택 기준 미달로 종료 | 후보 400판·성향별/소환법별 400판·단독 시간은 검증 안 됨. 승격용 홀드아웃은 미사용 | 새 코드·정책의 채택 및 승격 없음 |

기준 AI의 반복 패턴 상위 다섯 가지는 목표 전개 실패 46판, 패배한 14판에서 전개 선택지를 남긴 종료 34회, 약한 공격 표시 노출 15회, 링크 하락 4회, 자기 필드 순손실 3회였습니다. 공격 표시 노출 중 보상 없는 피격은 6회였습니다. 컨트롤형은 16판에서 8승과 초기 목표 9회를 기록했으며, 서브테러의 수비·리버스 장면을 별도로 검증했습니다. [고정 112판 원본](Logs/AiBenchmark/MirrorStage0/stage0-league-112/baseline/summary.json) · [서브테러 재현](Logs/AiBenchmark/MirrorStage0/stage2-protective-flip-auto/unity.log#L787)

새 미러의 성향별 Baseline-v1 상대 승수는 전개 55/72, 컨트롤 8/16, 락 11/16, 자원 5/8입니다. 1~2턴 소환법별 성공은 융합 1/16, 의식 3/8, 싱크로 8/16, 엑시즈 14/16, 펜듈럼 4/8, 링크 15/16입니다. 모든 구간은 표본 부족입니다. 새 미러의 100판당 자멸·킬각 누락·계측된 견제 낭비는 각각 0, 불필요한 피격 5.36, 자원 남긴 패배 종료 30.36, 링크 하락 3.57입니다.

도사 후보 선별에서 Baseline-v1 상대 성향 승수는 전개 46→50/72, 컨트롤 10/16 동일, 락 11/16 동일, 자원 4→5/8이었습니다. 소환법별 전개 수는 같았습니다. 100판당 자멸 0.89→0, 보상 없는 피격 6.25→4.46, 자원 남긴 패배 종료 52.68→35.71, 링크 하락 4.46→3.57, 킬각 누락·계측된 견제 낭비는 0이었습니다. 이 후보의 단독 시간 및 400판 확인은 선별 탈락으로 검증하지 않았습니다.

열락의 타락천사 후보의 Baseline-v1 상대 승수는 전개 52→48/72, 컨트롤 8→9/16, 락 11→11/16, 자원 7→4/8이었습니다. 1~2턴 소환법별 전개는 융합 1/16, 의식 3/8, 싱크로 8/16, 엑시즈 14/16, 펜듈럼 4/8, 링크 14/16으로 같았습니다. 100판당 자멸 0→0.89, 보상 없는 피격 4.46→5.36, 자원 남긴 패배 종료 36.61→41.07, 링크 하락 3.57→4.46, 킬각 누락과 계측된 견제 낭비는 양쪽 모두 0이었습니다. 전 구간 표본이 부족하고 후보의 단독 시간은 검증 안 됐습니다.

일반 학습 후보의 Baseline-v1 상대 성향 승수는 전개 50→50/72, 컨트롤 9→7/16, 락 10→11/16, 자원 6→5/8이었습니다. 1~2턴 소환법 전개는 융합 4→1/16, 의식 3→3/8, 싱크로 7→8/16, 엑시즈 14→13/16, 펜듈럼 3→4/8, 링크 15→15/16이었습니다. 100판당 실책은 Baseline-v1 상대에서 보상 없는 피격 5.36→4.46, 자원 남긴 패배 종료 37.50→50.89, 링크 하락 4.46→3.57이었고 나머지 세 항목은 0이었습니다. 기존판과 직접 대전에서 자멸은 1→4건이었습니다. 성향·소환법 구간 모두 표본 부족입니다. [기존 대진](Logs/AiBenchmark/MirrorStage0/stable-turn-learning-screen-112/previous-baseline/summary.json) · [후보 대진](Logs/AiBenchmark/MirrorStage0/stable-turn-learning-screen-112/baseline/summary.json)

마지막 유지비 후보의 Baseline-v1 상대 승수는 전개 51→50/72, 컨트롤 9→10/16, 락 11→11/16, 자원 6→6/8이었습니다. 1~2턴 소환은 융합 3→1/16, 의식 3→3/8, 싱크로 7→8/16, 엑시즈 14→14/16, 펜듈럼 4→4/8, 링크 15→15/16이었습니다. 100판당 불필요 피격은 5.36 유지, 자원 남긴 패배 종료는 36.61→41.96, 링크 하락은 4.46→2.68입니다. 자멸·킬각 누락·계측된 견제 낭비는 모두 0이었습니다. 성향·소환법 구간 모두 표본 부족입니다. [기존판 대진](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-screen-112/previous-baseline/summary.json) · [후보 대진](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-screen-112/baseline/summary.json)

직전 후보의 1~2턴 전개 성공은 전개형 55/93→57/93, 컨트롤 9/9, 락 7/21, 자원형 5/9였습니다. 소환법별 성공은 융합 15/27→16/27, 엑시즈 11/18→12/18, 의식 0/6, 싱크로 7/15, 펜듈럼 7/12, 링크 17/24였습니다. 모두 표본이 400판보다 적습니다.

100판당 자멸 0.76→0, 킬각 누락 0→0, 불필요한 피격 0→1.52, 계측된 견제 낭비 0→0, 자원 남긴 패배 종료 43.18→45.45, 하락 소환 2.27→1.52였습니다. 빈 체인의 실제 표적을 별도로 재생한 감사에서는 기존판과 Baseline-v1 각각 2건의 견제 낭비를 확인했습니다. 보호된 자동 집계 코드는 수정 없이 보존했습니다. [선제 견제 감사](Logs/AiBenchmark/native-prechain-negation-audit/unity.log)

## 채택·보류

- **채택 없음.** AI 소스와 두 정책은 `355e905` 상태입니다. 보호된 덱, 실책 집계, 고정 회귀, `TestNewDeckLevels.cs`도 보존했습니다.
- **제거 우선 비교, 세트 재료를 통한 특수소환, 방해 뒤 검색 복구**는 각각 장면 개선이 있었으나 전체 승률·보드 또는 실책 기준을 통과하지 못해 원복했습니다.
- 네이티브 브리지에는 프로세스 전역 카드·스크립트·로그 콜백이 있습니다. 별도 프로젝트·저장 경로를 갖는 Unity 배치 프로세스끼리만 병렬로 실행했습니다. 같은 프로세스의 동시 스레드 실행은 검증 안 됐습니다. 스크립트 데이터와 정책은 고정본을 재사용했습니다.
- 현재 브리지에는 듀얼 상태 복제 API가 없습니다. 기존 리플레이 경로는 초기 시드를 재사용하므로 새 기준의 숨은 정보 재배치 검증을 추가로 해야 합니다. 라이브 게임과 배포 빌드는 실행하지 않았습니다.
- 사용자 작성 장면이 없어 자동 진단과 기존 고정 리플레이를 사용했습니다. 고정 6건 중 코요테 공격 순서, D/D 메인 2 계약서, 너스 번 킬각, 제왕 릴리스는 통과했습니다. 현사 피해 방지와 네오 스페이스 커넥터 수비는 미해결입니다. [배포판 고정 회귀](Logs/AiBenchmark/FixedRegressions/deployed-ai-after-screen/summary.json)

## 전개가 끊긴 뒤의 판단 구조

현재 플레이 화면은 상대 덱 레벨과 별도로 `Smart` AI를 생성합니다. 이미 방해 후 계획을 다시 계산하는 경로가 있지만, 네오 스페이스 커넥터 고정 장면에서는 수비로 이어질 효과를 건너뛰고 턴을 끝냅니다. 적용 목표는 전 레벨 공통 판단입니다. 방해를 확인하면 기존 계획을 폐기하고 실제 합법 선택지를 새로 읽습니다. 재개, 기존 필드 유지, 수비 전환을 비교하고 상대가 공개한 공격 수단을 견딜 수 있는지 확인합니다. 확정 킬을 먼저 처리한 뒤, 후속 수단이 없는 하급 소환과 보상 없는 상위 링크 소재 소비를 줄입니다. 컨트롤은 남은 견제, 자원형은 필요한 빈 몬스터 존, 락은 핵심 카드와 생존을 함께 평가합니다. [현재 AI 생성](Assets/Scripts/UI/GameAppController.cs#L359) · [현재 방해 후 분기](Assets/Scripts/AI/TacticalAI.cs#L340) · [커넥터 고정 시험](Logs/AiBenchmark/FixedRegressions/deployed-ai-after-screen/summary.json)

## 의심 장면

| 덱·상황 | AI의 선택 | 시험한 대안과 결과 | 근거 |
|---|---|---|---|
| 푸른 눈, T12 상대 몬스터 2장과 후열 2장 | 배틀로 상대 LP를 500까지 줄이고 T15 패배 | 아룡 제거로 3장 제거 후 승리. 재실행에서도 제거 갈래 승리 | [응답 200 비교](Logs/AiBenchmark/native-removal-versus-battle/unity.log#L404) |
| 섬도희, T2 빈 필드 | 링케이지만 세트한 뒤 T3 패배 | 삼전의 호 세트 → 링케이지 → 섬도희＝제로. 자동 후보가 T8 승리 | [응답 22 비교](Logs/AiBenchmark/native-resource-terminal-proof/unity.log#L438) |
| 섬도희, T3 메인 2 | 바로 턴 종료 | 카가리로 추가 전개하면 다음 내 턴 LP가 5,800에서 5,000으로 하락. 이 장면은 종료가 유리했음 | [응답 38 비교](Logs/AiBenchmark/native-resource-set-and-zone-proof/unity.log#L719) |
| 서브테러, T1 도사 2장 보유 | 우라라 일반 소환 후 알미라지 | 도사 세트는 리버스와 우라라를 살렸지만 T4 LP는 6,500→5,000. 종합 우위 보류 | [선공 대조](Logs/AiBenchmark/native-control-lanes-probe/unity.log#L545) |
| T1 킬러 토마토·UFO 터틀 일반 소환 직후 | 손에서 무한포영 사용 | 발동을 넘기면 다음 자기 턴 시작 패 5→6. 두 표적의 효과는 전투 파괴 뒤 묘지에서 발동 | [실제 표적 4건](Logs/AiBenchmark/native-prechain-negation-audit/unity.log#L910) |
| 고정 회귀의 현사와 네오 스페이스 커넥터 | 뒤따를 전개 없이 약한 몬스터가 노출됨 | 후보에서 수비 세트·수비 특수소환으로 회복했으나 전체 탈락. 배포판은 미해결 | [회귀 결과](Logs/AiBenchmark/FixedRegressions/deployed-ai-after-screen/summary.json) |
| 히어로 미러, T2 메인 2, 자기 LP 1,000 | 필드에 에어맨 2장과 볼틱 썬더를 남긴 채 종료. T3 패배 | 엔진이 미러클 퓨전을 제시. 묘지 소재로 수비를 보강할 이득이 있었는지 후속 비교 필요 | [응답 44 리플레이](Logs/AiBenchmark/MirrorAudit/mirror-hero-end/unity-0.log#L758) |
| 너스 번 미러, T2 양쪽 LP 8,000 | 종료하면 T15 패배. 추방을 발동시키고 표적을 AI에 맡기면 열락의 타락천사가 상대에게 너스－레피큘을 넘겨 T8 패배 | 추방을 같은 지점에서 수동 발동한 뒤 후보 AI가 유코백을 자동 선택해 T10 승리. T4 자기 LP 1,550→8,950. 전체 선별에서 이 장면의 최초 발동은 두 버전 모두 건너뜀 | [자동 표적](Logs/AiBenchmark/MirrorStage0/stage2-opponent-summon-auto/unity.log#L1156) · [종료](Logs/AiBenchmark/MirrorStage0/stage2-opponent-summon-auto/unity.log#L1251) |
| D/D 미러 `1001/1/0`, `1001/1/1`, `1001/3/1` | 계약서를 메인 1에 활성화. 유지비가 T10 또는 T13에 치명타가 되어 세 판 패배 | 계약서를 보류한 후보는 첫 장면에서 LP 600으로 T13 메인까지 도달. 두 번째는 T9 배틀 패배, 세 번째는 기존 계약서 유지비로 T10 패배. 세 장면 모두 최종 패배 | [12판 네이티브 리플레이](Logs/AiBenchmark/MirrorStage0/self-upkeep-native-audit/unity.log#L598) · [갈래 비교](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-candidate/unity.log#L332) |
| 너스 번 미러 `1012/0/0`, T17 자기 LP 400 | 흑사병을 유지한 뒤 턴 종료. T19 스탠바이에서 흑사병 3,200 피해로 패배 | 먼저 공개된 자기 효과의 다음 피해량을 검토하고 현재 선택지에서 제거·무효·턴 내 승리가 가능한지 확인. 당시 제거·무효의 합법성은 미검증 | [T17 종료](Logs/AiBenchmark/MirrorStage0/self-upkeep-native-audit/unity.log#L411) · [T19 피해](Logs/AiBenchmark/MirrorStage0/self-upkeep-native-audit/unity.log#L422) |
| 서브테러 미러, T6 도사와 공격 표시 사수 | 상대 사수와 자기 도사를 동시에 뒷면으로 바꿀 수 있었으나 턴 종료 | 효과 발동 갈래 한 번은 승리했으나 자동 후보의 재생과 전체 선별에서는 승리가 재현되지 않음 | [공개 필드](Logs/AiBenchmark/MirrorAudit/mirror-guru-reset/unity-0.log#L312) · [후보 자동 선택](Logs/AiBenchmark/MirrorStage0/stage2-protective-flip-auto/unity.log#L787) |

## 아침에 확인할 3가지

1. 미러 `summary.json`의 `league.arms`에서 각 112판, 직접 대전 55승 57패와 랜덤 112전 전승을 확인. 단독 시간 `workers=1`의 최대 계획은 222.90ms입니다.
2. [선택·홀드아웃 일정](Tools/AiEvaluation/mirror-fixtures.json)의 덱과 시드 범위를 확인. 홀드아웃은 승격용입니다.
3. 마지막 후보의 직접 미러 55승 57패와 p=.74609375, 기준선 상대 100판당 자원 남긴 패배 종료 36.61→41.96을 확인합니다. [직접 대전](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-screen-112/direct/summary.json) · [기준선 상대](Logs/AiBenchmark/MirrorStage0/prevent-lethal-upkeep-screen-112/baseline/summary.json)

## 되돌리기

보고서와 0단계 미러 도구만 되돌리려면 해당 커밋을 `git revert`로 취소할 수 있습니다. AI 배포 기준은 커밋 `355e905`, 고정 비교 기준은 `1da3213`입니다. `adopted-*` 태그는 없습니다.
