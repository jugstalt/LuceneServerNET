using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    class RegexReplace : IOutFieldMethod
    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("pattern", typeof(string)),
            new MethodParameter("replacement", typeof(string))
        };

        #region IMethod

        public string Name => "REGEX_REPLACE";

        public IEnumerable<MethodParameter> Parameters => _parameters;

        public Type ReturnType => typeof(string);

        public object Invoke(object instance, object[] parameters)
        {
            string outFieldName = String.Empty;

            return Invoke(instance, parameters, ref outFieldName);
        }

        #endregion

        #region IOutFieldMethod

        public object Invoke(object instance, object[] parameters, ref string outFieldName)
        {
            if (parameters.Length != 2)
            {
                throw new Exception($"{ this.Name }: Invalid parameter count");
            }

            string newVal = Regex.Replace(instance.ToString() ?? String.Empty,
                                    parameters[0]?.ToString(),
                                    parameters[1]?.ToString(),
                                    RegexOptions.Multiline);

            if (newVal.Length < instance.ToString().Length)
                newVal = $"{ newVal }...";

            return newVal;
        }

        #endregion
    }
}
