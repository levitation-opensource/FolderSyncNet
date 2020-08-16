//
// Licensed to Roland Pihlakas under one or more agreements.
//
// Copyright (c) Roland Pihlakas 2019 - 2020
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FolderSync
{
    public static class FileExtensions
    {
        public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
#if false
            byte[] result;
            using (FileStream stream = File.Open(
                path, 
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            ))
            {
                result = new byte[stream.Length];
                await stream.ReadAsync(result, 0, (int)stream.Length, cancellationToken);
                return result;
            }
#else 
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 1024 * 1024,
                useAsync: true
            ))
            {
                byte[] result = new byte[stream.Length];
                await stream.ReadAsync(result, 0, (int)stream.Length, cancellationToken);
                return result;
            }
#endif
        }

        public static async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.OpenOrCreate, 
                FileAccess.Write, 
                FileShare.Read,
                bufferSize: 1024 * 1024, 
                useAsync: true
            ))
            {
                await stream.WriteAsync(contents, 0, contents.Length, cancellationToken);
            }
        }

    }
}
