using System;
using System.IO;
using System.Reflection;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    // 保存エラーの表示：例外の表示を同じ呼び出しで消さない、失敗中の表示文を毎フレーム作り直さない。
    public sealed class SaveErrorDisplayTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sod-saveerr-" + Guid.NewGuid().ToString("N"));
        private readonly FlakyFileSystem _files = new FlakyFileSystem();
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly bool _previousJapanese = Loc.Japanese;
        private ClientSession _session;

        public SaveErrorDisplayTests() { Log.ErrorSink = _ => { }; }

        [Fact]
        public void Exception_during_save_stays_visible_after_an_earlier_successful_write()
        {
            var session = CreateSession();
            session.SaveNow();
            session.FlushSaves();
            Assert.Null(session.SaveError);

            session.Profile.InterruptedRelics.Add(null);
            session.SaveNow();

            Assert.NotNull(session.SaveError);
        }

        [Fact]
        public void Write_failure_text_is_reused_until_the_cause_or_language_changes()
        {
            var session = CreateSession();
            _files.FailWrites = true;
            session.SaveNow();
            session.FlushSaves();

            Loc.Japanese = true;
            Call(session, "TickPeriodicSave");
            string first = session.SaveError;
            Assert.StartsWith("保存に失敗しました", first);
            Call(session, "TickPeriodicSave");
            Assert.Same(first, session.SaveError);

            Loc.Japanese = false;
            Call(session, "TickPeriodicSave");
            Assert.StartsWith("Saving failed", session.SaveError);
        }

        private ClientSession CreateSession()
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 0;
            Directory.CreateDirectory(_directory);
            var profile = Profile.CreateNew(301);
            var store = new ProfileStore(_files, Path.Combine(_directory, "profile.json"), 301);
            store.Save(profile);
            _session = new ClientSession(null) { Profile = profile, LocalHero = new Hero() };
            typeof(ClientSession).GetField("_store", Hidden).SetValue(_session, store);
            return _session;
        }

        public void Dispose()
        {
            _files.FailWrites = false;
            try { _session?.FlushSaves(); } catch { }
            Log.ErrorSink = _previousErrorSink;
            Loc.Japanese = _previousJapanese;
            NetworkClient.active = false;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static void Call(object target, string method) =>
            typeof(ClientSession).GetMethod(method, Hidden).Invoke(target, null);

        private sealed class FlakyFileSystem : IFileSystem
        {
            private readonly RealFileSystem _real = new RealFileSystem();
            internal volatile bool FailWrites;
            public bool Exists(string path) => _real.Exists(path);
            public string ReadAllText(string path) => _real.ReadAllText(path);
            public void WriteAllText(string path, string contents)
            {
                if (FailWrites) throw new IOException("Injected disk failure");
                _real.WriteAllText(path, contents);
            }
            public void Replace(string temp, string dest, string backupOrNull) => _real.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _real.Copy(source, dest, overwrite);
            public void Delete(string path) => _real.Delete(path);
        }
    }
}
