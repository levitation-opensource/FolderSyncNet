//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nito.AsyncEx;

namespace FolderSync
{
    internal partial class ConsoleWatch
    {
        /// <summary>
        /// The original console color
        /// </summary>
        internal static readonly ConsoleColor _consoleColor = Console.ForegroundColor;

        /// <summary>
        /// We need a static lock so it is shared by all.
        /// </summary>
        internal static readonly AsyncLock Lock = new AsyncLock();

        internal static DateTime PrevAlertTime;
        internal static string PrevAlertMessage;

        public static async Task WriteException(Exception ex_in, WatcherContext context)
        {
            var ex = ex_in;


            //if (ConsoleWatch.DoingInitialSync)  //TODO: config
            //    return;


            if (
                (
                    /*ex is TaskCanceledException
                    || */ex.GetInnermostException() is TaskCanceledException
                ) 
                && Global.CancellationToken.IsCancellationRequested
            )
            { 
                return;
            }



            ex = ex_in;     //TODO: refactor to shared function

            var message = new StringBuilder();
            message.Append(DateTime.Now);
            message.AppendLine(" Unhandled exception: ");

            message.AppendLine(ex.GetType().ToString());
            message.AppendLine(ex.Message);
            message.AppendLine("Stack Trace:");
            message.AppendLine(ex.StackTrace);

            /*
            while (ex.InnerException != null)
            {
                message.AppendLine("");
                message.Append("Inner exception: ");
                message.Append(ex.GetType().ToString());
                message.AppendLine(": ");
                message.AppendLine(ex.InnerException.Message);
                message.AppendLine("Inner exception stacktrace: ");
                message.AppendLine(ex.InnerException.StackTrace);

                ex = ex.InnerException;     //loop
            }
            */

            message.AppendLine("");


            using (await ConsoleWatch.Lock.LockAsyncNoException(context.Token))
            {
                if (context.Token.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                    return;


                await FileExtensions.AppendAllTextAsync
                (
                    "UnhandledExceptions.log",
                    message.ToString(),
                    context.Token,
                    suppressLogFile: true,
                    timeout: 0,     //NB!
                    suppressLongRunningOperationMessage: true     //NB!
                );
            }


            /*
            //Console.WriteLine(ex.Message);
            message.Clear();     //TODO: refactor to shared function
            message.Append(ex.Message.ToString());
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                //Console.WriteLine(ex.Message);
                message.AppendLine("");
                message.Append(ex.Message);
            }
            */


            var msg = $"{context.Event?.FullName} : {message}";
            await AddMessage(ConsoleColor.Red, msg, context, showAlert: true, addTimestamp: true);



            if (ex is AggregateException aggex)
            {
                //await WriteException(aggex.InnerException, context);

                foreach (var aggexInner in aggex.InnerExceptions)
                {
                    await WriteException(aggexInner, context);
                }

                return;
            }
        }

        public static async Task WriteException(Exception ex_in)
        {
            var ex = ex_in;

            if (
                (
                    /*ex is TaskCanceledException
                    || */ex.GetInnermostException() is TaskCanceledException
                )
                && Global.CancellationToken.IsCancellationRequested
            )
            {
                return;
            }



            ex = ex_in;     //TODO: refactor to shared function

            var message = new StringBuilder();
            message.Append(DateTime.Now);
            message.AppendLine(" Unhandled exception: ");

            message.AppendLine(ex.GetType().ToString());
            message.AppendLine(ex.Message);
            message.AppendLine("Stack Trace:");
            message.AppendLine(ex.StackTrace);

            /*
            while (ex.InnerException != null)
            {
                message.AppendLine("");
                message.Append("Inner exception: ");
                message.Append(ex.GetType().ToString());
                message.AppendLine(": ");
                message.AppendLine(ex.InnerException.Message);
                message.AppendLine("Inner exception stacktrace: ");
                message.AppendLine(ex.InnerException.StackTrace);

                ex = ex.InnerException;     //loop
            }
            */

            message.AppendLine("");


            using (await ConsoleWatch.Lock.LockAsyncNoException(Global.CancellationToken.Token))
            {
                if (Global.CancellationToken.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                    return;


                await FileExtensions.AppendAllTextAsync
                (
                    "UnhandledExceptions.log",
                    message.ToString(),
                    Global.CancellationToken.Token,
                    suppressLogFile: true,
                    timeout: 0,     //NB!
                    suppressLongRunningOperationMessage: true     //NB!
                );
            }


            /*
            //Console.WriteLine(ex.Message);
            message.Clear();     //TODO: refactor to shared function
            message.Append(ex.Message.ToString());
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                //Console.WriteLine(ex.Message);
                message.AppendLine("");
                message.Append(ex.Message);
            }
            */


            var time = DateTime.Now;
            var msg = message.ToString();
            await AddMessage(ConsoleColor.Red, msg, time, showAlert: true, addTimestamp: true);



            if (ex is AggregateException aggex)
            {
                //await WriteException(aggex.InnerException);

                foreach (var aggexInner in aggex.InnerExceptions)
                {
                    await WriteException(aggexInner);
                }

                return;
            }

        }   //private static async Task WriteException(Exception ex_in)

        public static async Task AddMessage(ConsoleColor color, string message, WatcherContext context, bool showAlert = false, bool addTimestamp = false)
        {
            if (addTimestamp || Global.AddTimestampToNormalLogEntries)
            {
                var time = context.Time.ToLocalTime();
                message = $"[{time:yyyy.MM.dd HH:mm:ss.ffff}] : {message}";
            }


            //await Task.Run(() =>
            {
                using (await ConsoleWatch.Lock.LockAsyncNoException(context.Token))
                {
                    if (context.Token.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                        return;


                    if (Global.LogToFile)
                    {
                        await FileExtensions.AppendAllTextAsync
                        (
                            "Console.log",
                            message,
                            context.Token,
                            suppressLogFile: true,
                            timeout: 0,     //NB!
                            suppressLongRunningOperationMessage: true     //NB! 
                        );
                    }


                    try
                    {
                        Console.ForegroundColor = color;
                        Console.WriteLine(message);
                                                
                        if (
                            showAlert
                            && Global.ShowErrorAlerts
                        )
                        {
                            if (PrevAlertTime != context.Time || PrevAlertMessage != message)
                            {
                                PrevAlertTime = context.Time;
                                PrevAlertMessage = message;

                                MessageBox.Show(message, "FolderSync");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        Console.ForegroundColor = _consoleColor;
                    }
                }
            }//)
            //.WaitAsync(context.Token);

        }   //public static async Task AddMessage(ConsoleColor color, string message, Context context, bool showAlert = false, bool addTimestamp = false)

        public static async Task AddMessage(ConsoleColor color, string message, DateTime time, bool showAlert = false, bool addTimestamp = false, CancellationToken? token = null, bool suppressLogFile = false)
        {
            if (addTimestamp || Global.AddTimestampToNormalLogEntries)
            {
                message = $"[{time:yyyy.MM.dd HH:mm:ss.ffff}] : {message}";
            }


            //await Task.Run(() => 
            {
                var cancellationToken = token ?? Global.CancellationToken.Token;
                using (await ConsoleWatch.Lock.LockAsyncNoException(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)  //the above lock will not throw because we are using LockAsyncNoException
                        return;


                    if (Global.LogToFile && !suppressLogFile)
                    {
                        await FileExtensions.AppendAllTextAsync
                        (
                            "Console.log",
                            message,
                            token ?? Global.CancellationToken.Token,
                            suppressLogFile: true,
                            timeout: 0,     //NB!
                            suppressLongRunningOperationMessage: true     //NB!
                        );
                    }


                    try
                    {
                        Console.ForegroundColor = color;
                        Console.WriteLine(message);

                        if (
                            showAlert
                            && Global.ShowErrorAlerts
                        )
                        {
                            if (ConsoleWatch.PrevAlertTime != time || ConsoleWatch.PrevAlertMessage != message)
                            {
                                MessageBox.Show(message, Assembly.GetExecutingAssembly().GetName().Name);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        Console.ForegroundColor = ConsoleWatch._consoleColor;
                    }
                }
            }//)
            //.WaitAsync(Global.CancellationToken.Token);
        }
    }
}
