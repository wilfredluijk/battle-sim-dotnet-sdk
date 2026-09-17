using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk;

public sealed record PowerupConfig
{
    public int OverdriveDurationTicks { get; init; } = 50;
    public double OverdriveSpeedMult { get; init; } = 1.6;
    public double OverdriveAccelMult { get; init; } = 1.6;
    public double OverdriveTurnMult { get; init; } = 1.5;
    public int ReinforcedHullDurationTicks { get; init; } = 70;
    public double ReinforcedHullDamageMult { get; init; } = 0.45;
    public int RepairDronesDurationTicks { get; init; } = 50;
    public int RepairDronesHpPerTick { get; init; } = 1;
    public int RepairDronesInstantHp { get; init; } = 20;
    public int SmokeScreenDurationTicks { get; init; } = 80;
    public double SmokeScreenRadius { get; init; } = 70.0;
    public int RapidFireDurationTicks { get; init; } = 50;
    public double RapidFireCooldownMult { get; init; } = 0.5;
    public int HeavyShellDurationTicks { get; init; } = 30;
    public double HeavyShellSplashMult { get; init; } = 1.5;
    public double HeavyShellDamageMult { get; init; } = 1.3;
    public int LongRangeDurationTicks { get; init; } = 40;
    public double LongRangeRangeMult { get; init; } = 1.5;
    public double LongRangeSpeedMult { get; init; } = 1.6;
    public int AwacsDurationTicks { get; init; } = 60;
    public double AwacsRangeMult { get; init; } = 2.0;
    public double AwacsSilentJitter { get; init; } = 15.0;
    public double AwacsSilentConfidence { get; init; } = 0.6;
    public int SilentRunningDurationTicks { get; init; } = 80;
    public double SilentRunningActiveRangeMult { get; init; } = 0.5;
    public int CounterBatteryArmTicks { get; init; } = 60;
    public int CounterBatteryRevealTicks { get; init; } = 15;
    public int EmpBurstDurationTicks { get; init; } = 40;
    public double EmpBurstRadius { get; init; } = 130.0;
    public double EmpGunCooldownMult { get; init; } = 2.0;
    public int DecoyFlareDurationTicks { get; init; } = 60;
    public double DecoyFlareDistanceMin { get; init; } = 80.0;
    public double DecoyFlareDistanceMax { get; init; } = 140.0;
    private JsonObject raw = new();
    public JsonObject Raw => Wire.Copy(raw);
    public static PowerupConfig FromJson(JsonObject data)
    {
        var result = new PowerupConfig
        {
            OverdriveDurationTicks = Wire.Int(data, "overdrive_duration_ticks", 50),
            OverdriveSpeedMult = Wire.Number(data, "overdrive_speed_mult", 1.6),
            OverdriveAccelMult = Wire.Number(data, "overdrive_accel_mult", 1.6),
            OverdriveTurnMult = Wire.Number(data, "overdrive_turn_mult", 1.5),
            ReinforcedHullDurationTicks = Wire.Int(data, "reinforced_hull_duration_ticks", 70),
            ReinforcedHullDamageMult = Wire.Number(data, "reinforced_hull_damage_mult", 0.45),
            RepairDronesDurationTicks = Wire.Int(data, "repair_drones_duration_ticks", 50),
            RepairDronesHpPerTick = Wire.Int(data, "repair_drones_hp_per_tick", 1),
            RepairDronesInstantHp = Wire.Int(data, "repair_drones_instant_hp", 20),
            SmokeScreenDurationTicks = Wire.Int(data, "smoke_screen_duration_ticks", 80),
            SmokeScreenRadius = Wire.Number(data, "smoke_screen_radius", 70.0),
            RapidFireDurationTicks = Wire.Int(data, "rapid_fire_duration_ticks", 50),
            RapidFireCooldownMult = Wire.Number(data, "rapid_fire_cooldown_mult", 0.5),
            HeavyShellDurationTicks = Wire.Int(data, "heavy_shell_duration_ticks", 30),
            HeavyShellSplashMult = Wire.Number(data, "heavy_shell_splash_mult", 1.5),
            HeavyShellDamageMult = Wire.Number(data, "heavy_shell_damage_mult", 1.3),
            LongRangeDurationTicks = Wire.Int(data, "long_range_duration_ticks", 40),
            LongRangeRangeMult = Wire.Number(data, "long_range_range_mult", 1.5),
            LongRangeSpeedMult = Wire.Number(data, "long_range_speed_mult", 1.6),
            AwacsDurationTicks = Wire.Int(data, "awacs_duration_ticks", 60),
            AwacsRangeMult = Wire.Number(data, "awacs_range_mult", 2.0),
            AwacsSilentJitter = Wire.Number(data, "awacs_silent_jitter", 15.0),
            AwacsSilentConfidence = Wire.Number(data, "awacs_silent_confidence", 0.6),
            SilentRunningDurationTicks = Wire.Int(data, "silent_running_duration_ticks", 80),
            SilentRunningActiveRangeMult = Wire.Number(data, "silent_running_active_range_mult", 0.5),
            CounterBatteryArmTicks = Wire.Int(data, "counter_battery_arm_ticks", 60),
            CounterBatteryRevealTicks = Wire.Int(data, "counter_battery_reveal_ticks", 15),
            EmpBurstDurationTicks = Wire.Int(data, "emp_burst_duration_ticks", 40),
            EmpBurstRadius = Wire.Number(data, "emp_burst_radius", 130.0),
            EmpGunCooldownMult = Wire.Number(data, "emp_gun_cooldown_mult", 2.0),
            DecoyFlareDurationTicks = Wire.Int(data, "decoy_flare_duration_ticks", 60),
            DecoyFlareDistanceMin = Wire.Number(data, "decoy_flare_distance_min", 80.0),
            DecoyFlareDistanceMax = Wire.Number(data, "decoy_flare_distance_max", 140.0),
            raw = Wire.Copy(data),
        };
        result.Validate();
        return result;
    }
    public void Validate()
    {
        Wire.Positive(OverdriveDurationTicks);
        Wire.Positive(OverdriveSpeedMult);
        Wire.Positive(OverdriveAccelMult);
        Wire.Positive(OverdriveTurnMult);
        Wire.Positive(ReinforcedHullDurationTicks);
        Wire.Nonnegative(ReinforcedHullDamageMult);
        Wire.Positive(RepairDronesDurationTicks);
        Wire.Nonnegative(RepairDronesHpPerTick);
        Wire.Nonnegative(RepairDronesInstantHp);
        Wire.Positive(SmokeScreenDurationTicks);
        Wire.Positive(SmokeScreenRadius);
        Wire.Positive(RapidFireDurationTicks);
        Wire.Positive(RapidFireCooldownMult);
        Wire.Positive(HeavyShellDurationTicks);
        Wire.Positive(HeavyShellSplashMult);
        Wire.Positive(HeavyShellDamageMult);
        Wire.Positive(LongRangeDurationTicks);
        Wire.Positive(LongRangeRangeMult);
        Wire.Positive(LongRangeSpeedMult);
        Wire.Positive(AwacsDurationTicks);
        Wire.Positive(AwacsRangeMult);
        Wire.Nonnegative(AwacsSilentJitter);
        Wire.Nonnegative(AwacsSilentConfidence);
        Wire.Positive(SilentRunningDurationTicks);
        Wire.Nonnegative(SilentRunningActiveRangeMult);
        Wire.Positive(CounterBatteryArmTicks);
        Wire.Positive(CounterBatteryRevealTicks);
        Wire.Positive(EmpBurstDurationTicks);
        Wire.Positive(EmpBurstRadius);
        Wire.Positive(EmpGunCooldownMult);
        Wire.Positive(DecoyFlareDurationTicks);
        Wire.Positive(DecoyFlareDistanceMin);
        Wire.Positive(DecoyFlareDistanceMax);
        if (AwacsSilentConfidence > 1 || DecoyFlareDistanceMin > DecoyFlareDistanceMax)
            throw new ArgumentException("Invalid confidence or decoy distance bounds.");
    }
}

public sealed record SensorConfig
{
    public double ActiveRadarRange { get; init; } = 350.0;
    public double ActiveRadarNoise { get; init; } = 2.0;
    public double PassiveHearActiveRange { get; init; } = 500.0;
    public double PassiveHearNearbyRange { get; init; } = 150.0;
    public double PassiveBearingNoiseDeg { get; init; } = 5.0;
    public static SensorConfig FromJson(JsonObject data)
    {
        var result = new SensorConfig
        {
            ActiveRadarRange = Wire.Number(data, "active_radar_range", 350.0),
            ActiveRadarNoise = Wire.Number(data, "active_radar_noise", 2.0),
            PassiveHearActiveRange = Wire.Number(data, "passive_hear_active_range", 500.0),
            PassiveHearNearbyRange = Wire.Number(data, "passive_hear_nearby_range", 150.0),
            PassiveBearingNoiseDeg = Wire.Number(data, "passive_bearing_noise_deg", 5.0),
        };
        result.Validate();
        return result;
    }
    public void Validate()
    {
        Wire.Positive(ActiveRadarRange);
        Wire.Nonnegative(ActiveRadarNoise);
        Wire.Positive(PassiveHearActiveRange);
        Wire.Positive(PassiveHearNearbyRange);
        Wire.Nonnegative(PassiveBearingNoiseDeg);
    }
}
