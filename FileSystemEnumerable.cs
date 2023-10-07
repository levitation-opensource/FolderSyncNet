//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/filesystemenumerable.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  FileSystemEnumerable
** 
** <OWNER>kimhamil</OWNER>
**
**
** Purpose: Enumerates files and dirs
**
===========================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security;
using System.Security.Permissions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;
using System.Threading;

using IOException = System.IO.IOException;
using Path = System.IO.Path;
using SearchOption = System.IO.SearchOption;

namespace FolderSyncNetSource
{
    // Overview:
    // The key methods instantiate FileSystemEnumerableIterators. These compose the iterator with search result
    // handlers that instantiate the FileInfo, DirectoryInfo, String, etc. The handlers then perform any
    // additional required permission demands. 
    internal static class FileSystemEnumerableFactory
    {
#if ENABLE_UNUSED_CODE
        internal static IEnumerable<String> CreateFileNameIterator(String path, String originalUserPath, String searchPattern,
                                                                    bool includeFiles, bool includeDirs, SearchOption searchOption, bool checkHost, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(path != null);
            Contract.Requires(originalUserPath != null);
            Contract.Requires(searchPattern != null);

            SearchResultHandler<String> handler = new StringResultHandler(includeFiles, includeDirs);
            return new FileSystemEnumerableIterator<String>(path, originalUserPath, searchPattern, searchOption, handler, checkHost, cancellationToken);
        }
#endif

        internal static FileSystemEnumerableIterator<System.IO.FileInfo>/*IEnumerable<System.IO.FileInfo>*/ CreateFileInfoIterator(String path, String originalUserPath, String searchPattern, SearchOption searchOption, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(path != null);
            Contract.Requires(originalUserPath != null);
            Contract.Requires(searchPattern != null);

            SearchResultHandler<System.IO.FileInfo> handler = new FileInfoResultHandler();
            return new FileSystemEnumerableIterator<System.IO.FileInfo>(path, originalUserPath, searchPattern, searchOption, handler, true, cancellationToken);
        }

        internal static FileSystemEnumerableIterator<DirectoryInfo> /*IEnumerable<DirectoryInfo>*/ CreateDirectoryInfoIterator(String path, String originalUserPath, String searchPattern, SearchOption searchOption, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(path != null);
            Contract.Requires(originalUserPath != null);
            Contract.Requires(searchPattern != null);

            SearchResultHandler<DirectoryInfo> handler = new DirectoryInfoResultHandler();
            return new FileSystemEnumerableIterator<DirectoryInfo>(path, originalUserPath, searchPattern, searchOption, handler, true, cancellationToken);
        }

#if ENABLE_UNUSED_CODE
        internal static IEnumerable<System.IO.FileSystemInfo> CreateFileSystemInfoIterator(String path, String originalUserPath, String searchPattern, SearchOption searchOption, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            Contract.Requires(path != null);
            Contract.Requires(originalUserPath != null);
            Contract.Requires(searchPattern != null);

            SearchResultHandler<System.IO.FileSystemInfo> handler = new FileSystemInfoResultHandler();
            return new FileSystemEnumerableIterator<System.IO.FileSystemInfo>(path, originalUserPath, searchPattern, searchOption, handler, true, cancellationToken);
        }
#endif
    }

    // Abstract Iterator, borrowed from Linq. Used in anticipation of need for similar enumerables
    // in the future
    abstract internal class Iterator<TSource> : IEnumerable<TSource>, IEnumerator<TSource>
    {
        int threadId;
        internal int state;
        internal TSource current;

        public Iterator()
        {
            threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public TSource Current
        {
            get { return current; }
        }

        protected abstract Iterator<TSource> Clone();

        public virtual/*roland*/ void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            current = default(TSource);
            state = -1;
        }

        public IEnumerator<TSource> GetEnumerator()
        {
            if (threadId == Thread.CurrentThread.ManagedThreadId && state == 0)
            {
                state = 1;
                return this;
            }

            Iterator<TSource> duplicate = Clone();
            duplicate.state = 1;
            return duplicate;
        }

        public abstract bool MoveNext();

        object IEnumerator.Current
        {
            get { return Current; }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void IEnumerator.Reset()
        {
            throw new NotSupportedException();
        }

    }

    internal class NetSourceTypes   //roland
    {
        public static readonly Type __ErrorType = Type.GetType("System.IO.__Error");
        public static readonly Type PathType = typeof(System.IO.Path);    //static types cannot be used as type arguments, so need to use it as runtime argument to PrivateStaticClassMethodInvoker.Invoke()
        public static readonly Type DirectoryType = typeof(System.IO.Directory);    //static types cannot be used as type arguments, so need to use it as runtime argument to PrivateStaticClassMethodInvoker.Invoke()
        public static readonly Type FileSecurityStateAccessType = Type.GetType("System.IO.FileSecurityStateAccess");
        public static readonly Type WIN32_FIND_DATA_Type = Type.GetType("Microsoft.Win32.Win32Native+WIN32_FIND_DATA");
        public static readonly Type WIN32_FILE_ATTRIBUTE_DATA_Type = Type.GetType("Microsoft.Win32.Win32Native+WIN32_FILE_ATTRIBUTE_DATA");

        public static readonly char[] Path_TrimEndChars = FolderSync.Reflection.GetStaticFieldValue<char[]>(PathType, "TrimEndChars");
    }

    // Overview:
    // Enumerates file system entries matching the search parameters. For recursive searches this
    // searches through all the sub dirs and executes the search criteria against every dir.
    // 
    // Generic implementation:
    // FileSystemEnumerableIterator is generic. When it gets a WIN32_FIND_DATA, it calls the 
    // result handler to create an instance of the generic type. 
    // 
    // Usage:
    // Use FileSystemEnumerableFactory to obtain FSEnumerables that can enumerate file system 
    // entries as String path names, FileInfos, DirectoryInfos, or FileSystemInfos.
    // 
    // Security:
    // For all the dirs/files returned, demands path discovery permission for their parent folders
    internal class FileSystemEnumerableIterator<TSource> : Iterator<TSource>        //TODO: Linux and Mac support?
    {
        private const int STATE_INIT = 1;
        private const int STATE_SEARCH_NEXT_DIR = 2;
        private const int STATE_FIND_NEXT_FILE = 3;
        private const int STATE_FINISH = 4;

        private SearchResultHandler<TSource> _resultHandler;
        private List<Directory.SearchData> searchStack;
        private Directory.SearchData searchData;
        private String searchCriteria;
        [System.Security.SecurityCritical]
        SafeFindHandle _hnd = null;
        bool needsParentPathDiscoveryDemand;

        // empty means we know in advance that we won�t find any search results, which can happen if:
        // 1. we don�t have a search pattern
        // 2. we�re enumerating only the top directory and found no matches during the first call
        // This flag allows us to return early for these cases. We can�t know this in advance for
        // SearchOption.AllDirectories because we do a �*� search for subdirs and then use the
        // searchPattern at each directory level.
        bool empty;

        private String userPath;
        private SearchOption searchOption;
        private String fullPath;
        private String normalizedSearchPath;
        private int oldMode;
        private bool _checkHost;
        private CancellationToken cancellationToken;    //roland

        [System.Security.SecuritySafeCritical]
        internal FileSystemEnumerableIterator(String path, String originalUserPath, String searchPattern, SearchOption searchOption, SearchResultHandler<TSource> resultHandler, bool checkHost, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            try     //roland
            {
                Contract.Requires(path != null);
                Contract.Requires(originalUserPath != null);
                Contract.Requires(searchPattern != null);
                Contract.Requires(searchOption == SearchOption.AllDirectories || searchOption == SearchOption.TopDirectoryOnly);
                Contract.Requires(resultHandler != null);

                this.oldMode = Win32Native.SetErrorMode(Win32Native.SEM_FAILCRITICALERRORS);

                this.searchStack = new List<Directory.SearchData>();

                String normalizedSearchPattern = NormalizeSearchPattern(searchPattern);

                if (normalizedSearchPattern.Length == 0)
                {
                    this.empty = true;
                }
                else
                {
                    this._resultHandler = resultHandler;
                    this.searchOption = searchOption;
                    this.cancellationToken = cancellationToken;


                    //fullPath = Path.GetFullPathInternal(path);
                    this.fullPath = FolderSync.PrivateStaticClassMethodInvoker<String, String>.Invoke(NetSourceTypes.PathType, "GetFullPathInternal", path);     //roland

                    String fullSearchString = GetFullSearchString(fullPath, normalizedSearchPattern);
                    this.normalizedSearchPath = Path.GetDirectoryName(fullSearchString);

#if false
                    if (CodeAccessSecurityEngine.QuickCheckForAllDemands())
                    {
                        // Full trust, just need to validate incoming paths
                        // (we don't need to get the demand directory as it has no impact)
                        FileIOPermission.EmulateFileIOPermissionChecks(fullPath);
                        FileIOPermission.EmulateFileIOPermissionChecks(normalizedSearchPath);
                    }
                    else
                    {
                        // Not full trust, need to check for rights
                        string[] demandPaths = new string[2];

                        // Any illegal chars such as *, ? will be caught by FileIOPermission.HasIllegalCharacters
                        demandPaths[0] = Directory.GetDemandDir(fullPath, true);

                        // For filters like foo\*.cs we need to verify if the directory foo is not denied access.
                        // Do a demand on the combined path so that we can fail early in case of deny
                        demandPaths[1] = Directory.GetDemandDir(normalizedSearchPath, true);
                        new FileIOPermission(FileIOPermissionAccess.PathDiscovery, demandPaths, false, false).Demand();
                    }
#endif

                    this._checkHost = checkHost;

                    // normalize search criteria
                    this.searchCriteria = GetNormalizedSearchCriteria(fullSearchString, normalizedSearchPath);

                    // fix up user path
                    String searchPatternDirName = Path.GetDirectoryName(normalizedSearchPattern);
                    String userPathTemp = originalUserPath;
                    if (searchPatternDirName != null && searchPatternDirName.Length != 0)
                    {
                        userPathTemp = Path.CombineNoChecks(userPathTemp, searchPatternDirName);
                        //userPathTemp = FolderSync.PrivateStaticClassMethodInvoker<String, String, String>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", userPathTemp, searchPatternDirName);     //roland
                    }
                    this.userPath = userPathTemp;

                    this.searchData = new Directory.SearchData(normalizedSearchPath, this.userPath, searchOption);

                    CommonInit();
                }
            }
            catch (Exception ex)   //roland
            {
                (_resultHandler as IDisposable)?.Dispose();
                throw;
            }
        }

        [System.Security.SecurityCritical]
        private void CommonInit()
        {
            Contract.Assert(searchCriteria != null && searchData != null, "searchCriteria and searchData should be initialized");

            // Execute searchCriteria against the current directory
            //String searchPath = Path.InternalCombine(searchData.fullPath, searchCriteria);
            String searchPath = FolderSync.PrivateStaticClassMethodInvoker<String, string, String>.Invoke(NetSourceTypes.PathType, "InternalCombine", searchData.fullPath, searchCriteria);     //roland

            Win32Native.WIN32_FIND_DATA data = new Win32Native.WIN32_FIND_DATA();

            // Open a Find handle
            _hnd = Win32Native.FindFirstFile(searchPath, ref data);

            if (_hnd.IsInvalid)
            {
                int hr = Marshal.GetLastWin32Error();
                if (hr != Win32Native.ERROR_FILE_NOT_FOUND && hr != Win32Native.ERROR_NO_MORE_FILES)
                {
                    HandleError(hr, searchData.fullPath);

                    //roland: HandleError may skip throwing, so we need to handle this case
                    empty = searchData.searchOption == SearchOption.TopDirectoryOnly;   //roland
                }
                else
                {
                    // flag this as empty only if we're searching just top directory
                    // Used in fast path for top directory only
                    empty = searchData.searchOption == SearchOption.TopDirectoryOnly;
                }
            }
            // fast path for TopDirectoryOnly. If we have a result, go ahead and set it to 
            // current. If empty, dispose handle.
            if (searchData.searchOption == SearchOption.TopDirectoryOnly)
            {
                if (empty)
                {
                    _hnd.Dispose();
                }
                else
                {
                    if (_resultHandler.IsResultIncluded(ref data))
                    {
                        try     //roland
                        { 
                            current = _resultHandler.CreateObject(searchData, ref data);
                        }
                        catch (Exception ex) when (
                            ex is ArgumentException   //Argument_InvalidPathChars
                            || ex is IOException
                        )
                        {
                            //TODO: log the error message
                            //skip the file but do not raise exceptions
                            //TODO: is this sufficient to just skip here, or do we need to add a loop to find a new suitable file?
                            Win32Native.SetLastErrorEx(0, 0);
                            bool qqq = true;    //for debugging
                        }
                    }
                }
            }
            // for AllDirectories, we first recurse into dirs, so cleanup and add searchData 
            // to the stack
            else
            {
                _hnd.Dispose();
                searchStack.Add(searchData);
            }
        }

#if ENABLE_UNUSED_CODE
        [System.Security.SecuritySafeCritical]
        private FileSystemEnumerableIterator(String fullPath, String normalizedSearchPath, String searchCriteria, String userPath, SearchOption searchOption, SearchResultHandler<TSource> resultHandler, bool checkHost, CancellationToken cancellationToken)    //roland: CancellationToken
        {
            try     //roland
            { 
                this.fullPath = fullPath;
                this.normalizedSearchPath = normalizedSearchPath;
                this.searchCriteria = searchCriteria;
                this._resultHandler = resultHandler;
                this.userPath = userPath;
                this.searchOption = searchOption;
                this._checkHost = checkHost;
                this.cancellationToken = cancellationToken;

                searchStack = new List<Directory.SearchData>();

                if (searchCriteria != null)
                {
#if false
                    if (CodeAccessSecurityEngine.QuickCheckForAllDemands())
                    {
                        // Full trust, just need to validate incoming paths
                        // (we don't need to get the demand directory as it has no impact)
                        FileIOPermission.EmulateFileIOPermissionChecks(fullPath);
                        FileIOPermission.EmulateFileIOPermissionChecks(normalizedSearchPath);
                    }
                    else
                    {
                        // Not full trust, need to check for rights
                        string[] demandPaths = new string[2];

                        // Any illegal chars such as *, ? will be caught by FileIOPermission.HasIllegalCharacters
                        demandPaths[0] = Directory.GetDemandDir(fullPath, true);

                        // For filters like foo\*.cs we need to verify if the directory foo is not denied access.
                        // Do a demand on the combined path so that we can fail early in case of deny
                        demandPaths[1] = Directory.GetDemandDir(normalizedSearchPath, true);

                        new FileIOPermission(FileIOPermissionAccess.PathDiscovery, demandPaths, false, false).Demand();
                    }
#endif

                    searchData = new Directory.SearchData(normalizedSearchPath, userPath, searchOption);
                    CommonInit();
                }
                else
                {
                    empty = true;
                }

            }
            catch   //roland
            {
                (_resultHandler as IDisposable)?.Dispose();
                throw;
            }
        }
#endif

        protected override Iterator<TSource> Clone()
        {
            //return new FileSystemEnumerableIterator<TSource>(fullPath, normalizedSearchPath, searchCriteria, userPath, searchOption, _resultHandler, _checkHost, cancellationToken);
            throw new NotImplementedException();
        }


        public override void Dispose()   //roland
        {
            (_resultHandler as IDisposable)?.Dispose();

            base.Dispose();
        }

        [System.Security.SecuritySafeCritical]
        protected override void Dispose(bool disposing)
        {
            (_resultHandler as IDisposable)?.Dispose();     //roland

            try
            {
                if (_hnd != null)
                    _hnd.Dispose();
            }
            finally
            {
                Win32Native.SetErrorMode(oldMode);
                base.Dispose(disposing);
            }
        }

        [System.Security.SecuritySafeCritical]
        public override bool MoveNext()
        {
            if (IsCancellationRequested())
                return false;


            Win32Native.WIN32_FIND_DATA data = new Win32Native.WIN32_FIND_DATA();
            switch (state)
            {
                case STATE_INIT:
                    {
                        if (empty)
                        {
                            state = STATE_FINISH;
                            goto case STATE_FINISH;
                        }
                        if (searchData.searchOption == SearchOption.TopDirectoryOnly)
                        {
                            state = STATE_FIND_NEXT_FILE;
                            if (current != null)
                            {
                                return true;
                            }
                            else
                            {
                                goto case STATE_FIND_NEXT_FILE;
                            }
                        }
                        else
                        {
                            state = STATE_SEARCH_NEXT_DIR;
                            goto case STATE_SEARCH_NEXT_DIR;
                        }
                    }
                case STATE_SEARCH_NEXT_DIR:
                    {
#if !ENABLE_UNUSED_CODE
                        throw new NotImplementedException();
#else
                        Contract.Assert(searchData.searchOption != SearchOption.TopDirectoryOnly, "should not reach this code path if searchOption == TopDirectoryOnly");
                        // Traverse directory structure. We need to get '*'
                        while (searchStack.Count > 0)
                        {
                            searchData = searchStack[0];
                            Contract.Assert((searchData.fullPath != null), "fullpath can't be null!");
                            searchStack.RemoveAt(0);

                            // Traverse the subdirs
                            AddSearchableDirsToStack(searchData);

                            if (IsCancellationRequested())
                                return false;


                            // Execute searchCriteria against the current directory
                            //String searchPath = Path.InternalCombine(searchData.fullPath, searchCriteria);
                            String searchPath = FolderSync.PrivateStaticClassMethodInvoker<String, string, String>.Invoke(NetSourceTypes.PathType, "InternalCombine", searchData.fullPath, searchCriteria);     //roland

                            // Open a Find handle
                            _hnd = Win32Native.FindFirstFile(searchPath, ref data);
                            if (_hnd.IsInvalid)
                            {
                                int hr = Marshal.GetLastWin32Error();
                                if (hr == Win32Native.ERROR_FILE_NOT_FOUND || hr == Win32Native.ERROR_NO_MORE_FILES || hr == Win32Native.ERROR_PATH_NOT_FOUND)
                                    continue;

                                _hnd.Dispose();
                                HandleError(hr, searchData.fullPath);
                                continue;     //roland: HandleError may skip throwing, so we need to continue loop in this case
                            }

                            state = STATE_FIND_NEXT_FILE;

                            needsParentPathDiscoveryDemand = true;
                            if (_resultHandler.IsResultIncluded(ref data))
                            {
                                if (needsParentPathDiscoveryDemand)
                                {
                                    DoDemand(searchData.fullPath);
                                    needsParentPathDiscoveryDemand = false;
                                }

                                try     //roland
                                { 
                                    current = _resultHandler.CreateObject(searchData, ref data);
                                }
                                catch (Exception ex) when (
                                    ex is ArgumentException   //Argument_InvalidPathChars
                                    || ex is IOException
                                )
                                {
                                    //TODO: log the error message
                                    //skip the file but continue the loop
                                    Win32Native.SetLastErrorEx(0, 0);
                                    goto case STATE_FIND_NEXT_FILE;
                                }

                                return true;
                            }
                            else
                            {
                                goto case STATE_FIND_NEXT_FILE;
                            }
                        }
                        state = STATE_FINISH;
                        goto case STATE_FINISH;
#endif
                    }
                case STATE_FIND_NEXT_FILE:
                    {
                        if (searchData != null && _hnd != null)
                        {
                            // Keep asking for more matching files/dirs, add it to the list 
                            while (Win32Native.FindNextFile(_hnd, ref data))
                            {
                                if (_resultHandler.IsResultIncluded(ref data))
                                {
                                    if (needsParentPathDiscoveryDemand)
                                    {
                                        DoDemand(searchData.fullPath);
                                        needsParentPathDiscoveryDemand = false;
                                    }

                                    try     //roland
                                    {
                                        current = _resultHandler.CreateObject(searchData, ref data);
                                    }
                                    catch (Exception ex) when (
                                        ex is ArgumentException   //Argument_InvalidPathChars
                                        || ex is IOException
                                    )
                                    {
                                        //TODO: log the error message
                                        //skip the file but continue the loop
                                        Win32Native.SetLastErrorEx(0, 0); 
                                        continue; 
                                    }

                                    return true;
                                }
                            }

                            // Make sure we quit with a sensible error.
                            int hr = Marshal.GetLastWin32Error();

                            if (_hnd != null)
                                _hnd.Dispose();

                            // ERROR_FILE_NOT_FOUND is valid here because if the top level
                            // dir doen't contain any subdirs and matching files then 
                            // we will get here with this errorcode from the searchStack walk
                            if ((hr != 0) && (hr != Win32Native.ERROR_NO_MORE_FILES)
                                && (hr != Win32Native.ERROR_FILE_NOT_FOUND))
                            {
                                HandleError(hr, searchData.fullPath);
                                bool qqq = true;   //for debugging   //roland: NB! HandleError may skip throwing
                            }
                        }

                        if (searchData.searchOption == SearchOption.TopDirectoryOnly)
                        {
                            state = STATE_FINISH;
                            goto case STATE_FINISH;
                        }
                        else
                        {
                            if (IsCancellationRequested())
                                return false;

                            state = STATE_SEARCH_NEXT_DIR;
                            goto case STATE_SEARCH_NEXT_DIR;
                        }
                    }
                case STATE_FINISH:
                    {
                        Dispose();
                        break;
                    }
            }

            return false;
        }

        private bool IsCancellationRequested()     //roland
        {
            if (this.cancellationToken.IsCancellationRequested)     //roland
            {
                state = STATE_FINISH;

                if (_hnd != null)
                    _hnd.Dispose();

                Dispose();
                return true;
            }
            else
            {
                return false;
            }
        }

        [System.Security.SecurityCritical]
        private void HandleError(int hr, String path)
        {
            //Dispose();    //cob roland

            try
            { 
                //__Error.WinIOError(hr, path);
                FolderSync.PrivateStaticClassMethodInvoker_Void<int, String>.Invoke(NetSourceTypes.__ErrorType, "WinIOError", hr, path);   //roland
            }
            catch (Exception ex) when (
                ex is ArgumentException   //Argument_InvalidPathChars
                || ex is IOException
            )
            {
                string errorMsg = Win32Native.GetMessage(hr);

                //TODO: log the error message
                bool qqq = true;
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

#if ENABLE_UNUSED_CODE
        [System.Security.SecurityCritical]  // auto-generated
        private void AddSearchableDirsToStack(Directory.SearchData localSearchData)
        {
            Contract.Requires(localSearchData != null);

            //String searchPath = Path.InternalCombine(localSearchData.fullPath, "*");
            String searchPath = FolderSync.PrivateStaticClassMethodInvoker<String, string, String>.Invoke(NetSourceTypes.PathType, "InternalCombine", localSearchData.fullPath, "*");     //roland

            SafeFindHandle hnd = null;
            Win32Native.WIN32_FIND_DATA data = new Win32Native.WIN32_FIND_DATA();
            try
            {
                // Get all files and dirs
                hnd = Win32Native.FindFirstFile(searchPath, ref data);

                if (hnd.IsInvalid)
                {
                    int hr = Marshal.GetLastWin32Error();

                    // This could happen if the dir doesn't contain any files.
                    // Continue with the recursive search though, eventually
                    // searchStack will become empty
                    if (hr == Win32Native.ERROR_FILE_NOT_FOUND || hr == Win32Native.ERROR_NO_MORE_FILES || hr == Win32Native.ERROR_PATH_NOT_FOUND)
                        return;

                    HandleError(hr, localSearchData.fullPath);
                    return;     //roland: HandleError may skip throwing, so we need to return in this case
                }

                // Add subdirs to searchStack. Exempt ReparsePoints as appropriate
                int incr = 0;
                do
                {
                    if (this.cancellationToken.IsCancellationRequested)     //roland
                        return;

                    if (data.IsNormalDirectory)
                    {
                        string fileName = data.cFileName;
                        string tempFullPath = Path.CombineNoChecks(localSearchData.fullPath, fileName);
                        string tempUserPath = Path.CombineNoChecks(localSearchData.userPath, fileName);
                        //string tempFullPath = FolderSync.PrivateStaticClassMethodInvoker<string, string, string>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", localSearchData.fullPath, fileName);     //roland
                        //string tempUserPath = FolderSync.PrivateStaticClassMethodInvoker<string, string, string>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", localSearchData.userPath, fileName);     //roland

                        SearchOption option = localSearchData.searchOption;

#if EXCLUDE_REPARSEPOINTS
                        // Traverse reparse points depending on the searchoption specified
                        if ((searchDataSubDir.searchOption == SearchOption.AllDirectories) && (0 != (data.dwFileAttributes & Win32Native.FILE_ATTRIBUTE_REPARSE_POINT)))
                            option = SearchOption.TopDirectoryOnly; 
#endif
                        // Setup search data for the sub directory and push it into the stack
                        Directory.SearchData searchDataSubDir = new Directory.SearchData(tempFullPath, tempUserPath, option);

                        searchStack.Insert(incr++, searchDataSubDir);
                    }
                } 
                while (Win32Native.FindNextFile(hnd, ref data));
                // We don't care about errors here
            }
            finally
            {
                if (hnd != null)
                    hnd.Dispose();
            }
        }
#endif

        [System.Security.SecurityCritical]
        internal void DoDemand(String fullPathToDemand)
        {
#if false
#if FEATURE_CORECLR
            if(_checkHost) {
                String demandDir = Directory.GetDemandDir(fullPathToDemand, true);
                FileSecurityState state = new FileSecurityState(FileSecurityStateAccess.PathDiscovery, String.Empty, demandDir);
                state.EnsureState();
            }
#else
            String demandDir = Directory.GetDemandDir(fullPathToDemand, true);
            FileIOPermission.QuickDemand(FileIOPermissionAccess.PathDiscovery, demandDir, false, false);
#endif
#endif
        }

        private static String NormalizeSearchPattern(String searchPattern)
        {
            Contract.Requires(searchPattern != null);

            // Win32 normalization trims only U+0020. 
            //String tempSearchPattern = searchPattern.TrimEnd(Path.TrimEndChars);
            String tempSearchPattern = searchPattern.TrimEnd(NetSourceTypes.Path_TrimEndChars);

            // Make this corner case more useful, like dir
            if (tempSearchPattern.Equals("."))
            {
                tempSearchPattern = "*";
            }

            //Path.CheckSearchPattern(tempSearchPattern);
            FolderSync.PrivateStaticClassMethodInvoker_Void<String>.Invoke(NetSourceTypes.PathType, "CheckSearchPattern", tempSearchPattern);     //roland

            return tempSearchPattern;
        }

        private static String GetNormalizedSearchCriteria(String fullSearchString, String fullPathMod)
        {
            Contract.Requires(fullSearchString != null);
            Contract.Requires(fullPathMod != null);
            Contract.Requires(fullSearchString.Length >= fullPathMod.Length);

            String searchCriteria = null;
            char lastChar = fullPathMod[fullPathMod.Length - 1];

            //if (Path.IsDirectorySeparator(lastChar))
            if (FolderSync.PrivateStaticClassMethodInvoker<bool, char>.Invoke(NetSourceTypes.PathType, "IsDirectorySeparator", lastChar))     //roland
            {
                // Can happen if the path is C:\temp, in which case GetDirectoryName would return C:\
                searchCriteria = fullSearchString.Substring(fullPathMod.Length);
            }
            else
            {
                Contract.Assert(fullSearchString.Length > fullPathMod.Length);
                searchCriteria = fullSearchString.Substring(fullPathMod.Length + 1);
            }
            return searchCriteria;
        }

        private static String GetFullSearchString(String fullPath, String searchPattern)
        {
            Contract.Requires(fullPath != null);
            Contract.Requires(searchPattern != null);

            //String tempStr = Path.InternalCombine(fullPath, searchPattern);
            String tempStr = FolderSync.PrivateStaticClassMethodInvoker<String, String, String>.Invoke(NetSourceTypes.PathType, "InternalCombine", fullPath, searchPattern);     //roland

            // If path ends in a trailing slash (\), append a * or we'll get a "Cannot find the file specified" exception
            char lastChar = tempStr[tempStr.Length - 1];

            //if (Path.IsDirectorySeparator(lastChar) || lastChar == Path.VolumeSeparatorChar)
            if (FolderSync.PrivateStaticClassMethodInvoker<bool, char>.Invoke(NetSourceTypes.PathType, "IsDirectorySeparator", lastChar) || lastChar == System.IO.Path.VolumeSeparatorChar)     //roland
            {
                tempStr = tempStr + '*';
            }

            return tempStr;
        }
    }

    internal abstract class SearchResultHandler<TSource>
    {
        [System.Security.SecurityCritical]
        internal abstract bool IsResultIncluded(ref Win32Native.WIN32_FIND_DATA findData);

        [System.Security.SecurityCritical]
        internal abstract TSource CreateObject(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData);
    }

#if ENABLE_UNUSED_CODE
    internal class StringResultHandler : SearchResultHandler<string>
    {
        private bool _includeFiles;
        private bool _includeDirs;

        internal StringResultHandler(bool includeFiles, bool includeDirs)
        {
            _includeFiles = includeFiles;
            _includeDirs = includeDirs;
        }

        [System.Security.SecurityCritical]
        internal override bool IsResultIncluded(ref Win32Native.WIN32_FIND_DATA findData)
            => (_includeFiles && findData.IsFile) || (_includeDirs && findData.IsNormalDirectory);

        [System.Security.SecurityCritical]
        internal override string CreateObject(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
            => Path.CombineNoChecks(searchData.userPath, findData.cFileName);
            //=> FolderSync.PrivateStaticClassMethodInvoker<string, string, string>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", searchData.userPath, findData.cFileName);     //roland

    }
#endif

    internal class FileInfoResultHandler : SearchResultHandler<System.IO.FileInfo>, IDisposable
    {
        IntPtr Ptr = IntPtr.Zero;     //roland
        static int Size = Marshal.SizeOf(NetSourceTypes.WIN32_FILE_ATTRIBUTE_DATA_Type);

        static FileInfoResultHandler()
        {
            if (Size != Marshal.SizeOf(FolderSync.TypeOf<Win32Native.WIN32_FILE_ATTRIBUTE_DATA>.Value))
                throw new ArgumentException();
        }

        public FileInfoResultHandler()
        { 
            Ptr = Marshal.AllocHGlobal(Size);
        }

        public void Dispose()   //roland
        {
            if (Ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Ptr);
                Ptr = IntPtr.Zero;
            }
        }

        [System.Security.SecurityCritical]
        internal override bool IsResultIncluded(ref Win32Native.WIN32_FIND_DATA findData) => findData.IsFile;

        [System.Security.SecurityCritical]
        internal override System.IO.FileInfo CreateObject(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
        {
            return CreateFileInfo(searchData, ref findData);
        }

        [System.Security.SecurityCritical]
        internal /*static*/ System.IO.FileInfo CreateFileInfo(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
        {
            string fileName = findData.cFileName;

            string fullPath = Path.CombineNoChecks(searchData.fullPath, fileName);
            //string fullPath = FolderSync.PrivateStaticClassMethodInvoker<string, string, string>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", searchData.fullPath, fileName);     //roland
#if false
            if (!CodeAccessSecurityEngine.QuickCheckForAllDemands())
            {
                // There is no need to emulate checks that FileIOPermission does if we aren't in full trust.
                // The paths we're getting are already tested and/or coming straight from the OS.
                new FileIOPermission(FileIOPermissionAccess.Read, new string[] { fullPath }, false, false).Demand();
            }
#endif

            FileInfo fi_temp = new FileInfo(fullPath, fileName);
            //need to disable path checks therefore using reflection to call internal constructor of System.IO.FileInfo, not the public one
            System.IO.FileInfo fi_out = FolderSync.Creator_<System.IO.FileInfo, string, string>.Create(fullPath, fileName);     //roland

            fi_temp.InitializeFrom(ref findData);
            //var findData2 = FolderSync.ForceCast<Win32Native.WIN32_FIND_DATA>.Do(NetSourceTypes.WIN32_FIND_DATA_Type, findData);
            //FolderSync.PrivateClassMethodInvoker_VoidRef<System.IO.FileInfo, Win32Native.WIN32_FIND_DATA>.Invoke(fi_out, "InitializeFrom", ref findData2);     //roland

            //var _data = FolderSync.ForceCast<Win32Native.WIN32_FILE_ATTRIBUTE_DATA>.Do(NetSourceTypes.WIN32_FILE_ATTRIBUTE_DATA_Type, fi_temp._data);
            //var _data = FolderSync.ForceCast<Win32Native.WIN32_FILE_ATTRIBUTE_DATA>.Do(typeof(object), fi_temp._data);    //this would result in memory corruption

            /*
            internal int fileAttributes;
            internal FILE_TIME ftCreationTime;
            internal FILE_TIME ftLastAccessTime;
            internal FILE_TIME ftLastWriteTime;
            internal int fileSizeHigh;
            internal int fileSizeLow;
            */
            var _dataField = FolderSync.Reflection.GetInstanceFieldValue<object, System.IO.FileInfo>(fi_out, "_data");

            /*
            //FolderSync.Reflection.SetInstanceField(fi_out, "_data", _data);
            FolderSync.Reflection.SetInstanceField(_dataField, "fileAttributes", fi_temp._data.fileAttributes);
            FolderSync.Reflection.SetInstanceField(_dataField, "ftCreationTime", fi_temp._data.ftCreationTime);
            FolderSync.Reflection.SetInstanceField(_dataField, "ftLastAccessTime", fi_temp._data.ftLastAccessTime);
            FolderSync.Reflection.SetInstanceField(_dataField, "ftLastWriteTime", fi_temp._data.ftLastWriteTime);
            FolderSync.Reflection.SetInstanceField(_dataField, "fileSizeHigh", fi_temp._data.fileSizeHigh);
            FolderSync.Reflection.SetInstanceField(_dataField, "fileSizeLow", fi_temp._data.fileSizeLow);
            */

            //var _dataBytes = GetBytes(fi_temp._data);
            //var _data = FromBytes(_dataBytes);
            Marshal.StructureToPtr(fi_temp._data, Ptr, true);
            var _data = Marshal.PtrToStructure(Ptr, NetSourceTypes.WIN32_FILE_ATTRIBUTE_DATA_Type);

            FolderSync.Reflection.SetInstanceField(fi_out, "_data", _data);
            FolderSync.Reflection.SetInstanceField(fi_out, "_dataInitialised", fi_temp._dataInitialised);

            return fi_out;
        }
    }

    internal class DirectoryInfoResultHandler : SearchResultHandler<DirectoryInfo>
    {
        [System.Security.SecurityCritical]
        internal override bool IsResultIncluded(ref Win32Native.WIN32_FIND_DATA findData) => findData.IsNormalDirectory;

        [System.Security.SecurityCritical]
        internal override DirectoryInfo CreateObject(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
        {
            return CreateDirectoryInfo(searchData, ref findData);
        }

        [System.Security.SecurityCritical]
        internal static DirectoryInfo CreateDirectoryInfo(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
        {
            string fileName = findData.cFileName;

            string fullPath = Path.CombineNoChecks(searchData.fullPath, fileName);
            //string fullPath = FolderSync.PrivateStaticClassMethodInvoker<string, string, string>.Invoke(NetSourceTypes.PathType, "CombineNoChecks", searchData.fullPath, fileName);     //roland

#if false
            if (!CodeAccessSecurityEngine.QuickCheckForAllDemands())
            {
                // There is no need to emulate checks that FileIOPermission does if we aren't in full trust.
                // The paths we're getting are already tested and/or coming straight from the OS.
                new FileIOPermission(FileIOPermissionAccess.Read, new string[] { fullPath + "\\." }, false, false).Demand();
            }
#endif

            DirectoryInfo di = new DirectoryInfo(fullPath, fileName);   //path checks are disabled in this custom adapted class so we can call it directly without reflection
            //DirectoryInfo di = FolderSync.Creator_<DirectoryInfo, string, string>.Create(fullPath, fileName);     //roland

            di.InitializeFrom(ref findData);
            //var findData2 = FolderSync.ForceCast<Win32Native.WIN32_FIND_DATA>.Do(NetSourceTypes.WIN32_FIND_DATA_Type, findData);
            //FolderSync.PrivateClassMethodInvoker_VoidRef<DirectoryInfo, Win32Native.WIN32_FIND_DATA>.Invoke(di, "InitializeFrom", ref findData2);     //roland

            return di;
        }
    }

#if ENABLE_UNUSED_CODE
    internal class FileSystemInfoResultHandler : SearchResultHandler<System.IO.FileSystemInfo>
    {
        [System.Security.SecurityCritical]
        internal override bool IsResultIncluded(ref Win32Native.WIN32_FIND_DATA findData) => findData.IsFile || findData.IsNormalDirectory;

        [System.Security.SecurityCritical]
        internal override System.IO.FileSystemInfo CreateObject(Directory.SearchData searchData, ref Win32Native.WIN32_FIND_DATA findData)
        {
            return findData.IsFile
                ? (System.IO.FileSystemInfo)FileInfoResultHandler.CreateFileInfo(searchData, ref findData)
                : (System.IO.FileSystemInfo)DirectoryInfoResultHandler.CreateDirectoryInfo(searchData, ref findData);
        }
    }
#endif
}
