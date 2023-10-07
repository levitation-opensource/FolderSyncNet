//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static Exception GetInnermostException(this Exception ex_in)
        {
            //see also https://stackoverflow.com/questions/16565834/exception-getbaseexception-returning-exception-with-not-null-innerexception

            var ex2 = ex_in;
            var ex2_aggex = ex2 as AggregateException;

            while (
                ex2_aggex != null
                && ex2_aggex.InnerExceptions.Count == 1
            )
            {
                ex2 = ex2.InnerException;
                ex2_aggex = ex2 as AggregateException;
            }

            return ex2;
        }

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
            {
                //roland: fullCheck = false
                return FolderSyncNetSource.Path.GetFullPath(path, fullCheck: false);    //GetFullPath: convert relative path to full path
            }


            //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7 and https://superuser.com/questions/1617012/support-of-the-unc-server-share-syntax-in-windows

            if (
                path.Length >= 4    //necessary to avoid exceptions in Substring()
                && path.Substring(0, 4) == @"\\?\"   //network path or path already starting with \\?\ 
            )
            {
                return path;
            }
            //https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
            else if (
                path.Length >= 4    //necessary to avoid exceptions in Substring()
                && path.Substring(0, 4) == @"\\.\"   //path already starting with \\.\ 
            )
            {
                return path;
            }
            //https://stackoverflow.com/questions/36219317/pathname-too-long-to-open
            else if (
                path.Length >= 2    //necessary to avoid exceptions in Substring()
                && path.Substring(0, 2) == @"\\"    //UNC network path
            )
            {
                return @"\\?\UNC\" + path.Substring(2);
            }
            else
            {
                //roland: fullCheck = false
                return @"\\?\" + FolderSyncNetSource.Path.GetFullPath(path, fullCheck: false);    //GetFullPath: convert relative path to full path
            }
        }   //public static string GetLongPath(string path)

        public static string GetDirPathWithTrailingSlash(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                return dirPath;

            dirPath = FolderSyncNetSource.Path.Combine(dirPath, "_");    //NB! add "_" in order to ensure that slash is appended to the end of the path
            dirPath = dirPath.Substring(0, dirPath.Length - 1);     //drop the "_" again
            return dirPath;
        }

        public static Task ContinueWithNoException(this Task task)
        {
            //https://stackoverflow.com/questions/20509158/taskcanceledexception-when-calling-task-delay-with-a-cancellationtoken-in-an-key/
            //https://blog.stephencleary.com/2013/10/continuewith-is-dangerous-too.html
            var result = task.ContinueWith  //ignore TaskCanceledException
            (
                completedTask => {
                    //this marks the exception as observed
                    return completedTask.Exception == null;
                },
                CancellationToken.None, 
                TaskContinuationOptions.ExecuteSynchronously, // | TaskContinuationOptions.OnlyOnCanceled, 
                TaskScheduler.Default
            );

            return result;
        }

        //on .NET 6 we could use .WaitAsync(new TimeSpan(timeout * 1000), childToken.Token) instead of creating a separate Task.Delay task.
        //https://github.com/dotnet/runtime/blob/933988c35c172068652162adf6f20477231f815e/src/libraries/Common/tests/System/Threading/Tasks/TaskTimeoutExtensions.cs
        //see also https://devblogs.microsoft.com/pfxteam/crafting-a-task-timeoutafter-method/
        public static async Task WaitAsync(this Task task, int timeout, CancellationToken cancellationToken)
        {
            if (task.IsCompleted)
                return;

            var taskCompletionSource = new TaskCompletionSource<bool>();
            using (new Timer
            (
                state => ((TaskCompletionSource<bool>)state).TrySetException(new TimeoutException()),
                state: taskCompletionSource, 
                dueTime: timeout, 
                period: Timeout.Infinite
            ))
            { 
                using (cancellationToken.Register
                (
                    state => ((TaskCompletionSource<bool>)state).TrySetCanceled(cancellationToken), 
                    taskCompletionSource
                ))
                {
                    await Task.WhenAny(task, taskCompletionSource.Task);
                }
            }
        }

        private static async Task WriteLongRunningOperationMessage(BoolRef longRunningTaskMessageWrittenRef, AsyncLock longRunningOperationMessageLock, CancellationTokenSource childToken, string longRunningOperationMessage, bool suppressLogFile)
        {
            try
            {
                if (!childToken.IsCancellationRequested)
                {
                    using (await longRunningOperationMessageLock.LockAsyncNoException(childToken.Token))
                    {
                        if (!childToken.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                        {
                            await ConsoleWatch.AddMessage(ConsoleColor.Gray, "Ongoing: " + longRunningOperationMessage, DateTime.Now, token: childToken.Token, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                            longRunningTaskMessageWrittenRef.Value = true;   //NB! set this flag only after the message was actually written else the task might be canceled before the console lock is taken
                        }
                    }
                }
            }
            catch (Exception)
            {
                //ignore it
            }
        }

        private static async Task RunWithTimeoutHelper(Task task, int timeout, CancellationToken parentToken, CancellationTokenSource childToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            bool hasLongRunningOperationMessage = !string.IsNullOrWhiteSpace(longRunningOperationMessage);
            var longRunningOperationMessageLock = new AsyncLock();
            var longRunningTaskMessageWrittenRef = new BoolRef();
            using 
            (
                hasLongRunningOperationMessage
                ? new Timer
                (
                    state => /*Task.WhenAny(*/Task.Run(async () => {
#if true
                        await WriteLongRunningOperationMessage
                        (
                            longRunningTaskMessageWrittenRef, 
                            longRunningOperationMessageLock, 
                            childToken, 
                            longRunningOperationMessage, 
                            suppressLogFile
                        )
                        .ContinueWithNoException();
#else
                        if (!childToken.IsCancellationRequested)
                        { 
                            using (await longRunningOperationMessageLock.LockAsync(/*childToken.Token*/))   
                            {
                                if (!childToken.IsCancellationRequested)
                                {
                                    await Program.AddMessage(ConsoleColor.Gray, longRunningOperationMessage, DateTime.Now, token: childToken.Token, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                                    longRunningTaskMessageWritten = true;   //NB! set this flag only after the message was actually written else the task might be canceled before the console lock is taken
                                }
                            }
                        }
#endif
                    })/*)*/,
                    state: null,
                    dueTime: 1000,           //TODO: config
                    period: 60 * 1000       //TODO: config
                ) 
                : null
            )
            {
                try
                {
                    if (timeout > 0)
                    {
                        await task.WaitAsync(timeout * 1000, parentToken);
                    }
                    else
                    {
                        await task;
                    }
                }
                finally
                {
                    if (!parentToken.IsCancellationRequested/* && !task.IsCompleted*/)
                    {
                        //cancel task and longRunningOperationMessage lock wait. Else the timer callback may run after the timer is disposed
                        //"Note that callbacks can occur after the Dispose() method overload has been called, because the timer queues callbacks for execution by thread pool threads."
                        //https://msdn.microsoft.com/en-us/library/system.threading.timer.aspx#Remarks
                        childToken.Cancel();
                    }
                }
            }   //using (new Timer

            if (hasLongRunningOperationMessage)
            { 
                //do not enter the if (longRunningTaskMessageWritten) check before the longRunningOperationMessageLock is free
                using (await longRunningOperationMessageLock.LockAsyncNoException(parentToken))
                {
                    //NB! The above lock will not throw because we are using LockAsyncNoException

                    if (task.IsCompleted && !task.IsCanceled)
                    {
                        if (longRunningTaskMessageWrittenRef.Value)
                        {
                            //NB! not using childToken here since it will be already canceled and would raise an exception
                            await ConsoleWatch.AddMessage(ConsoleColor.Gray, "DONE " + longRunningOperationMessage, DateTime.Now, token: parentToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                        }
                    }
                    else if (/*longRunningTaskMessageWrittenRef.Value && */parentToken.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.Red, "CANCELED " + longRunningOperationMessage, DateTime.Now, token: parentToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                    }
                    else if (task.Status == TaskStatus.Faulted)
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.Red, "FAILED " + longRunningOperationMessage, DateTime.Now, token: parentToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                    }
                    else //if (longRunningTaskMessageWrittenRef.Value)
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.Red, "TIMED OUT " + longRunningOperationMessage, DateTime.Now, token: parentToken, suppressLogFile: suppressLogFile);     //NB! suppressLogFile to avoid infinite recursion
                    }
                }
            }   //if (hasLongRunningOperationMessage)

        }   //private static async Task<bool> RunWithTimeoutHelper(Task task, int timeout, CancellationToken token, CancellationTokenSource childToken, string longRunningOperationMessage = null, bool suppressLogFile = false)

        public static async Task RunWithTimeout(Action<CancellationToken> func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (parentToken.IsCancellationRequested)
                throw new TaskCanceledException();


            //no timeout
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                func(parentToken);
                return;
            }


            //using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken);  //remove "using" since the childToken needs to stay alive until the task ends
            {
                //no need for TaskCreationOptions.LongRunning : http://blog.i3arnon.com/2015/07/02/task-run-long-running/
                var task = Task.Run(() => func(childToken.Token), childToken.Token);

                await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                //Disposal is only required for tokens that won't be cancelled, as cancellation does all of the same cleanup. - https://github.com/aspnet/AspNetKatana/issues/108
                //childToken.Cancel();  //comment-out: already called inside RunWithTimeoutHelper()

                if (task.IsCompleted && !task.IsCanceled)
                {
                    await task;     //raise any exceptions from the task
                    return;
                }
                else if (parentToken.IsCancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task RunWithTimeout(Action func, int timeout, CancellationToken parentToken)

        public static async Task RunWithTimeout(Func<CancellationToken, Task> func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (parentToken.IsCancellationRequested)
                throw new TaskCanceledException();


            //no timeout
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                await func(parentToken);
                return;
            }


            //using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken);  //remove "using" since the childToken needs to stay alive until the task ends
            {
                //no need for TaskCreationOptions.LongRunning : http://blog.i3arnon.com/2015/07/02/task-run-long-running/
                var task = Task.Run(() => func(childToken.Token), childToken.Token);

                await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                //Disposal is only required for tokens that won't be cancelled, as cancellation does all of the same cleanup. - https://github.com/aspnet/AspNetKatana/issues/108
                //childToken.Cancel();  //comment-out: already called inside RunWithTimeoutHelper()

                if (task.IsCompleted && !task.IsCanceled)
                {
                    await task;     //raise any exceptions from the task
                    return;
                }
                else if (parentToken.IsCancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task RunWithTimeout(Action func, int timeout, CancellationToken parentToken)

        public static async Task<T> RunWithTimeout<T>(Func<CancellationToken, T> func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (parentToken.IsCancellationRequested)
                throw new TaskCanceledException();


            //no timeout
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                var result = func(parentToken);
                return result;
            }


            //using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken);  //remove "using" since the childToken needs to stay alive until the task ends
            {
                //no need for TaskCreationOptions.LongRunning : http://blog.i3arnon.com/2015/07/02/task-run-long-running/
                var task = Task.Run(() => func(parentToken), childToken.Token);

                await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                //Disposal is only required for tokens that won't be cancelled, as cancellation does all of the same cleanup. - https://github.com/aspnet/AspNetKatana/issues/108
                //childToken.Cancel();  //comment-out: already called inside RunWithTimeoutHelper()

                if (task.IsCompleted && !task.IsCanceled)
                {
                    var result = await task;     //raise any exceptions from the task
                    return result;
                }
                else if (parentToken.IsCancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task<T> RunWithTimeout<T>(Func<T> func, int timeout, CancellationToken parentToken)

        public static async Task<T> RunWithTimeout<T>(Func<CancellationToken, Task<T>> func, int timeout, CancellationToken parentToken, string longRunningOperationMessage = null, bool suppressLogFile = false)
        {
            if (parentToken.IsCancellationRequested)
                throw new TaskCanceledException();


            //no timeout
            if (timeout <= 0 && string.IsNullOrWhiteSpace(longRunningOperationMessage))
            {
                var result = await func(parentToken);
                return result;
            }


            //using (var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            var childToken = CancellationTokenSource.CreateLinkedTokenSource(parentToken);  //remove "using" since the childToken needs to stay alive until the task ends
            {
                //no need for TaskCreationOptions.LongRunning : http://blog.i3arnon.com/2015/07/02/task-run-long-running/
                var task = Task.Run(() => func(childToken.Token), childToken.Token);

                await RunWithTimeoutHelper(task, timeout, parentToken, childToken, longRunningOperationMessage, suppressLogFile);

                //Disposal is only required for tokens that won't be cancelled, as cancellation does all of the same cleanup. - https://github.com/aspnet/AspNetKatana/issues/108
                //childToken.Cancel();  //comment-out: already called inside RunWithTimeoutHelper()

                if (task.IsCompleted && !task.IsCanceled)
                {
                    var result = await task;     //raise any exceptions from the task
                    return result;
                }
                else if (parentToken.IsCancellationRequested)
                    throw new TaskCanceledException(task);
                else
                    throw new TimeoutException("Timed out");
            }

        }   //public static async Task<T> RunWithTimeout<T>(Func<T> func, int timeout, CancellationToken parentToken)

        public static async Task FSOperation(Action<CancellationToken> func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
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
            catch (TaskCanceledException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Cancelled filesystem operation on " + path, ex);
            }
        }

        public static async Task FSOperation(Func<CancellationToken, Task> func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
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
            catch (TaskCanceledException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Cancelled filesystem operation on " + path, ex);
            }
        }

        public static async Task<T> FSOperation<T>(Func<CancellationToken, T> func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
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
            catch (TaskCanceledException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Cancelled filesystem operation on " + path, ex);
            }
        }

        public static async Task<T> FSOperation<T>(Func<CancellationToken, Task<T>> func, string path, CancellationToken token, int? timeout = null, bool suppressLogFile = false, bool suppressLongRunningOperationMessage = false)
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
            catch (TaskCanceledException ex)
            {
                //Console.WriteLine("Timed out filesystem operation on " + path);
                //throw;
                throw new AggregateException("Cancelled filesystem operation on " + path, ex);
            }
        }

        public static async Task<T[]> DirListOperation<T>(Func<CancellationToken, T[]> func, string path, int retryCount, CancellationToken token, int? timeout = null, bool suppressLongRunningOperationMessage = false)
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
                catch (TaskCanceledException ex)
                {
                    //Console.WriteLine("Timed out filesystem operation on " + path);
                    //throw;
                    throw new AggregateException("Cancelled filesystem operation on " + path, ex);
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

        //https://stackoverflow.com/questions/7265315/replace-multiple-characters-in-a-c-sharp-string
        public static string Replace(string str, char[] search, string replacement)
        {
            var temp = str.Split(search, StringSplitOptions.None);
            return String.Join(replacement, temp);
        }

        /// <summary>
        /// NB! If you use this method then you need to manually check whether cancellation was requested, since the lock will seemingly "enter", though it actually only canceled the wait
        /// </summary>
        //[DebuggerHidden]
        //[DebuggerStepThrough]
        public static async Task<IDisposable> LockAsyncNoException(this Nito.AsyncEx.AsyncLock lockObj, CancellationToken cancellationToken)
        {
            try
            {
                var lockHandle = await lockObj.LockAsync(cancellationToken);
                return lockHandle;
            }
            catch (TaskCanceledException)
            {
                //ignore it
                return Task.CompletedTask;
            }
        }
    }
}
