using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.Abstractions
{
    public interface IOutFieldMethod : IMethod
    {
        object Invoke(object instance, object[] parameters, ref string outFieldName);
    }
}
