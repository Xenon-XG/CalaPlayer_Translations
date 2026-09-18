using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Resolve a symbol's RVA from a PE + its PDB via dbghelp, then emit the function-start bytes as a
// UE4SS-style AOB. Usage: SigFinder <exe> <decorated-name-mask> [numBytes]
// e.g. SigFinder CalaPlayer.exe "??0FName@@*" 40
class Program
{
    const uint SYMOPT_DEBUG = 0x80000000;
    const ulong BASE = 0x10000000;

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, bool fInvadeProcess);
    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, ulong BaseOfDll, uint DllSize, IntPtr Data, uint Flags);
    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    static extern bool SymEnumSymbols(IntPtr hProcess, ulong BaseOfDll, string Mask, SymEnumProc cb, IntPtr ctx);
    [DllImport("dbghelp.dll")]
    static extern uint SymSetOptions(uint opts);

    delegate bool SymEnumProc(IntPtr pSymInfo, uint SymbolSize, IntPtr ctx);

    static readonly List<(string name, ulong rva, uint size)> Found = new();

    static bool Cb(IntPtr p, uint sz, IntPtr ctx)
    {
        ulong modBase = (ulong)Marshal.ReadInt64(p, 32);
        ulong addr = (ulong)Marshal.ReadInt64(p, 56);
        int nameLen = Marshal.ReadInt32(p, 76);
        uint symSize = (uint)Marshal.ReadInt32(p, 28);
        if (nameLen > 0 && nameLen < 4096)
        {
            string name = Marshal.PtrToStringAnsi(IntPtr.Add(p, 84), nameLen);
            Found.Add((name, addr - modBase, symSize));
        }
        return true;
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: SigFinder <exe> <mask> [numBytes]"); return 2; }
        string exe = args[0]; string mask = args[1]; int n = args.Length >= 3 ? int.Parse(args[2]) : 40;
        IntPtr h = new IntPtr(0x1234);
        SymSetOptions(SYMOPT_DEBUG);
        if (!SymInitialize(h, Path.GetDirectoryName(Path.GetFullPath(exe)), false)) { Console.Error.WriteLine("SymInitialize failed " + Marshal.GetLastWin32Error()); return 1; }
        ulong b = SymLoadModuleEx(h, IntPtr.Zero, exe, null, BASE, 0, IntPtr.Zero, 0);
        if (b == 0) { Console.Error.WriteLine("SymLoadModuleEx failed " + Marshal.GetLastWin32Error()); return 1; }
        Console.Error.WriteLine($"module loaded at 0x{b:X}; enumerating '{mask}' ...");
        SymEnumProc cb = Cb;
        if (!SymEnumSymbols(h, BASE, mask, cb, IntPtr.Zero)) Console.Error.WriteLine("SymEnumSymbols warn " + Marshal.GetLastWin32Error());
        GC.KeepAlive(cb);
        Console.Error.WriteLine($"matched {Found.Count} symbols");

        byte[] exeBytes = File.ReadAllBytes(exe);
        foreach (var (name, rva, size) in Found)
        {
            long fo = RvaToOffset(exeBytes, rva);
            string aob = fo >= 0 ? BytesToAob(exeBytes, (int)fo, n) : "<rva map failed>";
            Console.WriteLine($"RVA=0x{rva:X} size={size} | {name}");
            Console.WriteLine($"   AOB[{n}]: {aob}");
        }
        return 0;
    }

    static string BytesToAob(byte[] data, int off, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n && off + i < data.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(data[off + i].ToString("X2")); }
        return sb.ToString();
    }

    // PE RVA -> file offset via section headers
    static long RvaToOffset(byte[] d, ulong rva)
    {
        int peOff = BitConverter.ToInt32(d, 0x3C);
        // COFF header at peOff+4: Machine(2) NumberOfSections(2) ...
        int numSec = BitConverter.ToUInt16(d, peOff + 6);
        int optSize = BitConverter.ToUInt16(d, peOff + 20);
        int secStart = peOff + 24 + optSize;
        for (int i = 0; i < numSec; i++)
        {
            int s = secStart + i * 40;
            uint va = BitConverter.ToUInt32(d, s + 12);
            uint vsz = BitConverter.ToUInt32(d, s + 8);
            uint praw = BitConverter.ToUInt32(d, s + 20);
            uint rawsz = BitConverter.ToUInt32(d, s + 16);
            uint span = Math.Max(vsz, rawsz);
            if (rva >= va && rva < va + span) return praw + (long)(rva - va);
        }
        return -1;
    }
}
