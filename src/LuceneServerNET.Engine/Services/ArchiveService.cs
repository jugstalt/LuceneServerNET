using LuceneServerNET.Core.Models.Mapping;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LuceneServerNET.Engine.Services
{
    public class ArchiveService
    {
        private readonly string _archivePath;

        public ArchiveService(IOptionsMonitor<LuceneServiceOptions> options)
        {
            _archivePath = options.CurrentValue.ArchivePath;
        }

        #region Archives

        public bool ArchiveExists(string indexName)
        {
            if (!String.IsNullOrEmpty(_archivePath))
            {
                return new DirectoryInfo(ArchivePath(indexName)).Exists;
            }

            return false;
        }

        public bool CreateArchive(string indexName)
        {
            try
            {
                if (!ArchiveExists(indexName))
                {
                    var archivePath = ArchivePath(indexName);
                    var archiveMetaPath = ArchiveMetaPath(indexName);

                    new DirectoryInfo(archivePath).Create();
                    new DirectoryInfo(archiveMetaPath).Create();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveArchive(string indexName)
        {
            try
            {
                if (!String.IsNullOrEmpty(_archivePath))
                {
                    var archiveDirectory = new DirectoryInfo(ArchivePath(indexName));
                    var archiveMetaDirectory = new DirectoryInfo(ArchiveMetaPath(indexName));

                    if (archiveDirectory.Exists)
                    {
                        archiveDirectory.Delete(true);
                    }

                    if (archiveMetaDirectory.Exists)
                    {
                        archiveMetaDirectory.Delete(true);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public IEnumerable<string> GetArchivedIndexNames()
        {
            if(!String.IsNullOrEmpty(_archivePath))
            {
                var rootDirectoryInfo = new DirectoryInfo(_archivePath);

                List<string> names = new List<string>();
                
                foreach(var di in rootDirectoryInfo.GetDirectories()
                                                   .Where(d=>!d.Name.StartsWith(".")))
                {
                    var indexName = di.Name;
                    var metaDirectoryInfo = new DirectoryInfo(ArchiveMetaPath(indexName));

                    if(metaDirectoryInfo.Exists)
                    {
                        names.Add(indexName);
                    }
                }

                return names;
            }

            return new string[0];
        }

        #endregion

        #region Metadata

        #region Mapping

        public bool Map(string indexName, IndexMapping mapping)
        {
            try
            {
                if (ArchiveExists(indexName))
                {
                    var filePath = Path.Combine(ArchiveMetaPath(indexName), "mapping.json");

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        fileInfo.Delete();
                    }

                    if (fileInfo.Directory.Exists == false)
                    {
                        fileInfo.Directory.Create();
                    }

                    File.WriteAllText(
                        filePath,
                        JsonSerializer.Serialize(mapping));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public IndexMapping Mapping(string indexName)
        {
            try
            {
                if(ArchiveExists(indexName))
                {
                    var filePath = Path.Combine(ArchiveMetaPath(indexName), "mapping.json");
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        return JsonSerializer.Deserialize<IndexMapping>(File.ReadAllText(filePath));
                    }
                }
            }
            catch
            {
               
            }

            return null;
        }

        #endregion

        #region Custom

        public bool AddCustomMetadata(string indexName, string name, string metaData)
        {
            try
            {
                if (ArchiveExists(indexName))
                {
                    var filePath = Path.Combine(ArchiveMetaPath(indexName), $"{ name }.meta");

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        fileInfo.Delete();
                    }

                    if (fileInfo.Directory.Exists == false)
                    {
                        fileInfo.Directory.Create();
                    }

                    File.WriteAllText(
                        filePath,
                        metaData);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetCustomMetadata(string indexName, string name)
        {
            try
            {
                if (ArchiveExists(indexName))
                {
                    name = name?.Trim().ToLower();

                    if (String.IsNullOrEmpty(name))
                        return null;

                    var filePath = Path.Combine(ArchiveMetaPath(indexName), $"{ name }.meta");

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        return File.ReadAllText(fileInfo.FullName);
                    }

                }
            }
            catch { }

            return null;
        }

        public IEnumerable<string> GetCustomMetadataNames(string indexName)
        {
            try
            {
                if (ArchiveExists(indexName))
                {
                    var metaDirectory = new DirectoryInfo(ArchiveMetaPath(indexName));

                    return metaDirectory.GetFiles("*.meta")
                                        .Select(f => f.Name.Substring(0, f.Name.Length - ".meta".Length));
                }
            }
            catch { }

            return new string[0];
        }

        #endregion

        #endregion

        #region Index

        public bool Index(string indexName, IEnumerable<IDictionary<string, object>> items)
        {
            if (ArchiveExists(indexName))
            {
                if (items == null || items.Count() == 0)
                {
                    return true;
                }

                string archivePath = ArchivePath(indexName);

                foreach(var item in items)
                {
                    try
                    {
                        string guid = null;
                        if (item.ContainsKey("_guid"))
                        {
                            guid = item["_guid"]?.ToString();
                        }
                        if (String.IsNullOrEmpty(guid))
                        {
                            guid = Guid.NewGuid().ToString();
                        }

                        FileInfo fi = new FileInfo(Path.Combine(archivePath, $"{ guid.ToLower() }.json"));
                        if (fi.Exists)
                        {
                            fi.Delete();
                        }

                        File.WriteAllText(
                            fi.FullName,
                            JsonSerializer.Serialize(item));
                    }
                    catch { }
                }
            }
            return true;
        }

        #endregion

        #region Items

        public IDictionary<string,object> GetItem(string indexName, string guid)
        {
            try
            {
                if (ArchiveExists(indexName))
                {
                    var fi = new FileInfo(Path.Combine(ArchivePath(indexName), $"{ guid.ToLower()}.json"));
                    if (fi.Exists)
                    {
                        return JsonSerializer.Deserialize<Dictionary<string, object>>(
                            File.ReadAllText(fi.FullName));
                    }
                }
            }
            catch
            {

            }

            return null;
        }

        public IEnumerable<IDictionary<string, object>> GetLastItems(string indexName, int count)
        {
            if(ArchiveExists(indexName))
            {
                var di = new DirectoryInfo(ArchivePath(indexName));

                List<IDictionary<string, object>> items = new List<IDictionary<string, object>>();

                foreach(var fi in di.GetFiles("*.json").OrderByDescending(f=>f.CreationTime)
                                                       .Take(count))
                {
                    try
                    {
                        items.Add(JsonSerializer.Deserialize<Dictionary<string, object>>(
                            File.ReadAllText(fi.FullName)));
                    }
                    catch
                    {

                    }
                }

                return items;
            }
            return null;
        }

        public IEnumerable<IDictionary<string, object>> GetItemsSince(string indexName, DateTime since)
        {
            if (ArchiveExists(indexName))
            {
                var di = new DirectoryInfo(ArchivePath(indexName));

                List<IDictionary<string, object>> items = new List<IDictionary<string, object>>();

                foreach (var fi in di.GetFiles("*.json")
                                     .Where(f => f.CreationTimeUtc >= since)) 
                {
                    try
                    {
                        items.Add(JsonSerializer.Deserialize<Dictionary<string, object>>(
                            File.ReadAllText(fi.FullName)));
                    }
                    catch
                    {

                    }
                }

                return items;
            }
            return null;
        }

        public IEnumerable<IDictionary<string, object>> GetAllItems(string indexName)
        {
            if (ArchiveExists(indexName))
            {
                var di = new DirectoryInfo(ArchivePath(indexName));

                foreach (var fi in di.GetFiles("*.json"))
                {
                    Dictionary<string, object> item = null;
                    try
                    {
                        item = (JsonSerializer.Deserialize<Dictionary<string, object>>(
                            File.ReadAllText(fi.FullName)));
                    }
                    catch
                    {

                    }

                    if(item!=null)
                    {
                        yield return item;
                    }
                }

            }

            yield return null;
        }

        #endregion

        #region Helper

        private string ArchivePath(string indexName) => String.IsNullOrEmpty(_archivePath) ? null : Path.Combine(_archivePath, indexName);
        private string ArchiveMetaPath(string indexName)=> String.IsNullOrEmpty(_archivePath) ? null : Path.Combine(_archivePath, MetaIndexName(indexName));

        private string MetaIndexName(string indexName)
        {
            return $".{ indexName }";
        }

        #endregion
    }
}
