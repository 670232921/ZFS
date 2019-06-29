using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DokanNet;
using System.IO;
using System.Security.AccessControl;
using DokanNet.Logging;

namespace ZFS
{
    class Program
    {
        static void Main(string[] args)
        {
            IArchive archive = ArchiveFactory.Open(@"C:\Users\User\Desktop\1\1.7z");
            //Console.WriteLine(archive.TotalSize);
            //Console.WriteLine(archive.TotalUncompressSize);
            //foreach (var e in archive.Entries)
            //{
            //    Console.WriteLine(e.Key);
            //    Console.WriteLine(e.Size);
            //    Console.WriteLine(e.CompressedSize);
            //}
            ZFS zFS = new ZFS(archive);

            zFS.Mount("K", DokanOptions.WriteProtection, new Log());
            Console.WriteLine("done");
            Console.Read();
        }
    }

    class Log : ILogger
    {
        public void Debug(string message, params object[] args)
        {

        }

        public void Error(string message, params object[] args)
        {

        }

        public void Fatal(string message, params object[] args)
        {

        }

        public void Info(string message, params object[] args)
        {

        }

        public void Warn(string message, params object[] args)
        {

        }
    }

    class MyEntry
    {
        public MyEntry(IArchiveEntry archiveEntry)
        {
            Child = new List<MyEntry>();
            if (archiveEntry == null)
            {
                Key = string.Empty;
                Path = "\\";
                Name = string.Empty;
                ParentPath = string.Empty;
                IAE = null;
                FI = new FileInformation()
                {
                    Attributes = FileAttributes.Directory,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = null,
                    CreationTime = null,
                    FileName = string.Empty,
                    Length = 0
                };
            }
            else
            {
                IAE = archiveEntry;
                Key = IAE.Key;
                Path = "\\" + Key.Replace('/', '\\');
                ParentPath = Path.Substring(0, Path.LastIndexOf('\\'));
                Name = Path.Substring(ParentPath.Length + 1);
                if (string.IsNullOrWhiteSpace(ParentPath))
                {
                    ParentPath = "\\";
                }
                FI = new FileInformation()
                {
                    Attributes = IAE.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    LastAccessTime = IAE.LastAccessedTime,
                    LastWriteTime = IAE.LastModifiedTime,
                    CreationTime = IAE.CreatedTime,
                    FileName = Name,
                    Length = IAE.Size
                };
            }
        }
        public IArchiveEntry IAE { get; private set; }
        public FileInformation FI { get; private set; }
        public List<MyEntry> Child { get; private set; }
        public string Key { get; private set; }
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string ParentPath { get; private set; }
        public void Read(byte[] buffer, long offset, out int readed)
        {
            readed = 0;
            if (offset >= IAE.Size)
            {
                return;
            }
            lock (cache)
            {
                if (data == null)
                {
                    data = IAE.OpenEntryStream();
                }
            }
            long wanted = buffer.LongLength > IAE.Size - offset ? IAE.Size - offset : buffer.LongLength;
            if (offset + buffer.LongLength > cachedSize && cachedSize != IAE.Size)
            {
                lock (cache)
                {
                    byte[] t = new byte[wanted + offset - cachedSize];
                    data.Read(t, 0, t.Length);
                    cachedSize += t.Length;
                    cache.Add(t);
                }
            }
            long to = offset;
            readed = (int)wanted;
            foreach (var b in cache)
            {
                if (b.LongLength <= to)
                {
                    to -= b.LongLength;
                    continue;
                }
                long currentWanted = b.LongLength - to > wanted ? wanted : b.LongLength - to;
                Array.Copy(b, to, buffer, readed - wanted, currentWanted);
                wanted -= currentWanted;
                to = 0;
                if (wanted <= 0)
                {
                    break;
                }
            }
        }
        private Stream data;
        private List<byte[]> cache = new List<byte[]>();
        private long cachedSize = 0;
    }

    class ZFS : IDokanOperations
    {
        private IArchive archive;
        private List<MyEntry> list = new List<MyEntry>();
        private MyEntry root;

        public ZFS(IArchive a)
        {
            archive = a;
            root = new MyEntry(null);
            List<MyEntry> folders = new List<MyEntry>();
            list.Add(root);
            folders.Add(root);

            foreach (var e in archive.Entries)
            {
                var m = new MyEntry(e);
                list.Add(m);
                if (m.IAE.IsDirectory)
                {
                    folders.Add(m);
                }
            }
            foreach (var e in list)
            {
                var i = folders.FindIndex(f => f.Path == e.ParentPath);
                if (i >= 0)
                {
                    folders[i].Child.Add(e);
                }
            }
        }

        bool Find(string path, out MyEntry myEntry)
        {
            myEntry = null;
            var d = list.FindIndex(i => i.Path == path);
            if (d == -1)
            {
                return false;
            }
            myEntry = list[d];
            return true;
        }

        MyEntry Find(string path)
        {
            if (Find(path, out MyEntry e))
            {
                return e;
            }
            return null;
        }
        //private FileInformation EtoF(IArchiveEntry e)
        //{
        //    return new FileInformation()
        //    {
        //        FileName = e.Key.Substring(e.Key.LastIndexOf('/') + 1),
        //        Attributes = e.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
        //        CreationTime = e.CreatedTime,
        //        LastAccessTime = DateTime.Now,
        //        Length = e.Size
        //    };
        //}

        //private IArchiveEntry FindE(string name)
        //{
        //    if (name.StartsWith("\\"))
        //    {
        //        name = name.Substring(1);
        //    }
        //    name = name.Replace('\\', '/');
        //    foreach(var e in archive.Entries)
        //    {
        //        if (e.Key == name)
        //        {
        //            return e;
        //        }
        //    }
        //    return null;
        //}

        public void Cleanup(string fileName, DokanFileInfo info)
        {
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
        }

        public NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            if (/*mode == FileMode.Open && */Find(fileName) != null)
            {
                return NtStatus.Success;
            }
            else
            {
                return NtStatus.Error;
            }
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            files = null;
            if (Find(fileName, out MyEntry myEntry))
            {
                files = myEntry.Child.ConvertAll(e => e.FI);
                return NtStatus.Success;
            }
            return NtStatus.Error;
            //if (fileName == "\\")
            //{
            //    files = new List<FileInformation>(archive.Entries.Where(e => !(e.Key.Contains('/') || e.Key.Contains('\\'))).Select(e => EtoF(e)));
            //    return NtStatus.Success;
            //}
            //fileName = fileName.Substring(1).Replace('\\', '/');
            //var list = archive.Entries.Where(e => 
            //e.Key.StartsWith(fileName) &&
            //e.Key != fileName &&
            //!e.Key.Substring(fileName.Length + 1).Contains('/')).Select(e => EtoF(e));
            //files = new List<FileInformation>(list);
            //return NtStatus.Success;
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, DokanFileInfo info)
        {
            files = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, DokanFileInfo info)
        {
            freeBytesAvailable = 512 * 1024 * 1024;
            totalNumberOfBytes = 1024 * 1024 * 1024;
            totalNumberOfFreeBytes = 512 * 1024 * 1024;
            return DokanResult.Success;
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            fileInfo = default(FileInformation);
            if (Find(fileName, out MyEntry myEntry))
            {
                fileInfo = myEntry.FI;
                return NtStatus.Success;
            }
            return NtStatus.Error;
            //fileInfo = new FileInformation { FileName = fileName };
            //if (fileName == "\\")
            //{
            //    fileInfo.Attributes = FileAttributes.Directory;
            //    fileInfo.LastAccessTime = DateTime.Now;
            //    fileInfo.LastWriteTime = null;
            //    fileInfo.CreationTime = null;

            //    return DokanResult.Success;
            //}

            //try
            //{
            //    fileName = fileName.Substring(1).Replace('\\', '/');
            //    fileInfo = EtoF(archive.Entries.First(e => e.Key == fileName));
            //    return NtStatus.Success;
            //}
            //catch (Exception e)
            //{
            //    return NtStatus.Error;
            //}
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            security = null;
            return DokanResult.Error;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, DokanFileInfo info)
        {
            volumeLabel = "ZFS";
            features = FileSystemFeatures.ReadOnlyVolume;
            fileSystemName = string.Empty;
            maximumComponentLength = 256;
            return DokanResult.Error;
        }

        public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Mounted(DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            if (Find(fileName, out MyEntry myEntry) && !myEntry.IAE.IsDirectory)
            {
                myEntry.Read(buffer, offset, out bytesRead);
                return NtStatus.Success;
            }
            //if (info.Context != null)
            //{
            //    var s = info.Context as Stream;
            //    bytesRead = s.Read(buffer, (int)offset, (int)buffer.Length);
            //    return NtStatus.Success;
            //}
            //else
            //{
            //    var e = FindE(fileName);
            //    if (e != null)
            //    {
            //        var s = e.OpenEntryStream();
            //        bytesRead = s.Read(buffer, (int)offset, (int)buffer.Length);
            //        return NtStatus.Success;
            //    }
            //}
            bytesRead = 0;
            return DokanResult.Error;
            //todo
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Unmounted(DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            bytesWritten = 0;
            return DokanResult.Error;
        }
    }
}
