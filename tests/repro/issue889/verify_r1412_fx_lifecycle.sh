#!/bin/sh
# Issue #889 再現調査スクリプト（コード側の構造事実の検証）
#
# ゲームを起動できない環境での再現手段として、出荷済み本体 DLL（r1.4.1.2）から
# ドリームダストと富のエッセンスのコイン獲得演出の「作られ方・消え方」に関わる
# メソッドを ilspycmd で逆コンパイルし、下記の構造事実が本体に実際にあることを
# 確かめる。事実ごとに OK / NG を出す（NG は本体更新で前提が変わったことを意味する）。
#
#   A. 富のエッセンス（Gem_R_Wealth）は撃破ごとに PickupManager.DropGold で
#      ゴールド宝珠（コイン）を落とし、goldSpawnEffect を FxPlayNewNetworked で出す
#   B. DropGold / DropDreamDust は CreatePickupInstance で宝珠 Actor を作る
#   C. PickupInstance.OnPickup は pickupEffect / pickupEffectOnHero を再生し、
#      OnDestroyActor が停止するのは mainEffect だけ
#   D. FxPlayNetworked(pickupEffectOnHero, hero) が使う DewEffect.PlayNetworked
#      (NetworkIdentity, GameObject, Entity) は isNew=false / isPooled=false で送り、
#      クライアントは EffectAutoDestroy への登録なしにその場で再生する
#   E. EffectAutoDestroy は pooled 再生の後始末（OnAttachToEntity(null) と
#      SetActive(false)）と 30 秒の打ち切り・部屋替わりの一掃を持つ
#   F. FxAttachToEntity が対象の Visual._attachedEffects に登録され、
#      FrameUpdate でプレイヤーに追従し、OnDisable で外れる
#   G. Actor は ICustomDestroyRoutine で自分の FX 成分を EffectAutoDestroy に
#      登録して破棄を預ける（FX が止まるまで Actor の物体が残る）
#
# 使い方: sh tests/repro/issue889/verify_r1412_fx_lifecycle.sh
# ilspycmd（~/.dotnet/tools）と本体 DLL（~/dev/sod-gamedata）が無い環境では
# 「skipped」を出して終了する（fail-soft）。

set -u

TOOLS="${HOME}/.dotnet/tools"
ILSPY="${TOOLS}/ilspycmd"
MANAGED="${HOME}/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed"

if [ ! -x "${ILSPY}" ] || [ ! -f "${MANAGED}/Dew.Core.dll" ]; then
    echo "skipped: ilspycmd or game DLLs not found on this machine"
    exit 0
fi
PATH="${PATH}:${TOOLS}"

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT

decomp() { # decomp <型名> <DLL>
    ilspycmd -t "$1" "$2" > "${TMP}/$1.cs" 2>/dev/null
}

check() { # check <事実ID> <ファイル> <grepパターン>
    if grep -q -- "$3" "$2"; then
        echo "OK   $1"
    else
        echo "NG   $1  (pattern not found: $3)"
        FAILED=1
    fi
}

FAILED=0

CORE="${MANAGED}/Dew.Core.dll"
CONTENTS="${MANAGED}/Dew.Contents.dll"

decomp Gem_R_Wealth "${CONTENTS}"
decomp PickupManager "${CORE}"
decomp PickupInstance "${CORE}"
decomp DewEffect "${CORE}"
decomp EffectAutoDestroy "${CORE}"
decomp FxAttachToEntity "${CORE}"
decomp Actor "${CORE}"

# A. 富のエッセンスのコイン獲得演出の作られ方
check "A1 Gem_R_Wealth が PickupManager.DropGold でコインを落とす" \
    "${TMP}/Gem_R_Wealth.cs" "instance.DropGold("
check "A2 Gem_R_Wealth が goldSpawnEffect を FxPlayNewNetworked で出す" \
    "${TMP}/Gem_R_Wealth.cs" "FxPlayNewNetworked(goldSpawnEffect"

# B. 宝珠 Actor の生成
check "B1 DropGold が CreatePickupInstance で宝珠を作る" \
    "${TMP}/PickupManager.cs" "serverActor.CreatePickupInstance"
check "B2 DropDreamDust が CreatePickupInstance でダストを作る" \
    "${TMP}/PickupManager.cs" "_applyPendingDreamDust"

# C. 拾得時の演出と、OnDestroyActor の停止対象
check "C1 OnPickup が pickupEffect を再生する" \
    "${TMP}/PickupInstance.cs" "FxPlayNetworked(pickupEffect"
check "C2 OnPickup が pickupEffectOnHero を再生する" \
    "${TMP}/PickupInstance.cs" "FxPlayNetworked(pickupEffectOnHero"
check "C3 OnDestroyActor が止めるのは mainEffect だけ" \
    "${TMP}/PickupInstance.cs" "FxStop(mainEffect)"

# D. 演出メッセージの形（非プールでその場再生）
#    拾得演出が使う (NetworkIdentity, GameObject, Entity) オーバーロードの本体を
#    取り出し、isNew=false で送り isPooled を立てていないことを確かめる
OVERLOAD="$(sed -n '/PlayNetworked(NetworkIdentity parent, GameObject effect, Entity entity)/,/^	}/p' "${TMP}/DewEffect.cs")"
if [ -n "${OVERLOAD}" ] && echo "${OVERLOAD}" | grep -q "isNew = false" \
    && ! echo "${OVERLOAD}" | grep -q "isPooled"; then
    echo "OK   D1 PlayNetworked が isNew=false で送る（isPooled は立てない）"
else
    echo "NG   D1 PlayNetworked が isNew=false で送る（isPooled は立てない）"
    FAILED=1
fi
check "D2 ハンドラが非プールをその場で再生する分岐を持つ" \
    "${TMP}/DewEffect.cs" "Play(gameObject, msg.entity, msg.parent)"

# E. EffectAutoDestroy の後始末
check "E1 DestroyEntry が OnAttachToEntity(null) で追従を外す" \
    "${TMP}/EffectAutoDestroy.cs" "OnAttachToEntity(null)"
check "E2 DestroyEntry がプールへ SetActive(false) で返す" \
    "${TMP}/EffectAutoDestroy.cs" "gameObject.SetActive(value: false)"
check "E3 Tick が 30 秒の打ち切りを持つ" \
    "${TMP}/EffectAutoDestroy.cs" "time - e.startTime > 30f"
check "E4 部屋替わりで全演出を破棄する" \
    "${TMP}/EffectAutoDestroy.cs" "OnRoomChange"

# F. プレイヤーへの追従のしくみ
check "F1 FxAttachToEntity が Visual._attachedEffects に登録する" \
    "${TMP}/FxAttachToEntity.cs" "_attachedEffects.Add(this)"
check "F2 FrameUpdate が位置を追従更新する" \
    "${TMP}/FxAttachToEntity.cs" "UpdatePosition()"
check "F3 OnDisable で追従を外す" \
    "${TMP}/FxAttachToEntity.cs" "NotifyDetach()"

# G. Actor の破棄預け（FX が残る間は物体が残る）
check "G1 Actor が自分の FX 成分を EffectAutoDestroy に登録する" \
    "${TMP}/Actor.cs" "EffectAutoDestroy.Register(gameObject, _cachedFxComponents)"

echo
if [ "${FAILED}" -eq 0 ]; then
    echo "all structural facts verified against shipped r1.4.1.2 DLLs"
else
    echo "some facts NOT found — the game code may have changed; re-audit before fixing"
fi
exit "${FAILED}"
