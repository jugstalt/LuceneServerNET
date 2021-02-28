using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    public class As : IOutFieldMethod

    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("name", typeof(string))
        };

        #region IMethod

        public string Name => "AS";

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
            if (parameters.Length != 1)
            {
                throw new Exception($"{ this.Name }: Invalid parameter count");
            }

            outFieldName = parameters[0]?.ToString();

            return instance;
        }

        #endregion
    }
}
