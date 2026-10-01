using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Internal;

namespace SodRpg.Core
{
    public sealed class ParsedLedger
    {
        internal ParsedLedger(LedgerState state, int sourceVersion, List<string> notes)
        {
            State = state;
            SourceVersion = sourceVersion;
            Notes = notes;
        }

        public LedgerState State { get; }
        /// <summary>ファイル上の版。現行版より小さければ移行済み。</summary>
        public int SourceVersion { get; }
        public bool Migrated => SourceVersion < LedgerState.CurrentSchemaVersion;
        public List<string> Notes { get; }
    }

    /// <summary>
    /// 台帳 ⇔ ファイル文字列。形式: {"checksum":"&lt;sha256&gt;","body":{...}}。
    /// 版0（旧形式）はラッパーなしの本体のみで、読み込み時に現行版へ移行する。
    /// </summary>
    public static class LedgerSerializer
    {
        private const int MaxRawLength = 500;

        public static string ToFileText(LedgerState state)
        {
            JsonObject body = ToBody(state);
            string checksum = Sha256Hex(Json.Write(body));
            return Json.Write(new JsonObject().Add("checksum", checksum).Add("body", body));
        }

        /// <summary>構造が不正（壊れ・欠落・チェックサム不一致）なら LedgerFormatException、新しすぎる版なら LedgerVersionException。</summary>
        public static ParsedLedger Parse(string text, Catalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            object root = Json.Parse(text);
            var rootObj = root as JsonObject ?? throw new LedgerFormatException("ルートがオブジェクトではありません。");

            JsonObject body;
            if (rootObj.TryGet("checksum", out object cs) && rootObj.TryGet("body", out object b))
            {
                body = b as JsonObject ?? throw new LedgerFormatException("bodyがオブジェクトではありません。");
                string expected = cs as string ?? throw new LedgerFormatException("checksumが文字列ではありません。");
                if (!string.Equals(expected, Sha256Hex(Json.Write(body)), StringComparison.OrdinalIgnoreCase))
                    throw new LedgerFormatException("チェックサムが一致しません。");
            }
            else
            {
                body = rootObj; // 版0（ラッパーなし）
            }

            int version = body.TryGet("schemaVersion", out object sv) ? ToInt(sv, "schemaVersion") : 0;
            if (version < 0) throw new LedgerFormatException("schemaVersionが負です。");
            if (version > LedgerState.CurrentSchemaVersion)
                throw new LedgerVersionException("この版(" + version + ")は未対応です。MODを更新してください。");
            if (version >= 1 && !(rootObj.TryGet("checksum", out _)))
                throw new LedgerFormatException("版" + version + "のファイルにチェックサムがありません。");

            var notes = new List<string>();
            string profileKey = GetString(body, "profileKey");
            long revision = 0;
            if (version >= 1)
            {
                revision = GetLong(body, "revision");
                if (revision < 0) throw new LedgerFormatException("revisionが負です。");
            }

            var state = new LedgerState(profileKey, revision);

            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object el in GetArray(body, "items", optional: true))
            {
                var o = el as JsonObject ?? throw new LedgerFormatException("itemsの要素がオブジェクトではありません。");
                string itemId = GetString(o, "itemId");
                string instanceId = GetString(o, "instanceId");
                string acquiredBy = o.TryGet("acquiredBy", out object ab) ? (ab as string ?? throw new LedgerFormatException("acquiredByが文字列ではありません。")) : string.Empty;
                if (!catalog.HasItem(itemId))
                {
                    Quarantine(state, notes, "item", "未定義のアイテムID: " + itemId, o);
                    continue;
                }
                if (!instanceIds.Add(instanceId))
                {
                    Quarantine(state, notes, "item", "インスタンスIDが重複: " + instanceId, o);
                    continue;
                }
                state.AddItem(new ItemRecord(itemId, instanceId, acquiredBy));
            }

            if (body.TryGet("materials", out object mats))
            {
                var mo = mats as JsonObject ?? throw new LedgerFormatException("materialsがオブジェクトではありません。");
                foreach (var kv in mo.Properties)
                {
                    if (!(kv.Value is long n)) throw new LedgerFormatException("materialsの値が整数ではありません: " + kv.Key);
                    if (!catalog.TryGetMaterialCap(kv.Key, out int cap))
                    {
                        Quarantine(state, notes, "material", "未定義の素材ID: " + kv.Key, new JsonObject().Add(kv.Key, kv.Value));
                        continue;
                    }
                    if (n < 0 || n > cap)
                    {
                        Quarantine(state, notes, "material", "所持数が範囲外(0〜" + cap + "): " + kv.Key, new JsonObject().Add(kv.Key, kv.Value));
                        continue;
                    }
                    if (n > 0) state.SetMaterial(kv.Key, (int)n);
                }
            }

            if (version >= 1)
            {
                foreach (object el in GetArray(body, "appliedGrants", optional: true))
                {
                    var o = el as JsonObject ?? throw new LedgerFormatException("appliedGrantsの要素がオブジェクトではありません。");
                    string grantId = GetString(o, "grantId");
                    string defId = GetString(o, "defId");
                    long qty = GetLong(o, "quantity");
                    if (qty < 0 || qty > int.MaxValue) throw new LedgerFormatException("appliedGrantsのquantityが範囲外です。");
                    if (state.HasApplied(grantId))
                    {
                        Quarantine(state, notes, "grant", "報酬IDが重複: " + grantId, o);
                        continue;
                    }
                    state.RecordGrant(new AppliedGrant(grantId, defId, (int)qty));
                }

                foreach (object el in GetArray(body, "quarantine", optional: true))
                {
                    var o = el as JsonObject ?? throw new LedgerFormatException("quarantineの要素がオブジェクトではありません。");
                    state.AddQuarantine(new QuarantineEntry(GetString(o, "kind", allowEmpty: true), GetString(o, "reason", allowEmpty: true), GetString(o, "raw", allowEmpty: true)));
                }
            }
            else
            {
                notes.Add("旧形式(版0)から版" + LedgerState.CurrentSchemaVersion + "へ移行しました。");
            }

            return new ParsedLedger(state, version, notes);
        }

        private static void Quarantine(LedgerState state, List<string> notes, string kind, string reason, JsonObject raw)
        {
            string rawText = Json.Write(raw);
            if (rawText.Length > MaxRawLength) rawText = rawText.Substring(0, MaxRawLength);
            state.AddQuarantine(new QuarantineEntry(kind, reason, rawText));
            notes.Add("隔離: " + reason);
        }

        private static JsonObject ToBody(LedgerState s)
        {
            var items = new List<object>();
            foreach (var i in s.Items)
                items.Add(new JsonObject().Add("itemId", i.ItemId).Add("instanceId", i.InstanceId).Add("acquiredBy", i.AcquiredBy));

            var mats = new JsonObject();
            foreach (var kv in s.Materials)
                if (kv.Value > 0) mats.Add(kv.Key, (long)kv.Value);

            var grants = new List<object>();
            foreach (var g in s.AppliedGrants)
                grants.Add(new JsonObject().Add("grantId", g.GrantId).Add("defId", g.DefId).Add("quantity", (long)g.Quantity));

            var quarantine = new List<object>();
            foreach (var q in s.Quarantine)
                quarantine.Add(new JsonObject().Add("kind", q.Kind).Add("reason", q.Reason).Add("raw", q.Raw));

            return new JsonObject()
                .Add("schemaVersion", (long)LedgerState.CurrentSchemaVersion)
                .Add("profileKey", s.ProfileKey)
                .Add("revision", s.Revision)
                .Add("items", items)
                .Add("materials", mats)
                .Add("appliedGrants", grants)
                .Add("quarantine", quarantine);
        }

        internal static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string GetString(JsonObject o, string key, bool allowEmpty = false)
        {
            if (!o.TryGet(key, out object v)) throw new LedgerFormatException("必須項目がありません: " + key);
            if (!(v is string s)) throw new LedgerFormatException("文字列ではありません: " + key);
            if (!allowEmpty && s.Length == 0) throw new LedgerFormatException("空の値です: " + key);
            return s;
        }

        private static long GetLong(JsonObject o, string key)
        {
            if (!o.TryGet(key, out object v)) throw new LedgerFormatException("必須項目がありません: " + key);
            if (!(v is long l)) throw new LedgerFormatException("整数ではありません: " + key);
            return l;
        }

        private static int ToInt(object v, string key)
        {
            if (!(v is long l) || l < int.MinValue || l > int.MaxValue) throw new LedgerFormatException("整数ではありません: " + key);
            return (int)l;
        }

        private static List<object> GetArray(JsonObject o, string key, bool optional)
        {
            if (!o.TryGet(key, out object v))
            {
                if (optional) return new List<object>();
                throw new LedgerFormatException("必須項目がありません: " + key);
            }
            return v as List<object> ?? throw new LedgerFormatException("配列ではありません: " + key);
        }
    }
}
