# YGO Portable Unity (오프라인 전용 유희왕 포터블 프로젝트)

> **2026년 11월 최신 메타 카드 풀(천배룡, 라이제올, 데먼스미스 등) + LOTD 스타일 UI + 스마트 AI(WindBot) 탑재**  
> 불필요한 온라인 대전을 완전히 제거하고, **[덱 편집기]**와 **[오프라인 AI 대전]** 딱 2가지 핵심에 집중한 초경량 iOS & Nintendo Switch 포터블 유희왕 프로젝트입니다.

---

## 🎯 프로젝트 특징
1. **100% 완전 오프라인 구동**
   - 네트워크 연결/서버 불필요 (비행기, 지하철 등 어디서나 플레이 가능)
   - 서버 유지비 0원, 렉 및 접속 끊김 제로
2. **LOTD 1:1 스타일 UI**
   - 3분할 덱 편집기 (카드 상세 정보 / 중앙 보관함 / 내 덱 그리드)
   - 쿼터뷰 3D 필드 매트 및 LOTD 페이즈/LP 인터페이스
3. **2026년 11월 최신 카드 풀 완벽 지원**
   - 천배룡, 라이제올, 데먼스미스, 스네이크아이, 유벨, 크샤트리라 등 4,172종 신규 카드
   - 모든 카드의 체인/특수소환/서치 효과 100% 정상 발동
4. **고지능 스마트 AI (WindBot 연동)**
   - 멍청한 구형 AI 대신 정석 전개 콤보와 패트랩 타이밍을 아는 AI 탑재
   - 20인의 시리즈별 캐릭터 맞춤형 대전 모드
5. **멀티플랫폼 지원**
   - **iOS**: iPhone / iPad 풀 터치 조작 (AltStore / Sideloadly / Xcode)
   - **Nintendo Switch**: Atmosphere CFW용 NSP 패키지
   - **PC**: Windows Standalone

---

## 📂 폴더 구조
```
ygo-portable-unity/
├── Assets/
│   ├── Data/
│   │   ├── Cards/       # 2026 최신 카드 데이터베이스 (한글/일러스트)
│   │   └── Decks/       # characters.json, decks.json (20종 메타 덱 레시피)
│   ├── Plugins/         # iOS/Switch용 네이티브 C++ 듀얼 엔진 라이브러리
│   └── Scripts/
│       ├── Core/        # 듀얼 코어 P/Invoke 바인딩 및 게임 루프
│       ├── UI/          # LOTD 스타일 덱 에디터 및 듀얼 필드 UI
│       └── AI/          # WindBot 기반 C# 오프라인 인공지능
├── Docs/                # 설계 문서 및 빌드 가이드
├── .gitignore           # Unity 전용 Git 관리 파일
└── README.md
```

---

## 🔄 회사와 집 동기화 (Git 워크플로우)

### 🏢 회사(현재 PC)에서 작업 후 집으로 올릴 때
```bash
git add .
git commit -m "작업 내용 요약"
git push origin main
```

### 🏠 집 PC에서 처음 시작할 때 (최초 1회)
```bash
git clone https://github.com/yjmjeminq/ygo-portable-unity.git
cd ygo-portable-unity
```

### 🏠 집 PC에서 작업 시작 전 / 작업 후
```bash
# 작업 시작 전 (최신 변경사항 당겨오기)
git pull origin main

# 작업 완료 후 (회사로 변경사항 올리기)
git add .
git commit -m "집에서 작업한 내용"
git push origin main
```

### 🏢 회사 출근 시
```bash
git pull origin main
```
