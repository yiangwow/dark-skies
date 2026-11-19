using NUnit.Framework;
using AlienSkies.Run;

public class MachineVoiceCoreTests
{
    [Test]
    public void SilentAtStart()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNull(v.CurrentLine);
    }

    [Test]
    public void Post_SetsCurrentLine_AndAgeCountsUp()
    {
        var v = new MachineVoiceCore(1);
        var line = v.Post(VoiceTrigger.Kill);
        Assert.IsNotNull(line);
        Assert.AreEqual(line, v.CurrentLine);
        v.Tick(1.5f);
        Assert.AreEqual(1.5f, v.LineAge, 1e-4f);
    }

    [Test]
    public void LineExpiresAfterDuration()
    {
        var v = new MachineVoiceCore(1);
        v.Post(VoiceTrigger.Kill);
        v.Tick(MachineVoiceCore.LineDuration + 0.01f);
        Assert.IsNull(v.CurrentLine);
    }

    [Test]
    public void ChatterSuppressedDuringCooldown()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
        Assert.IsNull(v.Post(VoiceTrigger.Kill)); // within cooldown
        v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
    }

    [Test]
    public void PriorityAlwaysSpeaks()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
        Assert.IsNotNull(v.Post(VoiceTrigger.Death)); // ignores cooldown
        Assert.IsNotNull(v.Post(VoiceTrigger.RankS));
    }

    [Test]
    public void PoolCyclesWithoutRepeat_UntilExhausted()
    {
        var v = new MachineVoiceCore(7);
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 4; i++) // Kill pool has 4 lines
        {
            var line = v.Post(VoiceTrigger.Kill);
            Assert.IsTrue(seen.Add(line), $"repeat before exhaustion: {line}");
            v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        }
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill)); // bag refilled
    }

    [Test]
    public void DeterministicPerSeed()
    {
        var a = new MachineVoiceCore(42);
        var b = new MachineVoiceCore(42);
        Assert.AreEqual(a.Post(VoiceTrigger.Death), b.Post(VoiceTrigger.Death));
        Assert.AreEqual(a.Post(VoiceTrigger.Kill), b.Post(VoiceTrigger.Kill));
    }

    [Test]
    public void NewPostResetsAge()
    {
        var v = new MachineVoiceCore(1);
        v.Post(VoiceTrigger.Kill);
        v.Tick(2f);
        v.Post(VoiceTrigger.Death);
        Assert.AreEqual(0f, v.LineAge, 1e-4f);
    }

    [Test]
    public void EveryTriggerHasLines()
    {
        var v = new MachineVoiceCore(3);
        foreach (VoiceTrigger t in System.Enum.GetValues(typeof(VoiceTrigger)))
        {
            Assert.IsNotNull(v.Post(t), $"no line for {t}");
            v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        }
    }

    [Test]
    public void PostReturnsSameStringItStores()
    {
        var v = new MachineVoiceCore(1);
        Assert.AreEqual(v.Post(VoiceTrigger.RankD), v.CurrentLine);
    }

    [Test]
    public void DeathAttributionPools_NonEmpty_AndDisjointFromGenericDeath()
    {
        var v = new MachineVoiceCore(11);

        System.Collections.Generic.HashSet<string> Drain(VoiceTrigger trigger, int count)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                seen.Add(v.Post(trigger)); // priority trigger: ignores cooldown
                v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
            }
            return seen;
        }

        var deathLines = Drain(VoiceTrigger.Death, 4);          // generic Death pool has 4 lines
        var turretLines = Drain(VoiceTrigger.DeathByTurret, 3); // attribution pools have 3 lines each
        var droneLines = Drain(VoiceTrigger.DeathByDrone, 3);
        var selfLines = Drain(VoiceTrigger.DeathBySelf, 3);

        Assert.IsTrue(turretLines.Count > 0);
        Assert.IsTrue(droneLines.Count > 0);
        Assert.IsTrue(selfLines.Count > 0);
        Assert.IsFalse(deathLines.Overlaps(turretLines));
        Assert.IsFalse(deathLines.Overlaps(droneLines));
        Assert.IsFalse(deathLines.Overlaps(selfLines));
    }
}
