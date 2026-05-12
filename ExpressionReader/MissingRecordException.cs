using KenshiCore.Mods;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.ExpressionReader
{
    public class MissingRecordException : Exception
    {
        public ModRecord? Record { get; }

        public MissingRecordException(
            ModRecord? record,
            string message,
            Exception innerException)
            : base(message, innerException)
        {
            Record = record;
        }
    }
}
