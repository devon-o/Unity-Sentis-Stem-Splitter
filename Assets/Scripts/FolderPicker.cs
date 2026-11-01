using System;
using System.Runtime.InteropServices;


// Taken from: https://discussions.unity.com/t/native-folder-picker-under-windows/1624972

public static class FolderPicker
{
    private const int BIF_RETURNONLYFSDIRS = 0x0001;
    private const int BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    public static string SelectFolder()
    {
        BROWSEINFO bi = new BROWSEINFO();
        bi.lpszTitle = "Select a Folder";
        bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;

        IntPtr pidl = SHBrowseForFolder(ref bi);
        if (pidl == IntPtr.Zero)
        {
            return null; // User canceled
        }

        System.Text.StringBuilder path = new System.Text.StringBuilder(260);
        if (SHGetPathFromIDList(pidl, path))
        {
            CoTaskMemFree(pidl);
            return path.ToString(); // Return selected folder path
        }

        CoTaskMemFree(pidl);
        return null;
    }
}