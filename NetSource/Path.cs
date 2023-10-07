//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/path.cs

// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  Path
** 
** <OWNER>Microsoft</OWNER>
**
**
** Purpose: A collection of path manipulation methods.
**
**
===========================================================*/

using System;
using System.Security.Permissions;
//using Win32Native = Microsoft.Win32.Win32Native;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Runtime.Versioning;
using System.Diagnostics.Contracts;

using IOException = System.IO.IOException;
using PathTooLongException = System.IO.PathTooLongException;

namespace FolderSyncNetSource
{
    // Provides methods for processing directory strings in an ideally
    // cross-platform manner.  Most of the methods don't do a complete
    // full parsing (such as examining a UNC hostname), but they will
    // handle most string operations.  
    // 
    // File names cannot contain backslash (\), slash (/), colon (:),
    // asterick (*), question mark (?), quote ("), less than (<;), 
    // greater than (>;), or pipe (|).  The first three are used as directory
    // separators on various platforms.  Asterick and question mark are treated
    // as wild cards.  Less than, Greater than, and pipe all redirect input
    // or output from a program to a file or some combination thereof.  Quotes
    // are special.
    // 
    // We are guaranteeing that Path.SeparatorChar is the correct 
    // directory separator on all platforms, and we will support 
    // Path.AltSeparatorChar as well.  To write cross platform
    // code with minimal pain, you can use slash (/) as a directory separator in
    // your strings.
    // Class contains only static data, no need to serialize
    [ComVisible(true)]
    public static class Path
    {
        static readonly Type LongPathHelperType = Type.GetType("System.IO.LongPathHelper");      //roland

#if ENABLE_UNUSED_CODE
        // Platform specific directory separator character.  This is backslash
        // ('\') on Windows, slash ('/') on Unix, and colon (':') on Mac.
        // 
        public static readonly char DirectorySeparatorChar = '\\';
        internal const string DirectorySeparatorCharAsString = "\\";

        // Platform specific alternate directory separator character.  
        // This is backslash ('\') on Unix, and slash ('/') on Windows 
        // and MacOS.
        // 
        public static readonly char AltDirectorySeparatorChar = '/';

        // Platform specific volume separator character.  This is colon (':')
        // on Windows and MacOS, and slash ('/') on Unix.  This is mostly
        // useful for parsing paths like "c:\windows" or "MacVolume:System Folder".
        // 
        public static readonly char VolumeSeparatorChar = ':';
#endif 

        public static readonly char DirectorySeparatorChar = System.IO.Path.DirectorySeparatorChar;
        internal static readonly string DirectorySeparatorCharAsString = DirectorySeparatorChar.ToString();     //roland

        public static readonly char AltDirectorySeparatorChar = System.IO.Path.AltDirectorySeparatorChar;
        public static readonly char VolumeSeparatorChar = System.IO.Path.VolumeSeparatorChar;

#if ENABLE_UNUSED_CODE
        // Platform specific invalid list of characters in a path.
        // See the "Naming a File" MSDN conceptual docs for more details on
        // what is valid in a file name (which is slightly different from what
        // is legal in a path name).
        // Note: This list is duplicated in CheckInvalidPathChars
        [Obsolete("Please use GetInvalidPathChars or GetInvalidFileNameChars instead.")]
        public static readonly char[] InvalidPathChars = { '\"', '<', '>', '|', '\0', (Char)1, (Char)2, (Char)3, (Char)4, (Char)5, (Char)6, (Char)7, (Char)8, (Char)9, (Char)10, (Char)11, (Char)12, (Char)13, (Char)14, (Char)15, (Char)16, (Char)17, (Char)18, (Char)19, (Char)20, (Char)21, (Char)22, (Char)23, (Char)24, (Char)25, (Char)26, (Char)27, (Char)28, (Char)29, (Char)30, (Char)31 };

        // Trim trailing white spaces, tabs etc but don't be aggressive in removing everything that has UnicodeCategory of trailing space.
        // String.WhitespaceChars will trim aggressively than what the underlying FS does (for ex, NTFS, FAT).
        //roland: got the values using reflection
        internal static readonly char[] TrimEndChars = { (Char)0x09, (Char)0x0A, (Char)0x0B, (Char)0x0C, (Char)0x0D, (Char)0x20, (Char)0x85, (Char)0xA0 }; // LongPathHelper.s_trimEndChars;

        private static readonly char[] RealInvalidPathChars = PathInternal.InvalidPathChars;

        // This is used by HasIllegalCharacters
        private static readonly char[] InvalidPathCharsWithAdditionalChecks = { '\"', '<', '>', '|', '\0', (Char)1, (Char)2, (Char)3, (Char)4, (Char)5, (Char)6, (Char)7, (Char)8, (Char)9, (Char)10, (Char)11, (Char)12, (Char)13, (Char)14, (Char)15, (Char)16, (Char)17, (Char)18, (Char)19, (Char)20, (Char)21, (Char)22, (Char)23, (Char)24, (Char)25, (Char)26, (Char)27, (Char)28, (Char)29, (Char)30, (Char)31, '*', '?' };

        private static readonly char[] InvalidFileNameChars = { '\"', '<', '>', '|', '\0', (Char)1, (Char)2, (Char)3, (Char)4, (Char)5, (Char)6, (Char)7, (Char)8, (Char)9, (Char)10, (Char)11, (Char)12, (Char)13, (Char)14, (Char)15, (Char)16, (Char)17, (Char)18, (Char)19, (Char)20, (Char)21, (Char)22, (Char)23, (Char)24, (Char)25, (Char)26, (Char)27, (Char)28, (Char)29, (Char)30, (Char)31, ':', '*', '?', '\\', '/' };

        public static readonly char PathSeparator = ';';
#endif

        // Make this public sometime.
        // The max total path is 260, and the max individual component length is 255. 
        // For example, D:\<256 char file name> isn't legal, even though it's under 260 chars.
        internal static readonly int MaxPath = PathInternal.MaxShortPath;

#if ENABLE_UNUSED_CODE
        // Changes the extension of a file path. The path parameter
        // specifies a file path, and the extension parameter
        // specifies a file extension (with a leading period, such as
        // ".exe" or ".cs").
        //
        // The function returns a file path with the same root, directory, and base
        // name parts as path, but with the file extension changed to
        // the specified extension. If path is null, the function
        // returns null. If path does not contain a file extension,
        // the new file extension is appended to the path. If extension
        // is null, any exsiting extension is removed from path.
        //
        public static String ChangeExtension(String path, String extension)
        {
            if (path != null)
            {
                CheckInvalidPathChars(path);

                String s = path;
                for (int i = path.Length; --i >= 0;)
                {
                    char ch = path[i];
                    if (ch == '.')
                    {
                        s = path.Substring(0, i);
                        break;
                    }
                    if (ch == DirectorySeparatorChar || ch == AltDirectorySeparatorChar || ch == VolumeSeparatorChar) break;
                }
                if (extension != null && path.Length != 0)
                {
                    if (extension.Length == 0 || extension[0] != '.')
                    {
                        s = s + ".";
                    }
                    s = s + extension;
                }
                return s;
            }
            return null;
        }
#endif 

        // Returns the directory path of a file path. This method effectively
        // removes the last element of the given file path, i.e. it returns a
        // string consisting of all characters up to but not including the last
        // backslash ("\") in the file path. The returned value is null if the file
        // path is null or if the file path denotes a root (such as "\", "C:", or
        // "\\server\share").
        //
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        public static string GetDirectoryName(string path)
        {
            return InternalGetDirectoryName(path);
        }

        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        [System.Security.SecuritySafeCritical]

        private static string InternalGetDirectoryName(string path)
        {
            if (path != null)
            {
                CheckInvalidPathChars(path);

#if FEATURE_LEGACYNETCF
                if (!CompatibilitySwitches.IsAppEarlierThanWindowsPhone8) {
#endif

                // Expanding short paths is dangerous in this case as the results will change with the current directory.
                //
                // Suppose you have a path called "PICTUR~1\Foo". Now suppose you have two folders on disk "C:\Mine\Pictures Of Me"
                // and "C:\Yours\Pictures of You". If the current directory is neither you'll get back "PICTUR~1". If it is "C:\Mine"
                // get back "Pictures Of Me". "C:\Yours" would give back "Pictures of You".
                //
                // Because of this and as it isn't documented that short paths are expanded we will not expand short names unless
                // we're in legacy mode.
                //string normalizedPath = NormalizePath(path, fullCheck: false, expandShortPaths: AppContextSwitches.UseLegacyPathHandling);
                string normalizedPath = NormalizePath(path, fullCheck: false, expandShortPaths: false);

                // If there are no permissions for PathDiscovery to this path, we should NOT expand the short paths
                // as this would leak information about paths to which the user would not have access to.
                if (path.Length > 0
#if FEATURE_CAS_POLICY
                    // Only do the extra logic if we're not in full trust
                    && !CodeAccessSecurityEngine.QuickCheckForAllDemands()
#endif
                    )
                {
                    try
                    {
                        // If we were passed in a path with \\?\ we need to remove it as FileIOPermission does not like it.
                        string tempPath = RemoveLongPathPrefix(path);

                        // FileIOPermission cannot handle paths that contain ? or *
                        // So we only pass to FileIOPermission the text up to them.
                        int pos = 0;
                        while (pos < tempPath.Length && (tempPath[pos] != '?' && tempPath[pos] != '*'))
                            pos++;

                        // GetFullPath will Demand that we have the PathDiscovery FileIOPermission and thus throw 
                        // SecurityException if we don't. 
                        // While we don't use the result of this call we are using it as a consistent way of 
                        // doing the security checks. 
                        if (pos > 0)
                            GetFullPath(tempPath.Substring(0, pos));
                    }
                    catch (SecurityException)
                    {
                        // If the user did not have permissions to the path, make sure that we don't leak expanded short paths
                        // Only re-normalize if the original path had a ~ in it.
                        if (path.IndexOf("~", StringComparison.Ordinal) != -1)
                        {
                            normalizedPath = NormalizePath(path, fullCheck: false, expandShortPaths: false);
                        }
                    }
                    catch (PathTooLongException) { }
                    catch (NotSupportedException) { }  // Security can throw this on "c:\foo:"
                    catch (IOException) { }
                    catch (ArgumentException) { } // The normalizePath with fullCheck will throw this for file: and http:
                }

                path = normalizedPath;

#if FEATURE_LEGACYNETCF
                }
#endif

                int root = GetRootLength(path);
                int i = path.Length;
                if (i > root)
                {
                    i = path.Length;
                    if (i == root) return null;
                    while (i > root && path[--i] != DirectorySeparatorChar && path[i] != AltDirectorySeparatorChar) ;
                    String dir = path.Substring(0, i);
#if FEATURE_LEGACYNETCF
                    if (CompatibilitySwitches.IsAppEarlierThanWindowsPhone8) {
                        if (dir.Length >= MAX_PATH - 1)
                            throw new PathTooLongException(/*Environment.GetResourceString*/("IO.PathTooLong"));
                    }
#endif
                    return dir;
                }
            }
            return null;
        }

        // Gets the length of the root DirectoryInfo or whatever DirectoryInfo markers
        // are specified for the first part of the DirectoryInfo name.
        // 
        internal static int GetRootLength(string path)
        {
            CheckInvalidPathChars(path);

#if false
            if (AppContextSwitches.UseLegacyPathHandling)
            {
                return LegacyGetRootLength(path);
            }
            else
#endif
            {
                return PathInternal.GetRootLength(path);
            }
        }

        // Returns the extension of the given path. The returned value includes the
        // period (".") character of the extension except when you have a terminal period when you get String.Empty, such as ".exe" or
        // ".cpp". The returned value is null if the given path is
        // null or if the given path does not include an extension.
        //
        [Pure]
        public static String GetExtension(String path)
        {
            if (path == null)
                return null;

            CheckInvalidPathChars(path);
            int length = path.Length;
            for (int i = length; --i >= 0;)
            {
                char ch = path[i];
                if (ch == '.')
                {
                    if (i != length - 1)
                        return path.Substring(i, length - i);
                    else
                        return String.Empty;
                }
                if (ch == DirectorySeparatorChar || ch == AltDirectorySeparatorChar || ch == VolumeSeparatorChar)
                    break;
            }
            return String.Empty;
        }

        // Expands the given path to a fully qualified path. The resulting string
        // consists of a drive letter, a colon, and a root relative path. This
        // function does not verify that the resulting path 
        // refers to an existing file or directory on the associated volume.
        [Pure]
        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        public static String GetFullPath(String path, bool fullCheck = false)    //roland: fullCheck = false
        {
            String fullPath = GetFullPathInternal(path, fullCheck);
#if FEATURE_CORECLR
            FileSecurityState state = new FileSecurityState(FileSecurityStateAccess.PathDiscovery, path, fullPath);
            state.EnsureState();
#else
            //FileIOPermission.QuickDemand(FileIOPermissionAccess.PathDiscovery, fullPath, false, false);
#endif
            return fullPath;
        }

        // This method is package access to let us quickly get a string name
        // while avoiding a security check.  This also serves a slightly
        // different purpose - when we open a file, we need to resolve the
        // path into a fully qualified, non-relative path name.  This
        // method does that, finding the current drive &; directory.  But
        // as long as we don't return this info to the user, we're good.  However,
        // the public GetFullPath does need to do a security check.
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal static String GetFullPathInternal(String path, bool fullCheck = false)  //roland: fullCheck = false
        {
            if (path == null)
                throw new ArgumentNullException("path");
            Contract.EndContractBlock();

            string newPath = NormalizePath(path, fullCheck: fullCheck);

            return newPath;
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal unsafe static String NormalizePath(String path, bool fullCheck)
        {
            //return NormalizePath(path, fullCheck, AppContextSwitches.BlockLongPaths ? PathInternal.MaxShortPath : PathInternal.MaxLongPath);
            return NormalizePath(path, fullCheck, PathInternal.MaxLongPath);    //roland
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal unsafe static String NormalizePath(String path, bool fullCheck, bool expandShortPaths)
        {
            return NormalizePath(path, fullCheck, MaxPath, expandShortPaths: expandShortPaths);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal unsafe static String NormalizePath(String path, bool fullCheck, int maxPathLength)
        {
            //roland: expandShortPaths = true would change the path character case back to original
            //(lower) case if the path contains ~ character, and we do not want character case changes
            //since it would mess up the logic that does matching with src/mirror/history path starts, etc

            //return NormalizePath(path, fullCheck, maxPathLength, expandShortPaths: true); //cob roland
            return NormalizePath(path, fullCheck, maxPathLength, expandShortPaths: false);  //roland:
        }

        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        internal static string NormalizePath(string path, bool fullCheck, int maxPathLength, bool expandShortPaths)
        {
#if false
            if (AppContextSwitches.UseLegacyPathHandling)
            {
                return LegacyNormalizePath(path, fullCheck, maxPathLength, expandShortPaths);
            }
            else
#endif
            {
                if (PathInternal.IsExtended(path))
                {
                    // We can't really know what is valid for all cases of extended paths.
                    //
                    //  - object names can include other characters as well (':', '/', etc.)
                    //  - even file objects have different rules (pipe names can contain most characters)
                    //
                    // As such we will do no further analysis of extended paths to avoid blocking known and unknown
                    // scenarios as well as minimizing compat breaks should we block now and need to unblock later.
                    return path;
                }

                string normalizedPath = null;

                if (fullCheck == false)
                {
                    // Disabled fullCheck is only called by GetDirectoryName and GetPathRoot.
                    // Avoid adding addtional callers and try going direct to lighter weight NormalizeDirectorySeparators.
                    normalizedPath = NewNormalizePathLimitedChecks(path, maxPathLength, expandShortPaths);
                }
                else
                {
                    normalizedPath = NewNormalizePath(path, maxPathLength, expandShortPaths: true);
                }

#if false   //roland
                if (string.IsNullOrWhiteSpace(normalizedPath))
                    throw new ArgumentException(/*Environment.GetResourceString*/("Arg_PathIllegal"));
#endif 
                
                return normalizedPath;
            }
        }

        [System.Security.SecuritySafeCritical]
        private static string NewNormalizePathLimitedChecks(string path, int maxPathLength, bool expandShortPaths)
        {
            string normalized = PathInternal.NormalizeDirectorySeparators(path);

#if false   //roland
            if (PathInternal.IsPathTooLong(normalized) || PathInternal.AreSegmentsTooLong(normalized))
                throw new PathTooLongException();
#endif

            // Under old logic certain subsets of paths containing colons were rejected. Some portion of that comes
            // indirectly from FileIOPermissions, the rest comes from the section in LegacyNormalizePath below:
            //
            //   // To reject strings like "C:...\foo" and "C  :\foo"
            //   if (firstSegment && currentChar == VolumeSeparatorChar)
            //
            // The superset of this now is PathInternal.HasInvalidVolumeSeparator().
            //
            // Unfortunately a side effect of the old split logic is that some "bad" colon paths slip through when
            // fullChecks=false. Notably this means that GetDirectoryName and GetPathRoot would allow URIs (although
            // it would mangle them). A user could pass a "file://..." uri to GetDirectoryName(), get "file:\..." back,
            // then pass it to Uri which fixes up the bad URI. One particular user code path for this is calling
            // Assembly.CodePath and trying to get the directory before passing to the Uri class.
            //
            // To ease transitioning code forward we'll allow all "bad" colon paths through when we are doing
            // limited checks. If we want to add this back (under a quirk perhaps), we would need to conditionalize
            // for Device paths as follows:
            //
            //   if (!PathInternal.IsDevice(normalized) && PathInternal.HasInvalidVolumeSeparator(path))
            //      throw new ArgumentException(/*Environment.GetResourceString*/("Arg_PathIllegal"));

            if (expandShortPaths && normalized.IndexOf('~') != -1)
            {
                try
                {
                    //return LongPathHelper.GetLongPathName(normalized);
                    return FolderSync.PrivateStaticClassMethodInvoker<string, string>.Invoke(LongPathHelperType, "GetLongPathName", normalized);   //roland
                }
                catch
                {
                    // Don't care if we can't get the long path- might not exist, etc.
                }
            }

            return normalized;
        }

        /// <summary>
        /// Normalize the path and check for bad characters or other invalid syntax.
        /// </summary>
        [System.Security.SecuritySafeCritical]
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        private static string NewNormalizePath(string path, int maxPathLength, bool expandShortPaths)
        {
            Contract.Requires(path != null, "path can't be null");

#if false   //roland: NtCreateFile does not get confused by null characters
            // Embedded null characters are the only invalid character case we want to check up front.
            // This is because the nulls will signal the end of the string to Win32 and therefore have
            // unpredictable results. Other invalid characters we give a chance to be normalized out.
            if (path.IndexOf('\0') != -1)
                throw new ArgumentException(/*Environment.GetResourceString*/("Argument_InvalidPathChars"));
#endif

            // Note that colon and wildcard checks happen in FileIOPermissions

#if false   //roland
            // Technically this doesn't matter but we used to throw for this case
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(/*Environment.GetResourceString*/("Arg_PathIllegal"));
#endif

            // We don't want to check invalid characters for device format- see comments for extended above
            //return LongPathHelper.Normalize(path, (uint)maxPathLength, checkInvalidCharacters: !PathInternal.IsDevice(path), expandShortPaths: expandShortPaths);
            return FolderSync.PrivateStaticClassMethodInvoker<string, string, uint, bool, bool>.Invoke(LongPathHelperType, "Normalize", path, (uint)maxPathLength, /*checkInvalidCharacters: */!PathInternal.IsDevice(path), /*expandShortPaths: */expandShortPaths);   //roland
        }

#if ENABLE_UNUSED_CODE
        internal const int MaxLongPath = PathInternal.MaxLongPath;

        private const string LongPathPrefix = PathInternal.ExtendedPathPrefix;
        private const string UNCPathPrefix = PathInternal.UncPathPrefix;
        private const string UNCLongPathPrefixToInsert = PathInternal.UncExtendedPrefixToInsert;
        private const string UNCLongPathPrefix = PathInternal.UncExtendedPathPrefix;

        internal static bool HasLongPathPrefix(string path)
        {
            if (AppContextSwitches.UseLegacyPathHandling)
                return path.StartsWith(LongPathPrefix, StringComparison.Ordinal);
            else
                return PathInternal.IsExtended(path);
        }

        internal static string AddLongPathPrefix(string path)
        {
            if (AppContextSwitches.UseLegacyPathHandling)
            {
                if (path.StartsWith(LongPathPrefix, StringComparison.Ordinal))
                    return path;

                if (path.StartsWith(UNCPathPrefix, StringComparison.Ordinal))
                    return path.Insert(2, UNCLongPathPrefixToInsert); // Given \\server\share in longpath becomes \\?\UNC\server\share  => UNCLongPathPrefix + path.SubString(2); => The actual command simply reduces the operation cost.

                return LongPathPrefix + path;
            }
            else
            {
                return PathInternal.EnsureExtendedPrefix(path);
            }
        }
#endif

        internal static string RemoveLongPathPrefix(string path)
        {
#if false
            if (AppContextSwitches.UseLegacyPathHandling)
            {
                if (!path.StartsWith(LongPathPrefix, StringComparison.Ordinal))
                    return path;

                if (path.StartsWith(UNCLongPathPrefix, StringComparison.OrdinalIgnoreCase))
                    return path.Remove(2, 6); // Given \\?\UNC\server\share we return \\server\share => @'\\' + path.SubString(UNCLongPathPrefix.Length) => The actual command simply reduces the operation cost.

                return path.Substring(4);
            }
            else
#endif
            {
                return PathInternal.RemoveExtendedPrefix(path);
            }
        }

        internal static StringBuilder RemoveLongPathPrefix(StringBuilder pathSB)
        {
#if false
            if (AppContextSwitches.UseLegacyPathHandling)
            {
                if (!PathInternal.StartsWithOrdinal(pathSB, LongPathPrefix))
                    return pathSB;

                // Given \\?\UNC\server\share we return \\server\share => @'\\' + path.SubString(UNCLongPathPrefix.Length) => The actual command simply reduces the operation cost.
                if (PathInternal.StartsWithOrdinal(pathSB, UNCLongPathPrefix, ignoreCase: true))
                    return pathSB.Remove(2, 6);

                return pathSB.Remove(0, 4);
            }
            else
#endif
            {
                return PathInternal.RemoveExtendedPrefix(pathSB);
            }
        }

        // Returns the name and extension parts of the given path. The resulting
        // string contains the characters of path that follow the last
        // backslash ("\"), slash ("/"), or colon (":") character in 
        // path. The resulting string is the entire path if path 
        // contains no backslash after removing trailing slashes, slash, or colon characters. The resulting 
        // string is null if path is null.
        //
        [Pure]
        public static String GetFileName(String path)
        {
            if (path != null)
            {
                CheckInvalidPathChars(path);

                int length = path.Length;
                for (int i = length; --i >= 0;)
                {
                    char ch = path[i];
                    if (ch == DirectorySeparatorChar || ch == AltDirectorySeparatorChar || ch == VolumeSeparatorChar)
                        return path.Substring(i + 1, length - i - 1);

                }
            }
            return path;
        }

        [Pure]
        public static String GetFileNameWithoutExtension(String path)
        {
            path = GetFileName(path);
            if (path != null)
            {
                int i;
                if ((i = path.LastIndexOf('.')) == -1)
                    return path; // No path extension found
                else
                    return path.Substring(0, i);
            }
            return null;
        }

#if ENABLE_UNUSED_CODE
        // Tests if a path includes a file extension. The result is
        // true if the characters that follow the last directory
        // separator ('\\' or '/') or volume separator (':') in the path include 
        // a period (".") other than a terminal period. The result is false otherwise.
        //
        [Pure]
        public static bool HasExtension(String path)
        {
            if (path != null)
            {
                CheckInvalidPathChars(path);

                for (int i = path.Length; --i >= 0;)
                {
                    char ch = path[i];
                    if (ch == '.')
                    {
                        if (i != path.Length - 1)
                            return true;
                        else
                            return false;
                    }
                    if (ch == DirectorySeparatorChar || ch == AltDirectorySeparatorChar || ch == VolumeSeparatorChar) break;
                }
            }
            return false;
        }
#endif

        // Tests if the given path contains a root. A path is considered rooted
        // if it starts with a backslash ("\") or a drive letter and a colon (":").
        //
        [Pure]
        public static bool IsPathRooted(String path)
        {
            if (path != null)
            {
                CheckInvalidPathChars(path);

                int length = path.Length;
                if ((length >= 1 && (path[0] == DirectorySeparatorChar || path[0] == AltDirectorySeparatorChar)) || (length >= 2 && path[1] == VolumeSeparatorChar))
                    return true;
            }
            return false;
        }

        public static String Combine(String path1, String path2, bool replaceInvalidPathChars = true, bool allowDotsInFolderNames = false)  //roland: replaceInvalidPathChars, allowDotsInFolderNames
        {
#if true
            if (replaceInvalidPathChars)
            {
                char replaceInvalidPathCharsWith = '_';  //TODO: read config
                path1 = PathInternal.ReplaceInvalidChars(path1, replaceInvalidPathCharsWith, checkAdditional: true, allowDotsInFolderNames: allowDotsInFolderNames);
                path2 = PathInternal.ReplaceInvalidChars(path2, replaceInvalidPathCharsWith, checkAdditional: true, allowDotsInFolderNames: allowDotsInFolderNames);
            }

            return System.IO.Path.Combine(path1, path2);
#else
            if (path1 == null || path2 == null)
                throw new ArgumentNullException((path1 == null) ? "path1" : "path2");

            Contract.EndContractBlock();
            CheckInvalidPathChars(path1);
            CheckInvalidPathChars(path2);

            return CombineNoChecks(path1, path2);
#endif
        }

        public static String Combine(String path1, String path2, String path3, bool replaceInvalidPathChars = true, bool allowDotsInFolderNames = false)  //roland: replaceInvalidPathChars, allowDotsInFolderNames
        {
#if true
            if (replaceInvalidPathChars)
            {
                char replaceInvalidPathCharsWith = '_';  //TODO: read config
                path1 = PathInternal.ReplaceInvalidChars(path1, replaceInvalidPathCharsWith, checkAdditional: true, allowDotsInFolderNames: allowDotsInFolderNames);
                path2 = PathInternal.ReplaceInvalidChars(path2, replaceInvalidPathCharsWith, checkAdditional: true, allowDotsInFolderNames: allowDotsInFolderNames);
                path3 = PathInternal.ReplaceInvalidChars(path3, replaceInvalidPathCharsWith, checkAdditional: true, allowDotsInFolderNames: allowDotsInFolderNames);
            }

            return System.IO.Path.Combine(path1, path2, path3);
#else
            if (path1 == null || path2 == null || path3 == null)
                throw new ArgumentNullException((path1 == null) ? "path1" : (path2 == null) ? "path2" : "path3");

            Contract.EndContractBlock();
            CheckInvalidPathChars(path1);
            CheckInvalidPathChars(path2);
            CheckInvalidPathChars(path3);

            return CombineNoChecks(CombineNoChecks(path1, path2), path3);
#endif
        }

        internal static string CombineNoChecks(string path1, string path2)
        {
            if (path2.Length == 0)
                return path1;

            if (path1.Length == 0)
                return path2;

            if (IsPathRooted(path2))
                return path2;

            char ch = path1[path1.Length - 1];
            if (ch != DirectorySeparatorChar && ch != AltDirectorySeparatorChar && ch != VolumeSeparatorChar)
                return path1 + DirectorySeparatorCharAsString + path2;
            return path1 + path2;
        }

        internal static void CheckInvalidPathChars(string path, bool checkAdditional = false)
        {
            if (path == null)
                throw new ArgumentNullException("path");

#if false   //roland
            if (PathInternal.HasIllegalCharacters(path, checkAdditional))
                throw new ArgumentException(/*Environment.GetResourceString*/("Argument_InvalidPathChars"));
#endif
        }
    }
}

