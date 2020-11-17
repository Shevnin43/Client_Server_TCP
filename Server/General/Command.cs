using System;

namespace General
{
    public class Command
    {
        public string ComKey { get; set; }
        public string Comment { get; set; }
        public Func<string,string> Function { get; set; }

        public Command(string key, string comment, Func<string, string> function)
        {
            ComKey = key;
            Comment = comment;
            Function = function;
        }
    }
}
