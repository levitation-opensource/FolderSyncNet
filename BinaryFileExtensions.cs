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
        public static long MaxByteArraySize = 0x7FFFFFC7; //https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/gcallowverylargeobjects-element?redirectedfrom=MSDN#remarks

        //https://stackoverflow.com/questions/18472867/checking-equality-for-two-byte-arrays/
        public static bool BinaryEqual(Binary a, Binary b)
        {
            return a.Equals(b);
        }

        public static async Task<Tuple<byte[], long>> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default(CancellationToken), long maxFileSize = 0, int retryCount = 0, int readBufferKB = 0, int bufferReadDelayMs = 0, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath: {0}", nameof(path));


            retryCount = Math.Max(0, retryCount);
            for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<Tuple<byte[], long>>(cancellationToken);

                try
                {
                    using (var stream = new FileStream
                    (
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 1024 * 1024,
                        useAsync: true
                    ))
                    {
                        long longLen = stream.Length;    //NB! the length might change during the code execution, so need to save it into separate variable

                        maxFileSize = Math.Min(MaxByteArraySize, maxFileSize);
                        if (maxFileSize > 0 && longLen > maxFileSize)
                        {
                            return new Tuple<byte[], long>(null, longLen);
                        }


                        int len = (int)longLen;
                        byte[] result = new byte[len];

#if false
                        //await stream.ReadAsync(result, 0, (int)len, cancellationToken);
                        await Extensions.FSOperation
                        (
                            async () => await stream.ReadAsync(result, 0, (int)len, cancellationToken),
                            path,
                            cancellationToken,
                            timeout: timeout ?? Global.FileBufferReadTimeout,
                            suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                        );
#else
                        var readBufferLength = readBufferKB * 1024;
                        if (readBufferLength <= 0/* || bufferReadDelayMs <= 0*/)  //NB! disable write buffer length limit if delay is 0
                            readBufferLength = len;

                        for (int readOffset = 0; readOffset < len; readOffset += readBufferLength)
                        {
                            if (readOffset > 0 && bufferReadDelayMs > 0)
                            {
#if !NOASYNC
                                await Task.Delay(bufferReadDelayMs, cancellationToken);
#else
                                cancellationToken.WaitHandle.WaitOne(bufferReadDelayMs);
#endif
                            }

                            //await stream.WriteAsync(contents, i, writeBufferLength, cancellationToken);
                            await Extensions.FSOperation
                            (
                                async () => await stream.ReadAsync(result, readOffset, Math.Min(readBufferLength, len - readOffset), cancellationToken),
                                path,
                                cancellationToken,
                                timeout: timeout ?? Global.FileBufferReadTimeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            );
                        }   //for (int i = 0; i < contents.Length; i += writeBufferLength)
#endif

                        return new Tuple<byte[], long>(result, len);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException 
                    || ex is UnauthorizedAccessException    //can happen when a folder was just created     //TODO: abandon retries after a certain number of attempts in this case
                )
                {
                    //retry after delay

                    if (tryIndex + 1 < retryCount)     //do not sleep after last try
                    {
#if !NOASYNC
                        await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                        cancellationToken.WaitHandle.WaitOne(1000);
#endif
                    }
                }
            }

            return new Tuple<byte[], long>(null, -1);

        }   //public static async Task<Tuple<byte[], long>> ReadAllBytesAsync()

        public static async Task WriteAllBytesAsync(string path, byte[] contents, bool createTempFileFirst, CancellationToken cancellationToken = default(CancellationToken), int writeBufferKB = 0, int bufferWriteDelayMs = 0, int? timeout = null, bool suppressLongRunningOperationMessage = false)
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
                    using (var stream = new FileStream
                    (
                        tempPath,
                        FileMode.OpenOrCreate, 
                        FileAccess.Write, 
                        FileShare.Read,
                        bufferSize: 1024 * 1024, 
                        useAsync: true
                    ))
                    {
                        var writeBufferLength = writeBufferKB * 1024;
                        if (writeBufferLength <= 0/* || bufferWriteDelayMs <= 0*/)  //NB! disable write buffer length limit if delay is 0
                            writeBufferLength = contents.Length;

                        for (int writeOffset = 0; writeOffset < contents.Length; writeOffset += writeBufferLength)
                        {
                            if (writeOffset > 0 && bufferWriteDelayMs > 0)
                            {
#if !NOASYNC
                                await Task.Delay(bufferWriteDelayMs, cancellationToken); 
#else
                                cancellationToken.WaitHandle.WaitOne(bufferWriteDelayMs);
#endif
                            }

                            //await stream.WriteAsync(contents, i, writeBufferLength, cancellationToken);
                            await Extensions.FSOperation
                            (
                                async () => await stream.WriteAsync(contents, writeOffset, Math.Min(writeBufferLength, contents.Length - writeOffset), cancellationToken),
                                tempPath,
                                cancellationToken,
                                timeout: timeout ?? Global.FileBufferWriteTimeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            );
                        }   //for (int i = 0; i < contents.Length; i += writeBufferLength)
                    }

                    if (createTempFileFirst)
                    {
                        if (await Extensions.FSOperation
                        (
                            () => File.Exists(path), 
                            path,
                            cancellationToken,
                            timeout: timeout,
                            suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                        ))
                        {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                            await Extensions.FSOperation
                            (
                                () => File.Delete(path), 
                                path,
                                cancellationToken,
                                timeout: timeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            );
#pragma warning restore SEC0116
                        }

                        await Extensions.FSOperation
                        (
                            () => File.Move(tempPath, path),
                            tempPath + " " + Path.PathSeparator + " " + path,
                            cancellationToken,
                            timeout: timeout,
                            suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                        );
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
        }   //public static async Task WriteAllBytesAsync()

    }
}
