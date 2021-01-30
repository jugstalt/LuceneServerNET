using System;
using System.Collections.Generic;
using System.Linq;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class IndexMapping
    {
        private ICollection<FieldMapping> _fields = null;
        public ICollection<FieldMapping> Fields
        {
            get { return _fields ?? new FieldMapping[0]; }
            set { _fields = value; }
        }

        public string PrimaryField { get; set; }

        public void AddField(FieldMapping fieldMapping)
        {
            if (_fields == null)
            {
                _fields = new List<FieldMapping>();
            }

            Fields.Add(fieldMapping);

            if (String.IsNullOrEmpty(PrimaryField) && fieldMapping.Index == true)
            {
                this.PrimaryField = fieldMapping.Name;
            }
        }

        public bool IsValid()
        {
            return !String.IsNullOrWhiteSpace(PrimaryField) &&
                    this.Fields != null && this.Fields.Count() > 0;
        }
    }
}
