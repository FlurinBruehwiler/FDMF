using System;
using System.Text;
using LightningDB;
using Shared.Utils;

class Program
{
    static unsafe void Main()
    {
        var BasePtr = (byte*)Arena.VirtualAlloc(IntPtr.Zero, 1000, Arena.AllocationType.Reserve, Arena.MemoryProtection.ReadWrite);
        BasePtr[10] = 100;
    }
}