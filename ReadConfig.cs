//
// Copyright (c) Roland Pihlakas 2019 - 2022
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace FolderSync
{
    internal partial class Program
    {
        private static void ReadConfig()
        {
            //var environmentName = Environment.GetEnvironmentVariable("Hosting:Environment");


            var configBuilder = new ConfigurationBuilder()
                //.SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                //.AddJsonFile($"appsettings.{environmentName}.json", true)
                //.AddEnvironmentVariables()
                ;

            var config = configBuilder.Build();
            Global.Configuration = config;


            var fileConfig = config.GetSection("Files");



            Global.UsePolling = fileConfig.GetTextUpper("UsePolling") == "TRUE";   //default is false
            Global.PollingDelay = (int?)fileConfig.GetLong("PollingDelay") ?? Global.PollingDelay;


            Global.RetryCountOnEmptyDirlist = (int?)fileConfig.GetLong("RetryCountOnEmptyDirlist") ?? Global.RetryCountOnEmptyDirlist;
            Global.RetryCountOnSrcFileOpenError = (int?)fileConfig.GetLong("RetryCountOnSrcFileOpenError") ?? Global.RetryCountOnSrcFileOpenError;
            Global.FSOperationTimeout = (int?)fileConfig.GetLong("FSOperationTimeout") ?? Global.FSOperationTimeout;
            Global.DirListOperationTimeout = (int?)fileConfig.GetLong("DirListOperationTimeout") ?? Global.DirListOperationTimeout;
            Global.FileBufferWriteTimeout = (int?)fileConfig.GetLong("FileWriteTimeout") ?? Global.FileBufferWriteTimeout;
            Global.FileBufferReadTimeout = (int?)fileConfig.GetLong("FileReadTimeout") ?? Global.FileBufferReadTimeout;


            Global.UseIdlePriority = fileConfig.GetTextUpper("UseIdlePriority") == "TRUE";   //default is false
            Global.UseBackgroundMode = fileConfig.GetTextUpper("UseBackgroundMode") == "TRUE";   //default is false
            Global.Affinity = fileConfig.GetLongList("Affinity");

            Global.DirlistReadDelayMs = (int?)fileConfig.GetLong("DirlistReadDelayMs") ?? Global.DirlistReadDelayMs;
            Global.FileWriteDelayMs = (int?)fileConfig.GetLong("FileWriteDelayMs") ?? Global.FileWriteDelayMs;
            Global.ReadBufferKB = (int?)fileConfig.GetLong("ReadBufferKB") ?? Global.ReadBufferKB;
            Global.WriteBufferKB = (int?)fileConfig.GetLong("WriteBufferKB") ?? Global.WriteBufferKB;
            Global.BufferReadDelayMs = (int?)fileConfig.GetLong("BufferReadDelayMs") ?? Global.BufferReadDelayMs;
            Global.BufferWriteDelayMs = (int?)fileConfig.GetLong("BufferWriteDelayMs") ?? Global.BufferWriteDelayMs;


            Global.MaxFileSizeMB = fileConfig.GetLong("MaxFileSizeMB") ?? Global.MaxFileSizeMB;
            Global.DoNotCompareFileContent = fileConfig.GetTextUpper("DoNotCompareFileContent") == "TRUE";   //default is false
            Global.DoNotCompareFileDate = fileConfig.GetTextUpper("DoNotCompareFileDate") == "TRUE";   //default is false
            Global.DoNotCompareFileSize = fileConfig.GetTextUpper("DoNotCompareFileSize") == "TRUE";   //default is false

            //NB! these two options are independent!
            Global.CacheDestAndHistoryFolders = fileConfig.GetTextUpper("CacheDestAndHistoryFolders") == "TRUE";   //default is false since it consumes memory 
            Global.PersistentCacheDestAndHistoryFolders = fileConfig.GetTextUpper("PersistentCacheDestAndHistoryFolders") == "TRUE";   //default is false

            Global.CachePath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "CachePath"));
            if (string.IsNullOrWhiteSpace(Global.CachePath))
                Global.CachePath = Extensions.GetDirPathWithTrailingSlash(Path.Combine(".", "cache")).ToUpperInvariantOnWindows();


            Global.ShowErrorAlerts = fileConfig.GetTextUpper("ShowErrorAlerts") != "FALSE";   //default is true
            Global.LogInitialScan = fileConfig.GetTextUpper("LogInitialScan") == "TRUE";   //default is false
            Global.LogToFile = fileConfig.GetTextUpper("LogToFile") == "TRUE";   //default is false
            Global.AddTimestampToNormalLogEntries = fileConfig.GetTextUpper("AddTimestampToNormalLogEntries") != "FALSE";   //default is true


            if (!string.IsNullOrWhiteSpace(fileConfig.GetTextUpper("CaseSensitiveFilenames")))   //default is null
                Global.CaseSensitiveFilenames = fileConfig.GetTextUpper("CaseSensitiveFilenames") == "TRUE";


            Global.SrcPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "SrcPath"));


            Global.EnableMirror = fileConfig.GetTextUpper("EnableMirror") != "FALSE";   //default is true
            Global.BidirectionalMirror = Global.EnableMirror && fileConfig.GetTextUpper("Bidirectional") == "TRUE";   //default is false
            Global.MirrorIgnoreSrcDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreSrcDeletions") == "TRUE";   //default is false
            Global.MirrorIgnoreDestDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreDestDeletions") == "TRUE";   //default is false


            Global.MirrorDestPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorDestPath", "DestPath"));

            Global.MirrorWatchedExtension = new HashSet<string>(fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorWatchedExtensions", "MirrorWatchedExtension", "WatchedExtensions", "WatchedExtension"));

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            Global.MirrorExcludedExtensions = new HashSet<string>(fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorExcludedExtensions", "MirrorExcludedExtension", "ExcludedExtensions", "ExcludedExtension"));   //NB! UpperOnWindows

            Global.MirrorIgnorePathsStartingWithList = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsStartingWith", "MirrorIgnorePathStartingWith", "IgnorePathsStartingWith", "IgnorePathStartingWith");   //NB! UpperOnWindows
            Global.MirrorIgnorePathsContainingList = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsContaining", "MirrorIgnorePathContaining", "IgnorePathsContaining", "IgnorePathContaining");   //NB! UpperOnWindows
            Global.MirrorIgnorePathsEndingWithList = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsEndingWith", "MirrorIgnorePathEndingWith", "IgnorePathsEndingWith", "IgnorePathEndingWith");   //NB! UpperOnWindows

            var mirrorACInput = Global.MirrorIgnorePathsStartingWithList.Select(x => new KeyValuePair<string, bool>(NullChar + x, false))
                .Concat(Global.MirrorIgnorePathsContainingList.Select(x => new KeyValuePair<string, bool>(x, false)))
                .Concat(Global.MirrorIgnorePathsEndingWithList.Select(x => new KeyValuePair<string, bool>(x + NullChar, false)))
                .ToList();

            if (mirrorACInput.Any())  //needed to avoid exceptions
            {
                Global.MirrorIgnorePathsContainingACHasAny = true;
                Global.MirrorIgnorePathsContainingAC.Build(mirrorACInput);
            }



            Global.EnableHistory = fileConfig.GetTextUpper("EnableHistory") == "TRUE";   //default is false

            Global.HistoryDestPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryDestPath"));

            Global.HistoryWatchedExtension = new HashSet<string>(fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryWatchedExtensions", "HistoryWatchedExtension", "WatchedExtensions", "WatchedExtension"));

            Global.HistoryVersionFormat = fileConfig.GetTextUpper("HistoryVersionFormat") ?? "TIMESTAMP_BEFORE_EXT";
            Global.HistoryVersionSeparator = fileConfig.GetText("HistoryVersionSeparator") ?? ".";  //NB! no uppercase transformation here

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            Global.HistoryExcludedExtensions = new HashSet<string>(fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryExcludedExtensions", "HistoryExcludedExtension", "ExcludedExtensions", "ExcludedExtension"));   //NB! UpperOnWindows

            Global.HistoryIgnorePathsStartingWithList = fileConfig.GetListUpper("HistoryIgnorePathsStartingWith", "HistoryIgnorePathStartingWith", "IgnorePathsStartingWith", "IgnorePathStartingWith");   //NB! UpperOnWindows
            Global.HistoryIgnorePathsContainingList = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryIgnorePathsContaining", "HistoryIgnorePathContaining", "IgnorePathsContaining", "IgnorePathContaining");   //NB! UpperOnWindows
            Global.HistoryIgnorePathsEndingWithList = fileConfig.GetListUpper("HistoryIgnorePathsEndingWith", "HistoryIgnorePathEndingWith", "IgnorePathsEndingWith", "IgnorePathEndingWith");   //NB! UpperOnWindows

            var historyACInput = Global.HistoryIgnorePathsStartingWithList.Select(x => new KeyValuePair<string, bool>(NullChar + x, false))
                .Concat(Global.HistoryIgnorePathsContainingList.Select(x => new KeyValuePair<string, bool>(x, false)))
                .Concat(Global.HistoryIgnorePathsEndingWithList.Select(x => new KeyValuePair<string, bool>(x + NullChar, false)))
                .ToList();

            if (historyACInput.Any())  //needed to avoid exceptions
            {
                Global.HistoryIgnorePathsContainingACHasAny = true;
                Global.HistoryIgnorePathsContainingAC.Build(historyACInput);
            }



            Global.SrcPathMinFreeSpace = fileConfig.GetLong("SrcPathMinFreeSpace") ?? 0;
            Global.MirrorDestPathMinFreeSpace = fileConfig.GetLong("MirrorDestPathMinFreeSpace") ?? 0;
            Global.HistoryDestPathMinFreeSpace = fileConfig.GetLong("HistoryDestPathMinFreeSpace") ?? 0;
        }
    }
}
