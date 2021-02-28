using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    public class OutFieldMethods
    {
        static IEnumerable<IOutFieldMethod> _outFieldMethods =
            new IOutFieldMethod[]
            {
                new RegexReplace(),
                new As()
            };

        static public IOutFieldMethod Get(string name)
        {
            return _outFieldMethods.Where(m => m.Name.Equals(name))
                                   .FirstOrDefault();
        }
    }
}
