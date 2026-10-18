using System;
using System.Collections.Generic;
using SodRpg.Mod;
using UnityEngine;

// （#798）本体 Dew.Core KillTracker.cs（逆コンパイル、2026-10-19 確認）と同じ形の境界。
// 実物はグローバル名前空間の public class。NativeKillTrackerCullPerf の抽出部分が
// typeof(KillTracker) と AccessTools.Field(..., "_trackingDict") で参照する範囲のフィールドと、
// ValidateBody の検証に当てる本体 CullExpiredEntities と同じ命令並びの見本、間引きの等価性の
// 根拠になる本体 ReportKill と同じ命令並びの見本を置く。Time は NativeBoundary.cs、
// Entity・EventInfoKill は SodRpg.Mod のテスト境界のものを使う。
internal class KillTracker
{
    // 本体は private static List<Entity>(16)。掃除の見本が書き込むため同じ形で持つ。
    internal static readonly List<Entity> _expired = new List<Entity>(16);

    internal float _gracePeriod;

    internal Dictionary<Entity, float> _trackingDict = new Dictionary<Entity, float>();

    internal Action<EventInfoKill> _callback;

    // 本体では一度も代入されない（Stop・CullExpiredEntities の登録解除が引くだけ）。
    internal Action<EventInfoKill> _cachedReportKill;

    internal bool _isStopped;

    // 本体 CullExpiredEntities と同じ命令並び（逆コンパイル、2026-10-19 確認）。
    // _trackingDict の foreach（フィールド読み出し）と Dictionary.Remove の呼び出しを含み、
    // 期限切れ（Time.time - 値 > 猶予）か破壊済み（Key == null）の項目だけを除く。
    internal void CullExpiredEntitiesNativeLike()
    {
        foreach (KeyValuePair<Entity, float> item in _trackingDict)
        {
            if (item.Key == null || Time.time - item.Value > _gracePeriod)
            {
                _expired.Add(item.Key);
            }
        }
        if (_expired.Count <= 0)
        {
            return;
        }
        foreach (Entity item2 in _expired)
        {
            _trackingDict.Remove(item2);
            if (item2 != null)
            {
                item2.EntityEvent_OnDeath -= _cachedReportKill;
            }
        }
        _expired.Clear();
    }

    // 辞書への Remove が無い並び（掃除を集計だけにした書き換え）。ValidateBody から外れる。
    internal void CullExpiredEntitiesWithoutRemove()
    {
        foreach (KeyValuePair<Entity, float> item in _trackingDict)
        {
            if (item.Key == null || Time.time - item.Value > _gracePeriod)
            {
                _expired.Add(item.Key);
            }
        }
        _expired.Clear();
    }

    // _trackingDict を一切読まない並び（追跡辞書を差し替えた書き換え）。ValidateBody から外れる。
    internal void CullExpiredEntitiesOnForeignDictionary(Dictionary<Entity, float> dict)
    {
        foreach (KeyValuePair<Entity, float> item in dict)
        {
            if (item.Key == null || Time.time - item.Value > _gracePeriod)
            {
                _expired.Add(item.Key);
            }
        }
        foreach (Entity item2 in _expired)
        {
            dict.Remove(item2);
        }
        _expired.Clear();
    }

    // 本体 ReportKill と同じ命令並び（逆コンパイル、2026-10-19 確認）。TryGetValue の値で
    // 猶予を自分で再判定し、期限切れの項目では何もしない。
    internal void ReportKillNativeLike(EventInfoKill obj)
    {
        if (!_isStopped && _trackingDict.TryGetValue(obj.victim, out var value) && !(Time.time - value > _gracePeriod))
        {
            _trackingDict.Remove(obj.victim);
            _callback?.Invoke(obj);
        }
    }
}
