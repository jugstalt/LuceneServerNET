using LuceneServerNET.Parse.Extensions;
using LuceneServerNET.Parse.Methods.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Methods.OutFields
{
    class SentencesWith : IOutFieldMethod
    {
        private readonly IEnumerable<MethodParameter> _parameters = new MethodParameter[]
        {
            new MethodParameter("terms", typeof(string)),
            new MethodParameter("takeHits", typeof(int)),
            new MethodParameter("takeDefaults", typeof(int))
        };

        public string Name => "SENTENCES_WITH";

        public IEnumerable<MethodParameter> Parameters => _parameters;

        public Type ReturnType => typeof(string);

        public object Invoke(object instance, object[] parameters, ref string outFieldName)
        {
            if (parameters.Length == 0)
            {
                throw new Exception($"{ this.Name }: Invalid parameter count");
            }

            var terms = (parameters[0]?.ToString() ?? String.Empty).GetTermParts();
            var instanceValue = instance?.ToString() ?? String.Empty;

            int takeHits = 0;
            if(parameters.Length>1)
            {
                int.TryParse(parameters[1]?.ToString(), out takeHits);
            }
            int takeDefaults = 0;
            if (parameters.Length > 2)
            {
                int.TryParse(parameters[2]?.ToString(), out takeDefaults);
            }

            return instanceValue.GetSentence()
                                .GetTermSentencesOrDefault(terms, takeHits, takeDefaults);
        }

        public object Invoke(object instance, object[] parameters)
        {
            string outFieldName = String.Empty;

            return Invoke(instance, parameters, ref outFieldName);
        }
    }
}
