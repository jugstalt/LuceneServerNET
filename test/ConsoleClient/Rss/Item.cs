using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleClient.Rss
{
    public class Item
    {
        public string Link { get; set; }
        public string Title { get; set; }
        public string Subject { get; set; }
        public string Content { get; set; }
        public DateTime? PublishDate { get; set; }
        public FeedType FeedType { get; set; }
        public bool IsFeedHeaderItem { get; set; }

        public string MediaImageSmall { get; set; }
        public string MediaImageMedium { get; set; }
        public string MediaImageWide { get; set; }
        public string MediaImageLarge { get; set; }

        public string SmallestImage
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(this.MediaImageSmall))
                    return this.MediaImageSmall;

                if (!String.IsNullOrWhiteSpace(this.MediaImageMedium))
                    return this.MediaImageMedium;

                if (!String.IsNullOrWhiteSpace(this.MediaImageWide))
                    return this.MediaImageWide;

                return this.MediaImageLarge;
            }
        }

        public string MediaCopyright { get; set; }
        public string MediaDescription { get; set; }

        public bool HasMedia
        {
            get
            {
                return !String.IsNullOrWhiteSpace(this.MediaImageSmall) ||
                       !String.IsNullOrWhiteSpace(this.MediaImageMedium) ||
                       !String.IsNullOrWhiteSpace(this.MediaImageWide) ||
                       !String.IsNullOrWhiteSpace(this.MediaImageLarge);
            }
        }
        public void RemoveMedia()
        {
            this.MediaImageLarge =
                this.MediaImageMedium =
                this.MediaImageSmall =
                this.MediaImageWide = String.Empty;
        }

        public void SetAlternativeMediaUrl(string url)
        {
            this.MediaImageSmall = url;
            this.MediaImageMedium =
                this.MediaImageWide =
                this.MediaImageLarge = String.Empty;
        }

        public string LargestMedia
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(this.MediaImageLarge))
                    return this.MediaImageLarge;
                if (!String.IsNullOrWhiteSpace(this.MediaImageWide))
                    return this.MediaImageWide;
                if (!String.IsNullOrWhiteSpace(this.MediaImageMedium))
                    return this.MediaImageMedium;

                return this.MediaImageSmall;
            }
        }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool HasGeometry
        {
            get
            {
                return this.Longitude != null && this.Latitude != null && (this.Longitude != 0D || this.Latitude != 0D);
            }
        }

        public string[] Keywords { get; set; }

        public Item()
        {
            Link = "";
            Title = "";
            Content = "";
            Subject = "";
            PublishDate = DateTime.Today;
            FeedType = FeedType.RSS;
        }

        public Item Clone()
        {
            return new Item()
            {
                Link = Link,
                Title = Title,
                Subject = Subject,
                Content=Content,
                PublishDate = PublishDate,
                FeedType = FeedType,
                IsFeedHeaderItem = IsFeedHeaderItem,
                MediaImageSmall = MediaImageSmall,
                MediaImageMedium = MediaImageMedium,
                MediaImageWide = MediaImageWide,
                MediaImageLarge = MediaImageLarge,
                Latitude = Latitude,
                Longitude = Longitude
            };
        }
    }
}
