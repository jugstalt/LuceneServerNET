using System.Collections.Generic;

namespace LuceneServerNET.Core.Models.Custom
{
    /// <summary>Body of POST api/v2/indices/{id}/documents/_delete: removes all documents whose (string) field equals one of the values.</summary>
    public class DeleteByValues
    {
        public string Field { get; set; }
        public IEnumerable<string> Values { get; set; }
    }
}
