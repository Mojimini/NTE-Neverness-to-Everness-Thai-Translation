using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

// A worker timeout must also stop its repak children before releasing the app lock.
internal sealed class WorkerJob : IDisposable {
    IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic {public long PerProcessTime,PerJobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint ActiveLimit;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] struct Counters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] struct Extended {public Basic Basic;public Counters Counters;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr security,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int type,IntPtr data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public WorkerJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new InvalidOperationException("เริ่มเครื่องมือฟอนต์ไม่ได้");var data=new Extended();data.Basic.Flags=0x2000;int size=Marshal.SizeOf(data);IntPtr ptr=Marshal.AllocHGlobal(size);try{Marshal.StructureToPtr(data,ptr,false);if(!SetInformationJobObject(handle,9,ptr,(uint)size)){Dispose();throw new InvalidOperationException("ตั้งค่าการหยุดเครื่องมือฟอนต์ไม่ได้");}}finally{Marshal.FreeHGlobal(ptr);}}
    public void Attach(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new InvalidOperationException("ควบคุมเครื่องมือฟอนต์ไม่ได้ กรุณาลองใหม่");}
    public void Terminate(){if(handle!=IntPtr.Zero&&!TerminateJobObject(handle,1))throw new InvalidOperationException("หยุดเครื่องมือฟอนต์ไม่สำเร็จ");}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
