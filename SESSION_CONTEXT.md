# 세션 컨텍스트 부트스트랩 — Tenebris 프로젝트

다른 Claude Code 세션에서 이 세션과 동일한 작업 환경으로 이어받기 위한 컨텍스트 핸드오프 문서.
새 세션 첫 메시지로 아래 "세션 시작 프롬프트" 섹션을 그대로 붙여넣을 것.

**최종 갱신:** 2026-05-19 (세션 2)

---

## 세션 시작 프롬프트 (복사해서 새 세션에 붙여넣기)

```
이 세션은 Tenebris(테네브리스) 프로젝트 작업을 이어받습니다.
아래 컨텍스트와 진행 상황을 먼저 읽고 작업을 시작하세요.

## 프로젝트 개요

- **프로젝트명:** 테네브리스 (Tenebris)
- **경로:** c:\Users\paek6\Downloads\Buildup\Buildup
- **종류:** Unity 2022 LTS 기반 3D 탑다운 2인 협동 보스전 로그라이트
- **언어:** 한국어로 응답

## 필수 참조 파일 (작업 전 반드시 읽기)

1. **CLAUDE.md** — 프로젝트 작업 지침, skill 파일 참조 규칙, Codex 교차 검증 워크플로우
2. **GAME_DESIGN.md** — 전체 기획 및 기술 설계
3. **Assets/CHANGES.md** — 변경 이력 (작업 전 최신 확인 필수, 작업 후 업데이트 필수)
4. **ML_TRAINING_HANDOFF.md** — 운영↔학습 동기화 사양 (BAL-1, 129ch obs, [5] action)
5. **STATE_MANAGER_DESIGN.md** — 상태 FSM 설계
6. **자동 메모리:** C:\Users\paek6\.claude\projects\c--Users-paek6-Downloads-Buildup-Buildup\memory\MEMORY.md

## 핵심 작업 원칙 (CLAUDE.md 발췌)

- 모든 스탯 변경 → StatManager 경유 (직접 필드 수정 금지)
- 새 스킬 부품 → SkillComponents.cs에만 추가
- 새 전투 객체 → ICombatant 먼저 구현
- SO는 읽기 전용 (런타임 수치 변경 금지)
- UniTask 미설치 → 코루틴 사용, WaitForSeconds 캐싱
- Addressables 미설치 → [SerializeField] private 직접 참조

## Codex 교차 검증 (필수 워크플로우)

모든 기획/설계/코드 변경은 **구현 전 Codex 교차 검증** 필수.
검증 프롬프트를 텍스트 코드블록으로 출력 → 사용자가 외부 Codex에 수동 전달.
**절대 규칙: Codex 승인 없이 코드 수정/도구 실행/파일 생성을 먼저 하지 않는다.**
예외: 단순 오타 수정, CHANGES.md 업데이트, 문서만의 수정.

## 현재 ML 학습 환경 상태 (2026-05-19 세션 2 완료 시점)

### 아키텍처
- **단일 통합 에이전트:** BossInferenceAgent (이전 SpecializedBossAgent 4분리 폐기)
- **이동만 ML 학습**, 스킬은 BossAutoCastHelper가 자동시전 (텔레그래프 포함)
- **BT 플레이어:** P1/P2 독립 BehaviorGraph, 4 archetype (Melee/Ranged/CC/Hybrid)

### Agent 사양 (운영 완전 동기화 완료)
- **Observation:** 129ch (Position 11 + BossState 4 + BossSlots 35 + Touch 2 + PlayerSlots 70 + Extra 7)
- **Action:** 1 branch [5] — idle/forward/turnLeft/turnRight/backward(×0.7)
- **Reward:** Win+1, Loss-1, Timeout-0.3, DmgDealt+0.001, DmgTaken-0.0005, Phase+0.05, Step-0.0001
- **Episode:** 720s, Phase thresholds [0.75, 0.5, 0.25]

### 정규화 상수 (운영 일치 확인 완료)
- _maxDistance=55, _maxCooldown=30, _maxSpeed=16, _maxBurstDmg=80, _maxBossPhase=4, TouchRange=5

### 스탯 (운영 일치 확인 완료)
- Player: HP 150, MoveSpeed 14
- Boss: HP 6000, MoveSpeed 8.4, Rotation 540

### 페이즈 배율 (전부 구현 완료)
| Phase | Speed | Cooldown | Damage | Telegraph Scale |
|-------|-------|----------|--------|-----------------|
| 0 (초기) | 1.0x | 1.0x | 1.0x | 1.0x |
| 1 (75%HP) | 1.0x | 0.85x | 1.08x | 0.9x |
| 2 (50%HP) | 1.1x | 0.7x | 1.16x | 0.8x |
| 3 (25%HP/Enrage) | 1.2x | 0.5x | 1.25x | 0.75x |

구현 위치:
- Speed: BossInferenceAgent._phaseSpeedScale → GetPhaseSpeedScale()
- Cooldown: BossInferenceAgent._phaseCooldownScale → SkillExecutor.CooldownMultiplier
- Damage: BossInferenceAgent._phaseDamageScale → StatManager.PhaseDamageMultiplier (DamageUpMultiplier와 독립)
- Telegraph: BossAutoCastHelper._phaseTelegraphScale → CastStart → wait → Execute → CastEnd

### 텔레그래프 시스템 (신규 구현)
- SkillTelegraphTable.cs: 스킬별 Phase 1 base duration
  - ExecutionSpike 1.35s, CrushingBarrage 1.2s, FortressArmor 1.3s
  - CollapseRoar 1.6s, MarkWave 1.4s, BarrierBreaker 1.4s, ErosionField 1.6s
  - SurvivalPulse 0s, OverchargeMode 0s (즉시 시전)
- BossAutoCastHelper: 코루틴 기반, CastDirection 텔레그래프 시점 고정
- WaitForSeconds ms-key 캐시

### 동기화 완료 항목 (세션 1 + 세션 2 합산)
1. SkillLibrary.cs — Player 10종 + Boss 7종 운영 값 정렬
2. Player onMiss wide retry 4개 스킬에서 제거
3. CollapseRoar 구조 — Player: 80×2, Boss: 55×2
4. BossObservationCollector._maxBurstDmg 120→80
5. Boss SO 8종 Cooldown/Range 운영 값
6. Player SO PiercingShot Range 66→60
7. Player unlock config initial=1, max=4, interval=180s
8. TrainingSkillManager interval 파라미터 추가
9. 페이즈 쿨다운 스케일링 [1, 0.85, 0.7, 0.5] — SkillExecutor.CooldownMultiplier
10. 페이즈 데미지 스케일링 [1, 1.08, 1.16, 1.25] — StatManager.PhaseDamageMultiplier
11. BossObservationCollector maxCD → GetEffectiveCooldown (effective 기준)
12. 텔레그래프 duration 시스템 — SkillTelegraphTable + BossAutoCastHelper 코루틴
13. 드래프트 cadence 175→180s

### 학습 실행 준비
- **Config:** BossInference_config.yaml (PPO, 256×3, gamma 0.995, 40M steps)
- **Behavior name:** BossInference (config ↔ scene 일치 확인)
- **DecisionPeriod:** 5
- **실행:** `mlagents-learn BossInference_config.yaml --run-id=BossInference_v1`
- **init_from: null** — obs 55→129, action [4,4]→[5], hidden 128→256 변경으로 이전 가중치 사용 불가
- **상태: 학습 실행 가능**

### 핵심 파일 위치
| 파일 | 역할 |
|------|------|
| Assets/Scripts/AI/BossAI/Agents/BossInferenceAgent.cs | 통합 학습 에이전트 (이동 + 페이즈 배율) |
| Assets/Scripts/AI/BossAI/Observation/BossObservationCollector.cs | 129ch 관측 수집 |
| Assets/Scripts/AI/BossAI/Training/BossAutoCastHelper.cs | 보스 스킬 자동시전 + 텔레그래프 |
| Assets/Scripts/AI/BossAI/Training/SkillTelegraphTable.cs | 스킬별 텔레그래프 duration 테이블 |
| Assets/Scripts/AI/BossAI/Training/TrainingSkillManager.cs | 스킬 해금 관리 (180s interval) |
| Assets/Scripts/Skill/Core/SkillExecutor.cs | 쿨타임 관리 + CooldownMultiplier |
| Assets/Scripts/Skill/Core/SkillLibrary.cs | 스킬 조립 (런타임 로직) |
| Assets/Scripts/Stats/StatManager.cs | 스탯 관리 + PhaseDamageMultiplier |
| Assets/ScriptableObjects/Skills/BossSkills/*.asset | Boss SO 11종 |
| Assets/ScriptableObjects/Skills/PlayerSkills/*.asset | Player SO 12종 |
| Assets/ScriptableObjects/Skills/SkillPools/*.asset | Boss 3종 + Player 4종 풀 |
| BossInference_config.yaml | ML-Agents 학습 설정 |
| ML_TRAINING_HANDOFF.md | 운영↔학습 동기화 사양서 |

### 미해결 / 추후 작업
- 핸드오프 문서 §5 Player 슬롯 채널 순서 오기 (문서만 수정, 코드 정상)
- Phase 정규화: currentPhase/4 vs (currentPhase+1)/4 — 운영/학습 동일 코드라 ONNX 정합성 무관, 문서 확인만 필요
- WireBossInferenceAgent.cs 에디터 스크립트 — 와이어링 확인 후 삭제 가능
- 학습 완료 후 ONNX export → 운영 drop-in 테스트
- SkillDefinition.TelegraphDuration SO 필드 추가 (장기 — 현재는 SkillTelegraphTable로 대체)

### 레거시 (참조용)
- BossAgent, DualTargetAgent, SkillIntroAgent, SpecializedBossAgent, VsMeleeAgent — 전부 [Obsolete] 마킹
- 이전 학습 자산 (VsRanged_Q4 등) — ONNX 참조용으로만 보관

## 활성 MCP 서버 (.mcp.json)

- **mcp-unity** — Unity 에디터 제어 (씬, 게임오브젝트, 컴포넌트, 콘솔, 리컴파일)
- **codex** — OpenAI Codex CLI MCP (mcp__codex__codex, mcp__codex__codex-reply)

## 응답 스타일 (사용자 선호)

- 한국어로 응답, 짧고 간결하게
- 불필요한 요약/반복 금지
- 코드 작업 전 관련 skill 파일(CLAUDE.md skills 표) 먼저 읽기
- 작업 완료 후 Assets/CHANGES.md 업데이트
- Codex 검증 프롬프트는 텍스트 코드블록으로 출력 (MCP 직접 호출 금지)
- 같은 루프 2회 반복 시 멈추고 사용자 확인

## 작업 시작 시 체크리스트

1. □ Assets/CHANGES.md 최신 내용 확인
2. □ MEMORY.md 인덱스 확인
3. □ 작업 유형에 맞는 skills/SKILL.md 읽기
4. □ ML_TRAINING_HANDOFF.md 확인 (학습 관련 작업 시)
5. □ 기존 시스템(StatManager, ICombatant, SkillComponents 등) 확인

이제 작업 지시를 기다리세요.
```

---

## 사용 방법

1. 새 Claude Code 세션을 `c:\Users\paek6\Downloads\Buildup\Buildup` 디렉토리에서 시작
2. 위 ``` 블록 안의 내용을 통째로 복사
3. 새 세션 첫 메시지로 붙여넣기
4. Claude가 컨텍스트를 읽고 준비 완료 후 작업 지시 가능
