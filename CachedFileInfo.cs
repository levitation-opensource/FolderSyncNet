//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FolderSync
{
    [Serializable]
    class CachedFileInfo
    {
        public long? Length { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }
        public bool? Exists { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public DateTime CreationTimeUtc { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }
        public DateTime LastWriteTimeUtc { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public string FullName { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public FileAttributes Attributes { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public CachedFileInfo(string fullName, long length, DateTime lastWriteTimeUtc)
        {
            Exists = true;
            Length = length;

            CreationTimeUtc = lastWriteTimeUtc;
            LastWriteTimeUtc = lastWriteTimeUtc;
            FullName = fullName;
            Attributes = FileAttributes.Normal;
        }

        public CachedFileInfo(CachedFileInfo fileInfo, bool useNonFullPath)
        {
            Exists = fileInfo.Exists;
            Length = fileInfo.Length;

            CreationTimeUtc = fileInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            FullName = useNonFullPath ? ConsoleWatch.GetNonFullName(fileInfo.FullName) : fileInfo.FullName;
            Attributes = fileInfo.Attributes;
        }

        public CachedFileInfo(FileInfo fileInfo)
        {
            Exists = fileInfo.Exists;
            Length = Exists == true ? (long?)fileInfo.Length : null;   //need to check for exists else exception occurs during reading length

            CreationTimeUtc = fileInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            FullName = fileInfo.FullName;
            Attributes = fileInfo.Attributes;
        }

        public CachedFileInfo(FileSystemInfo fileSystemInfo)
        {
            var fileInfo = fileSystemInfo as FileInfo;

            Exists = fileInfo?.Exists;
            Length = Exists == true ? fileInfo?.Length : null;

            CreationTimeUtc = fileSystemInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileSystemInfo.LastWriteTimeUtc;
            FullName = fileSystemInfo.FullName;
            Attributes = fileSystemInfo.Attributes;
        }
    }   //private class CachedFileInfo
}
