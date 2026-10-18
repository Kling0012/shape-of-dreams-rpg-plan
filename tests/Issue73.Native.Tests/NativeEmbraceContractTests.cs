using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// 出荷済み Dew.Core.dll（本体 r1.4.0.13）をリフレクションで読み、
    /// NativeEmbracePositionPerf が前提とする Ai_EmbraceNewIdentity の形（UpdatePositions、
    /// _targetSkillInitPos(Vector3)、_easeFunc(EaseFunction)）と、パッチが記録した本体 IL の
    /// 指紋（長さ・ハッシュ・ローカル数・例外句数）が今も一致することを確かめる。
    /// ゲーム参照は環境変数 SOD_GAME_DIR、なければ docs/testing.md の開発機の既定位置から探し、
    /// 無い環境ではこのテストだけスキップする（ほかのテストには影響しない）。
    /// </summary>
    public sealed class GameDataFactAttribute : FactAttribute
    {
        public GameDataFactAttribute()
        {
            if (NativeEmbraceContractTests.ManagedDir == null)
                Skip = "game reference not found (set SOD_GAME_DIR to the Shape of Dreams install)";
        }
    }

    public sealed class NativeEmbraceContractTests
    {
        internal static readonly string ManagedDir = FindManagedDir();
        private static Assembly _dewCore;

        private static string FindManagedDir()
        {
            var env = Environment.GetEnvironmentVariable("SOD_GAME_DIR");
            var candidates = new[]
            {
                string.IsNullOrEmpty(env) ? null : Path.Combine(env, "Shape of Dreams_Data", "Managed"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "dev", "sod-gamedata", "Shape of Dreams", "Shape of Dreams_Data", "Managed"),
            };
            foreach (var candidate in candidates)
                if (candidate != null && File.Exists(Path.Combine(candidate, "Dew.Core.dll")))
                    return candidate;
            return null;
        }

        private static Assembly LoadDewCore()
        {
            if (_dewCore != null) return _dewCore;
            var dir = ManagedDir;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    // ゲーム DLL（Dew.Core など）からの解決要求だけ相手にする。
                    var requesting = args.RequestingAssembly;
                    if (requesting == null || !requesting.Location.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        return null;
                    var path = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFile(path) : null;
                }
                catch { return null; }
            };
            return _dewCore = Assembly.LoadFile(Path.Combine(dir, "Dew.Core.dll"));
        }

        [GameDataFact]
        public void ShippedDewCoreExposesTheMembersTheEmbracePatchReads()
        {
            var type = LoadDewCore().GetType("Ai_EmbraceNewIdentity");
            Assert.NotNull(type);

            var method = type.GetMethod("UpdatePositions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Equal("System.Void", method.ReturnType.FullName);
            Assert.Empty(method.GetParameters());

            var initPos = type.GetField("_targetSkillInitPos", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(initPos);
            Assert.Equal("UnityEngine.Vector3", initPos.FieldType.FullName);

            var easeFunc = type.GetField("_easeFunc", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(easeFunc);
            Assert.Equal("EaseFunction", easeFunc.FieldType.FullName);
            Assert.Equal(typeof(MulticastDelegate), easeFunc.FieldType.BaseType);
            var invoke = easeFunc.FieldType.GetMethod("Invoke");
            Assert.NotNull(invoke);
            Assert.Equal("System.Single", invoke.ReturnType.FullName);
            Assert.Equal(3, invoke.GetParameters().Length);

            var skill = type.GetProperty("NetworktargetSkill");
            Assert.NotNull(skill);
            Assert.NotNull(skill.GetGetMethod(true));
            Assert.Equal("SkillTrigger", skill.PropertyType.FullName);
        }

        [GameDataFact]
        public void RecordedIlFingerprintMatchesShippedUpdatePositions()
        {
            var type = LoadDewCore().GetType("Ai_EmbraceNewIdentity");
            var method = type.GetMethod("UpdatePositions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(NativeEmbracePositionPerf.GuardNativeBody(method),
                "本体 UpdatePositions の IL が記録した指紋とずれた。NativeEmbracePositionPerf の定数を更新して精査する。");
        }
    }
}
