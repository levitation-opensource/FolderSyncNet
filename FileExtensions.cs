//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC
using System;
#if NETSTANDARD
using System.Buffers;
#endif
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FolderSync
{
    public static partial class FileExtensions
    {
        //adapted from https://github.com/dotnet/runtime/blob/5ddc873d9ea6cd4bc6a935fec3057fe89a6932aa/src/libraries/System.IO.FileSystem/src/System/IO/File.cs

        //internal const int DefaultBufferSize = 4096;
        internal const int DefaultBufferSize = 1024 * 1024;     //roland

        private static Encoding s_UTF8NoBOM;

        // UTF-8 without BOM and with error detection. Same as the default encoding for StreamWriter.
        private static Encoding UTF8NoBOM => s_UTF8NoBOM ?? (s_UTF8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));


        private static StreamWriter AsyncStreamWriter(string path, Encoding encoding, bool append)
        {
            FileStream stream = new FileStream
            (
                path, 
                append ? FileMode.Append : FileMode.Create, 
                FileAccess.Write, 
                FileShare.Read, 
                DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            return new StreamWriter(stream, encoding);
        }

        private static async Task InternalWriteAllTextAsync(StreamWriter sw, string contents, string path, CancellationToken cancellationToken, bool suppressLogFile = false, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            char[] buffer = null;
            try
            {
#if NETSTANDARD
                buffer = ArrayPool<char>.Shared.Rent(DefaultBufferSize);
#else 
                buffer = new char[DefaultBufferSize / sizeof(char)];
#endif
                int count = contents.Length;
                int sourceOffset = 0;
                while (sourceOffset < count)
                {
                    int batchSize = Math.Min(DefaultBufferSize / sizeof(char), count - sourceOffset);
                    contents.CopyTo(sourceOffset, buffer, 0, batchSize);

                    //sw.WriteAsync(buffer, 0, batchSize).ConfigureAwait(false);
                    await Extensions.FSOperation
                    (
                        async (cancellationAndTimeoutToken) => await sw.WriteAsync(buffer, 0, batchSize),  //TODO: add cancellationToken here?
                        path,
                        cancellationToken,
                        timeout: timeout ?? Global.FileBufferWriteTimeout,
                        suppressLogFile: suppressLogFile,
                        suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                    );

                    sourceOffset += batchSize;
                }

                //cancellationToken.ThrowIfCancellationRequested();     //cob roland: let log writing complete
                /*do    //roland
                {
                    try
                    {*/
                        await Extensions.FSOperation
                        (
                            async (cancellationAndTimeoutToken) => await sw.FlushAsync(),  //TODO: add cancellationToken here?
                            path,
                            //cancellationToken,    //cob roland: let log writing complete
                            default(CancellationToken),  //roland: let log writing complete
                            timeout: timeout ?? Global.FileBufferWriteTimeout,
                            suppressLogFile: suppressLogFile,
                            suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                        );

                        /*break;
                    }
                    catch (Exception)
                    {
                        //TODO: tune delay
#if !NOASYNC
                        await Task.Delay(10, cancellationToken);
#else
                        cancellationToken.WaitHandle.WaitOne(10);
#endif
                    }
                }
                while (!cancellationToken.IsCancellationRequested);*/

                cancellationToken.ThrowIfCancellationRequested();    //roland: let log writing complete at least for one attempt loop
            }
            finally
            {
                //StreamReader, StreamWriter, BinaryReader and BinaryWriter all close/dispose their underlying streams when you call Dispose on them.
                //Personally I prefer to have a using statement for the stream as well.
                //https://stackoverflow.com/questions/1065168/does-disposing-streamreader-close-the-stream

                //sw.Dispose(); //cob roland: refactored to use using statement
#if NETSTANDARD
                if (buffer != null)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }
#endif
            }
        }

        public static Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default(CancellationToken), bool suppressLogFile = false, int? timeout = null, bool suppressLongRunningOperationMessage = false)
            => AppendAllTextAsync(path, contents, UTF8NoBOM, cancellationToken, suppressLogFile, timeout, suppressLongRunningOperationMessage);

        public static async Task AppendAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken), bool suppressLogFile = false, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath", nameof(path));


            var longRunningOperationMessage = !suppressLongRunningOperationMessage ? "Running append operation on " + path : null;

            while (true)    //roland
            {
                try    //roland
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    //if (cancellationToken.IsCancellationRequested)
                    //{
                    //    return Task.FromCanceled(cancellationToken);
                    //}

                    if (string.IsNullOrEmpty(contents))
                    {
                        // Just to throw exception if there is a problem opening the file.
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read).Dispose();
                        return; // Task.CompletedTask;
                    }

                    using (var writer = AsyncStreamWriter(path, encoding, append: true))
                    { 
                        await InternalWriteAllTextAsync
                        (
                            writer, 
                            contents, 
                            path, 
                            cancellationToken,
                            suppressLogFile,
                            timeout,
                            suppressLongRunningOperationMessage
                        );
                    }

                    return;
                }
                catch (Exception ex) when (     //roland
                    /*ex is IOException
                    || ex is UnauthorizedAccessException    //can happen when a folder was just created     //TODO: abandon retries after a certain number of attempts in this case
                    || */ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                    || ex.GetInnermostException() is UnauthorizedAccessException
                )
                {
                    //retry after delay

                    if (longRunningOperationMessage != null)
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.Yellow, "RETRYING " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                    }
#if !NOASYNC
                    await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                    cancellationToken.WaitHandle.WaitOne(1000);
#endif
                }
            }   //while (true)

            if (longRunningOperationMessage != null)
            {
                await ConsoleWatch.AddMessage(ConsoleColor.Red, "FAILED AND GIVING UP " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
            }
        }   //public static async Task AppendAllTextAsync()
    }
}
