//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/directoryinfo.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  DirectoryInfo
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
#if FEATURE_MACL
using System.Security.AccessControl;
#endif
using System.Security.Permissions;
using Microsoft.Win32;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;
//using System.IO;

using SearchOption = System.IO.SearchOption;
using System.Threading;

namespace FolderSyncNetSource
{
    [Serializable]
    [ComVisible(true)]
    public sealed class DirectoryInfo : FileSystemInfo
    {
#pragma warning disable CS0169
        // This member isn't used anymore but must be maintained for binary serialization compatibility
        private string[] demandDir;
#pragma warning restore CS0169


        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public DirectoryInfo(String path)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            Contract.EndContractBlock();

            Init(path, true);
        }

        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        private void Init(string path, bool checkHost)
        {
            // Special case "<DriveLetter>:" to point to "<CurrentDirectory>" instead
            if ((path.Length == 2) && (path[1] == ':'))
            {
                OriginalPath = ".";
            }
            else
            {
                OriginalPath = path;
            }

            var read = FolderSync.EnumReflector.GetValue(NetSourceTypes.FileSecurityStateAccessType, "Read"); //System.IO.FileSecurityStateAccess.Read;
            // Must fully qualify the path for the security check
            //string fullPath = System.IO.Directory.GetFullPathAndCheckPermissions(path, checkHost: checkHost);
            string fullPath = FolderSync.PrivateStaticClassMethodInvoker<string, string, bool, int>.Invoke(NetSourceTypes.DirectoryType, "GetFullPathAndCheckPermissions", path, checkHost, read);     //roland

            FullPath = fullPath;
            //DisplayPath = GetDisplayName(OriginalPath, FullPath);
        }


#if FEATURE_CORESYSTEM
        [System.Security.SecuritySafeCritical]
#endif //FEATURE_CORESYSTEM
        internal DirectoryInfo(string fullPath, string fileName)
        {
            OriginalPath = fileName;
            FullPath = fullPath;
            //DisplayPath = GetDisplayName(OriginalPath, FullPath);
        }


        public override string Name
        {
            [ResourceExposure(ResourceScope.Machine)]
            [ResourceConsumption(ResourceScope.Machine)]
            get
            {
                throw new NotImplementedException();
            }
        }

        public override string FullName
        {
            [SecuritySafeCritical]
            get
            {
                //Directory.CheckPermissions(string.Empty, FullPath, checkHost: true); //, access: FileSecurityStateAccess.PathDiscovery);
                return FullPath;
            }
        }


        // Tests if the given path refers to an existing DirectoryInfo on disk.
        // 
        // Your application must have Read permission to the directory's
        // contents.
        //
        public override bool Exists
        {
            [System.Security.SecuritySafeCritical]  // auto-generated
            get
            {
                throw new NotImplementedException();
            }
        }


        // Returns an array of Files in the current DirectoryInfo matching the 
        // given search criteria (ie, "*.txt").
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public System.IO.FileInfo[] GetFiles(String searchPattern, CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            if (searchPattern == null)
                throw new ArgumentNullException("searchPattern");
            Contract.EndContractBlock();

            return InternalGetFiles(searchPattern, SearchOption.TopDirectoryOnly, cancellationToken);
        }

        // Returns an array of Files in the current DirectoryInfo matching the 
        // given search criteria (ie, "*.txt").
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public System.IO.FileInfo[] GetFiles(String searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            if (searchPattern == null)
                throw new ArgumentNullException("searchPattern");
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException("searchOption", /*Environment.GetResourceString*/("ArgumentOutOfRange_Enum"));
            Contract.EndContractBlock();

            return InternalGetFiles(searchPattern, searchOption, cancellationToken);
        }

        // Returns an array of Files in the current DirectoryInfo matching the 
        // given search criteria (ie, "*.txt").
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        private System.IO.FileInfo[] InternalGetFiles(String searchPattern, SearchOption searchOption, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(searchPattern != null);
            Contract.Requires(searchOption == SearchOption.AllDirectories || searchOption == SearchOption.TopDirectoryOnly);

            using (var enble = FileSystemEnumerableFactory.CreateFileInfoIterator(FullPath, OriginalPath, searchPattern, searchOption, cancellationToken))
            {
                List<System.IO.FileInfo> fileList = new List<System.IO.FileInfo>(enble);
                return fileList.ToArray();
            }
        }

        // Returns an array of Files in the DirectoryInfo specified by path
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public System.IO.FileInfo[] GetFiles(CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            return InternalGetFiles("*", SearchOption.TopDirectoryOnly, cancellationToken);
        }

        // Returns an array of Directories in the current directory.
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public DirectoryInfo[] GetDirectories(CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            return InternalGetDirectories("*", SearchOption.TopDirectoryOnly, cancellationToken);
        }

        // Returns an array of Directories in the current DirectoryInfo matching the 
        // given search criteria (ie, "System*" could match the System & System32
        // directories).
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public DirectoryInfo[] GetDirectories(String searchPattern, CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            if (searchPattern == null)
                throw new ArgumentNullException("searchPattern");
            Contract.EndContractBlock();

            return InternalGetDirectories(searchPattern, SearchOption.TopDirectoryOnly, cancellationToken);
        }

        // Returns an array of Directories in the current DirectoryInfo matching the 
        // given search criteria (ie, "System*" could match the System & System32
        // directories).
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public DirectoryInfo[] GetDirectories(String searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default(CancellationToken))    //roland: CancellationToken
        {
            if (searchPattern == null)
                throw new ArgumentNullException("searchPattern");
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException("searchOption", /*Environment.GetResourceString*/("ArgumentOutOfRange_Enum"));
            Contract.EndContractBlock();

            return InternalGetDirectories(searchPattern, searchOption, cancellationToken);
        }

        // Returns an array of Directories in the current DirectoryInfo matching the 
        // given search criteria (ie, "System*" could match the System & System32
        // directories).
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        private DirectoryInfo[] InternalGetDirectories(String searchPattern, SearchOption searchOption, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(searchPattern != null);
            Contract.Requires(searchOption == SearchOption.AllDirectories || searchOption == SearchOption.TopDirectoryOnly);

            using (var enble = FileSystemEnumerableFactory.CreateDirectoryInfoIterator(FullPath, OriginalPath, searchPattern, searchOption, cancellationToken))
            {
                List<DirectoryInfo> fileList = new List<DirectoryInfo>(enble);
                return fileList.ToArray();
            }
        }

        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        public override void Delete()
        {
            //Directory.Delete(FullPath, OriginalPath, false, true);
            throw new NotImplementedException();
        }

        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        public void Delete(bool recursive)
        {
            //Directory.Delete(FullPath, OriginalPath, recursive, true);
            throw new NotImplementedException();
        }
    }
}
