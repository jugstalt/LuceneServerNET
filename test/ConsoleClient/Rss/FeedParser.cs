using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ConsoleClient.Rss
{
    public class FeedParser
    {
        // reuse httpClient;
        private static HttpClient _httpClient = new HttpClient();

        async public Task<IList<Item>> Parse(string url,
                                             FeedType feedType,
                                             int timeOut = 0)
        {
            //using (var httpClient = new HttpClient())
            if (timeOut > 0)
                _httpClient.Timeout = new TimeSpan(0, 0, timeOut);

            using (var response = await _httpClient.GetAsync(url))
            {
                var rss = await response.Content.ReadAsStringAsync();

                //XmlDocument xmlDocument = new XmlDocument();
                //XmlNamespaceManager ns = new XmlNamespaceManager(xmlDocument.NameTable);
                //ns.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                //xmlDocument.LoadXml(rss);

                XDocument doc = XDocument.Parse(RepairXml(rss));

                if (feedType == FeedType.Unknown)
                    feedType = DetermineFeedType(doc);

                switch (feedType)
                {
                    case FeedType.RSS:
                        return ParseRss(doc);
                    case FeedType.RDF:
                        return ParseRdf(doc);
                    case FeedType.Atom:
                        return ParseAtom(doc);
                    default:
                        return null;
                }
            }
        }

        private FeedType DetermineFeedType(XDocument doc)
        {
            if (doc.Root.Elements().Where(i => i.Name.LocalName == "entry").Count() > 0)
                return FeedType.Atom;
            if (doc.Root.Descendants().FirstOrDefault(i => i.Name.LocalName == "channel") != null &&
                doc.Root.Descendants().FirstOrDefault(i => i.Name.LocalName == "channel").Elements().FirstOrDefault(i => i.Name.LocalName == "item") != null)
                return FeedType.RSS;
            if (doc.Root.Descendants().Where(i => i.Name.LocalName == "item").FirstOrDefault() != null)
                return FeedType.RDF;

            return FeedType.Unknown;
        }

        private IList<Item> ParseAtom(XDocument doc)
        {
            try
            {
                // Feed/Entry
                var entries = doc.Root.Elements().Where(i => i.Name.LocalName == "entry")
                              .Select(item => new Item
                              {
                                  FeedType = FeedType.Atom,
                                  Content = item.Elements().FirstOrDefault(i => i.Name.LocalName == "content")?.Value,
                                  Link = item.Elements().FirstOrDefault(i => i.Name.LocalName == "link")?.Attribute("href")?.Value,
                                  PublishDate = ParseDate(item.Elements().FirstOrDefault(i => i.Name.LocalName == "published")?.Value),
                                  Title = item.Elements().FirstOrDefault(i => i.Name.LocalName == "title")?.Value
                              });
                return entries.ToList();
            }
            catch
            {
                return new List<Item>();
            }
        }

        private IList<Item> ParseRss(XDocument doc)
        {
            try
            {
                // RSS/Channel/Header
                var channel = doc.Root.Descendants().First(i => i.Name.LocalName == "channel");
                var channelItem = new Item()
                {
                    IsFeedHeaderItem = true,
                    FeedType = FeedType.RSS,
                    Content = channel.Elements().FirstOrDefault(i => i.Name.LocalName == "description")?.Value
                           + "<br/>" + channel.Elements().FirstOrDefault(i => i.Name.LocalName == "copyright")?.Value,
                    Link = channel.Elements().FirstOrDefault(i => i.Name.LocalName == "link")?.Value,
                    PublishDate = ParseDate(channel.Elements().FirstOrDefault(i => i.Name.LocalName == "lastBuildDate")?.Value),
                    Title = channel.Elements().FirstOrDefault(i => i.Name.LocalName == "title")?.Value
                };
                var image = channel.Elements().FirstOrDefault(i => i.Name.LocalName == "image");
                if (image != null && image.Elements().FirstOrDefault(i => i.Name.LocalName == "url") != null)
                {
                    SetItemMedia(channelItem, new MediaImage[]
                    {
                        new MediaImage()
                        {
                            Url=image.Elements().First(i => i.Name.LocalName == "url").Value
                        }
                    });
                }

                List<Item> result = new List<Item>();
                result.Add(channelItem);

                // RSS/Channel/item
                var entries = channel.Elements().Where(i => i.Name.LocalName == "item")
                                .Select(item => {
                                    var rssItem = new Item
                                    {
                                        FeedType = FeedType.RSS,
                                        Content = item.Elements().FirstOrDefault(i => i.Name.LocalName == "description")?.Value,
                                        Link = item.Elements().FirstOrDefault(i => i.Name.LocalName == "link")?.Value,
                                        PublishDate = ParseDate(item.Elements().FirstOrDefault(i => i.Name.LocalName == "pubDate")?.Value),
                                        Title = item.Elements().FirstOrDefault(i => i.Name.LocalName == "title")?.Value
                                    };

                                    #region GeoRss Point

                                    var pointElement = item.Elements().Where(i =>
                                        i.Name.ToString() == "{http://www.georss.org/georss}point" ||
                                        i.Name.ToString() == "{http://www.georss.org /georss/}point").FirstOrDefault();
                                    if(pointElement!=null)
                                    {
                                        var coords = pointElement.Value.Split(' ');
                                        if(coords.Length==2)
                                        {
                                            rssItem.Latitude = coords[0].ToDouble();
                                            rssItem.Longitude = coords[1].ToDouble();
                                        }
                                    }

                                    #endregion

                                    List<MediaImage> mediaImages = new List<MediaImage>();

                                    #region Media eg CNN RssFeed

                                    var yahooMediaElement = item.Elements().Where(i =>
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss}group" ||
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss/}group").FirstOrDefault();
                                    var yahooMediaThumbnails = item.Elements().Where(i =>
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss}thumbnail" ||
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss/}thumbnail").ToArray();
                                    var yahooMediaContent=item.Elements().Where(i=>
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss/}content" ||
                                        i.Name.ToString() == "{http://search.yahoo.com/mrss/}content").ToArray();

                                    if (yahooMediaElement != null)
                                    {
                                        mediaImages.AddRange(yahooMediaElement.Elements().Where(i => i.Name.LocalName == "content")
                                            .Select(c =>
                                            {
                                                if ((c.Attribute("medium")?.Value == "image" ||
                                                     c.Attribute("type")?.Value == "image/jpeg" ||
                                                     c.Attribute("type")?.Value == "image/jpg" ||
                                                     c.Attribute("type")?.Value == "image.png")
                                                            && c.Attribute("url") != null)
                                                {
                                                    int width = 0, height = 0;
                                                    int.TryParse(c.Attribute("width")?.Value, out width);
                                                    int.TryParse(c.Attribute("height")?.Value, out height);

                                                    var copyright = c.Elements().Where(i => i.Name.LocalName == "copyright").FirstOrDefault();
                                                    var description = c.Elements().Where(i => i.Name.LocalName == "description").FirstOrDefault();

                                                    return new MediaImage()
                                                    {
                                                        Url = c.Attribute("url").Value,
                                                        Width = width,
                                                        Height = height,
                                                        Copyright = copyright?.Value,
                                                        Description = description?.Value
                                                    };
                                                }

                                                return null;
                                            }));
                                    }
                                    if(yahooMediaThumbnails!=null && yahooMediaThumbnails.Length>0)
                                    {
                                        mediaImages.AddRange(yahooMediaThumbnails.Select(
                                            c =>
                                            {
                                                if (c.Attribute("url") != null)
                                                {
                                                    int width = 0, height = 0;
                                                    int.TryParse(c.Attribute("width")?.Value, out width);
                                                    int.TryParse(c.Attribute("height")?.Value, out height);

                                                    var copyright = c.Elements().Where(i => i.Name.LocalName == "copyright").FirstOrDefault();
                                                    var description = c.Elements().Where(i => i.Name.LocalName == "description").FirstOrDefault();

                                                    return new MediaImage()
                                                    {
                                                        Url = c.Attribute("url").Value,
                                                        Width = width,
                                                        Height = height,
                                                        Copyright = copyright?.Value,
                                                        Description = description?.Value
                                                    };
                                                }

                                                return null;
                                            }
                                            ));
                                    }
                                    if(yahooMediaContent!=null && yahooMediaContent.Length>0)
                                    {
                                        var mediaElement = yahooMediaContent.Where(c => c.Attribute("small")?.Value == "image" && c.Attribute("url") != null).FirstOrDefault();
                                        if (mediaElement == null)
                                            mediaElement = yahooMediaContent.Where(c => c.Attribute("medium")?.Value == "image" && c.Attribute("url") != null).FirstOrDefault();
                                        if (mediaElement == null)
                                            mediaElement = yahooMediaContent.Where(c => c.Attribute("url") != null).FirstOrDefault();

                                        if(mediaElement!=null)
                                        {
                                            int width = 0, height = 0;
                                            int.TryParse(mediaElement.Attribute("width")?.Value, out width);
                                            int.TryParse(mediaElement.Attribute("height")?.Value, out height);

                                            var copyright = mediaElement.Elements().Where(i => i.Name.LocalName == "copyright").FirstOrDefault();
                                            var description = mediaElement.Elements().Where(i => i.Name.LocalName == "description").FirstOrDefault();

                                            mediaImages.Add(new MediaImage()
                                            {
                                                Url = mediaElement.Attribute("url").Value,
                                                Width = width,
                                                Height = height,
                                                Copyright = copyright?.Value,
                                                Description = description?.Value
                                            });
                                        }
                                        //mediaImages.AddRange(yahooMediaContent
                                        //    .Select(c =>
                                        //     {
                                        //         if (c.Attribute("medium")?.Value == "image" && 
                                        //             c.Attribute("url") != null)
                                        //         {
                                        //             int width = 0, height = 0;
                                        //             int.TryParse(c.Attribute("width")?.Value, out width);
                                        //             int.TryParse(c.Attribute("height")?.Value, out height);

                                        //             var copyright = c.Elements().Where(i => i.Name.LocalName == "copyright").FirstOrDefault();
                                        //             var description = c.Elements().Where(i => i.Name.LocalName == "description").FirstOrDefault();

                                        //             return new MediaImage()
                                        //             {
                                        //                 Url = c.Attribute("url").Value,
                                        //                 Width = width,
                                        //                 Height = height,
                                        //                 Copyright = copyright?.Value,
                                        //                 Description = description?.Value
                                        //             };
                                        //         }
                                        //         return null;
                                        //     }));
                                    }

                                    #endregion

                                    #region enclosure 

                                    if(mediaImages.Count==0)
                                    {
                                        var enclosure = item.Elements().Where(i => i.Name.ToString() == "enclosure"
                                            && !String.IsNullOrWhiteSpace(i.Attribute("url")?.Value)).FirstOrDefault();
                                        if (enclosure != null)
                                            mediaImages.Add(new MediaImage()
                                            {
                                                Url = enclosure.Attribute("url").Value
                                            });
                                    }

                                    #endregion

                                    if (mediaImages.Count > 0)
                                        SetItemMedia(rssItem, mediaImages);

                                    return rssItem;
                                });

                result.AddRange(entries);
                return result;
            }
            catch(Exception ex)
            {
                string message = ex.Message;
                return new List<Item>();
            }
        }

        private IList<Item> ParseRdf(XDocument doc)
        {
            try
            {
                List<Item> result = new List<Item>();
                var root = doc.Root;

                var headerItem = new Item
                {
                    FeedType = FeedType.RDF,
                    IsFeedHeaderItem = true,
                    Content = root.Descendants().FirstOrDefault(i => i.Name.LocalName == "description")?.Value +
                              root.Descendants().FirstOrDefault(i => i.Name.LocalName == "publisher")?.Value +
                              root.Descendants().FirstOrDefault(i => i.Name.LocalName == "creator")?.Value +
                              root.Descendants().FirstOrDefault(i => i.Name.LocalName == "rights")?.Value,
                    Link = root.Descendants().FirstOrDefault(i => i.Name.LocalName == "link")?.Value,
                    PublishDate = ParseDate(root.Descendants().FirstOrDefault(i => i.Name.LocalName == "date")?.Value),
                    Title = root.Descendants().FirstOrDefault(i => i.Name.LocalName == "title")?.Value,
                    Subject = root.Descendants().FirstOrDefault(i => i.Name.LocalName == "subject")?.Value
                };
                if (!String.IsNullOrWhiteSpace(headerItem.Title) || !String.IsNullOrWhiteSpace(headerItem.Content))
                    result.Add(headerItem);

                // <item> is under the root
                var entries = doc.Root.Descendants().Where(i => i.Name.LocalName == "item")
                        .Select(item => new Item
                        {
                            FeedType = FeedType.RDF,
                            Content = item.Elements().FirstOrDefault(i => i.Name.LocalName == "description")?.Value,
                            Link = item.Elements().FirstOrDefault(i => i.Name.LocalName == "link")?.Value,
                            PublishDate = ParseDate(item.Elements().FirstOrDefault(i => i.Name.LocalName == "date")?.Value),
                            Title = item.Elements().FirstOrDefault(i => i.Name.LocalName == "title")?.Value,
                            Subject = item.Elements().FirstOrDefault(i => i.Name.LocalName == "subject")?.Value
                        });

                result.AddRange(entries);
                return result;
            }
            catch
            {
                return new List<Item>();
            }
        }

        private DateTime? ParseDate(string date)
        {
            DateTime result;
            if (DateTime.TryParse(date, out result))
                return result.ToUniversalTime();
            else
                return null;
        }

        private string RepairXml(string xml)
        {
            string[] umlaute = new string[] { "&Auml;", "&Ouml;", "&Uuml;", "&szlig;", "&iacute;", "&jacute;", "&aacute;", "&rlm;" };

            foreach (var umlaut in umlaute)
            {
                for (int i = 0; i < 2; i++)
                {
                    var u = i % 2 == 0 ? umlaut : umlaut.ToLower();
                    if (xml.Contains(u))
                    {
                        xml = xml.Replace(u, u.Replace("&", "&amp;"));
                    }
                }
            }

            return xml;
        }

        #region Media

        private void SetItemMedia(Item item, IEnumerable<MediaImage> mediaImages)
        {
            item.MediaImageLarge = GetBestFittingMediaImage(mediaImages, 300, 300);
            item.MediaImageWide = GetBestFittingMediaImage(mediaImages, 300, 150);
            item.MediaImageMedium = GetBestFittingMediaImage(mediaImages, 150, 150);
            item.MediaImageSmall = GetBestFittingMediaImage(mediaImages, 75, 75);

            item.MediaCopyright = mediaImages?.Where(m=>m!=null)
                                              .Where(m => !String.IsNullOrWhiteSpace(m.Copyright)).FirstOrDefault()?.Copyright;
            item.MediaDescription = mediaImages?.Where(m=>m!=null) 
                                                .Where(m => !String.IsNullOrWhiteSpace(m.Description)).FirstOrDefault()?.Description;
        }

        private string GetBestFittingMediaImage(IEnumerable<MediaImage> mediaImages, int width,int height)
        {
            if (mediaImages == null)
                return null;

            string url = mediaImages.Where(i => i != null)
                .Select(i => new MediaUrl()
                {
                    Url = i.Url,
                    FittingFactor = Math.Abs(height - i.Height) * i.Width + Math.Abs(width - i.Width) * i.Height
                })
                .OrderBy(i => i.FittingFactor).FirstOrDefault()?.Url;

            return url;
        }

        #endregion

        #region Classes

        private class MediaImage
        {
            public string Url { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Copyright { get; set; }
            public string Description { get; set; }
        }

        private class MediaUrl
        {
            public string Url { get; set; }
            public double FittingFactor { get; set; }
        }

        #endregion
    }

    public enum FeedType
    {
        RSS,
        RDF,
        Atom,
        Unknown
    }
}
