using NUnit.Framework;
using AlienSkies.Run;

public class RunCoreTests
{
    RunCore NewCore(RunSettings s = null) => new RunCore(s ?? new RunSettings());

    [Test]
    public void StartsReady_TickDoesNothingUntilStarted()
    {
        var c = NewCore();
        c.Tick(1f);
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual(0f, c.Time);
    }

    [Test]
    public void StartRun_ThenTick_AdvancesTime()
    {
        var c = NewCore();
        c.StartRun();
        c.Tick(0.5f);
        c.Tick(0.25f);
        Assert.AreEqual(RunState.Running, c.State);
        Assert.AreEqual(0.75f, c.Time, 1e-4f);
    }

    [TestCase(0f, 1)]
    [TestCase(0.32f, 1)]
    [TestCase(0.34f, 2)]
    [TestCase(0.67f, 3)]
    [TestCase(1f, 4)]
    public void MultiplierFor_MapsHeatToSteps(float heat, int expected)
    {
        var c = NewCore(); // MaxMultiplier 4 -> thirds
        Assert.AreEqual(expected, c.MultiplierFor(heat));
    }

    [Test]
    public void RegisterKill_ScoresThroughMultiplier_AndTracksPeak()
    {
        var c = NewCore();
        c.StartRun();
        c.RegisterKill(0f);   // x1 -> 100
        c.RegisterKill(1f);   // x4 -> 400
        c.RegisterKill(0.4f); // x2 -> 200
        Assert.AreEqual(700, c.Score);
        Assert.AreEqual(3, c.Kills);
        Assert.AreEqual(2, c.Multiplier);
        Assert.AreEqual(4, c.PeakMultiplier);
    }

    [Test]
    public void LastKillPoints_TracksTheScoredAmount()
    {
        var c = NewCore();
        c.StartRun();
        c.RegisterKill(1f); // x4 -> 400
        Assert.AreEqual(400, c.LastKillPoints);
        c.RegisterKill(0f); // x1 -> 100
        Assert.AreEqual(100, c.LastKillPoints);
        c.Reset();
        Assert.AreEqual(0, c.LastKillPoints);
    }

    [Test]
    public void KillsIgnoredUnlessRunning()
    {
        var c = NewCore();
        c.RegisterKill(1f);
        Assert.AreEqual(0, c.Score);
        c.StartRun();
        c.CompleteRun();
        c.RegisterKill(1f);
        Assert.AreEqual(0, c.Score);
    }

    [Test]
    public void CompleteRun_UnderPar_AddsTimeBonus()
    {
        var s = new RunSettings { ParTime = 60f, TimeBonusPerSecond = 10f, STargetScore = 2000 };
        var c = NewCore(s);
        c.StartRun();
        c.Tick(30f);            // 30s under par -> +300
        c.RegisterKill(1f);     // +400
        c.CompleteRun();
        Assert.AreEqual(RunState.Complete, c.State);
        Assert.AreEqual(700, c.FinalScore);
    }

    [Test]
    public void CompleteRun_OverPar_NoBonusNoPenalty()
    {
        var s = new RunSettings { ParTime = 10f };
        var c = NewCore(s);
        c.StartRun();
        c.Tick(50f);
        c.RegisterKill(0f); // 100
        c.CompleteRun();
        Assert.AreEqual(100, c.FinalScore);
    }

    [TestCase(2000, "S")]
    [TestCase(1500, "A")]
    [TestCase(1000, "B")]
    [TestCase(500, "C")]
    [TestCase(100, "D")]
    public void Rank_FromFinalScoreFraction(int killPoints, string expected)
    {
        var s = new RunSettings { ParTime = 0f, KillScore = killPoints, STargetScore = 2000 };
        var c = NewCore(s);
        c.StartRun();
        c.RegisterKill(0f); // x1 -> exactly killPoints
        c.CompleteRun();
        Assert.AreEqual(expected, c.Rank);
    }

    [Test]
    public void PointsToNextRank_ReturnsDeltaToNextRank_ZeroAtS()
    {
        var s = new RunSettings { ParTime = 0f, KillScore = 1000, STargetScore = 2000 };
        var c = NewCore(s);
        c.StartRun();
        c.RegisterKill(0f); // x1 -> 1000, frac 0.5 -> B; A threshold is 1500
        c.CompleteRun();
        Assert.AreEqual("B", c.Rank);
        Assert.AreEqual(500, c.PointsToNextRank());

        var sS = new RunSettings { ParTime = 0f, KillScore = 2000, STargetScore = 2000 };
        var cS = NewCore(sS);
        cS.StartRun();
        cS.RegisterKill(0f); // x1 -> 2000, frac 1.0 -> S
        cS.CompleteRun();
        Assert.AreEqual("S", cS.Rank);
        Assert.AreEqual(0, cS.PointsToNextRank());
    }

    [Test]
    public void Reset_ReturnsToReady_ClearsEverything()
    {
        var c = NewCore();
        c.StartRun();
        c.Tick(5f);
        c.RegisterKill(1f);
        c.CompleteRun();
        c.Reset();
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual(0f, c.Time);
        Assert.AreEqual(0, c.Score);
        Assert.AreEqual(1, c.PeakMultiplier);
        Assert.AreEqual("", c.Rank);
    }

    [Test]
    public void CompleteIgnoredUnlessRunning()
    {
        var c = NewCore();
        c.CompleteRun();
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual("", c.Rank);
    }

    [Test]
    public void RankOrder_OrdersEmptyThroughD_ThroughS()
    {
        Assert.Less(RunCore.RankOrder(""), RunCore.RankOrder("D"));
        Assert.Less(RunCore.RankOrder("D"), RunCore.RankOrder("C"));
        Assert.Less(RunCore.RankOrder("C"), RunCore.RankOrder("B"));
        Assert.Less(RunCore.RankOrder("B"), RunCore.RankOrder("A"));
        Assert.Less(RunCore.RankOrder("A"), RunCore.RankOrder("S"));
    }

    [Test]
    public void WithOverrides_SwapsParAndTarget_KeepsTheRest()
    {
        var s = new RunSettings { ParTime = 90f, STargetScore = 2000, KillScore = 123 };
        var o = s.WithOverrides(75f, 1500);
        Assert.AreEqual(75f, o.ParTime);
        Assert.AreEqual(1500, o.STargetScore);
        Assert.AreEqual(123, o.KillScore);
        Assert.AreEqual(90f, s.ParTime); // original untouched
    }
}
