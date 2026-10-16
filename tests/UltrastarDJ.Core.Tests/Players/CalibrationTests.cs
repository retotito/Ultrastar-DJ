using System.Text.Json;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Core.Tests.Players;

public class CalibrationTests
{
    private const string Jbl = "wasapi/{jbl}|0";
    private const string Laptop = "wasapi/{laptop}|0";
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void EachOutputKeepsItsOwnCalibration()
    {
        // Calibrated on the Bluetooth speaker, then on the laptop speakers: switching back finds the first one again.
        PlayerConfig p = PlayerConfig.Default(1).WithCalibration(Jbl, 368).WithCalibration(Laptop, 130);

        Assert.Equal(368, p.CalibrationFor(Jbl));
        Assert.Equal(130, p.CalibrationFor(Laptop));
        Assert.Null(p.CalibrationFor("wasapi/{tv}|0"));
    }

    [Fact]
    public void Recalibrating_ReplacesThatOutputsValueOnly()
    {
        PlayerConfig p = PlayerConfig.Default(1).WithCalibration(Jbl, 368).WithCalibration(Laptop, 130).WithCalibration(Jbl, 300);

        Assert.Equal(300, p.CalibrationFor(Jbl));
        Assert.Equal(130, p.CalibrationFor(Laptop));
    }

    [Fact]
    public void OldSettings_OneCalibration_MoveToItsOutput()
    {
        // Settings written before calibrations were kept per output (an installed app being updated).
        PlayerConfig old = PlayerConfig.Default(1) with { CalibratedTotalMs = 368, CalibrationOutputKey = Jbl };

        PlayerConfig migrated = old.Migrated();

        Assert.Equal(368, migrated.CalibrationFor(Jbl));
        Assert.Null(migrated.CalibratedTotalMs);
        Assert.Null(migrated.CalibrationOutputKey);
    }

    [Fact]
    public void OldSettings_NeverCalibrated_StayEmpty()
    {
        PlayerConfig migrated = PlayerConfig.Default(1).Migrated();

        Assert.Empty(migrated.Calibrations);
    }

    [Fact]
    public void OldSettingsFile_ReadsAndMigrates()
    {
        // The exact shape of a players.json entry from 0.2.0-beta.2.
        const string json = """{ "id": 1, "color": "blue", "calibratedTotalMs": 368, "calibrationOutputKey": "wasapi/{jbl}|0" }""";
        PlayerConfig read = JsonSerializer.Deserialize<PlayerConfig>(json, CamelCase)!;

        Assert.Equal(368, read.Migrated().CalibrationFor(Jbl));
    }
}
