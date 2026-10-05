# 星図の価値と夢の圧（課税）の概算モデル。docs/specs/v2.2-power-curve-review.md の表を再現する。
# 使い方: python3 tools/power-curve/model.py
# 前提（概算）：夢レベル30。星で振った点は記憶の冴え（MemoryDamage）へ最初の123点で平均+103%、
# 次の250点で+40%まで。ダメージの90%が記憶経由。能力値・加速・仕掛けの小さな星が500点で+20%。
# P は本体の「ダメージ増幅」の加算プール（エッセンス等が作る +P×100%）。MODの記憶の冴えが同じプールに
# 入る（加算）なら価値は 1/(1+P) に薄まり、別枠の乗算なら薄まらない。P の実値は実機で測る（仕様書の6章）。
import math
def tax(p, c_hp=0.005, c_dm=0.0025, lvl=25):
    hp = (1+0.025*lvl+c_hp*p)/(1+0.025*lvl)
    dm = (1+0.012*lvl+c_dm*p)/(1+0.012*lvl)
    return hp, dm
def m_avg(p, scale):
    a = min(p,123)/123*1.03
    b = min(max(p-123,0),250)/250*0.40
    return (a+b)*scale
def gross(p, P, mult, scale, f=0.9):
    m = m_avg(p, scale)
    dps = 1 + f*m*(1 if mult else 1/(1+P))
    other = 1 + 0.20*p/500   # 能力値・加速・仕掛けの小さな星の寄与（概算）
    return dps*other
def surv(p): return 1+0.15*p/500
rows=[]
print("P layer scale | p=125 TTK/Tax  | p=250 | p=500")
for P in (0,3,9):
  for mult in (False,True):
    for scale in (1,2):
      if P==0 and mult: continue
      out=[]
      for p in (125,250,500):
        hp,dm=tax(p)
        g=gross(p,P,mult,scale)
        out.append(f"{g:4.2f}/{hp:4.2f}={g/hp:4.2f}")
      print(f"P={P} {'乗算' if mult else '加算'} x{scale} | "+" | ".join(out))
print()
print("coef sweep (additive, P=3, scale1) TTK ratio at p=500 for c_hp")
for c in (0.005,0.0035,0.0025,0.0015):
    hp,dm=tax(500,c_hp=c,c_dm=c/2)
    print(c, f"hp x{hp:.2f} dm x{dm:.2f}", f"{gross(500,3,False,1)/hp:.2f}", f"{gross(500,3,True,2)/hp:.2f}")
print()
print("tax table lvl30:")
for p in (0,100,125,250,500):
    hp,dm=tax(p); print(p, f"{(1+0.625+0.005*p):.3f}", f"{(1.3+0.0025*p):.3f}", f"rel HP x{hp:.2f} DM x{dm:.2f}")

print()
print("星の位階倍率 r(p)=1+k*p/500 を星由来のダメージ値に掛け、乗算の別枠にした場合（TTK比 = 与ダメージ倍率 / 敵HPの課税）")
for k in (0, 1.0, 1.5, 2.0):
    out = []
    for p in (125, 250, 500):
        hp, dm = tax(p)
        g = gross(p, 0, True, 1 + k * p / 500)
        out.append(f"{g:4.2f}/{hp:4.2f}={g/hp:4.2f}")
    print(f"k={k:3.1f} | " + " | ".join(out))
