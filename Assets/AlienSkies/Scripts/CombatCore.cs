namespace AlienSkies.Combat
{
    /// <summary>Pure health/death/respawn/frag state machine — fully
    /// unit-testable; PlayerCombat drives it on the server only.</summary>
    public sealed class CombatCore
    {
        readonly CombatSettings _s;

        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public int Frags { get; private set; }
        public int Deaths { get; private set; }
        public float RespawnRemaining { get; private set; }

        public CombatCore(CombatSettings settings)
        {
            _s = settings;
            Health = _s.MaxHealth;
        }

        /// <summary>Returns true only for the killing hit.</summary>
        public bool ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f) return false;
            Health -= amount;
            if (Health > 0f) return false;
            Health = 0f;
            IsDead = true;
            Deaths++;
            RespawnRemaining = _s.RespawnDelay;
            return true;
        }

        public void AddFrag() => Frags++;
        public void AddSuicide() => Frags--;

        /// <summary>Returns true when the respawn timer has elapsed.</summary>
        public bool TickRespawn(float dt)
        {
            if (!IsDead) return false;
            RespawnRemaining -= dt;
            return RespawnRemaining <= 0f;
        }

        public void Respawn()
        {
            Health = _s.MaxHealth;
            IsDead = false;
            RespawnRemaining = 0f;
        }
    }
}
