//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/fileinfo.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** <OWNER>Microsoft</OWNER>
** 
** Class:  File
**
**
** Purpose: A collection of methods for manipulating Files.
**
**          April 09,2000 (some design refactorization)
**
===========================================================*/

using System;
#if FEATURE_MACL
using System.Security.AccessControl;
#endif
using System.Security.Permissions;
using PermissionSet = System.Security.PermissionSet;
//using Win32Native = Microsoft.Win32.Win32Native;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Serialization;
using System.Globalization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;

namespace FolderSyncNetSource
{
    // Class for creating FileStream objects, and some basic file management
    // routines such as Delete, etc.
    [Serializable]
    [ComVisible(true)]
    public sealed class FileInfo : FileSystemInfo
    {
        private String _name;

#if FEATURE_CORESYSTEM
        [System.Security.SecuritySafeCritical]
#endif //FEATURE_CORESYSTEM
        internal FileInfo(string fullPath, string fileName)
        {
            _name = fileName;
            OriginalPath = _name;
            FullPath = fullPath;
            //DisplayPath = _name;
        }

        public override String Name
        {
            get { return _name; }
        }

        // Deletes a file. The file specified by the designated path is deleted. 
        // If the file does not exist, Delete succeeds without throwing
        // an exception.
        // 
        // On NT, Delete will fail for a file that is open for normal I/O
        // or a file that is memory mapped.  On Win95, the file will be 
        // deleted irregardless of whether the file is being used.
        // 
        // Your application must have Delete permission to the target file.
        // 
        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        public override void Delete()
        {
            throw new NotImplementedException();
        }

        // Tests if the given file exists. The result is true if the file
        // given by the specified path exists; otherwise, the result is
        // false.  
        //
        // Your application must have Read permission for the target directory.
        public override bool Exists
        {
            [System.Security.SecuritySafeCritical]  // auto-generated
            get
            {
                try
                {
                    if (_dataInitialised == -1)
                        Refresh();
                    if (_dataInitialised != 0)
                    {
                        // Refresh was unable to initialise the data.
                        // We should normally be throwing an exception here, 
                        // but Exists is supposed to return true or false.
                        return false;
                    }
                    return (_data.fileAttributes & Win32Native.FILE_ATTRIBUTE_DIRECTORY) == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

    }
}

