namespace LuceneServerNET.Core.Models.Mapping
{
    public class FieldMapping
    {
        public FieldMapping()
        {
            this.Store = Store.YES;
        }
        public string FieldType { get; set; }
        public string Name { get; set; }
        public Store Store { get; set; }
    }
}
