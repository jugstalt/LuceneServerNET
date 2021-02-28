using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    public class OutFieldMethod
    {
        public OutFieldMethod(IOutFieldMethod methodInstance, object[] parameters)
        {
            this.MethodInstance = methodInstance;
            this.Parameters = parameters;
        }

        public IOutFieldMethod MethodInstance { get; }

        public object[] Parameters { get; }
    }
}
