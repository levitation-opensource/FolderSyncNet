//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using myoddweb.directorywatcher.interfaces;

namespace FolderSync
{
    internal class WatcherContext
    {
        public readonly DummyFileSystemEvent Event;
        public readonly CancellationToken Token;
        public readonly bool ForHistory;
        public readonly bool IsSrcPath;
        public readonly bool IsInitialScan;

        public CachedFileInfo FileInfo;
        public BoolRef FileInfoRefreshed;   //NB! need bool ref to share fileinfo refresh status between mirror and history contexts

        public CachedFileInfo OtherFileInfo;

        public DateTime Time
        {
            [DebuggerStepThrough]
            get
            {
                return Event?.DateTimeUtc ?? DateTime.UtcNow;
            }
        }

        [DebuggerStepThrough]
#pragma warning disable CA1068  //should take CancellationToken as the last parameter
        public WatcherContext(DummyFileSystemEvent eventObj, CancellationToken token, bool forHistory, bool isSrcPath, bool isInitialScan, BoolRef fileInfoRefreshedBoolRef)
#pragma warning restore CA1068
        {
            Event = eventObj;
            Token = token;
            ForHistory = forHistory;
            IsSrcPath = isSrcPath;
            IsInitialScan = isInitialScan;

            FileInfo = eventObj?.FileSystemInfo != null ? new CachedFileInfo(eventObj?.FileSystemInfo) : null;

            FileInfoRefreshed = fileInfoRefreshedBoolRef ?? new BoolRef();
            //FileInfo type is a file from directory scan and has stale file length. 
            //NB! if FileInfo is null then it is okay to set FileInfoRefreshed = true since if will be populated later with up-to-date information
            FileInfoRefreshed.Value = !(eventObj?.FileSystemInfo is FileInfo);
        }
    }

    internal partial class ConsoleWatch
    {
        private static readonly AsyncLockQueueDictionary<string> FileEventLocks = new AsyncLockQueueDictionary<string>();


        #pragma warning disable S1118   //Warning	S1118	Hide this public constructor by making it 'protected'.
        public ConsoleWatch(IWatcher3 watch)
#pragma warning restore S1118
        {
            //_consoleColor = Console.ForegroundColor;

            //watch.OnErrorAsync += OnErrorAsync;
            watch.OnAddedAsync += (fse, token) => OnAddedAsync(new DummyFileSystemEvent(fse), token, isInitialScan: false);
            watch.OnRemovedAsync += (fse, token) => OnRemovedAsync(new DummyFileSystemEvent(fse), token, isInitialScan: false);
            watch.OnRenamedAsync += (fse, token) => OnRenamedAsync(fse, token);
            watch.OnTouchedAsync += (fse, token) => OnTouchedAsync(new DummyFileSystemEvent(fse), token, isInitialScan: false);
        }

#if false
        private async Task OnErrorAsync(IEventError ee, CancellationToken token)
        {
            try
            {
                await AddMessage(ConsoleColor.Red, $"[!]:{ee.Message}", context);
            }
            catch (Exception ex)
            {
                await WriteException(ex, context);
            }
        }
#endif 

        public static async Task FileUpdated(WatcherContext context, bool isWatchedFileCheckDone)
        {
            if (
                (
                    isWatchedFileCheckDone 
                    || IsWatchedFile(context.Event.FullName, context.ForHistory, context.IsSrcPath)
                )
                && (await NeedsUpdate(context))     //NB!
            )
            {
                var otherFullName = await GetOtherFullName(context);
                using (await Global.FileOperationLocks.LockAsync(context.Event.FullName.ToUpperInvariantOnWindows(), otherFullName.ToUpperInvariantOnWindows(), context.Token))
                {
                    using (await Global.FileOperationSemaphore.LockAsync())
                    {
                        long maxFileSize = Math.Min(FileExtensions.MaxByteArraySize, Global.MaxFileSizeMB * (1024 * 1024));
                        //TODO: consider destination disk free space here together with the file size already before reading the file

                        Tuple<byte[], long> fileDataTuple = null;
                        try
                        {
#if false   //TODO
                            if (context.ForHistory)     
                            {
                                //open the original file. Need to open using FileInfo so that if the source contains multiple files by same name (like in Google Drive) then the correct file is opened
                                fileDataTuple = await FileExtensions.ReadAllBytesAsync
                                (
                                    //Extensions.GetLongPath(context.Event.FullName),
                                    context.Event.FileSystemInfo as FileInfo,
                                    /*allowVSS: */Global.AllowVSS,
                                    context.Token,
                                    maxFileSize,
                                    retryCount: Global.RetryCountOnSrcFileOpenError,
                                    readBufferKB: Global.ReadBufferKB,
                                    bufferReadDelayMs: Global.BufferReadDelayMs
                                );
                            }
                            else
#endif
                            {   //TODO: cache length of this file read
                                //open file by filename, so that if the source contains multiple files by same name (like in Google Drive) then same file is opened always. This avoid unnecessary overwrites in case these duplicate files in cloud drive have different sizes
                                fileDataTuple = await FileExtensions.ReadAllBytesAsync
                                (
                                    Extensions.GetLongPath(context.Event.FullName),
                                    /*allowVSS: */Global.AllowVSS,
                                    context.Token,
                                    maxFileSize,
                                    retryCount: Global.RetryCountOnSrcFileOpenError,
                                    readBufferKB: Global.ReadBufferKB,
                                    bufferReadDelayMs: Global.BufferReadDelayMs
                                );
                            }

                            if (fileDataTuple.Item1 == null)   //maximum length exceeded
                            {
                                if (fileDataTuple.Item2 >= 0)
                                    await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : fileLength > maxFileSize : {fileDataTuple.Item2} > {maxFileSize}", context);
                                else
                                    await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : unable to read file content", context);

                                return; //TODO: log error?
                            }

                            //recompare file size so that if the source contains multiple files by same name (like in Google Drive) then the actually opened file's size is considered. This avoid unnecessary overwrites in case these duplicate files in cloud drive have different sizes
                            if (context.FileInfo.Length != fileDataTuple.Item1.Length)
                            {
                                var dirlistFileLen = context.FileInfo.Length;
                                context.FileInfo.Length = fileDataTuple.Item1.Length;
                                bool needsUpdate = await NeedsUpdate(context);  //recheck the need to update based on updated file size information
                                if (!needsUpdate)
                                {
                                    await AddMessage(ConsoleColor.Red, $"Skipping file {context.Event.FullName} : file length in dirlist != file content length. Content length matches target file length : {dirlistFileLen} (dirlist) != {context.FileInfo.Length} (content)", context);

                                    return;
                                }
                            }
                        }
                        catch (FileNotFoundException)   //file was removed by the time queue processing got to it
                        {
                            return;
                        }

                        //save without transformations
                        await ConsoleWatch.SaveFileModifications(fileDataTuple.Item1, context);
                    }
                }
            }
        }

        private static async Task FileDeleted(WatcherContext context, bool isWatchedFileCheckDone)
        {
            if (
                isWatchedFileCheckDone
                || IsWatchedFile(context.Event.FullName, context.ForHistory, context.IsSrcPath)
            )
            {
                await RefreshFileInfo(context);  //NB! verify that the file is still deleted

                if (!await GetFileExists(context))  //NB! verify that the file is still deleted
                {
                    var otherFullName = await GetOtherFullName(context);

                    var otherFileInfo = new FileInfoRef(null, context.Token);
                    await DeleteFile(otherFileInfo, otherFullName, context);
                }
                else    //NB! file appears to be recreated
                {
                    //await FileUpdated(fullName, isSrcPath, context);
                }
            }
        }

#pragma warning disable AsyncFixer01
        private static async Task OnRenamedAsync(IRenamedFileSystemEvent fse, CancellationToken token)
        {
            try
            {
                //NB! create separate context to properly handle disk free space checks on cases where file is renamed from src path to dest path (not a recommended practice though!)
                var prevFileFSE = new DummyFileSystemEvent(fse.PreviousFileSystemInfo);

                var newFileFSE = new DummyFileSystemEvent(fse);

                var previousFullNameInvariant = prevFileFSE.FullName.ToUpperInvariantOnWindows();
                bool previousPathIsSrcPath = IsSrcPath(previousFullNameInvariant);
                var previousContext = new WatcherContext(prevFileFSE, token, forHistory: false, isSrcPath: previousPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: null);

                var newFullNameInvariant = newFileFSE.FileSystemInfo.FullName.ToUpperInvariantOnWindows();
                bool newPathIsSrcPath = IsSrcPath(newFullNameInvariant);

                var fileInfoRefreshedBoolRef = new BoolRef();
                var newContexts = new WatcherContext[] {
                    (
                        IsWatchedFile(prevFileFSE.FullName, forHistory: false, newPathIsSrcPath)
                        || IsWatchedFile(newFileFSE.FullName, forHistory: false, newPathIsSrcPath)
                    )
                    ? new WatcherContext(newFileFSE, token, forHistory: false, isSrcPath: newPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null,
                    (
                        IsWatchedFile(prevFileFSE.FullName, forHistory: true, newPathIsSrcPath)
                        || IsWatchedFile(newFileFSE.FullName, forHistory: true, newPathIsSrcPath)
                    )
                    ? new WatcherContext(newFileFSE, token, forHistory: true, isSrcPath: newPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null
                };


                foreach (var newContext in newContexts)
                {
                    if (newContext == null)    //not a watched file
                        continue;

                    if (newContext.ForHistory && !newContext.IsSrcPath)
                        continue;


                    try
                    {
                        if (newFileFSE.IsFile)
                        {
                            
                            var prevFileIsWatchedFile = IsWatchedFile(prevFileFSE.FileSystemInfo.FullName, newContext.ForHistory, newPathIsSrcPath);
                            var newFileIsWatchedFile = IsWatchedFile(newFileFSE.FileSystemInfo.FullName, newContext.ForHistory, newPathIsSrcPath);
                            /*
                            if (prevFileIsWatchedFile
                                || newFileIsWatchedFile)
                            */
                            {
                                await AddMessage(ConsoleColor.Cyan, $"[{(newFileFSE.IsFile ? "F" : "D")}][R]:{prevFileFSE.FullName} > {newFileFSE.FileSystemInfo.FullName}", newContext);

                                //NB! if file is renamed to cs~ or resx~ then that means there will be yet another write to same file, so lets skip this event here - NB! skip the event here, including delete event of the previous file
                                if (!newFileFSE.FileSystemInfo.FullName.EndsWith("~"))
                                {
                                    //using (await Global.FileOperationLocks.LockAsync(rfse.FileSystemInfo.FullName, rfse.PreviousFileSystemInfo.FullName, context.Token))  //comment-out: prevent deadlock
                                    {
                                        if (newFileIsWatchedFile)
                                        {
                                            using (await FileEventLocks.LockAsync(newFileFSE.FileSystemInfo.FullName, token))
                                            {
                                                await FileUpdated(newContext, isWatchedFileCheckDone: false);
                                            }
                                        }

                                        if (prevFileIsWatchedFile)
                                        {
                                            if (
                                                !newContext.ForHistory     //history files have a different name format than the original file names and would not cause a undefined behaviour
                                                && newFileIsWatchedFile     //both files were watched files
                                                && previousContext.IsSrcPath != newContext.IsSrcPath
                                                &&
                                                (
                                                    Global.BidirectionalMirror      //move in either direction between src and mirrorDest
                                                    || previousContext.IsSrcPath    //src -> mirrorDest move
                                                )
                                            )
                                            {
                                                //the file was moved from one watched path to another watched path, which is illegal, lets ignore the file move

                                                await AddMessage(ConsoleColor.Red, $"Ignoring file delete in the source path since the move was to the other managed path : {prevFileFSE.FullName} > {newFileFSE.FileSystemInfo.FullName}", previousContext);
                                            }
                                            else
                                            {
                                                //NB! invalidate file cache even if the src deletions are not mirrored

                                                var otherFullName = await GetOtherFullName(previousContext);
                                                CachedFileInfo dummy;
                                                var otherLongPath = Extensions.GetLongPath(otherFullName);
                                                Global.DestAndHistoryFileInfoCache.TryRemove(otherLongPath.ToUpperInvariantOnWindows(), out dummy);

                                                await InvalidateFileDataInPersistentCache(previousContext);


                                                if (
                                                    newContext.ForHistory
                                                    || (previousContext.IsSrcPath && Global.MirrorIgnoreSrcDeletions)
                                                    || (!previousContext.IsSrcPath && Global.MirrorIgnoreDestDeletions)
                                                )
                                                {
                                                    continue;
                                                }
                                                else
                                                {
                                                    using (await FileEventLocks.LockAsync(prevFileFSE.FileSystemInfo.FullName, token))
                                                    {
                                                        await FileDeleted(previousContext, isWatchedFileCheckDone: false);
                                                    }
                                                }
                                            }
                                        }   //if (prevFileIsWatchedFile)
                                    }
                                }   //if (!fse.FileSystemInfo.FullName.EndsWith("~"))
                            }
                        }
                        else
                        {
                            await AddMessage(ConsoleColor.Cyan, $"[{(newFileFSE.IsFile ? "F" : "D")}][R]:{prevFileFSE.FullName} > {newFileFSE.FileSystemInfo.FullName}", newContext);

                            //TODO trigger update / delete event for all files in new folder
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteException(ex, newContext);
                    }
                }
            }
            catch (Exception ex) when (
                /*ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                || ex.GetInnermostException() is TimeoutException
                || */ex.GetInnermostException() is UnauthorizedAccessException  //sometimes accessing the source file during new WatcherContext() constructor also causes access errors
            )
            {
                await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file rename {fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName} : unable to access file metadata", DateTime.Now, token: token);
            }
            catch (Exception ex)
            {
                await WriteException(ex);
            }
        }   //private static async Task OnRenamedAsync(IRenamedFileSystemEvent fse, CancellationToken token)

        internal static async Task OnRemovedAsync(DummyFileSystemEvent fse, CancellationToken token, bool isInitialScan)
        {
            try
            {
                var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows();
                bool isSrcPath = IsSrcPath(fullNameInvariant);

                var fileInfoRefreshedBoolRef = new BoolRef();
                var contexts = new WatcherContext[] {
                    //TODO: do not fail synchronisation in case of access permission error after delete operation since we actually do not need access to sources file?
                    IsWatchedFile(fse.FullName, forHistory: false, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null,
                    IsWatchedFile(fse.FullName, forHistory: true, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null
                };

                foreach (var context in contexts)
                {
                    if (context == null)    //not a watched file
                        continue;

                    if (context.ForHistory && !context.IsSrcPath)
                        continue;


                    context.FileInfo.Exists = false;

                    try
                    {
                        if (fse.IsFile)
                        {
                            //if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                            {
                                //NB! invalidate file cache even if the src deletions are not mirrored

                                var otherFullName = await GetOtherFullName(context);
                                CachedFileInfo dummy;
                                var otherLongPath = Extensions.GetLongPath(otherFullName);
                                Global.DestAndHistoryFileInfoCache.TryRemove(otherLongPath.ToUpperInvariantOnWindows(), out dummy);

                                await InvalidateFileDataInPersistentCache(context);


                                if (
                                    context.ForHistory
                                    || (context.IsSrcPath && Global.MirrorIgnoreSrcDeletions)
                                    || (!context.IsSrcPath && Global.MirrorIgnoreDestDeletions)
                                )
                                {
                                    continue;
                                }
                                else
                                {
                                    await AddMessage(ConsoleColor.Yellow, $"[{(fse.IsFile ? "F" : "D")}][-]:{fse.FileSystemInfo.FullName}", context);

                                    using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                                    {
                                        await FileDeleted(context, isWatchedFileCheckDone: true);
                                    }
                                }
                            }
                        }
                        else    //if (fse.IsFile)
                        {
                            //nothing to do here: the files are likely already deleted by now
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteException(ex, context);
                    }
                }
            }
            catch (Exception ex) when (
                /*ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                || ex.GetInnermostException() is TimeoutException
                || */ex.GetInnermostException() is UnauthorizedAccessException  //sometimes accessing the source file during new WatcherContext() constructor also causes access errors
            )
            {
                await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file removal {fse.FileSystemInfo.FullName} : unable to access file metadata", DateTime.Now, token: token);
            }
            catch (Exception ex)
            {
                await WriteException(ex);
            }
        }   //internal static async Task OnRemovedAsync(IFileSystemEvent fse, CancellationToken token)

        internal static async Task OnAddedAsync(DummyFileSystemEvent fse, CancellationToken token, bool isInitialScan)
        {
            try
            { 
                var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows();
                bool isSrcPath = IsSrcPath(fullNameInvariant);

                var fileInfoRefreshedBoolRef = new BoolRef();
                var contexts = new WatcherContext[] {
                    IsWatchedFile(fse.FullName, forHistory: false, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null,
                    IsWatchedFile(fse.FullName, forHistory: true, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null
                };

                foreach (var context in contexts)
                {
                    if (context == null)    //not a watched file
                        continue;

                    if (context.ForHistory && !context.IsSrcPath)
                        continue;


                    context.FileInfo.Exists = true;

                    try
                    {
                        if (fse.IsFile)
                        {
                            //if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                            {
                                if (!context.IsInitialScan)
                                    await AddMessage(ConsoleColor.Green, $"[{(fse.IsFile ? "F" : "D")}][+]:{fse.FileSystemInfo.FullName}", context);

                                using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                                {
                                    await FileUpdated(context, isWatchedFileCheckDone: true);
                                }
                            }
                        }
                        else
                        {
                            //nothing to do here: there are likely no files in here yet
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteException(ex, context);
                    }
                }
            }
            catch (Exception ex) when (
                /*ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                || ex.GetInnermostException() is TimeoutException
                || */ex.GetInnermostException() is UnauthorizedAccessException  //sometimes accessing the source file during new WatcherContext() constructor also causes access errors
            )
            {
                await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file add {fse.FileSystemInfo.FullName} : unable to access file metadata", DateTime.Now, token: token);
            }
            catch (Exception ex)
            {
                await WriteException(ex);
            }
        }   //internal static async Task OnAddedAsync(IFileSystemEvent fse, CancellationToken token, bool isInitialScan)

        internal static async Task OnTouchedAsync(DummyFileSystemEvent fse, CancellationToken token, bool isInitialScan)
        {
            try
            { 
                var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows();
                bool isSrcPath = IsSrcPath(fullNameInvariant);

                var fileInfoRefreshedBoolRef = new BoolRef();
                var contexts = new WatcherContext[] {
                    IsWatchedFile(fse.FullName, forHistory: false, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null,

                    IsWatchedFile(fse.FullName, forHistory: true, isSrcPath)
                    ? new WatcherContext(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
                    : null
                };

                foreach (var context in contexts)
                {
                    if (context == null)    //not a watched file
                        continue;

                    if (context.ForHistory && !context.IsSrcPath)
                        continue;


                    context.FileInfo.Exists = true;

                    try
                    {
                        if (fse.IsFile)
                        {
                            //if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                            {
                                //check for file type only after checking IsWatchedFile first since file type checking might already be a slow operation
                                if (await GetIsFile(context))     //for some reason fse.IsFile is set even for folders
                                {
                                    await AddMessage(ConsoleColor.White, $"[{(fse.IsFile ? "F" : "D")}][T]:{fse.FileSystemInfo.FullName}", context);

                                    using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                                    {
                                        await FileUpdated(context, isWatchedFileCheckDone: true);
                                    }
                                }
                            }
                        }
                        else
                        {
                            //nothing to do here: file update events are sent separately anyway
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteException(ex, context);
                    }
                }
            }
            catch (Exception ex) when (
                /*ex.GetInnermostException() is IOException  //this includes DriveNotFoundException
                || ex.GetInnermostException() is TimeoutException
                || */ex.GetInnermostException() is UnauthorizedAccessException  //sometimes accessing the source file during new WatcherContext() constructor also causes access errors
            )
            {
                await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file touch {fse.FileSystemInfo.FullName} : unable to access file metadata", DateTime.Now, token: token);
            }
            catch (Exception ex)    
            {
                await WriteException(ex);
            }
        }   //internal static async Task OnTouchedAsync(IFileSystemEvent fse, CancellationToken token)

    }
}
