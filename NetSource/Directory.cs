//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/directory.cs

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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security;
using System.Security.Permissions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;
using System.Threading;

using SearchOption = System.IO.SearchOption;

namespace FolderSyncNetSource
{
    [ComVisible(true)]
    public static class Directory
    {
        // Private class that holds search data that is passed around 
        // in the heap based stack recursion
        internal sealed class SearchData
        {
            public SearchData(String fullPath, String userPath, SearchOption searchOption)
            {
                Contract.Requires(fullPath != null && fullPath.Length > 0);
                Contract.Requires(userPath != null && userPath.Length > 0);
                Contract.Requires(searchOption == SearchOption.AllDirectories || searchOption == SearchOption.TopDirectoryOnly);

                this.fullPath = fullPath;
                this.userPath = userPath;
                this.searchOption = searchOption;
            }

            public readonly string fullPath;     // Fully qualified search path excluding the search criteria in the end (ex, c:\temp\bar\foo)
            public readonly string userPath;     // User specified path (ex, bar\foo)
            public readonly SearchOption searchOption;
        }
    }
}

