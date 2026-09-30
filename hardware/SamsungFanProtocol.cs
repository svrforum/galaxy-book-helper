using System;
using System.IO;

namespace GalaxyHardware
{
    // Protocol only. No device access and no unverified direct-RPM setter.
    // Based on successful Book6 PAMB official diagnostic responses on 2026-09-25.
    static class SamsungFanProtocol
    {
        internal sealed class Reading
        {
            public int FanCount;
            public int Fan1Rpm;
            public int? Fan2Rpm;
        }

        static byte[] Query(params byte[] payload)
        {
            var packet = new byte[21];
            packet[0] = 0x43; packet[1] = 0x58; packet[2] = 0x7A;
            Array.Copy(payload, 0, packet, 5, payload.Length);
            return packet;
        }
        internal static byte[] ReadSupport() { return Query(0xBB, 0xAA); }
        internal static byte[] ReadRpm() { return Query(0x82, 0xB5, 0x80); }
        internal static byte[] ReadMaxStep() { return Query(0x82, 0xB5, 0x81); }

        static void Validate(byte[] response)
        {
            if (response == null || response.Length != 21)
                throw new InvalidDataException("Samsung fan response must be exactly 21 bytes.");
            if (response[0] != 0x43 || response[1] != 0x58 || response[2] != 0x7A || response[3] != 0)
                throw new InvalidDataException("Unexpected Samsung response command.");
            if (response[4] != 0xAA)
                throw new InvalidDataException("Samsung command did not complete successfully.");
        }
        internal static bool ParseSupport(byte[] response)
        {
            Validate(response);
            return response[5] == 0xDD && response[6] == 0xCC;
        }
        internal static int ParseMaxStep(byte[] response)
        {
            Validate(response);
            // 3 observed through our driver on 2026-09-27; 6 during the OEM
            // diagnostic after changing performance mode. This is a read-only
            // capability observation, not permission to write either range.
            if (response[7] != 3 && response[7] != 6)
                throw new InvalidDataException("Fan step range differs from the verified Book6 firmware.");
            return response[7];
        }
        internal static Reading ParseRpm(byte[] response)
        {
            Validate(response);
            if (response[7] != 1 && response[7] != 2)
                throw new InvalidDataException("Invalid or unsupported fan count.");
            return new Reading {
                FanCount = response[7],
                Fan1Rpm = (response[8] << 8) | response[9],
                Fan2Rpm = response[7] == 2 ? (int?)((response[10] << 8) | response[11]) : null
            };
        }
    }
}
