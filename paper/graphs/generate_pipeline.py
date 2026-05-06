import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import matplotlib
import os

matplotlib.rcParams['font.family'] = 'Malgun Gothic'
matplotlib.rcParams['axes.unicode_minus'] = False

OUT = r"c:\Users\paek6\Downloads\Buildup\Buildup\paper\graphs"

fig, ax = plt.subplots(figsize=(10, 5))
ax.set_xlim(0, 10)
ax.set_ylim(0, 6)
ax.axis('off')
fig.patch.set_facecolor('white')

LW = 1.2
EC = 'black'

def box(ax, cx, cy, w, h, text, fontsize=11, bold=False):
    rect = mpatches.FancyBboxPatch(
        (cx - w/2, cy - h/2), w, h,
        boxstyle='square,pad=0',
        facecolor='white', edgecolor=EC, linewidth=LW, zorder=3
    )
    ax.add_patch(rect)
    fw = 'bold' if bold else 'normal'
    ax.text(cx, cy, text, fontsize=fontsize, fontweight=fw,
            ha='center', va='center', zorder=5)

def line(ax, x1, y1, x2, y2):
    ax.plot([x1, x2], [y1, y2], color=EC, linewidth=LW, zorder=2)

def arrow_down(ax, x1, y1, x2, y2):
    ax.annotate('', xy=(x2, y2), xytext=(x1, y1),
                arrowprops=dict(arrowstyle='->', color=EC, lw=LW), zorder=2)

# ── Phase 1 ──
box(ax, 5, 5.4, 6.5, 0.7, 'Phase 1:  통합 기초 학습 (SkillIntro)', 14, bold=True)

arrow_down(ax, 5, 5.0, 5, 4.6)

box(ax, 5, 4.2, 4.0, 0.55, 'Checkpoint (initialize-from)', 12, bold=True)

# ── Branch ──
branch_x = [1.5, 3.8, 6.2, 8.5]
branch_y_top = 3.9
branch_y_mid = 3.4

line(ax, 5, branch_y_top, 5, branch_y_mid)
line(ax, branch_x[0], branch_y_mid, branch_x[3], branch_y_mid)
for x in branch_x:
    arrow_down(ax, x, branch_y_mid, x, 3.0)

# ── Phase 2 cards ──
names = ['VsRanged', 'VsMelee', 'VsHybrid', 'VsCC']
goals = ['추격 / 접근', '반격 / 밀착', '거리 전환 대응', 'CC 돌파']
wrs = ['84.1%', '59.5%', '42.6%', '10.3%']

for i, x in enumerate(branch_x):
    box(ax, x, 2.6, 2.0, 0.55, names[i], 13, bold=True)
    arrow_down(ax, x, 2.3, x, 2.0)
    box(ax, x, 1.65, 2.0, 0.5, goals[i], 11)
    arrow_down(ax, x, 1.35, x, 1.05)
    box(ax, x, 0.7, 2.0, 0.5, f'WR  {wrs[i]}', 13, bold=True)

# ── Labels ──
ax.text(5, -0.05, 'Phase 2:  매치업별 특화 학습', fontsize=13, fontweight='bold',
        ha='center', va='center')

plt.tight_layout(pad=0.3)
plt.savefig(os.path.join(OUT, 'fig1_pipeline.png'), dpi=300, bbox_inches='tight',
            facecolor='white', edgecolor='none')
plt.close()
print("fig1_pipeline.png generated (simplified, large text)")
