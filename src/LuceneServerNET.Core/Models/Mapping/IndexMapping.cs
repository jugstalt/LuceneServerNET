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

        public ICollection<string> PrimaryFields { get; set; }

        public char[] PrimaryFieldsEncodeCharacters { get; set; }

        public Phonetics.Algorithm PrimaryFieldsPhonetics { get; set; }

        public void AddField(FieldMapping fieldMapping)
        {
            if (_fields == null)
            {
                _fields = new List<FieldMapping>();
            }

            Fields.Add(fieldMapping);

            if (PrimaryFields == null && fieldMapping.Index == true)
            {
                this.PrimaryFields = new List<string>(new string[] { fieldMapping.Name });
            }
        }

        public bool IsValid()
        {
            return 
                PrimaryFields != null &&
                PrimaryFields.Count > 0 &&
                this.Fields != null && this.Fields.Count() > 0;
        }

        public FieldMapping GetField(string name) => 
            _fields == null ? null : 
            _fields.Where(f => f.Name == name).FirstOrDefault();
    }
}
