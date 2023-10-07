//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/System.Data/System/Data/SQLTypes/UnsafeNativeMethods.cs

//------------------------------------------------------------------------------
// <copyright file="SafeNativeMethods.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// <owner current="true" primary="true">Microsoft</owner>
// <owner current="true" primary="false">Microsoft</owner>
// <owner current="true" primary="false">Microsoft</owner>
//------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;
using System.Data.Common;
using System.Runtime.Versioning;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace FolderSyncNetSource
{
    public class HGlobalSafeHandle : SafeHandleZeroOrMinusOneIsInvalid  //roalnd
    {
        [System.Security.SecurityCritical]  // auto-generated_required
        internal HGlobalSafeHandle() 
            : base(true) { }

        [System.Security.SecurityCritical]
        public HGlobalSafeHandle(IntPtr handle)
            : base(true)
        {
            this.SetHandle(handle);
        }

        [System.Security.SecurityCritical]
        override protected bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }

        public IntPtr IntPtr()
        {
            return handle;
        }
    }   //public class HGlobalSafeHandle : SafeHandleZeroOrMinusOneIsInvalid

    [SuppressUnmanagedCodeSecurity]
    internal static class UnsafeNativeMethods
    {
#region PInvoke methods

        //https://github.com/jhalon/SharpCall/blob/master/Native.cs
        [DllImport("ntdll.dll")]
        public static extern void RtlInitUnicodeString(ref UNICODE_STRING DestinationString, [MarshalAs(UnmanagedType.LPWStr)] string SourceString);

        public const int OBJ_EXCLUSIVE = 0x00000020;   //https://processhacker.sourceforge.io/doc/ntbasic_8h.html
        public const int OBJ_INHERIT = 0x00000002;      //https://processhacker.sourceforge.io/doc/ntbasic_8h.html
        public const int FILE_FLAG_OVERLAPPED = (int)FileOptions.Asynchronous; //0x40000000;    //https://github.com/tpn/winsdk-7/blob/master/v7.1A/Include/WinBase.h

        public const int STATUS_OBJECT_NAME_INVALID = unchecked((int)0xC0000033);   //C0000034 = STATUS_OBJECT_NAME_NOT_FOUND   //C000003A = STATUS_OBJECT_PATH_NOT_FOUND (if using wrong drive letter)

        //[DebuggerHidden]
        internal static SafeFileHandle NtCreateFile   //roland
        (
            string path,
            int fileAccess,
            FileAttributes fileAttributes,
            FileShare shareMode,
            CreationDisposition fileMode,
            CreateOption createOption,
            int objectAttributes
        )
        {
            path = FolderSync.Extensions.GetLongPath(path);     //NtCreateFile requires path prefix, so in case it is missing, we start from creating the path in \\?\ form. Later we replace it to \??\ form. This is because usually we assume that the path already is in \\?\ form.

            if (  
                path.Length >= 4    //necessary to avoid exceptions in Substring()
                && (path.Substring(0, 4) == @"\\?\" || path.Substring(0, 4) == @"\\.\")     //https://github.com/mirror/reactos/blob/master/rostests/apitests/ntdll/RtlDosPathNameToNtPathName_U.c
            )
            {
                //https://sourceware.org/pipermail/cygwin-developers/2008-March/008470.html
                //https://www.codeproject.com/Questions/1063766/NtCreateFile-Returning-Large-Neg-for-Directory
                //https://community.osr.com/discussion/109559/zwcreatfile-failure
                path = @"\??\" + path.Substring(4);
            }

            //https://github.com/jhalon/SharpCall/blob/master/Program.cs
            UNICODE_STRING filename = new UNICODE_STRING();
            RtlInitUnicodeString(ref filename, path);
            //IntPtr objectName = Marshal.AllocHGlobal(Marshal.SizeOf(filename));
            using (HGlobalSafeHandle objectName = new HGlobalSafeHandle(Marshal.AllocHGlobal(Marshal.SizeOf(filename))))
            {
                Marshal.StructureToPtr(filename, objectName.IntPtr(), true);

                OBJECT_ATTRIBUTES FileObjectAttributes = new OBJECT_ATTRIBUTES
                {
                    length = (int)Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES)),
                    rootDirectory = IntPtr.Zero,
                    objectName = objectName,
                    attributes = objectAttributes | ((!shareMode.HasFlag(FileShare.Read) && !shareMode.HasFlag(FileShare.Write)) ? OBJ_EXCLUSIVE : 0), //0x00000040, // OBJ_CASE_INSENSITIVE
                    securityDescriptor = IntPtr.Zero,
                    securityQualityOfService = IntPtr.Zero //default(SafeHandle) //IntPtr.Zero
                };

                IO_STATUS_BLOCK IoStatusBlock; // = new IO_STATUS_BLOCK();
                long allocationSize = 0;

                SafeFileHandle fileHandle;
                //see also https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile
                var status = NtCreateFile
                (
                    out fileHandle,
                    fileAccess, //FileAccess.FILE_GENERIC_WRITE,
                    ref FileObjectAttributes,
                    out IoStatusBlock,
                    ref allocationSize,
                    fileAttributes,
                    shareMode, //FileShare.Write,   //shareAccess
                    fileMode, //CreationDisposition.FILE_OPEN,  //createDisposition,
                    createOption, //CreateOption.FILE_SYNCHRONOUS_IO_NONALERT,  //createOptions
                    IntPtr.Zero,    //eaBuffer
                    0   //eaLength
                );
                                
                if (status == STATUS_OBJECT_NAME_INVALID)
                {
                    throw new IOException("NtCreateFile failed", unchecked((int)status));
                }                

                return fileHandle;

            }   //using (var objectName = new HGlobalSafeHandle(Marshal.AllocHGlobal(Marshal.SizeOf(filename)))
        }   //internal static SafeFileHandle NtCreateFile

        [DllImport("NtDll.dll", CharSet = CharSet.Unicode)]
        [ResourceExposure(ResourceScope.Machine)]
        internal static extern /*U*/Int32 NtCreateFile
        (
            out Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle,
            Int32 desiredAccess,
            ref OBJECT_ATTRIBUTES objectAttributes,
            out IO_STATUS_BLOCK ioStatusBlock,
            ref Int64 allocationSize,
            FileAttributes fileAttributes,
            FileShare shareAccess,
            CreationDisposition createDisposition,
            CreateOption createOptions,
            IntPtr/*SafeHandle*/ eaBuffer,
            UInt32 eaLength
        );

#endregion

        internal enum Method
        {
            METHOD_BUFFERED,
            METHOD_IN_DIRECT,
            METHOD_OUT_DIRECT,
            METHOD_NEITHER
        };

        internal enum Access
        {
            FILE_ANY_ACCESS,
            FILE_READ_ACCESS,
            FILE_WRITE_ACCESS
        }

#region Error codes

        internal const int ERROR_INVALID_HANDLE = 6;
        internal const int ERROR_MR_MID_NOT_FOUND = 317;

        internal const uint STATUS_INVALID_PARAMETER = 0xc000000d;
        internal const uint STATUS_SHARING_VIOLATION = 0xc0000043;
        internal const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xc0000034;

#endregion

#region definitions from wdm.h

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct OBJECT_ATTRIBUTES
        {
            internal int length;
            internal IntPtr rootDirectory;
            internal SafeHandle objectName;
            internal int attributes;
            internal IntPtr securityDescriptor;
            internal IntPtr/*SafeHandle*/ securityQualityOfService;
        }

        [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct UNICODE_STRING
        {
            internal UInt16 length;
            internal UInt16 maximumLength;
            internal IntPtr buffer;     //roland: https://gist.github.com/rasta-mouse/2f6316083dd2f38bb91f160cca2088df
            //internal string buffer;   //cob roland
        }

        // VSTFDevDiv # 547461 [Backport SqlFileStream fix on Win7 to QFE branch]
        // Win7 enforces correct values for the _SECURITY_QUALITY_OF_SERVICE.qos member.
        // taken from _SECURITY_IMPERSONATION_LEVEL enum definition in winnt.h
        internal enum SecurityImpersonationLevel
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct SECURITY_QUALITY_OF_SERVICE
        {
            internal UInt32 length;
            [MarshalAs(UnmanagedType.I4)]
            internal int impersonationLevel;
            internal byte contextDynamicTrackingMode;
            internal byte effectiveOnly;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct IO_STATUS_BLOCK
        {
            internal UInt32 status;
            internal IntPtr information;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct FILE_FULL_EA_INFORMATION
        {
            internal UInt32 nextEntryOffset;
            internal Byte flags;
            internal Byte EaNameLength;
            internal UInt16 EaValueLength;
            internal Byte EaName;
        }

        [Flags]
        internal enum CreateOption : uint
        {
            FILE_DIRECTORY_FILE = 0x00000001, 	//roland
            FILE_WRITE_THROUGH = 0x00000002,
            FILE_SEQUENTIAL_ONLY = 0x00000004,
            FILE_NO_INTERMEDIATE_BUFFERING = 0x00000008,
            FILE_SYNCHRONOUS_IO_ALERT = 0x00000010, 	//roland
            FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020,
            FILE_NON_DIRECTORY_FILE = 0x00000040,     //roland
            FILE_RANDOM_ACCESS = 0x00000800,
        }

        internal enum CreationDisposition : uint
        {
            FILE_SUPERSEDE = 0,
            FILE_OPEN = 1,
            FILE_CREATE = 2,
            FILE_OPEN_IF = 3,
            FILE_OVERWRITE = 4,
            FILE_OVERWRITE_IF = 5
        }

#endregion

#region definitions from winnt.h

        internal const int FILE_READ_DATA = 0x0001;
        internal const int FILE_WRITE_DATA = 0x0002;
        internal const int FILE_READ_ATTRIBUTES = 0x0080;
        internal const int SYNCHRONIZE = 0x00100000;

#endregion

#region definitions from ntdef.h

        [Flags]
        internal enum Attributes : uint
        {
            Inherit = 0x00000002,
            Permanent = 0x00000010,
            Exclusive = 0x00000020,
            CaseInsensitive = 0x00000040,
            OpenIf = 0x00000080,
            OpenLink = 0x00000100,
            KernelHandle = 0x00000200,
            ForceAccessCheck = 0x00000400,
            ValidAttributes = 0x000007F2
        }

#endregion

    }
}
