using Microsoft.Extensions.Options;
using System;
using System.Collections;
using System.Collections.Generic;

namespace LuceneServerNET.Services
{
    public class RestoreService
    {
        private readonly RestoreServiceOptions _options;
        private readonly ArchiveService _archive;
        private readonly LuceneService _lucene;

        public RestoreService(ArchiveService archive,
                              LuceneService lucene,
                              IOptionsMonitor<RestoreServiceOptions> options)
        {
            _archive = archive;
            _lucene = lucene;
            _options = options.CurrentValue;
        }

        public void TryRestoreIndices()
        {
            if (!_options.IsRestoreDesired())
                return;

            try
            {
                foreach (var indexName in _archive.GetArchivedIndexNames())
                {
                    try
                    {
                        if (!_lucene.IndexExists(indexName))
                        {
                            var mapping = _archive.Mapping(indexName);
                            if (mapping == null)
                            {
                                continue;
                            }

                            IEnumerable<IDictionary<string, object>> items = null;
                            if (_options.RestoreOnRestartCount > 0)
                            {
                                items = _archive.GetLastItems(indexName, _options.RestoreOnRestartCount);
                            }
                            else if (_options.RestoreOnRestartSince > 0)
                            {
                                items = _archive.GetItemsSince(indexName, DateTime.UtcNow.AddSeconds(-_options.RestoreOnRestartSince));
                            }
                            else
                            {
                                items = _archive.GetAllItems(indexName);
                            }

                            if (items != null)
                            {
                                if(_lucene.CreateIndex(indexName))
                                {
                                    if(_lucene.Map(indexName, mapping))
                                    {
                                        #region Custom Metadata

                                        foreach(var customMetaName in _archive.GetCustomMetadataNames(indexName))
                                        {
                                            var metaData = _archive.GetCustomMetadata(indexName, customMetaName);
                                            _lucene.AddCustomMetadata(indexName, customMetaName, metaData);
                                        }

                                        #endregion

                                        _lucene.Index(indexName, items, archive: false);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
