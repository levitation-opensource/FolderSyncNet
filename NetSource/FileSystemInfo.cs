//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/filesysteminfo.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  FileSystemInfo    
** 
** <OWNER>Microsoft</OWNER>
**
**
** Purpose: 
**
**
===========================================================*/

using System;
using System.Collections;
using System.Security;
using System.Security.Permissions;
using Microsoft.Win32;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;
//using System.IO;

namespace FolderSyncNetSource
{
    [Serializable]
#if !FEATURE_CORECLR
    [FileIOPermissionAttribute(SecurityAction.InheritanceDemand, Unrestricted = true)]
#endif
    [ComVisible(true)]
#if FEATURE_REMOTING
    public abstract class FileSystemInfo : MarshalByRefObject, ISerializable
    {
#else // FEATURE_REMOTING
    public abstract class FileSystemInfo : System.IO.FileSystemInfo //: ISerializable
    {
#endif  //FEATURE_REMOTING

        [System.Security.SecurityCritical] // auto-generated
        internal Win32Native.WIN32_FILE_ATTRIBUTE_DATA _data; // Cache the file information
        internal int _dataInitialised = -1; // We use this field in conjunction with the Refresh methods, if we succeed
                                            // we store a zero, on failure we store the HResult in it so that we can
                                            // give back a generic error back.

        [System.Security.SecurityCritical]
        internal void InitializeFrom(ref Win32Native.WIN32_FIND_DATA findData)
        {
            _data = new Win32Native.WIN32_FILE_ATTRIBUTE_DATA();
            _data.PopulateFrom(ref findData);
            _dataInitialised = 0;
        }
    }
}

