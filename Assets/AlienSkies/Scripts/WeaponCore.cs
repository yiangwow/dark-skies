using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Weapon selection + fire-rate gate. One shared cooldown timer:
    /// firing sets it to the weapon's interval, switching raises it to at least
    /// the swap delay (never lowers it, so switching can't bypass a cooldown).</summary>
    public sealed class WeaponCore
    {
        readonly CombatSettings _s;

        public int CurrentIndex { get; private set; }
        public WeaponStats Current => _s.Weapons[CurrentIndex];
        public float Cooldown { get; private set; }

        public WeaponCore(CombatSettings settings) => _s = settings;

        public void Tick(float dt)
        {
            if (Cooldown > 0f) Cooldown -= dt;
        }

        public bool TryFire()
        {
            if (Cooldown > 0f) return false;
            Cooldown = Current.FireInterval;
            return true;
        }

        public bool TrySwitch(int index)
        {
            if (index < 0 || index >= _s.Weapons.Length || index == CurrentIndex) return false;
            CurrentIndex = index;
            Cooldown = Mathf.Max(Cooldown, _s.SwitchDelay);
            return true;
        }

        public void SwitchNext(int direction) =>
            TrySwitch((CurrentIndex + direction + _s.Weapons.Length) % _s.Weapons.Length);
    }
}
