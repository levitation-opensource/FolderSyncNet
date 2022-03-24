//
// Licensed to Roland Pihlakas under one or more agreements.
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE and copyrights.txt files for more information.
//

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace FolderSync
{
    internal static class ConfigParser
    {
        public static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string ToUpperInvariantOnWindows(this string text, bool? caseSensitiveFilenames)
        {
            if (caseSensitiveFilenames == false || (caseSensitiveFilenames == null && !IsLinux))  //Under Windows assume NTFS or FAT filesystem which are usually case-insensitive. Under Mac filesystems are also case-insensitive by default.
                return text?.ToUpperInvariant();
            else
                return text;
        }

        public static long? GetLong(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            foreach (var sectionKeyAlternateName in sectionKeyAlternateNames)
            {
                var text = config[sectionKeyAlternateName];
                if (text != null)
                {
                    //long result;
                    //if (long.TryParse(text, out result))
                    //    return result;
                    return long.Parse(text);    //NB! if the parameter exists then it must be in a proper numeric format
                }
            }

            return null;
        }

        public static string GetTextUpperOnWindows(this IConfiguration config, bool? caseSensitiveFilenames, params string[] sectionKeyAlternateNames)
        {
            if (caseSensitiveFilenames == false || (caseSensitiveFilenames == null && !IsLinux))
                return config.GetTextUpper(sectionKeyAlternateNames);
            else
                return config.GetText(sectionKeyAlternateNames);
        }

        public static string GetTextUpper(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            return config.GetText(sectionKeyAlternateNames)?.ToUpperInvariant();
        }

        public static string GetText(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            foreach (var sectionKeyAlternateName in sectionKeyAlternateNames)
            {
                var text = config[sectionKeyAlternateName];
                if (text != null)
                    return text;
            }

            return null;
        }

        public static List<long> GetLongList(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            foreach (var sectionKeyAlternateName in sectionKeyAlternateNames)
            {
                var list = config.GetSection(sectionKeyAlternateName).GetList();
                if (list.Count > 0)
                {
                    return list
                            .Select(text => long.Parse(text))   //NB! if the parameter exists then it must be in a proper numeric format
                            .ToList();
                }
            }

            return new List<long>();
        }

        public static List<string> GetListUpperOnWindows(this IConfiguration config, bool? caseSensitiveFilenames, params string[] sectionKeyAlternateNames)
        {
            if (caseSensitiveFilenames == false || (caseSensitiveFilenames == null && !IsLinux))
                return config.GetListUpper(sectionKeyAlternateNames);
            else
                return config.GetList(sectionKeyAlternateNames);
        }

        public static List<string> GetListUpper(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            return config.GetList(sectionKeyAlternateNames)
                .Select(x => x?.ToUpperInvariant())
                .ToList();
        }

        public static List<string> GetList(this IConfiguration config, params string[] sectionKeyAlternateNames)
        {
            foreach (var sectionKeyAlternateName in sectionKeyAlternateNames)
            {
                var list = config.GetSection(sectionKeyAlternateName).GetList();
                if (list.Count > 0)
                    return list;
            }

            return new List<string>();
        }

        public static List<string> GetList(this IConfigurationSection config)
        {
            //TODO: detect also missing key and use a default value in such case

            if (config.Value != null)
                return new List<string>() { config.Value };
            else
                return config.GetChildren().Select(c => c.Value).ToList();
        }
    }
}
