using System;
using System.Collections.Generic;
using System.IO;

namespace SodRpg.Core
{
    public sealed class LoadResult
    {
        internal LoadResult(LedgerState state, List<string> notes, bool createdNew)
        {
            State = state;
            Notes = notes;
            CreatedNew = createdNew;
        }

        public LedgerState State { get; }
        /// <summary>利用者へ表示する復旧・隔離・移行の結果。空なら何も起きていない。</summary>
        public IReadOnlyList<string> Notes { get; }
        /// <summary>ファイルが1つも無く、空の台帳から始めた。</summary>
        public bool CreatedNew { get; }
    }

    /// <summary>
    /// プロフィールごとの独立保存。本体のセーブには触れない。
    /// 書込み: 一時ファイル → 置換（置換前の本体は .bak へ）。確定点は置換の完了で、一時ファイルは読み込みに使わない
    /// （確定前の内容を読むと、ackを返していない付与を「あった」ことにして、後の書込み失敗で失う恐れがあるため）。
    /// 読込み: 本体・バックアップのうち有効でrevisionが最大のものを採用し、どちらも有効でなければ読み込みを止める
    /// （空で続行しない）。
    /// </summary>
    public sealed class LedgerStore
    {
        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly string _profileKey;
        private readonly Catalog _catalog;

        private bool _mainIsValid;
        private string _legacySourcePath;
        private int _legacyVersion;

        public LedgerStore(IFileSystem fs, string path, string profileKey, Catalog catalog)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _profileKey = profileKey ?? throw new ArgumentNullException(nameof(profileKey));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public string TempPath => _path + ".tmp";
        public string BackupPath => _path + ".bak";
        public string CorruptPath => _path + ".corrupt";

        /// <summary>最後に Load した、または Mutate で保存した内容。</summary>
        public LedgerState State { get; private set; }

        public string ProfileKey => _profileKey;

        public LoadResult Load()
        {
            var notes = new List<string>();
            ParsedLedger best = null;
            string bestPath = null;
            bool mainValid = false;
            bool anyFileExisted = false;

            foreach (string candidate in new[] { _path, BackupPath })
            {
                if (!_fs.Exists(candidate)) continue;
                anyFileExisted = true;
                ParsedLedger parsed;
                try
                {
                    parsed = LedgerSerializer.Parse(_fs.ReadAllText(candidate), _catalog);
                }
                catch (LedgerVersionException e)
                {
                    // 新しい版のファイルは、他の候補で上書きして失わないよう読み込みごと止める
                    throw new LedgerCorruptException(Path.GetFileName(candidate) + ": " + e.Message);
                }
                catch (Exception e) when (e is LedgerFormatException || e is IOException)
                {
                    notes.Add(Path.GetFileName(candidate) + " は無効です: " + e.Message);
                    continue;
                }

                if (!string.Equals(parsed.State.ProfileKey, _profileKey, StringComparison.Ordinal))
                {
                    notes.Add(Path.GetFileName(candidate) + " は別のプロフィールのものです。使用しません。");
                    continue;
                }

                if (candidate == _path) mainValid = true;
                if (best == null || parsed.State.Revision > best.State.Revision)
                {
                    best = parsed;
                    bestPath = candidate;
                }
            }

            if (best == null)
            {
                if (anyFileExisted)
                {
                    throw new LedgerCorruptException(
                        "保存ファイルがすべて無効です。ファイルは変更していません。バックアップ(.bak)や .corrupt を確認してください。 " + string.Join(" / ", notes));
                }
                State = new LedgerState(_profileKey);
                _mainIsValid = false;
                _legacySourcePath = null;
                return new LoadResult(State, notes, true);
            }

            if (State != null && best.State.Revision < State.Revision)
                notes.Add("保存内容のrevisionが戻っています(" + State.Revision + "→" + best.State.Revision + ")。古いファイルが復元された可能性があります。");
            if (bestPath != _path)
                notes.Add(Path.GetFileName(bestPath) + " から復旧しました。");
            notes.AddRange(best.Notes);

            _mainIsValid = mainValid;
            _legacySourcePath = best.Migrated ? bestPath : null;
            _legacyVersion = best.SourceVersion;
            State = best.State;
            return new LoadResult(State, notes, false);
        }

        /// <summary>
        /// 複製に変更を加え、revisionを進めて保存し、成功後に差し替える。
        /// 保存で IOException が出た場合は State を変えない。ただし一時ファイルに新しい内容が残り得るため、
        /// 呼び出し側は失敗後に Load() で状態を同期し直すこと。
        /// </summary>
        public void Mutate(Action<LedgerState> change)
        {
            if (State == null) throw new InvalidOperationException("先に Load() を呼んでください。");
            if (change == null) throw new ArgumentNullException(nameof(change));

            LedgerState next = State.Clone();
            change(next);
            next.Revision = State.Revision + 1;
            Validate(next);
            Save(next);
            State = next;
        }

        private void Validate(LedgerState s)
        {
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in s.Items)
            {
                if (!_catalog.HasItem(i.ItemId)) throw new InvalidOperationException("未定義のアイテムIDです: " + i.ItemId);
                if (string.IsNullOrEmpty(i.InstanceId) || !instanceIds.Add(i.InstanceId))
                    throw new InvalidOperationException("インスタンスIDが空または重複しています: " + i.InstanceId);
            }
            foreach (var kv in s.Materials)
            {
                if (!_catalog.TryGetMaterialCap(kv.Key, out int cap)) throw new InvalidOperationException("未定義の素材IDです: " + kv.Key);
                if (kv.Value < 0 || kv.Value > cap) throw new InvalidOperationException("素材の所持数が範囲外です: " + kv.Key);
            }
        }

        private void Save(LedgerState state)
        {
            string text = LedgerSerializer.ToFileText(state);

            // 旧形式から移行した最初の保存では、元ファイルを版つきの別名で残す
            if (_legacySourcePath != null && _fs.Exists(_legacySourcePath))
            {
                string legacyBackup = _path + ".v" + _legacyVersion + ".bak";
                if (!_fs.Exists(legacyBackup)) _fs.Copy(_legacySourcePath, legacyBackup, false);
            }

            _fs.WriteAllText(TempPath, text);

            if (_fs.Exists(_path))
            {
                if (_mainIsValid)
                {
                    _fs.Replace(TempPath, _path, BackupPath);
                }
                else
                {
                    // 無効な本体で、有効なバックアップを上書きしない
                    _fs.Copy(_path, CorruptPath, true);
                    _fs.Replace(TempPath, _path, null);
                }
            }
            else
            {
                _fs.Replace(TempPath, _path, null);
            }

            _mainIsValid = true;
            _legacySourcePath = null;
        }
    }
}
