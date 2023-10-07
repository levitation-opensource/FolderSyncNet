//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/file.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  Directory
** 
** <OWNER>Microsoft</OWNER>
**
**
** Purpose: Exposes routines for enumerating through a 
** directory.
**
**          April 11,2000
**
===========================================================*/

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  File
** 
** <OWNER>Microsoft</OWNER>
**
**
** Purpose: A collection of methods for manipulating Files.
**
**        April 09,2000 (some design refactorization)
**
===========================================================*/

using System;
using System.Security.Permissions;
using PermissionSet = System.Security.PermissionSet;
//using Win32Native = Microsoft.Win32.Win32Native;
using System.Runtime.InteropServices;
using System.Security;
#if FEATURE_MACL
using System.Security.AccessControl;
#endif
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;

namespace FolderSyncNetSource
{
    // Class for creating FileStream objects, and some basic file management
    // routines such as Delete, etc.
    [ComVisible(true)]
    public static class File
    {
        // Moves a specified file to a new location and potentially a new file name.
        // This method does work across volumes.
        //
        // The caller must have certain FileIOPermissions.  The caller must
        // have Read and Write permission to 
        // sourceFileName and Write 
        // permissions to destFileName.
        // 
        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public static void Move(String sourceFileName, String destFileName, bool overwrite)     //roland: bool overwrite
        {
            InternalMove(sourceFileName, destFileName, true, overwrite);
        }

        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        private static void InternalMove(String sourceFileName, String destFileName, bool checkHost, bool overwrite)     //roland: bool overwrite
        {
            if (sourceFileName == null)
                throw new ArgumentNullException("sourceFileName", /*Environment.GetResourceString*/("ArgumentNull_FileName"));
            if (destFileName == null)
                throw new ArgumentNullException("destFileName", /*Environment.GetResourceString*/("ArgumentNull_FileName"));
            if (sourceFileName.Length == 0)
                throw new ArgumentException(/*Environment.GetResourceString*/("Argument_EmptyFileName"), "sourceFileName");
            if (destFileName.Length == 0)
                throw new ArgumentException(/*Environment.GetResourceString*/("Argument_EmptyFileName"), "destFileName");
            Contract.EndContractBlock();

            String fullSourceFileName = Path.GetFullPathInternal(sourceFileName);
            String fullDestFileName = Path.GetFullPathInternal(destFileName);

#if false
#if FEATURE_CORECLR
            if (checkHost) {
                FileSecurityState sourceState = new FileSecurityState(FileSecurityStateAccess.Write | FileSecurityStateAccess.Read, sourceFileName, fullSourceFileName);
                FileSecurityState destState = new FileSecurityState(FileSecurityStateAccess.Write, destFileName, fullDestFileName);
                sourceState.EnsureState();
                destState.EnsureState();
            }
#else
            FileIOPermission.QuickDemand(FileIOPermissionAccess.Write | FileIOPermissionAccess.Read, fullSourceFileName, false, false);
            FileIOPermission.QuickDemand(FileIOPermissionAccess.Write, fullDestFileName, false, false);
#endif

            if (!InternalExists(fullSourceFileName))
                __Error.WinIOError(Win32Native.ERROR_FILE_NOT_FOUND, fullSourceFileName);
#endif

            //if (!Win32Native.MoveFile(fullSourceFileName, fullDestFileName))
            uint flags =
                    /*
                    If a file named lpNewFileName exists, the function replaces its contents with the contents of the lpExistingFileName file, provided that security requirements regarding access control lists (ACLs) are met.
                    */
                    (overwrite ? Win32Native.MOVEFILE_REPLACE_EXISTING : 0)
                    /*
                    The function does not return until the file is actually moved on the disk.
                    Setting this value guarantees that a move performed as a copy and delete operation is flushed to disk before the function returns.The flush occurs at the end of the copy operation.
                    */
                    | Win32Native.MOVEFILE_WRITE_THROUGH        //roland
                ;  
            if (!Win32Native.MoveFileEx(fullSourceFileName, fullDestFileName, flags))   //roland
            {
                //__Error.WinIOError();
                FolderSync.PrivateStaticClassMethodInvoker_Void.Invoke(NetSourceTypes.__ErrorType, "WinIOError");   //roland
            }
        }
    }
}

