using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WindowsProgramInventory.Windows.Shortcuts;

/// <summary>Data resolved from a Windows .lnk shell link.</summary>
public sealed class ShellLinkData
{
    public string Target { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public int IconIndex { get; init; }
    public int ShowCommand { get; init; }
}

/// <summary>
/// Reads .lnk targets through the COM IShellLinkW + IPersistFile interfaces.
/// Pure interop – no external dependency, no shell execution.
/// </summary>
public static class ShellLinkReader
{
    private const int SlgpRawPath = 0x4;
    private const int MaxPathChars = 1024;

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IShellLinkWGuid = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IPersistFileGuid = new("0000010B-0000-0000-C000-000000000046");

    public static ShellLinkData Read(string lnkPath)
    {
        var linkType = Type.GetTypeFromCLSID(ShellLinkClsid)
            ?? throw new InvalidOperationException("ShellLink COM class not available.");
        var link = (IShellLinkW)(Activator.CreateInstance(linkType)
            ?? throw new InvalidOperationException("Cannot create ShellLink COM object."));
        var persist = (IPersistFile)link;
        try
        {
            persist.Load(lnkPath, 0);

            var sb = new StringBuilder(MaxPathChars);
            link.GetPath(sb, MaxPathChars, IntPtr.Zero, SlgpRawPath);
            var target = sb.ToString();

            link.GetArguments(sb, MaxPathChars);
            var arguments = sb.ToString();

            link.GetWorkingDirectory(sb, MaxPathChars);
            var workingDir = sb.ToString();

            link.GetDescription(sb, MaxPathChars);
            var description = sb.ToString();

            link.GetIconLocation(sb, MaxPathChars, out var iconIndex);
            var iconPath = sb.ToString();

            link.GetShowCmd(out var showCommand);

            return new ShellLinkData
            {
                Target = target,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                Description = description,
                IconPath = iconPath,
                IconIndex = iconIndex,
                ShowCommand = showCommand,
            };
        }
        finally
        {
            if (persist is not null)
            {
                Marshal.FinalReleaseComObject(persist);
            }

            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }
}