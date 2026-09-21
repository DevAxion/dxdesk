using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SRXDesk.Service;

/// <summary>
/// Windows xidməti (LocalSystem, Session 0) tərəfindən aktiv istifadəçi
/// sessiyasında proses işə salır. Standart korporativ yayım texnikasıdır
/// (SCCM/Intune/PsExec -s ilə eyni: xidmət → interaktiv sessiya).
///
/// İstifadə: helpdesk admin uzaqdan installer/uninstaller-i SYSTEM səviyyəsində
/// işə salır ki, istifadəçidə admin parolu olmadan proqram quraşdırılsın/silinsin.
/// UAC ayarlarına toxunmur; istifadəçinin öz UAC qorunması dəyişmir.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SessionLauncher
{
    /// <summary>
    /// <paramref name="application"/>-ı aktiv konsol sessiyasında SYSTEM kimi,
    /// masaüstündə görünən şəkildə işə salır. Uğurda true qaytarır.
    /// </summary>
    public static bool LaunchInActiveSession(string application, string arguments, Action<string> log)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            log("Aktiv konsol sessiyası yoxdur (heç kim daxil olmayıb).");
            return false;
        }

        // Xidmətin öz token-i (SYSTEM). Onu dublikatlayıb sessiya id-sini dəyişirik.
        if (!OpenProcessToken(GetCurrentProcess(),
                TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                out var processToken))
        {
            log($"OpenProcessToken alınmadı: {Marshal.GetLastWin32Error()}");
            return false;
        }

        var dupToken = IntPtr.Zero;
        var envBlock = IntPtr.Zero;
        try
        {
            var sa = new SECURITY_ATTRIBUTES();
            sa.nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>();

            if (!DuplicateTokenEx(processToken, MAXIMUM_ALLOWED, ref sa,
                    SecurityImpersonationLevel.SecurityIdentification, TokenType.TokenPrimary, out dupToken))
            {
                log($"DuplicateTokenEx alınmadı: {Marshal.GetLastWin32Error()}");
                return false;
            }

            // Token-i aktiv sessiyaya köçürürük ki, proses istifadəçinin masaüstündə görünsün.
            var sid = sessionId;
            if (!SetTokenInformation(dupToken, TokenInformationClass.TokenSessionId, ref sid, sizeof(uint)))
            {
                log($"SetTokenInformation alınmadı: {Marshal.GetLastWin32Error()}");
                return false;
            }

            CreateEnvironmentBlock(out envBlock, dupToken, false);

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();
            si.lpDesktop = @"winsta0\default";

            var cmdLine = string.IsNullOrEmpty(arguments) ? application : $"\"{application}\" {arguments}";

            var created = CreateProcessAsUser(
                dupToken,
                null,
                cmdLine,
                ref sa,
                ref sa,
                false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                envBlock,
                null,
                ref si,
                out var pi);

            if (!created)
            {
                log($"CreateProcessAsUser alınmadı: {Marshal.GetLastWin32Error()}");
                return false;
            }

            log($"İşə salındı (sessiya {sessionId}, PID {pi.dwProcessId}): {cmdLine}");
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
            CloseHandle(processToken);
        }
    }

    // ---- Sabitlər ----
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    private enum SecurityImpersonationLevel { SecurityAnonymous, SecurityIdentification, SecurityImpersonation, SecurityDelegation }
    private enum TokenType { TokenPrimary = 1, TokenImpersonation }
    private enum TokenInformationClass { TokenSessionId = 12 }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars;
        public int dwYCountChars; public int dwFillAttribute; public int dwFlags; public short wShowWindow;
        public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
        ref SECURITY_ATTRIBUTES lpTokenAttributes, SecurityImpersonationLevel ImpersonationLevel,
        TokenType TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr TokenHandle, TokenInformationClass TokenInformationClass,
        ref uint TokenInformation, uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr hToken, string? lpApplicationName, string? lpCommandLine,
        ref SECURITY_ATTRIBUTES lpProcessAttributes, ref SECURITY_ATTRIBUTES lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
