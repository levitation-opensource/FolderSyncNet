//
// Copyright (c) Roland Pihlakas 2019 - 2020
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Data.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FolderSync
{
    public static partial class FileExtensions
    {
        //https://stackoverflow.com/questions/18472867/checking-equality-for-two-byte-arrays/
        public static bool BinaryEqual(Binary a, Binary b)
        {
            return a.Equals(b);
        }

        public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<byte[]>(cancellationToken);

                try
                {
                    using (FileStream stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 1024 * 1024,
                        useAsync: true
                    ))
                    {
                        var len = (int)stream.Length;    //NB! the lenght might change during the code execution, so need to save it into separate variable
                        byte[] result = new byte[len];
                        await stream.ReadAsync(result, 0, len, cancellationToken);
                        return result;
                    }
                }
                catch (IOException)
                {
                    //retry after delay
                    try
                    {
#if !NOASYNC
                        await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                        cancellationToken.WaitHandle.WaitOne(1000);
#endif
                    }
                    catch (TaskCanceledException)
                    {
                        //do nothing here
                        return await Task.FromCanceled<byte[]>(cancellationToken);
                    }
                }
            }
        }

        public static async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
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
                        return;
                    }
                }
                catch (IOException)
                {
                    //retry after delay
#if !NOASYNC
                    await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                    cancellationToken.WaitHandle.WaitOne(1000);
#endif
                }
            }
        }

    }
}
