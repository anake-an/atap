using Atap.Core.Power;

namespace Atap.Core.Tests.Power;

public class BatterySaverPolicyTests
{
    private const int Below = 20;

    [Theory]
    // mode, on battery, battery %, Windows energy saver, saver active
    [InlineData(SaverMode.OnBattery, true, 80, false, true)]
    [InlineData(SaverMode.OnBattery, true, 5, true, true)]
    [InlineData(SaverMode.OnBattery, false, 80, false, false)]
    [InlineData(SaverMode.OnBattery, false, 5, true, false)]
    [InlineData(SaverMode.OnBattery, false, null, false, false)]
    [InlineData(SaverMode.OnBattery, true, null, false, true)]
    [InlineData(SaverMode.Always, false, null, false, true)]
    [InlineData(SaverMode.Always, false, 100, false, true)]
    [InlineData(SaverMode.Always, true, 50, true, true)]
    [InlineData(SaverMode.Never, true, 3, true, false)]
    [InlineData(SaverMode.Never, false, null, false, false)]
    [InlineData(SaverMode.BelowPercent, true, 19, false, true)]
    [InlineData(SaverMode.BelowPercent, true, 0, false, true)]
    [InlineData(SaverMode.BelowPercent, true, 20, false, false)]
    [InlineData(SaverMode.BelowPercent, true, 21, false, false)]
    [InlineData(SaverMode.BelowPercent, true, 80, true, false)]
    [InlineData(SaverMode.BelowPercent, false, 10, false, false)]
    [InlineData(SaverMode.BelowPercent, false, 10, true, false)]
    [InlineData(SaverMode.BelowPercent, true, null, false, false)]
    [InlineData(SaverMode.BelowPercent, false, null, false, false)]
    [InlineData(SaverMode.FollowWindows, false, 90, true, true)]
    [InlineData(SaverMode.FollowWindows, true, 15, true, true)]
    [InlineData(SaverMode.FollowWindows, true, 15, false, false)]
    [InlineData(SaverMode.FollowWindows, false, null, false, false)]
    [InlineData((SaverMode)99, true, 50, false, true)]
    [InlineData((SaverMode)99, false, 50, false, false)]
    public void IsActive_TruthTable(SaverMode mode, bool onBattery, int? percent, bool energySaver, bool expected)
    {
        Assert.Equal(expected, BatterySaverPolicy.IsActive(mode, Below, onBattery, percent, energySaver));
    }

    [Theory]
    [InlineData(-5, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(100, 99, true)]
    [InlineData(100, 100, false)]
    [InlineData(250, 99, true)]
    [InlineData(250, 100, false)]
    public void IsActive_BelowPercentThresholdIsClamped(int threshold, int percent, bool expected)
    {
        Assert.Equal(expected, BatterySaverPolicy.IsActive(SaverMode.BelowPercent, threshold, onBattery: true, percent, energySaverOn: false));
    }

    [Fact]
    public void IsActive_ThresholdMattersOnlyForBelowPercent()
    {
        foreach (var threshold in new[] { -1, 0, 20, 100, 1000 })
        {
            Assert.True(BatterySaverPolicy.IsActive(SaverMode.OnBattery, threshold, onBattery: true, batteryPercent: 90, energySaverOn: false));
            Assert.True(BatterySaverPolicy.IsActive(SaverMode.Always, threshold, onBattery: false, batteryPercent: 90, energySaverOn: false));
            Assert.False(BatterySaverPolicy.IsActive(SaverMode.Never, threshold, onBattery: true, batteryPercent: 1, energySaverOn: true));
            Assert.True(BatterySaverPolicy.IsActive(SaverMode.FollowWindows, threshold, onBattery: false, batteryPercent: 90, energySaverOn: true));
        }
    }

    [Theory]
    [InlineData("onBattery", SaverMode.OnBattery)]
    [InlineData("always", SaverMode.Always)]
    [InlineData("never", SaverMode.Never)]
    [InlineData("belowPercent", SaverMode.BelowPercent)]
    [InlineData("followWindows", SaverMode.FollowWindows)]
    [InlineData("ALWAYS", SaverMode.Always)]
    [InlineData(" never ", SaverMode.Never)]
    [InlineData(null, SaverMode.OnBattery)]
    [InlineData("", SaverMode.OnBattery)]
    [InlineData("auto", SaverMode.OnBattery)]
    [InlineData("1", SaverMode.OnBattery)]
    public void ParseMode_ReadsStoredValues(string? value, SaverMode expected)
    {
        Assert.Equal(expected, BatterySaverPolicy.ParseMode(value));
    }

    [Fact]
    public void ModeChoices_RoundTripEveryMode()
    {
        Assert.Equal(Enum.GetValues<SaverMode>().Length, BatterySaverPolicy.ModeChoices.Count);
        foreach (var mode in Enum.GetValues<SaverMode>())
            Assert.Equal(mode, BatterySaverPolicy.ParseMode(BatterySaverPolicy.ToSettingValue(mode)));
        Assert.Equal("onBattery", BatterySaverPolicy.ToSettingValue((SaverMode)99));
    }

    [Fact]
    public void Defaults_AreOnBatteryAt20PercentWithAFactorOf4()
    {
        Assert.Equal(SaverMode.OnBattery, BatterySaverPolicy.DefaultMode);
        Assert.Equal("onBattery", BatterySaverPolicy.ToSettingValue(BatterySaverPolicy.DefaultMode));
        Assert.Equal(20, BatterySaverPolicy.DefaultPercent);
        Assert.Equal(4, BatterySaverPolicy.Multiplier);
        Assert.Equal("saver.mode", BatterySaverPolicy.ModeKey);
        Assert.Equal("saver.percent", BatterySaverPolicy.PercentKey);
    }
}
