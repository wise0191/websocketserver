using System;

namespace DrugInfoWebSocketServer
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class JsonAliasAttribute : Attribute
    {
        public string[] Aliases { get; private set; }
        public JsonAliasAttribute(params string[] aliases)
        {
            Aliases = aliases ?? new string[0];
        }
    }
}
