using NUnit.Framework;
using AlienSkies.Movement;

public class SpeedFeelCoreTests
{
    const float Dt = 0.016f;

    [Test]
    public void Fov_RestsAtBase_AndCapsAtMax()
    {
        var c = new SpeedFeelCore();
        Assert.AreEqual(60f, c.FovFor(0f), 1e-3f);
        Assert.AreEqual(60f, c.FovFor(12f), 1e-3f);
        Assert.AreEqual(75f, c.FovFor(62f), 1e-3f);
        Assert.AreEqual(75f, c.FovFor(500f), 1e-3f);
    }

    [Test]
    public void ZipBurst_AddsThenDecays()
    {
        var c = new SpeedFeelCore();
        c.RegisterZip();
        Assert.AreEqual(66f, c.FovFor(0f), 1e-3f);
        c.Tick(1f, 0f);
        Assert.AreEqual(62f, c.FovFor(0f), 1e-3f);
        c.Tick(1f, 0f);
        Assert.AreEqual(60f, c.FovFor(0f), 1e-3f);
    }

    [Test]
    public void Roll_SignedAndClamped()
    {
        var c = new SpeedFeelCore();
        Assert.AreEqual(-2.5f, c.RollFor(30f), 1e-3f);
        Assert.AreEqual(2.5f, c.RollFor(-30f), 1e-3f);
        Assert.AreEqual(0f, c.RollFor(0f), 1e-3f);
    }

    [Test]
    public void LineIntensity_ZeroSlowFullFast()
    {
        var c = new SpeedFeelCore();
        Assert.AreEqual(0f, c.LineIntensityFor(20f), 1e-3f);
        Assert.AreEqual(0.6f, c.LineIntensityFor(70f), 1e-3f);
        Assert.AreEqual(0.3f, c.LineIntensityFor(47.5f), 1e-3f);
    }

    [Test]
    public void SmoothedAccel_ConstantSpeed_ZeroAccel_InnerRadiusAtBase()
    {
        var c = new SpeedFeelCore();
        for (int i = 0; i < 60; i++)
            c.Tick(Dt, 40f); // holding speed -- no acceleration at all
        Assert.AreEqual(0f, c.SmoothedAccel, 1e-3f);
        Assert.AreEqual(0.25f, c.InnerRadiusFor(), 1e-3f);
    }

    [Test]
    public void SmoothedAccel_RampUp_ShrinksInnerRadius_ThenDecaysWhenSpeedHolds()
    {
        var c = new SpeedFeelCore();
        float speed = 0f;
        for (int i = 0; i < 60; i++) // +30 m/s over ~1s of small ticks
        {
            speed += 0.5f;
            c.Tick(Dt, speed);
        }
        Assert.Greater(c.SmoothedAccel, 10f, "gaining speed must swell the smoothed accel estimate");
        Assert.Less(c.InnerRadiusFor(), 0.20f, "positive accel must pull the annulus inward");

        for (int i = 0; i < 125; i++) // hold the new speed for ~2s
            c.Tick(Dt, speed);
        Assert.Less(c.SmoothedAccel, 0.1f, "holding speed must decay the smoothed accel back toward zero");
        Assert.AreEqual(0.25f, c.InnerRadiusFor(), 0.01f,
            "holding speed must relax the annulus back to its resting radius");
    }
}
