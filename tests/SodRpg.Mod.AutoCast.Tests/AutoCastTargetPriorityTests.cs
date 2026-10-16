using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.AutoCast.Tests
{
    // HostAuthority.AutoCastTargets.cs の実物（接触記録と選敵順位）をスタブ上で動かす。
    // 順位: 自分が AutoCastOwnHitSeconds 以内に当てた敵・自分を狙う敵 = 0、
    // 味方だけが AutoCastAllyHitSeconds 以内に当てた敵 = 2（自分を狙っていても 2）、他 = 1。
    public sealed class AutoCastTargetPriorityTests
    {
        public AutoCastTargetPriorityTests()
        {
            Mirror.NetworkServer.active = true;
            UnityEngine.Time.time = 0f;
            Log.Warnings.Clear();
            DewPlayer.allHumanPlayers.Clear();
        }

        private sealed class Room
        {
            public readonly HostAuthority Host = new HostAuthority();
            public readonly Hero Me = new Hero();
            public readonly Hero Ally = new Hero();
            public readonly Entity Enemy = new Entity { Relation = EntityRelation.Enemy };
            public readonly DewPlayer MePlayer, AllyPlayer;

            public Room()
            {
                MePlayer = new DewPlayer { hero = Me };
                AllyPlayer = new DewPlayer { hero = Ally };
                DewPlayer.allHumanPlayers.Add(MePlayer);
                DewPlayer.allHumanPlayers.Add(AllyPlayer);
            }

            public void Record(Actor attacker, float at, float amount = 10f)
            {
                UnityEngine.Time.time = at;
                Host.RecordAutoCastContactForTest(new EventInfoDamage
                {
                    victim = Enemy,
                    actor = attacker,
                    damage = new DamageData { amount = amount }
                });
            }

            public int Rank(float now, bool targetsMe = false)
            {
                if (targetsMe) Enemy.AI = new AiBrain { context = { targetEnemy = Me } };
                return Host.AutoCastTargetPriority(Me, Enemy, now);
            }
        }

        [Fact]
        public void Enemies_hit_by_this_hero_or_aiming_at_it_rank_first()
        {
            var room = new Room();
            room.Record(room.Me, 10f);
            Assert.Equal(0, room.Rank(12.9f));                  // 3秒以内の自分の命中
            Assert.Equal(0, room.Rank(12.9f, targetsMe: true)); // 狙われても同じ順位

            // スキル由来の命中も本人に帰属する。
            var skillRoom = new Room();
            skillRoom.Record(new SkillTrigger { owner = skillRoom.Me, parentActor = skillRoom.Me }, 10f);
            Assert.Equal(0, skillRoom.Rank(11f));

            // 命中がなくても、自分を狙う敵（AI目標・攻撃対象）は 0。
            var aiRoom = new Room();
            aiRoom.Enemy.AI = new AiBrain { context = { targetEnemy = aiRoom.Me } };
            Assert.Equal(0, aiRoom.Rank(0f));
            var attackRoom = new Room();
            attackRoom.Enemy.Control.attackTarget = attackRoom.Me;
            Assert.Equal(0, attackRoom.Rank(0f));
        }

        [Fact]
        public void Ally_only_enemies_rank_last_even_while_aiming_at_this_hero()
        {
            var room = new Room();
            room.Record(room.Ally, 10f);
            Assert.Equal(2, room.Rank(11.5f, targetsMe: true)); // 自分を狙っていても 2
            Assert.Equal(2, room.Rank(11.5f));                  // 狙っていなければ当然 2

            // 自分の命中が切れていれば、味方の命中だけが残る敵は 2。
            var staleRoom = new Room();
            staleRoom.Record(staleRoom.Me, 5f);
            staleRoom.Record(staleRoom.Ally, 11f);
            Assert.Equal(2, staleRoom.Rank(11.5f, targetsMe: true));
        }

        [Fact]
        public void Own_recent_hit_wins_over_an_ally_hit_on_the_same_enemy()
        {
            var room = new Room();
            room.Record(room.Me, 11f);
            room.Record(room.Ally, 11.5f);
            Assert.Equal(0, room.Rank(12f));
            Assert.Equal(0, room.Rank(12f, targetsMe: true));
        }

        [Fact]
        public void Uninvolved_enemies_rank_between_and_hit_windows_expire()
        {
            var room = new Room();
            Assert.Equal(1, room.Rank(0f)); // 誰も関与しない敵は 1

            var ownRoom = new Room();
            ownRoom.Record(ownRoom.Me, 10f);
            Assert.Equal(0, ownRoom.Rank(13f));      // ちょうど 3 秒ではまだ自分の敵
            Assert.Equal(1, ownRoom.Rank(13.05f));   // 3 秒を過ぎれば 1 へ戻る（時間切れ）
            Assert.Equal(0, ownRoom.Rank(13.05f, targetsMe: true)); // 狙われていれば 0 のまま

            var allyRoom = new Room();
            allyRoom.Record(allyRoom.Ally, 10f);
            Assert.Equal(2, allyRoom.Rank(12f));     // ちょうど 2 秒ではまだ後回し
            Assert.Equal(1, allyRoom.Rank(12.05f));  // 2 秒を過ぎれば 1 へ戻る
        }

        [Fact]
        public void Contacts_are_recorded_only_for_real_damage_in_multiplayer()
        {
            // 与ダメージ 0 は命中として数えない。
            var zeroRoom = new Room();
            zeroRoom.Record(zeroRoom.Me, 10f, amount: 0f);
            Assert.Equal(1, zeroRoom.Rank(11f));

            // 味方関係にない攻撃者の命中は本人の順位に使えない。
            var hostileRoom = new Room();
            hostileRoom.Ally.Relation = EntityRelation.Enemy;
            hostileRoom.Record(hostileRoom.Ally, 10f);
            Assert.Equal(1, hostileRoom.Rank(11f));

            // ソロ（人間1人）では接触を記録しない。
            var soloRoom = new Room();
            DewPlayer.allHumanPlayers.Clear();
            DewPlayer.allHumanPlayers.Add(soloRoom.MePlayer);
            soloRoom.Record(soloRoom.Me, 10f);
            Assert.Equal(1, soloRoom.Rank(11f));
        }
    }
}
