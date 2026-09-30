// Read-only ABI discovery against Intel's installed ipfcore.dll.
// Interface layout reference: intel/dptf IPF/Sources/Common/ipf_core_iface.h (Apache-2.0).
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

class IpfProbe
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetInterface(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Init();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Exit();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate ulong Create(IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Connect(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Destroy(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetApp(IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int AppCreate(IntPtr interfaces, ulong esif, out ulong app, IntPtr data, int state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int AppDestroy(ulong app);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int AppName(ref Data name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int ParticipantCreate(ulong app, ulong participant, IntPtr data, int state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int ParticipantDestroy(ulong app, ulong participant);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int DomainCreate(ulong app, ulong participant, ulong domain, IntPtr data, int state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int DomainDestroy(ulong app, ulong participant, ulong domain);
    static readonly System.Collections.Generic.List<string> participants = new System.Collections.Generic.List<string>();
    static string DataString(IntPtr ptr) {
        Data value=(Data)Marshal.PtrToStructure(ptr,typeof(Data));
        if(value.buffer==IntPtr.Zero || value.length>4096 || value.length>value.capacity) return "";
        return Marshal.PtrToStringAnsi(value.buffer,(int)value.length).TrimEnd('\0');
    }
    static readonly Delegate[] callbacks = new Delegate[] {
        new AppCreate(delegate(IntPtr iface, ulong esif, out ulong app, IntPtr data, int state) { app=1; return 0; }),
        new AppDestroy(delegate(ulong app) { return 0; }),
        new AppName(delegate(ref Data data) { byte[] name=System.Text.Encoding.ASCII.GetBytes("GalaxyHelperReadOnly\0"); data.length=(uint)name.Length; if(data.capacity<data.length) return 1300; Marshal.Copy(name,0,data.buffer,name.Length); return 0; }),
        new ParticipantCreate(delegate(ulong app, ulong id, IntPtr data, int state) { try { string name=DataString(IntPtr.Add(data,44)); lock(participants) participants.Add(name); Console.WriteLine("Participant="+name+" Description="+DataString(IntPtr.Add(data,64))+" Device="+DataString(IntPtr.Add(data,104))); return 0; } catch { return 1003; } }),
        new ParticipantDestroy(delegate(ulong app, ulong id) { return 0; }),
        new DomainCreate(delegate(ulong app, ulong participant, ulong domain, IntPtr data, int state) { try { Console.WriteLine("Domain="+DataString(IntPtr.Add(data,4))+" Type="+Marshal.ReadInt32(data,64)); return 0; } catch { return 1003; } }),
        new DomainDestroy(delegate(ulong app, ulong participant, ulong domain) { return 0; })
    };
    [StructLayout(LayoutKind.Sequential, Pack=1)] struct Data { public int type; public IntPtr buffer; public uint capacity; public uint length; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Execute(ulong session, ref Data command, IntPtr request, ref Data response);
    static void Query(Execute execute, ulong session, string command)
    {
        IntPtr input = Marshal.StringToHGlobalAnsi(command), output = Marshal.AllocHGlobal(65536);
        try {
            Marshal.Copy(new byte[65536], 0, output, 65536);
            var cmd = new Data { type=8, buffer=input, capacity=(uint)command.Length+1, length=(uint)command.Length+1 };
            var response = new Data { type=36, buffer=output, capacity=65536 };
            int rc=execute(session, ref cmd, IntPtr.Zero, ref response);
            Console.WriteLine("Query=" + command + " rc=" + rc + " type=" + response.type + " length=" + response.length);
            if(response.length > 0 && response.length <= 65536) { byte[] data=new byte[response.length]; Marshal.Copy(output,data,0,data.Length); Console.WriteLine(BitConverter.ToString(data)); }
        } finally { Marshal.FreeHGlobal(input); Marshal.FreeHGlobal(output); }
    }
    static T Fn<T>(IntPtr data, int index) where T : class
    { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(data, 68 + index * 8), typeof(T)) as T; }
    static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] != "--elevated")) return 2;
        IntPtr module = LoadLibraryEx(args[0], IntPtr.Zero, 0x1100);
        if (module == IntPtr.Zero) { Console.WriteLine("Load failed: " + Marshal.GetLastWin32Error()); return 1; }
        IntPtr memory = Marshal.AllocHGlobal(156);
        try
        {
            Marshal.Copy(new byte[156],0,memory,156);
            Marshal.WriteInt32(memory,0,7); Marshal.WriteInt16(memory,4,1); Marshal.WriteInt16(memory,6,156);
            // Declare the actual public SDK used for this layout, not an empty version.
            byte[] sdk = System.Text.Encoding.ASCII.GetBytes("1.0.11402\0");
            Marshal.Copy(sdk, 0, IntPtr.Add(memory, 48), sdk.Length);
            IntPtr entry = GetProcAddress(module,"GetIpfInterface");
            if (entry == IntPtr.Zero) { Console.WriteLine("GetIpfInterface not exported"); return 1; }
            var get = (GetInterface)Marshal.GetDelegateForFunctionPointer(entry,typeof(GetInterface));
            int result = get(memory); Console.WriteLine("GetIpfInterface=" + result);
            Console.WriteLine("type=" + Marshal.ReadInt32(memory) + " version=" + Marshal.ReadInt16(memory,4) + " size=" + Marshal.ReadInt16(memory,6));
            if (result != 0 || Marshal.ReadInt32(memory) != 7 || Marshal.ReadInt16(memory,4) != 1 || Marshal.ReadInt16(memory,6) != 156) return 1;
            Console.WriteLine("Core=" + Marshal.PtrToStringAnsi(IntPtr.Add(memory,8)) + " SDK=" + Marshal.PtrToStringAnsi(IntPtr.Add(memory,28)));
            var init=Fn<Init>(memory,0); var exit=Fn<Exit>(memory,1); var create=Fn<Create>(memory,3); var destroy=Fn<Destroy>(memory,4); var connect=Fn<Connect>(memory,5); var disconnect=Fn<Destroy>(memory,6);
            result=init(); Console.WriteLine("Init=" + result); if(result!=0) return 1;
            try
            {
                IntPtr appInterface = Marshal.AllocHGlobal(192);
                try {
                    Marshal.Copy(new byte[192],0,appInterface,192);
                    Marshal.WriteInt32(appInterface,0,0); Marshal.WriteInt16(appInterface,4,4); Marshal.WriteInt16(appInterface,6,192);
                    int[] slots={0,1,8,10,11,12,13};
                    for(int n=0;n<slots.Length;n++) Marshal.WriteIntPtr(appInterface,8+slots[n]*8,Marshal.GetFunctionPointerForDelegate(callbacks[n]));
                    int registered=Fn<SetApp>(memory,2)(appInterface); Console.WriteLine("SetAppIface="+registered); if(registered!=0) return 1;
                } finally { Marshal.FreeHGlobal(appInterface); }
                IntPtr info = Marshal.AllocHGlobal(1456);
                ulong session;
                try {
                    Marshal.Copy(new byte[1456],0,info,1456); Marshal.WriteInt32(info,1);
                    byte[] app=System.Text.Encoding.ASCII.GetBytes("GalaxyHelperReadOnly\0"); Marshal.Copy(app,0,IntPtr.Add(info,4),app.Length);
                    if(args.Length==2) { byte[] address=System.Text.Encoding.ASCII.GetBytes("pipe://ipfsrv.elevated\0"); Marshal.Copy(address,0,IntPtr.Add(info,1176),address.Length); }
                    session=create(info);
                } finally { Marshal.FreeHGlobal(info); }
                Console.WriteLine("Session=" + session.ToString("X"));
                if (session == UInt64.MaxValue || session == 0) return 1;
                try
                {
                    var task=Task.Run(() => connect(session));
                    if(!task.Wait(5000)) { Console.WriteLine("Connection timeout; no commands sent"); Environment.Exit(3); }
                    Console.WriteLine("PublicConnect=" + task.Result);
                    if(task.Result==0) {
                        try {
                            System.Threading.Thread.Sleep(1000);
                            var execute=Fn<Execute>(memory,8);
                            string[] names; lock(participants) names=participants.ToArray();
                            Console.WriteLine("ParticipantCount="+names.Length);
                            foreach(string name in names) {
                                if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9_]{1,64}$")) continue;
                                Query(execute,session,"execute-primitive " + name + " GET_FAN_STATUS D0");
                            }
                        }
                        finally { disconnect(session); }
                    }
                }
                finally { destroy(session); }
            }
            finally { exit(); }
            return 0;
        }
        finally { Marshal.FreeHGlobal(memory); FreeLibrary(module); }
    }
}
