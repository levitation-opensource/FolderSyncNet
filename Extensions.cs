//
// Copyright (c) Roland Pihlakas 2019 - 2022
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Nito.AspNetBackgroundTasks;
using Nito.AsyncEx;

namespace FolderSync
{
    public static class Extensions
    {
        public static long? CheckDiskSpace(string path)
        {
            long? freeBytes = null;

            try     //NB! on some drives (for example, RAM drives, GetDiskFreeSpaceEx does not work
            {
                //NB! DriveInfo works on paths well in Linux    //TODO: what about Mac?
                var drive = new DriveInfo(path);
                freeBytes = drive.AvailableFreeSpace;
            }
            catch (ArgumentException)
            {
                if (ConfigParser.IsWindows)
                {
                    long freeBytesOut;
                    if (WindowsDllImport.GetDiskFreeSpaceEx(path, out freeBytesOut, out var _, out var __))
                        freeBytes = freeBytesOut;
                }
            }

            return freeBytes;
        }

        public static string GetLongPath(string path)
        {
            if (!ConfigParser.IsWindows)
                return Path.GetFullPath(path);    //GetFullPath: convert relative path to full path


            //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7 and https://superuser.com/questions/1617012/support-of-the-unc-server-share-syntax-in-windows

            if (
                path.Length >= 2    //necessary to avoid exceptions in Substring()
                && path.Substring(0, 2) == @"\\"   //network path or path already starting with \\?\ 
            )
            {
                return path;
            }
            else
            {
                return @"\\?\" + Path.GetFullPath(path);    //GetFullPath: convert relative path to full path
            }
        }

        public static string GetDirPathWithTrailingSlash(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                return dirPath;

            dirPath = Path.Combine(dirPath, ".");    //NB! add "." in order to ensure that slash is appended to the end of the path
            dirPath = dirPath.Substring(0, dirPath.Length - 1);     //drop the "." again
            return dirPath;
        }

        public static Task ContinueWithNoException(this Task task)
        {
            //https://stackoverflow.com/questions/20509158/taskcanceledexception-when-calling-task-delay-with-a-cancellationtoken-in-an-key/
            //https://blog.stephencleary.com/2013/10/continuewith-is-dangerous-too.html
            var result = task.ContinueWith
            (
                t => { /*ignore TaskCanceledException*/ }, 
                CancellationToken.None, 
                TaskContinuationOptions.ExecuteSynchronously, 
                TaskScheduler.Default
            );

            return result;
        }

        private static async Task<bool> RunWithTimeoutHelper(Task task, int timeout, CancellationToken token, CancellationTokenSource childToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            Task messageTask = null;
            bool longRunningTaskMessageWritten = false;
            if (!string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                messageTask = Task.Run
                (
                    async () =>
                    {
                        await Task.Delay(1000, childToken.Token).ContinueWithNoException();   //TODO: config

                        while (!childToken.IsCancellationRequested)
                        {
                            await Program.AddMessage(ConsoleColor.Gray, longRunningOperationMessage, DateTime.Now, token: childToken.Token, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                            longRunningTaskMessageWritten = true;   //NB! set this flag only after the message was actually written else the task might be canceled before the console lock is taken

                            if (!childToken.IsCancellationRequested)
                                await Task.Delay(60 * 1000, childToken.Token).ContinueWithNoException();   //TODO: config
                        }
                    },
                    childToken.Token
                )
                .ContinueWithNoException();
            }
            
            if (timeout > 0)
            {
                var timeoutTask = Task.Delay(timeout * 1000, childToken.Token).ContinueWithNoException();
                await Task.WhenAny(task, timeoutTask);
            }
            else
            {
                await task;
            }


            bool cancellationRequested = childToken.IsCancellationRequested;

            if (!cancellationRequested)
                childToken.Cancel();    //NB! Cancel timeoutTask if task completes first, or cancel task if timeoutTask completes first. Also cancel messageTask.

            if (messageTask != null)
            {
                await messageTask;      //NB! ensure that messageTask is finished before disposing childToken

                if (longRunningTaskMessageWritten && !cancellationRequested)
                { 
                    //NB! not using childToken here since it will be already canceled and would raise an exception
                    await Program.AddMessage(ConsoleColor.Gray, "DONE " + longRunningOperationMessage, DateTime.Now, token: token, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                }
            }

            return cancellationRequested;

        }   //private static async Task<bool> RunWithTimeoutHelper(Task task, int timeout, CancellationToken token, CancellationTokenSource childToken, string longRunningOperationMessage = null, bool suppressLogFile = false)

        public static async Task RunWithTimeout(Action func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                func();
                return;
            }


            using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            {
                var task = Task.Run(func, childToken.Token);

                bool cancellationRequested = await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                if (task.IsCompleted)
                {
                    await task;     //raise any exceptions from the task
                    return;
                }
                else if (task.IsCanceled || cancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task RunWithTimeout(Action func, int timeout, CancellationToken parentToken)

        public static async Task<T> RunWithTimeout<T>(Func<T> func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                var result = func();
                return result;
            }


            using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            {
                var task = Task.Run(func, childToken.Token);

                bool cancellationRequested = await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                if (task.IsCompleted)
                {
                    var result = await task;     //raise any exceptions from the task
                    return result;
                }
                else if (task.IsCanceled || cancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task<T> RunWithTimeout<T>(Func<T> func, int timeout, CancellationToken parentToken)

        public static async Task FSOperation(Action func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
        {
            //await Task.Run(func).WaitAsync(token);
            //func();
            try
            {
                await RunWithTimeout
                (
                    func, 
                    timeout ?? Global.FSOperationTimeout, 
                    token,
                    !suppressLongRunningOperationMessage ? "Running filesystem operation on " + path : null, 
                    suppressLogFile
                );
            }
            catch (TimeoutException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Timed out filesystem operation on " + path, ex);
            }
        }

        public static async Task<T> FSOperation<T>(Func<T> func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
        {
            //var result = await Task.Run(func).WaitAsync(token);
            //var result = func();
            //return result;
            try
            {
                var result = await RunWithTimeout
                (
                    func, 
                    timeout ?? Global.FSOperationTimeout, 
                    token,
                    !suppressLongRunningOperationMessage ? "Running filesystem operation on " + path : null,
                    suppressLogFile
                );
                return result;
            }
            catch (TimeoutException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Timed out filesystem operation on " + path, ex);
            }
        }

        public static async Task<T[]> DirListOperation<T>(Func<T[]> func, string path, int retryCount, CancellationToken token, int? timeout = null, bool suppressLongRunningOperationMessage = false)
        {
            retryCount = Math.Max(0, retryCount);
            for (int tryIndex = -1; tryIndex < retryCount; tryIndex++)
            {
                //T result = await Task.Run(func).WaitAsync(token);
                //var result = func();
                T[] result;
                try
                {
                    result = await RunWithTimeout
                    (
                        func, 
                        timeout ?? Global.DirListOperationTimeout, 
                        token,
                        !suppressLongRunningOperationMessage ? "Running dirlist operation on " + path : null
                    );
                }
                catch (TimeoutException ex)
                {
                    //Console.WriteLine("Timed out dirlist operation on " + path);
                    //throw;
                    throw new AggregateException("Timed out dirlist operation on " + path, ex);
                }


                if (result.Length > 0)
                    return result;

#if DEBUG
                if (result is FileInfo[])
                {
                    bool qqq = true;    //for debugging
                }
#endif

                if (tryIndex + 1 < retryCount)     //do not sleep after last try
                {
#if !NOASYNC
                    await Task.Delay(1000, token);     //TODO: config file?
#else
                    token.WaitHandle.WaitOne(1000);
#endif
                }
            }

            return new T[0];

        }   //public static async Task<T[]> DirListOperation<T>(Func<T[]> func, string path, int retryCount, CancellationToken token)

        public static byte[] SerializeBinary<T>(this T obj, bool compress = true)
        {
            var formatter = new BinaryFormatter();
            formatter.Context = new StreamingContext(StreamingContextStates.Persistence);

            using (var mstream = new MemoryStream())
            {
                if (compress)
                {
                    using (var gzStream = new GZipStream(mstream, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        formatter.Serialize(gzStream, obj);
                    }
                }
                else
                {
                    formatter.Serialize(mstream, obj);
                }

                byte[] bytes = new byte[mstream.Length];
                mstream.Position = 0;   //NB! reset stream position
                mstream.Read(bytes, 0, (int)mstream.Length);
                return bytes;
            }
        }

        public static T DeserializeBinary<T>(this byte[] bytes, bool? decompress = null)
        {
            var formatter = new BinaryFormatter();
            formatter.Context = new StreamingContext(StreamingContextStates.Persistence);

            using (var mstream = new MemoryStream(bytes))
            {
                mstream.Position = 0;

                if (decompress == null)     //auto detect
                {
                    if (mstream.ReadByte() == 0x1F && mstream.ReadByte() == 0x8B)
                        decompress = true;

                    mstream.Position = 0;   //reset stream position
                }

                if (decompress == true)
                {
                    using (var gzStream = new GZipStream(mstream, CompressionMode.Decompress, leaveOpen: true))
                    {
                        object result = formatter.Deserialize(gzStream);
                        if (result is T)
                            return (T)result;
                        return default(T);
                    }
                }
                else
                {
                    object result = formatter.Deserialize(mstream);
                    if (result is T)
                        return (T)result;
                    return default(T);
                }
            }
        }
    }
}
