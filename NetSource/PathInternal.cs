//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

#define ASYNC

//adapted from https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/pathinternal.cs

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace FolderSyncNetSource
{
    /// <summary>Contains internal path helpers that are shared between many projects.</summary>
    internal static class PathInternal
    {
        internal const string ExtendedPathPrefix = @"\\?\";
#if ENABLE_UNUSED_CODE
        internal const string UncPathPrefix = @"\\";
        internal const string UncExtendedPrefixToInsert = @"?\UNC\";
#endif
        internal const string UncExtendedPathPrefix = @"\\?\UNC\";
#if ENABLE_UNUSED_CODE
        internal const string DevicePathPrefix = @"\\.\";
#endif
        internal const int DevicePrefixLength = 4;
        internal const int MaxShortPath = 260;

        // Windows is limited in long paths by the max size of its internal representation of a unicode string.
        // UNICODE_STRING has a max length of USHORT in _bytes_ without a trailing null.
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff564879.aspx
        internal const int MaxLongPath = short.MaxValue;
        internal static readonly int MaxComponentLength = 255;

#if false
        internal static readonly char[] InvalidPathChars =
        {
            '\"', '<', '>', '|', '\0',
            (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
            (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
            (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
            (char)31
        };
#else
        //roland: https://stackoverflow.com/questions/7406102/create-sane-safe-filename-from-any-unsafe-string
        //blacklist = set(chr(127) + r'<>:"/\|?*')
        public static readonly HashSet<char> InvalidPathChars = new HashSet<char>
        (
            new char[]  //in comparison to original InvalidPathChars this list adds ':' and either '/' or '\\', depending on OS
            {
                //127 is unprintable, the rest are illegal in Windows.
                //(char)127,
                '<', '>', ':', '"', '/', '\\', '|' //, '?', '*'
            }
            .Where(x => x != Path.DirectorySeparatorChar)  //re-insert DirectorySeparatorChar into whitelist
            .Concat(Enumerable.Range(0, 32).Select(x => (char)x))
        );
#endif

#if false
        //roland: https://stackoverflow.com/questions/7406102/create-sane-safe-filename-from-any-unsafe-string
        //whitelist = { chr(x) for x in range(32, 256) } - blacklist    # 0-32, 127 are unprintable
        public static readonly HashSet<char> WhiteList = new HashSet<char>
        (
            Enumerable.Range(32, 256 - 32)
                .Select(x => (char)x)
                .Where(x => !InvalidPathChars.Contains(x))
        );
#endif

        internal static char[] WildCardChars = new char[] { '*', '?' };     //roland

        public static readonly HashSet<string> ForbiddenFileNames = new HashSet<string>   //roland
        {
            // https://stackoverflow.com/questions/7406102/create-sane-safe-filename-from-any-unsafe-string
            // device names, '.', and '..' are invalid filenames in Windows.
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2",
            "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "CONIN$", "CONOUT$" //, "..", "."
        };

        /// <summary>
        /// Returns true if the given character is a valid drive letter
        /// </summary>
        internal static bool IsValidDriveChar(char value)
        {
            return ((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z'));
        }

#if ENABLE_UNUSED_CODE
        /// <summary>
        /// Returns true if the path is too long
        /// </summary>
        internal static bool IsPathTooLong(string fullPath)
        {
#if false
            // We'll never know precisely what will fail as paths get changed internally in Windows and
            // may grow beyond / shrink below exceed MaxLongPath.
            if (AppContextSwitches.BlockLongPaths)
            {
                // We allow paths of any length if extended (and not in compat mode)
                if (AppContextSwitches.UseLegacyPathHandling || !IsExtended(fullPath))
                    return fullPath.Length >= MaxShortPath;
            }
#endif

            return fullPath.Length >= MaxLongPath;
        }

        /// <summary>
        /// Return true if any path segments are too long
        /// </summary>
        internal static bool AreSegmentsTooLong(string fullPath)
        {
            int length = fullPath.Length;
            int lastSeparator = 0;

            for (int i = 0; i < length; i++)
            {
                if (IsDirectorySeparator(fullPath[i]))
                {
                    if (i - lastSeparator > MaxComponentLength)
                        return true;
                    lastSeparator = i;
                }
            }

            if (length - 1 - lastSeparator > MaxComponentLength)
                return true;

            return false;
        }
#endif

#if ENABLE_UNUSED_CODE
        /// <summary>
        /// Adds the extended path prefix (\\?\) if not relative or already a device path.
        /// </summary>
        internal static string EnsureExtendedPrefix(string path)
        {
            // Putting the extended prefix on the path changes the processing of the path. It won't get normalized, which
            // means adding to relative paths will prevent them from getting the appropriate current directory inserted.

            // If it already has some variant of a device path (\??\, \\?\, \\.\, //./, etc.) we don't need to change it
            // as it is either correct or we will be changing the behavior. When/if Windows supports long paths implicitly
            // in the future we wouldn't want normalization to come back and break existing code.

            // In any case, all internal usages should be hitting normalize path (Path.GetFullPath) before they hit this
            // shimming method. (Or making a change that doesn't impact normalization, such as adding a filename to a
            // normalized base path.)
            if (IsPartiallyQualified(path) || IsDevice(path))
                return path;

            // Given \\server\share in longpath becomes \\?\UNC\server\share
            if (path.StartsWith(UncPathPrefix, StringComparison.OrdinalIgnoreCase))
                return path.Insert(2, UncExtendedPrefixToInsert);

            return ExtendedPathPrefix + path;
        }
#endif 

        /// <summary>
        /// Removes the extended path prefix (\\?\) if present.
        /// </summary>
        internal static string RemoveExtendedPrefix(string path)
        {
            if (!IsExtended(path))
                return path;

            // Given \\?\UNC\server\share we return \\server\share
            if (IsExtendedUnc(path))
                return path.Remove(2, 6);

            return path.Substring(DevicePrefixLength);
        }

        /// <summary>
        /// Removes the extended path prefix (\\?\) if present.
        /// </summary>
        internal static StringBuilder RemoveExtendedPrefix(StringBuilder path)
        {
            if (!IsExtended(path))
                return path;

            // Given \\?\UNC\server\share we return \\server\share
            if (IsExtendedUnc(path))
                return path.Remove(2, 6);

            return path.Remove(0, DevicePrefixLength);
        }

        /// <summary>
        /// Returns true if the path uses any of the DOS device path syntaxes. ("\\.\", "\\?\", or "\??\")
        /// </summary>
        internal static bool IsDevice(string path)
        {
            // If the path begins with any two separators it will be recognized and normalized and prepped with
            // "\??\" for internal usage correctly. "\??\" is recognized and handled, "/??/" is not.
            return IsExtended(path)
                ||
                (
                    path.Length >= DevicePrefixLength
                    && IsDirectorySeparator(path[0])
                    && IsDirectorySeparator(path[1])
                    && (path[2] == '.' || path[2] == '?')
                    && IsDirectorySeparator(path[3])
                );
        }

        /// <summary>
        /// Returns true if the path uses the canonical form of extended syntax ("\\?\" or "\??\"). If the
        /// path matches exactly (cannot use alternate directory separators) Windows will skip normalization
        /// and path length checks.
        /// </summary>
        internal static bool IsExtended(string path)
        {
            // While paths like "//?/C:/" will work, they're treated the same as "\\.\" paths.
            // Skipping of normalization will *only* occur if back slashes ('\') are used.
            return path.Length >= DevicePrefixLength
                && path[0] == '\\'
                && (path[1] == '\\' || path[1] == '?')
                && path[2] == '?'
                && path[3] == '\\';
        }

        /// <summary>
        /// Returns true if the path uses the canonical form of extended syntax ("\\?\" or "\??\"). If the
        /// path matches exactly (cannot use alternate directory separators) Windows will skip normalization
        /// and path length checks.
        /// </summary>
        internal static bool IsExtended(StringBuilder path)
        {
            // While paths like "//?/C:/" will work, they're treated the same as "\\.\" paths.
            // Skipping of normalization will *only* occur if back slashes ('\') are used.
            return path.Length >= DevicePrefixLength
                && path[0] == '\\'
                && (path[1] == '\\' || path[1] == '?')
                && path[2] == '?'
                && path[3] == '\\';
        }

        /// <summary>
        /// Returns true if the path uses the extended UNC syntax (\\?\UNC\ or \??\UNC\)
        /// </summary>
        internal static bool IsExtendedUnc(string path)
        {
            return path.Length >= UncExtendedPathPrefix.Length
                && IsExtended(path)
                && char.ToUpper(path[4]) == 'U'
                && char.ToUpper(path[5]) == 'N'
                && char.ToUpper(path[6]) == 'C'
                && path[7] == '\\';
        }

        /// <summary>
        /// Returns true if the path uses the extended UNC syntax (\\?\UNC\ or \??\UNC\)
        /// </summary>
        internal static bool IsExtendedUnc(StringBuilder path)
        {
            return path.Length >= UncExtendedPathPrefix.Length
                && IsExtended(path)
                && char.ToUpper(path[4]) == 'U'
                && char.ToUpper(path[5]) == 'N'
                && char.ToUpper(path[6]) == 'C'
                && path[7] == '\\';
        }

        /// <summary>
        /// Returns a value indicating if the given path contains invalid characters (", &lt;, &gt;, | 
        /// NUL, or any ASCII char whose integer representation is in the range of 1 through 31).
        /// Does not check for wild card characters ? and *.
        ///
        /// Will not check if the path is a device path and not in Legacy mode as many of these
        /// characters are valid for devices (pipes for example).
        /// </summary>
        internal static bool HasIllegalCharacters(string path, bool checkAdditional = false)
        {
#if false
            if (!AppContextSwitches.UseLegacyPathHandling && IsDevice(path))
            {
                return false;
            }
#endif

            //return AnyPathHasIllegalCharacters(path, checkAdditional: checkAdditional);
            return false;   //roland
        }

#if ENABLE_UNUSED_CODE
        /// <summary>
        /// Version of HasIllegalCharacters that checks no AppContextSwitches. Only use if you know you need to skip switches and don't care
        /// about proper device path handling.
        /// </summary>
        internal static bool AnyPathHasIllegalCharacters(string path, bool checkAdditional = false)
        {
            return path.IndexOfAny(InvalidPathChars) >= 0 || (checkAdditional && AnyPathHasWildCardCharacters(path));
        }

        /// <summary>
        /// Check for ? and *.
        /// </summary>
        internal static bool HasWildCardCharacters(string path)
        {
            // Question mark is part of some device paths
            //int startIndex = AppContextSwitches.UseLegacyPathHandling ? 0 : IsDevice(path) ? ExtendedPathPrefix.Length : 0;
            int startIndex = IsDevice(path) ? ExtendedPathPrefix.Length : 0;
            return AnyPathHasWildCardCharacters(path, startIndex: startIndex);
        }

        /// <summary>
        /// Version of HasWildCardCharacters that checks no AppContextSwitches. Only use if you know you need to skip switches and don't care
        /// about proper device path handling.
        /// </summary>
        internal static bool AnyPathHasWildCardCharacters(string path, int startIndex = 0)
        {
            char currentChar;
            for (int i = startIndex; i < path.Length; i++)
            {
                currentChar = path[i];
                if (currentChar == '*' || currentChar == '?') return true;
            }
            return false;
        }
#endif

        /// <summary>
        /// Gets the length of the root of the path (drive, share, etc.).
        /// </summary>
        [System.Security.SecuritySafeCritical]
        internal unsafe static int GetRootLength(string path)
        {
            fixed (char* value = path)
            {
                return (int)GetRootLength(value, (ulong)path.Length);
            }
        }

        [System.Security.SecurityCritical]
        private unsafe static uint GetRootLength(char* path, ulong pathLength)
        {
            uint i = 0;
            uint volumeSeparatorLength = 2;  // Length to the colon "C:"
            uint uncRootLength = 2;          // Length to the start of the server name "\\"

            bool extendedSyntax = StartsWithOrdinal(path, pathLength, ExtendedPathPrefix);
            bool extendedUncSyntax = StartsWithOrdinal(path, pathLength, UncExtendedPathPrefix);
            if (extendedSyntax)
            {
                // Shift the position we look for the root from to account for the extended prefix
                if (extendedUncSyntax)
                {
                    // "\\" -> "\\?\UNC\"
                    uncRootLength = (uint)UncExtendedPathPrefix.Length;
                }
                else
                {
                    // "C:" -> "\\?\C:"
                    volumeSeparatorLength += (uint)ExtendedPathPrefix.Length;
                }
            }

            if ((!extendedSyntax || extendedUncSyntax) && pathLength > 0 && IsDirectorySeparator(path[0]))
            {
                // UNC or simple rooted path (e.g. "\foo", NOT "\\?\C:\foo")

                i = 1; //  Drive rooted (\foo) is one character
                if (extendedUncSyntax || (pathLength > 1 && IsDirectorySeparator(path[1])))
                {
                    // UNC (\\?\UNC\ or \\), scan past the next two directory separators at most
                    // (e.g. to \\?\UNC\Server\Share or \\Server\Share\)
                    i = uncRootLength;
                    int n = 2; // Maximum separators to skip
                    while (i < pathLength && (!IsDirectorySeparator(path[i]) || --n > 0)) i++;
                }
            }
            else if (pathLength >= volumeSeparatorLength && path[volumeSeparatorLength - 1] == Path.VolumeSeparatorChar)
            {
                // Path is at least longer than where we expect a colon, and has a colon (\\?\A:, A:)
                // If the colon is followed by a directory separator, move past it
                i = volumeSeparatorLength;
                if (pathLength >= volumeSeparatorLength + 1 && IsDirectorySeparator(path[volumeSeparatorLength])) i++;
            }
            return i;
        }

        [System.Security.SecurityCritical]
        private unsafe static bool StartsWithOrdinal(char* source, ulong sourceLength, string value)
        {
            if (sourceLength < (ulong)value.Length) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != source[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the characters to skip at the start of the path if it starts with space(s) and a drive or directory separator.
        /// (examples are " C:", " \")
        /// This is a legacy behavior of Path.GetFullPath().
        /// </summary>
        /// <remarks>
        /// Note that this conflicts with IsPathRooted() which doesn't (and never did) such a skip.
        /// </remarks>
        internal static int PathStartSkip(string path)
        {
            int startIndex = 0;
            while (startIndex < path.Length && path[startIndex] == ' ') 
                startIndex++;

            if (
                startIndex > 0 
                && (startIndex < path.Length 
                && IsDirectorySeparator(path[startIndex]))
                || 
                (
                    startIndex + 1 < path.Length 
                    && path[startIndex + 1] == Path.VolumeSeparatorChar 
                    && IsValidDriveChar(path[startIndex]))
                )
            {
                // Go ahead and skip spaces as we're either " C:" or " \"
                return startIndex;
            }

            return 0;
        }

        /// <summary>
        /// True if the given character is a directory separator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDirectorySeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }

        /// <summary>
        /// Normalize separators in the given path. Converts forward slashes into back slashes and compresses slash runs, keeping initial 2 if present.
        /// Also trims initial whitespace in front of "rooted" paths (see PathStartSkip).
        /// 
        /// This effectively replicates the behavior of the legacy NormalizePath when it was called with fullCheck=false and expandShortpaths=false.
        /// The current NormalizePath gets directory separator normalization from Win32's GetFullPathName(), which will resolve relative paths and as
        /// such can't be used here (and is overkill for our uses).
        /// 
        /// Like the current NormalizePath this will not try and analyze periods/spaces within directory segments.
        /// </summary>
        /// <remarks>
        /// The only callers that used to use Path.Normalize(fullCheck=false) were Path.GetDirectoryName() and Path.GetPathRoot(). Both usages do
        /// not need trimming of trailing whitespace here.
        /// 
        /// GetPathRoot() could technically skip normalizing separators after the second segment- consider as a future optimization.
        /// 
        /// For legacy desktop behavior with ExpandShortPaths:
        ///  - It has no impact on GetPathRoot() so doesn't need consideration.
        ///  - It could impact GetDirectoryName(), but only if the path isn't relative (C:\ or \\Server\Share).
        /// 
        /// In the case of GetDirectoryName() the ExpandShortPaths behavior was undocumented and provided inconsistent results if the path was
        /// fixed/relative. For example: "C:\PROGRA~1\A.TXT" would return "C:\Program Files" while ".\PROGRA~1\A.TXT" would return ".\PROGRA~1". If you
        /// ultimately call GetFullPath() this doesn't matter, but if you don't or have any intermediate string handling could easily be tripped up by
        /// this undocumented behavior.
        /// </remarks>
        internal static string NormalizeDirectorySeparators(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            char current;
            int start = PathStartSkip(path);

            if (start == 0)
            {
                // Make a pass to see if we need to normalize so we can potentially skip allocating
                bool normalized = true;

                for (int i = 0; i < path.Length; i++)
                {
                    current = path[i];
                    if (IsDirectorySeparator(current)
                        && (current != Path.DirectorySeparatorChar
                            // Check for sequential separators past the first position (we need to keep initial two for UNC/extended)
                            || (i > 0 && i + 1 < path.Length && IsDirectorySeparator(path[i + 1]))))
                    {
                        normalized = false;
                        break;
                    }
                }

                if (normalized) return path;
            }

            StringBuilder builder = StringBuilderCache.Acquire(path.Length);

            if (IsDirectorySeparator(path[start]))
            {
                start++;
                builder.Append(Path.DirectorySeparatorChar);
            }

            for (int i = start; i < path.Length; i++)
            {
                current = path[i];

                // If we have a separator
                if (IsDirectorySeparator(current))
                {
                    // If the next is a separator, skip adding this
                    if (i + 1 < path.Length && IsDirectorySeparator(path[i + 1]))
                    {
                        continue;
                    }

                    // Ensure it is the primary separator
                    current = Path.DirectorySeparatorChar;
                }

                builder.Append(current);
            }

            return StringBuilderCache.GetStringAndRelease(builder);
        }

        public static string ReplaceInvalidChars(string path, char replaceInvalidPathCharsWith, bool checkAdditional = true, bool allowDotsInFolderNames = false)  //roland
        {
            //TODO: replace leftmost and rightmost space chars in folder names, filename and extension

            if (path == string.Empty) 
            {
                bool qqq = true; //do nothing  //NB! do not replace with replaceInvalidPathCharsWith character since it might be a path component before Path.Combine 
            }
            else if (string.IsNullOrWhiteSpace(path))   //Google drive does not seem to allow creating such file names at least in UI side
            {
                //path = string.Concat(Enumerable.Repeat(replaceInvalidPathCharsWith, path.Length));
                path = new string(replaceInvalidPathCharsWith, path.Length);
            }
            else
            {
#if false
                path = FolderSync.Extensions.Replace(path, InvalidPathChars, replaceInvalidPathCharsWith);

                if (checkAdditional)
                    path = FolderSync.Extensions.Replace(path, WildCardChars, replaceInvalidPathCharsWith);
#else 
                var originalPath = path;
                int pathStartSkip = GetRootLength(path);
                path = path.Substring(pathStartSkip);

                path = string.Concat(path.Select(x =>
                {
                    //if (!WhiteList.Contains(x))
                    if (InvalidPathChars.Contains(x))
                    {
                        return replaceInvalidPathCharsWith;
                    }
                    else
                    {
                        if (checkAdditional && WildCardChars.Contains(x))
                            return replaceInvalidPathCharsWith;
                        else
                            return x; //.ToString();
                    }
                }));
#endif                 

                //var folder = Path.GetDirectoryName(path);
                var pathComponents = path.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.None);  //NB! not specifying StringSplitOptions.RemoveEmptyEntries
                var pathComponentsChanged = false;
                pathComponents = pathComponents
                    .Select(pathComponent =>
                    {
                        bool isAllowedDotOrTwoDots = allowDotsInFolderNames && (pathComponent == "." || pathComponent == "..");

                        if (!isAllowedDotOrTwoDots)
                        {
                            if (pathComponent == ".")
                            {
                                pathComponentsChanged = true;
                                pathComponent = new string(replaceInvalidPathCharsWith, 1);
                            }
                            else if (pathComponent == "..")
                            {
                                pathComponentsChanged = true;
                                pathComponent = new string(replaceInvalidPathCharsWith, 2);
                            }
                            else if (pathComponent.EndsWith("."))    //GetFileNameWithoutExtension and GetExtension would get confused by such name - both would ignore the last dot, and this is not what we want
                            {
                                pathComponentsChanged = true;
                                pathComponent = pathComponent.Substring(0, pathComponent.Length - 1) + replaceInvalidPathCharsWith;
                            }

                            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(pathComponent);
                            if (ForbiddenFileNames.Contains(filenameWithoutExtension))
                            {
                                pathComponentsChanged = true;
                                var extension = Path.GetExtension(pathComponent);
                                pathComponent = replaceInvalidPathCharsWith + filenameWithoutExtension + extension;
                            }
                        }
                        else   //if (!isAllowedDotOrTwoDots)
                        {
                            bool qqq = true;    //for debugging
                        }

                        return pathComponent;
                    })  //.Select(folderPathComponent =>
                    .ToArray();     //NB! necessary to calculate pathComponentsChanged since .Select() is lazy

                if (pathComponentsChanged)
                {
                    path = string.Join(Path.DirectorySeparatorCharAsString, pathComponents);
                }


                path = originalPath.Substring(0, pathStartSkip) + path;

                if (originalPath != path)
                {
                    bool qqq = true;    //for debugging
                }
            }   //else if (string.IsNullOrWhiteSpace(path))

            //TODO: truncate path components if they are too long

            return path;

        }   //public static string ReplaceInvalidChars(string path, char replaceInvalidPathCharsWith, bool checkAdditional = true, bool allowDotsInFolderNames = false)  
    }
}
