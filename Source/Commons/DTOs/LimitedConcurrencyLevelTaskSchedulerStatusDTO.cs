using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Commons.DTOs
{
    public class LimitedConcurrencyLevelTaskSchedulerStatusDTO
    {
        /// <summary>
        /// Number of running tasks
        /// </summary>
        public int MaxTasks { get; set; }

        /// <summary>
        /// Number of running tasks
        /// </summary>
        public int TasksRunning { get; set; }

        /// <summary>
        /// Number of pending tasks
        /// </summary>
        public int TasksPending { get; set; }
    }
}
