using System;
using System.Collections.Generic;
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
        // Startup diagnostics only. Never invoke Prepare/TargetMethod(s) again to discover a target.
        internal static Type StartupPatchClass;
        internal static MethodBase StartupTarget;
        private static string _startupOwner;
        private static readonly Dictionary<Type, HashSet<MethodBase>> StartupTargets =
            new Dictionary<Type, HashSet<MethodBase>>();

        internal static void BeginStartup(string owner)
        {
            _startupOwner = owner;
            StartupPatchClass = null;
            StartupTarget = null;
            StartupTargets.Clear();
        }

        internal static void EndStartup()
        {
            _startupOwner = null;
            StartupPatchClass = null;
            StartupTarget = null;
            StartupTargets.Clear();
        }

        internal static void RecordStartupTarget(Type patchClass, MethodBase target)
        {
            if (_startupOwner == null || patchClass == null || target == null) return;
            try
            {
                if (!StartupTargets.TryGetValue(patchClass, out var targets))
                    StartupTargets.Add(patchClass, targets = new HashSet<MethodBase>());
                targets.Add(target);
            }
            catch (Exception) { /* Diagnostic collection must not reject a patch. */ }
        }

        internal static string StartupForeignOwners()
            => ForeignOwners(StartupPatchClass, _startupOwner, StartupTarget);

        internal static string ForeignOwners(Type patchClass, string owner, MethodBase target = null)
        {
            if (string.IsNullOrEmpty(owner)) return "";
            try
            {
                var targets = new HashSet<MethodBase>();
                if (target != null) targets.Add(target);
                else if (patchClass != null)
                {
                    if (StartupTargets.TryGetValue(patchClass, out var known)) targets.UnionWith(known);
                    // Native performance patches retain the targets resolved by their existing Prepare.
                    foreach (var field in patchClass.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        // Other MethodInfo fields can be helper calls, not this class's native targets.
                        if (field.Name != "_target" && field.Name != "_targets" && field.Name != "_publish") continue;
                        if (typeof(MethodBase).IsAssignableFrom(field.FieldType))
                        {
                            if (field.GetValue(null) is MethodBase resolved) targets.Add(resolved);
                        }
                        else if (field.FieldType == typeof(MethodBase[]))
                        {
                            if (field.GetValue(null) is MethodBase[] resolved) targets.UnionWith(resolved);
                        }
                    }
                    var attribute = HarmonyMethodExtensions.GetMergedFromType(patchClass);
                    if (attribute.declaringType != null && attribute.methodName != null)
                    {
                        var resolved = AccessTools.DeclaredMethod(attribute.declaringType, attribute.methodName, attribute.argumentTypes);
                        if (resolved != null) targets.Add(resolved);
                    }
                }
                var owners = new HashSet<string>(StringComparer.Ordinal);
                foreach (var original in targets)
                {
                    if (original == null) continue;
                    var info = Harmony.GetPatchInfo(original);
                    if (info == null) continue;
                    foreach (string id in info.Owners)
                        if (!string.IsNullOrEmpty(id) && id != owner) owners.Add(id);
                }
                if (owners.Count == 0) return "";
                var sorted = new List<string>(owners);
                sorted.Sort(StringComparer.Ordinal);
                return " (other Harmony owners: " + string.Join(", ", sorted) + ")";
            }
            catch (Exception) { return ""; }
        }

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
