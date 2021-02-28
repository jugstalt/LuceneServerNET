using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.Abstractions
{
    public interface IMethod
    {
        string Name { get; }

        IEnumerable<MethodParameter> Parameters { get; }

        Type ReturnType { get; }

        object Invoke(object instance, object[] parameters);
    }
}
