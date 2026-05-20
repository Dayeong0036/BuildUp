# 스크립트 변경 이력

> 원본에서 변경된 스크립트들을 기록한다.
> 스크립트가 변경될 때마다 이 파일을 반드시 업데이트한다.

---

## 3단계 커리큘럼 학습 — Touch/Engage 이동 모드 + TrainingMode enum
**변경일:** 2026-05-20

### 변경 사유
Touch(기초 접근) → Engage(고도화 포지셔닝) → Combat(전투) 3단계 커리큘럼 학습 구조 도입. 동일 네트워크(129ch obs, [5] action) 유지하여 단계 간 가중치 carry-over. 기존 `_touchOnlyMode` bool → `BossTrainingMode` enum 전환.

### 변경 내용

**BossInferenceAgent.cs:**
- `BossTrainingMode` enum 추가: `{Combat, Touch, Engage}`
- `_touchOnlyMode` bool → `[SerializeField] private BossTrainingMode _trainingMode` 교체
- Helper properties: `IsCombatMode`, `IsTouchMode`, `IsEngageMode`, `UsesCombatSystems`
- Touch 모드 필드: `_touchEpisodeDuration=30f`, `_proximityRadius=5f`, `_doubleTouchWindow=3f`, `_touchProgressWeight=0.02f`, `_touchIdlePenalty=0.005f`, `_touchMinMoveDelta=0.05f`
- Touch 상태: `_p1Touched`, `_p2Touched`, `_p1TouchTime`, `_p2TouchTime`, `_prevTouchMetric`, `_prevBossPos`, `_prevPosValid`, `_touchSuccessCount`, `_touchFastCount`
- `ResetTouchState()`: 터치 추적 변수 초기화
- `ApplyTouchRewards()`: 거리합 진행 보상, 터치 +0.5, 빠른 더블터치 +0.5, 정지 패널티
- `CheckTouchTermination()`: OOB -1, 30s 타임아웃 (없음=-0.5, 부분=-0.2)
- Engage 모드 필드: `_engageRangeMin=3f`, `_engageRangeMax=8f`, `_engageDotThreshold=0.5f`, `_pressurePointsMax=10f`, `_pressureDrainRate=1f`, `_engageEpisodeDuration=90f`, `_engageFarPenaltyDist=15f`
- Engage 상태: `_p1PressureHP`, `_p2PressureHP`, `_p1WasEngaged`, `_p2WasEngaged`, `_lastEngageTickTime`, `_engageSuccessCount`
- `ResetEngageState()`: 압박 HP / engaged 플래그 / tick 시간 초기화
- `IsEngaged()`: 거리 [min,max] AND dot ≥ threshold 확인
- `ApplyEngageRewards()`: dt 기반 — engage +0.01/s per player, both +0.02/s, transition +0.1, pressure drain +0.05/pt, success +0.5, far penalty -0.005/s, idle -0.005/s
- `CheckEngageTermination()`: OOB -1, 타임아웃 잔여 HP 비례 패널티
- `OnEpisodeBegin()`: 모드별 분기 — AutoCast는 Combat에서만 활성, Touch/Engage는 `ResetTouchState`/`ResetEngageState`
- `OnActionReceived()`: switch(_trainingMode) — Touch→ApplyTouchRewards+CheckTouchTermination, Engage→ApplyEngageRewards+CheckEngageTermination, Combat→기존
- `AssignPools()`, `AssignPlayerProfile()`: `UsesCombatSystems` 가드로 Touch/Engage 시 스킬 할당 스킵
- `ResetState()`: 스킬/executor/bias 리셋 `UsesCombatSystems` 조건부
- `WriteDiscreteActionMask()`: `!UsesCombatSystems` 시 early return
- 에피소드 로깅 추가: 시작/종료, 페이즈 전환 + 배율 값, 플레이어 사망
- `_playerSkillPools` 필드 제거 (dead code)
- `SetPlayersFrozenForTouch` / `FreezeForTouch` 메서드 제거 (플레이어 BT 유지)

**BossAutoCastHelper.cs:**
- `EmitTelemetry()` HIT/MISS 로깅 추가 (hit count + distance)

**TrainingSkillManager.cs:**
- `UnlockNextSlot()` 스킬 언락 로깅 추가 (skill name, slot#, total)

**BossInference_touch_config.yaml (신규):**
- PPO, batch 512, buffer 5120, gamma 0.99, 5M steps, time_horizon 512, hidden 256×3

**BossInference_engage_config.yaml (신규):**
- PPO, batch 1024, buffer 10240, gamma 0.995, 10M steps, time_horizon 1024, hidden 256×3

**씬 Inspector 수정 (Chapter1.unity):**
- BossObservationCollector._maxBurstDmg: 120→80
- Player 1P/2P TrainingSkillManager: _maxUnlockCount 3→4, _unlockInterval 15→180
- SpecializedBossAgent 컴포넌트 (Train) Boss에서 제거

### 커리큘럼 학습 명령어
1. Touch: `mlagents-learn BossInference_touch_config.yaml --run-id=BossInference_touch_v1`
2. Engage: `mlagents-learn BossInference_engage_config.yaml --initialize-from=BossInference_touch_v1 --run-id=BossInference_engage_v1`
3. Combat: `mlagents-learn BossInference_config.yaml --initialize-from=BossInference_engage_v1 --run-id=BossInference_v1`

### 검증 결과
- 컴파일 에러 0, 워닝 0

---

## 텔레그래프 duration 시스템 + 드래프트 cadence 수정
**변경일:** 2026-05-19

### 변경 사유
1. 핸드오프 §7/§8 텔레그래프 미구현 — 스킬이 즉시 발동되어 운영과 다른 타이밍
2. 드래프트 cadence 175s → 180s 불일치 (핸드오프 §2/§7: 180s × 4 라운드)

### 변경 내용

**NEW SkillTelegraphTable.cs:**
- 보스 스킬별 Phase 1 기준 텔레그래프 duration 테이블 (9종)
- ExecutionSpike 1.35s, CrushingBarrage 1.2s, FortressArmor 1.3s, CollapseRoar 1.6s, MarkWave 1.4s, BarrierBreaker 1.4s, ErosionField 1.6s, SurvivalPulse 0s, OverchargeMode 0s

**BossAutoCastHelper.cs:**
- 텔레그래프 순서 변경: CastStart → wait(duration) → Execute → CastEnd
- `_phaseTelegraphScale = {1, 0.9, 0.8, 0.75}` 페이즈별 텔레그래프 스케일링
- `_castRoutine` 필드로 중복 코루틴 방지
- `StopCastRoutine()` cleanup — Initialize, SetEnabled(false), OnDisable에서 호출
- WaitForSeconds ms-key 캐시 (CLAUDE.md 규칙 준수)
- CastDirection은 텔레그래프 시작 시점에 고정 (회피 윈도우 제공)
- telegraph=0 스킬(SurvivalPulse, OverchargeMode)은 기존과 동일 즉시 시전

**TrainingSkillManager.cs:**
- `_unlockInterval` 기본값 175f → 180f

**BossInferenceAgent.cs:**
- `SetUnlockConfig(1, 4, 175f)` → `SetUnlockConfig(1, 4, 180f)` (P1, P2 모두)

---

## 페이즈별 쿨다운/데미지 배율 구현 (핸드오프 §7)
**변경일:** 2026-05-19

### 변경 사유
ML_TRAINING_HANDOFF.md §7 페이즈 배율 중 speed만 구현돼 있었음. cooldown [1, 0.85, 0.7, 0.5]과 damage [1, 1.08, 1.16, 1.25] 미구현 → 학습 환경이 운영 페이즈 스케일링 미재현.

### 변경 내용

**SkillExecutor.cs:**
- `CooldownMultiplier` 프로퍼티 추가 (기본값 1f, 최소 0.01f clamp)
- `GetEffectiveCooldown(skill)` 메서드 추가 → `skill.Cooldown * _cooldownMultiplier`
- `CanUse()`, `GetRemainingCooldown()` → effective cooldown 기준으로 변경
- `ResetAll()`에서 `_cooldownMultiplier = 1f` 초기화
- 디버그 로그에 effective/base/multiplier 표시

**StatManager.cs:**
- `_phaseDamageMultiplier` 필드 추가 (버프 DamageUpMultiplier와 독립)
- `SetPhaseDamageMultiplier(float)` 메서드 추가
- `DealDamage()`, `DealShieldBreakDamage()` → `* _phaseDamageMultiplier` 추가
- `ResetForTraining()`에서 `_phaseDamageMultiplier = 1f` 초기화

**BossInferenceAgent.cs:**
- `_phaseCooldownScale = {1, 0.85, 0.7, 0.5}` 배열 추가
- `_phaseDamageScale = {1, 1.08, 1.16, 1.25}` 배열 추가
- `ApplyPhaseMultipliers()` 메서드: phase 변경 감지 시 SkillExecutor + StatManager 적용
- `OnActionReceived()` 초반 호출, `ResetState()`에서 `_lastPhaseApplied=-1` + 즉시 적용

**BossObservationCollector.cs:**
- `AddSlotObservation()` maxCD 채널 → `executor.GetEffectiveCooldown(skill)` 사용
- remCD/maxCD 모두 effective 기준으로 관측 일관성 확보

---

## 운영 동기화 — SkillLibrary + BossObservationCollector 값 정렬
**변경일:** 2026-05-19

### 변경 사유
Codex 교차 검증에서 학습 환경 SkillLibrary.cs 값과 운영(ML_TRAINING_HANDOFF.md §8) 값 불일치 다수 발견. ONNX drop-in 호환 + 학습 품질 보장을 위해 운영 값으로 일괄 정렬.

### 변경 내용

**BossObservationCollector.cs:**
- `_maxBurstDmg` 120→80 (운영 §5 #128 정규화 상수 일치)

**SkillLibrary.cs — Player 스킬 (10종 수정):**
- ExecutionSpike: dmg 125→100, cone 30°→32°, onMiss wide retry 제거
- CrushingBarrage: per-hit 34→28, cone 35°→38°, onMiss wide retry 제거
- ErosionField: projSpeed 19→28, innerAoE 7.6→9, outerAoE 20.4→20
- HuntingMark: dmg 68→60, projSpeed 22→35
- FortressArmor: dmg 90→75, cone 35°→38°, onMiss wide retry 제거
- SealChain: dmg 60→50, projSpeed 20→30
- CollapseRoar: dmg 95→80×2hit(구조 변경), innerAoE 10.6→12, onMiss 제거
- BarrierBreaker: dmg 105→88, projSpeed 19→30
- PiercingShot: dmg 140→110, projSpeed 25→40
- RuptureMagazine: dmg 115→95, projSpeed 20→30

**SkillLibrary.cs — Boss 스킬 (7종 수정):**
- ExecutionSpike_Boss: dmg 118→76, range 16.6→18, cone 35°→38°
- CrushingBarrage_Boss: per-hit 32→20
- ErosionField_Boss: innerAoE 9→8, outerAoE 22.2→20, DoT 7→8/s
- FortressArmor_Boss: dmg 82→65
- CollapseRoar_Boss: 88×1hit→55×2hit (구조 변경)
- MarkWave_Boss: dmg 70→50, cone 80°→65°, range 27→22
- BarrierBreaker_Boss: dmg 90→62, projRange 48→40

**Boss SkillDefinition SO (8종 Cooldown/Range 수정 — 관측 채널 직접 영향):**
- ExecutionSpike_Boss: CD 8→10, Range 24→18
- CrushingBarrage_Boss: CD 6→10, Range 24→15
- FortressArmor_Boss: CD 8→12, Range 24→16
- CollapseRoar_Boss: CD 10→14, Range 27→24
- MarkWave_Boss: CD 8→12, Range 33→22
- BarrierBreaker_Boss: CD 9→14, Range 66→48
- ErosionField_Boss: CD 10→14, Range 42→22
- SurvivalPulse_Boss: CD 14→20

**Player SkillDefinition SO (1종):**
- PiercingShot: Range 66→60

**TrainingSkillManager.cs:**
- `SetUnlockConfig` interval 파라미터 추가 (기본값 -1 = 변경 없음)

**BossInferenceAgent.cs:**
- Player TrainingSkillManager unlock config 코드 추가: initial=1, max=4, interval=175s
- 핸드오프 §7 기준: 초기 1슬롯 + 175s 간격 3회 드래프트 = 최대 4슬롯

### 미해결
- 핸드오프 문서 §5 Player 슬롯 채널 순서 오기(문서만 수정 필요, 코드 정상)

---

## BAL-1 R3-R8 Phase 2C — 레거시 Agent obsolete + config.yaml + Codex 수정
**변경일:** 2026-05-19

### 변경 내용

**레거시 5 Agent [Obsolete] 마킹:**
- BossAgent.cs, DualTargetAgent.cs, SkillIntroAgent.cs, SpecializedBossAgent.cs, VsMeleeAgent.cs
- 모두 `[System.Obsolete("BossInferenceAgent 사용")]` 추가
- Obsolete 클래스 내부에서 Obsolete 메서드 호출 시 컴파일러 자동 억제 확인 (pragma 불필요)

**BossInference_config.yaml (신규):**
- §11 하이퍼파라미터 그대로: PPO, batch 1024, buffer 10240, lr 3e-4
- hidden_units 256, num_layers 3 (이전 128/2에서 상향)
- gamma 0.995, time_horizon 1024
- max_steps 40M, summary_freq 50000, checkpoint_interval 200000
- keep_checkpoints 5→30 (Codex 권장, 40M 학습 중간 모델 비교용)

### Codex 피드백 수정 (APPROVED WITH CHANGES)

**C1 — Header 인코딩:** BossInferenceAgent.cs 모든 [Header] ASCII로 교체
**C2 — Obsolete 워닝:** [Obsolete] 클래스 내부 자동 억제 확인, pragma 불필요로 판명 → 제거
**C3 — AssignPools NRE:** _trainingSkillManager null guard 추가
**C4 — Scene wiring:** Unity Editor 수동 작업 목록 확인 (코드 범위 밖)

**Suggestions 반영:**
- keep_checkpoints 30으로 상향
- CSV 파일명에 worker_id 추가 (병렬 학습 파일 경합 방지)
- BossController.OnPhaseChanged: training mode에서 NavMesh speed mutation 방지

### 검증 결과
- 컴파일 에러 0, 워닝 3 (기존, 신규 0)

---

## BAL-1 R3-R8 Phase 2B — BossInferenceAgent 신규 작성 + Codex 수정
**변경일:** 2026-05-19
**기준 문서:** ML_TRAINING_HANDOFF.md §2/§5/§10/§11 + project_phase2_additions 체크리스트

### 변경 사유
4개 레거시 Agent(VsMelee/Ranged/Hybrid/CC + SpecializedBossAgent)를 단일 통합 에이전트로 교체. 1 branch [5] 이동만, 스킬은 BossAutoCastHelper 위임. 129ch observation + 7 reward 신호.

### 변경 내용

**BossInferenceAgent.cs (신규):**
- `Agent` 상속, BehaviorParameters: obs=129, branches=[5], continuous=0
- Action Branch 0: idle(0)/forward(1)/turnLeft(2)/turnRight(3)/backward(4, ×0.7)
- 후퇴 속도 = `_moveSpeed × 0.7 × phaseSpeedScale`
- IsBusy 가드: IsCasting 시 이동 액션 1~4 마스킹 (idle만 허용)
- CollectObservations: `_collector.CollectFull129(sensor)` 단일 호출
- Reward 7신호: Win+1, Loss-1, Timeout-0.3, Dmg+0.001, DmgTaken-0.0005, Phase+0.05, Step-0.0001
- Episode 720s, OutOfBounds -5f
- OnEpisodeBegin: archetype 독립 균등 샘플, 풀 분배, 스폰, PlayerBiasTracker.ResetAll()
- P1/P2 BehaviorGraph 교체 (archetype별 프로필)
- CSV 로깅: results/episodes_{behavior}.csv (§12 포맷)
- Heuristic: W=fwd, A=left, D=right, S=backward

### Codex 피드백 수정 (APPROVED WITH CHANGES)

**Critical 1 — Phase reset:**
- BossController.cs: `ResetPhaseForTraining()` public 메서드 추가 (currentPhase=0)
- BossInferenceAgent.ResetState()에서 호출

**Critical 2 — Phase observation 정규화:**
- BossObservationCollector.CollectFull129: `phase / 4` → `(phase + 1) / 4`
- Phase 0→0.25, Phase 3(Enrage)→1.0 으로 full range 사용

**Critical 3 — Boss 5-slot 즉시 해금:**
- TrainingSkillManager: `SetUnlockConfig(int initial, int max)` 추가
- BossInferenceAgent.Initialize에서 `SetUnlockConfig(5, 5)` 호출

**Critical 4 — IsCasting 마스크:**
- 현재 스킬이 즉시 실행 구조이므로 학습 지장 없음 (Codex 동의)
- telegraph duration이 추가되면 별도 busy timer 필요 (Phase 3 이후)

**Suggestions 반영:**
- BossAutoCastHelper.CanBossCast(): 7조건 명시 체크 (IsAlive, IsCasting, IsParrying, Stunned, HitStun, Silence, Rooted, StateManager.CanCast)
- BossAutoCastHelper.ResetState() 제거 (agent에서 executor.ResetAll 한 곳에서만)
- BossAutoCastHelper.Initialize(): _enabled=false 시작, agent가 ResetState 완료 후 SetEnabled(true) 호출
- BossInferenceAgent: 모든 컴포넌트 참조에 null guard 추가
- CSV outcome: _endReason 직접 기록 (OutOfBounds 분리)

### 검증 결과
- 컴파일 에러 0, 워닝 6 (기존, 신규 없음)

---

## BAL-1 R3-R8 Phase 2A — 학습 환경 기반 인프라 (Slot·Obs·AutoCast)
**변경일:** 2026-05-19
**기준 문서:** ML_TRAINING_HANDOFF.md + 운영 Q1~Q4 회신

### 변경 사유
BossInferenceAgent(Phase 2B) 작성 전 필요한 인프라 3종: 5슬롯 스킬 관리, 129ch 관측 수집, 보스 전용 자동 시전. 운영 Q1~Q4 회신 반영.

### 변경 내용

**TrainingSkillManager.cs:**
- MaxSlots 3→5, _maxUnlockCount 3→5, _unlockInterval 15→175s (운영 cardDraftInterval 동기)

**PlayerBiasTracker.cs:**
- `ResetAll()` public 메서드 추가 (에피소드 리셋용, 운영 Q2 회신)

**BossObservationCollector.cs:**
- BossSkillSlots 3→5, FullObsSize=129, ChannelsPerSlot=7, TouchRange=5f 상수 추가
- P1/P2 SkillManager/SkillExecutor/TrainingSkillManager 캐시 추가
- `EnsureSkillArrays()` + `OnValidate()` — 직렬화 배열 3→5 안전 보정
- `CollectFull129(VectorSensor)` — 129ch 통합 관측 메서드 신규
  - `AddSlotObservation()` per-slot 7ch (remCD/30, maxCD/30, range/55, coneOrAoE, one-hot3)
  - `AddPlayerSlots()` 플레이어 5슬롯 관측
- `CollectUpToPhase3/4/5` → `[Obsolete("CollectFull129 사용")]` 마킹

**BossAutoCastHelper.cs (신규):**
- 보스 전용 자동 시전 — SkillManager.AutoCast(플레이어→보스)와 분리
- Initialize(p1, p2)로 플레이어 타겟 설정 + SkillManager.AutoCast 비활성화
- Update 루프: 슬롯 순회 → CanUse → 가까운 플레이어 탐색 → 거리/조건 검사 → Execute
- BossController를 ICombatant Caster로 사용

### 검증 결과
- 컴파일 에러 0, 워닝 6 (기존 2 + Obsolete 3 + 미사용 필드 1, 신규 에러 없음)

---

## BAL-1 R3-R8 운영 동기화 — Batch 1B (AIHint 필드 + 보스 wide retry 제거)
**변경일:** 2026-05-19
**기준 문서:** 운영 T1C 완료 회신 (SkillDefinition AIHint 필드 + 23 SO 값 확정)

### 변경 사유
운영 T1C 라운드 완료로 SkillDefinition AI 관측 힌트 필드가 확정됨. 학습 환경에 동일 필드 추가 + 23개 SO 값 동기화. 보스 스킬 4개의 wide retry(onMiss) 분기는 운영 R2에서 이미 제거되어 학습 측도 동기 제거.

### 변경 내용

**SkillDefinition.cs 신규 필드:**
- `SkillCategoryFlag` enum 추가 (None=0, Directional=1, AoE=2, Projectile=3, Self=4)
- `AIHint_ConeOrAoE` (float) — cone: deg / AoE: m / projectile·self: 0
- `AIHint_Category` (SkillCategoryFlag)

**23 SO .asset 값 동기화 (Boss 11 + Player 12):**
- Boss: ExecutionSpike(38,Dir) CrushingBarrage(40,Dir) FortressArmor(35,Dir) CollapseRoar(12,AoE) MarkWave(65,Dir) BarrierBreaker(0,Proj) ErosionField(20,AoE) SurvivalPulse(0,Self) OverchargeMode(0,Self) SealChain(0,None) RuptureMagazine(0,None)
- Player: ExecutionSpike(32,Dir) CrushingBarrage(38,Dir) FortressArmor(38,Dir) CollapseRoar(12,AoE) PiercingShot(0,Proj) BarrierBreaker(0,Proj) HuntingMark(0,Proj) ErosionField(9,Proj) SealChain(0,Proj) RuptureMagazine(0,Proj) SurvivalPulse(0,Self) OverchargeMode(0,Self)

**보스 wide retry(onMiss) 제거 — SkillLibrary.cs:**
- `ExecutionSpike_Boss`: onMiss 분기 제거, TriggerOnHit(onHit:...) 만 유지
- `CrushingBarrage_Boss`: onMiss 분기 제거 (중첩 TriggerOnHit 포함)
- `FortressArmor_Boss`: onMiss 분기 제거
- `CollapseRoar_Boss`: 두 번째 TriggerOnHit(onMiss:...) 호출 전체 제거

### 검증 결과
- `rg AIHint_ConeOrAoE` Assets/ScriptableObjects/Skills → 23건
- `rg AIHint_Category` → 23건, Category:0 은 SealChain_Boss + RuptureMagazine_Boss만
- 보스 onMiss 잔존 0건, 플레이어 4건은 운영 T2 대기
- 컴파일 에러 0, 워닝 3 (기존, 신규 없음)

---

## BAL-1 R3-R8 운영 동기화 — Batch 1A (사양 수치 동기화)
**변경일:** 2026-05-19
**기준 문서:** ML_TRAINING_HANDOFF.md (BAL-1, 2026-05-18) + 운영 측 18 항목 회신

### 변경 사유
운영(production) 클라이언트 BAL-1 R3-R8 패치로 학습 환경 동기화 필수. 학습 ML이 만든 ONNX의 운영 drop-in 정합성 확보. Batch 1A는 운영 T1C/T2/T3 라운드 완료를 기다리지 않고 즉시 적용 가능한 수치 동기화 범위.

### 변경 내용

**기본 스탯 SO (.cs default + .asset 인스펙터):**
- `Assets/PlayerStatsSO.cs`: MaxHP 100→150, CurrentHP 100→150, MoveSpeed 5→14
- `Assets/BossStatsSO.cs`: BossMaxHP 1000→6000, BossCurrentHP 1000→6000
- `Assets/Player&Boss/PlayerStatsSO.asset`: MaxHP 200→150, CurrentHP 100→150, MoveSpeed 100→14
- `Assets/Player&Boss/PlayerStatsSO_Training.asset`: MaxHP 300→150, CurrentHP 300→150, MoveSpeed 100→14
- `Assets/Player&Boss/BossStatsSO.asset`: BossMaxHP 1000→6000, BossCurrentHP 1000→6000, BossPhaseThresholds []→[0.75, 0.5, 0.25]
- `Assets/Player&Boss/BossStatsSO_Training.asset`: BossMaxHP 5000→6000, BossCurrentHP 5000→6000, BossPhaseThresholds []→[0.75, 0.5, 0.25]

**관측 정규화 상수 (BossObservationCollector + Chapter1.unity + 3 Agent 내부 const):**
- `Assets/Scripts/AI/BossAI/Observation/BossObservationCollector.cs`: _maxBurstDmg 50→120, _maxSpeed 12→16, _maxBossPhase 2→4
- `Assets/Scenes/Chapter1.unity` (BossObservationCollector serialized): _maxBurstDmg 50→120, _maxSpeed 12→16, _maxBossPhase 2→4
- `SkillIntroAgent.cs:644-645`: const maxSpeed 12f→16f, maxBurstDmg 50f→120f
- `SpecializedBossAgent.cs:727-728`: 동일
- `VsMeleeAgent.cs:685-686`: 동일

**학습 Agent 파라미터 (5개 Agent .cs + Chapter1.unity 씬 override):**
- `BossAgent.cs` / `DualTargetAgent.cs` / `SkillIntroAgent.cs` / `SpecializedBossAgent.cs` / `VsMeleeAgent.cs`: _moveSpeed 5f→8.4f, _rotationSpeed 200f→540f
- `Chapter1.unity` (SpecializedBossAgent serialized): _moveSpeed 10→8.4, _rotationSpeed 200→540

**Projectile 감지 반경:**
- `Assets/Scripts/Skill/Projectile/SkillProjectile.cs:10`: _detectionRadius 0.5f→1.0f
- `Assets/Scripts/Skill/Prefab/ProjectTile.prefab`: _detectionRadius serialize 없음 → .cs default 자동 적용, prefab Edit 불필요

### 운영 회신 근거
- §B1: _maxBurstDmg 80→120 (PiercingShot 110×1.25=137, CB 4히트=140 → 80도 saturation)
- §F1: Boss Rotation 540°/s 확정 (Codex 권장 oscillation 감소로 720→540)
- §C: SO .asset YAML 직접 수정 OK (단순 수치)
- Codex F: SkillProjectile prefab 내 _detectionRadius serialize 없음 — .cs default 변경으로 충분

### 검증 가이드 (Codex D, E)
- 100K smoke는 발산/NaN/극단 oscillation 탐지 용도만 (정식 학습 품질 판단 X — BossInferenceAgent + 새 7신호 reward 적용 후로 보류)
- 모니터링: action histogram(좌/우 회전 비율, 좌우 번갈이 비율), 초반 10초 yaw delta/sec, wall/out-of-bounds 접촉 시간, Policy/Entropy 급락, KL spike, value loss spike, episode length, target distance

### 후속 Batch / Phase
- **Batch 1B** (운영 T1C 완료 후): SkillDefinition AIHint 필드 + 23 SO .asset 동기 + 보스 7개 wide retry 동기
- **Batch 1C** (운영 T2 완료 후): 플레이어 4개 wide retry 제거 동기 (ExecutionSpike/CrushingBarrage/FortressArmor/CollapseRoar onMiss)
- **Batch 1D** (운영 T3 완료 후): SkillExecutor.TelegraphScale + StatManager.PhaseDamageScale + SkillExecutor.CooldownScale
- **Phase 2**: BossInferenceAgent 신규 작성 (1 branch [5], 129ch obs) + AutoCast 4파일 포팅 + BT PlayerBiasTracker hook
- **Phase 3**: Reward 7신호 + Episode 720s + cardDraftInterval 175s × 4 라운드 + config.yaml(40M)
- **Phase 4**: 비교표 4종 + recompile 검증 + 100K smoke
- **Phase 5**: 본 학습 40M step (운영 T2/T3 완료 후)

---

## 플레이스타일 특화 보스 에이전트 (SpecializedBossAgent)
**경로:** `Assets/Scripts/AI/BossAI/Agents/SpecializedBossAgent.cs` (신규)
**변경일:** 2026-04-30 (VsMelee 튜닝: 2026-05-03)

### 변경 사유
- 범용 SkillIntroAgent의 단일 보상 체계로는 플레이스타일별 최적 대응 학습 한계 (46.5% 승률에서 수렴 정체)
- 플레이어 유형(근접/원거리/하이브리드/CC)마다 보스가 다른 보상 가중치로 특화 학습 필요

### 변경 내용
- **SpecializedBossAgent.cs** (신규) — SkillIntroAgent 전체 로직 복사 + 특화 기능 추가
  - `RewardPreset` enum: VsMelee / VsRanged / VsHybrid / VsCC 4종 드롭다운
  - 4개 전용 `PlayerProfile` 필드 (`_vsMeleeProfile`, `_vsRangedProfile`, `_vsHybridProfile`, `_vsCCProfile`) — 배열 인덱스 대신 명시적 필드 사용 (Codex 피드백 반영)
  - `ApplyPreset()` — 프리셋별 하드코딩 보상 가중치 적용, `OnEpisodeBegin`에서 `AssignPools()` 이전 호출
  - `GetPresetProfile()` — switch 표현식으로 프리셋에 맞는 프로파일 반환
  - `AssignPools()` — 랜덤 선택 대신 프리셋 프로파일 사용, P1/P2 동일 프로파일 적용
  - 관측값(55), 행동(2 브랜치), 데이터 수집(43 컬럼) 로직은 SkillIntroAgent와 동일
- **학습 설정:** `results/VsHybrid/`, `results/VsCc/` 컨피그 생성, `initialize_from: SkillIntro_Comprehensive`로 가중치 전이

### VsMelee 프리셋 특화 튜닝 (2026-05-03)
- **변경 사유:** VsMelee 프리셋이 기본값 수준으로 근접 매치업 특화 안 됨. Codex 교차 검증 후 조정 적용.
- **핵심 방향:** 반격/정렬/후퇴억제 중심 특화
- **변경 값:** CounterAttack 0.08→0.12, Progress 0.15→0.10, Align 0.005→0.006, CloseAlignBonus 0.002→0.005, RetreatPenalty 0.003→0.006, touchReward 0.2→0.15, OutOfRangePenalty 0.1→0.08, rangeTolerance 3→3.5, DamageReward 0.7→0.75, ShieldDamageReward 0.3→0.35, HitReward 0.3→0.35, StepPenalty 0.0005→0.0003, FireReward 0.05→0.06, MissPenalty 0.03→0.05, IdlePenalty 0.003→0.008, RangeMaintain 0.2→0.15, playerKilledReward 0.5→0.6

---

## 플레이스타일별 BehaviorGraph 노드 분화 + 거리 전환 / 협공 각도 시스템
**경로:** `Assets/Scripts/AI/Player/DoubleMove/AdjustRangeBySkillState.cs` (신규), `Assets/Scripts/AI/Player/DoubleMove/IsFlankAngleLowCondition.cs` (신규), `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`, `Assets/Scripts/AI/Player/Combat/PlayerBotCombatInit.cs`
**변경일:** 2026-04-27

### 변경 사유
- 4종 BehaviorGraph가 동일 노드 구조 → 플레이스타일별 실제 행동 분화 필요
- 회피형(RangedKiter)이 스킬 쿨다운 중에도 보스 근처에 머무르는 문제
- CCDebuffer의 플랭크 트리거가 "보스 시선" 기준이라 협공 판정 부정확

### 변경 내용
- **AdjustRangeBySkillState.cs** (신규) — Action 노드. SkillManager.CanUse() 확인 후 OptimalMin/Max를 공격거리↔안전거리로 전환. SkillManager 1회 캐싱, 없으면 현재 값 유지+Success (Codex 피드백 반영)
- **IsFlankAngleLowCondition.cs** (신규) — Condition 노드. Boss-Self-Ally 사잇각이 MinFlankAngle 이하인지 판정. Ally 사망 시 false 반환 (PlayerController.IsAlive 체크, Codex 피드백 반영)
- **SkillIntroAgent.cs** — PlayerProfile에 AttackRangeMin/Max, SafeRangeMin/Max, MinFlankAngle 5개 필드 추가. SwapPlayerGraph()에 5개 변수 주입 추가
- **PlayerBotCombatInit.cs** — 동일 5개 SerializeField 추가, override 블록에 주입 추가

### 그래프별 적용 계획 (Unity 에디터 수동 작업 필요)
- **MeleeAggro_Move** — 변경 없음 (블랙보드에 변수만 공통 추가)
- **RangedKiter_Move** — CalcApproach 앞에 AdjustRangeBySkillState 삽입
- **HybridBalanced_Move** — 동일 위치에 AdjustRangeBySkillState 삽입 (파라미터 값 다름)
- **CCDebuffer_Move** — IsBossTargetingAlly → IsFlankAngleLow 교체

---

## 스킬풀별 플레이어 이동 BehaviorGraph 분리
**경로:** `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`, `Assets/Scripts/AI/Player/Combat/PlayerBotCombatInit.cs`
**변경일:** 2026-04-27

### 변경 사유
- 플레이어 봇이 모든 스킬풀에서 동일한 이동 패턴 → 보스가 다양한 플레이 스타일에 대응 학습 불가

### 변경 내용
- **SkillIntroAgent.cs** — `PlayerProfile` 구조체 (SkillPoolSO + BehaviorGraph + 이동 파라미터 8개), `_playerProfiles[]` 배열로 병렬 배열 대체, `SwapPlayerGraph()` 메서드 (Graph 교체 → Init → 블랙보드 주입 → Restart)
- **PlayerBotCombatInit.cs** — `_overrideMovementParams` bool 플래그 추가, 학습 모드에서는 SkillIntroAgent가 파라미터 주입

---

## CSV 데이터 확장 + 학습 타입별 파일 분리
**경로:** `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`
**변경일:** 2026-04-27

### 변경 사유
- 종료 사유, 누적 보상, 터치/사망 시점, 시전 횟수, 이동 거리 등 밸런스 분석에 필요한 데이터 누락
- 여러 학습 타입(SkillIntro, DualTarget 등)의 로그가 같은 파일에 혼재

### 변경 내용
- CSV 14컬럼 → 23컬럼 확장 (EndReason, BossCasts, CumulativeReward, FirstTouchP1/P2, P1/P2DeathTime, BossTravelDist, UnlockedSkills)
- 파일명 `matchup_log_{BehaviorName}.csv`로 학습 타입별 분리
- Codex 지적: AddReward → RecordMatchResult 순서로 변경하여 CumulativeReward에 terminal reward 포함
- 기존 SkillExecutor.TotalUseCount 재사용 (TotalAttemptCount 중복 추가 안 함)

---

## 플레이어 봇 벽 구석 박힘 방지
**경로:** `Assets/Scripts/AI/Player/DoubleMove/PlayerArenaBounds.cs` (신규), `CalcFleePositionAction.cs`, `CalcStrafePositionAction.cs`, `CalcSpreadPositionAction.cs`, `CalcFlankPositionAction.cs`
**변경일:** 2026-04-27

### 변경 사유
- Flee/Strafe/Spread/Flank 계산 시 맵 경계를 인식하지 못해 플레이어가 벽 구석에 박히는 현상

### 변경 내용
- **PlayerArenaBounds.cs** (신규) — 아레나 중심(9.86, 7.62), 유효범위 71m 기준 `ClampToArena()` static 헬퍼
- **4개 Calc 액션** — candidate 계산 후 `PlayerArenaBounds.ClampToArena()` 호출 추가 (NavMesh.SamplePosition 전)

---

## 스킬풀 랜덤 분배 + 매치업 승률 기록
**경로:** `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`, `Assets/Scripts/AI/BossAI/Training/TrainingSkillManager.cs`
**신규 SO:** `Assets/ScriptableObjects/Skills/` — PlayerMeleeBurst, PlayerRangedKiter, PlayerHybridSurvivor, PlayerCCDebuffer, BossMeleeAggro, BossRangedZoner, BossTankSustain
**변경일:** 2026-04-27

### 변경 사유
- 고정 스킬풀로는 보스가 한 가지 플레이 스타일에만 적응 → 범용성 부족
- 어떤 매치업에서 보스가 강/약한지 파악할 수단 없음

### 변경 내용
- **TrainingSkillManager.cs** — `SetSkillPool()` 메서드 추가 (런타임 풀 교체)
- **SkillIntroAgent.cs**:
  - `_bossSkillPools[]`, `_playerSkillPools[]` Inspector 배열 추가
  - `AssignRandomPools()` — 에피소드 시작마다 보스/P1/P2 풀 랜덤 할당
  - `RecordMatchResult()` — 보스 승/패를 매치업 키(보스풀 vs P1풀+P2풀)별로 집계
  - `LogMatchupStats()` — 50 에피소드마다 전체 매치업 승률 콘솔 출력
  - 종료 로그에 매치업 키 포함 (`GetMatchupKey()`)
- **스킬풀 SO 7종 신규 생성** — 플레이어 4종(근접폭딜/원거리견제/생존균형/CC디버프) + 보스 3종(근접압살/원거리공간압박/생존지구전)

---

## 죽은 플레이어 타격 방지 보상 구조 수정
**경로:** `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`
**변경일:** 2026-04-26

### 변경 사유
- 보스가 이미 죽은(Frozen) 플레이어를 계속 타겟팅/타격하면서 의미 없는 보상을 수집
- 접근/정렬 보상(progress/align)이 죽은 플레이어 방향으로도 발생
- 학습 효율 저하 — 보스가 죽은 플레이어에게 시간 낭비

### 변경 내용
- **GetActiveTarget()** — 죽은 플레이어를 타겟 후보에서 완전 제외, 살아있는 플레이어만 반환
- **TryExecuteSkill()** — 살아있는 타겟이 없을 때 스킬 사용 차단 + `_hitDeadPlayerPenalty`(0.3) 페널티 부여
- **_hitDeadPlayerPenalty** 필드 추가 (Inspector에서 실시간 조정 가능)

---

## 스킬 풀 분리 — 보스/플레이어 별도 스킬 풀
**경로:** `Assets/ScriptableObjects/Skills/PlayerTrainingSkillPool.asset` (신규)
**수정:** `Assets/Scenes/Chapter1.unity`
**변경일:** 2026-04-26

### 변경 사유
- 보스와 플레이어가 같은 _Boss 스킬풀을 공유하고 있어 밸런스 붕괴 (보스가 매 에피소드 사망)
- BarrierBreaker_Boss의 실드파괴 보너스(195 dmg/hit)를 플레이어가 그대로 사용

### 변경 내용
- **PlayerTrainingSkillPool.asset** 신규 생성 — 플레이어 버전 스킬 12종 (BarrierBreaker, CollapseRoar, CrushingBarrage, ErosionField, ExecutionSpike, FortressArmor, HuntingMark, OverchargeMode, PiercingShot, RuptureMagazine, SealChain, SurvivalPulse)
- **Chapter1.unity** — P1/P2 TrainingSkillManager._skillPool을 PlayerTrainingSkillPool로 변경
- 보스 TrainingSkillManager는 기존 TrainingCombatSkillPool(_Boss 스킬 11종) 유지

---

## Phase 3 SkillIntroAgent — 원거리 스킬 대응 보스 학습 에이전트
**경로:** `Assets/Scripts/AI/BossAI/Agents/SkillIntroAgent.cs`, `Assets/Scripts/AI/BossAI/Training/TrainingSkillManager.cs`, `Assets/Scripts/Skill/Core/SkillPoolSO.cs`
**보완:** `BossController.cs`, `BossObservationCollector.cs`, `SkillManager.cs`
**변경일:** 2026-04-24

### 신규
- **SkillIntroAgent.cs** — Phase 3 ML-Agent (19 obs, 2 Branch: 이동 4 + 스킬 4)
  - 전진/좌회전/우회전 이동 (Phase 1/2 동일)
  - 스킬 시전: B1 = 없음/슬롯0/1/2, Action Masking으로 미해금+쿨다운 차단
  - 보상: 시간 압박, 스킬 적중, 진행(거리 감소), 정렬(dot), 정지 패널티
  - 에피소드 경계: StatManager.SetCasting(false) + EndParryWindow() + StateManager.ForceReset()
- **TrainingSkillManager.cs** — 학습 전용 점진적 스킬 해금 관리
  - 에피소드 시작 1개 → 15초마다 1개 추가 → 최대 3개
  - BossObservationCollector와 동기화 (SetUnlockedSlotCount, SetBossSkill)
- **SkillPoolSO.cs** — 스킬 풀 ScriptableObject (스킬 목록 + 원거리 분류)

### 보완
- **BossController.cs**
  - _trainingMode 플래그 + TrainingMode 프로퍼티 (setter에서 NavMeshAgent 비활성 + 상태 리셋)
  - ApplyTrainingModeToAgent() 분리 (setter + Start() 양쪽 호출)
  - Update() 학습/비학습 분기 (학습 시 HandleActions() + NavAgent 제어 스킵)
  - FindNearestPlayer() private → public
- **BossObservationCollector.cs**
  - Phase3Size 18→19, Phase4Size 24→25, Phase5Size 33→34
  - #18: unlockedSlotCount / maxSlots 관측값 추가
  - SetUnlockedSlotCount(), SetBossSkill() 공개 API 추가
- **SkillManager.cs**
  - _roundRobinEnabled + _roundRobinStart 필드 추가
  - Update() 라운드 로빈 루프 (Execute 성공 시에만 break + 시작 인덱스 순환)
  - RoundRobinEnabled 프로퍼티 추가

### Phase 3 관측/보상 재설계 (Round 3 승인)
- **BossObservationCollector.cs** — Phase3Size 19→22 (+3: 슬롯별 스킬 Range, Clamp01 정규화)
  - Phase4Size 25→28, Phase5Size 34→37 연쇄 조정
- **SkillIntroAgent.cs** — 보상 체계 전면 수정
  - 시전 보상 0.3→0.05 (소보상), 실제 HP 감소 ×0.5 (주보상)
  - Shield 감소 ×0.3 (보조), TotalHitCount 증가 ×0.2 (보조)
  - 범위 밖 시전 -0.1 (TargetType.Self 제외)
  - 에피소드 시작: 투사체/장판 풀 ReturnAll() + SkillExecutor.ResetAll()
- **SkillExecutor.cs** — TotalHitCount 프로퍼티 + ResetAll() (쿨다운 포함 전체 초기화)
- **ProjectilePool.cs** — ReturnAll() 추가 (활성 투사체 풀 반환)
- **PersistentAreaPool.cs** — ReturnAll() 추가 (활성 장판 풀 반환)

### 이동 프리트레인 분리 + 인스펙터 튜닝 확장 (Codex 승인)
- **TrainingSkillManager.cs** — `_initialUnlockCount` SerializeField 추가 (기본값 1)
  - `ResetForEpisode()`에서 `_initialUnlockCount`만큼 해금 (0이면 스킬 없음=이동 전용)
- **SkillIntroAgent.cs** — 벽 충돌 패널티 하드코딩 → `_wWallHitPenalty`(0.05), `_wWallStayPenalty`(0.01)로 SerializeField 추출
- **Chapter1.unity** — MaxStep: 0→8000, 직렬화 필드 현재 코드 동기화
- **SkillIntro_Move_config.yaml** — 이동 프리트레인용 (initialize-from 없음)
- **SkillIntro_config.yaml** — 스킬 학습용 (initialize-from: SkillIntro_Move)

### 더블 터치 시스템 통합 + 관측값 24 확장 (Codex 3차 승인)
**변경일:** 2026-04-25

- **SkillIntroAgent.cs** — DualTargetAgent 검증 로직 통합
  - 관측값 22→24 (p1Touched, p2Touched 추가)
  - 더블 터치: _proximityRadius(5m) 내 진입 시 터치 판정, 양쪽 터치 시 성공 종료
  - ActiveIsP1(): P1 터치 → P2 추적, P2 터치 → P1 추적, 양쪽/미터치 → nearest
  - 보상: 터치 +0.2, 빠른 더블터치 +0.3 (3초 이내), 시간초과 터치 없음 -0.5, 부분 터치 -0.2
  - 진행/정렬/스킬 시전 전부 ActiveTarget 기준으로 전환
- **TrainingSkillManager.cs** — `_maxUnlockCount` SerializeField 추가 (Tick()에서 cap 제한)
- **BossController.cs** — ApplyTrainingModeToAgent()에서 agent.enabled = false (NavMesh 완전 비활성)
- **Chapter1.unity** — VectorObservationSize: 22→24, DualTargetAgent 컴포넌트 완전 제거
- **SkillIntro_config.yaml** — initialize_from: SkillIntro_Move 추가

### HP=0 사망 기반 에피소드 종료 + 학습 리셋 (Codex 수정→반영)
**변경일:** 2026-04-25

- **StatManager.cs** — `ResetForTraining()` 메서드 추가
  - 상태이상/버프/디버프 코루틴 전체 정지 + Dictionary 초기화
  - runtimeStats를 baseStats 원본으로 복원, HP 풀회복, _isAlive=true 복원
- **SkillIntroAgent.cs** — 사망 종료 조건 + 에피소드 리셋 보강
  - CheckTermination() 우선순위: 보스 사망(-1.0) → 양쪽 사망(+1.0) → 단일 사망(+0.3, 계속) → 맵 이탈 → 시간 초과
  - ActiveIsP1(): 사망자 제외 → 이동/정렬/스킬 보상 전부 생존자 기준
  - GetActiveTarget(): 양쪽 사망 시 null 반환
  - CheckProximityTouch(): 사망자 근접 판정 스킵
  - _p1DeathHandled/_p2DeathHandled 플래그로 킬 보상 1회 보장 + 자동 터치
  - ApplyStepRewards(): 양쪽 사망 시 진행/정렬 보상 스킵
  - ResetEpisodeState(): 보스 + P1/P2 StatManager.ResetForTraining() + StateManager.ForceReset()
  - PlaceOnSpawn(): Rigidbody velocity 초기화 + StopAllCoroutines (밀림 버그 수정)
  - SerializeField 추가: _bossDiedPenalty(1.0), _allKilledReward(1.0), _playerKilledReward(0.3)

### 플레이어 스킬 분배 + 관측값 55 확장 (Codex 수정→반영)
**변경일:** 2026-04-26

- **SkillIntroAgent.cs** — 플레이어 스킬 분배 및 관측값 대폭 확장
  - 관측값 30→55: Phase3(28) + touch(2) + 플레이어스킬(18) + 추가(7)
  - P1/P2 TrainingSkillManager 참조 추가 (에피소드 리셋/Tick 호출)
  - CollectPlayerSkillObs(): 플레이어당 범위×3 + 쿨다운비율×3 + 타겟타입×3 = 9×2 = 18
  - CollectExtraObs(): P1/P2 캐스팅(2) + 이동속도(2) + 해금수(2) + 보스 버스트DMG(1) = 7
  - 사망 플레이어 관측 → 전부 0 (stale signal 방지, Codex 피드백)
  - ResetPlayer(): SkillExecutor.ResetAll() 추가 (쿨다운 이월 방지, Codex 피드백)
  - OnActionReceived(): 생존 플레이어만 TrainingSkillManager.Tick() 호출
  - CheckTermination(): Debug.Log 추가 (P1/P2 사망, 에피소드 종료 조건별)
  - 미스 패널티(_wMissPenalty=0.05), 시전 보상 제거(_wFireReward=0)
  - FreezePlayer/UnfreezePlayer: 사망 시 NavMesh 정지 + Rigidbody kinematic
  - 터치 후 행동: 먼 플레이어 타겟 + 스킬 범위 유지 보상(_wRangeMaintain)
- **BossObservationCollector.cs** — Phase3Size 22→28 (스킬 총쿨×3 + 타겟타입×3 추가)
- **BossSkillPool.asset** → **TrainingCombatSkillPool.asset** 리네임 (중립 네이밍, Codex 피드백)
- **Chapter1.unity** — VectorObservationSize: 30→55, P1/P2에 TrainingSkillManager 컴포넌트 추가
- **StatManager.cs** — ResetForTraining() 메서드 추가 (코루틴 정리 + runtimeStats 복원)

### Codex 교차 검증
- Phase 3 설계: 5차 검증 승인
- Phase 3 관측/보상 재설계: 3차 검증 승인 (Range Clamp01, Self 스킬 제외, HP+Shield+HitCount 3중 보상, 쿨다운 리셋, 에피소드 경계 투사체/장판 정리)
- 이동 프리트레인 분리: 1차 승인 (0슬롯 obs 안정성 OK, hidden_units:128 적절)
- 더블 터치 시스템: 3차 검증 승인 (hidden state 제거, fallback=nearest, touch reward Inspector 조정)
- 사망 종료 로직: Codex 수정 피드백 3가지 반영 (ActiveIsP1 사망 체크, DeathHandled 플래그, 우선순위 명시)
- 플레이어 스킬 분배: Codex 수정 피드백 — SkillExecutor.ResetAll 필수, 에셋 중립 네이밍, 사망 관측 0 처리
- 관측값 55 확장: Codex 승인 (SkillIntroAgent 직접 수집, 사망 관측 0, 총 55개 PPO 부담 없음)

---

## 보스 관측값 시스템 — BossObservationCollector + PlayerBiasTracker + BossActionWeightCalculator
**경로:** `Assets/Scripts/AI/BossAI/Observation/BossObservationCollector.cs`, `Assets/Scripts/AI/BossAI/Observation/PlayerBiasTracker.cs`, `Assets/Scripts/AI/BossAI/Observation/BossActionWeightCalculator.cs`
**보완:** `BossController.cs`, `SkillExecutor.cs`, `SkillContext.cs`, `SkillComponents.cs`, `SkillProjectile.cs`
**변경일:** 2026-04-24

### 신규
- **BossObservationCollector.cs** — Phase 3+ ML-Agent용 33개 관측값 중앙 수집
  - Phase별 누적 수집: CollectUpToPhase3(18), CollectUpToPhase4(24), CollectUpToPhase5(33)
  - Phase 1/2 에이전트는 기존 코드 유지, Phase 3+부터 P1/P2 고정 정렬 (Phase 2 active/secondary와 의도적 단절)
  - 내부 추적: 버스트 피해(1초 윈도우), 이동속도 EMA, 스킬 이력/명중률
  - recentParryCount(#18)는 패링 시스템 미구현으로 0 고정
- **PlayerBiasTracker.cs** — 9종 플레이어 행동 편향 점수 (0.0~1.0)
  - 이중 시간축: 15~20초 장기 로그 + 0.5초 단기 재계산
  - 이벤트 트리거: NotifyAttack/SkillUse/HitTaken/Evade/Parry/RopeUse
  - 패링/로프 편향은 샘플링 기반 fallback (시스템 미구현 → 자연감쇠로 0 유지)
- **BossActionWeightCalculator.cs** — 9종 보스 행동 가중치 + weighted-random 선택
  - 비적합 게이트: 관련 편향 < 0.05 → 가중치 0
  - 적합 행동 바닥값 0.1, weighted-random 확률 샘플링
  - 반복 방지: 직전 동일 -0.2, 최근 3회 중 2회 -0.35, 직전 2회 동일 추가 -0.15
  - legal mask: 미구현/페이즈 잠금 행동 원천 차단
  - fallback: Σweights ≤ 0 → legal 행동만 균등 분배

### 보완
- **BossController.cs** — `public int CurrentPhase => currentPhase;` 프로퍼티 추가
- **SkillExecutor.cs** — 적중 기록 API 추가
  - RecordAttempt: Execute() 내부에서 시도 기록
  - RecordHit: SkillContext.OnHitRecorded 콜백으로 적중 지점에서 기록 (dedupe 플래그)
  - 외부 조회: GetHitRate, GetUseCount, GetLastNSkillIds, TotalUseCount
- **SkillContext.cs** — `OnHitRecorded` 콜백 + `HitRecorded` dedupe 플래그 추가
- **SkillComponents.cs** — ApplyInArea, DealDirectionalHit에서 anyHit 시 OnHitRecorded 호출
- **SkillProjectile.cs** — ApplyHit에서 OnHitRecorded 호출

### Codex 교차 검증
- 5차 검증 승인 (RecordHit dedupe, legal mask fallback, 비적합 게이트+바닥값 0.1)

---

## 전투 봇 AI — 스킬 포지셔닝 통합 (신규)
**경로:** `Assets/Scripts/AI/Player/Combat/PlayerBotCombatInit.cs`, `Assets/Scripts/Skill/Core/SkillDistributor.cs`, `Assets/Scripts/AI/Player/DoubleMove/LookAtBossAction.cs`
**변경일:** 2026-04-23

### 신규
- **PlayerBotCombatInit.cs** — 전투 봇 Blackboard 초기화 컴포넌트
  - BehaviorGraphAgent를 Awake()에서 비활성화, Start()에서 값 주입 후 활성화 (초기화 보장)
  - Boss/Agent/Ally/Self + 거리 파라미터 8종(DangerRange, OptimalMin, OptimalMax, FleeDistance, FlankRadius, StrafeRadius, StrafeAngleStep, MinSpacing) Blackboard 주입
  - 방어 코드: _behaviorAgent, bb, _navAgent, _ally null 시 경고 로그
- **SkillDistributor.cs** — 중앙 스킬 슬롯 분배기 (플레이어 전용)
  - SkillLoadout(nested class): Name, Target(SkillManager), Skills(SkillDefinition[])
  - Start()에서 ClearAll() + SetSlot() 호출
  - 범위: P1/P2 플레이어만. 보스는 SkillManager 일반화(ICombatant) 후 별도 진행
- **LookAtBossAction.cs** — Behavior Graph 액션 노드 (보스 방향 회전)
  - 각 이동 분기의 SetNavDestination 뒤에서 Slerp로 보스 방향 회전
  - NavMeshAgent.updateRotation = false 설정
  - 사유: 스킬이 transform.forward 기반이므로 이동 후 보스를 향해야 적중

### Codex 교차 검증
- 5차 검증 승인 (PlayerBotCombatInit + Blackboard 주입 구조)
- 7차 검증 승인 (SkillDistributor 플레이어 전용 + FaceTarget 대체)

---

## SkillProjectile 타격 판정을 Trigger → 위치 기반으로 변경 (수정)
**경로:** `Assets/Scripts/Skill/Projectile/SkillProjectile.cs`
**변경일:** 2026-04-23

### 수정
- **OnTriggerEnter 제거** → FixedUpdate에서 `Physics.OverlapSphereNonAlloc`로 위치 기반 적중 판정
  - 사유: 서버 권위(Host-authoritative) 전환 준비. 다른 스킬(DealDirectionalHit, ApplyInArea, PersistentArea)과 판정 방식 통일
- **OverlapSphereNonAlloc + static buffer** — GC 할당 방지 (투사체는 매 틱 반복이므로)
- **FixedUpdate 사용** — Rigidbody 이동과 동일한 물리 틱에서 판정 (프레임레이트 비의존)
- **ShouldRunHitDetection() 가드** — 서버 전환 시 IsServer/IsHost 체크로 교체할 지점 분리
- **ApplyHit() 분리** — 서버 전환 시 RPC 발송 지점으로 활용
- **LayerMask _targetMask 추가** — 불필요한 콜라이더 탐색 감소
- Collider/RequireComponent 유지 — 프리팹 호환성 보존

---

## CrushingBarrage onMiss 무조건 타격 버그 수정 (버그수정)
**경로:** `Assets/Scripts/Skill/Core/SkillLibrary.cs`
**변경일:** 2026-04-22

### 버그 수정
- **CrushingBarrage() / CrushingBarrage_Boss()** — onMiss 핸들러에서 DealMultiHitDamage가 2차 DealDirectionalHit 적중 여부와 무관하게 무조건 실행되는 버그 수정
  - 사유: 2차 cone도 miss인데 ctx.PrimaryTarget이 복원되어 다단히트(34×4=136)가 방향 무관 적용됨
  - 수정: onMiss 내부 DealMultiHitDamage를 TriggerOnHit(onHit:)으로 감싸서 2차 cone 적중 시에만 발동

---

## RangeDisplay 형태별 분기 + 부채꼴 메쉬 + 타겟 탐색 수정 (수정/버그수정)
**경로:** `Assets/Scripts/Skill/Core/SkillRangeDisplay.cs`, `Assets/Scripts/Skill/Core/SkillComponents.cs`, `Assets/Scripts/Skill/Core/SkillManager.cs`, `Assets/Scripts/Skill/Prefab/RangeIndicatorMat.mat`
**변경일:** 2026-04-22

### 버그 수정
- **SkillManager.FindNearestTarget()** — `bossObj.GetComponent<ICombatant>()` → `GetComponentInChildren<ICombatant>()`
  - 사유: `_gameManager.Bosses`에 등록된 GameObject에서 ICombatant가 자식에 있으면 탐색 실패 → 모든 스킬 "타겟 없음"
- **RangeIndicatorMat.mat** — `_ALPHAPREMULTIPLY_ON` → `_EMISSION` 키워드 교체, `_SrcBlend: 1` → `5`(SrcAlpha)
  - 사유: URP Unlit→Lit 전환 시 알파 블렌딩 미작동 → 인디케이터 화면에 안 보임

### 수정
- **SkillRangeDisplay.cs** — 전면 개선
  - 부채꼴 런타임 메쉬 생성 (`BuildFanMesh`) — Cone 형태를 원형이 아닌 실제 부채꼴로 표시
  - `_cylinderMesh` Awake 시 원본 캐싱 — 풀 반환 후 메쉬 복원 보장
  - Emission 발광 (`_emissionIntensity = 3f`), URP Lit Material 연동
  - 가시성 강화: Y높이 0.15→0.5, 두께 0.15→0.6, 지속시간 1.5→2.5초, 홀드 비율 50%
  - 형광색 적용: Hit 빨강, Miss 노랑, Projectile 초록 (알파 1.0)
- **SkillComponents.cs ApplyInArea** — 형태별 RangeDisplay 분기
  - 기존: 모든 형태에 `ShowCircle`만 호출
  - 변경: Circle→ShowCircle, Cone→ShowCone, Line→ShowLine
- **SkillComponents.cs SpawnPersistentArea** — RangeDisplay 호출 추가
  - 침식 장판 등 장판 생성 시 범위 표시 누락 해결

### Codex 교차 검증
- 승인 완료. 추후 고려사항: Line 장판 분기 추가, Cone 메쉬 캐시 Dictionary화 (현재는 단일 각도 캐싱)

---

## 2차 튜닝 — SO Range ×3 + SkillLibrary 이펙트 ×1.5 + RangeDisplay 시각 ×2.5 (수정)
**경로:** `Assets/ScriptableObjects/Skills/*.asset` (19개), `Assets/Editor/SkillDefinitionGenerator.cs`, `Assets/Scripts/Skill/Core/SkillLibrary.cs`, `Assets/Scripts/Skill/Core/SkillRangeDisplay.cs`
**변경일:** 2026-04-21

### 수정
- **SO Range ×3 확대** — 자동 시전 거리 재조정 (유저 실 테스트 기반)
  - 근접 방향타격: 8m → 24m
  - 자기중심 광역: 9m → 27m
  - Cone(MarkWave_Boss): 11m → 33m
  - 투사체/원거리: 22m → 66m
  - 장판 배치형: 14m → 42m
  - Self 타입: 0m 유지
- **SkillDefinitionGenerator.cs** — range 파라미터 SO ×3 값과 동기화
- **SkillLibrary.cs 이펙트 범위 ×1.5 확대**
  - DealDirectionalHit 좁은: 4.5~5.5m → 6.8~8.3m, 광역: 5.5~6.8m → 8.3~10.2m
  - ApplyInArea 좁은: 3.5~4.0m → 5.3~6.0m, 광역: 7.2~7.9m → 10.8~11.9m, Cone: 9.0m → 13.5m
  - SpawnPersistentArea 내부: 2.5~3.0m → 3.8~4.5m, 외부: 6.8~7.4m → 10.2~11.1m
  - LaunchProjectile range: 14~20m → 21~30m, speed: 16~21 → 19~25
- **SkillRangeDisplay.cs** — `_visualScale = 2.5f` 필드 추가, Circle/Cone/Line 인디케이터 스케일에 적용
  - 사유: 유저 육안 테스트에서 인디케이터가 식별 불가 수준으로 작았음 (시각적 크기 지정, 기획 변경 아님)

### Codex 교차 검증
- 2차 튜닝 승인 완료

---

## 스킬 범위 확장 + ICombatant 부모 탐색 + 중복 타격 방지 (수정)
**경로:** `Assets/Scripts/Skill/Core/SkillComponents.cs`, `Assets/Scripts/Skill/Projectile/SkillProjectile.cs`, `Assets/Scripts/Skill/Area/SkillArea.cs`, `Assets/Scripts/Skill/Core/SkillLibrary.cs`, `Assets/Editor/SkillDefinitionGenerator.cs`, `Assets/ScriptableObjects/Skills/*.asset` (23개)
**변경일:** 2026-04-20

### 수정
- **ICombatant 탐색 통일 (4곳)** — `GetComponent<ICombatant>` / `TryGetComponent<ICombatant>` → `GetComponentInParent<ICombatant>` 변경
  - `SkillComponents.cs` DealDirectionalHit (line 100), ApplyInArea (line 400)
  - `SkillArea.cs` TickArea (line 111)
  - `SkillProjectile.cs` OnTriggerEnter (line 65)
  - 사유: 보스의 Collider가 자식 오브젝트에 있으면 부모의 BossController(ICombatant)를 못 찾던 문제 해결
- **중복 타격 방지** — 위 4곳 모두 `HashSet<ICombatant>`로 동일 대상 1회만 처리
  - `SkillProjectile`은 인스턴스 필드 `_hitTargets` 추가 (pierce 관통 투사체 대응), OnSpawn/OnDespawn에서 Clear
- **SO Range 스킬군별 재조정** — 40m 일괄 → 스킬 유형별 적정 거리
  - 근접 방향타격 (ExecutionSpike, CrushingBarrage, FortressArmor + Boss 3종): 8m
  - 자기중심 광역 (CollapseRoar + Boss): 9m
  - 자기중심 Cone (MarkWave_Boss): 11m
  - 투사체/원거리 (HuntingMark, SealChain, BarrierBreaker, PiercingShot, RuptureMagazine, BarrierBreaker_Boss): 22m
  - 장판 배치형 (ErosionField + Boss, SealChain_Boss, RuptureMagazine_Boss): 14m
  - Self 타입 (SurvivalPulse, OverchargeMode + Boss): 0m (Generator와 일치)
- **SkillDefinitionGenerator.cs** — range 파라미터를 SO Range와 동일하게 동기화 (재생성 시 덮어쓰기 방지)
- **SkillLibrary.cs 이펙트 범위 차등 확장** — 부품 유형별 배율 적용
  - DealDirectionalHit: ×2.5 (좁은 4.5~5.5m, 광역 재시도 5.5~6.8m)
  - ApplyInArea 좁은: ×2.5 (3.5~4.0m), 광역/Cone: ×1.8 (7.2~9.0m)
  - SpawnPersistentArea 내부: ×2.5 (2.5~3.0m), 외부: ×2.0 (6.8~7.4m)
  - LaunchProjectile range: ×2.0 (14~20m), speed: ×1.3 (16~21)
  - 데미지/상태이상/각도는 변경 없음

### Codex 교차 검증
- 4차 검증 후 승인 (1차: 일괄 4.5배 거부 → 2차: 부품별 차등 + 원인 분리 → 3차: 중복 타격 방지 추가 → 4차: 최종 승인)

---

## 스킬 범위 표시 가시성 강화 + 자동 시전 거리 40m 확장 (수정)
**경로:** `Assets/Scripts/Skill/Core/SkillRangeDisplay.cs`, `Assets/ScriptableObjects/Skills/*.asset` (23개 전체)
**변경일:** 2026-04-18

### 수정
- **SkillRangeDisplay** — 가시성 대폭 강화
  - Y 높이 0.05→0.15, 두께 0.02→0.15, Line 폭 0.6→1.0
  - 알파값 강화 (HIT 0.85, MISS 0.7, Projectile 0.8)
  - 지속시간 0.8s→1.5s
- **전체 스킬 SO Range** — 2.2~11m → 40m 일괄 변경 (자동 시전 거리 확장)

---

## 스킬 범위 시각 표시 + 디버그 로그 추가 (신규/수정)
**경로:** `Assets/Scripts/Skill/Core/SkillRangeDisplay.cs` (신규), `Assets/Scripts/Skill/Core/SkillComponents.cs`, `Assets/Scripts/Skill/Core/SkillExecutor.cs`, `Assets/Scripts/Skill/Core/SkillManager.cs`, `Assets/Scripts/Stats/StatManager.cs`
**변경일:** 2026-04-18

### 신규
- **SkillRangeDisplay** — 스킬 범위 시각 표시 싱글톤 (프리팹 기반, URP 호환)
  - 런타임 셰이더 생성 → 프리팹 풀링 방식으로 변경 (PersistentAreaPool 패턴)
  - `MaterialPropertyBlock`으로 색상 제어 — Material 인스턴스 누수 없음
  - Inspector에 `_indicatorPrefab` 슬롯 — URP 투명 Material 적용된 Cylinder 프리팹 드래그
  - `_logRange` 토글 — 범위 표시 시 콘솔 로그 출력
  - `DealDirectionalHit` → 주황색 콘 (HIT/MISS 색상 구분)
  - `ApplyInArea` → 주황색 원형 (HIT/MISS 색상 구분)
  - `LaunchProjectile` → 파란색 직선 (발사 경로)
  - Inspector `Show Range` 해제로 비활성화 가능
### 수정
- **SkillExecutor** — `_logExecution` 토글 추가. 스킬 실행 시 시전자/대상/HP/쿨타임/실행 로그 출력
- **SkillManager** — `_logAutoCast` 토글 추가 + CanCast 실패 사유 경고 로그
- **StatManager** — `_logCombat` 토글 + Inspector 실시간 모니터 (HP/Shield/배율/상태이상 표시)
- **SkillComponents** — DealDirectionalHit/ApplyInArea/LaunchProjectile에 SkillRangeDisplay 호출 추가

---

## 스킬 시스템 버그 수정 2차 — CrushingBarrage / 조건형 쿨타임 / SkillArea HitLanded (수정)
**경로:** `Assets/Scripts/Skill/Core/SkillLibrary.cs`, `Assets/Scripts/Skill/Core/SkillDefinition.cs`, `Assets/Scripts/Skill/Core/SkillExecutor.cs`, `Assets/Scripts/Skill/Core/SkillManager.cs`, `Assets/Scripts/Skill/Core/SkillBinder.cs`, `Assets/Scripts/Skill/Area/SkillArea.cs`
**변경일:** 2026-04-17

### 수정
- **CrushingBarrage (플레이어/보스)** — `DealMultiHitDamage` 가 `DealDirectionalHit` MISS 시에도 무조건 실행
  - 수정 전: MISS → 멀티히트 + 광역 재시도 + 광역 멀티히트 = 이중 데미지
  - 수정 후: 멀티히트를 `TriggerOnHit(onHit:)` 내부로 이동
- **OverchargeMode / SurvivalPulse (플레이어/보스)** — 조건 미충족 시에도 쿨타임 소모
  - `SkillDefinition.RuntimeCondition` 필드 추가 (실행 전 조건, null = 항상 실행)
  - `SkillExecutor.Execute` — RuntimeCondition false 시 쿨타임 미기록 + false 반환
  - `SkillManager.CanCast` — RuntimeCondition 검사 추가 (다음 슬롯 시도 가능)
  - `SkillBinder.Bind` — `SkillCondition condition` 옵션 파라미터 추가
  - `SkillLibrary` — 4종 Condition 메서드 추가 (SurvivalPulse/OverchargeMode × 플레이어/보스)
- **SkillArea.TickArea** — `ctx.HitLanded` 미저장/미복원 → 틱 이펙트가 HitLanded 수정 시 오염
  - `PrimaryTarget` 과 동일하게 save/restore 패턴 적용

---

## 스킬 시스템 버그 수정 — ApplyInArea HitLanded + 투사체 이중발사 (수정)
**경로:** `Assets/Scripts/Skill/Core/SkillComponents.cs`, `Assets/Scripts/Skill/Core/SkillLibrary.cs`
**변경일:** 2026-04-17

### 수정
- **ApplyInArea** — `ctx.HitLanded` 미기록 버그 수정
  - 범위 내 타겟 1명 이상 적중 시 `HitLanded = true` 설정
  - 영향: 붕괴 포효(플레이어/보스)의 좁은범위→광역 분기가 정상 작동
  - 수정 전: 항상 이중 발동 (좁은범위 + 광역 둘 다 실행)
- **관통 저격 / 파열 탄창** — 투사체(비동기) + TriggerOnHit(동기) 타이밍 충돌로 항상 2발 발사
  - 약점 판정(NoCoreHit) 시스템 구현 전까지 재발사 분기 제거, 1발만 발사
- 보스 공용 주석 "12종" → "11종" 수정 (실제 구현 수량 반영)

---

## 스킬 SO 자동 생성 + 부트스트랩 (신규)
**경로:** `Assets/Editor/SkillDefinitionGenerator.cs`, `Assets/Scripts/Skill/Core/SkillBootstrap.cs`
**변경일:** 2026-04-17

### 신규
- **SkillDefinitionGenerator** (에디터 스크립트)
  - 메뉴: `Tenebris > 스킬 SO 전체 생성 (공용 12종 + 보스 12종)`
  - 23종 SkillDefinition SO 자동 생성 (`Assets/ScriptableObjects/Skills/`)
  - SkillRegistry SO 자동 생성 + 전체 스킬 풀 등록
  - 재실행 시 기존 SO 메타데이터 갱신 (중복 생성 없음)
- **SkillBootstrap** (MonoBehaviour)
  - 씬에 배치 → SkillRegistry 연결
  - Awake 시 `SkillBinder.BindAll(registry)` 호출 → 모든 SO에 RuntimeStep 주입

---

## DualTargetAgent Phase 2b — 추격 품질 향상 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 변경
- Phase 2a(순차 추격) 기반 위에 추격 품질 요소 추가:
  - `_wAlign = 0.003` — active 타겟을 바라보며 접근 시 보상 (dot > 0.7)
  - `_wIdlePenalty = 0.005` — 정지/회전만 시 패널티 (위치 변화 < 0.05)
  - `_wStepPenalty` 0.002 → 0.005 — 시간 압박 강화
- 관측값 11개 변경 없음 → `--resume` 으로 이어서 학습 가능
- DualTarget 학습 결과 → `results/DualTarget_backup/` 에 백업

---

## DualTargetAgent 순차 추격 학습 — secondary 보상 제거 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 문제
active + secondary 동시 progress 보상 → 두 힘의 균형점인 가운데로 가서 빙글빙글 도는 local optimum.

### 변경
- **`_wSecondaryProgress` 제거** — secondary 방향 보상이 가운데로 끌던 원인
- active 하나만 쫓다가 근접하면 → `ActiveIsP1()` 이 자동으로 나머지를 active로 전환 → 다음 것 추격
- 관련 필드/변수 정리: `_prevSecondaryDist`, `secondaryDist` 제거
- 최종 보상 구조 (2개만):
  - `_wStepPenalty = 0.002` (가벼운 시간 압박)
  - `_wProgress = 0.50` (active 거리 감소만)
  - 근접 성공 보너스: 0.5 × 2 + FAST 0.5

---

## DualTargetAgent 근접 거리 종료 + 정적 보상 제거 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 문제
학습 중간부터 보스가 두 플레이어를 바라보면서 그 자리에서 빙글빙글 회전만 하고 접촉을 시도하지 않음. 정적 보상(closeness/midpoint/align)을 챙기는 local optimum에 갇힘.

### 변경
- **종료 조건 변경**: trigger collision → **근접 거리 진입(`_proximityRadius = 3.0f`)**
  - `OnTriggerEnter` 의 Player 처리 블록 전체 제거
  - `ApplyStepRewards` 안에 `CheckProximityTouch(distP1, distP2)` 매 스텝 호출
  - 양쪽 다 근접 진입 → `_successCount++` + `EndEpisode()`
- **정적 보상 전부 제거** (빙글빙글 방지):
  - `_wTargetCloseness`   0.005 → **0**
  - `_wMidpointCloseness` 0.002 → **0**
  - `_wAlign`             0.001 → **0**
- **진행/패널티 보상 강화** (실제 이동 유도):
  - `_wProgress`          0.30  → **0.50**
  - `_wSecondaryProgress` 0.10  → **0.20**
  - `_wIdlePenalty`       0.005 → **0.015**
  - `_wStepPenalty`       0.005 → **0.01**
- 미사용 필드 정리: `_alignDotThreshold`, `_targetSwitchReward` 제거
- 로그/주석 "접촉" → "근접" 으로 변경

### 효과 예측
- 가만히 회전만 하면: 시간 압박(−0.01) + 정지 패널티(−0.015) = 매 스텝 −0.025 누적
- 실제 거리 좁히면: progress 보상이 압도적으로 커서 이동 학습 강제
- 양쪽 한번씩 근접하기만 하면 종료 → 접촉(trigger) 정확도 의존성 제거

---

## DualTargetAgent 양쪽 균형 + 성공 카운터 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 변경
- **`_wSecondaryProgress = 0.10f`** 신규 — secondary 거리 감소도 보상
  - active 진행 보상의 1/3 가중치
  - secondary 가 이미 접촉됐으면 적용 안 함
  - 효과: 한 명에게만 직진 → 양쪽 모두 압박하는 경로 학습
- **`_wMidpointCloseness`** 0.001 → **0.002** (살짝 회복)
- 성공 카운터 신규: `_successCount`, `_fastSuccessCount`, `_slowSuccessCount`
- `OnTriggerEnter` 양쪽 접촉 성공 시 카운터 증가 + 누적 횟수 로그
- 성공 시 보상 명세:
  - 첫 접촉 +0.5, 두 번째 접촉 +0.5, FAST(gap≤3s) 보너스 +0.5
  - 누적: 1.0(SLOW) ~ 1.5(FAST)
- 로그 포맷: `[DualTarget] ✅ FAST 성공 #N (FAST=A / SLOW=B) gap=X.XXs reward=Y.YY`

---

## DualTargetAgent 진동/정지 회전 spam 해결 — 관측 정렬 + 보상 재설계 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 문제
- 보스가 두 타겟을 번갈아 보면서 회전만 함 (이동 학습 안 됨)
- 원인 ① 관측이 P1/P2 고정 슬롯 → 정책이 두 타겟에 균등 반응 (진동 학습)
- 원인 ② 회전·정지 비용이 0에 가까움 → activeTarget 보상만으로 가만히 있어도 양수 누적
- 원인 ③ 진행 보상 가중치 0.05 — 너무 약해서 위치 보상이 압도

### 변경 — 관측 재설계 (P1/P2 → Active/Secondary 정렬)
- `ActiveIsP1(distP1, distP2)` 헬퍼 추가 (관측/보상 공통 사용)
- 관측 11개 슬롯을 활성/2차 타겟 우선순위로 정렬:
  - [0~3] activeTarget (방향x, 방향z, 거리, 정렬도)
  - [4~7] secondaryTarget (방향x, 방향z, 거리, 정렬도)
  - [8]   두 플레이어 사이 거리
  - [9]   secondaryTouched 플래그
  - [10]  에피소드 시간 진행도 (긴박감 신호 — 신규)
- 효과: 정책이 "active 슬롯은 항상 추격 대상"이라는 일관성 학습 → 진동 제거

### 변경 — 보상 재설계 (정지/회전 spam 제거)
| 가중치 | 기존 | 신규 | 변화 |
|--------|------|------|------|
| `_wStepPenalty` (시간 압박) | 0.002 | **0.005** | 2.5× 강화 |
| `_wTargetCloseness` (근접) | 0.015 | **0.005** | 1/3 감소 |
| `_wMidpointCloseness` (중점) | 0.008 | **0.001** | 거의 제거 |
| `_wProgress` (진행, 메인) | 0.05  | **0.30**  | **6× 강화** |
| `_wAlign` (정렬) | 0.005 | **0.001** | 1/5 감소 (회전 spam 방지) |
| `_wIdlePenalty` (정지) | (없음) | **0.005** | 신규 |
| `_minMoveDelta` (정지 임계) | (없음) | **0.05** | 신규 |

### 변경 — 정지 패널티 신규
- `_prevBossPos`, `_prevPosValid` 필드 추가
- 매 스텝 `Distance(현재, 이전) < _minMoveDelta` 면 `−_wIdlePenalty` 부과
- 효과: 가만히 회전만 하면 매 스텝 −0.01 (스텝 + 정지) 손실 → 반드시 직진해야 양수

### 학습 영향
- "두 타겟 중간에서 회전만" 지역 최적점 제거
- 진행 보상이 메인 신호 → "다가가기" 행동에 강한 보상
- 처음부터 학습 권장 (`--initialize-from` 제거, `--force` 사용)

---

## DualTargetAgent 시간 하드 컷오프 단순화 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-17

### 변경
- 기존 단계별 타임아웃(`_noTouchTimeoutSec`/`_firstTouchTimeoutSec`) 제거
- `_episodeMaxDurationSec` 단일 하드 컷오프로 통합 (기본 30s)
- 시간 종료 시 접촉 상태별 차등 패널티:
  - 둘 다 미접촉 → `_noTouchPenalty (-0.5)` (마젠타)
  - 한쪽만 접촉 → `_partialTouchPenalty (-0.2)` (검정)
  - 양쪽 접촉(비정상) → 0 (흰색)

---

## DualTargetAgent 첫 접촉 자체 타임아웃 추가 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-16

### 문제
- 기존 종료 조건이 첫 접촉을 전제로 함 (XOR 한쪽만 접촉 시) → **둘 다 한 번도 안 만지면 영원히 안 끝남**
- Agent.MaxStep 기본값 0(무한)이라 글로벌 컷오프도 없음

### 변경
- `_noTouchTimeoutSec = 15f`, `_noTouchFailPenalty = -0.5f` 필드 추가
- `_episodeStartTime` 필드 추가, `ResetEpisodeState()` 에서 `Time.time` 기록
- `CheckTerminationConditions()` 에 신규 케이스: 둘 다 미접촉 + 15초 경과 → 패널티 후 EndEpisode (마젠타 색)

### 종료 경로 (최종)
1. ✅ 성공 — 양쪽 접촉 완료 (`OnTriggerEnter`)
2. ❌ 맵 밖 낙하 (Y < -5)
3. ❌ **첫 접촉 자체 타임아웃 (15초)** — 신규
4. ❌ 첫 접촉 후 두 번째 접촉 실패 (5초)

---

## DualTargetAgent 보상 구조 재설계 — "중점 압박 + 단일 집중" 하이브리드 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-16

### 배경
- 플레이어 AI는 카이팅(거리 유지) 전략 — 보스 속도 = 플레이어 속도일 때 단일 추격은 수학적으로 불가능
- 기하학적 최적: 두 플레이어의 **중점**에 위치 → 양쪽 도망 방향 차단 → MinSpacing 제약으로 분산도 한계
- 한쪽 접촉 후엔 단일 타겟 집중이 자연스러움

### 변경
- **activeTarget 결정 로직** — 안 만진 쪽 우선, 둘 다 미접촉이면 더 가까운 쪽
- **하이브리드 보상**:
  - activeTarget 근접: `_wTargetCloseness = 0.015f` (주 압박)
  - 중점 근접: `_wMidpointCloseness = 0.008f` (둘 다 미접촉 시만)
  - 거리 감소 진행: `(prevDist − currDist) * _wProgress(0.05f)`
  - activeTarget 정렬: `_wAlign = 0.005f`
- 매 스텝 패널티 −0.001 → **−0.002** (지연 페널티 2배)
- **타겟 전환 보상 제거** (`_targetSwitchReward` 필드는 호환성 유지, 미사용)
- `_prevTargetDist` 필드 추가, `OnTriggerEnter`/`ResetEpisodeState()` 에서 −1f 리셋
- 4개 가중치를 `[Header("위치 전략 가중치")]` 로 Inspector 노출 (튜닝 용이)

### 학습 영향
- **둘 다 미접촉 단계**: 보스가 두 플레이어 사이로 진입 → 압박 → 가까운 쪽 직진
- **한쪽 접촉 후**: 안 만진 쪽 단일 추격 (Phase 1 BasicMove 정책 재활용)
- 중점 가중치(0.008)가 단일 타겟 가중치(0.015)의 약 절반 — 중점 ≠ 종착지, 압박 도구

---

## DualTargetAgent 스폰 로직 개선 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-16

### 변경 사항
- 스폰 시 `localPosition` → **월드 좌표(`position`)** 사용 (스폰포인트 부모 transform 에 영향받던 버그 제거)
- **`PlaceOnSpawn()`** 헬퍼 추가 — 플레이어에 `NavMeshAgent` 가 있으면 `Warp()` 로 NavMesh 상태 재동기화 + `ResetPath()` + `velocity = 0`
- 리스트 2개 미만이면 경고 로그 후 스폰 스킵

### 설정 가이드 (Inspector)
- Boss → DualTargetAgent → `Player Spawn Points` 리스트에 **4개 Transform 드래그**
- 에피소드 시작 시 자동으로 4개 중 서로 다른 2개 선택하여 P1 / P2 배치

---

## DualTargetAgent 관측 거리 확장 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-16

### 변경 사항
- `_maxDistance` : `15f` → **`55f`**

### 이유
- Plane scale 80 (실제 800m × 800m) 맵에서 봇 공전 반경 40m 운용 대응
- 기존 15m 정규화는 40m 밖에서 관측값이 `Clamp01(dist/15) = 1` 로 포화되어 학습 불가
- `OptimalMax(50)` 대비 약간의 여유를 두어 55f 채택

### 연동 Blackboard (참고 — 에디터에서 수동 설정)
```
DangerRange=15, MinSpacing=8, OptimalMin=30, OptimalMax=50,
FleeDistance=20, FlankRadius=35, StrafeRadius=40, StrafeAngleStep=60
NavMeshAgent.Speed=12 (Boss/1P/2P 공통)
```

---

## DualTargetAgent 종료 조건 재설계 (수정)
**경로:** `Assets/Scripts/AI/BossAI/DualTargetAgent.cs`
**변경일:** 2026-04-16

### 이전 문제
- 종료 조건이 "양쪽 접촉" 단 하나뿐 — 한쪽만 접촉한 채 MaxStep까지 방치되어 샘플 효율 저조
- `_targetSwitchReward = 0.08` 너무 커서 좌우 진동 익스플로잇 가능
- `_doubleTouchWindow = 2.0s` 타이트해서 보너스 획득 거의 불가

### 신규 종료 조건
1. **FAST 성공** (초록) — 양쪽 접촉 + 윈도우(3초) 이내 → `+1.5` (0.5 × 2 + 0.5 보너스) + EndEpisode
2. **SLOW 성공** (청록) — 양쪽 접촉 + 윈도우 초과 → `+1.0` (0.5 × 2) + EndEpisode
3. **타임아웃 실패** (검정) — 첫 접촉 후 5초 내 두 번째 실패 → `-0.3` + EndEpisode
4. **맵 이탈 실패** (회색) — Y < -5 낙하 → `-1.0` + EndEpisode
5. **MaxStep 초과** — ML-Agents 프레임워크 자동

### 튜닝 조정
| 필드 | Before | After |
|---|---|---|
| `_doubleTouchWindow` | 2.0s | **3.0s** |
| `_targetSwitchReward` | 0.08 | **0.03** |

### 신규 인스펙터 필드
- `_firstTouchTimeoutSec` = 5.0 (2번째 접촉 제한시간)
- `_timeoutFailPenalty` = -0.3
- `_outOfBoundsY` = -5
- `_outOfBoundsPenalty` = -1.0
- `_fastDoubleTouchBonus` = 0.5

### 신규 메서드
- `CheckTerminationConditions()` — OnActionReceived 매 스텝 호출, 실패 조건 2종 검사

---

## BossController 듀얼 플레이어 대응 (수정)
**경로:** `Assets/Scripts/BossController.cs`
**변경일:** 2026-04-15

### 변경 사항
- `playerTarget` (단일 Transform) 필드 **삭제** — 두 플레이어 환경에서 한쪽만 추적하던 한계 제거
- `Awake()` 에서 `_gameManager = GameManager.Instance` 자동 바인딩 추가
- `TrackPlayer()` 메서드 **삭제** (이미 주석 처리되어 있었고, 이동은 ML-Agents 가 담당)
- `HandleActions()` → `FindNearestPlayer()` 로 가장 가까운 살아있는 플레이어 거리 기반 공격 트리거
- `FindNearestPlayer()` 신규 — `GameManager.Players` 순회, `ICombatant.IsAlive` 필터링

### 호환성
- Inspector 의 `playerTarget` 슬롯은 사용되지 않으므로 무시 가능 (직렬화 데이터만 잔존)
- `EffectAttackRoutine` 360° 회전 이펙트는 그대로 양쪽 플레이어 모두 피격 가능

---

## StateManager 도입 (신규)
**경로:** `Assets/Scripts/State/CombatantState.cs`, `Assets/Scripts/State/StateManager.cs`
**변경일:** 2026-04-13
**설계 문서:** [STATE_MANAGER_DESIGN.md](../STATE_MANAGER_DESIGN.md)

### 신규 파일
- **CombatantState** — 7종 배타 상태 enum (Idle / Moving / Casting / Parrying / HitStun / Stunned / Dead)
- **StateManager** — StatManager 를 읽어 파생 상태 계산. 자체 저장 없음
  - 우선순위 기반 단일 패스 `ComputeState()` — 상위 계층이 하위를 강제 인터럽트
  - `ChangeState()` 단일 관문 — OnStateExited → Enter → Changed 이벤트 보장
  - Capability 플래그: `CanAct / CanMove / CanCast / CanParry`
  - **CanCast = State ∈ {Idle, Moving} && !Silence** — 스킬은 Idle/Moving 에서만 시전 가능
  - 외부 Notify API: `NotifyMovementInput / NotifyCastStart / NotifyCastEnd / NotifyParryStart / NotifyParryEnd`
  - `ForceReset()` — ML 에피소드 리셋용

### PlayerController.cs 연동
- `[RequireComponent(typeof(StateManager))]` 추가
- `_stateManager` 필드 + `StateMgr` 프로퍼티 노출
- `Start()` 에서 `BindOwner(this)` 호출
- `Update()` 순서: StatManager.Tick → HandleMovement → StateManager.Tick
- `HandleMovement()` 이동 입력 시 `NotifyMovementInput(hasInput)` 호출 + `CanMove` 가드

### BossController.cs 연동
- `[RequireComponent(typeof(StateManager))]` 추가
- `_stateManager` 필드 + `StateMgr` 프로퍼티 노출
- `Start()` 에서 `BindOwner(this)` 호출
- `Update()` 에 NavAgent velocity 기반 `NotifyMovementInput` + `agent.isStopped = !CanMove` 연동
- `EffectAttackRoutine` 의 `SetCasting` 직접 호출 → `NotifyCastStart/End` 경유로 교체

### SkillManager.cs 연동
- `_stateManager` 필드 추가 (Awake 자동 바인딩)
- `CanCast()` 의 6개 상태 분기 → `_stateManager.CanCast` 1줄로 축약
- `Update()` 시전 시점에 `NotifyCastStart → Execute → NotifyCastEnd` 순차 호출
  (현재 RuntimeStep 동기 실행 — 캐스팅 타임 도입 시 코루틴화 예정)

---

## SkillManager.cs (디버그/학습 모드 리팩터)
**경로:** `Assets/Scripts/Skill/Core/SkillManager.cs`
**변경일:** 2026-04-13

### 배경
카드 드래프트는 추후로 미루고 Inspector 슬롯 직접 지정 방식으로 단순화.

### 변경
- 슬롯 자료구조 `List<SkillDefinition>` → **고정 크기 `SkillDefinition[5]` + [SerializeField]** 로 교체
  - Inspector 의 "Slots" 배열에 SkillDefinition SO 를 드래그해 슬롯별 스킬 지정
  - `null` 엔트리는 Update 루프가 건너뜀
  - `OnValidate()` 로 Inspector 에서 배열 크기 변경되어도 5 로 강제 복원
- `_maxSlots`, `_preloadSkills`, `Start()`(preload 로직) 제거
- API 교체: `Equip/Unequip/UnequipAll` → **`SetSlot(int, SkillDefinition)`, `ClearSlot(int)`, `ClearAll()`**
  - 카드 드래프트가 슬롯 index 를 명시적으로 지정하도록 구조 변경

### 영향
- **CardManager.cs** — `skillManager.Equip(...)` 제거, 빈 슬롯 탐색 후 `SetSlot(i, skill)` 호출로 변경

---

## SkillLibrary.cs / SkillBinder.cs
**경로:** `Assets/Scripts/Skill/Core/SkillLibrary.cs`, `Assets/Scripts/Skill/Core/SkillBinder.cs`
**변경일:** 2026-04-13

### 배경
기획표 스펙 대조 후 확실 구현 가능한 스킬만 남기고 나머지는 null 반환으로 비활성화.
`RuntimeStep = null` → `SkillDefinition.IsReady = false` → SkillManager 시전 시도 자체 차단.

### SkillLibrary 수정
- **ErosionField** — inner tick 중복 `ApplyDamageOverTime(1,1)` 제거 (스펙에 없음)
- **CounterStance / CounterSlash** — `null` 반환. 사유: 패링 입력 바인딩 부재 → `IsParrying` 영원히 false
- **WireHook / RopeShockwave** — `null` 반환. 사유: RopeLanding 이벤트 추적 시스템 부재
- **CollapseStrike** — `null` 반환. 사유: ParrySuccessRecent 이력 추적 시스템 부재
- **SealChain_Boss** — `null` 반환. 사유: 4방향 멀티 디렉션 투사체 래퍼 부재
- **RuptureMagazine_Boss** — `null` 반환. 사유: 8방향 폭발 패턴 래퍼 부재

### 유지 (동작 검증 완료)
- 플레이어: ExecutionSpike, CrushingBarrage, ErosionField, HuntingMark, SurvivalPulse,
  FortressArmor, SealChain, CollapseRoar, BarrierBreaker, OverchargeMode, PiercingShot,
  RuptureMagazine, ParryEnhance (버프 적용만)
- 보스: ExecutionSpike_Boss, CrushingBarrage_Boss, ErosionField_Boss, SurvivalPulse_Boss,
  FortressArmor_Boss, CollapseRoar_Boss, OverchargeMode_Boss, MarkWave_Boss, BarrierBreaker_Boss

### SkillBinder 수정
- `Bind()` 가 `step == null` 일 때 카운트 제외 → 로그 `[SkillBinder] N종 바인딩 완료` 의 N 이 실제 구현 개수만 반영

---

## StatsManager.cs
**경로:** `Assets/StatsManager.cs`
**변경일:** 2026-03-30

### 변경 전
- `GetPlayerHP()`, `ApplyDamageToPlayer()`, `GetBossHP()`, `ApplyDamageToBoss()`, `GetBossPhase()` 5개 메서드만 존재
- 피해 처리 시 Shield 로직 없음

### 변경 후 (추가된 로직)
- **조회**
  - `GetPlayerMaxHP()`, `GetPlayerHPPercent()`, `GetPlayerShield()`
  - `GetBossMaxHP()`, `GetBossHPPercent()`
  - `GetPlayerMoveControl()`, `GetBossMoveControl()`
  - `HasStatusPlayer(StatusType)`, `HasStatusBoss(StatusType)`

- **피해 / 회복 / 실드**
  - `ApplyDamageToPlayer()` — Shield 우선 소진 로직 추가
  - `TakeShieldBreakDamagePlayer(float amount, float multiplier)`
  - `TakeShieldBreakDamageBoss(float amount, float multiplier)`
  - `RecoverHPPlayer(float amount)` — HealingReceivedMultiplier 반영
  - `RecoverHPBoss(float amount)`
  - `AddShieldPlayer(float amount)` — ShieldMax 클램프

- **상태이상 (Player / Boss 각각)**
  - `ApplyStatusPlayer(StatusType, float duration, float value)`
  - `RemoveStatusesPlayer(CleanseType, int count)`
  - `ApplyStatusBoss(StatusType, float duration, float value)`
  - `RemoveStatusesBoss(CleanseType, int count)`
  - 내부: 코루틴 기반 자동 해제, StunDurationMultiplier / DebuffDurationResistance 반영
  - 내부: HPRegen / DamageOverTime → 1초 틱 루프 처리

- **버프 (Player / Boss 각각)**
  - `ApplyBuffPlayer(BuffType, float duration, float value)`
  - `RemoveBuffsPlayer(DispelType, int count)`
  - `ApplyBuffBoss(BuffType, float duration, float value)`
  - `RemoveBuffsBoss(DispelType, int count)`

- **디버프 (Player / Boss 각각)**
  - `ApplyDebuffPlayer(DebuffType, float duration, float value)`
  - `ApplyDebuffBoss(DebuffType, float duration, float value)`

---

## PlayerController.cs
**경로:** `Assets/Scripts/PlayerController.cs`
**변경일:** 2026-03-30

### 변경 전
- `private float currentHP` / `private float currentShield` 필드를 Controller가 직접 보관
- `TakeDamage()` 내부에서 `currentHP`, `currentShield` 직접 수정
- `RegenerateHP()` 내부에서 `currentHP` 직접 수정
- 이동 속도에 MoveControlMultiplier 미반영
- `ICombatant` 미구현

### 변경 후
- `currentHP`, `currentShield` 필드 **제거**
- `public StatsManager statsManager` 참조 추가
- `ICombatant` 구현 — 모든 스탯 변경은 StatsManager 경유
- `TakeDamage()`, `TakeShieldBreakDamage()` → StatsManager 위임
- `RecoverHP()`, `AddShield()` → StatsManager 위임
- `ApplyStatus/Buff/Debuff/RemoveStatuses/RemoveBuffs` → StatsManager 위임
- `Knockback()` → Rigidbody.AddForce(Impulse)
- `Pull()`, `MoveBy()` → 코루틴 기반 Lerp 이동
- `NotifyParryReward()` → Invulnerable / Buff 구현, Counter/HitStun 추후
- 이동 속도 → `playerStats.MoveSpeed * statsManager.GetPlayerMoveControl()` (스턴/슬로우 자동 반영)

### 변경 (2026-04-06)
- `TakeDamage(float, ICombatant attacker)` — attacker 파라미터 추가
  - 패링 중 + attacker 존재 → 임시 반격 피해 전달
  - Reflecting 상태 + attacker → `statsManager.GetPlayerReflectRatio()` 비율만큼 반사 피해
- `TakeShieldBreakDamage(float, float, ICombatant attacker)` — attacker 파라미터 추가
- `NotifyParryReward()` — Counter(반격 피해) / HitStun(경직 부여) / Invulnerable(무적) / Buff(DamageUp) 4종 전부 구현, attacker 파라미터 추가
- `GameManager _gameManager` SerializeField 참조 추가

---

## BossController.cs
**경로:** `Assets/Scripts/BossController.cs`
**변경일:** 2026-03-30

### 변경 전
- `private float currentHP` 필드를 Controller가 직접 보관
- `TakeDamage()` 내부에서 `currentHP` 직접 수정
- `HandlePhase()` 내부에서 `currentHP / bossStats.BossMaxHP` 직접 계산
- `ICombatant` 미구현

### 변경 후
- `currentHP` 필드 **제거**
- `public StatsManager statsManager` 참조 추가
- `ICombatant` 구현 — 모든 스탯 변경은 StatsManager 경유
- `TakeDamage()`, `TakeShieldBreakDamage()` → StatsManager 위임
- `RecoverHP()` → StatsManager 위임 / `AddShield()` → 보스 실드 없음(빈 구현)
- `ApplyStatus/Buff/Debuff/RemoveStatuses/RemoveBuffs` → StatsManager 위임
- `Knockback()`, `Pull()`, `MoveBy()` → NavMeshAgent.Warp() 기반
- `NotifyParryReward()` → 보스는 패링 없음(빈 구현)
- `HandlePhase()` → `statsManager.GetBossHPPercent()` 참조
- `TrackPlayer()` → NavMeshAgent 속도에 `statsManager.GetBossMoveControl()` 반영
- `EffectAttackRoutine()` → 공격 중 `isCasting = true` 설정 (CheckTargetCasting 조건 반영)

### 변경 (2026-04-06)
- `TakeDamage(float, ICombatant attacker)` — attacker 파라미터 추가 (시그니처 맞춤)
- `TakeShieldBreakDamage(float, float, ICombatant attacker)` — attacker 파라미터 추가
- `NotifyParryReward(ParryRewardType, float, float, ICombatant attacker)` — attacker 파라미터 추가
- `GameManager _gameManager` SerializeField 참조 추가

---

## ICombatant.cs
**경로:** `Assets/Scripts/Skill/Interfaces/ICombatant.cs`
**변경일:** 2026-04-06

### 변경 후
- `TakeDamage(float amount, ICombatant attacker = null)` — attacker 파라미터 추가
- `TakeShieldBreakDamage(float amount, float multiplier, ICombatant attacker = null)` — attacker 파라미터 추가
- `NotifyParryReward(ParryRewardType rewardType, float value, float duration, ICombatant attacker = null)` — attacker 파라미터 추가

---

## StatsManager.cs (2차)
**경로:** `Assets/StatsManager.cs`
**변경일:** 2026-04-06

### 변경 후
- `GetPlayerReflectRatio()` public getter 추가 — PlayerController.TakeDamage 피해 반사에서 사용

---

## SkillComponents.cs
**경로:** `Assets/Scripts/Skill/Core/SkillComponents.cs`
**변경일:** 2026-04-06

### 변경 후
- 37종 완성본 작성
- `CheckRecentDamage` (구 #36), `CheckTargetCasting` (구 #37) 제거
- `MoveSelf` → 새 #36, `CheckTargetDistance` → 새 #37로 번호 재정렬
- DealDamage(#1), DealMultiHitDamage(#2), ExecuteBelowHP(#29), DealDirectionalHit(#35), DealShieldBreakDamage(#28) — `ctx.Caster`를 attacker로 전달
- ApplyParryReward(#24) — `ctx.PrimaryTarget`를 attacker로 전달
- LaunchProjectile(#32) — ProjectilePool 싱글턴 사용, pool 파라미터 제거

---

## SkillContext.cs
**경로:** `Assets/Scripts/Skill/Core/SkillContext.cs`
**변경일:** 2026-04-06

### 변경 후
- `RecentDamageTaken` 필드 제거 (dead field)

---

## SkillTypes.cs
**경로:** `Assets/Scripts/Skill/Core/SkillTypes.cs`
**변경일:** 2026-04-06

### 변경 후
- `CastingCheckType` 열거형 제거 (dead type)

---

## 신규 파일 (2026-04-06)

| 파일 | 경로 | 설명 |
|------|------|------|
| GameManager.cs | `Assets/Scripts/GameManager.cs` | 싱글턴, 플레이어/보스 리스트 관리, 전투 타이머 |
| SkillDefinition.cs | `Assets/Scripts/Skill/Core/SkillDefinition.cs` | ScriptableObject — 스킬 메타데이터 + RuntimeStep |
| SkillExecutor.cs | `Assets/Scripts/Skill/Core/SkillExecutor.cs` | 스킬 실행 + 쿨타임 관리 |
| SkillRegistry.cs | `Assets/Scripts/Skill/Core/SkillRegistry.cs` | ScriptableObject — 스킬 풀 관리, 태그 조회, 드래프트/카운터 후보 |
| ProjectilePool.cs | `Assets/Scripts/Skill/Projectile/ProjectilePool.cs` | 싱글턴 오브젝트 풀 — SkillProjectile 관리 |
| SkillProjectile.cs | `Assets/Scripts/Skill/Projectile/SkillProjectile.cs` | 투사체 프리팹 컴포넌트 — IProjectile 구현 |
| PersistentAreaManager.cs | `Assets/Scripts/Skill/Area/PersistentAreaManager.cs` | 싱글턴 — 장판 소환 허브 |
| PersistentAreaPool.cs | `Assets/Scripts/Skill/Area/PersistentAreaPool.cs` | 오브젝트 풀 — SkillArea 관리 |
| SkillArea.cs | `Assets/Scripts/Skill/Area/SkillArea.cs` | 장판 프리팹 컴포넌트 — IPersistentArea 구현 |
| IProjectile.cs | `Assets/Scripts/Skill/Interfaces/IProjectile.cs` | 투사체 인터페이스 (IPoolable 상속) |
| IPersistentArea.cs | `Assets/Scripts/Skill/Interfaces/IPersistentArea.cs` | 장판 인터페이스 (IPoolable 상속) |
| IPoolable.cs | `Assets/Scripts/Skill/Interfaces/IPoolable.cs` | 풀링 공통 인터페이스 — OnSpawn/OnDespawn |

---

## 삭제된 파일 (2026-04-06)

| 파일 | 경로 | 사유 |
|------|------|------|
| GoalDetector.cs | `Assets/Scripts/AI/BossAI/GoalDetector.cs` | BossAgent.cs에 병합 완료

---

## 스킬 슬롯 연동 — 드래프트→실행 파이프라인 구축 (2026-04-08)

### AbilityCard.cs
**경로:** `Assets/Scripts/AbilityCard.cs`
- `SkillDefinition skillDefinition` 필드 추가 (신규 스킬 시스템 연결)
- 기존 `skillObjectName` 필드는 레거시 호환용으로 유지

### PlayerController.cs
**경로:** `Assets/Scripts/PlayerController.cs`
- `PlayerSkillSlot _skillSlot` SerializeField 추가
- `HandleActions()` → Q/W/E/R/T 키 입력 시 PlayerSkillSlot.TryExecute 호출로 교체
- `BuildSkillContext()` 추가 — Caster/Target/Position/Direction 자동 생성
- `FindNearestBoss()` 추가 — GameManager.Bosses에서 가장 가까운 보스 탐색

### CardManager.cs
**경로:** `Assets/Scripts/CardManager.cs`
- `UnlockSkill()` → SkillDefinition 기반으로 교체 (PlayerSkillSlot.Equip 호출)
- skillDefinition 없을 경우 레거시(skillObjectName) 폴백 유지

### GameManager.cs
**경로:** `Assets/Scripts/GameManager.cs`
- `SkillRegistry _skillRegistry` SerializeField 추가
- `Start()`에서 `SkillBinder.BindAll(_skillRegistry)` 호출 추가

### 신규 파일

| 파일 | 경로 | 역할 |
|------|------|------|
| PlayerSkillSlot.cs | `Assets/Scripts/Skill/Core/PlayerSkillSlot.cs` | 스킬 슬롯 관리 (장착/해제/실행/쿨타임 조회) |
| SkillBinder.cs | `Assets/Scripts/Skill/Core/SkillBinder.cs` | SkillLibrary → SkillDefinition.RuntimeStep 일괄 주입 |

---

## SkillLibrary.cs (전체 재작성)
**경로:** `Assets/Scripts/Skill/Core/SkillLibrary.cs`
**변경일:** 2026-04-11

### 변경 전
- `ErosionField_Boss()`, `SealChain()` 2개 메서드만 존재
- SkillBinder에서 참조하는 27개 메서드 미정의 → CS0117 컴파일 에러

### 변경 후
- **29개 전체 스킬 메서드 구현 완료**
- 플레이어 공용 (12): ExecutionSpike, CrushingBarrage, ErosionField, HuntingMark, SurvivalPulse, FortressArmor, SealChain, CollapseRoar, BarrierBreaker, OverchargeMode, PiercingShot, RuptureMagazine
- 플레이어 전용 (6): CounterStance, ParryEnhance, CounterSlash, WireHook, RopeShockwave, CollapseStrike
- 보스 공용 (11): ExecutionSpike_Boss, CrushingBarrage_Boss, ErosionField_Boss, SurvivalPulse_Boss, FortressArmor_Boss, CollapseRoar_Boss, OverchargeMode_Boss, MarkWave_Boss, SealChain_Boss, BarrierBreaker_Boss, RuptureMagazine_Boss
- 비율형(Vulnerability/AntiHeal) → 0~1 decimal 변환 (5% → 0.05f)
- 정수형(DamageUp/DefenseDown) → 그대로 전달 (15 → 15f)
- 최대HP 기반 계산 → `ctx.Caster?.MaxHP ?? 0f` 사용
- **스텁 조건**: WireHook/CollapseStrike의 RopeLanding, ParrySuccessRecent → `cond => true` (상태 추적 미구현)
- **단순화**: SealChain_Boss/RuptureMagazine_Boss → 단일 투사체 (다방향 래퍼 추후)

---

## SkillComponents.cs (TriggerOnHit 시그니처 변경)
**경로:** `Assets/Scripts/Skill/Core/SkillComponents.cs`
**변경일:** 2026-04-11

### 변경 전
- `TriggerOnHit(SkillStep onHit, SkillStep onMiss = null)` — onHit 필수 파라미터

### 변경 후
- `TriggerOnHit(SkillStep onHit = null, SkillStep onMiss = null)` — **onHit도 선택 파라미터로 변경**
- SkillLibrary에서 `TriggerOnHit(onMiss: ...)` 형태로 onMiss만 전달하는 7곳의 CS7036 에러 해결
- 내부 로직 `(ctx.HitLanded ? onHit : onMiss)?.Invoke(ctx)` 가 null-safe이므로 동작 변화 없음

---

## 신규 파일 (2026-04-11)

| 파일 | 경로 | 설명 |
|------|------|------|
| AI_SYSTEM.md | `Assets/Scripts/AI/AI_SYSTEM.md` | AI 시스템 전체 문서 — BossAgent(ML-Agents PPO), 플레이어 봇(Behavior Graph), 학습 설정/이력, DoubleMove 설계 |

---

## PlayerController / CardManager Stage 7 — 수동 입력 제거 + SkillManager 전환 (2026-04-13)

**경로:**
- `Assets/Scripts/PlayerController.cs` (정리)
- `Assets/Scripts/CardManager.cs` (SkillManager 우선 경로 추가)

### PlayerController 제거/이관
- `[SerializeField] PlayerSkillSlot _skillSlot` **삭제** → `[SerializeField] SkillManager _skillManager`
- `static readonly KeyCode[] SkillKeys (Q/W/E/R/T)` **삭제**
- `HandleActions()` **삭제** (수동 Input.GetKeyDown 루프)
- `BuildSkillContext()` / `FindNearestBoss()` **삭제** (SkillManager 로 이관)
- `[SerializeField] GameManager _gameManager` **삭제** (SkillManager 가 자체 참조)
- `Update()` → `Tick + HandleMovement` 만 남음, 스킬 시전은 SkillManager 자동 처리
- 외부 프로퍼티: `SkillSlot` → `SkillMgr` 로 교체
- `Awake()` 에 `_skillManager` GetComponent fallback 추가

### CardManager 변경
- `Equip` 경로에서 `SkillManager` 를 먼저 조회, 없으면 `PlayerSkillSlot` 폴백
- 로그 메시지 "SkillManager / PlayerSkillSlot 둘 다 없음" 으로 교체

### 최종 PlayerController 책임
1. 이동 입력(WASD → Rigidbody)
2. ICombatant 프록시 (전부 StatManager 위임)
3. 위치 제어 코루틴 (Knockback/Pull/MoveBy)
4. 사망 처리

### Scene 작업 필요
- Player GameObject 에 `SkillManager` 컴포넌트 부착 (RequireComponent 아님 — 수동)
- `SkillManager` 의 `_statManager / _gameManager / _owner` Inspector 바인딩 (Awake 에 GetComponent fallback 있지만 `_gameManager` 는 scene 참조 필요)
- 기존 `PlayerSkillSlot` 컴포넌트는 남겨도 무방 (CardManager 폴백 경로)

---

## SkillManager Stage 6 — 자동 시전 매니저 신규 (2026-04-13)

**경로:** `Assets/Scripts/Skill/Core/SkillManager.cs` (신규)

### 개요
`PlayerSkillSlot` 의 확장판. 슬롯 index 순서 = 우선순위. 매 프레임 `CanCast` 를 돌려
첫 번째 가능한 스킬을 자동 시전한다. Q/W/E/R/T 수동 입력 없이 동작.

### 공통 구조 (PlayerSkillSlot 과 동일)
- `[RequireComponent(typeof(SkillExecutor))]`
- `Equip / Unequip / UnequipAll`
- `TryExecute / CanUse / GetRemainingCooldown / ResetCooldown`

### 신규 기능
- `Update()` — `_autoCastEnabled && IsAlive` 일 때 슬롯 순회, 첫 CanCast 통과 스킬 1개 실행 후 break
- `SetAutoCast(bool)` — 토글
- `CanCast(skill, out ctx)` — 7조건 검사
- `BuildSkillContext(target)` / `FindNearestTarget()` — PlayerController 에서 이관

### CanCast 7조건
| # | 조건 | 체크 |
|---|------|------|
| 1 | 쿨타임 만료 | `_executor.CanUse(skill)` |
| 2 | 플레이어 생존 | `_statManager.IsAlive` (Update 진입 단계) |
| 3 | 시전 중 아님 | `!_statManager.IsCasting` |
| 4 | 패링 중 아님 | `!_statManager.IsParrying` |
| 5 | 무력화 없음 | `!HasStatus(Stunned/HitStun/Silence/Rooted)` |
| 6 | 타겟 존재 + 생존 | `target != null && target.IsAlive` (Self 는 skip) |
| 7 | 사거리 내 | `dist ≤ skill.Range` (Self 는 skip) |

### 직렬화 참조
- `_statManager` / `_gameManager` / `_owner(PlayerController)` — Inspector 바인딩, Awake 에 GetComponent fallback

### TODO (Stage 7)
- `PlayerController.HandleActions` 의 Q/W/E/R/T 수동 입력 제거
- 기존 `PlayerSkillSlot` 을 `SkillManager` 로 치환 (또는 공존)
- 통합 테스트 + AI_SYSTEM.md 갱신

---

## BossController Stage 5 — StatManager 위임 + 모놀리스 삭제 (2026-04-13)

**경로:** `Assets/Scripts/BossController.cs`, `Assets/StatsManager.cs` (삭제)

### BossController 변경
- 필드 `public StatsManager statsManager` → `[SerializeField] private StatManager _statManager`
- `[RequireComponent(typeof(StatManager))]` 추가
- `isCasting / isParrying` 로컬 필드 **삭제** → `_statManager.IsCasting / IsParrying`
  - EffectAttackRoutine 에서 `_statManager.SetCasting(true/false)` 호출
- `Start()` 에서 `_statManager.Initialize(bossStats, BossMaxHP, shieldMax=0, hpRegenRate=0, parryWindow=0, Boss)` + `BindOwner(this)`
- `Update()` 에서 `_statManager.Tick(Time.deltaTime)` 호출
- HP/상태 조회: `GetBossHP/GetBossMaxHP/GetBossHPPercent` → `GetHP/GetMaxHP/GetHPPercent` 통합
- 모든 ICombatant 메서드를 `_statManager` 로 위임 (Player 와 동일 구조)
- `NotifyParryReward` 도 StatManager 위임 — 보스는 실사용 안하지만 인터페이스 통일

### 삭제
- **`Assets/StatsManager.cs`** 삭제 (모놀리스 — Player/Boss 전용 메서드 28개 중복, StatManager 로 통합 완료)
- `Assets/StatsManager.cs.meta` 함께 삭제

### 주의 — Scene 재연결 필요
- `Chapter1.unity`, `_Recovery/*.unity` 에는 GUID 로 StatsManager 컴포넌트가 참조되어 있음 — Unity 에디터에서 열면 "missing script" 경고 발생
- 해당 GameObject 에서 StatsManager 컴포넌트 제거 후 StatManager 컴포넌트로 교체 필요
- Player / Boss GameObject 에 `StatManager` 컴포넌트 부착 (RequireComponent 이 자동 처리)
- Inspector 의 `_statManager` 필드는 Awake 에서 GetComponent fallback, 명시 바인딩도 가능

### TODO (Stage 6)
- SkillManager 신규 생성 (자동 시전, 플레이어 상태별 CanCast 판정)

---

## PlayerController Stage 4 — StatManager 위임 리팩터 (2026-04-13)

**경로:** `Assets/Scripts/PlayerController.cs`

### 변경 내용
- 필드 `public StatsManager statsManager` → `[SerializeField] private StatManager _statManager` 교체
- `[RequireComponent(typeof(StatManager))]` 추가 — 같은 GameObject 자동 부착
- `private bool isCasting / isParrying` 로컬 필드 **삭제** → `_statManager.IsCasting / IsParrying`
- `private void RegenerateHP()` **삭제** → `_statManager.Tick(Time.deltaTime)` 로 대체
- `Start()` 에서 `_statManager.Initialize(playerStats, MaxHP, ShieldMax, HPRegenRate, ParryWindow, Player)` + `BindOwner(this)`

### ICombatant 구현 — 전부 StatManager 위임
| 메서드 | 위임 대상 |
|---|---|
| TakeDamage | `_statManager.ReceiveDamage(amount, attacker)` + Die 체크 |
| TakeShieldBreakDamage | `_statManager.ReceiveShieldBreakDamage(...)` + Die 체크 |
| RecoverHP / AddShield | `_statManager.RecoverHP / AddShield` |
| ApplyStatus / Buff / Debuff | `_statManager.Apply...` |
| HasStatus / RemoveStatuses / RemoveBuffs | `_statManager.Has... / Remove...` |
| NotifyParryReward | `_statManager.NotifyParryReward` (분기 전부 StatManager 에서 처리) |

### 제거된 중복 로직
- `TakeDamage` 내부의 패링 반격 / 반사 / DamageTakenMultiplier / Shield 분기 → 전부 StatManager.ReceiveDamage 1곳에 있음
- `NotifyParryReward` 의 switch 4분기 → StatManager 에서 Counter/HitStun/Invulnerable/Buff 전부 구현

### 새 scene 연결
- GameObject 에 `StatManager` 컴포넌트를 **같이 부착** (RequireComponent 가 자동 추가)
- Inspector 에서 `_statManager` 필드는 자동 바인딩 (Awake 에서 `GetComponent<StatManager>()` fallback)

### TODO (Stage 5)
- BossController 도 동일 리팩터
- 모놀리스 `Assets/StatsManager.cs` 삭제

---

## StatManager Stage 3 — 상태/버프/디버프/회복/실드 (2026-04-13)

**경로:** `Assets/Scripts/Stats/StatManager.cs`

### 추가된 기능
- `RecoverHP(amount)` — `HealingReceivedMultiplier` 반영 후 MaxHP 클램프
- `AddShield(amount)` — `ShieldMax` 클램프
- `ApplyStatus(StatusType, duration, value)` — `CalcStatusDuration` 적용, 기존 코루틴 중복 시 교체
- `RemoveStatuses(CleanseType, count)` — All/DamageOverTime/Debuff 필터
- `ApplyBuff(BuffType, duration, value)` — DamageUp/DefenseUp (+value/100), ParryWindowUp (+base×value/100), ParryRewardUp
- `RemoveBuffs(DispelType, count)` — All/DefenseBuff/OffenseBuff 필터
- `ApplyDebuff(DebuffType, duration, value)` — `DebuffDurationResistance` 반영
- `HasBuff(BuffType)` / `HasDebuff(DebuffType)` 조회
- `NotifyParryReward` 의 **Invulnerable** / **Buff** 분기 완성 → `ApplyStatus` / `ApplyBuff` 위임

### 내부 설계
- `_baseStats` 필드로 원본 SO 참조 보관 (RevertStatus/Buff/Debuff 시 기준값 복원)
- `_baseParryWindow` 로 ParryWindowUp Revert 시 원상 복구
- `WaitOneSec` 정적 캐시 (HPRegen/DamageOverTime 틱)
- 상태별 ApplyStatusValue/RevertStatus switch — 기존 monolith 패턴 이관
- DamageOverTime 은 직접 `SetHP(cur - value × DamageTakenMultiplier)` 로 처리 (ReceiveDamage 우회, 패링/반사 무시)

### TODO (Stage 4 이후)
- Controller 측 연결 (PlayerController → StatManager 위임)
- 모놀리스 `Assets/StatsManager.cs` 제거

---

## StatManager Stage 2 — DealDamage/ReceiveDamage + 패링 (2026-04-13)

**경로:** `Assets/Scripts/Stats/StatManager.cs`

### 추가된 기능
- `BindOwner(ICombatant)` — DealDamage 시 attacker 참조용 소유자 바인딩
- `DealDamage(target, amount)` — DamageUp 반영 후 `target.TakeDamage(adjusted, _owner)` 호출
- `DealShieldBreakDamage(target, amount, multiplier)` — 동일 경로, shield break 전용
- `ReceiveDamage(amount, attacker)` — 패링/반사/DamageTakenMultiplier/Shield/HP 전체 플로우
- `ReceiveShieldBreakDamage(amount, multiplier, attacker)` — shield 우선 차감 + 초과 HP
- `BeginParryWindow() / EndParryWindow()` — `WaitForSeconds` 캐싱 코루틴
- `NotifyParryReward(type, value, duration, attacker)` — Counter/HitStun 구현 (Invulnerable/Buff 는 Stage 3 TODO)
- `HasStatus(type)` 조회 메서드
- `_parryWait` WaitForSeconds 캐시 (Initialize 시점 생성)

### 피해 처리 순서 (ReceiveDamage 내부)
1. `!IsAlive` → 조기 종료
2. `IsParrying && attacker != null` → `attacker.TakeDamage(amount, _owner)` → 피해 무효
3. `HasStatus(Reflecting)` → `attacker.TakeDamage(amount × ReflectRatio, _owner)` (자신도 피해 받음)
4. `finalDamage = amount × DamageTakenMultiplier`
5. Shield > 0 이면 Shield 우선 차감, 음수면 초과분 HP 로
6. Shield == 0 이면 바로 HP 차감
7. HP == 0 이면 `IsAlive = false`

### TODO (Stage 3)
- `ApplyStatus / ApplyBuff / ApplyDebuff / RemoveStatuses / RemoveBuffs / RecoverHP / AddShield`
- Parry Invulnerable / Buff 보상 분기 본체

---

## StatManager Stage 1 — 통합 스켈레톤 (2026-04-13)

**경로:** `Assets/Scripts/Stats/StatManager.cs` (신규)

### 신규 파일
- `StatManager.cs` — Player/Boss 공용 통합 스탯 매니저 스켈레톤
- `CombatantKind` enum (Player/Boss 구분)

### 구현 범위 (Stage 1)
- `Initialize(BaseStatsSO, maxHP, shieldMax, hpRegenRate, parryWindow, kind)` — 런타임 복사본 생성
- 조회: GetHP / GetMaxHP / GetHPPercent / GetShield / GetShieldMax / GetMoveControl / GetReflectRatio 등
- 상태 플래그: IsAlive / IsCasting / IsParrying + SetCasting / SetParrying
- `Tick(float dt)` — HP 자동 재생 틱
- 내부 유틸: SetHP / SetShield (Stage 2+ 에서 ReceiveDamage 가 사용)
- 코루틴 추적용 Dictionary 3종 (StatusType / BuffType / DebuffType) — Stage 3 에서 활용

### 설계 포인트
- 런타임 HP/Shield 는 StatManager **내부 필드**로 관리 (SO 내부값에 의존 X)
- SO 는 Multiplier / ReflectRatio 등 공통 값만 제공
- Player/Boss 호출자가 Initialize 시 타입별 필드(MaxHP vs BossMaxHP 등) 읽어서 넘김

### 다음 Stage
- Stage 2: DealDamage ↔ ReceiveDamage 쌍 + 패링/반사 로직
- Stage 3: 상태이상/버프/디버프/정화 이관

---

## DoubleMove 커스텀 노드 구현 (2026-04-12)

**경로:** `Assets/Scripts/AI/Player/DoubleMove/`

2인 협동 플레이어 봇용 Behavior Graph 커스텀 노드 11개 로직 구현 (기존에는 스켈레톤만 존재).

### Condition 노드 (4개) — `bool IsTrue()` 구현
| 파일 | 역할 |
|------|------|
| IsBossTooCloseCondition.cs | Self↔Boss 거리 < DangerRange 검사 (sqrMagnitude 사용) |
| IsAllyTooCloseCondition.cs | Self↔Ally 거리 < MinSpacing 검사 (겹침 방지) |
| IsBossTargetingAllyCondition.cs | Boss.forward와 Self/Ally 방향 내적 비교 → Ally 쪽이 더 정렬되면 true |
| IsAtOptimalRangeCondition.cs | OptimalMin ≤ Self↔Boss 거리 ≤ OptimalMax 검사 |

### Action 노드 (7개) — `Status OnStart()` 구현
| 파일 | 역할 |
|------|------|
| UpdateSituationAction.cs | Self/Boss/Ally 위치로 DistanceToBoss, DistanceToAlly, BossToAllyAngle 3개 Out 변수 갱신 |
| CalcFleePositionAction.cs | Boss 반대 방향으로 5개 후보 샘플링 → NavMesh 유효 지점 중 보스와 가장 먼 곳 선택 |
| CalcSpreadPositionAction.cs | Ally 반대 방향 MinSpacing×1.2 위치 NavMesh 샘플 |
| CalcFlankPositionAction.cs | Boss 중심, Ally 반대편 FlankRadius 위치 NavMesh 샘플 (측면 협공) |
| CalcStrafePositionAction.cs | Boss 주위 StrafeRadius 반경을 StrafeAngleStep 도 회전한 지점 (실패 시 반대 회전 재시도) |
| CalcApproachPositionAction.cs | Boss 기준 Self 방향으로 (OptimalMin+OptimalMax)/2 거리 위치 |
| SetNavDestinationAction.cs | NavMeshAgent.SetDestination(TargetPosition) (isOnNavMesh 검사 포함) |

### 변수명/타입 수정 (스켈레톤 오타)
- UpdateSituationAction: `Ally의` → `Ally`, `BossToAllyAngle에` → `BossToAllyAngle`
- CalcFleePositionAction: `NavMesh` (Vector3) → `TargetPosition` (Vector3)
- CalcStrafePositionAction: `StrafeAngleStep` 타입 Vector3 → float

### 공통 설계
- 모든 Action: Self/Boss/Ally/Agent null 검사 → Failure 반환
- NavMesh 샘플 실패 시 Warning 로그 + Failure
- Story 텍스트에 `[변수명]` 형식으로 블랙보드 바인딩 슬롯 명시
