//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/microsoft/win32/win32native.cs

// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*============================================================
**
** Class:  Microsoft.Win32.Win32Native
**
**
** Purpose: The CLR wrapper for all Win32 as well as 
**          ROTOR-style Unix PAL, etc. native operations
**
**
===========================================================*/
/**
 * Notes to PInvoke users:  Getting the syntax exactly correct is crucial, and
 * more than a little confusing.  Here's some guidelines.
 *
 * For handles, you should use a SafeHandle subclass specific to your handle
 * type.  For files, we have the following set of interesting definitions:
 *
 *  [DllImport(KERNEL32, SetLastError=true, CharSet=CharSet.Auto, BestFitMapping=false)]
 *  private static extern SafeFileHandle CreateFile(...);
 *
 *  [DllImport(KERNEL32, SetLastError=true)]
 *  unsafe internal static extern int ReadFile(SafeFileHandle handle, ...);
 *
 *  [DllImport(KERNEL32, SetLastError=true)]
 *  internal static extern bool CloseHandle(IntPtr handle);
 * 
 * P/Invoke will create the SafeFileHandle instance for you and assign the 
 * return value from CreateFile into the handle atomically.  When we call 
 * ReadFile, P/Invoke will increment a ref count, make the call, then decrement
 * it (preventing handle recycling vulnerabilities).  Then SafeFileHandle's
 * ReleaseHandle method will call CloseHandle, passing in the handle field
 * as an IntPtr.
 *
 * If for some reason you cannot use a SafeHandle subclass for your handles,
 * then use IntPtr as the handle type (or possibly HandleRef - understand when
 * to use GC.KeepAlive).  If your code will run in SQL Server (or any other
 * long-running process that can't be recycled easily), use a constrained 
 * execution region to prevent thread aborts while allocating your 
 * handle, and consider making your handle wrapper subclass 
 * CriticalFinalizerObject to ensure you can free the handle.  As you can 
 * probably guess, SafeHandle  will save you a lot of headaches if your code 
 * needs to be robust to thread aborts and OOM.
 *
 *
 * If you have a method that takes a native struct, you have two options for
 * declaring that struct.  You can make it a value type ('struct' in CSharp),
 * or a reference type ('class').  This choice doesn't seem very interesting, 
 * but your function prototype must use different syntax depending on your 
 * choice.  For example, if your native method is prototyped as such:
 *
 *    bool GetVersionEx(OSVERSIONINFO & lposvi);
 *
 *
 * you must use EITHER THIS OR THE NEXT syntax:
 *
 *    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
 *    internal struct OSVERSIONINFO {  ...  }
 *
 *    [DllImport(KERNEL32, CharSet=CharSet.Auto)]
 *    internal static extern bool GetVersionEx(ref OSVERSIONINFO lposvi);
 *
 * OR:
 *
 *    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
 *    internal class OSVERSIONINFO {  ...  }
 *
 *    [DllImport(KERNEL32, CharSet=CharSet.Auto)]
 *    internal static extern bool GetVersionEx([In, Out] OSVERSIONINFO lposvi);
 *
 * Note that classes require being marked as [In, Out] while value types must
 * be passed as ref parameters.
 *
 * Also note the CharSet.Auto on GetVersionEx - while it does not take a String
 * as a parameter, the OSVERSIONINFO contains an embedded array of TCHARs, so
 * the size of the struct varies on different platforms, and there's a
 * GetVersionExA & a GetVersionExW.  Also, the OSVERSIONINFO struct has a sizeof
 * field so the OS can ensure you've passed in the correctly-sized copy of an
 * OSVERSIONINFO.  You must explicitly set this using Marshal.SizeOf(Object);
 *
 * For security reasons, if you're making a P/Invoke method to a Win32 method
 * that takes an ANSI String and that String is the name of some resource you've 
 * done a security check on (such as a file name), you want to disable best fit 
 * mapping in WideCharToMultiByte.  Do this by setting BestFitMapping=false 
 * in your DllImportAttribute.
 */

using BCLDebug = System.Diagnostics.Debug;

namespace FolderSyncNetSource
{
    using System;
    using System.Security;
    using System.Security.Principal;
    using System.Text;
    using System.Configuration.Assemblies;
    using System.Runtime.Remoting;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Microsoft.Win32.SafeHandles;
    using System.Runtime.CompilerServices;
    using System.Runtime.ConstrainedExecution;
    using System.Runtime.Versioning;

    /**
     * Win32 encapsulation for MSCORLIB.
     */
    // Remove the default demands for all P/Invoke methods with this
    // global declaration on the class.
    // <

    [System.Security.SecurityCritical]
    [SuppressUnmanagedCodeSecurityAttribute()]
    internal static class Win32Native
    {
        [DllImport(USER32, SetLastError = true)]
        internal static extern void SetLastErrorEx(uint dwErrCode, uint dwType);    //roland

        // Security Quality of Service flags
        internal const int SECURITY_ANONYMOUS = ((int)SECURITY_IMPERSONATION_LEVEL.Anonymous << 16);
        internal const int SECURITY_SQOS_PRESENT = 0x00100000;

        [StructLayout(LayoutKind.Sequential)]
        internal class SECURITY_ATTRIBUTES
        {
            internal int nLength = 0;
            // don't remove null, or this field will disappear in bcl.small
            internal unsafe byte* pSecurityDescriptor = null;
            internal int bInheritHandle = 0;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        internal struct WIN32_FILE_ATTRIBUTE_DATA
        {
            internal int fileAttributes;
            internal FILE_TIME ftCreationTime;
            internal FILE_TIME ftLastAccessTime;
            internal FILE_TIME ftLastWriteTime;
            internal int fileSizeHigh;
            internal int fileSizeLow;

            [System.Security.SecurityCritical]
            internal void PopulateFrom(ref WIN32_FIND_DATA findData)
            {
                // Copy the information to data
                fileAttributes = findData.dwFileAttributes;
                ftCreationTime = findData.ftCreationTime;
                ftLastAccessTime = findData.ftLastAccessTime;
                ftLastWriteTime = findData.ftLastWriteTime;
                fileSizeHigh = findData.nFileSizeHigh;
                fileSizeLow = findData.nFileSizeLow;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FILE_TIME
        {
            public FILE_TIME(long fileTime)
            {
                ftTimeLow = (uint)fileTime;
                ftTimeHigh = (uint)(fileTime >> 32);
            }

            public long ToTicks()
            {
                return ((long)ftTimeHigh << 32) + ftTimeLow;
            }

            internal uint ftTimeLow;
            internal uint ftTimeHigh;
        }

        internal const String KERNEL32 = "kernel32.dll";
        internal const String USER32 = "user32.dll";

        // From WinBase.h
        internal const int SEM_FAILCRITICALERRORS = 1;

        [DllImport(KERNEL32, CharSet = CharSet.Auto, BestFitMapping = true)]
        [ResourceExposure(ResourceScope.None)]
        internal static extern int FormatMessage(int dwFlags, IntPtr lpSource,
                    int dwMessageId, int dwLanguageId, [Out] StringBuilder lpBuffer,
                    int nSize, IntPtr va_list_arguments);

        // Gets an error message for a Win32 error code.
        internal static String GetMessage(int errorCode)    //used for debugging
        {
            StringBuilder sb = StringBuilderCache.Acquire(512);
            int result = Win32Native.FormatMessage(FORMAT_MESSAGE_IGNORE_INSERTS |
                FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ARGUMENT_ARRAY,
                IntPtr.Zero, errorCode, 0, sb, sb.Capacity, IntPtr.Zero);
            if (result != 0)
            {
                // result is the # of characters copied to the StringBuilder.
                return StringBuilderCache.GetStringAndRelease(sb);
            }
            else
            {
                StringBuilderCache.Release(sb);
                return /*Environment.GetResourceString*/"UnknownError_Num: " + errorCode;
            }
        }

        // Disallow access to all non-file devices from methods that take
        // a String.  This disallows DOS devices like "con:", "com1:", 
        // "lpt1:", etc.  Use this to avoid security problems, like allowing
        // a web client asking a server for "http://server/com1.aspx" and
        // then causing a worker process to hang.
        [System.Security.SecurityCritical]  // auto-generated
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal static SafeFileHandle SafeCreateFile(String lpFileName,
                    int dwDesiredAccess, System.IO.FileShare dwShareMode,
                    SECURITY_ATTRIBUTES securityAttrs, System.IO.FileMode dwCreationDisposition,
                    int dwFlagsAndAttributes, IntPtr hTemplateFile)
        {
            SafeFileHandle handle = CreateFile(lpFileName, dwDesiredAccess, dwShareMode,
                                securityAttrs, dwCreationDisposition,
                                dwFlagsAndAttributes, hTemplateFile);

            if (!handle.IsInvalid)
            {
                int fileType = Win32Native.GetFileType(handle);
                if (fileType != Win32Native.FILE_TYPE_DISK)
                {
                    handle.Dispose();
                    throw new NotSupportedException(/*Environment.GetResourceString*/("NotSupported_FileStreamOnNonFiles"));
                }
            }

            return handle;
        }

        // Do not use these directly, use the safe or unsafe versions above.
        // The safe version does not support devices (aka if will only open
        // files on disk), while the unsafe version give you the full semantic
        // of the native version.
        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.Machine)]
        private static extern SafeFileHandle CreateFile(String lpFileName,
                    int dwDesiredAccess, System.IO.FileShare dwShareMode,
                    SECURITY_ATTRIBUTES securityAttrs, System.IO.FileMode dwCreationDisposition,
                    int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport(KERNEL32)]
        [ResourceExposure(ResourceScope.None)]
        internal static extern int GetFileType(SafeFileHandle handle);


        // From WinBase.h
        internal const int FILE_TYPE_DISK = 0x0001;

        private const int FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;
        private const int FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
        private const int FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x00002000;


        // Constants from WinNT.h
        internal const int FILE_ATTRIBUTE_READONLY = 0x00000001;
        internal const int FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        internal const int FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

        internal const int IO_REPARSE_TAG_MOUNT_POINT = unchecked((int)0xA0000003);


        // Error codes from WinError.h
        internal const int ERROR_SUCCESS = 0x0;
        internal const int ERROR_INVALID_FUNCTION = 0x1;
        internal const int ERROR_FILE_NOT_FOUND = 0x2;
        internal const int ERROR_PATH_NOT_FOUND = 0x3;
        internal const int ERROR_ACCESS_DENIED = 0x5;
        internal const int ERROR_INVALID_HANDLE = 0x6;
        internal const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
        internal const int ERROR_INVALID_DATA = 0xd;
        internal const int ERROR_INVALID_DRIVE = 0xf;
        internal const int ERROR_NO_MORE_FILES = 0x12;
        internal const int ERROR_NOT_READY = 0x15;
        internal const int ERROR_BAD_LENGTH = 0x18;
        internal const int ERROR_SHARING_VIOLATION = 0x20;
        internal const int ERROR_NOT_SUPPORTED = 0x32;
        internal const int ERROR_FILE_EXISTS = 0x50;
        internal const int ERROR_INVALID_PARAMETER = 0x57;
        internal const int ERROR_BROKEN_PIPE = 0x6D;
        internal const int ERROR_CALL_NOT_IMPLEMENTED = 0x78;
        internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        internal const int ERROR_INVALID_NAME = 0x7B;
        internal const int ERROR_BAD_PATHNAME = 0xA1;
        internal const int ERROR_ALREADY_EXISTS = 0xB7;
        internal const int ERROR_ENVVAR_NOT_FOUND = 0xCB;
        internal const int ERROR_FILENAME_EXCED_RANGE = 0xCE;  // filename too long.
        internal const int ERROR_NO_DATA = 0xE8;
        internal const int ERROR_PIPE_NOT_CONNECTED = 0xE9;
        internal const int ERROR_MORE_DATA = 0xEA;
        internal const int ERROR_DIRECTORY = 0x10B;
        internal const int ERROR_OPERATION_ABORTED = 0x3E3;  // 995; For IO Cancellation
        internal const int ERROR_NOT_FOUND = 0x490;          // 1168; For IO Cancellation
        internal const int ERROR_NO_TOKEN = 0x3f0;
        internal const int ERROR_DLL_INIT_FAILED = 0x45A;
        internal const int ERROR_NON_ACCOUNT_SID = 0x4E9;
        internal const int ERROR_NOT_ALL_ASSIGNED = 0x514;
        internal const int ERROR_UNKNOWN_REVISION = 0x519;
        internal const int ERROR_INVALID_OWNER = 0x51B;
        internal const int ERROR_INVALID_PRIMARY_GROUP = 0x51C;
        internal const int ERROR_NO_SUCH_PRIVILEGE = 0x521;
        internal const int ERROR_PRIVILEGE_NOT_HELD = 0x522;
        internal const int ERROR_NONE_MAPPED = 0x534;
        internal const int ERROR_INVALID_ACL = 0x538;
        internal const int ERROR_INVALID_SID = 0x539;
        internal const int ERROR_INVALID_SECURITY_DESCR = 0x53A;
        internal const int ERROR_BAD_IMPERSONATION_LEVEL = 0x542;
        internal const int ERROR_CANT_OPEN_ANONYMOUS = 0x543;
        internal const int ERROR_NO_SECURITY_ON_OBJECT = 0x546;
        internal const int ERROR_TRUSTED_RELATIONSHIP_FAILURE = 0x6FD;


        // Win32 Structs in N/Direct style
        [Serializable]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [BestFitMapping(false)]
        internal unsafe struct WIN32_FIND_DATA
        {
            internal int dwFileAttributes;
            internal FILE_TIME ftCreationTime;
            internal FILE_TIME ftLastAccessTime;
            internal FILE_TIME ftLastWriteTime;
            internal int nFileSizeHigh;
            internal int nFileSizeLow;

            // If the file attributes' reparse point flag is set, then
            // dwReserved0 is the file tag (aka reparse tag) for the 
            // reparse point.  Use this to figure out whether something is
            // a volume mount point or a symbolic link.
            internal int dwReserved0;
            internal int dwReserved1;

            private fixed char _cFileName[260];

            // We never use this, don't expose it to avoid accidentally allocating strings
            private fixed char _cAlternateFileName[14];

            internal string cFileName { get { fixed (char* c = _cFileName) return new string(c); } }

            /// <summary>
            /// Every directory enumeration returns "." and ".." and we don't want them.
            /// Use this to avoid allocating for this simple check.
            /// </summary>
            internal bool IsRelativeDirectory
            {
                get
                {
                    fixed (char* c = _cFileName)
                    {
                        char first = c[0];
                        if (first != '.')
                            return false;
                        char second = c[1];
                        return (second == '\0' || (second == '.' && c[2] == '\0'));
                    }
                }
            }

            /// <summary>
            /// True if this represents a file.
            /// </summary>
            internal bool IsFile => (dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0;

            /// <summary>
            /// True if this is a directory and NOT one of the special "." and ".." directories.
            /// </summary>
            internal bool IsNormalDirectory => ((dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) && !IsRelativeDirectory;
        }


        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.None)]
        internal static extern SafeFindHandle FindFirstFile(string fileName, ref WIN32_FIND_DATA data);

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.None)]
        internal static extern bool FindNextFile(
                    SafeFindHandle hndFindFile,
                    ref WIN32_FIND_DATA lpFindFileData);

        [DllImport(KERNEL32)]
        [ResourceExposure(ResourceScope.None)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        internal static extern bool FindClose(IntPtr handle);


#if FEATURE_CORESYSTEM
        [DllImport(KERNEL32, SetLastError=true, CharSet=CharSet.Auto, BestFitMapping=false)]
        [ResourceExposure(ResourceScope.Machine)]
        private static extern bool MoveFileEx(String src, String dst, uint flags);

        internal static bool MoveFile(String src, String dst)
        {
            return MoveFileEx(src, dst, 2 /* MOVEFILE_COPY_ALLOWED */);
        }
#else // FEATURE_CORESYSTEM

        //https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw
        /*
        If a file named lpNewFileName exists, the function replaces its contents with the contents of the lpExistingFileName file, provided that security requirements regarding access control lists (ACLs) are met.
        */
        public const uint MOVEFILE_REPLACE_EXISTING = 1; //roland
        /*
        The function does not return until the file is actually moved on the disk.
        Setting this value guarantees that a move performed as a copy and delete operation is flushed to disk before the function returns.The flush occurs at the end of the copy operation.
        */
        public const uint MOVEFILE_WRITE_THROUGH = 8;    //roland

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.Machine)]
        internal static extern bool MoveFileEx(String src, String dst, uint flags);  //roland

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.Machine)]
        internal static extern bool MoveFile(String src, String dst);
#endif // FEATURE_CORESYSTEM


        [DllImport(KERNEL32, SetLastError = false, EntryPoint = "SetErrorMode", ExactSpelling = true)]
        [ResourceExposure(ResourceScope.Process)]
        private static extern int SetErrorMode_VistaAndOlder(int newMode);

        [DllImport(KERNEL32, SetLastError = true, EntryPoint = "SetThreadErrorMode")]
        [ResourceExposure(ResourceScope.None)]
        private static extern bool SetErrorMode_Win7AndNewer(int newMode, out int oldMode);

        // RTM versions of Win7 and Windows Server 2008 R2
        private static readonly Version ThreadErrorModeMinOsVersion = new Version(6, 1, 7600);

        // this method uses the thread-safe version of SetErrorMode on Windows 7 / Windows Server 2008 R2 operating systems.
        // 
        [ResourceExposure(ResourceScope.Process)]
        [ResourceConsumption(ResourceScope.Process)]
        internal static int SetErrorMode(int newMode)
        {
#if !FEATURE_CORESYSTEM // ARMSTUB
            if (Environment.OSVersion.Version >= ThreadErrorModeMinOsVersion)
            {
                int oldMode;
                SetErrorMode_Win7AndNewer(newMode, out oldMode);
                return oldMode;
            }
#endif
            return SetErrorMode_VistaAndOlder(newMode);
        }

        internal enum SECURITY_IMPERSONATION_LEVEL
        {
            Anonymous = 0,
            Identification = 1,
            Impersonation = 2,
            Delegation = 3,
        }
    }
}