//
// Copyright (c) Roland Pihlakas 2019 - 2022
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dasync.Collections;
using Microsoft.Extensions.Configuration;
using myoddweb.directorywatcher;
using myoddweb.directorywatcher.interfaces;
using Nito.AspNetBackgroundTasks;
using Nito.AsyncEx;
using NReco.Text;

namespace FolderSync
{
    internal partial class Program
    {
        //let null char mark start and end of a filename
        //https://stackoverflow.com/questions/54205087/how-can-i-create-a-file-with-null-bytes-in-the-filename
        //https://stackoverflow.com/questions/1976007/what-characters-are-forbidden-in-windows-and-linux-directory-names
        //https://serverfault.com/questions/242110/which-common-characters-are-illegal-in-unix-and-windows-filesystems
        public static readonly string NullChar = new string(new char[]{ (char)0 });

        public static readonly string DirectorySeparatorChar = new string(new char[] { Path.DirectorySeparatorChar });


        private static byte[] GetHash(string inputString)
        {
#pragma warning disable SCS0006     //Warning	SCS0006	Weak hashing function
            HashAlgorithm algorithm = MD5.Create();
#pragma warning restore SCS0006
            return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

        public static string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }

        private static void Main()
        {
            ReadConfig();


            var pathHashes = "";
            //TODO!!! allow multiple instances with differet settings
            pathHashes += "_" + GetHashString(Global.SrcPath);
            pathHashes += "_" + GetHashString(Global.MirrorDestPath ?? "");
            pathHashes += "_" + GetHashString(Global.HistoryDestPath ?? "");

            //NB! prevent multiple instances from starting on same directories
            using (var mutex = new Mutex(false, "Global\\" + Assembly.GetExecutingAssembly().GetName().Name + "_" + pathHashes))
            {
                try
                {
                    if (!mutex.WaitOne(0, false))
                    {
                        Console.WriteLine("Instance already running");
                        return;
                    }
                }
                catch (AbandonedMutexException)    //The wait completed due to an abandoned mutex. - happens when the other process was killed
                {
                    //ignore it
                }

                MainTask().Wait();
            }
        }

        private static async Task MainTask()
        {
            try
            {
                //Console.WriteLine(Environment.Is64BitProcess ? "x64 version" : "x86 version");
                Console.WriteLine("Press Ctrl+C to stop the monitors.");


                if (Global.UseIdlePriority)
                {
                    try
                    { 
                        var CurrentProcess = Process.GetCurrentProcess();
                        CurrentProcess.PriorityClass = ProcessPriorityClass.Idle; 
                        CurrentProcess.PriorityBoostEnabled = false;

                        if (ConfigParser.IsWindows)
                        { 
                            WindowsDllImport.SetIOPriority(CurrentProcess.Handle, WindowsDllImport.PROCESSIOPRIORITY.PROCESSIOPRIORITY_VERY_LOW);
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unable to set idle priority.");
                    }
                }

                if (Global.UseBackgroundMode)
                {
                    try
                    {
                        var CurrentProcess = Process.GetCurrentProcess();

                        if (ConfigParser.IsWindows)
                        {
                            WindowsDllImport.SetPriorityClass(CurrentProcess.Handle, WindowsDllImport.PROCESS_MODE_BACKGROUND_BEGIN);
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unable to set background mode.");
                    }
                }

                if (Global.Affinity.Count > 0)
                {
                    try
                    {
                        var CurrentProcess = Process.GetCurrentProcess();

                        long affinityMask = 0;
                        foreach (var affinityEntry in Global.Affinity)
                        {
                            if (affinityEntry < 0 || affinityEntry > 63)
                                throw new ArgumentException("Affinity");

                            affinityMask |= (long)1 << (int)affinityEntry;
                        }

                        CurrentProcess.ProcessorAffinity = new IntPtr(affinityMask);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unable to set affinity.");
                    }
                }


                ThreadPool.SetMinThreads(32, 4096);   //TODO: config
                //TODO: MaxThreads setting


                //start the monitor.
                using (var watch = new Watcher())
                {
                    watch.Add(new Request(Extensions.GetLongPath(Global.SrcPath), recursive: true));

                    if (Global.BidirectionalMirror)
                    {
                        watch.Add(new Request(Extensions.GetLongPath(Global.MirrorDestPath), recursive: true));
                    }


                    //prepare the console watcher so we can output pretty messages.
                    var consoleWatch = new ConsoleWatch(watch);


                    //start watching
                    //NB! start watching before synchronisation
                    watch.Start();


                    var initialSyncMessageContext = new WatcherContext
                    (
                        eventObj: null,
                        token: Global.CancellationToken.Token,
                        forHistory: false,   //unused here
                        isSrcPath: false,   //unused here
                        isInitialScan: true,
                        fileInfoRefreshedBoolRef: null
                    );


                    BackgroundTaskManager.Run(async () =>
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.White, "Doing initial synchronisation...", initialSyncMessageContext);

                        await ScanFolders(initialSyncMessageContext: initialSyncMessageContext);

                        BackgroundTaskManager.Run(async () =>
                        {
                            await InitialSyncCountdownEvent.WaitAsync(Global.CancellationToken.Token);

                            //if (!Global.CancellationToken.IsCancellationRequested)
                                await ConsoleWatch.AddMessage(ConsoleColor.White, "Done initial synchronisation...", initialSyncMessageContext);
                        });


                        if (Global.UsePolling)
                        {
                            while (!Global.CancellationToken.IsCancellationRequested)
                            { 
#if !NOASYNC
                                await Task.Delay(Global.PollingDelay * 1000, Global.CancellationToken.Token);
#else
                                Global.CancellationToken.Token.WaitHandle.WaitOne(Global.PollingDelay * 1000);
#endif

                                await ScanFolders(initialSyncMessageContext: null);
                            }
                        
                        }   //if (Global.UsePolling)

                    });     //BackgroundTaskManager.Run(async () =>


                    //listen for the Ctrl+C 
                    await WaitForCtrlC();

                    Console.WriteLine("Stopping...");

                    //stop everything.
                    watch.Stop();

                    GC.KeepAlive(consoleWatch);
                }
            }
            catch (Exception ex)
            {
                await ConsoleWatch.WriteException(ex);
            }
            finally
            {
                Console.WriteLine("Exiting...");

                Environment.Exit(0);
            }
        }   //private static async Task MainTask()

        private static Task WaitForCtrlC()
        {
            var exitEvent = new AsyncManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                Global.CancellationToken.Cancel();
                e.Cancel = true;
                Console.WriteLine("Stop detected.");
                exitEvent.Set();
            };
            return exitEvent.WaitAsync();
        }
    }

    internal class BoolRef
    {
        public bool Value;
    }

    internal partial class ConsoleWatch
    {
        private static readonly ConcurrentDictionary<string, DateTime> BidirectionalSynchroniserSavedFileDates = new ConcurrentDictionary<string, DateTime>();

        
        public static bool IsSrcPath(string fullNameInvariant)
        {
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.SrcPath));
        }

        public static bool IsMirrorDestPath(string fullNameInvariant)
        {
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.MirrorDestPath));
        }

        public static bool IsHistoryDestPath(string fullNameInvariant)
        {
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.HistoryDestPath));
        }

        public static string GetNonFullName(string fullName)
        {
            fullName = Extensions.GetLongPath(fullName);
            var fullNameInvariant = fullName.ToUpperInvariantOnWindows();

            if (IsHistoryDestPath(fullNameInvariant))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.HistoryDestPath).Length);
            }
            else if (IsMirrorDestPath(fullNameInvariant))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.MirrorDestPath).Length);
            }
            else if (IsSrcPath(fullNameInvariant))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.SrcPath).Length);
            }
            else
            {
                throw new ArgumentException("Unexpected path provided to GetNonFullName()");
            }
        }

        public static string GetCacheDirName(string dirFullName, bool forHistory)
        {
            var fullNameInvariant = dirFullName.ToUpperInvariantOnWindows();
            var nonFullNameFolder = GetNonFullName(dirFullName);

            if (forHistory)
            {
                if (IsHistoryDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "History", nonFullNameFolder);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetCacheDirName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "Mirror", nonFullNameFolder);
                }
#if false
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "DestMirror", nonFullNameFolder);
                }
#endif
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetCacheDirName()");
                }
            }
        }   //public static string GetCacheDirName(string dirFullName, bool forHistory)

        public static string GetOtherDirName(string dirFullName, bool forHistory)
        {
            var fullNameInvariant = dirFullName.ToUpperInvariantOnWindows();
            var nonFullNameFolder = GetNonFullName(dirFullName);

            if (forHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.HistoryDestPath, nonFullNameFolder);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherDirName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.SrcPath, nonFullNameFolder);
                }
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.MirrorDestPath, nonFullNameFolder);
                }
                else 
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherDirName()");
                }
            }
        }   //public static string GetOtherDirName(string dirFullName, bool forHistory)

        public static async Task<string> GetOtherFullName(FileInfo fileInfo, bool forHistory)
        {
            var fullNameInvariant = fileInfo.FullName.ToUpperInvariantOnWindows();
            var nonFullName = GetNonFullName(fileInfo.FullName);

            if (forHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    var srcFileDate = fileInfo.LastWriteTimeUtc; //await GetFileTime(fileInfo);    //NB! here read the current file time, not file time at the event

                    if (Global.HistoryVersionFormat == "PREFIX_TIMESTAMP")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileName = Path.GetFileName(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{srcFileDate.Ticks}{Global.HistoryVersionSeparator}{fileName}");
                    }
                    else if (Global.HistoryVersionFormat == "TIMESTAMP_BEFORE_EXT")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(nonFullName);
                        var fileExtension = Path.GetExtension(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{fileNameWithoutExtension}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}{fileExtension}");
                    }
                    else if (Global.HistoryVersionFormat == "SUFIX_TIMESTAMP")
                    {
                        return Path.Combine(Global.HistoryDestPath, $"{nonFullName}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}");
                    }
                    else
                    {
                        throw new ArgumentException("Unexpected HistoryFileNameFormat configuration");
                    }
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.SrcPath, nonFullName);
                }
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.MirrorDestPath, nonFullName);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
        }   //public static async Task<string> GetOtherFullName(FileInfo fileInfo, bool forHistory)

        public static async Task<string> GetOtherFullName(WatcherContext context)
        {
            var fullNameInvariant = context.Event.FullName.ToUpperInvariantOnWindows();
            var nonFullName = GetNonFullName(context.Event.FullName);

            if (context.ForHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    var srcFileDate = await GetFileTime(context);    //NB! here read the current file time, not file time at the event

                    if (Global.HistoryVersionFormat == "PREFIX_TIMESTAMP")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileName = Path.GetFileName(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{srcFileDate.Ticks}{Global.HistoryVersionSeparator}{fileName}");
                    }
                    else if (Global.HistoryVersionFormat == "TIMESTAMP_BEFORE_EXT")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(nonFullName);
                        var fileExtension = Path.GetExtension(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{fileNameWithoutExtension}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}{fileExtension}");
                    }
                    else if (Global.HistoryVersionFormat == "SUFIX_TIMESTAMP")
                    {
                        return Path.Combine(Global.HistoryDestPath, $"{nonFullName}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}");
                    }
                    else
                    {
                        throw new ArgumentException("Unexpected HistoryFileNameFormat configuration");
                    }
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.SrcPath, nonFullName);
                }
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.MirrorDestPath, nonFullName);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
        }   //public static async Task<string> GetOtherFullName(Context context)

        public static async Task DeleteFile(FileInfoRef otherFileInfo, string otherFullName, WatcherContext context)
        {
            try
            {
                otherFullName = Extensions.GetLongPath(otherFullName);

                while (true)
                {
                    context.Token.ThrowIfCancellationRequested();

                    try
                    {
                        var backupFileInfo = new FileInfoRef(null, context.Token);
                        if (await GetFileExists(backupFileInfo, otherFullName + "~", isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory))
                        {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                            await Extensions.FSOperation
                            (
                                () => File.Delete(otherFullName + "~"),
                                otherFullName + "~",
                                context.Token
                            );
#pragma warning restore SEC0116
                        }

                        //fileInfo?.Refresh();
                        if (await GetFileExists(otherFileInfo, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory))
                        {
                            await Extensions.FSOperation
                            (
                                () => File.Move(otherFullName, otherFullName + "~"),
                                otherFullName + " " + Path.PathSeparator + " " + otherFullName + "~",
                                context.Token
                            );
                        }

                        return;
                    }
                    catch (IOException)
                    {
                        //retry after delay
#if !NOASYNC
                        await Task.Delay(1000, context.Token);     //TODO: config file?
#else
                        context.Token.WaitHandle.WaitOne(1000);
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                await WriteException(ex, context);
            }
        }   //public static async Task DeleteFile(string fullName, Context context)

        public static DateTime GetBidirectionalSynchroniserSaveDate(string fullName)
        {
            DateTime converterSaveDate;
            if (!BidirectionalSynchroniserSavedFileDates.TryGetValue(fullName, out converterSaveDate))
            {
                converterSaveDate = DateTime.MinValue;
            }

            return converterSaveDate;
        }

        public static async Task<bool> NeedsUpdate(WatcherContext context)
        {
            if (
                (!Global.EnableMirror && !context.ForHistory)
                || (!Global.EnableHistory && context.ForHistory)
            )
            {
                return false;
            }

            //compare file date only when not doing initial sync OR when file content comparison is turned off
            if (context.IsInitialScan && !Global.DoNotCompareFileContent)
            {
                return true;
            }
            else    
            {
                if (context.FileInfo.Length != null)   //a file from directory scan
                {
                    //var fileLength = await GetFileSize(context);

                    var fileLength = context.FileInfo.Length.Value;      //NB! this info might be stale, but lets ignore that issue here
                    long maxFileSize = Math.Min(FileExtensions.MaxByteArraySize, Global.MaxFileSizeMB * (1024 * 1024));
                    if (maxFileSize > 0 && fileLength > maxFileSize)
                    {
                        await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : fileLength > maxFileSize : {fileLength} > {maxFileSize}", context);

                        return false;
                    } 
                }

                var synchroniserSaveDate = (Global.BidirectionalMirror && !context.ForHistory) ? GetBidirectionalSynchroniserSaveDate(context.Event.FullName) : DateTime.MinValue;
                var fileTime = context.Event.FileSystemInfo.LastWriteTimeUtc; //GetFileTime(fullName);

                if (
                    !Global.BidirectionalMirror   //no need to debounce BIDIRECTIONAL file save events when bidirectional save is disabled 
                    || context.ForHistory
                    || fileTime > synchroniserSaveDate.AddSeconds(3)     //NB! ignore if the file changed during 3 seconds after bidirectional save   //TODO!! config
                )
                {
                    var otherFullName = await GetOtherFullName(context);

                    bool considerDateAsNewer = false;
                    if (Global.DoNotCompareFileDate)
                    { 
                        considerDateAsNewer = true;
                    }
                    else
                    {
                        var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                        var otherFileTime = await GetFileTime(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                        context.OtherFileInfo = otherFileInfoRef.Value;

                        if (otherFileTime == DateTime.MinValue)     //file not found
                            return true;

                        if (fileTime > otherFileTime)     //NB!
                            considerDateAsNewer = true;
                    }

                    if (considerDateAsNewer)
                    { 
                        if (Global.DoNotCompareFileContent)
                        {
                            if (Global.DoNotCompareFileSize)
                            {
                                //if date check was done above then there is no need to do file existence check here
                                if (Global.DoNotCompareFileDate)
                                {
                                    var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                                    bool otherFileExists = await GetFileExists(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                                    context.OtherFileInfo = otherFileInfoRef.Value;

                                    if (!otherFileExists)
                                    {
                                        return true;
                                    }
                                    else
                                    {
                                        return false; 
                                    }
                                }
                                else
                                {
                                    return false;   //if the other file does not exist then the function returns true in date check
                                }
                            }
                            else    //if (Global.DoNotCompareFileSize)
                            {
                                var fileLength = await GetFileSize(context);

                                var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                                var otherFileLength = await GetFileSize(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                                context.OtherFileInfo = otherFileInfoRef.Value;

                                if (fileLength != otherFileLength)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false; 
                                }
                            }    //if (Global.DoNotCompareFileSize)
                        }
                        else    //if (Global.DoNotCompareFileContent)
                        { 
                            return true;
                        }
                    }   //if (fileTime > otherFileTime)
                }

                return false;

            }   //if (context.IsInitialScan && !Global.DoNotCompareFileContent)
        }

        private static bool IsWatchedFile(string fullName, bool forHistory, bool isSrcPath)
        {
            if (
                (!Global.EnableMirror && !forHistory)
                || (!Global.EnableHistory && forHistory)
                || (forHistory && !isSrcPath)
            )
            {
                return false;
            }

            var fullNameInvariant = fullName.ToUpperInvariantOnWindows();

            if (
                !forHistory
                &&
                (
                    Global.MirrorWatchedExtension.Any(x => fullNameInvariant.EndsWith("." + x))
                    || Global.MirrorWatchedExtension.Contains("*")
                )
                &&
                Global.MirrorExcludedExtensions.All(x =>  //TODO: optimise

                    !fullNameInvariant.EndsWith("." + x)
                    &&
                    (   //handle exclusion patterns in the forms like *xyz
                        !x.StartsWith("*")
                        || fullNameInvariant.Length < x.Length - 1
                        || !fullNameInvariant.EndsWith(/*"." + */x.Substring(1))    //NB! the existence of dot is not verified in this case     //TODO: use Regex
                    )
                )
            )
            {
                var nonFullNameInvariantWithLeadingSlash = Program.DirectorySeparatorChar + GetNonFullName(fullNameInvariant);

                if (
                    //Global.MirrorIgnorePathsStartingWith.Any(x => nonFullNameInvariantWithLeadingSlash.StartsWith(x))
                    //|| Global.MirrorIgnorePathsContaining.Any(x => nonFullNameInvariantWithLeadingSlash.Contains(x))
                    //|| Global.MirrorIgnorePathsEndingWith.Any(x => nonFullNameInvariantWithLeadingSlash.EndsWith(x))
                    Global.MirrorIgnorePathsContainingACHasAny  //needed to avoid exceptions
                    && Global.MirrorIgnorePathsContainingAC.ParseText(Program.NullChar + nonFullNameInvariantWithLeadingSlash + Program.NullChar).Any()
                )
                {
                    return false;
                }

                return true;
            }
            else if (
                forHistory
                &&
                (
                    Global.HistoryWatchedExtension.Any(x => fullNameInvariant.EndsWith("." + x))
                    || Global.HistoryWatchedExtension.Contains("*")
                )
                &&
                Global.HistoryExcludedExtensions.All(x =>  //TODO: optimise

                    !fullNameInvariant.EndsWith("." + x)
                    &&
                    (   //handle exclusion patterns in the forms like *xyz
                        !x.StartsWith("*")
                        || fullNameInvariant.Length < x.Length - 1
                        || !fullNameInvariant.EndsWith(/*"." + */x.Substring(1))    //NB! the existence of dot is not verified in this case     //TODO: use Regex
                    )
                )
            )
            {
                var nonFullNameInvariantWithLeadingSlash = Program.DirectorySeparatorChar + GetNonFullName(fullNameInvariant);

                if (
                    //Global.HistoryIgnorePathsStartingWith.Any(x => nonFullNameInvariantWithLeadingSlash.StartsWith(x))
                    //|| Global.HistoryIgnorePathsContaining.Any(x => nonFullNameInvariantWithLeadingSlash.Contains(x))
                    //|| Global.HistoryIgnorePathsEndingWith.Any(x => nonFullNameInvariantWithLeadingSlash.EndsWith(x))
                    Global.HistoryIgnorePathsContainingACHasAny  //needed to avoid exceptions
                    && Global.HistoryIgnorePathsContainingAC.ParseText(Program.NullChar + nonFullNameInvariantWithLeadingSlash + Program.NullChar).Any()
                )
                {
                    return false;
                }

                return true;
            }

            return false;

        }   //private bool IsWatchedFile(string fullName, bool forHistory)

        public static async Task SaveFileModifications(byte[] fileData, WatcherContext context)
        {
            var otherFullName = await GetOtherFullName(context);


            var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
            var longOtherFullName = Extensions.GetLongPath(otherFullName);

            long maxFileSize = Math.Min(FileExtensions.MaxByteArraySize, Global.MaxFileSizeMB * (1024 * 1024));

            //NB! detect whether the file actually changed
            var otherFileDataTuple = 
                !Global.DoNotCompareFileContent 
                    && 
                    (await GetFileExists
                    (
                        otherFileInfoRef, 
                        otherFullName, 
                        isSrcFile: !context.IsSrcPath, 
                        forHistory: context.ForHistory
                    ))
                ? await FileExtensions.ReadAllBytesAsync    //TODO: optimisation: no need to read the bytes in case the file lengths are different
                        (
                            longOtherFullName,
                            /*allowVSS: */Global.AllowVSS,
                            context.Token,
                            maxFileSize,
                            readBufferKB: Global.ReadBufferKB,
                            bufferReadDelayMs: Global.BufferReadDelayMs
                        )
                : null;

            context.OtherFileInfo = otherFileInfoRef.Value;

            if (
                (
                    !Global.DoNotCompareFileContent
                    &&
                    (
                        (otherFileDataTuple?.Item1?.Length ?? -1) != fileData.Length
                        || !FileExtensions.BinaryEqual(otherFileDataTuple.Item1, fileData)
                    )
                )
                ||
                Global.DoNotCompareFileContent
            )
            {
                var minDiskFreeSpace = context.ForHistory ? Global.HistoryDestPathMinFreeSpace : (context.IsSrcPath ? Global.MirrorDestPathMinFreeSpace : Global.SrcPathMinFreeSpace);
                var actualFreeSpace = minDiskFreeSpace > 0 ? Extensions.CheckDiskSpace(otherFullName) : 0;
                if (minDiskFreeSpace > actualFreeSpace - fileData.Length)
                {
                    await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : minDiskFreeSpace > actualFreeSpace : {minDiskFreeSpace} > {actualFreeSpace}", context);

                    return;
                }


                //if (!context.ForHistory && context.OtherFileInfo.Exists != false)    //assume that in case of history files there is no point in making a back copy of the history file even if it exists at the destination
                //    await DeleteFile(otherFileInfoRef, otherFullName, context);


                var otherDirName = Extensions.GetDirPathWithTrailingSlash(Path.GetDirectoryName(otherFullName));
                var longOtherDirName = Extensions.GetLongPath(otherDirName);
                bool newFolderCreated = false;

                if (
                    !Global.CacheDestAndHistoryFolders
                    || !Global.CreatedFoldersCache.ContainsKey(longOtherDirName.ToUpperInvariantOnWindows())
                )
                { 
                    if (!await Extensions.FSOperation
                    (
                        () => Directory.Exists(longOtherDirName),
                        longOtherDirName,
                        context.Token
                    ))
                    {
                        newFolderCreated = true;

                        if (    
                            Global.CacheDestAndHistoryFolders
                            && Global.PersistentCacheDestAndHistoryFolders
                            && context.IsSrcPath
                            && (!Global.BidirectionalMirror || context.ForHistory)
                        )
                        {
                            //NB! create file cache before creating the folder so that any files that are concurrently added to the folder upon creating it are all added to cache
                            //We do not just create a cache folder in every case a file is added to an existing folder since that folder will be cached separately by the initial folder scan once it reaches this folder

                            using (await Global.PersistentCacheLocks.LockAsync(longOtherDirName.ToUpperInvariantOnWindows(), context.Token))
                            {
                                var cachedFileInfos = await ReadFileInfoCache(longOtherDirName, context.ForHistory);

                                if (cachedFileInfos == null)    //ensure that the cache was not created yet by a concurrent file write to same folder
                                {
                                    cachedFileInfos = new Dictionary<string, CachedFileInfo>();
                                    await SaveFileInfoCache(cachedFileInfos, longOtherDirName, context.ForHistory);
                                }
                            }
                        }

                        await Extensions.FSOperation
                        (
                            () => Directory.CreateDirectory(longOtherDirName),
                            longOtherDirName,
                            context.Token
                        );

                    }   //if (!await Extensions.FSOperation(() => Directory.Exists(longOtherDirName), context.Token))

                    if (Global.CacheDestAndHistoryFolders)
                        Global.CreatedFoldersCache.TryAdd(longOtherDirName.ToUpperInvariantOnWindows(), true);
                }


                //invalidate file data in dirlist cache before file write
                if (!newFolderCreated)    //optimisation
                    await InvalidateFileDataInPersistentCache(context);


                var utcNowBeforeSave = DateTime.UtcNow;
                bool createTempFileFirst = !context.ForHistory && context.OtherFileInfo.Exists != false;
                await FileExtensions.WriteAllBytesAsync
                (
                    longOtherFullName, 
                    fileData, 
                    createTempFileFirst: createTempFileFirst, 
                    cancellationToken: context.Token, 
                    writeBufferKB: Global.WriteBufferKB, 
                    bufferWriteDelayMs: Global.BufferWriteDelayMs
                );


                if (Global.BidirectionalMirror && !context.ForHistory)
                { 
                    var utcNowAfterSave = DateTime.UtcNow;  //NB! for bidirectional mirror compute now after saving the file
                    BidirectionalSynchroniserSavedFileDates[otherFullName] = utcNowAfterSave;
                }


                //the file info cache became obsolete for this file
                if (
                    Global.CacheDestAndHistoryFolders
                    && context.IsSrcPath
                    && (!Global.BidirectionalMirror || context.ForHistory)
                )
                { 
                    CachedFileInfo fileInfo = null;
                    if (Global.DestAndHistoryFileInfoCache.TryGetValue(longOtherFullName.ToUpperInvariantOnWindows(), out fileInfo))
                    {
                        if (fileInfo.Exists.Value)
                        { 
                            fileInfo.LastWriteTimeUtc = utcNowBeforeSave;
                            fileInfo.Length = fileData.Length;
                        }
                        else
                        {
                            //Global.DestAndHistoryFileInfoCache.TryRemove(longOtherFullName, out fileInfo);
                            fileInfo = new CachedFileInfo(longOtherFullName, fileData.Length, utcNowBeforeSave);
                        }
                    }
                    else
                    {
                        fileInfo = new CachedFileInfo(longOtherFullName, fileData.Length, utcNowBeforeSave);

                        Global.DestAndHistoryFileInfoCache[longOtherFullName.ToUpperInvariantOnWindows()] = fileInfo;
                    }
                    

                    if (Global.PersistentCacheDestAndHistoryFolders)
                    {
                        using (await Global.PersistentCacheLocks.LockAsync(longOtherDirName.ToUpperInvariantOnWindows(), context.Token))
                        {
                            var cachedFileInfos = await ReadFileInfoCache(longOtherDirName, context.ForHistory);

                            //We do not just create a cache folder in every case a file is added to an existing folder since that folder will be cached separately by the initial folder scan once it reaches this folder
                            if (cachedFileInfos != null)
                            {
                                cachedFileInfos[GetNonFullName(longOtherFullName).ToUpperInvariantOnWindows()] = new CachedFileInfo(fileInfo, useNonFullPath: true);

                                await SaveFileInfoCache(cachedFileInfos, longOtherDirName, context.ForHistory);
                            }
                        }
                    }
                }


                await AddMessage(ConsoleColor.Magenta, $"Synchronised updates from file {context.Event.FullName}", context);


                if (Global.FileWriteDelayMs > 0)
                { 
#if !NOASYNC
                    await Task.Delay(Global.FileWriteDelayMs, context.Token);
#else
                    context.Token.WaitHandle.WaitOne(Global.FileWriteDelayMs);
#endif
                }
            }
            else if (false)     //TODO: config
            {
                //touch the file
                var now = DateTime.UtcNow;  //NB! compute common now for ConverterSavedFileDates

                try
                {
                    await Extensions.FSOperation
                    (
                        () => File.SetLastWriteTimeUtc(longOtherFullName, now),
                        longOtherFullName,
                        context.Token
                    );
                }
                catch (Exception ex)
                {
                    await ConsoleWatch.WriteException(ex, context);
                }


                if (Global.BidirectionalMirror && !context.ForHistory)
                    BidirectionalSynchroniserSavedFileDates[otherFullName] = now;
            }
        }   //public static async Task SaveFileModifications(string fullName, byte[] fileData, byte[] originalData, Context context)

#pragma warning restore AsyncFixer01
    }
}
