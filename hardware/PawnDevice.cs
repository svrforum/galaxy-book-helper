using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GalaxyHardware
{
    // Independent device-IOCTL client. Wire layout follows the public PawnIO interface.
    // Only the two pinned official modules and explicitly allowlisted functions are exposed.
    sealed class PawnDevice : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr attributes, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint written, IntPtr overlapped);
        readonly SafeFileHandle handle;
        readonly string module;
        const uint DeviceType = 41394;
        public PawnDevice(string name)
        {
            string expected;
            if (name == "IntelMSR") expected = "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F";
            else if (name == "IntelMCHBAR") expected = "3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE";
            else throw new ArgumentException("Module not allowed");
            module = name;
            byte[] blob = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modules", name + ".bin"));
            using (SHA256 sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(blob)).Replace("-", "") != expected) throw new InvalidDataException("Official module SHA256 mismatch");
            handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "PawnIO 열기 실패. 공식 서명 버전 설치 및 관리자 실행이 필요합니다. Win32=" + error); }
            try { Call((DeviceType << 16) | (0x821u << 2), blob, 0); } catch { handle.Dispose(); throw; }
        }
        byte[] Call(uint ioctl, byte[] input, int expectedBytes)
        {
            byte[] output = new byte[expectedBytes]; uint written;
            if (!DeviceIoControl(handle, ioctl, input, (uint)input.Length, output, (uint)output.Length, out written, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (written != expectedBytes) throw new InvalidDataException("Driver response length mismatch");
            return output;
        }
        ulong Execute(string function, ulong[] input, bool result)
        {
            byte[] name = Encoding.ASCII.GetBytes(function); if (name.Length >= 32) throw new ArgumentException("function");
            byte[] buffer = new byte[32 + input.Length * 8]; Array.Copy(name, buffer, name.Length);
            for (int i = 0; i < input.Length; i++) Array.Copy(BitConverter.GetBytes(input[i]), 0, buffer, 32 + i * 8, 8);
            byte[] output = Call((DeviceType << 16) | (0x841u << 2), buffer, result ? 8 : 0);
            return result ? BitConverter.ToUInt64(output, 0) : 0;
        }
        public ulong ReadMsr(uint address)
        {
            if (module != "IntelMSR" || (address != 0x606 && address != 0x610 && address != 0x611 && address != 0x1A2 && address != 0x1B1)) throw new ArgumentException("MSR not allowed");
            return Execute("ioctl_read_msr", new ulong[] { address }, true);
        }
        public void WritePackageLimit(ulong value)
        {
            if (module != "IntelMSR") throw new InvalidOperationException();
            Execute("ioctl_write_msr", new ulong[] { 0x610, value }, false);
        }
        public ulong ReadMchbar(uint offset)
        {
            if (module != "IntelMCHBAR" || (offset != 0x5938 && offset != 0x59A0)) throw new ArgumentException("MCHBAR offset not allowed");
            return Execute("ioctl_read_qword", new ulong[] { offset }, true);
        }
        public ulong MchbarBase()
        {
            if (module != "IntelMCHBAR") throw new InvalidOperationException();
            return Execute("ioctl_get_mchbar_addr", new ulong[0], true);
        }
        public void Dispose() { handle.Dispose(); }
    }
}
