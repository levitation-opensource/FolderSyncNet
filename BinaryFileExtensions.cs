//
// Copyright (c) Roland Pihlakas 2019 - 2022
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
        public static int MaxByteArraySize = 0x7FFFFFC7; //https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/gcallowverylargeobjects-element?redirectedfrom=MSDN#remarks

        //https://stackoverflow.com/questions/18472867/checking-equality-for-two-byte-arrays/
        public static bool BinaryEqual(Binary a, Binary b)
        {
            return a.Equals(b);
        }

        public static async Task<Tuple<byte[], long>> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken), long maxFileSize = 0)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath: {0}", nameof(path));

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<Tuple<byte[], long>>(cancellationToken);

                try
                {
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 1024 * 1024,
                        useAsync: true
                    ))
                    {
                        long len = stream.Length;    //NB! the length might change during the code execution, so need to save it into separate variable

                        maxFileSize = Math.Min(MaxByteArraySize, maxFileSize);
                        if (maxFileSize > 0 && len > maxFileSize)
                        {
                            return new Tuple<byte[], long>(null, len);
                        }

                        byte[] result = new byte[len];
                        await stream.ReadAsync(result, 0, (int)len, cancellationToken);
                        return new Tuple<byte[], long>(result, len);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException 
                    || ex is UnauthorizedAccessException    //can happen when a folder was just created
                )
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
                        return await Task.FromCanceled<Tuple<byte[], long>>(cancellationToken);
                    }
                }
            }
        }

        public static async Task WriteAllBytesAsync(string path, byte[] contents, bool createTempFileFirst, CancellationToken cancellationToken = default(CancellationToken), int writeBufferKB = 0, int bufferWriteDelayMs = 0)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath: {0}", nameof(path));

            var tempPath = path;
            if (createTempFileFirst)
                tempPath += ".tmp";

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {                    
                    using (var stream = new FileStream(
                        tempPath,
                        FileMode.OpenOrCreate, 
                        FileAccess.Write, 
                        FileShare.Read,
                        bufferSize: 1024 * 1024, 
                        useAsync: true
                    ))
                    {
                        var writeBufferLength = writeBufferKB * 1024;
                        if (writeBufferLength <= 0 || bufferWriteDelayMs <= 0)  //NB! disable write buffer length limit if delay is 0
                            writeBufferLength = contents.Length;

                        for (int i = 0; i < contents.Length; i += writeBufferLength)
                        {
                            if (i > 0 && bufferWriteDelayMs > 0)
                            {
#if !NOASYNC
                                await Task.Delay(bufferWriteDelayMs, cancellationToken); 
#else
                                cancellationToken.WaitHandle.WaitOne(bufferWriteDelayMs);
#endif
                            }

                            await stream.WriteAsync(contents, i, writeBufferLength, cancellationToken);                            
                        }
                    }

                    if (createTempFileFirst)
                    {
                        if (await Extensions.FSOperation(() => File.Exists(path), cancellationToken))
                        {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                            await Extensions.FSOperation(() => File.Delete(path), cancellationToken);
#pragma warning restore SEC0116
                        }

                        await Extensions.FSOperation(() => File.Move(tempPath, path), cancellationToken);
                    }


                    return;     //exit while loop
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
