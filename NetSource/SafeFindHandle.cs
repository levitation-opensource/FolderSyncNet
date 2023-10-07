//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/microsoft/win32/safehandles/safefindhandle.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  SafeFindHandle 
**
**
** A wrapper for find handles
**
** 
===========================================================*/

using System;
using System.Security;
using System.Security.Permissions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using Microsoft.Win32.SafeHandles;
//using Microsoft.Win32;

namespace FolderSyncNetSource
{
    [System.Security.SecurityCritical]  // auto-generated
    internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [System.Security.SecurityCritical]  // auto-generated_required
        internal SafeFindHandle() : base(true) { }

        [System.Security.SecurityCritical]
        override protected bool ReleaseHandle()
        {
            return Win32Native.FindClose(handle);
        }
    }
}