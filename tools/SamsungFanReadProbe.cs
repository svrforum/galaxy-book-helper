// Only the three read commands observed in the installed Samsung PC Diagnostics.
// No caller-supplied command, EC write, diagnostic mode or fan setter.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class SamsungFanReadProbe {
 [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr parent,uint flags);
 [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr info,IntPtr device,ref Guid guid,uint index,ref InterfaceData data);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info,ref InterfaceData data,IntPtr detail,uint size,out uint required,IntPtr device);
 [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
 public static string FindInterface() {
  Guid guid=new Guid("53567919-4a93-414f-9772-7171da240ecf");
  IntPtr info=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,0x12);
  if(info==new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
  try {
   InterfaceData data=new InterfaceData(); data.Size=Marshal.SizeOf(typeof(InterfaceData));
   if(!SetupDiEnumDeviceInterfaces(info,IntPtr.Zero,ref guid,0,ref data)) throw new System.ComponentModel.Win32Exception();
   uint required; SetupDiGetDeviceInterfaceDetail(info,ref data,IntPtr.Zero,0,out required,IntPtr.Zero);
   IntPtr detail=Marshal.AllocHGlobal((int)required);
   try {
    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
    if(!SetupDiGetDeviceInterfaceDetail(info,ref data,detail,required,out required,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
    return Marshal.PtrToStringUni(IntPtr.Add(detail,4));
   } finally {Marshal.FreeHGlobal(detail);}
  } finally {SetupDiDestroyDeviceInfoList(info);}
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
 public static string Read(string path, string query) {
  if (!path.StartsWith(@"\\?\ACPI#SAM0430#",StringComparison.OrdinalIgnoreCase) || !path.EndsWith("#{53567919-4a93-414f-9772-7171da240ecf}",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unexpected Samsung interface");
  byte[] payload;
  switch(query) {
   case "Support": payload=new byte[]{0xBB,0xAA}; break;
   case "Rpm": payload=new byte[]{0x82,0xB5,0x80}; break;
   case "MaxStep": payload=new byte[]{0x82,0xB5,0x81}; break;
   default: throw new ArgumentException("Read command not allowed");
  }
  byte[] input=new byte[21],output=new byte[21];
  input[0]=0x43; input[1]=0x58; input[2]=0x7A;
  Array.Copy(payload,0,input,5,payload.Length);
  using(var h=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(h.IsInvalid) return "OpenError="+Marshal.GetLastWin32Error();
   uint count;
   if(!DeviceIoControl(h,0x82774004,input,21,output,21,out count,IntPtr.Zero)) return "IoctlError="+Marshal.GetLastWin32Error();
   string raw="Bytes="+count+" Raw="+BitConverter.ToString(output);
   if(count!=21 || output[4]!=0xAA) return "InvalidResponse "+raw;
   if(query=="Support") return "Supported="+(output[5]==0xDD && output[6]==0xCC)+" "+raw;
   if(query=="Rpm") return "FanCount="+output[7]+" Fan1Rpm="+((output[8]<<8)|output[9])+" Fan2Rpm="+((output[10]<<8)|output[11])+" "+raw;
   return "MaxStep="+output[7]+" "+raw;
  }
 }
}
