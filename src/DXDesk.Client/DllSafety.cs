using System.Runtime.InteropServices;

// TƏHLÜKƏSİZLİK: P/Invoke DLL-ləri (wtsapi32, userenv, ...) yalnız System32-dən yüklənsin.
// Əks halda .NET əvvəlcə exe-nin qovluğunda axtarır və ora qoyulmuş saxta DLL SYSTEM kimi işləyər.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
