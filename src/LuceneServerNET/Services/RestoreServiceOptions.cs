using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Services
{
    public class RestoreServiceOptions
    {
        public bool RestoreOnRestart { get; set; }
        public int RestoreOnRestartCount { get; set; }
        public int RestoreOnRestartSince { get; set; }

        public bool IsRestoreDesired()
            => RestoreOnRestart == true &&
               (RestoreOnRestartCount > 0 || RestoreOnRestartSince > 0);
    }
}
