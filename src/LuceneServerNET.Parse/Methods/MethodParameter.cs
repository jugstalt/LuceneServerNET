using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods
{
    public class MethodParameter
    {
        public MethodParameter(string name, Type type)
        {
            this.Name = name;
            this.ParameterType = type;
        }

        public string Name { get;  }
        public Type ParameterType { get;  }
    }
}
