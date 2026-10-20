using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#901）部屋開始時の依存関係の並べ替えの軽量化（NativeRoomStartDependencyPerf.SortByStartDependency）が
    /// 本体 Dew.Core/Room.GetSortedByStartDependency（r1.4.1.2 逆コンパイル Room.cs:572）と同じ結果を
    /// 返すことの検証。テスト内の ReferenceGetSortedByStartDependency は本体の LINQ 版の写しで、
    /// 依存なし・一方向の連鎖・1つの部件への複数依存・同じ型の部件の重複・継承した属性・環の6つの場面で
    /// 出力の並び（部件の同一性と順序）と環の例外（型とメッセージ）を比べる。
    /// あわせて、型ごとのキャッシュ配列が並べ替えで書き換えられないこと（同じ型が2回出ても
    /// 共有のキャッシュ配列が減らないこと）を確かめる。
    /// </summary>
    public sealed class RoomStartDependencyPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class PlainA : RoomComponent { }
        private sealed class PlainB : RoomComponent { }

        // 一方向の連鎖: Leaf → Middle → Root（→ は「〜に依存する」）。
        private sealed class ChainRoot : RoomComponent { }
        [RoomComponentStartDependency(typeof(ChainRoot))]
        private sealed class ChainMiddle : RoomComponent { }
        [RoomComponentStartDependency(typeof(ChainMiddle))]
        private sealed class ChainLeaf : RoomComponent { }

        // 1つの部件への複数依存: Dependent は Root と Left の両方に依存し、Second も Root に依存する。
        private sealed class MultiRoot : RoomComponent { }
        private sealed class MultiLeft : RoomComponent { }
        [RoomComponentStartDependency(typeof(MultiRoot))]
        [RoomComponentStartDependency(typeof(MultiLeft))]
        private sealed class MultiDependent : RoomComponent { }
        [RoomComponentStartDependency(typeof(MultiRoot))]
        private sealed class MultiSecond : RoomComponent { }

        // 同じ型の部件が複数: Dependent 2つと Target 2つを交互に並べる。
        private sealed class DupTarget : RoomComponent { }
        [RoomComponentStartDependency(typeof(DupTarget))]
        private sealed class DupDependent : RoomComponent { }

        // 継承した属性: DepBase の属性を Derived は継承し、Extra は自身の属性を重ねる。
        private sealed class InheritRoot : RoomComponent { }
        private sealed class InheritOther : RoomComponent { }
        [RoomComponentStartDependency(typeof(InheritRoot))]
        private class InheritDepBase : RoomComponent { }
        private sealed class InheritDerived : InheritDepBase { }
        [RoomComponentStartDependency(typeof(InheritOther))]
        private sealed class InheritExtra : InheritDepBase { }

        // 環: CycleA と CycleB が互いに依存する。
        [RoomComponentStartDependency(typeof(CycleB))]
        private sealed class CycleA : RoomComponent { }
        [RoomComponentStartDependency(typeof(CycleA))]
        private sealed class CycleB : RoomComponent { }

        /// <summary>本体 Dew.Core/Room.cs の GetSortedByStartDependency（LINQ 版）の写し。</summary>
        private static RoomComponent[] ReferenceGetSortedByStartDependency(RoomComponent[] arr)
        {
            RoomComponentStartDependencyAttribute[][] array = new RoomComponentStartDependencyAttribute[arr.Length][];
            for (int i = 0; i < arr.Length; i++)
            {
                object[] customAttributes = arr[i].GetType().GetCustomAttributes(typeof(RoomComponentStartDependencyAttribute), inherit: true);
                array[i] = new RoomComponentStartDependencyAttribute[customAttributes.Length];
                for (int j = 0; j < customAttributes.Length; j++)
                {
                    array[i][j] = (RoomComponentStartDependencyAttribute)customAttributes[j];
                }
            }
            Dictionary<Type, int> dictionary = new Dictionary<Type, int>();
            for (int k = 0; k < arr.Length; k++)
            {
                dictionary[arr[k].GetType()] = k;
            }
            List<RoomComponent> list = new List<RoomComponent>();
            Queue<RoomComponent> queue = new Queue<RoomComponent>();
            foreach (RoomComponent roomComponent in arr)
            {
                if (array[dictionary[roomComponent.GetType()]].Length == 0)
                {
                    queue.Enqueue(roomComponent);
                }
            }
            while (queue.Count > 0)
            {
                RoomComponent rc = queue.Dequeue();
                list.Add(rc);
                for (int m = 0; m < arr.Length; m++)
                {
                    if (array[m].Any((RoomComponentStartDependencyAttribute dep) => dep.targetRoomComponent == rc.GetType()))
                    {
                        array[m] = array[m].Where((RoomComponentStartDependencyAttribute dep) => dep.targetRoomComponent != rc.GetType()).ToArray();
                        if (array[m].Length == 0)
                        {
                            queue.Enqueue(arr[m]);
                        }
                    }
                }
            }
            if (list.Count < arr.Length)
            {
                throw new Exception("Circular dependency detected.");
            }
            return list.ToArray();
        }

        private static RoomComponent[] SortByStartDependency(RoomComponent[] arr)
        {
            MethodInfo method = typeof(NativeRoomStartDependencyPerf).GetMethod("SortByStartDependency", Hidden);
            Assert.NotNull(method);
            return (RoomComponent[])method.Invoke(null, new object[] { arr });
        }

        private static Dictionary<Type, RoomComponentStartDependencyAttribute[]> AttributeCache()
            => (Dictionary<Type, RoomComponentStartDependencyAttribute[]>)typeof(NativeRoomStartDependencyPerf)
                .GetField("AttributeCache", Hidden).GetValue(null);

        private static void AssertSameOrder(RoomComponent[] arr)
        {
            RoomComponent[] expected = ReferenceGetSortedByStartDependency(arr);
            RoomComponent[] actual = SortByStartDependency(arr);
            Assert.Equal(expected, actual);
            Assert.Equal(arr.Length, actual.Length); // 同じ部件が同じ回数だけ並ぶ（参照の同一性は Assert.Equal の要素比較）
        }

        [Fact]
        public void MatchesReferenceWithoutDependencies()
        {
            // 依存なし: 入力の並びを保ったまま全員が出る。
            AssertSameOrder(new RoomComponent[] { new PlainB(), new PlainA(), new PlainB() });
        }

        [Fact]
        public void MatchesReferenceOnOneWayChain()
        {
            // 一方向の連鎖: 逆順に入れても依存元から順に出る。
            AssertSameOrder(new RoomComponent[] { new ChainLeaf(), new ChainMiddle(), new ChainRoot() });
        }

        [Fact]
        public void MatchesReferenceWithMultipleDependenciesOnOneComponent()
        {
            // 1つの部件への複数依存: 片方（Left）が外れても残り（Root）があるうちは並ばない。
            AssertSameOrder(new RoomComponent[]
            {
                new MultiDependent(), new MultiSecond(), new MultiLeft(), new MultiRoot(),
            });
        }

        [Fact]
        public void MatchesReferenceWithDuplicateComponentTypes()
        {
            // 同じ型の部件が複数: 依存側・依存される側とも2つずつ交互に並べる。
            AssertSameOrder(new RoomComponent[]
            {
                new DupDependent(), new DupTarget(), new DupDependent(), new DupTarget(), new PlainA(),
            });
        }

        [Fact]
        public void MatchesReferenceWithInheritedAttributes()
        {
            // 継承した属性: Derived は基本クラスの属性を継承し、Extra は自身の属性を重ねる。
            // inherit 引数が本体（true）と違うと、Derived が依存なしと扱われて並びが変わる。
            AssertSameOrder(new RoomComponent[]
            {
                new InheritDerived(), new InheritExtra(), new InheritRoot(), new InheritOther(),
            });
        }

        [Fact]
        public void ThrowsTheSameCircularDependencyException()
        {
            // 環: 本体と同じ System.Exception（派生ではなくその型）に同じメッセージで落ちる。
            var a = new CycleA();
            var b = new CycleB();
            RoomComponent[] arr = { a, b, new PlainA() };

            Exception reference = Record.Exception(() => ReferenceGetSortedByStartDependency(arr));
            Assert.IsType<Exception>(reference);
            Assert.Equal("Circular dependency detected.", reference.Message);

            var actual = Assert.Throws<TargetInvocationException>(() => SortByStartDependency(arr));
            Assert.IsType<Exception>(actual.InnerException);
            Assert.Equal(reference.Message, actual.InnerException.Message);
        }

        [Fact]
        public void AttributeCacheArrayIsNotRewrittenAcrossCalls()
        {
            // 並べ替えで差し替えるのは呼び出しごとの局所配列だけ。キャッシュした配列は
            // 同じ型が2回出ても2回呼んでも書き換えられず、属性は減らない。
            var first = new DupDependent();
            var second = new DupDependent();
            RoomComponent[] arr = { new DupTarget(), first, new DupTarget(), second };

            RoomComponent[] result1 = SortByStartDependency(arr);
            var cache = AttributeCache();
            Assert.True(cache.TryGetValue(typeof(DupDependent), out RoomComponentStartDependencyAttribute[] entry));

            RoomComponent[] result2 = SortByStartDependency(arr);

            Assert.Same(entry, cache[typeof(DupDependent)]);
            Assert.Single(entry); // 依存が外れたあとでもキャッシュの属性は1つのまま
            Assert.Equal(typeof(DupTarget), entry[0].targetRoomComponent);
            Assert.Equal(result1, result2);
        }
    }
}
