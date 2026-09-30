// Read-only _FST probe. No arbitrary method or control-code input.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class FanAcpiProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle,uint ioctl,byte[] input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
 public static string ReadStatus(string pdo) {
  if(!System.Text.RegularExpressions.Regex.IsMatch(pdo,@"^\\Device\\[0-9a-fA-F]{8}$")) throw new ArgumentException("Invalid PDO");
  using(var handle=CreateFile(@"\\?\GLOBALROOT"+pdo,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(handle.IsInvalid) return "OpenError="+Marshal.GetLastWin32Error();
   byte[] input={0x41,0x65,0x69,0x42,0x5f,0x46,0x53,0x54}; // 'BieA', _FST
   byte[] output=new byte[4096]; uint returned;
   if(!DeviceIoControl(handle,0x0032C004,input,8,output,4096,out returned,IntPtr.Zero)) return "EvalError="+Marshal.GetLastWin32Error();
   if(returned>output.Length) throw new InvalidOperationException("Invalid length");
   return "Bytes="+returned+" Data="+BitConverter.ToString(output,0,(int)returned);
  }
 }
}
