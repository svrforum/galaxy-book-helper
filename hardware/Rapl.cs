using System;

namespace GalaxyHardware
{
    public static class Rapl
    {
        public const ulong PowerMask = 0x00007FFF00007FFFUL;
        public static double PowerUnit(ulong units) { return Math.Pow(0.5, (int)(units & 15)); }
        public static double EnergyUnit(ulong units) { return Math.Pow(0.5, (int)((units >> 8) & 31)); }
        public static double Pl1(ulong raw, ulong units) { return (raw & 0x7FFF) * PowerUnit(units); }
        public static double Pl2(ulong raw, ulong units) { return ((raw >> 32) & 0x7FFF) * PowerUnit(units); }
        public static bool Locked(ulong raw) { return (raw & (1UL << 63)) != 0; }
        public static ulong EncodeReduction(ulong raw, ulong units, double pl1, double pl2)
        {
            if (Locked(raw)) throw new InvalidOperationException("MSR package power limit is firmware-locked.");
            if ((raw & (1UL << 15)) == 0 || (raw & (1UL << 47)) == 0) throw new InvalidOperationException("Both existing power limits must already be enabled.");
            if (Double.IsNaN(pl1) || Double.IsNaN(pl2) || Double.IsInfinity(pl1) || Double.IsInfinity(pl2) ||
                pl1 < 5 || pl2 < pl1 || pl2 > 80) throw new ArgumentOutOfRangeException("Use 5 <= PL1 <= PL2 <= 80 watts.");
            if (pl1 > Pl1(raw, units) || pl2 > Pl2(raw, units)) throw new InvalidOperationException("This research build only lowers existing limits.");
            ulong a = (ulong)Math.Floor(pl1 / PowerUnit(units)); ulong b = (ulong)Math.Floor(pl2 / PowerUnit(units));
            if (a == 0 || a > 0x7FFF || b > 0x7FFF) throw new ArgumentOutOfRangeException("Encoded limit exceeds field.");
            return (raw & ~PowerMask) | a | (b << 32); // Preserve enable, clamp, tau, reserved and lock bits.
        }
        public static double Watts(uint previous, uint current, ulong units, double elapsedSeconds)
        {
            if (elapsedSeconds <= 0) throw new ArgumentOutOfRangeException("elapsedSeconds");
            return unchecked(current - previous) * EnergyUnit(units) / elapsedSeconds;
        }
    }
}
