import matplotlib.pyplot as plt
import matplotlib
import numpy as np
import os

matplotlib.rcParams['font.family'] = 'Malgun Gothic'
matplotlib.rcParams['axes.unicode_minus'] = False

OUT = r"c:\Users\paek6\Downloads\Buildup\Buildup\paper\graphs"
DPI = 300

quarters = ['Q1', 'Q2', 'Q3', 'Q4']

# ── Academic monochrome palette ──
BLACK = '#222222'
DARK = '#555555'
MID = '#888888'
LIGHT = '#BBBBBB'
VLIGHT = '#E0E0E0'
BAR_GRAYS = ['#333333', '#666666', '#999999', '#CCCCCC']
BAR_HATCHES = ['', '///', '...', 'xxx']

TITLE_SIZE = 13
AXIS_LABEL_SIZE = 11
TICK_SIZE = 10
DATA_LABEL_SIZE = 10
LEGEND_SIZE = 9.5
ANNOT_SIZE = 9

def clean_axes(ax):
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)
    for spine in ax.spines.values():
        spine.set_linewidth(0.8)
        spine.set_color(BLACK)
    ax.tick_params(labelsize=TICK_SIZE, colors=BLACK)

# ============================================================
# Graph 1: 매치업별 최종 승률 비교 (Q4)
# ============================================================
fig, ax = plt.subplots(figsize=(7, 4.5))
matchups = ['VsRanged', 'VsMelee', 'VsHybrid', 'VsCC']
final_wr = [84.1, 59.5, 42.6, 10.3]
bars = ax.bar(matchups, final_wr, color=BAR_GRAYS, width=0.5,
              edgecolor=BLACK, linewidth=0.8)
for b, v, h in zip(bars, final_wr, BAR_HATCHES):
    b.set_hatch(h)
    ax.text(b.get_x() + b.get_width()/2, v + 1.5, f'{v}%',
            ha='center', va='bottom', fontweight='bold', fontsize=DATA_LABEL_SIZE+1, color=BLACK)
ax.set_ylabel('Win Rate (%)', fontsize=AXIS_LABEL_SIZE)
ax.set_title('매치업별 최종 승률 비교 (Q4)', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax.set_ylim(0, 100)
clean_axes(ax)
ax.axhline(y=50, color=MID, linestyle='--', linewidth=0.7)
ax.text(3.35, 51.5, '50%', fontsize=8, color=MID)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph1_final_winrate.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 1 done")

# ============================================================
# Graph 2: VsRanged: 승률 및 명중률 회복 추이
# ============================================================
wr_r = [84.1, 68.2, 59.1, 84.1]
hr_r = [80.8, 63.6, 58.7, 81.9]

fig, ax = plt.subplots(figsize=(7, 4.5))
ax.axvspan(1, 2, alpha=0.05, color=MID)
ax.plot(quarters, wr_r, 'o-', color=BLACK, linewidth=2, markersize=7, label='Win Rate')
ax.plot(quarters, hr_r, 's--', color=MID, linewidth=2, markersize=7, label='Hit Rate')

for i in range(4):
    ofs_wr = (0, 10) if i != 2 else (0, -16)
    ofs_hr = (0, -16) if i != 2 else (0, 10)
    ax.annotate(f'{wr_r[i]}%', (quarters[i], wr_r[i]), textcoords="offset points",
                xytext=ofs_wr, ha='center', fontsize=DATA_LABEL_SIZE, color=BLACK, fontweight='bold')
    ax.annotate(f'{hr_r[i]}%', (quarters[i], hr_r[i]), textcoords="offset points",
                xytext=ofs_hr, ha='center', fontsize=DATA_LABEL_SIZE, color=DARK)

ax.set_ylabel('비율 (%)', fontsize=AXIS_LABEL_SIZE)
ax.set_ylim(40, 100)
ax.set_title('VsRanged: 승률 및 명중률 회복 추이', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax.legend(loc='lower left', fontsize=LEGEND_SIZE, framealpha=0.9, edgecolor=LIGHT)
clean_axes(ax)
ax.annotate('탐색 구간', xy=(1.5, 53), fontsize=ANNOT_SIZE, ha='center', color=DARK, style='italic')
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph2_vsranged_wr_hr.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 2 done")

# ============================================================
# Graph 3: VsRanged: 구간별 평균 전투 거리 변화
# ============================================================
dist_r = [23.9, 26.5, 32.3, 22.0]

fig, ax = plt.subplots(figsize=(7, 4.5))
ax.axvspan(1, 2, alpha=0.05, color=MID)
ax.plot(quarters, dist_r, 'o-', color=BLACK, linewidth=2, markersize=7)
ax.fill_between(quarters, dist_r, alpha=0.08, color=MID)
for i, d in enumerate(dist_r):
    ax.annotate(f'{d}m', (quarters[i], d), textcoords="offset points",
                xytext=(0, 10), ha='center', fontsize=DATA_LABEL_SIZE, fontweight='bold', color=BLACK)
ax.set_ylabel('보스-P1 평균 거리 (m)', fontsize=AXIS_LABEL_SIZE)
ax.set_title('VsRanged: 구간별 평균 전투 거리 변화', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax.set_ylim(15, 40)
ax.axhline(y=30, color=DARK, linestyle=':', linewidth=0.8)
ax.text(3.4, 30.7, '원거리 선호 거리', fontsize=8, color=DARK, ha='center')
clean_axes(ax)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph3_vsranged_distance.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 3 done")

# ============================================================
# Graph 4: VsHybrid: 내부 전투 효율 지표 개선
# ============================================================
hr_h = [34.6, 42.5, 37.9, 49.9]
dur_h = [74.5, 70.7, 65.9, 52.0]

fig, ax1 = plt.subplots(figsize=(7, 4.5))
ax2 = ax1.twinx()
l1 = ax1.plot(quarters, hr_h, 'o-', color=BLACK, linewidth=2, markersize=7, label='Hit Rate (%)')
l2 = ax2.plot(quarters, dur_h, 's--', color=MID, linewidth=2, markersize=7, label='Duration (s)')

for i in range(4):
    ax1.annotate(f'{hr_h[i]}%', (quarters[i], hr_h[i]), textcoords="offset points",
                 xytext=(-16, 8), ha='center', fontsize=DATA_LABEL_SIZE, color=BLACK, fontweight='bold')
    ax2.annotate(f'{dur_h[i]}s', (quarters[i], dur_h[i]), textcoords="offset points",
                 xytext=(16, -12), ha='center', fontsize=DATA_LABEL_SIZE, color=DARK)

ax1.set_ylabel('Hit Rate (%)', fontsize=AXIS_LABEL_SIZE)
ax2.set_ylabel('Duration (s)', fontsize=AXIS_LABEL_SIZE)
ax1.set_ylim(25, 60)
ax2.set_ylim(40, 85)
ax1.set_title('VsHybrid: 내부 전투 효율 지표 개선', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax1.tick_params(labelsize=TICK_SIZE)
ax2.tick_params(labelsize=TICK_SIZE)
for spine in ['top']:
    ax1.spines[spine].set_visible(False)
    ax2.spines[spine].set_visible(False)
lines = l1 + l2
labels = [l.get_label() for l in lines]
ax1.legend(lines, labels, loc='center right', fontsize=LEGEND_SIZE, framealpha=0.9, edgecolor=LIGHT)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph4_vshybrid_hr_dur.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 4 done")

# ============================================================
# Graph 5: VsCC: 보상-승률 불일치 분석
# ============================================================
rew_cc = [9.3, 11.5, 13.6, 14.6]
wr_cc = [11.3, 4.2, 18.3, 10.3]

fig, ax1 = plt.subplots(figsize=(7, 4.5))
ax2 = ax1.twinx()
l1 = ax1.plot(quarters, rew_cc, 'o-', color=BLACK, linewidth=2, markersize=7, label='누적 보상')
l2 = ax2.plot(quarters, wr_cc, 's--', color=MID, linewidth=2, markersize=7, label='Win Rate (%)')
for i in range(4):
    ax1.annotate(f'{rew_cc[i]}', (quarters[i], rew_cc[i]), textcoords="offset points",
                 xytext=(-12, 8), ha='center', fontsize=DATA_LABEL_SIZE, color=BLACK, fontweight='bold')
    ax2.annotate(f'{wr_cc[i]}%', (quarters[i], wr_cc[i]), textcoords="offset points",
                 xytext=(12, -12), ha='center', fontsize=DATA_LABEL_SIZE, color=DARK)
ax1.set_ylabel('누적 보상', fontsize=AXIS_LABEL_SIZE)
ax2.set_ylabel('Win Rate (%)', fontsize=AXIS_LABEL_SIZE)
ax1.set_ylim(5, 20)
ax2.set_ylim(0, 30)
ax1.set_title('VsCC: 보상-승률 불일치 분석', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax1.tick_params(labelsize=TICK_SIZE)
ax2.tick_params(labelsize=TICK_SIZE)
for spine in ['top']:
    ax1.spines[spine].set_visible(False)
    ax2.spines[spine].set_visible(False)
lines = l1 + l2
labels = [l.get_label() for l in lines]
ax1.legend(lines, labels, loc='upper left', fontsize=LEGEND_SIZE, framealpha=0.9, edgecolor=LIGHT)
ax1.annotate('보상 증가, 승률 정체', xy=(2.5, 17.5),
             fontsize=ANNOT_SIZE, ha='center', color=BLACK,
             bbox=dict(boxstyle='round,pad=0.3', facecolor=VLIGHT, edgecolor=MID, linewidth=0.8))
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph5_vscc_reward_wr.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 5 done")

# ============================================================
# Graph 6: 매치업별 전투 종료 원인 비교
# ============================================================
matchup_labels = ['VsRanged\n(90s)', 'VsMelee\n(90s)', 'VsHybrid\n(60~90s)', 'VsCC\n(60s)']
player_death = [73.9, 58.3, 42.6, 11.0]
timeout = [23.9, 4.8, 48.6, 89.0]
boss_death = [2.3, 36.9, 8.8, 0.0]

fig, ax = plt.subplots(figsize=(7.5, 4.5))
x = np.arange(len(matchup_labels))
w = 0.48
b1 = ax.bar(x, player_death, w, label='보스 승리 (AllPlayerDeath)',
            color='#444444', edgecolor=BLACK, linewidth=0.6)
b2 = ax.bar(x, timeout, w, bottom=player_death, label='시간 초과 (Timeout)',
            color=LIGHT, edgecolor=BLACK, linewidth=0.6, hatch='///')
b3 = ax.bar(x, boss_death, w, bottom=[p+t for p,t in zip(player_death, timeout)],
            label='보스 패배 (BossDeath)', color=VLIGHT, edgecolor=BLACK, linewidth=0.6)

for i in range(4):
    if player_death[i] > 8:
        ax.text(i, player_death[i]/2, f'{player_death[i]}%', ha='center', va='center',
                fontweight='bold', fontsize=DATA_LABEL_SIZE, color='white')
    if timeout[i] > 8:
        ax.text(i, player_death[i] + timeout[i]/2, f'{timeout[i]}%', ha='center', va='center',
                fontweight='bold', fontsize=DATA_LABEL_SIZE, color=BLACK)
    if boss_death[i] > 5:
        ax.text(i, player_death[i] + timeout[i] + boss_death[i]/2, f'{boss_death[i]}%',
                ha='center', va='center', fontweight='bold', fontsize=DATA_LABEL_SIZE-1, color=DARK)

ax.set_xticks(x)
ax.set_xticklabels(matchup_labels, fontsize=TICK_SIZE)
ax.set_ylabel('비율 (%)', fontsize=AXIS_LABEL_SIZE)
ax.set_title('매치업별 전투 종료 원인 비교', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax.set_ylim(0, 108)
ax.legend(loc='upper center', ncol=3, fontsize=LEGEND_SIZE-0.5, framealpha=0.9,
          edgecolor=LIGHT, bbox_to_anchor=(0.5, -0.08))
clean_axes(ax)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph6_end_reason.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 6 done")

# ============================================================
# Graph 7: VsRanged: 환경 조정 전후 비교
# ============================================================
metrics = ['Win Rate\n(%)', 'Hit Rate\n(%)', '접촉률\n(%, P1)', '평균 거리\n(m)']
v1_vals = [0.8, 8.8, 9.7, 35.7]
v2_vals = [73.9, 48.1, 80.1, 30.8]

fig, ax = plt.subplots(figsize=(7, 4.5))
x = np.arange(len(metrics))
w = 0.3
b1 = ax.bar(x - w/2, v1_vals, w, label='v1 (조정 전)', color=LIGHT, edgecolor=BLACK, linewidth=0.8)
b2 = ax.bar(x + w/2, v2_vals, w, label='v2 (조정 후)', color='#444444', edgecolor=BLACK, linewidth=0.8)

for b, v in zip(b1, v1_vals):
    ax.text(b.get_x() + b.get_width()/2, v + 1.5, f'{v}', ha='center', va='bottom',
            fontsize=DATA_LABEL_SIZE, color=DARK)
for b, v in zip(b2, v2_vals):
    ax.text(b.get_x() + b.get_width()/2, v + 1.5, f'{v}', ha='center', va='bottom',
            fontsize=DATA_LABEL_SIZE, fontweight='bold', color=BLACK)

ax.set_xticks(x)
ax.set_xticklabels(metrics, fontsize=TICK_SIZE)
ax.set_title('VsRanged: 환경 조정 전후 비교', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax.set_ylim(0, 100)
ax.legend(fontsize=LEGEND_SIZE, loc='upper right', framealpha=0.9, edgecolor=LIGHT)
clean_axes(ax)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph7_vsranged_v1v2.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 7 done")

# ============================================================
# Graph 8: VsMelee: 승률 수렴 및 보상 개선 추이
# ============================================================
wr_m = [54.2, 58.4, 60.9, 59.5]
rwd_m = [-102.6, -83.7, -52.6, -33.0]

fig, ax1 = plt.subplots(figsize=(7, 4.5))
ax2 = ax1.twinx()
l1 = ax1.plot(quarters, wr_m, 'o-', color=BLACK, linewidth=2, markersize=7, label='Win Rate (%)')
l2 = ax2.plot(quarters, rwd_m, 's--', color=MID, linewidth=2, markersize=7, label='누적 보상')

for i in range(4):
    ax1.annotate(f'{wr_m[i]}%', (quarters[i], wr_m[i]), textcoords="offset points",
                 xytext=(-16, 8), ha='center', fontsize=DATA_LABEL_SIZE, color=BLACK, fontweight='bold')
    ax2.annotate(f'{rwd_m[i]:.0f}', (quarters[i], rwd_m[i]), textcoords="offset points",
                 xytext=(16, -12), ha='center', fontsize=DATA_LABEL_SIZE, color=DARK)

ax1.set_ylabel('Win Rate (%)', fontsize=AXIS_LABEL_SIZE)
ax2.set_ylabel('누적 보상', fontsize=AXIS_LABEL_SIZE)
ax1.set_ylim(45, 70)
ax2.set_ylim(-120, -10)
ax1.set_title('VsMelee: 승률 수렴 및 보상 개선 추이', fontsize=TITLE_SIZE, fontweight='bold', pad=10)
ax1.tick_params(labelsize=TICK_SIZE)
ax2.tick_params(labelsize=TICK_SIZE)
for spine in ['top']:
    ax1.spines[spine].set_visible(False)
    ax2.spines[spine].set_visible(False)
lines = l1 + l2
labels = [l.get_label() for l in lines]
ax1.legend(lines, labels, loc='center right', fontsize=LEGEND_SIZE, framealpha=0.9, edgecolor=LIGHT)
ax1.axhline(y=60, color=MID, linestyle='--', linewidth=0.7)
ax1.text(3.35, 60.7, '60%', fontsize=8, color=MID)
plt.tight_layout()
plt.savefig(os.path.join(OUT, 'graph8_vsmelee_wr_reward.png'), dpi=DPI, bbox_inches='tight', facecolor='white')
plt.close()
print("Graph 8 done")

print("\n=== All 8 graphs regenerated (academic monochrome style, 300 DPI) ===")
