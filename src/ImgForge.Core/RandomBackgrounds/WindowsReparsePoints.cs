using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Reads a Windows reparse point's tag, to tell links (symbolic links, junctions, WSL symlinks) from other reparse
/// points such as cloud placeholder folders (Dropbox, OneDrive online-only), which are searched like normal folders.
/// </summary>
internal static class WindowsReparsePoints
{
    /// <summary>The "name surrogate" bit: set for tags that point to another file or folder (i.e. links).</summary>
    private const uint NameSurrogateBit = 0x20000000;

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4; // read, write, delete
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000; // needed to open a folder
    private const uint FileFlagOpenReparsePoint = 0x00200000; // open the reparse point itself, not what it points to
    private const int FileAttributeTagInfo = 9;

    /// <summary>
    /// True when <paramref name="reparseTag"/> marks a link to another file or folder, the same rule Windows (and .NET's
    /// <c>Directory.Delete</c>) uses to decide not to descend into a reparse point.
    /// </summary>
    internal static bool IsNameSurrogate(uint reparseTag) => (reparseTag & NameSurrogateBit) != 0;

    /// <summary>
    /// The reparse tag of <paramref name="path"/>, or <c>null</c> when it can't be read. Only metadata is read (no data
    /// access is requested), so this doesn't download an online-only cloud file or folder.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static uint? GetReparseTag(string path)
    {
        using var handle = CreateFileW(path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
            return null;
        return info.ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

#pragma warning disable SYSLIB1054 // LibraryImport would need unsafe code enabled for the project.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile, int fileInformationClass, out FileAttributeTagInformation lpFileInformation, uint dwBufferSize);
#pragma warning restore SYSLIB1054
}
