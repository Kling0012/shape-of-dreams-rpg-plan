namespace SodRpg.Core.Game
{
    public static class TravelerCombat
    {
        /// <summary>The four designated travelers qualify only while their current basic attack is melee.</summary>
        public static bool IsMelee(string heroId, bool meleeBasicAttack) => meleeBasicAttack &&
            (heroId == "Hero_Vesper" || heroId == "Hero_Husk" || heroId == "Hero_Mist" || heroId == "Hero_Aurena");
    }
}
