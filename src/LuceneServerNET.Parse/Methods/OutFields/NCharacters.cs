using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    class NCharacters : IOutFieldMethod
    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("n", typeof(int))
        };

        public string Name => "CHARS";

        public IEnumerable<MethodParameter> Parameters => _parameters;

        public Type ReturnType => typeof(string);

        public object Invoke(object instance, object[] parameters, ref string outFieldName)
        {
            if (parameters.Length != 1)
            {
                throw new Exception($"{ this.Name }: Invalid parameter count");
            }

            int n;
            if(!int.TryParse(parameters[0]?.ToString(), out n))
            {
                throw new Exception($"{ this.Name }: Invalid number { parameters[0] }");
            }

            var newVal = (instance.ToString() ?? String.Empty);
            if (newVal.Length <= n)
                return newVal;

            return $"{ newVal.Substring(0, n) }...";
        }

        public object Invoke(object instance, object[] parameters)
        {
            string outFieldName = String.Empty;

            return Invoke(instance, parameters, ref outFieldName);
        }
    }
}
