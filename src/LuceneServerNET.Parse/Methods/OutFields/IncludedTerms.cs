using LuceneServerNET.Parse.Extensions;
using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    class IncludedTerms : IOutFieldMethod
    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("terms", typeof(string))
        };

        public string Name => "INCL";

        public IEnumerable<MethodParameter> Parameters => _parameters;

        public Type ReturnType => typeof(string);

        public object Invoke(object instance, object[] parameters, ref string outFieldName)
        {
            if (parameters.Length != 1)
            {
                throw new Exception($"{ this.Name }: Invalid parameter count");
            }

            var instanceValue = instance.ToString() ?? String.Empty;
            var terms = parameters[0].ToString().GetTermParts();

            List<string> includedTerms = new List<string>();
            foreach (var term in terms)
            {
                if (instanceValue.IndexOf(term, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    includedTerms.Add(term);
                }
            }

            return String.Join(" ", includedTerms);
        }

        public object Invoke(object instance, object[] parameters)
        {
            string outFieldName = String.Empty;

            return Invoke(instance, parameters, ref outFieldName);
        }
    }
}
