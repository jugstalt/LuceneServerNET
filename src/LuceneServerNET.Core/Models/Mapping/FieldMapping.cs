namespace LuceneServerNET.Core.Models.Mapping
{
    public class FieldMapping
    {
        public FieldMapping()
        {
            this.Store = this.Index = true;
        }
        public string FieldType { get; set; }
        public string Name { get; set; }
        public bool Store { get; set; }
        public bool Index { get; set; }
    }
}
