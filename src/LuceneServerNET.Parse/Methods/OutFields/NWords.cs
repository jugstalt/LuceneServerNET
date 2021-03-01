using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    class NWords : IOutFieldMethod
    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("n", typeof(int))
        };

        public string Name => "WORDS";

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

            int start = 0;
            for (int i = 0; i < n; i++)
            {
                var pos = newVal.IndexOf(" ", start);
                if (pos < 0)
                    return newVal;

                start = pos + 1;
            }

            return $"{ newVal.Substring(0, Math.Max(0,start - 1)) }...";
        }

        public object Invoke(object instance, object[] parameters)
        {
            string outFieldName = String.Empty;

            return Invoke(instance, parameters, ref outFieldName);
        }
    }
}
