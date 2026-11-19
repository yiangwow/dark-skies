namespace AlienSkies.Combat
{
    /// <summary>Anything that can be shot and killed for credit — the player
    /// capsule and turrets both implement this so hitscan/splash code doesn't
    /// need to know which kind of thing it hit. Attacker stays PlayerCombat
    /// (only players attack; turrets are victims only).</summary>
    public interface IDamageable
    {
        void TakeDamage(float amount, PlayerCombat attacker);
        bool IsDead { get; }
    }
}
