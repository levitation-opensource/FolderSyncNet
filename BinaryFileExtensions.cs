//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Data.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Alphaleonis.Win32.Vss;

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

        public static async Task<Tuple<byte[], long>> ReadAllBytesAsync(string path, bool allowVSS, CancellationToken cancellationToken = default(CancellationToken), long maxFileSize = 0, int retryCount = 0, int readBufferKB = 0, int bufferReadDelayMs = 0, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath: {0}", nameof(path));


            var longRunningOperationMessage = !suppressLongRunningOperationMessage ? "Running read operation on " + path : null;
            bool suppressLogFile = false;

            retryCount = Math.Max(0, retryCount);
            for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<Tuple<byte[], long>>(cancellationToken);

                try
                {
                    if (allowVSS)
                    {
                        try
                        { 
                            var result = await ReadAllBytesWithVSSAsync(path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                            return result;
                        }
                        catch (VssException ex)
                        {
                            await ConsoleWatch.WriteException(ex);

                            var result = await ReadAllBytesNoVSSAsync(path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                            return result;
                        }
                    }
                    else
                    {
                        var result = await ReadAllBytesNoVSSAsync(path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                        return result;
                    }
                }
                catch (Exception ex) when (
                    /*ex is IOException 
                    || ex is TimeoutException
                    || ex is UnauthorizedAccessException    //can happen when a folder was just created     //TODO: abandon retries after a certain number of attempts in this case
                    || */ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                    || ex.GetInnermostException() is TimeoutException
                    || ex.GetInnermostException() is UnauthorizedAccessException
                )
                {
                    //retry after delay

                    if (
                        ex.GetInnermostException().HResult == unchecked((int)0x8007007B)    //The filename, directory name, or volume label syntax is incorrect
                        || ex.GetInnermostException().HResult == FolderSyncNetSource.UnsafeNativeMethods.STATUS_OBJECT_NAME_INVALID
                    )
                    {
                        break;
                    }
                    else if (tryIndex + 1 < retryCount)     //do not sleep after last try
                    {
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
                }
            }

            if (longRunningOperationMessage != null)
            {
                await ConsoleWatch.AddMessage(ConsoleColor.Red, "FAILED AND GIVING UP " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
            }

            return new Tuple<byte[], long>(null, -1);

        }   //public static async Task<Tuple<byte[], long>> ReadAllBytesAsync()

        public static async Task<Tuple<byte[], long>> ReadAllBytesWithVSSAsync(string path, CancellationToken cancellationToken, long maxFileSize, int readBufferKB, int bufferReadDelayMs, int? timeout, bool suppressLongRunningOperationMessage)
        {
            if (path.StartsWith(@"\\?\"))     //roland: VSS does not like \\?\ paths
                path = path.Substring(4);

            using (var vss = new VssBackup())
            {
                vss.Setup(Path.GetPathRoot(path));

#if true
                using (var stream = vss.GetStream(path))
#else
                using (var stream = new FileStream
                (
                    vss.GetSnapshotPath(path),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read, //Write,
                    bufferSize: 1024 * 1024,
                    //useAsync: true
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan
                ))
#endif
                {
                    var result = await ReadAllBytesFromStreamAsync(stream, path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                    return result;
                }
            }
        }

        //https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/filestream.cs
        [System.Security.SecuritySafeCritical]  // auto-generated
        private static FolderSyncNetSource.Win32Native.SECURITY_ATTRIBUTES GetSecAttrs(FileShare share)
        {
            FolderSyncNetSource.Win32Native.SECURITY_ATTRIBUTES secAttrs = null;
            if ((share & FileShare.Inheritable) != 0)
            {
                secAttrs = new FolderSyncNetSource.Win32Native.SECURITY_ATTRIBUTES();
                secAttrs.nLength = (int)Marshal.SizeOf(secAttrs);

                secAttrs.bInheritHandle = 1;
            }
            return secAttrs;
        }

        private const int GENERIC_READ = unchecked((int)0x80000000);
        private const int GENERIC_WRITE = 0x40000000;
        private const bool _canUseAsync = true;

        //[DebuggerHidden]
        public static async Task<Tuple<byte[], long>> ReadAllBytesNoVSSAsync(string path, CancellationToken cancellationToken, long maxFileSize, int readBufferKB, int bufferReadDelayMs, int? timeout, bool suppressLongRunningOperationMessage)
        {
            //case sensitive access
            int FILE_FLAG_POSIX_SEMANTICS = 0x01000000; //https://doxygen.reactos.org/de/ddc/modules_2rosapps_2applications_2net_2tsclient_2rdesktop_2disk_8h.html#a5beec6b1d9cb47590bb950d312ab6f93

            var accessMode = FileAccess.Read;
            var shareMode = FileShare.ReadWrite | FileShare.Delete;
            var options = FileOptions.Asynchronous | FileOptions.SequentialScan;// | (FileOptions)FILE_FLAG_POSIX_SEMANTICS; // | (FileOptions)FileAttributes.Normal;
            var ntDllOptions = FolderSyncNetSource.UnsafeNativeMethods.CreateOption.FILE_SEQUENTIAL_ONLY | FolderSyncNetSource.UnsafeNativeMethods.CreateOption.FILE_NON_DIRECTORY_FILE;
            //see also https://doxygen.reactos.org/dd/d83/dll_2win32_2kernel32_2client_2file_2create_8c_source.html
            var ntDllFileAttributes = /*(FileAttributes)FolderSyncNetSource.UnsafeNativeMethods.FILE_FLAG_OVERLAPPED | */FileAttributes.Normal;
            var ntDllObjectAttributes = FolderSyncNetSource.UnsafeNativeMethods.OBJ_INHERIT; // | FolderSyncNetSource.UnsafeNativeMethods.OBJ_CASE_INSENSITIVE;

#if true   //this fails with special names under .NET 4.0. .NET 4.6 seems to be ok though?
            bool openSuccess = false;
            try
            {
                using (var stream = new FileStream
                (
                    path,
                    FileMode.Open,
                    accessMode,
                    shareMode,
                    bufferSize: 1024 * 1024,
                    //useAsync: true
                    options: options
                ))
                {
                    openSuccess = true;
                    var result = await ReadAllBytesFromStreamAsync(stream, path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (openSuccess || !ConfigParser.IsWindows)    //open succeeded above, so the exception came from ReadAllBytesFromStreamAsync()
                    throw;
	        }
#endif

            //if (ConfigParser.IsWindows)     //TODO: would that somehow work under Linux and Mac as well?
            {
                //retry using a different stream open method

                //https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/filestream.cs
                var secAttrs = GetSecAttrs(shareMode);
                var fAccess = accessMode == FileAccess.Read ? GENERIC_READ :
                                accessMode == FileAccess.Write ? GENERIC_WRITE :
                                GENERIC_READ | GENERIC_WRITE;
                options |= (FileOptions)FILE_FLAG_POSIX_SEMANTICS;  //https://doxygen.reactos.org/dd/d83/dll_2win32_2kernel32_2client_2file_2create_8c_source.html

                bool isAsync = false;
                // WRT async IO, do the right thing for whatever platform we're on.
                // This way, someone can easily write code that opens a file 
                // asynchronously no matter what their platform is.  
                if (_canUseAsync && (options & FileOptions.Asynchronous) != 0)
                {
                    isAsync = true;
                }
                else
                {
                    options &= ~FileOptions.Asynchronous;

                    //https://doxygen.reactos.org/dd/d83/dll_2win32_2kernel32_2client_2file_2create_8c_source.html
                    //fAccess |= FolderSyncNetSource.UnsafeNativeMethods.SYNCHRONIZE;   //SYNCHRONIZE is already part of GENERIC_*     //https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile
                    //fAccess |= FolderSyncNetSource.UnsafeNativeMethods.FILE_READ_ATTRIBUTES;  //FILE_READ_ATTRIBUTES is already part of GENERIC_*     //https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile

                    /*
                    https://community.osr.com/discussion/75144/win32-handle-vs-nt-handle
                    absense of FILE_FLAG_OVERLAPPED -> FILE_SYNCHRONOUS_IO_NONALERT
                    FILE_FLAG_OVERLAPPED -> absense of FILE_SYNCHRONOUS_IO_NONALERT
                    */

                    //see also https://doxygen.reactos.org/dd/d83/dll_2win32_2kernel32_2client_2file_2create_8c_source.html
                    ntDllOptions |= FolderSyncNetSource.UnsafeNativeMethods.CreateOption.FILE_SYNCHRONOUS_IO_NONALERT;
                    //ntDllFileAttributes &= ~(FileAttributes)FolderSyncNetSource.UnsafeNativeMethods.FILE_FLAG_OVERLAPPED;
                }

                //int flagsAndAttributes = (int)options;
#if !FEATURE_PAL
                // For mitigating local elevation of privilege attack through named pipes
                // make sure we always call CreateFile with SECURITY_ANONYMOUS so that the
                // named pipe server can't impersonate a high privileged client security context
                options |= (FileOptions)(FolderSyncNetSource.Win32Native.SECURITY_SQOS_PRESENT | FolderSyncNetSource.Win32Native.SECURITY_ANONYMOUS);
#endif

#if false   //SafeCreateFile seems to work both with .NET 4.0 and .NET 4.6, but NtCreateFile would probably provide maximum robustness.
                //TODO: if using this block of code then need to detect the error opening the file due to filename and not retry. The current code does retry.
                using (var handle = FolderSyncNetSource.Win32Native.SafeCreateFile
                (
                    path,
                    fAccess,
                    shareMode,
                    secAttrs,
                    FileMode.Open,
                    (int)options,
                    IntPtr.Zero //hTemplateFile
                ))
#else 
                //https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile
                //ntDllOptions |= FILE_OPEN_FOR_BACKUP_INTENT;  //TODO
                //ntDllOptions |= FILE_RESERVE_OPFILTER;  //TODO
                //ntDllOptions |= FILE_OPEN_REQUIRING_OPLOCK;  //TODO
                //ntDllOptions |= FILE_COMPLETE_IF_OPLOCKED;  //TODO

                //NtCreateFile function is documented by Microsoft, so it is okay to use it
                using (var handle = FolderSyncNetSource.UnsafeNativeMethods.NtCreateFile    
                (
                    path,
                    fAccess,
                    ntDllFileAttributes,
                    shareMode,
                    FolderSyncNetSource.UnsafeNativeMethods.CreationDisposition.FILE_OPEN,
                    ntDllOptions,
                    ntDllObjectAttributes
                ))
#endif
                {
                    if (handle.IsInvalid)   //roland
                        //return new Tuple<byte[], long>(null, -1);   //TODO: retry with retrycount
                        throw new IOException("NtCreateFile failed");
                        //throw new IOException("SafeCreateFile failed");

                    using (var stream = new FileStream
                    (
                        handle,
                        //FileMode.Open,
                        accessMode,
                        //FileShare.ReadWrite,
                        bufferSize: 1024 * 1024,
                        //useAsync: true
                        isAsync: isAsync //options: FileOptions.Asynchronous | FileOptions.SequentialScan
                    ))
                    {
                        var result = await ReadAllBytesFromStreamAsync(stream, path, cancellationToken, maxFileSize, readBufferKB, bufferReadDelayMs, timeout, suppressLongRunningOperationMessage);
                        return result;
                    }
                }
            }   //if (ConfigParser.IsWindows)
        }   //public static async Task<Tuple<byte[], long>> ReadAllBytesNoVSSAsync(string path, CancellationToken cancellationToken, long maxFileSize, int readBufferKB, int bufferReadDelayMs, int? timeout, bool suppressLongRunningOperationMessage)

        public static async Task<Tuple<byte[], long>> ReadAllBytesFromStreamAsync(Stream stream, string path, CancellationToken cancellationToken, long maxFileSize, int readBufferKB, int bufferReadDelayMs, int? timeout, bool suppressLongRunningOperationMessage)
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
            if (readBufferLength <= 0/* || bufferReadDelayMs <= 0*/)  //NB! disable read buffer length limit if delay is 0
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

                await Extensions.FSOperation
                (
                    async (cancellationAndTimeoutToken) => await stream.ReadAsync(result, readOffset, Math.Min(readBufferLength, len - readOffset), cancellationAndTimeoutToken),
                    path,
                    cancellationToken,
                    timeout: timeout ?? Global.FileBufferReadTimeout,
                    suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                )
                ;//.Unwrap();
            }   //for (int i = 0; i < contents.Length; i += writeBufferLength)
#endif

            return new Tuple<byte[], long>(result, len);
        }

        public static async Task WriteAllBytesAsync(string path, byte[] contents, bool createTempFileFirst, CancellationToken cancellationToken = default(CancellationToken), int retryCount = 0, int writeBufferKB = 0, int bufferWriteDelayMs = 0, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException("Argument_EmptyPath: {0}", nameof(path));


            var longRunningOperationMessage = !suppressLongRunningOperationMessage ? "Running write operation on " + path : null;
            bool suppressLogFile = false;

            var tempPath = path;
            if (createTempFileFirst)
                tempPath += ".tmp";


            retryCount = Math.Max(0, retryCount);

            //write to putput file
            bool writeSuccess = false;
            for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)
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
                        //useAsync: true
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan
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
                                async (cancellationAndTimeoutToken) => await stream.WriteAsync(contents, writeOffset, Math.Min(writeBufferLength, contents.Length - writeOffset), cancellationAndTimeoutToken),
                                tempPath,
                                cancellationToken,
                                timeout: timeout ?? Global.FileBufferWriteTimeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            )
                            ;//.Unwrap();
                        }   //for (int i = 0; i < contents.Length; i += writeBufferLength)

                        //make the flush operation explicit so it can be async
                        //This is a use case for IAsyncDisposable as proposed in async streams, a candidate feature for C# 8.0, combined with a using statement enhancement that that proposal didn't cover. C# 7.x and below don't have a "nice" way to do asynchronous dispose, so the implicit Dispose() call from your using statement does the flush synchronously. The workaround is directly calling and awaiting on FlushAsync() so that the synchronous Dispose() doesn't have to synchronously flush.
                        //https://stackoverflow.com/questions/21987533/streamwriter-uses-synchronous-invocation-when-calling-asynchronous-methods
                        /*while (!cancellationToken.IsCancellationRequested)    //roland
                        {
                            try
                            {*/
                                await Extensions.FSOperation
                                (
                                    async (cancellationAndTimeoutToken) => await stream.FlushAsync(cancellationAndTimeoutToken),
                                    tempPath,
                                    cancellationToken,
                                    timeout: timeout ?? Global.FileBufferWriteTimeout,
                                    suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                                )
                                ;//.Unwrap();

                                /*break;
                            }
                            catch (Exception)
                            {
#if !NOASYNC
                                await Task.Delay(bufferWriteDelayMs, cancellationToken);
#else
                                cancellationToken.WaitHandle.WaitOne(bufferWriteDelayMs);
#endif
                            }
                        }*/

                    }   //using (var stream = new FileStream

                    writeSuccess = true;
                    break;     //exit while loop
                }
                catch (Exception ex) when (
                    /*ex is IOException
                    || ex is TimeoutException
                    || ex is UnauthorizedAccessException    //can happen when a folder was just created     //TODO: abandon retries after a certain number of attempts in this case
                    || */ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                    || ex.GetInnermostException() is TimeoutException
                    || ex.GetInnermostException() is UnauthorizedAccessException
                )
                {
                    //retry after delay

                    if (
                        ex.GetInnermostException().HResult == unchecked((int)0x8007007B)    //The filename, directory name, or volume label syntax is incorrect
                        || ex.GetInnermostException().HResult == FolderSyncNetSource.UnsafeNativeMethods.STATUS_OBJECT_NAME_INVALID
                    )
                    { 
                        break;
                    }
                    else if (tryIndex + 1 < retryCount)     //do not sleep after last try
                    {
                        if (longRunningOperationMessage != null)
                        {
                            await ConsoleWatch.AddMessage(ConsoleColor.Yellow, "RETRYING WRITE " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                        }
#if !NOASYNC
                        await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                        cancellationToken.WaitHandle.WaitOne(1000);
#endif
                    }
                    else
                    {
                        throw ex;
                    }
                }
            }   //for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)

            if (!writeSuccess)
            {
                if (longRunningOperationMessage != null)
                {
                    await ConsoleWatch.AddMessage(ConsoleColor.Red, "FAILED WRITE AND GIVING UP " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                }

                throw new ExternalException();
            }
            else
            {
                if (!createTempFileFirst)
                {
                    return;
                }
                else
                {
                    retryCount *= 2;    //use double retry count for file rename    //TODO: config

                    //rename temp file
                    for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
#if false
                            if (await Extensions.FSOperation
                            (
                                cancellationAndTimeoutToken => File.Exists(path), 
                                path,
                                cancellationToken,
                                timeout: timeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            ))
                            {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                                await Extensions.FSOperation
                                (
                                    cancellationAndTimeoutToken => File.Delete(path), 
                                    path,
                                    cancellationToken,
                                    timeout: timeout,
                                    suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                                );
#pragma warning restore SEC0116
                            }
#endif

                            await Extensions.FSOperation
                            (
                                cancellationAndTimeoutToken => FolderSyncNetSource.File.Move(tempPath, path, overwrite: true),
                                tempPath + " " + Path.PathSeparator + " " + path,
                                cancellationToken,
                                timeout: timeout,
                                suppressLongRunningOperationMessage: suppressLongRunningOperationMessage
                            );


                            return;     //exit while loop
                        }
                        catch (Exception ex) when (
                            /*ex is IOException
                            || ex is TimeoutException
                            || ex is UnauthorizedAccessException    //can happen when a folder was just created     //TODO: abandon retries after a certain number of attempts in this case
                            || */ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                            || ex.GetInnermostException() is TimeoutException
                            || ex.GetInnermostException() is UnauthorizedAccessException
                        )
                        {
                            //retry after delay

                            if (
                                ex.GetInnermostException().HResult == unchecked((int)0x8007007B)    //The filename, directory name, or volume label syntax is incorrect
                                || ex.GetInnermostException().HResult == FolderSyncNetSource.UnsafeNativeMethods.STATUS_OBJECT_NAME_INVALID
                            )
                            {
                                break;
                            }
                            else if (tryIndex + 1 < retryCount)     //do not sleep after last try
                            {
                                if (longRunningOperationMessage != null)
                                {
                                    await ConsoleWatch.AddMessage(ConsoleColor.Yellow, "RETRYING TEMP FILE RENAME " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                                }
#if !NOASYNC
                                await Task.Delay(1000, cancellationToken);     //TODO: config file?
#else
                                cancellationToken.WaitHandle.WaitOne(1000);
#endif
                            }
                            else
                            {
                                throw ex;
                            }
                        }
                    }   //for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)

                    if (longRunningOperationMessage != null)
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.Red, "FAILED TEMP FILE RENAME AND GIVING UP " + longRunningOperationMessage, DateTime.Now, token: cancellationToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                    }

                    throw new ExternalException();

                }   //if (!createTempFileFirst)
            }   //if (writeSuccess)

        }   //public static async Task WriteAllBytesAsync()

    }
}
