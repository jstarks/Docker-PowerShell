﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tar
{
    public static class TarWriterExtensions
    {
        private static async Task CreateEntryFromFileInfoAsync(this TarWriter writer, FileInfo fi, string entryName)
        {
            var entry = new TarEntry
            {
                Name = entryName.Replace('\\', '/'),
                AccessTime = fi.LastAccessTimeUtc,
                ModifiedTime = fi.LastWriteTimeUtc,
                Type = TarEntryType.File,
                Mode = Convert.ToInt32("644", 8),
            };

            if (fi.Attributes.HasFlag(FileAttributes.Directory))
            {
                entry.Type = TarEntryType.Directory;
                entry.Mode = Convert.ToInt32("755", 8);
            }
            else
            {
                entry.Length = fi.Length;
            }

            if (fi.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Mode &= ~Convert.ToInt32("222", 8);
            }

            await writer.CreateEntryAsync(entry);

            if (!fi.Attributes.HasFlag(FileAttributes.Directory))
            {
                using (var file = fi.OpenRead())
                {
                    await file.CopyToAsync(writer.CurrentFile);
                }
            }
        }

        public static Task CreateEntryFromFileAsync(this TarWriter writer, string path, string entryName)
        {
            var fi = new FileInfo(path);
            return writer.CreateEntryFromFileInfoAsync(fi, entryName);
        }

        public static async Task CreateEntriesFromDirectoryAsync(this TarWriter writer, string path, string entryBase)
        {
            var stack = new List<IEnumerator<string>>();
            IEnumerator<string> enumerator = null;
            try
            {
                enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                while (enumerator != null)
                {
                    while (enumerator.MoveNext())
                    {
                        var filePath = enumerator.Current;
                        var fileName = filePath.Substring(path.Length + 1);
                        var fi = new FileInfo(filePath);
                        await writer.CreateEntryFromFileInfoAsync(fi, Path.Combine(entryBase, fileName));
                        if (fi.Attributes.HasFlag(FileAttributes.Directory))
                        {
                            stack.Add(enumerator);
                            enumerator = Directory.EnumerateFileSystemEntries(filePath).GetEnumerator();
                        }
                    }

                    enumerator.Dispose();
                    enumerator = null;
                    if (stack.Count > 0)
                    {
                        enumerator = stack.Last();
                        stack.RemoveAt(stack.Count - 1);
                    }
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    enumerator.Dispose();
                }

                foreach (var e in stack)
                {
                    e.Dispose();
                }
            }
        }

        public static async Task ExtractDirectory(this TarReader reader, string basePath)
        {
            for (;;)
            {
                var entry = await reader.GetNextEntryAsync();
                if (entry == null)
                {
                    break;
                }

                var path = Path.Combine(basePath, entry.Name);

                switch (entry.Type)
                {
                    default: // Don't know how to handle these. Pretend they are files.
                    case TarEntryType.File:
                        using (var file = File.OpenWrite(path))
                        {
                            await reader.CurrentFile.CopyToAsync(file);
                        }
                        File.SetLastWriteTimeUtc(path, entry.ModifiedTime);
                        break;

                    case TarEntryType.Directory:
                        Directory.CreateDirectory(path);
                        Directory.SetLastWriteTimeUtc(path, entry.ModifiedTime);
                        break;
                }

            }
        }
    }
}
