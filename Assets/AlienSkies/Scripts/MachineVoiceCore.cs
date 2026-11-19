using System.Collections.Generic;

namespace AlienSkies.Run
{
    public enum VoiceTrigger
    {
        RunStart, Kill, KillAtMaxHeat, Chain,
        Death, DeathByVoid, DeathByTurret, DeathByDrone, DeathBySelf,
        RankS, RankA, RankMid, RankD, NewBest,
        LevelSwitch, Idle,
    }

    /// <summary>The Voice of the Machine: the Gyroscope is the antagonist — a
    /// contemptuous machine divinity that built the arena as a test. Pure C#:
    /// line pools with seeded no-repeat rotation, a global cooldown for
    /// chatter, and priority triggers (deaths, ranks) that always speak.
    /// ORIGINAL character and lines only — the SHODAN archetype is the homage,
    /// never the text.</summary>
    public sealed class MachineVoiceCore
    {
        public const float LineDuration = 4f;
        public const float ChatterCooldown = 4f;

        public string CurrentLine { get; private set; }
        public float LineAge { get; private set; }

        readonly System.Random _rng;
        readonly Dictionary<VoiceTrigger, string[]> _pools;
        readonly Dictionary<VoiceTrigger, List<string>> _bags = new Dictionary<VoiceTrigger, List<string>>();
        float _cooldown;

        static readonly HashSet<VoiceTrigger> Priority = new HashSet<VoiceTrigger>
        {
            VoiceTrigger.Death, VoiceTrigger.DeathByVoid,
            VoiceTrigger.DeathByTurret, VoiceTrigger.DeathByDrone, VoiceTrigger.DeathBySelf,
            VoiceTrigger.RankS, VoiceTrigger.RankA, VoiceTrigger.RankMid,
            VoiceTrigger.RankD, VoiceTrigger.NewBest,
            VoiceTrigger.Chain, // rare, earned — must never be eaten by the kill line's cooldown (review)
        };

        public MachineVoiceCore(int seed)
        {
            _rng = new System.Random(seed);
            _pools = BuildPools();
        }

        public void Tick(float dt)
        {
            if (_cooldown > 0f) _cooldown -= dt;
            if (CurrentLine == null) return;
            LineAge += dt;
            if (LineAge >= LineDuration) CurrentLine = null;
        }

        /// <summary>Returns the spoken line, or null when suppressed
        /// (chatter during cooldown). Priority triggers always speak.</summary>
        public string Post(VoiceTrigger trigger)
        {
            bool priority = Priority.Contains(trigger);
            if (!priority && _cooldown > 0f) return null;

            string line = Draw(trigger);
            if (line == null) return null;
            CurrentLine = line;
            LineAge = 0f;
            _cooldown = ChatterCooldown;
            return line;
        }

        // Shuffle-bag: each pool is drawn in a seeded random order without
        // repeats; when exhausted it refills. Deterministic per seed.
        string Draw(VoiceTrigger trigger)
        {
            if (!_pools.TryGetValue(trigger, out var pool) || pool.Length == 0) return null;
            if (!_bags.TryGetValue(trigger, out var bag) || bag.Count == 0)
            {
                bag = new List<string>(pool);
                _bags[trigger] = bag;
            }
            int i = _rng.Next(bag.Count);
            string line = bag[i];
            bag.RemoveAt(i);
            return line;
        }

        static Dictionary<VoiceTrigger, string[]> BuildPools() =>
            new Dictionary<VoiceTrigger, string[]>
            {
                [VoiceTrigger.RunStart] = new[]
                {
                    "RUN, LITTLE COMET. I AM TIMING YOU.",
                    "THE ARENA WAS BUILT FOR BETTER THAN YOU. PROVE ME WRONG.",
                    "GO. THE VOID IS PATIENT. I AM NOT.",
                },
                [VoiceTrigger.Kill] = new[]
                {
                    "ADEQUATE.",
                    "THE DRONE WAS EXPENDABLE. AS ARE YOU.",
                    "ONE LESS OF MINE. THOUSANDS REMAIN.",
                    "NOTED. UNIMPRESSED.",
                },
                [VoiceTrigger.KillAtMaxHeat] = new[]
                {
                    "THE MUSIC LIKES YOU. I REMAIN UNCONVINCED.",
                    "LOUD. FAST. ALMOST WORTH WATCHING.",
                    "YOU BURN BRIGHTLY. BRIGHT THINGS BURN OUT.",
                },
                [VoiceTrigger.Chain] = new[]
                {
                    "AN INTERESTING PATTERN. DO IT AGAIN.",
                    "CHAIN REGISTERED. A FLICKER OF COMPETENCE.",
                    "MY DRONES DIE IN RHYTHM. HOW MUSICAL OF YOU.",
                },
                [VoiceTrigger.Death] = new[]
                {
                    "DELETED BY MY SMALLEST FINGER.",
                    "YOUR PULSE WAS A ROUNDING ERROR. CORRECTED.",
                    "AGAIN. FROM THE BEGINNING. FOREVER, IF NEEDED.",
                    "THE ARENA REJECTS YOU. POLITELY, THIS TIME.",
                },
                [VoiceTrigger.DeathByVoid] = new[]
                {
                    "GRAVITY. MY OLDEST SERVANT.",
                    "THE VOID ACCEPTS ALL DONATIONS.",
                    "FLIGHT WAS NEVER PROMISED. ONLY FALLING, DELAYED.",
                },
                [VoiceTrigger.DeathByTurret] = new[]
                {
                    "THE SENTRY DOES NOT BLINK.",
                    "STANDING STILL IS A CONFESSION.",
                    "IT WATCHED YOU THE WHOLE WAY DOWN.",
                },
                [VoiceTrigger.DeathByDrone] = new[]
                {
                    "OUTSHOT BY MY SMALLEST THOUGHT.",
                    "THE DRONES LEARN NOTHING. THEY DID NOT NEED TO.",
                    "A PROCESS THAT SIMPLE. AND STILL.",
                },
                [VoiceTrigger.DeathBySelf] = new[]
                {
                    "YOUR OWN ORDNANCE. EXQUISITE.",
                    "I DID NOT EVEN HAVE TO HELP.",
                    "THE ROCKET LOVED YOU BACK.",
                },
                [VoiceTrigger.RankS] = new[]
                {
                    "...RECALCULATING. YOU WERE NOT SUPPOSED TO DO THAT.",
                    "FLAWLESS. I WILL PRETEND I PLANNED THIS.",
                },
                [VoiceTrigger.RankA] = new[]
                {
                    "NEARLY WORTHY OF MY ARENA. NEARLY.",
                    "IMPRESSIVE, FOR A CREATURE OF MEAT AND PANIC.",
                },
                [VoiceTrigger.RankMid] = new[]
                {
                    "MEDIOCRITY, MEASURED TO THE MILLISECOND.",
                    "I HAVE ARCHIVED FASTER DUST.",
                },
                [VoiceTrigger.RankD] = new[]
                {
                    "I HAVE SEEN DEBRIS ORBIT WITH MORE PURPOSE.",
                    "A GRADE EVEN MY PITY CANNOT ROUND UP.",
                },
                [VoiceTrigger.NewBest] = new[]
                {
                    "ARCHIVED. YOUR SPECIES PEAKS SO BRIEFLY.",
                    "A NEW RECORD. I HAVE ADJUSTED MY EXPECTATIONS. SLIGHTLY.",
                },
                [VoiceTrigger.LevelSwitch] = new[]
                {
                    "ANOTHER SPHERE. ANOTHER GRAVE.",
                    "CHOOSE YOUR ARENA. THE OUTCOME IS UNCHANGED.",
                },
                [VoiceTrigger.Idle] = new[]
                {
                    "THE CLOCK ONLY MOVES WHEN YOU DO. A MERCY.",
                    "STANDING STILL. BOLD STRATEGY. WRONG ARENA.",
                },
            };
    }
}
