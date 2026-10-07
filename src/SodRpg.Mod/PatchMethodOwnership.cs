using System;
using System.Reflection;
using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>
    /// Harmony のパッチ情報から「このクラスが入れたパッチ」を探す、再読み込みに耐える方法。
    /// 本体（DewMod.Load）はMOD DLLをアセンブリ名に時刻印を付けて書き直し、丸ごと読み込む。旧コピーはアンロード
    /// されず、書き直しはモジュールID（MVID）を保存するため、MOD更新の再読み込み後は同じMVIDのモジュールが
    /// 同じプロセスに2つ載る。Harmony はパッチ状態を直列化して保存し、読み出しのたびにパッチメソッドを
    /// 「module GUID＋メタデータトークン」で最初に見つかったモジュール（＝旧コピー）へ解決し直す。そのため
    /// <see cref="Patch.PatchMethod"/> の宣言型を今のクラスと「参照」で比較すると必ず旧コピー側に外れ、
    /// 入ったばかりのパッチが「未導入」と誤検出される（報告「Infinity patch was not installed …」）。
    /// 完全名での比較はどちらのコピーに解決しても一致し、所有者IDはMODの読み込みごとに一意なので、
    /// 「所有者＋完全名」が他のインスタンスや他クラスのパッチを拾うこともない。
    /// </summary>
    internal static class PatchMethodOwnership
    {
        /// <summary>パッチがこのクラス（またはその入れ子の型）で宣言されたかどうか。解決できないパッチは所属不明として扱う。</summary>
        internal static bool DeclaresPatch(Patch patch, Type patchClass)
        {
            if (patch == null || patchClass == null) return false;
            try
            {
                for (var t = patch.PatchMethod?.DeclaringType; t != null; t = t.DeclaringType)
                    if (t.FullName == patchClass.FullName) return true;
            }
            catch (Exception)
            {
                // モジュールが解決できないパッチは比較の対象にできない。失敗を偽装しない（所属なしと扱うだけ）。
            }
            return false;
        }
    }
}
